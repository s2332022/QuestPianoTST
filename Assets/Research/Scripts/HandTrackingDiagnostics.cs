using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace QuestPianoMotion.Research
{
    /// <summary>Periodically samples XR Hands and dispatches the result to registered sinks.</summary>
    [DisallowMultipleComponent]
    public sealed class HandTrackingDiagnostics : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Write left-hand state to the Unity Console.")]
        bool m_LogLeftHand = true;

        [SerializeField]
        [Tooltip("Write right-hand state to the Unity Console.")]
        bool m_LogRightHand = true;

        [SerializeField, Min(0.05f)]
        [Tooltip("Minimum time in seconds between Console samples.")]
        float m_LogIntervalSeconds = 1f;

        [SerializeField, Min(0.1f)]
        [Tooltip("How often to check for an XRHandSubsystem while it is unavailable or stopped.")]
        float m_SubsystemRetryIntervalSeconds = 1f;

        readonly List<IHandTrackingSampleSink> m_Sinks = new List<IHandTrackingSampleSink>(2);
        readonly SampleIntervalGate m_LogGate = new SampleIntervalGate(1d);
        XRHandSubsystem m_Subsystem;
        ConsoleHandTrackingSampleSink m_ConsoleSink;
        double m_NextSubsystemLookupTime;

        /// <summary>Registers an additional sink, such as a future CSV recorder.</summary>
        public void RegisterSink(IHandTrackingSampleSink sink)
        {
            if (sink != null && !m_Sinks.Contains(sink))
                m_Sinks.Add(sink);
        }

        /// <summary>Stops dispatching samples to a previously registered sink.</summary>
        public void UnregisterSink(IHandTrackingSampleSink sink)
        {
            if (sink != null)
                m_Sinks.Remove(sink);
        }

        void OnEnable()
        {
            if (m_ConsoleSink == null)
                m_ConsoleSink = new ConsoleHandTrackingSampleSink(this);

            RegisterSink(m_ConsoleSink);
            ApplyInspectorSettings();
            m_LogGate.Reset();
            m_NextSubsystemLookupTime = 0d;
            TryBindLoadedSubsystem();
        }

        void OnDisable()
        {
            BindSubsystem(null);
            UnregisterSink(m_ConsoleSink);
        }

        void OnValidate()
        {
            m_LogIntervalSeconds = Mathf.Max(0.05f, m_LogIntervalSeconds);
            m_SubsystemRetryIntervalSeconds = Mathf.Max(0.1f, m_SubsystemRetryIntervalSeconds);
            ApplyInspectorSettings();
            m_LogGate.Reset();
        }

        void Update()
        {
            if (m_Subsystem != null && m_Subsystem.running)
                return;

            var now = Time.realtimeSinceStartupAsDouble;
            if (now < m_NextSubsystemLookupTime)
                return;

            m_NextSubsystemLookupTime = now + m_SubsystemRetryIntervalSeconds;
            TryBindLoadedSubsystem();
        }

        void TryBindLoadedSubsystem()
        {
            var settings = XRGeneralSettings.Instance;
            var manager = settings != null ? settings.Manager : null;
            var loader = manager != null ? manager.activeLoader : null;
            var loadedSubsystem = loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null;

            if (loadedSubsystem != m_Subsystem)
                BindSubsystem(loadedSubsystem);
        }

        void BindSubsystem(XRHandSubsystem subsystem)
        {
            if (m_Subsystem != null)
                m_Subsystem.updatedHands -= OnUpdatedHands;

            m_Subsystem = subsystem;

            if (m_Subsystem != null)
                m_Subsystem.updatedHands += OnUpdatedHands;
        }

        void OnUpdatedHands(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic || !subsystem.running)
                return;

            var monotonicTime = Time.realtimeSinceStartupAsDouble;
            if (!m_LogGate.ShouldSample(monotonicTime))
                return;

            var sample = XRHandSampleReader.Capture(
                subsystem,
                monotonicTime,
                Time.frameCount,
                updateSuccessFlags);

            for (var i = 0; i < m_Sinks.Count; ++i)
                m_Sinks[i].Write(in sample);
        }

        void ApplyInspectorSettings()
        {
            m_LogGate.IntervalSeconds = m_LogIntervalSeconds;
            if (m_ConsoleSink != null)
                m_ConsoleSink.Configure(m_LogLeftHand, m_LogRightHand);
        }

        sealed class ConsoleHandTrackingSampleSink : IHandTrackingSampleSink
        {
            readonly StringBuilder m_Buffer = new StringBuilder(2048);
            readonly Object m_Context;
            bool m_LogLeft;
            bool m_LogRight;

            public ConsoleHandTrackingSampleSink(Object context)
            {
                m_Context = context;
            }

            public void Configure(bool logLeft, bool logRight)
            {
                m_LogLeft = logLeft;
                m_LogRight = logRight;
            }

            public void Write(in HandTrackingSample sample)
            {
                if (!m_LogLeft && !m_LogRight)
                    return;

                m_Buffer.Clear();
                m_Buffer.Append("[XRHands] t=").Append(sample.MonotonicTimeSeconds.ToString("F6"));
                m_Buffer.Append(" frame=").Append(sample.FrameCount);
                m_Buffer.Append(" update=").Append(sample.UpdateSuccessFlags);

                if (m_LogLeft)
                    AppendHand(m_Buffer, "Left", sample.Left);
                if (m_LogRight)
                    AppendHand(m_Buffer, "Right", sample.Right);

                Debug.Log(m_Buffer.ToString(), m_Context);
            }

            static void AppendHand(StringBuilder buffer, string label, HandSideSample hand)
            {
                buffer.Append('\n').Append(label).Append(" tracked=").Append(hand.IsTracked);
                AppendJoint(buffer, "Wrist", hand.Wrist);
                AppendJoint(buffer, "Palm", hand.Palm);
                AppendJoint(buffer, "ThumbTip", hand.ThumbTip);
                AppendJoint(buffer, "IndexTip", hand.IndexTip);
                AppendJoint(buffer, "MiddleTip", hand.MiddleTip);
                AppendJoint(buffer, "RingTip", hand.RingTip);
                AppendJoint(buffer, "LittleTip", hand.LittleTip);
            }

            static void AppendJoint(StringBuilder buffer, string label, HandJointSample joint)
            {
                buffer.Append('\n').Append("  ").Append(label);
                buffer.Append(" valid=").Append(joint.PoseValid);
                buffer.Append(" state=").Append(joint.TrackingState);
                if (!joint.PoseValid)
                    return;

                var position = joint.Pose.position;
                var rotation = joint.Pose.rotation;
                buffer.Append(" p=(").Append(position.x).Append(',').Append(position.y).Append(',').Append(position.z).Append(')');
                buffer.Append(" q=(").Append(rotation.x).Append(',').Append(rotation.y).Append(',').Append(rotation.z).Append(',').Append(rotation.w).Append(')');
            }
        }
    }
}
