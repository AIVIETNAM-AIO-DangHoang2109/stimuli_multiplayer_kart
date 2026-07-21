using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace Extra.TelemetryLog
{
    public class CsvFileWriter
    {
        private string _filePath;
        private StreamWriter _writer;
        private int _headerCount;
        private readonly List<string> _lineBuffer = new List<string>();

        public string FilePath => _filePath;
        public bool IsOpen => _writer != null;
        public int BufferedRowCount => _lineBuffer.Count;

        /// <summary>
        /// Creates the parent directory, creates the file, and writes the header row.
        /// </summary>
        public void Open(string filePath, string[] headerColumns)
        {
            if (IsOpen)
            {
                Debug.LogWarning($"[CsvFileWriter] File already open: {_filePath}. Closing it first.");
                Close();
            }

            if (headerColumns == null || headerColumns.Length == 0)
            {
                Debug.LogError("[CsvFileWriter] Cannot open file without header columns.");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _filePath = filePath;
                _headerCount = headerColumns.Length;

                // Create stream writer with UTF-8 encoding (no BOM preferred, or standard)
                _writer = new StreamWriter(filePath, false, Encoding.UTF8);
                _writer.AutoFlush = false;

                // Write header
                string headerRow = string.Join(",", headerColumns);
                _writer.WriteLine(headerRow);
                _writer.Flush(); // Flush header immediately
            }
            catch (IOException ex)
            {
                Debug.LogError($"[CsvFileWriter] IOException opening file {filePath}: {ex.Message}");
                _writer = null;
                _filePath = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CsvFileWriter] Exception opening file {filePath}: {ex.Message}");
                _writer = null;
                _filePath = null;
            }
        }

        /// <summary>
        /// Buffers a single CSV row in memory.
        /// </summary>
        public void AppendRow(string[] values)
        {
            if (!IsOpen)
            {
                Debug.LogError("[CsvFileWriter] Cannot append row. File is not open.");
                return;
            }

            if (values == null || values.Length != _headerCount)
            {
                int valLen = values != null ? values.Length : 0;
                Debug.LogError($"[CsvFileWriter] Row value count mismatch. Expected {_headerCount}, got {valLen}. Skipping row.");
                return;
            }

            string[] sanitized = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                sanitized[i] = SanitizeCsvValue(values[i]);
            }

            string rowString = string.Join(",", sanitized);
            _lineBuffer.Add(rowString);
        }

        /// <summary>
        /// Writes all buffered rows to disk.
        /// </summary>
        public void Flush()
        {
            if (!IsOpen) return;
            if (_lineBuffer.Count == 0) return;

            try
            {
                foreach (string line in _lineBuffer)
                {
                    _writer.WriteLine(line);
                }
                _writer.Flush();
                _lineBuffer.Clear();
            }
            catch (IOException ex)
            {
                Debug.LogError($"[CsvFileWriter] IOException flushing data to {_filePath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CsvFileWriter] Exception flushing data to {_filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Flushes remaining data and closes the file stream.
        /// </summary>
        public void Close()
        {
            if (!IsOpen) return;

            try
            {
                Flush();
                _writer.Close();
            }
            catch (IOException ex)
            {
                Debug.LogError($"[CsvFileWriter] IOException closing file {_filePath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CsvFileWriter] Exception closing file {_filePath}: {ex.Message}");
            }
            finally
            {
                _writer = null;
                _filePath = null;
                _headerCount = 0;
                _lineBuffer.Clear();
            }
        }

        /// <summary>
        /// Escapes commas and quotes inside string values.
        /// </summary>
        public static string SanitizeCsvValue(string value)
        {
            if (value == null) return "";
            bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (needsQuotes)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }
    }
}
