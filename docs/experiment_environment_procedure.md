# Experiment environment procedure

## Before the session

1. Build/install the existing enabled scene, `Assets/Research/Scenes/HandTrackingResearch.unity`,
   without changing the sample/package scenes.
2. Enable Quest hand tracking and start the application.
3. Confirm XR Hands Ready, Left Hand Tracked, and Right Hand Tracked.
4. Connect and select the electronic piano following `quest_midi_setup.md`.
5. Verify middle C (60), Note Off, a three-note chord, and CC64.

## Calibrate

1. Choose the hand used as the point marker with **Point Hand Left/Right**, then press
   **Calibration Start**. Use the other hand to operate the panel.
2. Place an index tip at A (reference white key front-left) and press **Register Next Point**.
3. Register B to the right along the keyboard.
4. Register C toward the rear of the keyboard.
5. Confirm Calibration Status says Valid. If rejected, increase point separation and make the
   right/depth directions less parallel.
6. Press **Save Calibration** and visually check virtual/physical overlap.

## Record and verify

1. Press **Start Recording**, perform for at least 30 seconds, then press **Stop Recording**.
2. Confirm a session id and save path are shown.
3. Restart the app, press **Load Calibration**, and confirm the alignment is restored.
4. Retrieve the session as described in `log_retrieval.md`.
5. Check all seven required files, increasing timestamps, note 60 On/Off, simultaneous chord state,
   CC64 events, both-hand rows, and a zero dropped-batch count.

Do not run correction, IK, IMU, MediaPipe, ML, cloud, or automatic fast-motion features in this
phase.
