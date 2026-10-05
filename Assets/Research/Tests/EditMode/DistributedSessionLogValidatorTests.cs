using System;
using System.IO;
using NUnit.Framework;
using QuestPianoMotion.Research.Distributed;
using UnityEngine;

namespace QuestPianoMotion.Research.Tests
{
    public sealed class DistributedSessionLogValidatorTests
    {
        const string SessionId = "00000000-0000-0000-0000-000000000001";
        string m_Directory;

        [SetUp]
        public void SetUp()
        {
            m_Directory = Path.Combine(Path.GetTempPath(), "QuestPianoMotionValidatorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "session_metadata.json"),
                "{\"session_id\":\"" + SessionId + "\",\"protocol_version\":1}");
            WriteCsv("quest_hand_joints.csv", "session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,callback_index,update_type,hand,hand_tracked,joint_id,pose_valid,tracking_state,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w");
            WriteCsv("quest_head_pose.csv", "session_id,quest_timestamp_sec,estimated_pc_timestamp_sec,pc_receive_timestamp_sec,sequence_number,unity_frame,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w");
            WriteCsv("pc_midi_events.csv", "session_id,pc_timestamp_sec,event_index,device_name,event_type,channel,note_number,velocity,control_number,control_value,network_sequence_number");
            WriteCsv("quest_keyboard_state.csv", "session_id,estimated_pc_timestamp_sec,note_number,pressed,velocity,sustain_active");
            WriteCsv("clock_sync.csv", "pc_t0_sec,quest_q1_sec,quest_q2_sec,pc_t3_sec,rtt_sec,offset_sec,selected,sample_index");
            WriteCsv("network_diagnostics.csv", "pc_timestamp_sec,direction,packet_type,sequence_number,payload_bytes,send_success,receive_success,out_of_order,duplicate,dropped_estimate,queue_depth");
            WriteSummary("\"queue_overflows\":0,\"recorded_rows\":0,\"log_lines\":0,\"writer_flush_close_completed\":true");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Directory)) Directory.Delete(m_Directory, true);
        }

        [Test]
        public void QueueOverflow_MakesSessionInvalid()
        {
            WriteSummary("\"queue_overflows\":2,\"recorded_rows\":0,\"log_lines\":0,\"writer_flush_close_completed\":true");

            var result = DistributedSessionLogValidator.ValidateDirectory(m_Directory);

            Assert.That(result.Status, Is.EqualTo(SessionLogValidationStatus.Invalid));
            Assert.That(string.Join("\n", result.Errors), Does.Contain("queue_overflows"));
        }

        [Test]
        public void WriterFlushCloseFailure_MakesSessionInvalid()
        {
            WriteSummary("\"queue_overflows\":0,\"recorded_rows\":0,\"log_lines\":0,\"writer_flush_close_completed\":false");

            var result = DistributedSessionLogValidator.ValidateDirectory(m_Directory);

            Assert.That(result.Status, Is.EqualTo(SessionLogValidationStatus.Invalid));
            Assert.That(string.Join("\n", result.Errors), Does.Contain("writer_flush_close_completed"));
        }

        [Test]
        public void OlderSummaryWithoutCompletenessFields_IsWarning()
        {
            WriteSummary("\"recorded_rows\":0,\"log_lines\":0");

            var result = DistributedSessionLogValidator.ValidateDirectory(m_Directory);

            Assert.That(result.Status, Is.EqualTo(SessionLogValidationStatus.Warning));
            var warnings = string.Join("\n", result.Warnings);
            Assert.That(warnings, Does.Contain("queue_overflows"));
            Assert.That(warnings, Does.Contain("writer_flush_close_completed"));
        }

        [Test]
        public void QuestMetadata_CapturesAppliedCalibrationAndKeepsProtocolVersion()
        {
            var id=Guid.NewGuid();
            var directory=Path.Combine(Application.persistentDataPath,"PianoResearch","DistributedSessions",id.ToString("N")+"_Quest");
            try
            {
                var calibration=new PianoCalibrationData
                {
                    valid=true,formatVersion=1,calibrationId="calibration-test",pointA=new Vector3(1,2,3),
                    pointB=new Vector3(2,2,3),pointC=new Vector3(1,2,4),scaleX=1.1f,scaleZ=0.9f
                };
                DistributedSessionRecorder.WriteQuestMetadata(id,calibration,null);
                var metadata=File.ReadAllText(Path.Combine(directory,"session_metadata.json"));
                Assert.That(metadata,Does.Contain("\"protocol_version\": 1"));
                Assert.That(metadata,Does.Contain("\"calibration_version\": 1"));
                Assert.That(metadata,Does.Contain("\"calibration-test\""));
                Assert.That(metadata,Does.Contain("\"pointA\""));
                Assert.That(metadata,Does.Contain("\"scaleX\": 1.1"));
                Assert.That(metadata,Does.Contain("\"keyboard_root_position\": null"));
                Assert.That(metadata,Does.Contain("\"keyboard_display_mode\": null"));
            }
            finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        }

        void WriteSummary(string fields)
        {
            File.WriteAllText(Path.Combine(m_Directory, "session_summary.json"),
                "{\"session_id\":\"" + SessionId + "\",\"end_utc\":\"2026-09-23T00:00:00Z\"," + fields + "}");
        }

        void WriteCsv(string fileName, string header) => File.WriteAllText(Path.Combine(m_Directory, fileName), header + "\n");
    }
}
