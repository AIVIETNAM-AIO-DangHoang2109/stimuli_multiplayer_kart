using UnityEngine;

namespace Extra.ScreenRecording
{
    public enum ImageFormat
    {
        JPG,
        PNG
    }

    [System.Serializable]
    public class ScreenRecordingSettings
    {
        [Tooltip("Width of captured frames in pixels.")]
        public int CaptureWidth = 1280;

        [Tooltip("Height of captured frames in pixels.")]
        public int CaptureHeight = 720;

        [Range(1, 60)]
        [Tooltip("Frames per second to capture. Lower = smaller files, less CPU.")]
        public int TargetFrameRate = 15;

        [Tooltip("Frame encoding format (JPG or PNG). JPG is ~5x smaller.")]
        public ImageFormat ImageFormat = ImageFormat.JPG;

        [Range(1, 100)]
        [Tooltip("JPEG compression quality (1–100). 75 is a good balance.")]
        public int JpgQuality = 75;

        [Tooltip("Duration of each saved video segment in seconds.")]
        public float SegmentDurationSeconds = 60f;

        [Tooltip("Whether to capture game audio alongside video frames.")]
        public bool CaptureAudio = true;

        [Tooltip("Audio sample rate (matches Unity's default audio output).")]
        public int AudioSampleRate = 44100;

        [Tooltip("Stereo audio capture.")]
        public int AudioChannels = 2;

        [Tooltip("Base output directory (Editor). On Android, auto-switches to Application.persistentDataPath + \"/ScreenRecording\".")]
        public string OutputFolderPath = "Assets/Extra/Resources/ScreenRecording";

        [Tooltip("Prefix for segment folder/file names.")]
        public string FileNamePrefix = "screen_";

        [Tooltip("Whether recording starts immediately when the manager initialises.")]
        public bool AutoStartOnAwake = true;

        [Tooltip("Toggle verbose logging for development.")]
        public bool EnableDebugLogs = false;

        [Tooltip("Max background threads writing frame files simultaneously.")]
        public int MaxConcurrentEncoders = 2;

        /// <summary>
        /// Returns the output folder path adjusted for the platform.
        /// </summary>
        public string GetOutputPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return System.IO.Path.Combine(Application.persistentDataPath, "ScreenRecording");
#else
            return OutputFolderPath;
#endif
        }

        /// <summary>
        /// Validates settings, adjusting values to be within safe limits and printing warnings if corrected.
        /// </summary>
        public void Validate()
        {
            if (CaptureWidth <= 0)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] CaptureWidth must be > 0. Resetting to 1280.");
                CaptureWidth = 1280;
            }
            if (CaptureHeight <= 0)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] CaptureHeight must be > 0. Resetting to 720.");
                CaptureHeight = 720;
            }
            if (TargetFrameRate < 1 || TargetFrameRate > 60)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] TargetFrameRate must be between 1 and 60. Resetting to 15.");
                TargetFrameRate = 15;
            }
            if (JpgQuality < 1 || JpgQuality > 100)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] JpgQuality must be between 1 and 100. Resetting to 75.");
                JpgQuality = 75;
            }
            if (SegmentDurationSeconds <= 0f)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] SegmentDurationSeconds must be > 0. Resetting to 60.");
                SegmentDurationSeconds = 60f;
            }
            if (AudioSampleRate <= 0)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] AudioSampleRate must be > 0. Resetting to 44100.");
                AudioSampleRate = 44100;
            }
            if (AudioChannels < 1 || AudioChannels > 2)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] AudioChannels must be 1 (Mono) or 2 (Stereo). Resetting to 2.");
                AudioChannels = 2;
            }
            if (MaxConcurrentEncoders < 1)
            {
                Debug.LogWarning($"[ScreenRecordingSettings] MaxConcurrentEncoders must be >= 1. Resetting to 2.");
                MaxConcurrentEncoders = 2;
            }
        }
    }
}
