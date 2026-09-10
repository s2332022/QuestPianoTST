using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace QuestPianoMotion.Research
{
    [Serializable]
    sealed class SessionMetadata
    {
        public string session_id;
        public string device_model;
        public string quest_os_version;
        public string unity_version;
        public string openxr_version;
        public string xr_hands_version;
        public string midi_device_name;
        public string electronic_piano_model;
        public double display_refresh_rate_hz;
        public string hand_tracking_mode;
        public string calibration_id;
        public double recording_duration_sec;
        public string git_commit;
        public string log_format_version;
        public long dropped_log_batches;
    }

    enum LogFileKind { Hands, Midi, Head, Keyboard, UpdateSummary }

    readonly struct LogBatch
    {
        public LogBatch(LogFileKind kind, string text) { Kind = kind; Text = text; }
        public LogFileKind Kind { get; }
        public string Text { get; }
    }

    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SynchronizedSessionRecorder : MonoBehaviour
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        readonly MonotonicSessionClock m_Clock = ResearchServices.Clock;
        BlockingCollection<LogBatch> m_Queue;
        Thread m_WriterThread;
        XRHandPoseProvider m_Hands;
        AndroidMidiInput m_Midi;
        VirtualPianoKeyboard m_Keyboard;
        PianoCalibrationManager m_Calibration;
        Transform m_Head;
        double m_LastStatusLog;
        long m_DroppedBatches;
        volatile string m_WriterError;

        public bool IsRecording { get; private set; }
        public string SessionId { get; private set; } = string.Empty;
        public string SessionPath { get; private set; } = string.Empty;
        public double RecordingTimeSeconds => IsRecording ? m_Clock.SessionTimeSeconds : 0d;

        public void Initialize(XRHandPoseProvider hands, AndroidMidiInput midi, VirtualPianoKeyboard keyboard,
            PianoCalibrationManager calibration, Transform head)
        {
            m_Hands = hands;
            m_Midi = midi;
            m_Keyboard = keyboard;
            m_Calibration = calibration;
            m_Head = head;
            m_Hands.RawFrameUpdated += OnHandFrame;
            m_Midi.MessageReceived += OnMidi;
            m_Keyboard.StateChanged += OnKeyboardState;
        }

        void Update()
        {
            if (m_WriterError != null)
            {
                var error = m_WriterError;
                m_WriterError = null;
                Debug.LogError($"[Recording] Writer failed: {error}", this);
            }
            if (!IsRecording || m_Head == null) return;
            var t = m_Clock.SessionTimeSeconds;
            var p = m_Head.position;
            var q = m_Head.rotation;
            Enqueue(LogFileKind.Head, string.Format(Invariant,
                "{0:F9},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R}\n",
                t, Time.frameCount, p.x, p.y, p.z, q.x, q.y, q.z, q.w));
            if (t - m_LastStatusLog >= 1d)
            {
                m_LastStatusLog = t;
                Debug.Log($"[Recording] {SessionId} {t:F1}s, dropped batches={m_DroppedBatches}", this);
            }
        }

        void OnDisable()
        {
            StopRecording();
            Unsubscribe();
        }

        void OnApplicationQuit() => StopRecording();

        public bool StartRecording()
        {
            if (IsRecording) return false;
            try
            {
                SessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                SessionPath = Path.Combine(Application.persistentDataPath, "PianoResearch", "sessions", SessionId);
                Directory.CreateDirectory(SessionPath);
                WriteCalibrationSnapshot();
                m_DroppedBatches = 0;
                m_Queue = new BlockingCollection<LogBatch>(4096);
                m_WriterThread = new Thread(WriterLoop) { IsBackground = true, Name = "PianoResearchLogWriter" };
                m_Clock.StartSession();
                IsRecording = true;
                m_LastStatusLog = 0d;
                m_WriterThread.Start();
                return true;
            }
            catch (Exception exception)
            {
                IsRecording = false;
                m_Clock.StopSession();
                Debug.LogError($"[Recording] Start failed: {exception.Message}", this);
                return false;
            }
        }

        public void StartRecordingAction() => StartRecording();

        public void StopRecording()
        {
            if (!IsRecording) return;
            var duration = m_Clock.SessionTimeSeconds;
            IsRecording = false;
            m_Queue?.CompleteAdding();
            if (m_WriterThread != null && !m_WriterThread.Join(5000))
                Debug.LogWarning("[Recording] Writer did not finish within five seconds.", this);
            WriteMetadata(duration);
            m_Clock.StopSession();
            m_Queue?.Dispose();
            m_Queue = null;
            m_WriterThread = null;
            Debug.Log($"[Recording] Saved {SessionId} to {SessionPath}", this);
        }

        void OnHandFrame(HandPoseFrame frame)
        {
            if (!IsRecording) return;
            var t = m_Clock.AbsoluteToSessionTime(frame.AbsoluteTimeSeconds);
            if (t < 0d) return;
            var builder = new StringBuilder(12288);
            AppendHand(builder, t, frame, true);
            AppendHand(builder, t, frame, false);
            Enqueue(LogFileKind.Hands, builder.ToString());
            Enqueue(LogFileKind.UpdateSummary, string.Format(Invariant,
                "{0:F9},{1},{2},{3},{4},{5},{6}\n", t, frame.UnityFrame, frame.CallbackIndex,
                frame.UpdateType, frame.SuccessFlags, frame.LeftTracked, frame.RightTracked));
        }

        void OnMidi(MidiMessage message)
        {
            if (!IsRecording) return;
            var t = m_Clock.AbsoluteToSessionTime(message.AbsoluteTimeSeconds);
            if (t < 0d) return;
            Enqueue(LogFileKind.Midi, string.Format(Invariant,
                "{0:F9},{1},{2},{3},{4},{5},{6},{7},{8}\n", t, message.EventIndex,
                Csv(message.DeviceName), message.EventType, message.Channel, message.NoteNumber,
                message.Velocity, message.ControlNumber, message.ControlValue));
        }

        void OnKeyboardState(KeyboardStateChange change)
        {
            if (!IsRecording) return;
            var t = m_Clock.AbsoluteToSessionTime(change.AbsoluteTimeSeconds);
            if (t < 0d) return;
            Enqueue(LogFileKind.Keyboard, string.Format(Invariant, "{0:F9},{1},{2},{3},{4},{5}\n",
                t, Time.frameCount, change.NoteNumber, change.Pressed, change.Velocity, change.SustainActive));
        }

        static void AppendHand(StringBuilder builder, double t, HandPoseFrame frame, bool left)
        {
            var handName = left ? "left" : "right";
            var tracked = left ? frame.LeftTracked : frame.RightTracked;
            var root = left ? frame.LeftRootPose : frame.RightRootPose;
            AppendJoint(builder, t, frame, handName, tracked, "Root", tracked, tracked ? "Pose" : "None", root);
            var joints = left ? frame.LeftJoints : frame.RightJoints;
            for (var i = 0; i < joints.Length; ++i)
            {
                var joint = joints[i];
                AppendJoint(builder, t, frame, handName, tracked, joint.JointId.ToString(), joint.PoseValid,
                    joint.TrackingState.ToString(), joint.Pose);
            }
        }

        static void AppendJoint(StringBuilder b, double t, HandPoseFrame frame, string hand, bool tracked,
            string joint, bool valid, string state, Pose pose)
        {
            b.Append(t.ToString("F9", Invariant)).Append(',').Append(frame.UnityFrame).Append(',')
                .Append(frame.CallbackIndex).Append(',').Append(frame.UpdateType).Append(',').Append(hand).Append(',')
                .Append(tracked).Append(',').Append(joint).Append(',').Append(valid).Append(',').Append(state).Append(',')
                .Append(pose.position.x.ToString("R", Invariant)).Append(',')
                .Append(pose.position.y.ToString("R", Invariant)).Append(',')
                .Append(pose.position.z.ToString("R", Invariant)).Append(',')
                .Append(pose.rotation.x.ToString("R", Invariant)).Append(',')
                .Append(pose.rotation.y.ToString("R", Invariant)).Append(',')
                .Append(pose.rotation.z.ToString("R", Invariant)).Append(',')
                .Append(pose.rotation.w.ToString("R", Invariant)).Append('\n');
        }

        void Enqueue(LogFileKind kind, string line)
        {
            var queue = m_Queue;
            if (queue == null || queue.IsAddingCompleted || !queue.TryAdd(new LogBatch(kind, line)))
                Interlocked.Increment(ref m_DroppedBatches);
        }

        void WriterLoop()
        {
            var writers = new Dictionary<LogFileKind, StreamWriter>();
            try
            {
                writers.Add(LogFileKind.Hands, Open("hand_joints.csv", "session_time_sec,unity_frame,callback_index,update_type,hand,hand_tracked,joint_id,pose_valid,tracking_state,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w"));
                writers.Add(LogFileKind.Midi, Open("midi_events.csv", "session_time_sec,event_index,device_name,event_type,channel,note_number,velocity,control_number,control_value"));
                writers.Add(LogFileKind.Head, Open("head_pose.csv", "session_time_sec,unity_frame,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w"));
                writers.Add(LogFileKind.Keyboard, Open("keyboard_state.csv", "session_time_sec,unity_frame,note_number,pressed,velocity,sustain_active"));
                writers.Add(LogFileKind.UpdateSummary, Open("update_summary.csv", "session_time_sec,unity_frame,callback_index,update_type,success_flags,left_tracked,right_tracked"));
                foreach (var batch in m_Queue.GetConsumingEnumerable()) writers[batch.Kind].Write(batch.Text);
            }
            catch (Exception exception)
            {
                m_WriterError = exception.Message;
            }
            finally
            {
                foreach (var writer in writers.Values) { writer.Flush(); writer.Dispose(); }
            }
        }

        StreamWriter Open(string fileName, string header)
        {
            var writer = new StreamWriter(Path.Combine(SessionPath, fileName), false, new UTF8Encoding(false), 65536);
            writer.WriteLine(header);
            return writer;
        }

        void WriteCalibrationSnapshot()
        {
            var calibration = m_Calibration != null ? m_Calibration.Current : new PianoCalibrationData();
            File.WriteAllText(Path.Combine(SessionPath, "piano_calibration.json"), JsonUtility.ToJson(calibration, true));
        }

        void WriteMetadata(double duration)
        {
            try
            {
                var data = new SessionMetadata
                {
                    session_id = SessionId,
                    device_model = SystemInfo.deviceModel,
                    quest_os_version = SystemInfo.operatingSystem,
                    unity_version = Application.unityVersion,
                    openxr_version = "1.17.1",
                    xr_hands_version = "1.8.1",
                    midi_device_name = m_Midi == null ? string.Empty :
                        (!string.IsNullOrEmpty(m_Midi.ConnectedDeviceName) ? m_Midi.ConnectedDeviceName : m_Midi.SelectedDeviceName),
                    electronic_piano_model = PlayerPrefs.GetString("PianoResearch.ElectronicPianoModel",
                        m_Midi != null ? m_Midi.SelectedDeviceName : "unspecified"),
                    display_refresh_rate_hz = Screen.currentResolution.refreshRateRatio.value,
                    hand_tracking_mode = "OpenXR XR Hands",
                    calibration_id = m_Calibration?.Current?.calibrationId ?? string.Empty,
                    recording_duration_sec = duration,
                    git_commit = "unavailable (project has no .git directory)",
                    log_format_version = "1.0.0",
                    dropped_log_batches = m_DroppedBatches
                };
                File.WriteAllText(Path.Combine(SessionPath, "session_metadata.json"), JsonUtility.ToJson(data, true));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Recording] Metadata save failed: {exception.Message}", this);
            }
        }

        void Unsubscribe()
        {
            if (m_Hands != null) m_Hands.RawFrameUpdated -= OnHandFrame;
            if (m_Midi != null) m_Midi.MessageReceived -= OnMidi;
            if (m_Keyboard != null) m_Keyboard.StateChanged -= OnKeyboardState;
        }

        static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
