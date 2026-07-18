using UnityEngine;
using System;

namespace Extra.ScreenRecording
{
    public class AudioCapture : MonoBehaviour
    {
        private ScreenRecordingSettings _settings;
        private RingBuffer<float> _ringBuffer;
        private bool _isCapturing;
        private int _channels;

        public bool IsCapturing => _isCapturing;

        /// <summary>
        /// Initializes the audio capture with configuration settings.
        /// </summary>
        public void Initialize(ScreenRecordingSettings settings)
        {
            _settings = settings;
            _channels = settings.AudioChannels;
            
            // Size the ring buffer for ~5 seconds of audio headroom
            int bufferSize = settings.AudioSampleRate * _channels * 5;
            _ringBuffer = new RingBuffer<float>(bufferSize);

            if (_settings.EnableDebugLogs)
            {
                Debug.Log($"[AudioCapture] Initialized with SampleRate: {settings.AudioSampleRate}, Channels: {_channels}, RingBufferSize: {bufferSize}");
            }
        }

        public void StartCapture()
        {
            if (_isCapturing) return;

            if (_ringBuffer != null)
            {
                _ringBuffer.Clear();
            }

            _isCapturing = true;

            if (_settings != null && _settings.EnableDebugLogs)
            {
                Debug.Log("[AudioCapture] Audio capture started.");
            }
        }

        public void StopCapture()
        {
            if (!_isCapturing) return;

            _isCapturing = false;

            if (_settings != null && _settings.EnableDebugLogs)
            {
                Debug.Log("[AudioCapture] Audio capture stopped.");
            }
        }

        /// <summary>
        /// Reads all available samples from the ring buffer. Call this from the main thread.
        /// </summary>
        public float[] ReadAvailableSamples()
        {
            if (_ringBuffer == null) return Array.Empty<float>();

            int available = _ringBuffer.AvailableRead;
            if (available <= 0) return Array.Empty<float>();

            float[] samples = new float[available];
            _ringBuffer.Read(samples, 0, available);
            return samples;
        }

        /// <summary>
        /// Unity Audio Callback. Runs on the audio thread.
        /// Copies PCM data to the ring buffer. Must NOT perform any allocations.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_isCapturing || _ringBuffer == null) return;

            // Copy to ring buffer (zero allocation)
            _ringBuffer.Write(data, 0, data.Length);
        }
    }
}
