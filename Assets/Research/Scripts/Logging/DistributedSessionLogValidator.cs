using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    public enum SessionLogValidationStatus { Valid, Warning, Invalid }

    public sealed class SessionLogValidationResult
    {
        readonly List<string> m_Errors = new List<string>();
        readonly List<string> m_Warnings = new List<string>();

        public SessionLogValidationStatus Status => m_Errors.Count > 0
            ? SessionLogValidationStatus.Invalid
            : m_Warnings.Count > 0 ? SessionLogValidationStatus.Warning : SessionLogValidationStatus.Valid;
        public IReadOnlyList<string> Errors => m_Errors;
        public IReadOnlyList<string> Warnings => m_Warnings;
        public bool IsValid => m_Errors.Count == 0;
        internal void Error(string message) => m_Errors.Add(message);
        internal void Warning(string message) => m_Warnings.Add(message);
    }

    /// <summary>Read-only validator for the distributed session recorder output.</summary>
    public static class DistributedSessionLogValidator
    {
        static readonly string[] RequiredFiles =
        {
            "session_metadata.json", "quest_hand_joints.csv", "quest_head_pose.csv",
            "pc_midi_events.csv", "quest_keyboard_state.csv", "clock_sync.csv",
            "network_diagnostics.csv", "session_summary.json"
        };

        [Serializable]
        sealed class MetadataDocument
        {
            public string session_id;
            public int protocol_version;
        }

        [Serializable]
        sealed class SummaryDocument
        {
            public string session_id;
            public string end_utc;
            public long queue_overflows;
            public long recorded_rows;
            public long log_lines;
            public bool writer_flush_close_completed;
        }

        public static SessionLogValidationResult ValidateDirectory(string directory)
        {
            var result = new SessionLogValidationResult();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                result.Error("Session directory does not exist: " + directory);
                return result;
            }

            foreach (var file in RequiredFiles)
                if (!File.Exists(Path.Combine(directory, file))) result.Error("Missing file: " + file);

            var metadata = ReadJson<MetadataDocument>(directory, "session_metadata.json", result);
            var summary = ReadJson<SummaryDocument>(directory, "session_summary.json", result);
            if (summary != null) ValidateSummary(directory, summary, result);
            if (metadata != null)
            {
                if (!Guid.TryParse(metadata.session_id, out _)) result.Error("metadata.session_id is not a GUID.");
                if (metadata.protocol_version != 1) result.Error("Unsupported protocol_version: " + metadata.protocol_version);
            }
            if (summary != null && string.IsNullOrWhiteSpace(summary.end_utc)) result.Error("summary.end_utc is empty.");
            if (metadata != null && summary != null && !string.Equals(metadata.session_id, summary.session_id, StringComparison.OrdinalIgnoreCase))
                result.Error("session_id differs between metadata and summary.");

            ValidateCsv(directory, "quest_hand_joints.csv",
                "session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,callback_index,update_type,hand,hand_tracked,joint_id,pose_valid,tracking_state,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w",
                new[] { 1, 2, 3, 13, 14, 15, 16, 17, 18, 19 }, metadata?.session_id, result);
            ValidateCsv(directory, "quest_head_pose.csv",
                "session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w",
                new[] { 1, 2, 3, 6, 7, 8, 9, 10, 11, 12 }, metadata?.session_id, result);
            ValidateCsv(directory, "pc_midi_events.csv",
                "session_id,pc_timestamp_sec,event_index,device_name,event_type,channel,note_number,velocity,control_number,control_value,network_sequence_number",
                new[] { 1 }, metadata?.session_id, result);
            ValidateCsv(directory, "quest_keyboard_state.csv",
                "session_id,estimated_pc_timestamp_sec,note_number,pressed,velocity,sustain_active",
                new[] { 1 }, metadata?.session_id, result);
            ValidateCsv(directory, "clock_sync.csv",
                "pc_t0_sec,quest_q1_sec,quest_q2_sec,pc_t3_sec,rtt_sec,offset_sec,selected,sample_index",
                new[] { 0, 1, 2, 3, 4, 5 }, null, result);
            ValidateCsv(directory, "network_diagnostics.csv",
                "pc_timestamp_sec,direction,packet_type,sequence_number,payload_bytes,send_success,receive_success,out_of_order,duplicate,dropped_estimate,queue_depth",
                new[] { 0 }, null, result);

            ValidateClockValues(directory, result);
            return result;
        }

        static void ValidateSummary(string directory, SummaryDocument summary, SessionLogValidationResult result)
        {
            var path = Path.Combine(directory, "session_summary.json");
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception exception)
            {
                result.Error("Could not read session_summary.json: " + exception.Message);
                return;
            }

            var hasOverflowCount = json.IndexOf("\"queue_overflows\"", StringComparison.Ordinal) >= 0;
            if (hasOverflowCount && summary.queue_overflows > 0)
                result.Error("summary.queue_overflows is " + summary.queue_overflows + "; log rows were dropped.");
            else if (!hasOverflowCount)
                result.Warning("summary.queue_overflows is absent; log completeness cannot be confirmed.");
            if (hasOverflowCount && summary.queue_overflows < 0)
                result.Error("summary.queue_overflows is negative.");

            var hasWriterStatus = json.IndexOf("\"writer_flush_close_completed\"", StringComparison.Ordinal) >= 0;
            if (hasWriterStatus && !summary.writer_flush_close_completed)
                result.Error("summary.writer_flush_close_completed is false; the writer did not confirm flush and close.");
            else if (!hasWriterStatus)
                result.Warning("summary.writer_flush_close_completed is absent; writer completion cannot be confirmed.");

            var hasRecordedRows = json.IndexOf("\"recorded_rows\"", StringComparison.Ordinal) >= 0;
            var hasLogLines = json.IndexOf("\"log_lines\"", StringComparison.Ordinal) >= 0;
            if (hasRecordedRows && summary.recorded_rows < 0)
                result.Error("summary.recorded_rows is negative.");
            if (hasLogLines && summary.log_lines < 0)
                result.Error("summary.log_lines is negative.");
            if (hasRecordedRows && hasLogLines && summary.recorded_rows != summary.log_lines)
                result.Error("summary.recorded_rows differs from summary.log_lines.");
        }

        static T ReadJson<T>(string directory, string fileName, SessionLogValidationResult result) where T : class
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return null;
            try
            {
                var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (value == null) result.Error("Invalid JSON: " + fileName);
                return value;
            }
            catch (Exception exception)
            {
                result.Error("Invalid JSON " + fileName + ": " + exception.Message);
                return null;
            }
        }

        static void ValidateCsv(string directory, string fileName, string expectedHeader, int[] finiteColumns,
            string expectedSessionId, SessionLogValidationResult result)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return;
            try
            {
                using (var reader = new StreamReader(path))
                {
                    var header = reader.ReadLine();
                    if (!string.Equals(header, expectedHeader, StringComparison.Ordinal))
                    {
                        result.Error("Unexpected header: " + fileName);
                        return;
                    }

                    var lineNumber = 1;
                    while (!reader.EndOfStream)
                    {
                        ++lineNumber;
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!TryParseCsv(line, out var fields))
                        {
                            result.Error(fileName + " line " + lineNumber + " is not valid CSV.");
                            continue;
                        }
                        var expectedColumns = expectedHeader.Split(',').Length;
                        if (fields.Count != expectedColumns)
                        {
                            result.Error(fileName + " line " + lineNumber + " has " + fields.Count + " columns; expected " + expectedColumns + ".");
                            continue;
                        }
                        if (expectedSessionId != null && !string.Equals(fields[0], expectedSessionId, StringComparison.OrdinalIgnoreCase))
                            result.Error(fileName + " line " + lineNumber + " has a different session_id.");
                        foreach (var column in finiteColumns)
                            if (!TryFinite(fields[column])) result.Error(fileName + " line " + lineNumber + " has a non-finite numeric value at column " + column + ".");
                    }
                }
            }
            catch (Exception exception)
            {
                result.Error("Could not read " + fileName + ": " + exception.Message);
            }
        }

        static void ValidateClockValues(string directory, SessionLogValidationResult result)
        {
            var path = Path.Combine(directory, "clock_sync.csv");
            if (!File.Exists(path)) return;
            try
            {
                using (var reader = new StreamReader(path))
                {
                    reader.ReadLine();
                    var lineNumber = 1;
                    while (!reader.EndOfStream)
                    {
                        ++lineNumber;
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!TryParseCsv(line, out var fields) || fields.Count < 5) continue;
                        if (!double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var rtt) || rtt < 0d)
                            result.Error("clock_sync.csv line " + lineNumber + " has a negative or invalid RTT.");
                    }
                }
            }
            catch (Exception exception)
            {
                result.Error("Could not validate clock_sync.csv: " + exception.Message);
            }
        }

        static bool TryFinite(string value)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                !double.IsNaN(parsed) && !double.IsInfinity(parsed);
        }

        static bool TryParseCsv(string line, out List<string> fields)
        {
            fields = new List<string>();
            var value = new System.Text.StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; ++i)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); ++i; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { fields.Add(value.ToString()); value.Length = 0; }
                else value.Append(c);
            }
            if (quoted) return false;
            fields.Add(value.ToString());
            return true;
        }
    }
}
