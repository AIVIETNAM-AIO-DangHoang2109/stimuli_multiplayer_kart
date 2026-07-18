using System;
using UnityEngine;

namespace Extra.VoiceChat
{
    public class AudioSegmenter
    {
        private float[] _segmentBuffer;
        private int _segmentWritePos;
        private int _segmentIndex;

        /// <summary>
        /// Callback fired when a segment is ready to be written.
        /// Arguments: (float[] buffer, int sampleCount, int segmentIndex)
        /// </summary>
        public event Action<float[], int, int> OnSegmentReady;

        /// <summary>
        /// Index of the next segment to be recorded.
        /// </summary>
        public int SegmentIndex => _segmentIndex;

        /// <summary>
        /// Sized in total number of float elements (sampleRate * durationSeconds * channelCount).
        /// </summary>
        public AudioSegmenter(int sampleRate, float durationSeconds, int channelCount)
        {
            int totalSamples = Mathf.RoundToInt(sampleRate * durationSeconds * channelCount);
            if (totalSamples <= 0)
            {
                throw new ArgumentException("Segment duration and sample rate must yield a positive buffer size.");
            }

            _segmentBuffer = new float[totalSamples];
            Reset();
        }

        /// <summary>
        /// Feeds incoming audio samples into the segment buffer, invoking OnSegmentReady whenever the buffer fills.
        /// </summary>
        public void FeedSamples(float[] samples, int count)
        {
            if (samples == null || count <= 0) return;

            int srcOffset = 0;
            int samplesLeft = count;

            while (samplesLeft > 0)
            {
                int space = _segmentBuffer.Length - _segmentWritePos;
                int toCopy = Math.Min(samplesLeft, space);

                Array.Copy(samples, srcOffset, _segmentBuffer, _segmentWritePos, toCopy);

                _segmentWritePos += toCopy;
                srcOffset += toCopy;
                samplesLeft -= toCopy;

                if (_segmentWritePos >= _segmentBuffer.Length)
                {
                    OnSegmentReady?.Invoke(_segmentBuffer, _segmentBuffer.Length, _segmentIndex);
                    _segmentIndex++;
                    _segmentWritePos = 0;
                }
            }
        }

        /// <summary>
        /// Forces the release of the remaining samples in the active segment, even if it is not full.
        /// </summary>
        public void FlushRemaining()
        {
            if (_segmentWritePos > 0)
            {
                OnSegmentReady?.Invoke(_segmentBuffer, _segmentWritePos, _segmentIndex);
                _segmentIndex++;
                _segmentWritePos = 0;
            }
        }

        /// <summary>
        /// Clears the segmenter state, resetting counters and buffers.
        /// </summary>
        public void Reset()
        {
            _segmentWritePos = 0;
            _segmentIndex = 0;
            Array.Clear(_segmentBuffer, 0, _segmentBuffer.Length);
        }
    }
}
