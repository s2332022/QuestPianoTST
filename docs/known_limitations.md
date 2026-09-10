# Known limitations

- MIDI uses the first Android output port of the selected device. Multi-port routing and MIDI 2.0
  UMP are not implemented.
- Only channel Note On, Note Off, and Control Change are surfaced. SysEx, aftertouch, pitch bend,
  program change, clock, and audio synthesis are outside this phase.
- Device matching for reconnect is exact-name based; two identical devices are ambiguous.
- Android MIDI was designed for API 23+ and the project minimum is currently API 32. A USB
  class-compliant device and compatible OTG/power path are required.
- The visual keyboard is C4–C5. State/data structures cover 128 notes, but an 88-key visual asset is
  not yet supplied.
- Sustain is logged separately and never holds the physical key visual down.
- Three-point calibration relies on index-tip placement and user precision. There is no image,
  surface, or keyboard recognition.
- Raw and display hand pose are separated, but the existing XR Hands sample visual remains driven
  by its existing components. The current processor is identity; no correction or IK is applied.
- The bounded log queue drops whole batches under exceptional storage pressure and records the
  count in metadata.
- Package versions in metadata are constants matched to the inspected manifest. Update them if the
  project package versions change.
- The working copy has no Git repository, so metadata cannot contain a real commit hash.
- Quest hardware, a physical piano, and ADB are required for the 15-step hardware acceptance test;
  static compilation alone cannot prove USB compatibility or runtime frame stability.

