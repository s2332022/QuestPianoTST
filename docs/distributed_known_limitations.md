# Distributed known limitations

- UDP v1 is unauthenticated and unencrypted; use a trusted private LAN. NAT/cloud routing is unsupported.
- Clock selection minimizes RTT but does not yet fit/apply drift. Wi-Fi asymmetry remains an offset error source.
- Pose identity echo is for display-path validation, not correction. A lost/incomplete corrected frame is ignored and local Raw remains the disconnect fallback.
- `winmm` covers requested short channel messages, not SysEx/MIDI 2.0. Device hot-plug requires Refresh/reconnect in the UI.
- MIDI queue overflow is reported as critical; it cannot reconstruct events lost by the OS/device. Pose favors newest data.
- HMD tracking-state bits are not separately sourced by the current camera API; `HeadValid` means a Main Camera was available.
- Hardware 30-second results, real firewall behavior, Quest sleep, and cable/device-specific disconnect behavior require the documented two-device test.

