# Virtual keyboard specification

`VirtualPianoKeyboard` creates MIDI notes 60 through 72 (C4–C5, inclusive) at runtime. Each
`PianoKeyView` stores the MIDI note number, black/white classification, rest position, pressed
position, pressed state, velocity, and visual state.

Note On moves a key down 8 mm and changes its color/emission according to velocity. Note Off (or
Note On velocity 0) returns it to rest. Velocity is visual-only and is never used to alter hand
poses. Per-note 16-bit channel masks make Note On idempotent on a channel, keep chords independent,
and allow the same note on different channels without premature release.

Sustain CC64 is stored and logged separately. It does not keep a physically released virtual key
down. Audio note lifetime is intentionally outside this phase.

The current generated range is controlled by `FirstNote` and `LastNote`; state storage already
covers all 128 MIDI notes, so an 88-key visual can be substituted without changing MIDI or logging
interfaces.

