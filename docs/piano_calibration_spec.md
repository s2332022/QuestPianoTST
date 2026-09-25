# Piano calibration specification

## Points

- A: top-surface front-left corner of the C4 white key; becomes the keyboard origin.
- B: top-surface front-left corner of the C5 white key; A-B is one physical octave.
- C: top-surface back-left corner of the C4 white key; A-C is its physical depth.

Use “Point Hand Left/Right” to choose the marker hand. “Register Next Point” samples that hand's
tracked index-tip joint, so the other hand can operate the UI.
All captured points are converted from XR tracking space to Unity world space.

## Calculation

`right = normalize(B-A)`. The C direction is Gram–Schmidt orthogonalized against `right` and
normalized as `depth`. `normal = normalize(cross(depth,right))`. `Quaternion.LookRotation`
maps local +Z to depth and local +Y to normal; local +X consequently maps to right. The virtual
keyboard root is assigned the calculated origin and rotation.

The measured octave span is `length(B-A)`. The measured white-key depth is the length of
`(C-A) - project(C-A, right)`. `scaleX` is the measured octave span divided by the base octave span;
`scaleZ` is the measured white-key depth divided by the base white-key depth. `scaleY` is always 1.
The base dimensions are defined once by `VirtualPianoKeyboard`: 36 mm white-key center pitch,
252 mm octave span (seven white-key intervals), 33.84 mm white-key width, and 160 mm white-key depth.
Scale limits are X 0.50–1.50 and Z 0.60–1.40. Non-finite, non-positive, and out-of-range scales
are rejected before applying any transform.

The runtime `KeyboardRoot` origin remains A and is assigned the calibration origin and rotation; the
scene object named `Piano Root` is not transformed by calibration. `KeyboardRoot.localScale` remains
one. The generated keys, meshes, and colliders are children of `Keyboard Geometry`. Only this
geometry transform scales, with local scale `(scaleX, 1, scaleZ)`. Its local position is recomputed
as `(BaseWhiteKeyWidthMeters*scaleX/2, 0, BaseWhiteKeyDepthMeters*scaleZ/2)`. At scale 1 this is the
existing `(0.01692, 0, 0.080)` m origin offset. Since a transform's own local position is not scaled
by its own local scale, this adjustment keeps the offset applied once and places the C4 top-surface
front-left corner at A. The C4 cube's local center is `(0, -0.009, 0)`, placing its top face at the
calibration plane. C5's front-left and C4's back-left corners consequently match B and C.

X and Z key spacing, key sizes, meshes, and colliders follow the geometry scale. Key animation moves
the key down 8 mm in local Y; because `scaleY` is 1, the world-space travel remains 8 mm.

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

The generated visual remains C4 through C5 (MIDI 60..72): eight white keys and five black keys.
White-key center pitch is 0.036 m. Each white cube is 0.03384 m wide, 0.018 m high, and 0.160 m
deep. Each black cube is 0.02088 m wide, 0.025 m high, and 0.0928 m deep, centered between
adjacent white keys at local Y=0.012 m and Z=0.035 m.

The canonical file is
`Application.persistentDataPath/PianoResearch/piano_calibration.json`. It is loaded on startup,
and a snapshot named `piano_calibration.json` is written into every session folder.
`formatVersion` remains 1. New data adds `scaleX`, `scaleZ`, physical octave span and white-key depth,
the base octave span and white-key depth, and `calibrationPointDefinitionVersion` (1 for the C4/C5
point meanings above). Old files without that field are treated as point-definition version 0, with
`scaleX=1` and `scaleZ=1`; their saved position and rotation are preserved. Unknown format or point
definition versions are rejected without replacing an existing valid state. Saves and session
snapshots use `CurrentAppliedCalibration`, including its scale fields, never a failed
`LastCalibrationAttempt`.

A failed three-point attempt reports its validation reason, preserves the last applied position,
rotation, and scale, and leaves the final point available for recapture. Successful calibration status
shows width and depth scales plus the measured octave span and white-key depth in millimeters.

All stored points and the applied pose are Unity world-space values. Consequently a saved calibration
is reusable only while the XR tracking coordinate frame remains spatially consistent. A guardian/
tracking-origin reset or a materially changed origin requires recalibration.
