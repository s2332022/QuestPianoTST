# Distributed architecture

## Responsibilities and modes

`DistributedSettings.executionMode` is explicit; platform detection never selects a mode. `StandaloneQuest` keeps the existing Android USB MIDI path. `DistributedQuestClient` captures XR Hands/HMD, sends raw pose, receives MIDI/control/corrected-pose frames, and renders the existing 13-key keyboard. `DistributedPcHost` owns Windows MIDI, clock sync, session control, diagnostics, and durable logs. `EdgeAiQuest` is reserved and intentionally has no inference implementation.

PC data flow: Windows MIDI -> `PcMidiInput` -> binary `MidiPacket` -> UDP -> Quest `NetworkMidiInput` -> existing `KeyboardStateTracker` / `VirtualPianoKeyboard`. Quest data flow: `XRHandPoseProvider` raw frame + HMD -> chunked `PosePacket` -> PC identity processing boundary -> logging and optional identity `CorrectedPose` echo.

## Standalone difference and fallback

Standalone uses unchanged `AndroidMidiInput` and `QuestMidiBridge.java`; distributed mode never initializes them. Network callbacks, MIDI callbacks, and file writes do not touch Unity objects. A disconnected Quest keeps XR rendering, releases every network-owned key, displays Disconnected, and forces the pose processor to local Raw. A disconnected PC continues MIDI callbacks, records diagnostics when a session is active, preserves open logs, and accepts a later Quest sequence range.

## Edge migration

Keep capture, `IHandPoseProcessor`, keyboard state, protocol, and session schemas stable. Replace Identity on PC with a future rule/AI processor for distributed comparison, then export the trained model to ONNX and implement `EdgeAiQuest` with Unity Inference Engine only in a separately approved phase. Switching back requires selecting `StandaloneQuest` and reconnecting USB MIDI to Quest; no distributed asset replaces the standalone scene.

