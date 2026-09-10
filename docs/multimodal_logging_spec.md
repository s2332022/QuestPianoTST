# Multimodal logging specification

One `MonotonicSessionClock` based on `Stopwatch` is shared by XR Hands, Android MIDI, HMD pose,
and keyboard state. Starting a recording defines session time 0. Android
`elapsedRealtimeNanos` is offset-mapped into that clock domain.

CSV data is batched through a bounded, thread-safe queue and written by one background thread with
64 KiB `StreamWriter` buffers. Stop, `OnDisable`, and `OnApplicationQuit` complete the queue,
flush, and close all writers. A full queue increments `dropped_log_batches` in metadata rather
than blocking rendering.

Each session folder contains:

- `session_metadata.json`
- `hand_joints.csv`
- `midi_events.csv`
- `head_pose.csv`
- `keyboard_state.csv`
- `piano_calibration.json`
- `update_summary.csv`

`hand_joints.csv` columns:
`session_time_sec, unity_frame, callback_index, update_type, hand, hand_tracked, joint_id,
pose_valid, tracking_state, position_x, position_y, position_z, rotation_x, rotation_y,
rotation_z, rotation_w`. A synthetic `Root` row is followed by every XR Hands joint for each
hand and callback.

`midi_events.csv` columns:
`session_time_sec, event_index, device_name, event_type, channel, note_number, velocity,
control_number, control_value`.

`head_pose.csv` columns:
`session_time_sec, unity_frame, position_x, position_y, position_z, rotation_x, rotation_y,
rotation_z, rotation_w`.

`keyboard_state.csv` columns:
`session_time_sec, unity_frame, note_number, pressed, velocity, sustain_active`.

`update_summary.csv` columns:
`session_time_sec, unity_frame, callback_index, update_type, success_flags, left_tracked,
right_tracked`.

Metadata stores session id, device model, OS, Unity/OpenXR/XR Hands versions, MIDI device, piano
model, refresh rate, hand mode, calibration id, duration, git commit field, format version, and
dropped batch count. This working copy has no `.git`, so the git value is explicitly unavailable.

