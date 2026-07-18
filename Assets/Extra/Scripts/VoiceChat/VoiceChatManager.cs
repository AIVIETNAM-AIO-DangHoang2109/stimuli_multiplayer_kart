using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Extra.VoiceChat
{
    [DisallowMultipleComponent]
    public class VoiceChatManager : MonoBehaviour
    {
        private static VoiceChatManager _instance;

        public static VoiceChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<VoiceChatManager>();
                }
                return _instance;
            }
        }

        [Header("Voice Chat Configuration")]
        [SerializeField] private VoiceChatSettings settings = new VoiceChatSettings();

        private MicrophoneCapture _capture;
        private AudioSegmenter _segmenter;
        private bool _isRecording;
        private bool _needsRefresh;
        private readonly object _lock = new object();

        private readonly ConcurrentQueue<string> _savedFilesQueue = new ConcurrentQueue<string>();
        private readonly List<Task> _activeSaveTasks = new List<Task>();

        public bool IsRecording => _isRecording;
        public VoiceChatSettings Settings => settings;

        /// <summary>
        /// Event fired on the main thread when a segment has been successfully written to disk.
        /// Returns the absolute path to the saved WAV file.
        /// </summary>
        public event Action<string> OnSegmentSaved;

        private void Awake()
        {
            // Singleton Enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Initialize components
            _capture = new MicrophoneCapture();

            if (settings.AutoStartOnAwake)
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Starts capturing and segmenting voice chat.
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording) return;

            string[] devices = _capture.GetAvailableDevices();
            if (devices.Length == 0)
            {
                Debug.LogError("[VoiceChatManager] Cannot start recording: No microphone devices detected.");
                return;
            }

            if (settings.EnableDebugLogs)
            {
                Debug.Log("[VoiceChatManager] Starting voice chat recording...");
            }

            // Create segmenter (sampleRate * segmentDuration * channelCount)
            _segmenter = new AudioSegmenter(settings.SampleRate, settings.SegmentDurationSeconds, settings.ChannelCount);
            _segmenter.OnSegmentReady += OnSegmentReady;

            // Start hardware capture
            _capture.StartCapture(settings.MicrophoneDeviceName, settings.SampleRate, settings.MaxMicBufferLengthSeconds, settings.ChannelCount);

            if (_capture.IsCapturing)
            {
                _isRecording = true;
            }
            else
            {
                Debug.LogError("[VoiceChatManager] Failed to start microphone capture.");
            }
        }

        /// <summary>
        /// Stops capture, flushes remaining samples to disk, and cleans up recording state.
        /// </summary>
        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

            if (settings.EnableDebugLogs)
            {
                Debug.Log("[VoiceChatManager] Stopping recording and flushing remaining samples...");
            }

            if (_capture != null)
            {
                _capture.StopCapture();
            }

            if (_segmenter != null)
            {
                _segmenter.FlushRemaining();
                _segmenter.OnSegmentReady -= OnSegmentReady;
                _segmenter = null;
            }
        }

        private void Update()
        {
            // Read hardware samples and feed the segmenter
            if (_isRecording && _capture != null && _capture.IsCapturing)
            {
                float[] samples = _capture.ReadAvailableSamples();
                if (samples != null && samples.Length > 0 && _segmenter != null)
                {
                    _segmenter.FeedSamples(samples, samples.Length);
                }
            }

            // Dispatch OnSegmentSaved events on the main thread
            while (_savedFilesQueue.TryDequeue(out string savedPath))
            {
                try
                {
                    OnSegmentSaved?.Invoke(savedPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VoiceChatManager] Error in OnSegmentSaved subscriber: {ex.Message}");
                }
            }

            // Check if we need to refresh the Editor AssetDatabase
            bool refreshNow = false;
            lock (_lock)
            {
                if (_needsRefresh)
                {
                    _needsRefresh = false;
                    refreshNow = true;
                }
            }

            if (refreshNow)
            {
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
                if (settings.EnableDebugLogs)
                {
                    Debug.Log("[VoiceChatManager] AssetDatabase refreshed successfully.");
                }
#endif
            }
        }

        private void OnSegmentReady(float[] buffer, int sampleCount, int segmentIndex)
        {
            // Create a copy of the buffer data because the Segmenter will reuse it
            float[] bufferCopy = new float[sampleCount];
            Array.Copy(buffer, 0, bufferCopy, 0, sampleCount);

            string folderPath = GetAbsoluteFolderPath();
            string fileName = GenerateFileName(segmentIndex);
            string filePath = Path.Combine(folderPath, fileName);

            if (settings.EnableDebugLogs)
            {
                Debug.Log($"[VoiceChatManager] Segment {segmentIndex} ready. Saving {sampleCount} samples to {filePath} asynchronously...");
            }

            // Run the WAV writing on a background thread
            Task saveTask = WavFileWriter.SaveAsync(filePath, bufferCopy, sampleCount, settings.SampleRate, settings.ChannelCount);

            lock (_activeSaveTasks)
            {
                _activeSaveTasks.Add(saveTask);
            }

            saveTask.ContinueWith(t =>
            {
                lock (_activeSaveTasks)
                {
                    _activeSaveTasks.Remove(saveTask);
                }

                if (t.IsFaulted)
                {
                    Debug.LogError($"[VoiceChatManager] Failed to write segment {segmentIndex} to {filePath}: {t.Exception?.InnerException?.Message}");
                }
                else
                {
                    _savedFilesQueue.Enqueue(filePath);
                    lock (_lock)
                    {
                        _needsRefresh = true;
                    }
                }
            });
        }

        private string GenerateFileName(int segmentIndex)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{settings.FileNamePrefix}{timestamp}_{segmentIndex:D3}.wav";
        }

        private string GetAbsoluteFolderPath()
        {
            string folder = settings.OutputFolderPath;
            if (string.IsNullOrEmpty(folder))
            {
                folder = "Assets/Extra/Resources/VoiceChat";
            }

            if (folder.StartsWith("Assets"))
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    return Path.Combine(projectRoot, folder);
                }
            }

            if (Path.IsPathRooted(folder))
            {
                return folder;
            }

            return Path.Combine(Application.persistentDataPath, folder);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // Application paused: flush segmenter, stop microphone capture
                if (_isRecording)
                {
                    if (settings.EnableDebugLogs)
                    {
                        Debug.Log("[VoiceChatManager] App paused. Flushing and suspending capture.");
                    }
                    if (_segmenter != null)
                    {
                        _segmenter.FlushRemaining();
                    }
                    if (_capture != null)
                    {
                        _capture.StopCapture();
                    }
                }
            }
            else
            {
                // Application resumed: restart microphone capture if it was recording
                if (_isRecording && _capture != null && !_capture.IsCapturing)
                {
                    if (settings.EnableDebugLogs)
                    {
                        Debug.Log("[VoiceChatManager] App resumed. Restarting capture.");
                    }
                    _capture.StartCapture(settings.MicrophoneDeviceName, settings.SampleRate, settings.MaxMicBufferLengthSeconds, settings.ChannelCount);
                }
            }
        }

        private void OnApplicationQuit()
        {
            StopRecording();
        }

        private void OnDestroy()
        {
            StopRecording();

            // Wait for any pending saves to finish (up to 2 seconds)
            Task[] tasksToWait;
            lock (_activeSaveTasks)
            {
                tasksToWait = _activeSaveTasks.ToArray();
            }

            if (tasksToWait.Length > 0)
            {
                if (settings.EnableDebugLogs)
                {
                    Debug.Log($"[VoiceChatManager] Waiting for {tasksToWait.Length} pending file saves to complete on destroy...");
                }
                Task.WaitAll(tasksToWait, 2000);
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
