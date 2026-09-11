# Distributed session logging

PC is authoritative. Start creates a UUID, checks/continues clock sync, sends Start with retries, and opens writers only after Quest ACK. Stop sends Stop with retries; ACK or retry exhaustion drains the bounded writer queue, flushes/closes all streams, and writes a summary. Root: `Application.persistentDataPath/PianoResearch/DistributedSessions/<UTC>_<uuid>`; the Host UI shows it.

Files: `session_metadata.json`; `quest_hand_joints.csv` (the required session/time/sequence/frame/callback/update/hand/tracking/joint/pose columns); `quest_head_pose.csv` (three timestamps, sequence/frame, position/quaternion); `pc_midi_events.csv` (required device/event/channel/note/velocity/control/network sequence columns); `quest_keyboard_state.csv`; `clock_sync.csv` (t0/q1/q2/t3/RTT/offset/selected/index); `network_diagnostics.csv` (required direction/type/sequence/bytes/results/order/drop/queue columns); `session_summary.json`.

CSV streams stay open on one background writer thread and flush periodically and at stop. The 65,536-line bounded queue never blocks XR/network main-thread work; overflow is a critical diagnostic and summary counter.

