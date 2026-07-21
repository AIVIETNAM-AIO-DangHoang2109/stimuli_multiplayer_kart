using UnityEngine;
using System.IO;

namespace Extra.TelemetryLog
{
    [System.Serializable]
    public class TelemetryLogSettings
    {
        [Tooltip("Snapshot window duration. 4 Hz = every 0.25 seconds.")]
        [Range(0.05f, 2.0f)]
        public float SamplingIntervalSeconds = 0.25f;

        [Tooltip("Relative project folder or absolute path where CSV files are saved. On Android, auto-switches to persistentDataPath.")]
        public string OutputFolderPath = "Assets/Extra/Resources/TelemetryLog";

        [Tooltip("Prefix for the saved file names.")]
        public string FileNamePrefix = "telemetry_";

        [Tooltip("Start logging immediately when the manager initializes.")]
        public bool AutoStartOnAwake = true;

        [Tooltip("Toggle verbose debug logs in the Unity Editor console.")]
        public bool EnableDebugLogs = false;

        [Tooltip("Number of CSV rows buffered in memory before flushing to disk (40 rows is approx. 10 seconds at 4 Hz).")]
        [Range(10, 200)]
        public int MaxBufferedRowsBeforeFlush = 40;

        /// <summary>
        /// Returns the absolute path where telemetry CSV files should be saved.
        /// </summary>
        public string GetOutputPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Path.Combine(Application.persistentDataPath, "TelemetryLog");
#else
            string folder = OutputFolderPath;
            if (string.IsNullOrEmpty(folder))
            {
                folder = "Assets/Extra/Resources/TelemetryLog";
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
#endif
        }

        /// <summary>
        /// Validates and clamps settings to reasonable ranges.
        /// </summary>
        public void Validate()
        {
            SamplingIntervalSeconds = Mathf.Clamp(SamplingIntervalSeconds, 0.05f, 2.0f);
            MaxBufferedRowsBeforeFlush = Mathf.Clamp(MaxBufferedRowsBeforeFlush, 10, 200);
            if (string.IsNullOrEmpty(OutputFolderPath))
            {
                OutputFolderPath = "Assets/Extra/Resources/TelemetryLog";
            }
            if (string.IsNullOrEmpty(FileNamePrefix))
            {
                FileNamePrefix = "telemetry_";
            }
        }
    }
}
