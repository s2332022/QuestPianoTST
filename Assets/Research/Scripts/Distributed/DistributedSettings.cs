using System;
using UnityEngine;

namespace QuestPianoMotion.Research.Distributed
{
    public enum ResearchExecutionMode { StandaloneQuest, DistributedQuestClient, DistributedPcHost, EdgeAiQuest }
    public enum PoseSendPolicy { DynamicOnly, BeforeRenderOnly, Both }
    public enum PoseDisplaySource { LocalRaw, PcDisplayPose }
    public enum DistributedLogVerbosity { ErrorsOnly, Normal, Detailed }

    [DisallowMultipleComponent]
    public sealed class DistributedSettings : MonoBehaviour
    {
        [Header("Explicit mode (never inferred from platform)")]
        public ResearchExecutionMode executionMode = ResearchExecutionMode.StandaloneQuest;
        [Header("Network")]
        public string pcIpAddress = "192.168.1.2";
        [Min(1)] public int pcReceivePort = 50000;
        [Min(1)] public int questReceivePort = 50001;
        [Min(1)] public int clockSyncPort = 50002;
        [Min(1f)] public float poseSendHz = 60f;
        [Min(0.25f)] public float connectionTimeoutSeconds = 3f;
        [Min(0.1f)] public float heartbeatIntervalSeconds = 1f;
        [Min(1f)] public float clockResyncIntervalSeconds = 5f;
        [Min(1)] public int initialClockSyncSamples = 10;
        [Min(1)] public int controlRetryCount = 5;
        [Min(0.05f)] public float controlRetrySeconds = 0.35f;
        [Header("Pose")]
        public PoseSendPolicy poseSendPolicy = PoseSendPolicy.DynamicOnly;
        public bool sendLeftHand = true;
        public bool sendRightHand = true;
        public bool sendHeadPose = true;
        public PoseDisplaySource displaySource = PoseDisplaySource.LocalRaw;
        [Header("Queues / diagnostics")]
        [Min(8)] public int poseQueueCapacity = 64;
        [Min(32)] public int midiQueueCapacity = 2048;
        [Min(8)] public int controlQueueCapacity = 128;
        public DistributedLogVerbosity logVerbosity = DistributedLogVerbosity.Normal;

        public void ValidateRuntime()
        {
            pcReceivePort = ClampPort(pcReceivePort); questReceivePort = ClampPort(questReceivePort);
            clockSyncPort = ClampPort(clockSyncPort); poseSendHz = Mathf.Clamp(poseSendHz, 1f, 120f);
            connectionTimeoutSeconds = Mathf.Max(0.25f, connectionTimeoutSeconds);
            heartbeatIntervalSeconds = Mathf.Max(0.1f, heartbeatIntervalSeconds);
        }
        static int ClampPort(int value) => Math.Max(1, Math.Min(65535, value));
    }
}
