using System;
using UnityEngine;

namespace Extra.VoiceChat
{
    public class MicrophoneCapture
    {
        private AudioClip _micClip;
        private string _deviceName;
        private int _lastReadPosition;
        private int _micChannels;
        private int _targetChannels;
        private bool _isCapturing;

        public bool IsCapturing => _isCapturing;

        /// <summary>
        /// Returns the name of the device being used, or null/empty if default.
        /// </summary>
        public string DeviceName => _deviceName;

        /// <summary>
        /// Lists all available microphone devices on the system.
        /// </summary>
        public string[] GetAvailableDevices()
        {
            return Microphone.devices;
        }

        /// <summary>
        /// Starts continuous microphone recording.
        /// </summary>
        /// <param name="device">Microphone device name.</param>
        /// <param name="sampleRate">Recording sample rate.</param>
        /// <param name="bufferLengthSec">Length of recording buffer.</param>
        /// <param name="targetChannels">Desired output channel count (e.g. 1 for Mono, 2 for Stereo).</param>
        public void StartCapture(string device, int sampleRate, int bufferLengthSec, int targetChannels = 1)
        {
            if (_isCapturing)
            {
                StopCapture();
            }

            _deviceName = string.IsNullOrEmpty(device) ? null : device;
            if (string.IsNullOrEmpty(_deviceName))
            {
                if (Microphone.devices.Length > 0)
                    _deviceName = Microphone.devices[0];
                else
                    Debug.LogError("[MicrophoneCapture] No microphone devices detected on this system.");
            }
            _targetChannels = targetChannels;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[MicrophoneCapture] No microphone devices detected on this system.");
                return;
            }

            _micClip = Microphone.Start(_deviceName, true, bufferLengthSec, sampleRate);
            if (_micClip == null)
            {
                Debug.LogError($"[MicrophoneCapture] Failed to start microphone recording on device: {device ?? "Default"}");
                return;
            }

            _lastReadPosition = 0;
            _micChannels = _micClip.channels;
            _isCapturing = true;

            Debug.Log($"[MicrophoneCapture] Started capture on device '{_deviceName ?? "Default"}' (Hardware Channels: {_micChannels}, Target Channels: {_targetChannels}, Sample Rate: {sampleRate}Hz, Buffer: {bufferLengthSec}s)");
        }

        /// <summary>
        /// Reads new samples since the last read. Handles wrap-around of the looping clip and
        /// automatically converts hardware channels to the requested target channel count.
        /// Returns null or an empty array if no new samples are available.
        /// </summary>
        public float[] ReadAvailableSamples()
        {
            if (!_isCapturing || _micClip == null)
            {
                return null;
            }

            int currentPosition = Microphone.GetPosition(_deviceName);
            if (currentPosition < 0 || currentPosition == _lastReadPosition)
            {
                return null;
            }

            int clipSamples = _micClip.samples;

            // Calculate how many samples (per channel) have been written since last read
            int newSamplesCount;
            bool wrappedAround = false;

            if (currentPosition > _lastReadPosition)
            {
                newSamplesCount = currentPosition - _lastReadPosition;
            }
            else
            {
                newSamplesCount = (clipSamples - _lastReadPosition) + currentPosition;
                wrappedAround = true;
            }

            if (newSamplesCount > clipSamples / 2)
            {
                Debug.LogWarning($"[MicrophoneCapture] Read position fell behind by {newSamplesCount} samples (more than half the buffer size {clipSamples}). Data loss may have occurred!");
            }

            // Read the raw samples from the AudioClip
            float[] rawSamples = new float[newSamplesCount * _micChannels];

            if (!wrappedAround)
            {
                _micClip.GetData(rawSamples, _lastReadPosition);
            }
            else
            {
                int part1Samples = clipSamples - _lastReadPosition;
                float[] part1Buffer = new float[part1Samples * _micChannels];
                _micClip.GetData(part1Buffer, _lastReadPosition);
                Array.Copy(part1Buffer, 0, rawSamples, 0, part1Buffer.Length);

                int part2Samples = currentPosition;
                if (part2Samples > 0)
                {
                    float[] part2Buffer = new float[part2Samples * _micChannels];
                    _micClip.GetData(part2Buffer, 0);
                    Array.Copy(part2Buffer, 0, rawSamples, part1Buffer.Length, part2Buffer.Length);
                }
            }

            _lastReadPosition = currentPosition;

            // Perform channel count conversion if necessary
            return ConvertChannels(rawSamples, newSamplesCount, _micChannels, _targetChannels);
        }

        /// <summary>
        /// Converts audio samples from source channels count to target channels count.
        /// </summary>
        private float[] ConvertChannels(float[] sourceSamples, int sampleFrameCount, int sourceChannels, int targetChannels)
        {
            if (sourceChannels == targetChannels)
            {
                return sourceSamples;
            }

            float[] convertedSamples = new float[sampleFrameCount * targetChannels];

            if (targetChannels == 1)
            {
                // Downmix to Mono (average all source channels)
                for (int i = 0; i < sampleFrameCount; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < sourceChannels; c++)
                    {
                        sum += sourceSamples[i * sourceChannels + c];
                    }
                    convertedSamples[i] = sum / sourceChannels;
                }
            }
            else if (sourceChannels == 1 && targetChannels == 2)
            {
                // Upmix Mono to Stereo (duplicate mono channel)
                for (int i = 0; i < sampleFrameCount; i++)
                {
                    float sample = sourceSamples[i];
                    convertedSamples[i * 2] = sample;     // Left
                    convertedSamples[i * 2 + 1] = sample; // Right
                }
            }
            else
            {
                // General fallback: copy channel 0 and pad remaining with silence if target > source,
                // or truncate channels if target < source.
                for (int i = 0; i < sampleFrameCount; i++)
                {
                    for (int tc = 0; tc < targetChannels; tc++)
                    {
                        if (tc < sourceChannels)
                        {
                            convertedSamples[i * targetChannels + tc] = sourceSamples[i * sourceChannels + tc];
                        }
                        else
                        {
                            convertedSamples[i * targetChannels + tc] = 0f;
                        }
                    }
                }
            }

            return convertedSamples;
        }

        /// <summary>
        /// Stops microphone recording and releases resource references.
        /// </summary>
        public void StopCapture()
        {
            if (!_isCapturing) return;

            if (Microphone.IsRecording(_deviceName))
            {
                Microphone.End(_deviceName);
            }

            _micClip = null;
            _isCapturing = false;
            Debug.Log("[MicrophoneCapture] Capture stopped.");
        }
    }
}
