# Quest 2 USB MIDI setup

1. Use a USB class-compliant electronic piano. Disable vendor-only USB drivers/modes.
2. Connect the piano's USB device port to Quest 2 through a USB-C OTG adapter or hub. A powered
   hub is recommended when the piano or adapter draws appreciable bus power.
3. Power on the piano before or after starting the app; hot-plug is supported.
4. Start `HandTrackingResearch`. In the world-space panel select **Refresh MIDI**.
5. Press **Select / Connect** until the expected model name is displayed. Accept Android's USB/MIDI
   access prompt if the OS presents one.
6. Play middle C and confirm `NoteOn ch=... note=60` in Last MIDI Event and movement of key 60.
7. Release it, test a three-note chord, then press/release the sustain pedal and confirm CC64.

If no device appears, reconnect the cable, verify OTG/data support (not charge-only), try a powered
hub, and check `adb logcat -s Unity QuestMidiBridge`. Device names come from Android MIDI
properties and can differ from the product's printed model.

Desktop Editor MIDI is intentionally not provided. `InjectForTesting` exists for automated or
developer simulation only.

