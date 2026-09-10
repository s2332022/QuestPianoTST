# Retrieving logs with ADB

The current Android application id in ProjectSettings is
`com.UnityTechnologies.com.unity.template.urpblank`. If it changes, substitute the new id below.

```powershell
adb devices
adb shell ls /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/sessions
adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/sessions .\QuestPianoSessions
```

To retrieve only one session:

```powershell
adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/sessions/SESSION_ID .\QuestPianoSessions\SESSION_ID
```

The reusable calibration is adjacent to `sessions`:

```powershell
adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/piano_calibration.json .
```

If direct access is denied, use a Development Build and:

```powershell
adb exec-out run-as com.UnityTechnologies.com.unity.template.urpblank tar -cf - files/PianoResearch > PianoResearch.tar
```

Stop recording before pulling so buffered rows have been flushed. Compare CSV header widths and
`session_metadata.json.recording_duration_sec` after transfer.

