# QuestPianoMotion Agent Rules

## Primary policy

Before implementing anything:

1. Search the current project for reusable code and assets.
2. Check installed official packages.
3. Check imported official samples, prefabs, and components.
4. Prefer maintained official functionality over custom implementation.
5. Implement only the missing research-specific behavior.
6. If custom implementation is necessary, report why existing options were insufficient.

Do not add external packages without explicit user approval.

## Project purpose

This project studies MIDI-assisted correction of markerless hand poses during piano performance.

The research-specific areas are:

- XR hand pose and MIDI synchronization
- Tracking-quality evaluation
- Fingering estimation
- Selective hand-pose correction
- Session recording and evaluation metrics
- PC versus edge inference comparison

General XR, UI, interaction, input, and inference functionality should use existing official implementations whenever practical.

## Reuse priority

Use the following priority order:

1. Existing project implementation
2. Installed Unity or Meta official package
3. Imported official sample or prefab
4. Maintained library with compatible license
5. Thin adapter around existing functionality
6. New custom implementation only as a last resort

## Current standard components

Prefer the existing project configuration:

- OpenXR
- XR Hands
- XR Interaction Toolkit
- XRI Near-Far Interactor
- XR UI Input Module
- Tracked Device Graphic Raycaster
- Input System
- TextMesh Pro

Do not replace standard XRI UI interaction with a new custom pointer or pinch implementation.

The legacy MinimalHandUiInputModule is diagnostic fallback only.

## Protected areas

Do not modify these unless the task explicitly requires it:

- Packages/
- ProjectSettings/
- Assets/Research/Scenes/HandTrackingResearch.unity
- Assets/Research/Scenes/PianoMinimalTest.unity
- Assets/Research/Scenes/PianoDistributedHost.unity
- UDP protocol version 1
- Existing packet header format
- Existing CSV columns
- StandaloneQuest behavior
- WindowsHostXrBuildGuard
- QuestMidiBridge.java

Do not change package versions or install packages without first reporting:

- Package name
- Version
- Provider
- License
- Unity 6 compatibility
- Android and IL2CPP compatibility
- Quest 2 compatibility
- Existing-package conflicts
- Reason the current project cannot satisfy the requirement

## Change policy

Keep every task narrow.

Before editing:

1. Inspect the relevant files.
2. Report reusable existing functionality.
3. Identify the minimum files that require modification.
4. Check whether an official component satisfies the requirement.

During editing:

- Modify only files relevant to the requested task.
- Avoid unrelated cleanup.
- Avoid large refactoring.
- Preserve existing public behavior unless explicitly requested.
- Do not alter protocol or log compatibility without explicit approval.
- Do not treat physical headset behavior as verified without user confirmation.

## Unity scene policy

- Do not edit unrelated scenes.
- Do not save over a scene unless it is an approved target.
- Do not add duplicate EventSystems, Cameras, XR Interaction Managers, or runtime services.
- Use one active UI Input Module.
- Prefer existing scene builders and validators.
- Build only the scene appropriate to the selected execution mode.

## Coordinate-space policy

Never leave coordinate space implicit.

Use explicit names or metadata for:

- TrackingOriginLocal
- World
- WristLocal
- KeyboardLocal

Do not:

- Apply XR Origin transforms twice
- Apply Camera Offset transforms twice
- Assign world coordinates to localPosition
- Assign local coordinates directly to world position
- Add empirical offsets without identifying the coordinate-system cause

## Hand-pose policy

Use XR Hands official data access.

Recommended responsibilities:

- Dynamic update: research logic, logging, networking, synchronization
- BeforeRender update: final visual update only

HandPoseFrame is reusable mutable data.

Do not retain its reference in an asynchronous queue or long-lived buffer.
Serialize or copy required values before asynchronous processing.

## MIDI policy

MIDI is the authoritative source for key state and timing.

Preserve:

- Note On
- Note Off
- Velocity
- Channel
- CC64
- Note On with velocity zero normalized to Note Off
- All Notes Off behavior during disconnects

Do not replace the existing Windows winmm or Android MIDI paths without explicit approval.

## Calibration policy

The physical calibration points are:

- A: front-left edge of the reference white key
- B: point to the right of A along the keyboard
- C: point behind A along the keyboard depth

Preserve the three-point calibration as a manual fallback.

Do not change keyboard dimensions, calibration axes, scale, or Piano Root placement based on assumptions.
Any calibration change must include focused mathematical tests.

Failed calibration attempts must not destroy the last valid applied calibration.

## Networking policy

Preserve UDP protocol version 1 compatibility.

- Pose data may prioritize recent data over old data.
- MIDI and session-control loss must be handled explicitly.
- Do not replace UDP with TCP without explicit approval.
- Preserve heartbeat, sequence diagnostics, clock synchronization, and disconnect reset behavior.

## Testing workflow

Use this order:

1. Static inspection
2. Relevant focused EditMode tests
3. Relevant focused PlayMode tests
4. Full EditMode and PlayMode tests once before build
5. Platform build
6. Physical-device verification by the user

Do not run the full test suite after every small edit.

Do not rebuild Android or Windows when the change cannot affect that platform.

If a test fails:

- Report the failing test
- Report the relevant error
- Fix only the identified failure
- Do not perform unrelated improvements

## Build workflow

Use the existing project builders where available.

Quest client:

- Scene: PianoDistributedQuest.unity only
- Platform: Android
- Scripting backend: IL2CPP
- Architecture: ARM64

Windows host:

- Scene: PianoDistributedHost.unity only
- Platform: Windows x64
- XR initialization: disabled

Preferred output paths:

- Builds/Android/PianoDistributedQuest.apk
- Builds/WindowsHost/PianoDistributedHost.exe

After a build, report only:

- Success or failure
- Output path
- File size
- Last-write time
- SHA-256
- Included scene
- Relevant warnings

## Git safety

Never run:

- git reset --hard
- git clean -fd
- destructive checkout
- forced deletion of uncommitted work
- automatic commit without explicit instruction

Before modifying files, inspect:

- git status --short
- current branch
- uncommitted changes

Preserve all existing uncommitted changes.

## Output policy

Keep reports concise.

Unless more detail is requested, report only:

1. Reused existing functionality
2. Root cause
3. Changed files
4. Tests run and results
5. Build result
6. Remaining physical-device checks

Do not repeat the full project history in every response.

## Physical-device verification

Do not claim the following as successful without user observation or device logs:

- Hand visibility
- Hand alignment
- Ray visibility
- Pinch usability
- Physical keyboard alignment
- Quest-to-PC connectivity
- Perceived latency
- Tracking recovery

Mark these as unverified when a headset is unavailable.
