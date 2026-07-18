using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Extra.ScreenRecording
{
    public static class VideoEncoder
    {
        [System.Serializable]
        private class SegmentMetadata
        {
            public int frameRate;
            public int width;
            public int height;
            public int frameCount;
            public double durationSeconds;
            public int audioSampleRate;
            public int audioChannels;
            public string imageFormat;
            public string platform;
            public string createdAt;
        }

        /// <summary>
        /// Saves a SegmentData object to disk in the background.
        /// </summary>
        /// <param name="data">The segment data copy.</param>
        /// <param name="outputFolder">Absolute or relative output folder path.</param>
        /// <param name="segmentName">Name of the folder representing the segment.</param>
        /// <param name="settings">The screen recording settings.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public static Task SaveSegmentAsync(SegmentData data, string outputFolder, string segmentName, ScreenRecordingSettings settings)
        {
            // Prepare metadata and serialize to JSON on the main thread
            var metadata = new SegmentMetadata
            {
                frameRate = data.FrameRate,
                width = data.Width,
                height = data.Height,
                frameCount = data.Frames.Length,
                durationSeconds = Math.Round(data.Duration, 2),
                audioSampleRate = data.AudioSampleRate,
                audioChannels = data.AudioChannels,
                imageFormat = settings.ImageFormat.ToString(),
                platform = Application.platform.ToString(),
                createdAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK")
            };
            string metadataJson = JsonUtility.ToJson(metadata, true);

            // Execute the file saving operations on a background thread
            return Task.Run(async () =>
            {
                try
                {
                    // Ensure the output folder and segment folder exist
                    string segmentPath = Path.Combine(outputFolder, segmentName);
                    if (!Directory.Exists(segmentPath))
                    {
                        Directory.CreateDirectory(segmentPath);
                    }

                    // 1. Write frames in parallel (throttled to limit resource consumption)
                    await SaveFramesAsync(data, segmentPath, settings);

                    // 2. Write WAV audio if captured
                    if (settings.CaptureAudio && data.AudioSampleCount > 0)
                    {
                        string audioFilePath = Path.Combine(segmentPath, "audio.wav");
                        WriteWavAudio(audioFilePath, data.AudioSamples, data.AudioSampleCount, data.AudioSampleRate, data.AudioChannels);
                    }

                    // 3. Write metadata JSON
                    string metadataFilePath = Path.Combine(segmentPath, "metadata.json");
                    File.WriteAllText(metadataFilePath, metadataJson);

                    if (settings.EnableDebugLogs)
                    {
                        Debug.Log($"[VideoEncoder] Segment '{segmentName}' saved successfully to '{segmentPath}'");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VideoEncoder] Failed to save segment '{segmentName}': {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        /// <summary>
        /// Writes pre-encoded frame byte arrays to disk in parallel (limit concurrency to prevent OS limits).
        /// </summary>
        private static async Task SaveFramesAsync(SegmentData data, string segmentFolder, ScreenRecordingSettings settings)
        {
            string extension = settings.ImageFormat == ImageFormat.JPG ? "jpg" : "png";
            
            // Set max concurrent writes to a reasonable limit (e.g. 16)
            using (var semaphore = new SemaphoreSlim(16))
            {
                var writeTasks = new Task[data.Frames.Length];
                for (int i = 0; i < data.Frames.Length; i++)
                {
                    int index = i;
                    byte[] bytes = data.Frames[i].EncodedBytes;
                    if (bytes != null)
                    {
                        string filePath = Path.Combine(segmentFolder, $"frame_{index:D4}.{extension}");
                        writeTasks[i] = WriteFileWithSemaphoreAsync(filePath, bytes, semaphore);
                    }
                    else
                    {
                        writeTasks[i] = Task.CompletedTask;
                    }
                }
                await Task.WhenAll(writeTasks);
            }
        }

        private static async Task WriteFileWithSemaphoreAsync(string filePath, byte[] bytes, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await fs.WriteAsync(bytes, 0, bytes.Length);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Writes raw PCM float samples into a 16-bit WAV file.
        /// Runs entirely on the background thread without Unity API dependencies.
        /// </summary>
        private static void WriteWavAudio(string filePath, float[] samples, int sampleCount, int sampleRate, int channels)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fileStream))
            {
                const int bitsPerSample = 16;
                int byteRate = sampleRate * channels * (bitsPerSample / 8);
                int blockAlign = channels * (bitsPerSample / 8);
                int subChunk2Size = sampleCount * (bitsPerSample / 8);
                int chunkSize = 36 + subChunk2Size;

                // 1. RIFF header
                writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
                writer.Write(chunkSize);
                writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));

                // 2. fmt chunk
                writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
                writer.Write(16); // Subchunk1Size
                writer.Write((short)1); // AudioFormat (1 = PCM)
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                // 3. data chunk
                writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
                writer.Write(subChunk2Size);

                // Convert float samples to 16-bit signed PCM
                for (int i = 0; i < sampleCount; i++)
                {
                    float sample = samples[i];
                    // Clamp to range [-1.0, 1.0] and convert to short
                    int val = (int)(sample * 32767f);
                    if (val > 32767) val = 32767;
                    else if (val < -32768) val = -32768;
                    writer.Write((short)val);
                }
            }
        }
    }
}
