# Network protocol v1

All multi-byte integers and IEEE-754 floats are big-endian (network byte order). UUID is the 16 opaque bytes returned by .NET `Guid.ToByteArray`; readers must not reinterpret field endianness. Maximum UDP datagram is 1,200 bytes.

## Common header (38 bytes)

| Offset | Bytes | Type | Field |
|---:|---:|---|---|
| 0 | 4 | uint32 | magic `0x51504D31` (`QPM1`) |
| 4 | 2 | uint16 | protocol version = 1 |
| 6 | 2 | uint16 | packet type |
| 8 | 4 | uint32 | sender sequence |
| 12 | 8 | float64 | monotonic sender timestamp seconds |
| 20 | 16 | bytes | session UUID |
| 36 | 2 | uint16 | payload byte length |

Unknown type, bad magic/version, non-exact length, over-size datagram, invalid MIDI channel, or malformed Pose joint is discarded without stopping either runtime.

## Types and payloads

1 Pose: frame int32, callback int64, update byte, success uint32, tracked/head flags, chunk index/count uint16, left/right/head poses (7 float32 each), joint count uint16, then 36-byte joint records (hand byte, joint uint16, valid byte, tracking uint32, position 3 float32, rotation 4 float32). Maximum 24 joints/chunk; all chunks share frame/callback IDs. Incomplete newest frames are superseded and not resent.
2 MIDI: event index int64, event/channel/note/velocity/control/value bytes, device hash uint32. Note On velocity zero is decoded as Note Off.
3 ClockSyncRequest: t0 float64. 4 ClockSyncResponse: t0/q1/q2 float64. 5 SessionControl: command byte + command ID uint32. 6 SessionAck: command/status bytes + command ID. 7 Heartbeat: last received sequence uint32 + state byte. 8 Diagnostic: packet/send-failure/drop/queue counters uint32. 9 CorrectedPose reserves the Pose payload layout for Identity/future correction. 10 StartupHello: Quest instance ID, application-version hash, and build-ID hash as three uint32 values. 11 StartupAck: echoed Quest instance ID uint32. Quest retries StartupHello at the heartbeat interval while disconnected; a matching StartupAck establishes the connection.

Sequence numbers are uint32 per sender and packet stream (Pose has a dedicated stream) and use wrap-aware comparison. Gaps increment loss estimate; equal is duplicate; older is out-of-order. Pose is never retransmitted. Start/stop and clock request/response are acknowledged or naturally paired and retried by the PC. Heartbeat timeout defaults to 3 seconds. A breaking field/layout change increments protocol version; compatible additions require a new packet type or length-gated tail.
