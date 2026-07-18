using System;
using System.Collections.Concurrent;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Extra.ScreenRecording
{
    public class ScreenRecordingManager : MonoBehaviour
    {
        private static ScreenRecordingManager _instance;

        public static ScreenRecordingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ScreenRecordingManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("ScreenRecordingManager");
                        _instance = go.AddComponent<ScreenRecordingManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Screen Recording Settings")]
        [SerializeField] private ScreenRecordingSettings settings = new ScreenRecordingSettings();

        private FrameCapture _frameCapture;
        private AudioCapture _audioCapture;
        private VideoSegmenter _segmenter;
        private Texture2D _reusableTex;

        private bool _isRecording;
        private bool _wasRecordingBeforePause;
        private int _activeEncoderCount;

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        public bool IsRecording => _isRecording;
        public ScreenRecordingSettings Settings => settings;

        /// <summary>
        /// Fired on the main thread when a segment has been saved to disk.
        /// Returns the absolute path to the saved segment folder.
        /// </summary>
        public event Action<string> OnSegmentSaved;

        private void Awake()
        {
            // Singleton enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (settings.AutoStartOnAwake)
            {
                StartRecording();
            }
        }

        public void StartRecording()
        {
            if (_isRecording) return;

            settings.Validate();

            // Create output directory in editor if it doesn't exist
#if UNITY_EDITOR
            string outPath = settings.GetOutputPath();
            if (!Directory.Exists(outPath))
            {
                Directory.CreateDirectory(outPath);
            }
#endif

            // Initialize Segmenter
            _segmenter = new VideoSegmenter(settings);
            _segmenter.OnSegmentReady += OnSegmentReady;
            _segmenter.Start();

            // Initialize Reusable Texture
            _reusableTex = new Texture2D(settings.CaptureWidth, settings.CaptureHeight, TextureFormat.RGB24, false);

            // Initialize Frame Capture
            _frameCapture = new FrameCapture(settings, this);
            _frameCapture.OnFrameCaptured += OnFrameCaptured;
            _frameCapture.Initialize();
            _frameCapture.StartCapture();

            // Initialize Audio Capture if enabled
            if (settings.CaptureAudio)
            {
                InitializeAudioCapture();
            }

            _isRecording = true;
            _wasRecordingBeforePause = false;

            if (settings.EnableDebugLogs)
            {
                Debug.Log("[ScreenRecordingManager] Recording started.");
            }
        }

        public void StopRecording()
        {
            if (!_isRecording) return;

            _isRecording = false;

            // Stop frame capture
            if (_frameCapture != null)
            {
                _frameCapture.StopCapture();
                _frameCapture.OnFrameCaptured -= OnFrameCaptured;
                _frameCapture.Dispose();
                _frameCapture = null;
            }

            // Stop audio capture
            if (_audioCapture != null)
            {
                _audioCapture.StopCapture();
                _audioCapture = null;
            }

            // Flush remaining data in the segmenter
            if (_segmenter != null)
            {
                _segmenter.FlushRemaining();
                _segmenter.OnSegmentReady -= OnSegmentReady;
                _segmenter = null;
            }

            // Destroy reusable texture
            if (_reusableTex != null)
            {
                Destroy(_reusableTex);
                _reusableTex = null;
            }

            if (settings.EnableDebugLogs)
            {
                Debug.Log("[ScreenRecordingManager] Recording stopped.");
            }
        }

        private void InitializeAudioCapture()
        {
            // Find existing AudioListener in the scene
            AudioListener listener = FindObjectOfType<AudioListener>();
            if (listener == null)
            {
                // Fallback: attach one to the manager itself
                if (settings.EnableDebugLogs)
                {
                    Debug.LogWarning("[ScreenRecordingManager] No AudioListener found in the scene. Attaching to manager GameObject.");
                }
                listener = gameObject.GetComponent<AudioListener>();
                if (listener == null)
                {
                    listener = gameObject.AddComponent<AudioListener>();
                }
            }

            // Attach AudioCapture to the AudioListener's GameObject
            _audioCapture = listener.gameObject.GetComponent<AudioCapture>();
            if (_audioCapture == null)
            {
                _audioCapture = listener.gameObject.AddComponent<AudioCapture>();
            }

            _audioCapture.Initialize(settings);
            _audioCapture.StartCapture();
        }

        private void OnFrameCaptured(NativeArray<byte> rawPixels, float timestamp, int frameIndex)
        {
            if (!_isRecording || _reusableTex == null) return;

            // Check if size matches
            int expectedLength = settings.CaptureWidth * settings.CaptureHeight * 3; // RGB24
            if (rawPixels.Length != expectedLength)
            {
                if (settings.EnableDebugLogs)
                {
                    Debug.LogError($"[ScreenRecordingManager] Raw pixels size mismatch. Expected {expectedLength}, got {rawPixels.Length}");
                }
                return;
            }

            // Load raw data and apply
            _reusableTex.LoadRawTextureData(rawPixels);
            _reusableTex.Apply();

            // Encode to JPG/PNG on the main thread
            byte[] encodedBytes = settings.ImageFormat == ImageFormat.JPG
                ? _reusableTex.EncodeToJPG(settings.JpgQuality)
                : _reusableTex.EncodeToPNG();

            var frame = new FrameData
            {
                EncodedBytes = encodedBytes,
                Timestamp = timestamp,
                FrameIndex = frameIndex
            };

            // Feed to the segmenter
            _segmenter.FeedFrame(frame);
        }

        private void LateUpdate()
        {
            if (!_isRecording) return;

            // Process actions dispatched from background threads
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                action?.Invoke();
            }

            // Feed accumulated audio to the segmenter
            if (settings.CaptureAudio)
            {
                // Re-initialize audio capture if it was lost (e.g. scene transition destroyed the old camera/listener)
                if (_audioCapture == null || !_audioCapture.gameObject)
                {
                    InitializeAudioCapture();
                }

                if (_audioCapture != null && _audioCapture.IsCapturing)
                {
                    float[] audioSamples = _audioCapture.ReadAvailableSamples();
                    if (audioSamples.Length > 0)
                    {
                        _segmenter.FeedAudio(audioSamples, audioSamples.Length);
                    }
                }
            }
        }

        private void OnSegmentReady(SegmentData segmentData)
        {
            if (_activeEncoderCount >= settings.MaxConcurrentEncoders)
            {
                Debug.LogWarning($"[ScreenRecordingManager] Max concurrent encoders ({settings.MaxConcurrentEncoders}) reached. Dropping segment {segmentData.SegmentIndex} to prevent memory exhaustion.");
                return;
            }

            System.Threading.Interlocked.Increment(ref _activeEncoderCount);
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string segmentName = $"{settings.FileNamePrefix}{timestamp}_{segmentData.SegmentIndex:D3}";
            string outFolder = settings.GetOutputPath();

            // Dispatch saving to a background thread
            VideoEncoder.SaveSegmentAsync(segmentData, outFolder, segmentName, settings).ContinueWith(t =>
            {
                System.Threading.Interlocked.Decrement(ref _activeEncoderCount);
                
                string savedPath = Path.Combine(outFolder, segmentName);
                
                // Dispatch completion back to main thread
                _mainThreadQueue.Enqueue(() =>
                {
                    OnSegmentSaved?.Invoke(savedPath);

                    if (settings.EnableDebugLogs)
                    {
                        Debug.Log($"[ScreenRecordingManager] Segment {segmentData.SegmentIndex} saved successfully: {savedPath}");
                    }

#if UNITY_EDITOR
                    UnityEditor.AssetDatabase.Refresh();
#endif
                });
            });
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (_isRecording)
                {
                    _wasRecordingBeforePause = true;
                    StopRecording();
                }
            }
            else
            {
                if (_wasRecordingBeforePause)
                {
                    _wasRecordingBeforePause = false;
                    StartRecording();
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

            // Wait briefly for background save tasks to complete (max 2 seconds)
            float startWait = Time.realtimeSinceStartup;
            while (_activeEncoderCount > 0 && Time.realtimeSinceStartup - startWait < 2f)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }
}
