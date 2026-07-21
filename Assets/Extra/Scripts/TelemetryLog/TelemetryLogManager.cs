using System;
using System.IO;
using UnityEngine;

namespace Extra.TelemetryLog
{
    [DisallowMultipleComponent]
    public class TelemetryLogManager : MonoBehaviour
    {
        private static TelemetryLogManager _instance;

        public static TelemetryLogManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<TelemetryLogManager>();
                }
                return _instance;
            }
        }

        [Header("Telemetry Log Configuration")]
        [SerializeField] private TelemetryLogSettings settings = new TelemetryLogSettings();

        private GeneralFeatureLogManager _generalLogger;
        private DependentFeatureLogManager _dependentLogger;
        private CsvFileWriter _csvWriter;

        private string _sessionId;
        private string _deviceType;
        private int _engineTickCounter;
        private float _timePassed;
        private float _lastSnapshotTime;
        private float _sessionStartTime;
        private bool _isLogging;

        public bool IsLogging => _isLogging;
        public TelemetryLogSettings Settings => settings;

        /// <summary>
        /// Fired on the main thread when the log file is finalized (on stop/destroy).
        /// Returns the absolute path to the saved CSV file.
        /// </summary>
        public event Action<string> OnLogFileSaved;

        private void Awake()
        {
            // Singleton enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            
            // Check if we have a parent ExtraManager (to avoid duplicate DontDestroyOnLoad if not parented)
            if (transform.parent == null && Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            // Find or create General Logger child
            _generalLogger = GetComponentInChildren<GeneralFeatureLogManager>();
            if (_generalLogger == null)
            {
                Transform genTrans = transform.Find("GeneralLogger");
                GameObject genGo;
                if (genTrans == null)
                {
                    genGo = new GameObject("GeneralLogger");
                    genGo.transform.SetParent(transform);
                }
                else
                {
                    genGo = genTrans.gameObject;
                }
                _generalLogger = genGo.AddComponent<GeneralFeatureLogManager>();
            }

            // Find or create Dependent Logger child
            _dependentLogger = GetComponentInChildren<DependentFeatureLogManager>();
            if (_dependentLogger == null)
            {
                Transform depTrans = transform.Find("DependentLogger");
                GameObject depGo;
                if (depTrans == null)
                {
                    depGo = new GameObject("DependentLogger");
                    depGo.transform.SetParent(transform);
                }
                else
                {
                    depGo = depTrans.gameObject;
                }
                _dependentLogger = depGo.AddComponent<DependentFeatureLogManager>();
            }

            _csvWriter = new CsvFileWriter();

            // Validate settings
            if (settings != null)
            {
                settings.Validate();
            }
        }

        private void Start()
        {
            if (settings != null && settings.AutoStartOnAwake)
            {
                StartLogging();
            }
        }

        /// <summary>
        /// Generates a session ID, opens the CSV file, writes the master header, and starts the sampling loop.
        /// </summary>
        public void StartLogging()
        {
            if (_isLogging) return;

            settings.Validate();

            // 1. Generate session ID and device type
            _sessionId = Guid.NewGuid().ToString();
            _deviceType = CsvFileWriter.SanitizeCsvValue(SystemInfo.deviceModel);

            // 2. Resolve output folder and file path
            string folderPath = settings.GetOutputPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{settings.FileNamePrefix}{timestamp}.csv";
            string filePath = Path.Combine(folderPath, fileName);

            if (settings.EnableDebugLogs)
            {
                Debug.Log($"[TelemetryLogManager] Starting session {_sessionId} logging to: {filePath}");
            }

            // 3. Construct master header
            string[] controlHeaders = new string[]
            {
                "[control]timestamp_unix",
                "[control]session_id",
                "[string]device_type",
                "[control]engine_tick",
                "time_passed"
            };

            string[] generalHeaders = _generalLogger.GetHeaders();
            string[] dependentHeaders = _dependentLogger.GetHeaders();

            int totalLength = controlHeaders.Length + generalHeaders.Length + dependentHeaders.Length;
            string[] masterHeaders = new string[totalLength];

            Array.Copy(controlHeaders, 0, masterHeaders, 0, controlHeaders.Length);
            Array.Copy(generalHeaders, 0, masterHeaders, controlHeaders.Length, generalHeaders.Length);
            Array.Copy(dependentHeaders, 0, masterHeaders, controlHeaders.Length + generalHeaders.Length, dependentHeaders.Length);

            // 4. Open writer and write headers
            _csvWriter.Open(filePath, masterHeaders);

            if (_csvWriter.IsOpen)
            {
                _isLogging = true;
                _sessionStartTime = Time.unscaledTime;
                _lastSnapshotTime = Time.unscaledTime;
                _engineTickCounter = 0;
            }
            else
            {
                Debug.LogError("[TelemetryLogManager] Failed to start logging: CSV file writer could not be opened.");
            }
        }

        /// <summary>
        /// Flushes the active CSV buffer, closes the file, and resets logging state.
        /// </summary>
        public void StopLogging()
        {
            if (!_isLogging) return;

            _isLogging = false;

            string savedPath = _csvWriter.FilePath;

            if (settings.EnableDebugLogs)
            {
                Debug.Log($"[TelemetryLogManager] Stopping telemetry session. Saving finalized file to: {savedPath}");
            }

            _csvWriter.Close();

            if (!string.IsNullOrEmpty(savedPath))
            {
                try
                {
                    OnLogFileSaved?.Invoke(savedPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TelemetryLogManager] Error in OnLogFileSaved subscriber: {ex.Message}");
                }
            }

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        private void Update()
        {
            if (!_isLogging) return;

            // Frame-level collection
            _engineTickCounter++;
            
            if (_generalLogger != null)
            {
                _generalLogger.CollectFrameData();
            }
            if (_dependentLogger != null)
            {
                _dependentLogger.CollectFrameData();
            }

            // Snapshot timer check
            float elapsed = Time.unscaledTime - _lastSnapshotTime;
            if (elapsed >= settings.SamplingIntervalSeconds)
            {
                TakeSnapshot();
            }
        }

        /// <summary>
        /// Captures timestamps, aggregates sub-logger snapshots, and writes row to CSV.
        /// </summary>
        private void TakeSnapshot()
        {
            _timePassed = Time.unscaledTime - _sessionStartTime;
            _lastSnapshotTime = Time.unscaledTime;

            long timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Construct control values
            string[] controlValues = new string[]
            {
                timestampUnix.ToString(),
                _sessionId,
                _deviceType,
                _engineTickCounter.ToString(),
                _timePassed.ToString("F3")
            };

            // Get snapshots
            string[] generalValues = _generalLogger != null ? _generalLogger.FlushAndGetSnapshot() : Array.Empty<string>();
            string[] dependentValues = _dependentLogger != null ? _dependentLogger.FlushAndGetSnapshot() : Array.Empty<string>();

            // Combine arrays
            int totalLength = controlValues.Length + generalValues.Length + dependentValues.Length;
            string[] fullRow = new string[totalLength];

            Array.Copy(controlValues, 0, fullRow, 0, controlValues.Length);
            Array.Copy(generalValues, 0, fullRow, controlValues.Length, generalValues.Length);
            Array.Copy(dependentValues, 0, fullRow, controlValues.Length + generalValues.Length, dependentValues.Length);

            // Write row
            _csvWriter.AppendRow(fullRow);

            // Reset frame counter for next window
            _engineTickCounter = 0;

            // Flush if buffer is full
            if (_csvWriter.BufferedRowCount >= settings.MaxBufferedRowsBeforeFlush)
            {
                _csvWriter.Flush();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // App paused: flush memory buffer to disk to prevent loss
                if (_isLogging && _csvWriter != null && _csvWriter.IsOpen)
                {
                    if (settings.EnableDebugLogs)
                    {
                        Debug.Log("[TelemetryLogManager] App paused. Flushing buffered rows to disk.");
                    }
                    _csvWriter.Flush();
                }
            }
        }

        private void OnApplicationQuit()
        {
            StopLogging();
        }

        private void OnDestroy()
        {
            StopLogging();

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
