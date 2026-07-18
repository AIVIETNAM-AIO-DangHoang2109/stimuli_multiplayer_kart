using System;
using System.Collections.Generic;
using UnityEngine;

namespace Extra.ScreenRecording
{
    public class SegmentData
    {
        public FrameData[] Frames;
        public float[] AudioSamples;
        public int AudioSampleCount;
        public int SegmentIndex;
        public float Duration;          // Actual duration of this segment in seconds
        public int FrameRate;
        public int AudioSampleRate;
        public int AudioChannels;
        public int Width;
        public int Height;
    }

    public class VideoSegmenter
    {
        private readonly ScreenRecordingSettings _settings;
        private readonly List<FrameData> _frameBuffer;
        private readonly List<float> _audioBuffer;

        private float _segmentStartTime;
        private int _segmentIndex;
        private float _segmentDuration;
        private bool _isRecording;

        public event Action<SegmentData> OnSegmentReady;

        public VideoSegmenter(ScreenRecordingSettings settings)
        {
            _settings = settings;
            _segmentDuration = settings.SegmentDurationSeconds;

            // Pre-size buffers to avoid allocations/resizing during recording
            int expectedFrames = Mathf.RoundToInt(settings.TargetFrameRate * _segmentDuration);
            _frameBuffer = new List<FrameData>(expectedFrames);

            if (settings.CaptureAudio)
            {
                int expectedSamples = Mathf.RoundToInt(settings.AudioSampleRate * settings.AudioChannels * _segmentDuration);
                _audioBuffer = new List<float>(expectedSamples);
            }
            else
            {
                _audioBuffer = new List<float>(0);
            }
        }

        public void Start()
        {
            Reset();
            _isRecording = true;
            _segmentStartTime = Time.realtimeSinceStartup;
        }

        public void FeedFrame(FrameData frame)
        {
            if (!_isRecording) return;

            _frameBuffer.Add(frame);

            // Check if segment duration is reached
            float elapsed = Time.realtimeSinceStartup - _segmentStartTime;
            if (elapsed >= _segmentDuration)
            {
                TriggerSegmentReady(elapsed);
            }
        }

        public void FeedAudio(float[] samples, int count)
        {
            if (!_isRecording || !_settings.CaptureAudio || samples == null || count <= 0) return;

            // Appending only the valid count of samples
            for (int i = 0; i < count; i++)
            {
                _audioBuffer.Add(samples[i]);
            }
        }

        public void FlushRemaining()
        {
            if (!_isRecording) return;

            float elapsed = Time.realtimeSinceStartup - _segmentStartTime;
            if (_frameBuffer.Count > 0 || _audioBuffer.Count > 0)
            {
                TriggerSegmentReady(elapsed);
            }
            else
            {
                Reset();
            }
        }

        public void Reset()
        {
            _frameBuffer.Clear();
            _audioBuffer.Clear();
            _segmentStartTime = Time.realtimeSinceStartup;
            _segmentIndex = 1;
            _isRecording = false;
        }

        private void TriggerSegmentReady(float elapsed)
        {
            if (_settings.EnableDebugLogs)
            {
                Debug.Log($"[VideoSegmenter] Segment {_segmentIndex} ready. Frames: {_frameBuffer.Count}, Audio samples: {_audioBuffer.Count}, Duration: {elapsed:F2}s");
            }

            var segmentData = new SegmentData
            {
                Frames = _frameBuffer.ToArray(),
                AudioSamples = _audioBuffer.ToArray(),
                AudioSampleCount = _audioBuffer.Count,
                SegmentIndex = _segmentIndex++,
                Duration = elapsed,
                FrameRate = _settings.TargetFrameRate,
                AudioSampleRate = _settings.AudioSampleRate,
                AudioChannels = _settings.AudioChannels,
                Width = _settings.CaptureWidth,
                Height = _settings.CaptureHeight
            };

            // Reset buffers and start timestamp for next segment
            _frameBuffer.Clear();
            _audioBuffer.Clear();
            _segmentStartTime = Time.realtimeSinceStartup;

            OnSegmentReady?.Invoke(segmentData);
        }
    }
}
