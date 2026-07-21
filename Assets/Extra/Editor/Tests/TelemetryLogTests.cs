using NUnit.Framework;
using System.IO;
using UnityEngine;
using Extra.TelemetryLog;

namespace Extra.TelemetryLog.Tests
{
    public class TelemetryLogTests
    {
        private string testFilePath;

        [SetUp]
        public void SetUp()
        {
            testFilePath = Path.Combine(Application.temporaryCachePath, "test_telemetry.csv");
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }

            // Clear singleton instance to prevent test pollution
            var instanceField = typeof(TelemetryLogManager).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (instanceField != null)
            {
                instanceField.SetValue(null, null);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }

            // Clear singleton instance to prevent test pollution
            var instanceField = typeof(TelemetryLogManager).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (instanceField != null)
            {
                instanceField.SetValue(null, null);
            }
        }

        [Test]
        public void CsvFileWriter_CreatesFileAndWritesHeader()
        {
            var writer = new CsvFileWriter();
            string[] headers = new string[] { "Col1", "Col2", "[control]timestamp_unix" };
            
            writer.Open(testFilePath, headers);
            Assert.IsTrue(writer.IsOpen);
            writer.Close();

            Assert.IsTrue(File.Exists(testFilePath));
            string[] lines = File.ReadAllLines(testFilePath);
            Assert.AreEqual(1, lines.Length);
            Assert.AreEqual("Col1,Col2,[control]timestamp_unix", lines[0]);
        }

        [Test]
        public void CsvFileWriter_AppendsAndSanitizesRows()
        {
            var writer = new CsvFileWriter();
            string[] headers = new string[] { "Col1", "Col2" };
            
            writer.Open(testFilePath, headers);
            writer.AppendRow(new string[] { "normal", "value,with,commas" });
            writer.AppendRow(new string[] { "quote\"inside", "another_value" });
            writer.Close();

            string[] lines = File.ReadAllLines(testFilePath);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("normal,\"value,with,commas\"", lines[1]);
            // CSV escapes quotes by doubling them, and wraps the cell in quotes if it has quotes or commas.
            Assert.AreEqual("\"quote\"\"inside\",another_value", lines[2]);
        }

        [Test]
        public void CsvFileWriter_Writes100RowsCorrectly()
        {
            var writer = new CsvFileWriter();
            string[] headers = new string[] { "Col1", "Col2" };
            
            writer.Open(testFilePath, headers);
            for (int i = 0; i < 100; i++)
            {
                writer.AppendRow(new string[] { $"row_{i}", i.ToString() });
            }
            writer.Close();

            string[] lines = File.ReadAllLines(testFilePath);
            Assert.AreEqual(101, lines.Length);
            Assert.AreEqual("row_0,0", lines[1]);
            Assert.AreEqual("row_99,99", lines[100]);
        }

        [Test]
        public void TelemetryLogSettings_ValidationAndPath()
        {
            var settings = new TelemetryLogSettings();
            settings.SamplingIntervalSeconds = 5.0f; // out of range [0.05, 2.0]
            settings.MaxBufferedRowsBeforeFlush = 300; // out of range [10, 200]
            settings.OutputFolderPath = "";

            settings.Validate();

            Assert.AreEqual(2.0f, settings.SamplingIntervalSeconds);
            Assert.AreEqual(200, settings.MaxBufferedRowsBeforeFlush);
            Assert.AreEqual("Assets/Extra/Resources/TelemetryLog", settings.OutputFolderPath);

            string path = settings.GetOutputPath();
            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsTrue(path.Contains("Assets/Extra/Resources/TelemetryLog") || path.Contains("Assets\\Extra\\Resources\\TelemetryLog"));
        }

        [Test]
        public void FrustumUtils_VisibilityChecks()
        {
            var camGo = new GameObject("TestCamera");
            var camera = camGo.AddComponent<Camera>();
            camera.transform.position = Vector3.zero;
            camera.transform.rotation = Quaternion.identity;

            var targetGo = new GameObject("TestTarget");
            
            // Place target directly in front of the camera (z = 5)
            targetGo.transform.position = new Vector3(0, 0, 5);
            Assert.IsTrue(FrustumUtils.IsVisibleToCamera(targetGo.transform, camera));

            // Place target behind the camera (z = -5)
            targetGo.transform.position = new Vector3(0, 0, -5);
            Assert.IsFalse(FrustumUtils.IsVisibleToCamera(targetGo.transform, camera));

            // Place target far off to the side (x = 100)
            targetGo.transform.position = new Vector3(100, 0, 5);
            Assert.IsFalse(FrustumUtils.IsVisibleToCamera(targetGo.transform, camera));

            GameObject.DestroyImmediate(camGo);
            GameObject.DestroyImmediate(targetGo);
        }

        [Test]
        public void TelemetryInputCollector_InitialStateAndFlush()
        {
            var collector = new TelemetryInputCollector();
            Assert.AreEqual(0, collector.InputIntensity);
            Assert.AreEqual(0, collector.InputDiversity);
            Assert.AreEqual(0.0f, collector.IdleFraction);

            collector.CollectFrame();
            collector.Flush();

            Assert.AreEqual(0, collector.InputIntensity);
            Assert.AreEqual(0, collector.InputDiversity);
            Assert.AreEqual(1.0f, collector.IdleFraction);
        }

        [Test]
        public void TelemetryLogManager_SimulatedSampling_WritesCorrectly()
        {
            var go = new GameObject("TelemetryLogManagerTest");
            var manager = go.AddComponent<TelemetryLogManager>();

            // Force Awake to run in EditMode test context
            var awakeMethod = typeof(TelemetryLogManager).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(awakeMethod);
            awakeMethod.Invoke(manager, null);

            var settings = manager.Settings;
            settings.AutoStartOnAwake = false;
            settings.OutputFolderPath = "Assets/Extra/Resources/TelemetryLog/TestOutput";
            settings.SamplingIntervalSeconds = 0.1f;
            settings.MaxBufferedRowsBeforeFlush = 1;

            manager.StartLogging();
            Assert.IsTrue(manager.IsLogging);

            // Set _lastSnapshotTime to 1 second ago using reflection
            var lastSnapshotField = typeof(TelemetryLogManager).GetField("_lastSnapshotTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(lastSnapshotField);
            lastSnapshotField.SetValue(manager, Time.unscaledTime - 1.0f);

            // Call Update to trigger TakeSnapshot
            var updateMethod = typeof(TelemetryLogManager).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(updateMethod);
            
            // Capture the output file path before stopping
            var csvWriterField = typeof(TelemetryLogManager).GetField("_csvWriter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var csvWriter = (CsvFileWriter)csvWriterField.GetValue(manager);
            string filePath = csvWriter.FilePath;

            // Trigger update
            updateMethod.Invoke(manager, null);

            // Stop logging
            manager.StopLogging();
            Assert.IsFalse(manager.IsLogging);

            // Verify file was written
            Assert.IsTrue(File.Exists(filePath));
            string[] lines = File.ReadAllLines(filePath);
            Assert.IsTrue(lines.Length >= 2); // Header + at least 1 snapshot row

            // Clean up
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            string dir = Path.GetDirectoryName(filePath);
            if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
            {
                Directory.Delete(dir);
            }
            GameObject.DestroyImmediate(go);
        }
    }
}
