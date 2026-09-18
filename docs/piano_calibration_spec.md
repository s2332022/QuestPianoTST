# Piano calibration specification

## Points

- A: front-left edge of the reference white key; becomes the keyboard origin.
- B: a point to the right along the keyboard.
- C: a point toward the rear of the keyboard.

Use “Point Hand Left/Right” to choose the marker hand. “Register Next Point” samples that hand's
tracked index-tip joint, so the other hand can operate the UI.
All captured points are converted from XR tracking space to Unity world space.

## Calculation

`right = normalize(B-A)`. The C direction is Gram–Schmidt orthogonalized against `right` and
normalized as `depth`. `normal = normalize(cross(depth,right))`. `Quaternion.LookRotation`
maps local +Z to depth and local +Y to normal; local +X consequently maps to right. The virtual
keyboard root is assigned the calculated origin and rotation.

The runtime `KeyboardRoot` origin remains A and is assigned the calibration origin and rotation; the
scene object named `Piano Root` is not transformed by calibration. The generated keys are children of
`Keyboard Geometry`, which is a thin geometry parent under `KeyboardRoot`. Its local position is the
fixed model offset `(0.01692, 0, 0.080)` m and its local scale is one. The C4 white-key cube keeps
its existing local position `(0, 0, 0)` under that parent, so its center is at the offset from
`KeyboardRoot` and its front-left edge coincides with A. The offset is applied once to the geometry
parent; key dimensions and all key-to-key local spacing remain unchanged.

Any pair of points closer than 5 cm is rejected. Axes within 10 degrees of parallel or antiparallel
are rejected, as is a degenerate orthogonalized depth. `LastCalibrationAttempt` stores the latest
successful or failed result. On rejection, `LastValidCalibration` and `CurrentAppliedCalibration`
remain unchanged and `CalibrationChanged` is not emitted. `Current` remains a compatibility alias
for `CurrentAppliedCalibration`, so the keyboard and the last valid data remain available for saving.
An explicit `ClearCalibration` call is required to remove the valid in-memory state.

An orientation whose calculated normal points below Unity world-up is rejected as mirrored. Quality
is a 0..1 score: the minimum of point-separation quality (5 cm is 0, 20 cm is 1) and angular quality
(10/170 degrees is 0, 90 degrees is 1). Labels are `Low` (<0.4), `Acceptable` (<0.75), and `Good`.

## Virtual keyboard geometry

The generated visual is C4 through C5 (MIDI 60..72): eight white keys and five black keys. White-key
center pitch is 0.036 m. Each white cube is 0.03384 m wide, 0.018 m high, and 0.160 m deep. Each
black cube is 0.02088 m wide, 0.025 m high, and 0.0928 m deep, centered between adjacent white keys
at local Y=0.012 m and Z=0.035 m. Calibration changes position and rotation only; it never changes
scale.

The canonical file is
`Application.persistentDataPath/PianoResearch/piano_calibration.json`. It is loaded on startup,
and a snapshot named `piano_calibration.json` is written into every session folder.
Saved fields are `formatVersion` (currently 1), ID/time/validity, A/B/C, origin, right/depth/normal,
rotation, minimum point distance, source-axis angle, quality score/label, and validation message.
Legacy files without a version deserialize as version 0 and are migrated in memory to version 1;
unknown versions are rejected without replacing an existing valid in-memory state. Saves and session
snapshots use the currently applied valid calibration, never a failed `LastCalibrationAttempt`.

All stored points and the applied pose are Unity world-space values. Consequently a saved calibration
is reusable only while the XR tracking coordinate frame remains spatially consistent. A guardian/
tracking-origin reset or a materially changed origin requires recalibration.
