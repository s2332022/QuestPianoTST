# MIDI input specification

## Scope

The Quest build uses Android's platform `android.media.midi` API (API 23+) through
`Assets/Plugins/Android/QuestMidiBridge.java`. No Unity package or third-party binary is added.
The bridge enumerates devices that expose at least one output port (data flowing from the piano),
opens the first such port, and handles disconnect/reconnect notifications.

Supported channel messages are Note On (0x9n), Note Off (0x8n), and Control Change (0xBn).
Note On with velocity zero is normalized to Note Off. Channels are logged as 1–16. CC64 values
0–63 mean sustain off and 64–127 mean sustain on. Unsupported and system messages are ignored.
Running status and interleaved MIDI real-time bytes are handled.

## Timing and threads

`MidiReceiver.onSend` timestamps each parsed event with the supplied Android timestamp, falling
back to `System.nanoTime()`. At startup this clock is mapped to the shared .NET
`Stopwatch` domain by a midpoint measurement. Java only sends an encoded event to Unity;
`AndroidMidiInput` first places it in a thread-safe queue; callback delivery never directly
changes a GameObject. The queue is drained on `Update`.

## Device lifecycle

The UI provides refresh and select/connect actions. The selected name is retained. After a
disconnect, device discovery continues and an exact name match is reopened at two-second
intervals. Open failures and detach events leave the application running.

## Dependency and license decision

The selected backend is Android framework code and project-owned source, so there is no
additional third-party package license. DryWetMIDI (MIT; built-in device API supports Windows and
macOS, not Android) and MidiJack (MIT; its documented targets are Windows and macOS) were considered
but not added; neither provides the required Quest USB MIDI backend for this project.
