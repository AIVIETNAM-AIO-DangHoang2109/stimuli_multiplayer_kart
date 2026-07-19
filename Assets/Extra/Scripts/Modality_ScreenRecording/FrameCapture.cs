using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Extra.ScreenRecording
{
    public class FrameCapture : IDisposable
    {
        private readonly ScreenRecordingSettings _settings;
        private readonly MonoBehaviour _runner;
        private readonly int _width;
        private readonly int _height;
        private readonly float _captureInterval;

        private RenderTexture _captureRT;
        private Coroutine _captureCoroutine;
        private Texture2D _fallbackTex;

        private float _lastCaptureTime;
        private int _frameIndex;
        private bool _isCapturing;
        private bool _useAsyncGPUReadback;

        public bool IsCapturing => _isCapturing;

        /// <summary>
        /// Event fired when a frame is captured.
        /// Parameters: Raw pixels (RGB24), timestamp, frame index.
        /// </summary>
        public event Action<NativeArray<byte>, float, int> OnFrameCaptured;

        public FrameCapture(ScreenRecordingSettings settings, MonoBehaviour runner)
        {
            _settings = settings;
            _runner = runner;
            _width = settings.CaptureWidth;
            _height = settings.CaptureHeight;
            _captureInterval = 1f / settings.TargetFrameRate;
            _useAsyncGPUReadback = SystemInfo.supportsAsyncGPUReadback;
        }

        public void Initialize(Camera camera = null)
        {
            _captureRT = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
            _captureRT.antiAliasing = 1;
            _captureRT.filterMode = FilterMode.Bilinear;
            _captureRT.Create();

            if (_settings.EnableDebugLogs)
            {
                Debug.Log($"[FrameCapture] Initialized with resolution {_width}x{_height}, target FPS: {_settings.TargetFrameRate}, AsyncGPUReadback supported: {_useAsyncGPUReadback}");
            }
        }

        public void StartCapture()
        {
            if (_isCapturing) return;

            _isCapturing = true;
            _lastCaptureTime = 0f;
            _frameIndex = 0;
            _captureCoroutine = _runner.StartCoroutine(CaptureLoop());

            if (_settings.EnableDebugLogs)
            {
                Debug.Log("[FrameCapture] Capture loop started.");
            }
        }

        public void StopCapture()
        {
            if (!_isCapturing) return;

            _isCapturing = false;
            if (_captureCoroutine != null)
            {
                _runner.StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }

            if (_settings.EnableDebugLogs)
            {
                Debug.Log("[FrameCapture] Capture loop stopped.");
            }
        }

        private IEnumerator CaptureLoop()
        {
            var waitForEndOfFrame = new WaitForEndOfFrame();

            while (_isCapturing)
            {
                yield return waitForEndOfFrame;

                float currentTime = Time.realtimeSinceStartup;
                if (currentTime - _lastCaptureTime < _captureInterval)
                    continue;

                _lastCaptureTime = currentTime;

                // Capture the screen directly into the RenderTexture
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);

                int currentFrameIndex = _frameIndex++;
                float timestamp = currentTime;

                if (_useAsyncGPUReadback)
                {
                    AsyncGPUReadback.Request(_captureRT, 0, TextureFormat.RGB24, (AsyncGPUReadbackRequest request) =>
                    {
                        if (!_isCapturing) return;

                        if (request.hasError)
                        {
                            if (_settings.EnableDebugLogs)
                            {
                                Debug.LogWarning("[FrameCapture] AsyncGPUReadback request failed. Falling back to synchronous ReadPixels.");
                            }
                            // Fallback on next frame by disabling async readback
                            _useAsyncGPUReadback = false;
                            return;
                        }

                        var data = request.GetData<byte>();
                        OnFrameCaptured?.Invoke(data, timestamp, currentFrameIndex);
                    });
                }
                else
                {
                    // Synchronous ReadPixels fallback
                    RenderTexture activeRT = RenderTexture.active;
                    RenderTexture.active = _captureRT;

                    if (_fallbackTex == null)
                    {
                        _fallbackTex = new Texture2D(_width, _height, TextureFormat.RGB24, false);
                    }

                    _fallbackTex.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                    _fallbackTex.Apply();

                    RenderTexture.active = activeRT;

                    var data = _fallbackTex.GetRawTextureData<byte>();
                    OnFrameCaptured?.Invoke(data, timestamp, currentFrameIndex);
                }
            }
        }

        public void Dispose()
        {
            StopCapture();

            if (_captureRT != null)
            {
                _captureRT.Release();
                UnityEngine.Object.Destroy(_captureRT);
                _captureRT = null;
            }

            if (_fallbackTex != null)
            {
                UnityEngine.Object.Destroy(_fallbackTex);
                _fallbackTex = null;
            }

            OnFrameCaptured = null;
        }
    }
}
