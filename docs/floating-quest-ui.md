# Floating Quest research UI

## Scope and existing system

Quest scene: `Assets/Research/Scenes/PianoDistributedQuest.unity`.
`DistributedQuestUi` builds a single world-space Canvas at runtime. Its scene object is a world root, separate from Piano Root, KeyboardRoot and XR Origin. BLE diagnostics attach their controls later at runtime. The host UI uses OnGUI and is unchanged.

Input remains the imported XRI 3.5.1 Right Near-Far Interactor (near disabled, far/UI enabled), XRUIInputModule and TrackedDeviceGraphicRaycaster. MinimalHandUiInputModule remains diagnostic-only. No EventSystem, camera, interactor, research service or Unity package is added.

Existing IP/display-mode persistence remains in PianoResearch/distributed_host_ip.json. Research session recording, calibration, pose transforms, protocol v1 and correction algorithms are unchanged.

## Layout

The normal header shows MIDI transport, tracking, calibration, raw recording and the live CorrectionSettings / processor status. DETAIL SETTINGS opens five pages:

1. PC / CALIBRATION: IP keypad, connect/disconnect, A/B/C, save, cancel/retry, 13/88 keys.
2. BLE MIDI: permissions, scan, stop, device selection, connect/disconnect, full diagnostic message.
3. CORRECTION: enabled, hand, finger, display mesh, strength, maximum correction, pad offset and surface band.
4. LOG / DIAGNOSTICS: complete network/log/correction errors, legacy pointer diagnostics, GPU/GameObject hand display.
5. UI POSITION: face user, reset, nearer/farther and configurable distance limits.

START/STOP RAW REC and TIP ON/OFF remain in normal mode. Compact mode changes visibility only. Every original operation is retained. Existing PerformanceUiSafety MIDI lock, release requirement and confirmation callbacks are reused by new buttons. Long diagnostics and calibration messages use official ScrollRect, RectMask2D and ContentSizeFitter. Text sizes are fixed; button labels are checked with TMP layout, not auto-shrunk.

The former correction/BLE panels occupied the same (740,-20) rectangle; legacy pointer diagnostics overlapped calibration status, and the last main button extended beyond the 720-unit root. Page membership and explicit nonoverlapping slots remove those conflicts. Root height changes preserve the header's world position. The IP keyboard is a centered popup, inheriting root placement and MIDI lock.

Noto Sans JP is a runtime TMP fallback for Japanese device names and diagnostics. Source: https://github.com/google/fonts/tree/main/ofl/notosansjp . The unmodified font and OFL are bundled under Resources/QuestUi; the font license is included in the player. No Unity package was added. Original TMP atlas/material assets remain unchanged. The Japanese fallback is created once at runtime, reference counted across UI instances, and its in-memory fallback membership is restored when the final UI is destroyed.

## Move and recover

Aim the ray at HOLD RAY + PINCH TO MOVE, hold the existing UI selection, move, then release. Only QuestPanelMoveHandle accepts pointer-down/drag events. Buttons, sliders and scroll views have no panel drag ancestor. A confirmation or open keypad prevents starting a move; MIDI lock cancels an active move. Release, tracking loss, disable and pause stop movement and clear selection.

On pointer-down the adapter captures the root world position, ray depth and offset. Translation is the difference of ray points at that captured depth, including hand origin translation. The start pose never snaps to the ray. Radial head distance is clamped; exponential smoothing (18/s default) runs only while selected. Rotation is unchanged during drag. Nearer/farther buttons provide reliable depth changes of 0.1 m. Default limits are 0.45-2.5 m and can be adjusted in UI POSITION / serialized settings.

XRI 3.5 sets screen delta to zero when no Graphic is hit, and may omit OnDrag in this case. While the XRI pointer press remains selected, the handle reads that same official TrackedDeviceEventData in LateUpdate. Selection and release still come from the existing XRI input module; no custom pinch or pointer system is installed. This small adapter is needed for free panel movement outside the old panel rectangle.

Canvas text faces local -Z, so its +Z points away from the viewer. Initial placement uses HMD horizontal forward, world up, 1.05 m forward, 0.15 m right and 0.05 m above eye position. It has zero pitch/roll and does not inherit keyboard tilt. Main panel lower edge is above the default keyboard height. FACE USER preserves position and computes yaw from viewer to panel. Placement remains world fixed after release, including subsequent keyboard calibration or HMD movement.

RECENTER UI returns the panel to the current HMD front and saves it. RESET PLACEMENT clears the saved placement and returns to a safe default without immediately saving a replacement. The existing Quest system recenter tracking-origin event is also a recovery path when the panel is out of view. That physical recovery path must be verified on Quest.

## Persistence

Path: Application.persistentDataPath/QuestUi/placement-v1.json, separate from research/IP files.

Schema: version=1, coordinateSpace=KeyboardLocal, reference=CalibratedKeyboardRoot, calibrationId, local position and local rotation. The independent UI root is never parented to KeyboardRoot; the calibrated root is only a read reference. Store rotation using inverse keyboard rotation; store position using InverseTransformPoint. Current reference transforms are applied once on restore, avoiding any assumption of persistent session world coordinates.

Only a valid applied keyboard calibration enables persistent relative placement. Without it, placement remains usable for this session and the next launch starts in front; the UI explains this. A changed calibration ID rejects restoration. Missing/malformed/unsupported data, nonfinite or unnormalized values, unavailable calibration, distance outside current limits, a rear/out-of-view pose or an inverted panel all fall back to the current HMD front. Save exceptions also restore a safe front position and are shown in UI POSITION.

## Verification and physical limits

Focused tests exercise actual XRUIInputModule -> tracked raycaster -> handle selection/movement/release/reselection, tracking loss, MIDI cancellation, confirmations, button isolation, TMP layout, Japanese glyph generation, long scroll content, UI modes and position persistence including translated/rotated reference origins.

Run focused EditMode, focused PlayMode, then all EditMode/PlayMode tests before building. Use the existing PianoDistributedSceneBuilder.BuildAndroidValidation builder (Android / IL2CPP / ARM64 / Quest scene only).

Editor RenderTexture previews are available in the external validation directory. They support layout review; they do not establish headset readability, pinch usability, real keyboard clearance, actual BLE/UDP connectivity or spatial-origin behavior. No Quest was connected during development. Verify these with the final APK, including application restart with calibrated placement and Quest system recenter. Detail mode deliberately uses a taller panel; close it or use compact mode during performance.
