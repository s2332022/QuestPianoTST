# Distributed processing implementation plan

Date: 2026-09-11

## Repository baseline

- Branch: `master`
- HEAD: `1f8ba9b1d412361804dbc7ff91964dd8014d2c23` (`Backup before creating minimal test environment`)
- Unity: 6000.5.10f1
- Existing MIDI dependency: none; Android MIDI uses `QuestMidiBridge.java`.
- PC MIDI implementation: Windows Multimedia MIDI (`winmm.dll`) through a small platform-gated P/Invoke bridge. No package or ProjectSettings change is required.

## Preservation boundary

Existing modified/untracked work discovered before implementation is retained. In particular, this change does not edit the existing scenes, Packages, ProjectSettings, package samples, calibration, virtual keyboard, minimal runtime, or existing tests. Distributed code is additive. `AndroidMidiInput` only gains an explicit `IMidiInput` implementation; its bridge and standalone behavior are retained.

## Planned additions

- `Assets/Research/Scripts/Distributed/`: execution settings, binary protocol, bounded queues, sequence diagnostics, clock synchronization, UDP transport, Quest client runtime, PC host runtime, MIDI inputs, background session logging, and minimal status UIs.
- `Assets/Research/Editor/PianoDistributedSceneBuilder.cs`: deterministic creation and validation of the two new scenes and build-validation entry points.
- `Assets/Research/Scenes/PianoDistributedQuest.unity`
- `Assets/Research/Scenes/PianoDistributedHost.unity`
- New EditMode and PlayMode distributed tests in separate files.
- Requested distributed architecture, protocol, clock, MIDI, logging, device-test, limitations, and Edge AI migration documents.

## Verification plan

1. Compile scripts in Unity batch mode.
2. Generate and validate both new scenes.
3. Run EditMode tests, then PlayMode tests.
4. Run Windows host build and Android Quest build without changing build settings.
5. Inspect Git diff/status and confirm protected files are unchanged by this implementation.
