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

Any pair of points closer than 5 cm is rejected. Axes within 10 degrees of parallel or antiparallel
are rejected, as is a degenerate orthogonalized depth. The last valid calibration remains usable
until replaced.

The canonical file is
`Application.persistentDataPath/PianoResearch/piano_calibration.json`. It is loaded on startup,
and a snapshot named `piano_calibration.json` is written into every session folder.
