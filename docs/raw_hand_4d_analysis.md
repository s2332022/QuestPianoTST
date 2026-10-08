# 4D Raw hand research logging and analysis

## Scope and reuse

This phase records spatial coordinates x/y/z and a separate time value t. It never computes a four-dimensional Euclidean distance and never applies hand correction, IK, smoothing, MIDI attraction, wrist constraints, or pressed/nonpressed constraints.

The implementation reuses XRHandPoseProvider.RawFrameUpdated, ResearchServices.Clock, official XR Hands poses, existing BLE normalization and diagnostic timestamps, UDP receive timestamps, existing calibration, keyboard geometry, session control, and PianoDistributedSceneBuilder.BuildAndroidValidation. Installed XR Hands 1.8.1, XRI 3.5.1, OpenXR 1.17.1, Meta OpenXR 2.5.1 and imported official samples provide acquisition/interaction but do not provide this research-specific synchronized recording and offline analysis. No new packages were installed.

Existing quest_hand_joints.csv, PC MIDI CSVs, protocol_version=1, UDP payloads, calibration snapshot, and StandaloneQuest recording behavior remain compatible. Additional files below are Quest-side distributed-client research files, including manual local sessions without a PC. Old sessions without contemporaneous transforms cannot be accurately reconstructed: the tool refuses missing snapshots instead of assuming identity.

## Coordinates

Tracking T is exactly the raw XR Hands pose in ResearchServices.TrackingOrigin local coordinates. Existing FindTrackingOrigin selects XROrigin.CameraFloorOffsetObject, falling back to Origin/camera parent. World W is Unity world space. Keyboard K is KeyboardRoot local space.

`pW = TrackingOrigin.localToWorldMatrix * pT`

`pK = KeyboardRoot.worldToLocalMatrix * pW`

No Camera Offset or XR Origin transform is applied twice. Matrices include ancestors and scale; row-major arrays multiply homogeneous column vectors. Snapshot position/rotation are world values, localScale is the actual local scale. Full matrices are authoritative when ancestors introduce scale/shear. Missing or singular transforms fail recording/analysis explicitly.

| KeyboardRoot axis | Physical direction | Paper term |
|---|---|---|
| +X | Right, A toward B, along keys | lat |
| +Y | Up, away from key surface | normal |
| +Z | Rear, A toward C, along key depth | depth |

These existing axes and calibration are unchanged. Calibration scaleX/scaleZ and origin offset belong to KeyboardGeometry, a child of KeyboardRoot. The recorder saves both transforms and each key's *rest* bounds in KeyboardRoot local coordinates, including black-key height. Keyboard units are metres with the project's unit KeyboardRoot scale; arbitrary root scaling changes world metre equivalence. Geometric results use KeyboardRoot local units.

## Time and validity

- Hand `quest_timestamp_sec` is ResearchServices.Clock.AbsoluteSeconds (Stopwatch.GetTimestamp/Frequency) sampled once on the XR Hands callback, before capture. It is the frame acquisition/observation time, not a hardware sensor exposure timestamp. Every joint in a callback shares that value and callback_index. Unity frame and Dynamic update type are retained. Only Dynamic callbacks enter the additional research CSV, even if BeforeRender network capture is enabled.
- pose_valid, hand_tracked, tracking_state and success_flags are preserved. Analysis valid means pose_valid AND hand_tracked. Invalid raw values stay in the derived table; world/keyboard/velocity values are blank for invalid samples. Loss rate is invalid joint rows / observed Dynamic rows, not a measure of periods with no subsystem callbacks.
- BLE `quest_receive_timestamp_sec` maps Java System.nanoTime receive nanos into the same Quest Stopwatch clock through existing synchronization. `sender_timestamp_sec` preserves the existing message time (Direct GATT normally also based on receive time). `ble_timestamp_13bit_ms` preserves the wrapped 0..8191 sender timestamp; it is not directly subtracted from Quest time. connection_generation and synthetic disconnect releases are retained. Existing BLE diagnostics remain available with raw nanos and clock synchronization details.
- UDP `quest_receive_timestamp_sec` is the existing Quest socket receive timestamp. `sender_timestamp_sec` stays in the independent PC clock. Network/scheduling delay is included in Quest receive timing. The protocol is unchanged. Quest receive time is used for Hand/MIDI comparisons.
- MIDI `quest_observed_timestamp_sec` is Unity processing time, separate from transport receive time. MIDI snapshot_id refers to transforms at this observation, not a retrospective claim about transforms on the Java/socket thread.
- `start_utc` is a separate wall-clock anchor. Seconds are monotonic absolute values within the Quest process/boot clock domain, not UTC or seconds since recording start. Equal successive timestamps are allowed; decreasing values are rejected. Finite differences require dt>0 and consecutive valid samples in the same transform snapshot.

MIDI Note On velocity=0 is normalized to Note Off. Note On is **not physical contact time**. `time_offset_ms = (Quest MIDI receive time - selected hand sample time)*1000`; it includes transport, scan and scheduling effects. The selected sample is a finite key-top proximity candidate inside a configurable time window, not proof of contact or a definitive finger assignment.

## Session files

Location: `Application.persistentDataPath/PianoResearch/DistributedSessions/<session_guid_without_hyphens>_Quest/`.

- session_metadata.json: retains existing metadata and calibration. Adds research_log_version, TrackingOrigin and KeyboardRoot position/quaternion/localScale/full row-major matrices, coordinate definitions, axis mapping, timing definitions, device_model, git_branch, snapshot file/policy and zero-offset pad model. Existing runtime_versions and Git commit/dirty remain. The build captures branch along with existing package versions. Build metadata JSON is generated by the existing builder and restored afterwards; do not commit the local generated asset accidentally.
- quest_hand_raw.csv: additional immutable Tracking-space joint observations with session_id, shared Quest timestamp, frame/callback/update, hand/tracked flag, joint ID/name, pose validity, tracking state, raw position and quaternion, snapshot_id and success_flags. No raw coordinate is replaced with World/Keyboard values.
- quest_midi_research.csv: source BLE/UDP, receive and sender times, BLE sender timestamp when available, generation, event index/type/channel/note/velocity/CC, synthetic flag, snapshot_id and Unity observation time. BLE NoteOn/Off/CC events include synthetic releases. UDP records accepted fresh MIDI packets; existing network diagnostics/state snapshots still govern transport repairs.
- transform_snapshots.jsonl: initial snapshot, then changes in exact TrackingOrigin, KeyboardRoot, KeyboardGeometry matrices or key count, checked at every logged Dynamic callback/MIDI observation. Contains monotonically assigned snapshot_id, Quest time, transform descriptors and key rest bounds. Every hand/MIDI row references the exact captured snapshot. Metadata stores only initial transforms, keeping JSON compact. Parent movement, recentering, calibration and display-mode changes are covered. Exact matrix checks prioritize traceability over compression.
- research_recording_summary.json: reason, writer_closed, queue_overflows and error. Queue is bounded; main thread serializes/copies mutable frames before background writes. Never retains HandPoseFrame in the queue. A full queue/writer failure marks the session incomplete, is visible via RAW status, and is rejected by the analysis tool. Force-closing the app may leave no summary; that is also rejected.

PC Start/Stop controls start/stop the Quest research files with the same session ID. Local START/STOP RAW REC operates independently of PC connectivity. A host Start finalizes a local session before opening the host session. STOP RAW REC stops only a local session; host recording is stopped from the host. PC disconnect closes a host-controlled recording; local recording survives PC disconnect. Stop recording before suspending the headset and inspect the final summary after retrieval.

## Offline use

Python 3 standard library only. Keep output outside the source session directory.

```powershell
python tools/analyze_raw_session.py C:\ResearchData\SESSION_Quest --output C:\ResearchData\analysis\SESSION
python tools/analyze_raw_session.py C:\ResearchData\SESSION_Quest --output C:\ResearchData\analysis\SESSION --static 123.5 128.5 --window 0.15 --pad-offset 0 0 0
python tools/test_analyze_raw_session.py
```

`--static START END` uses **absolute Quest timestamps**, is repeatable and must be labelled by the operator. Without static intervals, static_jitter.csv contains only a header: deliberate movement is not automatically called jitter. A zero-sample interval produces no jitter row. Long sessions are loaded in memory; allow sufficient PC RAM and use separate short measurement sessions. Default alignment half-window is 150 ms. `--pad-offset x y z` rotates an explicit future pad offset by the raw joint quaternion before coordinate conversion; default zero does not assume FingerTip is a physical contact point. Raw quaternions remain in derived rows; transformed quaternions are not invented under scaled/sheared transforms.

Outputs:

| File | Meaning |
|---|---|
| session_summary.csv | Session counts, duration, parameters and external_ground_truth=False |
| joint_summary.csv | All available joints: valid counts/loss, Keyboard mean/population std x/y/z, raw/world mean/std, mean/max speed and velocity sample count |
| derived_hand_coordinates.csv | Per-observation raw/world/keyboard positions, raw quaternion, pad positions, per-axis joint/pad velocity and joint acceleration; finger trajectories included |
| static_jitter.csv | Per operator-labelled static interval and transform snapshot: population std x/y/z |
| bone_lengths.csv / bone_summary.csv | Adjacent anatomical chain lengths in Tracking space, temporal std/range; only both-valid observations |
| midi_hand_alignment.csv | Every recorded hand/finger candidate for each nonsynthetic positive-velocity NoteOn; nearest finite top rectangle sample, delta t, normal velocity and geometry metrics |
| midi_hand_trajectories.csv | Candidate finger trajectories in the event window, relative times and normal velocity |
| key_penetration.csv | Tip/pad penetration within finite key rest volumes; pressed / observed_released / unknown MIDI state |
| analysis_manifest.json | Source SHA-256 hashes, static intervals, pad offset, window and interpretation/limitations |

At least Wrist, Palm and all five tips are supported; all logged joints and MCP/PIP/DIP equivalents (Proximal/Intermediate/Distal) can be analyzed. Acceleration is an optional finite difference, can amplify noise and is not smoothed. Summaries of moving sessions describe motion distributions, not static jitter. Coordinate derivatives are deliberately blank across transform transitions or invalid observations.

## Interpretation and limitations

A: tracking repeatability/jitter; B: keyboard geometric consistency; C: MIDI timing consistency; D: anatomical/bone consistency. None is true Quest 3D position error or external-ground-truth accuracy. True spatial accuracy, absolute bias and true physical contact timing require an independent measurement system.

Geometric metrics use recorded key rest bounding boxes: signed normal surface distance, lateral/depth overhang and penetration only inside the finite volume. They are reference geometry, not measurements of the physical depressed key surface. Virtual key animation is not used as ground truth. Black/white bounding boxes can overlap; occlusion and real key contours are not resolved. Nonpressed `observed_released` is limited to observed source/channel states; absence of events at session start is `unknown`, and unobserved owners may still be unknown. Synthetic disconnect releases and BLE generation changes invalidate the observed source state; they are not evidence of physical release. CC64 does not imply a physical key remains down. No finger is conclusively assigned to a note.

## Quest 2 device procedure (requires user observation)

1. Install the APK built by existing PianoDistributedSceneBuilder.BuildAndroidValidation; Android / IL2CPP / ARM64, only PianoDistributedQuest scene. Enable hand tracking and grant BLE permissions. Keep existing BLE setup instructions in QuestBleMidiDiagnostics.md.
2. Check passthrough calibration A/B/C, preserve/apply the valid calibration, 88-key display and white separators. Confirm both hand visual modes, tracking recovery and physical keyboard alignment by observation.
3. With PC disconnected, connect WU-BT10 Direct GATT BLE. Start RAW REC; confirm RAW REC status without error. Hold both hands still for a labelled ~5 s interval, move all fingers, temporarily hide a hand, play notes/chords including velocity=0 releases, sustain, and disconnect/reconnect BLE. Confirm keyboard release/animation and BLE generation changes.
4. While recording, recenter and reapply calibration or change keyboard display mode once. Stop RAW REC. Retrieve the whole session and existing BLE diagnostics using docs/log_retrieval.md and inspect writer_closed=true, queue_overflows=0, error=null. If the app was killed before Stop, repeat instead of treating the partial session as complete.
5. Validate metadata device model, Unity/package versions, Git branch/hash/dirty, retained calibration, TrackingOrigin Camera Floor Offset and KeyboardRoot snapshots. Verify matrices, localScale and snapshot IDs match recenter/calibration transitions and hand timestamps never decrease. All joints in each callback must share one Quest timestamp.
6. Run offline analysis above; inspect derived positions versus physical axis motions (+X right, +Y up, +Z rear), invalid rows, static jitter, bone consistency, all ten candidate tip trajectories, MIDI receive times and BLE 13bit values. Do not interpret NoteOn as physical contact or assign the nearest candidate as confirmed fingering.
7. Repeat with PC MIDI -> UDP; start/stop the existing host session. Confirm PC and Quest session IDs match, legacy PC CSVs still exist, UDP source timestamps differ from Quest receive timestamps, key animation/CC64/snapshot repair work, disconnect finalizes host recording, and local BLE recording can still work independently.
8. Run a representative-duration session and monitor storage, RAW error, writer closure, frame rate and perceived latency. These performance and physical-device observations remain unverified until Quest logs/user observation are available.

## Next phase: fingering estimation

Build a separate estimator over derived_hand_coordinates.csv and midi_hand_trajectories.csv, preserving candidate_status=unassigned as input. Use recorded geometry, pad model, validity, velocity and timing uncertainty. Add confidence/provenance and source/event/generation keys in new outputs. Do not overwrite Raw or silently turn candidate_finger into truth. Runtime integration can subscribe to XRHandPoseProvider.RawFrameUpdated and BleMidiInput.ResearchEvent / UDP receive after validating the offline estimator. Correction remains a separate later component.

## Git operation

Before substantial changes, create a checkpoint branch/tag. Implement -> focused tests -> full project EditMode/PlayMode -> existing platform builder -> device checks -> explicitly add only related files -> commit -> push the existing GitHub remote. Never add all files, force push, reset hard or commit generated Builds/Temp/Recovery/Screenshots/logs. Inspect status, staged diff and remote before push; inspect status and log after push. Record device checks as unverified until observed.
