# PC MIDI setup

`PcMidiInput` uses Windows Multimedia MIDI (`winmm.dll`: `midiInGetNumDevs`, `midiInOpen`). It requires no Unity package and works in Windows Editor/Player. The callback only copies packed messages into a bounded queue; Unity events run in `Update`.

In `PianoDistributedHost`, use Refresh MIDI Devices, select a device, then Connect MIDI. Note On/Off, velocity, channels 1-16, Control Change including CC64, open, close, and queue overflow diagnostics are supported. Start the host with no device safely; hot unplug closes at the Windows API level and the UI can refresh/reconnect.

Alternative: a maintained managed MIDI package can offer SysEx and friendlier hot-plug APIs, but adds license/version/IL2CPP validation and was intentionally not added. A custom native DLL could wrap modern Windows MIDI Services, but it creates an extra binary/build matrix. `winmm` is sufficient for the requested channel messages and Windows x64 IL2CPP P/Invoke.

