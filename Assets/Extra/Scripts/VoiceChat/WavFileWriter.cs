using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Extra.VoiceChat
{
    public static class WavFileWriter
    {
        /// <summary>
        /// Saves a float PCM array as a 16-bit signed WAV file asynchronously.
        /// </summary>
        /// <param name="filePath">Target destination path for the .wav file.</param>
        /// <param name="samples">PCM float samples (normalized to range [-1.0, 1.0]).</param>
        /// <param name="sampleCount">Number of valid samples in the array to write.</param>
        /// <param name="sampleRate">Sampling rate in Hz (e.g. 16000).</param>
        /// <param name="channels">Number of audio channels (e.g. 1).</param>
        /// <returns>A Task representing the asynchronous write operation, returning true if successful.</returns>
        public static Task<bool> SaveAsync(string filePath, float[] samples, int sampleCount, int sampleRate, int channels)
        {
            // Copy samples to a local array so the thread does not access shared mutable state
            float[] sampleCopy = new float[sampleCount];
            Array.Copy(samples, 0, sampleCopy, 0, sampleCount);

            return Task.Run(() =>
            {
                try
                {
                    // Ensure the directory exists
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
                    using (BinaryWriter writer = new BinaryWriter(fileStream))
                    {
                        const int bitsPerSample = 16;
                        int byteRate = sampleRate * channels * (bitsPerSample / 8);
                        int blockAlign = channels * (bitsPerSample / 8);
                        int subChunk2Size = sampleCount * (bitsPerSample / 8);
                        int chunkSize = 36 + subChunk2Size;

                        // 1. RIFF header
                        writer.Write(Encoding.UTF8.GetBytes("RIFF"));
                        writer.Write(chunkSize);
                        writer.Write(Encoding.UTF8.GetBytes("WAVE"));

                        // 2. fmt chunk
                        writer.Write(Encoding.UTF8.GetBytes("fmt "));
                        writer.Write(16); // Subchunk1Size
                        writer.Write((short)1); // AudioFormat (1 = PCM)
                        writer.Write((short)channels);
                        writer.Write(sampleRate);
                        writer.Write(byteRate);
                        writer.Write((short)blockAlign);
                        writer.Write((short)bitsPerSample);

                        // 3. data chunk
                        writer.Write(Encoding.UTF8.GetBytes("data"));
                        writer.Write(subChunk2Size);

                        // Convert float samples to 16-bit signed PCM
                        for (int i = 0; i < sampleCount; i++)
                        {
                            float sample = sampleCopy[i];
                            short shortSample = (short)Mathf.Clamp(sample * 32767f, -32768f, 32767f);
                            writer.Write(shortSample);
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WavFileWriter] Failed to save WAV file at '{filePath}': {ex.Message}");
                    return false;
                }
            });
        }
    }
}
