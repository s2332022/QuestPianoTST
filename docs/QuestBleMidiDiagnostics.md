# Quest Direct GATT BLE-MIDI diagnostics

Quest OS lacks the standard BluetoothMidiService on the tested Quest 2. The diagnostic
BLE path uses BluetoothDevice.connectGatt (autoConnect=false, TRANSPORT_LE), never
MidiManager.openBluetoothDevice. QuestMidiBridge and existing Android/UDP MIDI remain intact.
No packages or scenes were changed. Existing service-filtered BLE scan, permissions,
MidiMessage, IMidiInput, AndroidMidiInput.NormalizeMessage, BleMidiSession,
MonotonicSessionClock and RejectingBoundedQueue are reused.

## Connection and delivery

Standard MIDI service: 03B80E5A-EDE8-4B33-A751-6CE34EC4C700.
MIDI 1.0 characteristic: 7772E5DB-3868-4112-A1A9-F2669D106BF3.
CCCD: 00002902-0000-1000-8000-00805F9B34FB.

Only the selected scanned address is connected. GATT connection callback starts discovery;
service discovery validates the characteristic, NOTIFY property and CCCD. Local notification
enable plus remote CCCD ENABLE_NOTIFICATION_VALUE write are both required. Only a successful
descriptor-write callback marks the input connected. Notifications arriving during subscription
are queued and consumed after success. API 33+ uses the byte-array notification callback and
descriptor-write overload; older Android uses the legacy callbacks. All callbacks validate the
connection request and GATT instance; obsolete callbacks cannot change current state or queue.
Each attempt creates a new GATT object, and disconnect/close invalidates the request and queue.

States: connecting -> discovering -> subscribing -> connected. Failures include GATT status,
missing service/characteristic/CCCD, notification enable/write errors and a 15-second setup
timeout. Failures and actual GATT disconnects require manual CONNECT. No periodic retries.
No MIDI arrival timeout: a silent connected piano remains connected beyond 30 seconds.
Bluetooth disable and permission loss also close/release the BLE session. Notifications and
normalized messages have separate bounded queues (2048 each); overflow disconnects and emits
synthetic NoteOff/CC64=0 through the existing BLE session, with no UDP side effects.

## Packet subset and clocks

The C# parser runs on the Unity thread. It handles NoteOn, NoteOff, velocity-zero normalization,
CC (including CC64 and CC123), channel-message lengths, running status with shared or explicit
timestamps, multiple messages, timestamp-low rollover and timestamped MIDI real-time bytes.
Running status resets at every notification boundary. BLE-MIDI channel messages cannot be
split across notifications: incomplete messages are rejected and never joined to a later packet.
SysEx is outside the supported note/CC subset and is discarded. Malformed packets publish no
partial events; a following valid packet parses independently.

The sender's 13-bit millisecond timestamp is retained in Sample.BleTimestampMilliseconds and
the dedicated CSV appended column ble_timestamp_13bit_ms. It wraps every 8192 ms and is in an
unsynchronized remote clock domain. It is NOT converted directly into Android nanoTime.
android_timestamp_nanos remains 0 for GATT samples; java_received_nanos captures System.nanoTime
at notification receipt. Event absolute time uses that receipt mapped through the existing
Quest clock synchronization. time_source states gatt_receive_ble_sender_timestamp_retained.
This records both clocks without inventing a remote-to-local synchronization. Synthetic releases
have no BLE timestamp (-1). Existing research CSV formats and UDP protocol are unchanged.

## Startup diagnostics

Unity [BLE] and dedicated CSV state rows include:

- android.software.midi feature boolean (informational, never blocks GATT).
- BluetoothMidiService explicit component lookup: unavailable_not_found, disabled or
  present_enabled (binding not tested). Manifest package query makes it visible on Android 11+.
  Component presence is not proof of a successful bind; the app does not bind or open it.
- transport=Direct GATT BLE-MIDI.

## Quest 2 procedure (hardware behavior remains unverified)

1. Install Builds/Android/PianoDistributedQuest.apk, enable Bluetooth and piano advertising.
2. Capture logcat [BLE]. Confirm all three startup diagnostics and absence of Bluetooth MIDI
   service bind attempts from this BLE path.
3. Grant Nearby devices with PERMISSIONS; SCAN 12s, NEXT DEVICE to select the advertised
   address, CONNECT. Confirm discovering/subscribing/connected and transport=direct_gatt.
4. Play and release notes, velocity zero, chords/rapid sequences, CC64 and CC123. Check channel,
   note, velocity, ble_timestamp_13bit_ms and java_received_nanos in ble_diagnostics/quest_ble_*.csv.
   The existing keyboard/PC research logs continue to use UDP only.
5. Leave the device silent for at least 45 seconds; verify connected remains true, then play again.
6. Hold notes/sustain, power off or move out of range. Confirm gatt_disconnected_STATUS and
   synthetic releases. Observe at least 30 seconds: no recurring connect attempts. Power on,
   rescan if needed and press CONNECT. Rapid disconnect/reconnect must not replay old events.
7. Test permission denial/revocation, Bluetooth disable, component enable/disable and app restart.
   Verify UDP still functions independently throughout BLE failures and disconnects.
8. Stress notifications; verify drop/stale diagnostics and bounded-queue release behavior.

Use authorized Android file access to export the dedicated diagnostics. Perceived latency,
notification delivery and physical radio disconnect detection still require device evidence.

## Primary references

- https://developer.android.com/reference/android/bluetooth/BluetoothGatt
- https://developer.android.com/reference/android/bluetooth/BluetoothGattCallback
- https://midi.org/midi-over-bluetooth-low-energy-ble-midi
- https://microsoft.github.io/MIDI/kb/ble-midi-transport-architecture/ (MIDI 1.0 packet format)

Validation evidence for this change: C:/Users/Yuki_/gatt-work/ (test XML, Java compilation,
build log, preservation hashes and APK SHA-256 report). See validation.json after the build.

## Changed files and tests for this revision

- Assets/Plugins/Android/QuestBleMidi.java: Direct GATT connection, subscription, generation checks, raw notification queue and startup capability diagnostics.
- Assets/Research/Editor/AndroidBleMidiBuildGuard.cs: explicit Bluetooth MIDI service package visibility query; existing permissions retained.
- Assets/Research/Scripts/Midi/BleMidiInput.cs: consume GATT packets, log sender timestamp, remove periodic retry and receive-idle disconnect.
- Assets/Research/Scripts/Midi/BleMidiSession.cs: retain sender timestamp metadata in Sample.
- Assets/Research/Scripts/Midi/BleMidiPacketParser.cs (+ .meta): new BLE-MIDI 1.0 note/CC decoder.
- Assets/Research/Scripts/Midi/BleMidiDiagnostics.cs: update connection instructions and remove automatic-retry control.
- Assets/Research/Tests/EditMode/BleMidiPacketParserTests.cs (+ .meta): Note On/Off/velocity-zero, running status, multiple messages, notification boundaries, malformed packets, timestamp wrap, real-time interleaving and old generation.
- Assets/Research/Tests/PlayMode/BleMidiIsolationTests.cs: exercise the real BLE parser while retaining UDP keyboard ownership.
- docs/QuestBleMidiDiagnostics.md: implementation limits, clocks and hardware checks.

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Focused EditMode | 47 | 0 | 0 |
| Focused PlayMode | 8 | 0 | 0 |
| Full EditMode | 1049 | 0 | 4 |
| Full PlayMode | 43 | 0 | 0 |

Four pre-existing skips: two Addressables tests require an uninstalled/configured package;
two CLI tests require Unix. Java compile using the installed Android SDK and Unity classes.jar
passed with -Xlint:deprecation and no warnings. No git add/commit or branch switch was run.

## Final APK verification (2026-10-06)

Build succeeded using the existing Android builder; only PianoDistributedQuest.unity included.
Output: Builds/Android/PianoDistributedQuest.apk
Size: 51,921,876 bytes
Last write: 2026-10-06T14:45:22.578773 JST
SHA-256: c9a0c27605de8195ca5b3f24a4ca870de2d62f1a5a57e54620daf212434169e2
ABI: arm64-v8a only; libil2cpp.so and Direct GATT bridge confirmed in the APK.
Manifest: min API 32 / target API 36; SCAN + CONNECT and neverForLocation retained,
no ADVERTISE; INTERNET + ACCESS_NETWORK_STATE retained; BLE/MIDI features optional.
Startup service lookup package query confirmed. Protected file hashes all match the
pre-change snapshot; no unexpected existing-file changes, and staging area remains empty.
Existing build warnings include obsolete object-find APIs and XRSimulationPreferences
move destination collision. Build exit 0; hardware receipt remains unverified.
