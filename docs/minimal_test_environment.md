# 最小研究テスト環境

## 目的と非破壊方針

研究に必要な経路だけを確認する新規Sceneとして `Assets/Research/Scenes/PianoMinimalTest.unity` を追加した。`Assets/Research/Scenes/HandTrackingResearch.unity` は復旧・比較用として元の場所に維持し、内容を変更していない。

今回、既存アセットの削除・移動は行っていない。`Packages`、`ProjectSettings`、Unity公式Package Samples、`Library`を成果物として変更せず、外部パッケージも追加していない。Build ProfileのScene Listも自動変更していない。

## 実装範囲

| 要件 | 最小環境での実装 |
|---|---|
| XR Origin / Main Camera / HMD | `XR Origin/Camera Offset/Main Camera`。Input System `TrackedPoseDriver`でHMD位置・回転・tracking stateを取得 |
| 左右XR Hands | `XRHandPoseProvider`が`XRHandSubsystem`へ接続し、未起動時は1秒間隔で再試行 |
| Raw → Display | `IdentityHandPoseProcessor`が追跡フレーム、root pose、全jointを事前確保済み配列へコピー。補正処理なし |
| 左右手表示 | `MinimalHandVisualizer`がDisplayHandPoseの有効jointを左右色分けしてGPU instancing表示。Collider・影・sample processorなし |
| Android USB MIDI | 既存`AndroidMidiInput`と`Assets/Plugins/Android/QuestMidiBridge.java`を維持 |
| MIDI内容 | Note On、Note Off（Note On velocity 0を含む）、velocity、channel 1～16、CC64 |
| 仮想鍵盤 | `VirtualPianoKeyboard` / `KeyboardStateTracker`。MIDI 60～72固定、白鍵・黒鍵、押下復帰、velocity色、和音・channel別状態 |
| 3点キャリブレーション | 選択した手のIndex TipでA（前左）、B（右）、C（奥）を順次取得。近接点・ほぼ平行な軸は拒否 |
| 保存・読込 | `Application.persistentDataPath/PianoResearch/piano_calibration.json` |
| 同期記録 | XR Hands raw frame、MIDI、HMD、鍵盤状態、update summaryを共通の単調時計で同期 |
| 研究用UI | 指定された7表示と11操作だけをruntime生成 |
| UI入力 | 左右どちらの手もIndex Tipで対象を指し、Thumb Tipとのpinchでクリック。controller/ray/poke Interactorなし |
| EventSystem | Sceneに1個だけ配置し、`MinimalHandUiInputModule`を使用 |

## Runtime構成

Sceneに保存される研究componentは`Research Runtime`上の`PianoResearchRuntime`だけである。起動時に同じGameObjectへ次を各1個だけ生成し、既配置なら再利用する。

- `XRHandPoseProvider`
- `AndroidMidiInput`
- `VirtualPianoKeyboard`
- `PianoCalibrationManager`
- `SynchronizedSessionRecorder`
- `MinimalHandVisualizer`
- `MinimalResearchStatusPanel`

鍵盤実体は`Piano Root`、Canvasは`Research UI`の子へ生成する。MIDI未接続、XR subsystem未起動、左右いずれかの追跡喪失、保存済みキャリブレーションなしは正常な待機状態として扱う。

## データフロー

```text
XRHandSubsystem
  -> Raw HandPoseFrame
  -> IdentityHandPoseProcessor
  -> Display HandPoseFrame -> MinimalHandVisualizer / calibration / hand UI

QuestMidiBridge (Android callback)
  -> ConcurrentQueue
  -> AndroidMidiInput
  -> KeyboardStateTracker
  -> 13-key VirtualPianoKeyboard

Raw Hands + MIDI + HMD + Keyboard
  -> shared MonotonicSessionClock
  -> bounded queue (4096 batches)
  -> background log writer
```

## 記録成果物

記録先は`Application.persistentDataPath/PianoResearch/sessions/<session_id>/`で、次の7ファイルを出力する。

- `hand_joints.csv`
- `midi_events.csv`
- `head_pose.csv`
- `keyboard_state.csv`
- `update_summary.csv`
- `piano_calibration.json`
- `session_metadata.json`

## 最小UI

表示は次の7項目だけで、0.25秒間隔で更新する。

- Left Hand Tracked
- Right Hand Tracked
- MIDI Connected
- Last MIDI Event
- Calibration Status
- Recording Status
- Recording Time

操作は次の11個だけである。

- Refresh MIDI
- Connect MIDI
- Point Hand Left
- Point Hand Right
- Capture A
- Capture B
- Capture C
- Save Calibration
- Load Calibration
- Start Recording
- Stop Recording

## 性能上の判断

- 手フレーム配列と描画matrix配列を再利用し、手表示は左右それぞれ1 instanced draw callとした。
- 鍵盤は共有Materialと`MaterialPropertyBlock`を使う。最小モードではUnlit shader、cast/receive shadows無効、light/reflection probe無効とした。
- `CreatePrimitive`由来の13個のColliderは最小モード初期化時に除去する。Rigidbody、AudioSource、物理演奏経路はない。
- MIDI callbackはGameObjectを操作せずqueueへ格納し、main threadで処理する。
- 記録writerは既存のbounded queueとbackground threadを維持した。
- 毎フレームの`Debug.Log`はない。既存Recorderの録画中status logは1秒に1回である。

## 維持した依存

- XR Hands 1.8.1
- XR Management 4.6.1
- OpenXR 1.17.1 / Meta OpenXR 2.5.1
- XR Core Utils 2.6.0
- Input System 1.20.0
- UGUI 2.5.0
- URP 17.5.0
- `QuestPianoMotion.Research` assembly
- `Assets/Plugins/Android/QuestMidiBridge.java`とAndroid限定PluginImporter設定

`HandTrackingResearch.unity`、Sample Scene、Package Samplesを新Sceneの直接依存にはしていない。

## 配置しなかった機能

Teleport、Locomotion、Continuous Move、Snap Turn、controller Interactor、Ray Interactor、XRI sample gesture、sample床・机・照明、debug表示、説明UI、追加Canvas、AudioSource、Rigidbody、物理演奏Collider、mouse入力、88鍵、音声、装飾モデル、AI、IMU、MediaPipe、Für Elise、補正processorは配置していない。

## 検証結果

Unity 6000.5.10f1 batchmodeで次を確認した。

- C#コンパイルエラー: 0
- Scene validator: 5 roots、Missing Script 0、Missing Reference 0、serialized Collider/Rigidbody/AudioSource/Light 0
- EditMode: 7/7 pass（既存5件 + CC64/channel + Identity）
- PlayMode smoke: 1/1 pass（MIDI未接続起動、service一意性、13鍵、不要物理componentなし、7表示、11button、Start/Stop Recording、7ファイル生成）
- PlayModeログ内の`NullReferenceException` / `MissingReferenceException`: 0
- Android development APK build: success。Scene配列は`PianoMinimalTest.unity` 1件を明示し、Build Settingsは変更していない

USB MIDI実機列挙、Quest上の左右手表示・pinch UI、HMD追従、APK install後の長時間性能はPC batchmodeでは合格扱いにしていない。手順は`minimal_environment_device_test.md`に記載した。
