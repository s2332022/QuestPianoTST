# PianoMinimalTest Quest実機確認手順

## 前提

- Unity 6000.5.10f1
- Quest 2（Developer Mode、USB debugging、Hand Tracking有効）
- data通信対応USB/OTG cable。必要ならpowered USB hub
- class-compliant USB MIDI piano
- Android Build Support、SDK、NDK、OpenJDK（当該Unity installに存在することを確認済み）
- 実施記録に日付、Quest OS、APK build日時/hash、piano型番、cable/hub、結果を残す

自動検証では`PianoMinimalTest.unity`だけを明示したAndroid development APK buildが成功済みである。これはC#/IL2CPP、Android/Gradle、Scene packaging、Android pluginを含むPC側build gateであり、QuestへのinstallとUSB MIDI受信を代替しない。

## 1. Editor gate

1. `Assets/Research/Scenes/PianoMinimalTest.unity`を開く。
2. ConsoleをClearし、script compile errorが0であることを確認する。
3. Hierarchy rootが`XR Origin`、`Research Runtime`、`Piano Root`、`Research UI`、`EventSystem`の5個だけであることを確認する。
4. Main Camera、EventSystem、PianoResearchRuntimeが各1個、Missing Script / Missing Referenceが0であることを確認する。
5. MIDIを接続しないでPlayし、例外なく起動し`MIDI Connected: False`となることを確認する。
6. Test RunnerでEditMode `QuestPianoMotion.Research.Tests`（7件）とPlayMode `QuestPianoMotion.Research.PlayModeTests`（1件）を実行する。

## 2. Build ProfileへSceneを登録

この作業ではProjectSettingsとBuild Profileを自動変更していない。Unity UIで次を行う。

1. `File > Build Profiles`を開く。
2. 既存のMeta Quest / Android profileを選択し、必要なら`Switch Platform`でAndroidへ切り替える。
3. Scene Listを開く。profileがGlobal Scene Listを上書きできる場合は、そのprofile内のlistを使用する。
4. `Assets/Research/Scenes/PianoMinimalTest.unity`をドラッグするか、Sceneを開いて`Add Open Scenes`を実行する。
5. `PianoMinimalTest.unity`だけをenabledにする。他Sceneはasset削除せず、profile上でdisableまたはlistから外す。
6. `HandTrackingResearch.unity`がAssets内に残っていることを確認する。
7. OpenXR、Meta Quest feature、IL2CPP、ARM64、Minimum API Levelを既存profileで確認する。現設定の読取値はMinimum API 32、ARM64である。
8. Project Validationを実行し、問題がないことを確認して`Build And Run`する。

## 3. MIDI未接続cold start

1. Pianoを外したままappを起動する。
2. HMD映像、UI、13鍵が表示され、crash / NullReferenceExceptionがないことを確認する。
3. `Refresh MIDI`、`Connect MIDI`を押しても安全に待機することを確認する。
4. `Start Recording`、数秒待機、`Stop Recording`が成功することを確認する。

## 4. HMD・左右手・UI

1. 頭を平行移動・回転し、Main Cameraが追従することを確認する。
2. 左右の手を片側ずつtracking範囲へ出し入れする。
3. `Left/Right Hand Tracked`と青/橙のjoint表示が独立して変化し、喪失・復帰でcrashしないことを確認する。
4. Index Tipでbuttonを指し、Thumb Tipとのpinchで全11buttonが操作できることを確認する。
5. 追跡喪失中のCaptureが`Index tip is not tracked.`となり、例外にならないことを確認する。
6. 表示が指定7項目だけで、長文、joint text、event履歴、毎秒詳細統計がないことを確認する。

## 5. USB MIDI

1. USB MIDI pianoを接続し、AndroidのUSB/MIDI permissionが表示された場合はユーザーが許可する。
2. `Refresh MIDI`、`Connect MIDI`の順で押し、`MIDI Connected: True`を確認する。
3. Note 60と72でNote On / Off、弱い/強いvelocityによる押下色、復帰を確認する。
4. Note On velocity 0がNote Offとして扱われることを確認する。
5. 3音以上の和音を押し、各鍵が独立して復帰することを確認する。
6. 同一noteをchannel 1 / 2でOnにし、片channelだけOffにしても鍵が早期復帰しないことを確認する。
7. CC64 value 64以上/未満を送信し、Last MIDI Eventと記録値を確認する。
8. Note 59 / 73を送っても鍵盤が13鍵から増えないことを確認する。
9. 抜線・再接続でcrashせず、同名deviceへの再接続が動作することを確認する。

## 6. キャリブレーション

1. `Point Hand Left`または`Point Hand Right`を選ぶ。
2. `Capture A`を基準白鍵の前左、`Capture B`を鍵盤右方向、`Capture C`を奥方向で押す。
3. 一度、5 cm未満の点またはほぼ平行な3点を入力し、Invalidとして拒否されることを確認する。
4. 有効な3点で鍵盤rootが実鍵盤へ整列することを確認する。
5. `Save Calibration`後にappを終了・再起動し、`Load Calibration`で同じ位置へ復元されることを確認する。
6. 保存ファイルなしのLoadでもcrashしないことを確認する。

## 7. 同期記録

1. `Start Recording`後30秒以上、両手・HMDを動かし、単音、和音、複数channel、CC64を入力する。
2. `Stop Recording`し、二重Start / Stopでもcrashしないことを確認する。
3. `PianoResearch/sessions/<session_id>/`から7ファイルを取得する。
4. timestampが非減少で、hands左右tracking flags、head pose、MIDI note/channel/velocity/CC64、keyboard pressed/velocity/sustain、calibration snapshot、metadata duration/device/calibration IDが存在することを確認する。
5. `session_metadata.json`の`dropped_log_batches`が0であることを確認する。

ADB例（application idは実際のprofileに置換する）:

```text
adb devices
adb logcat -s Unity QuestMidiBridge
adb shell ls /sdcard/Android/data/<application-id>/files/PianoResearch/sessions
adb pull /sdcard/Android/data/<application-id>/files/PianoResearch/sessions
```

記録停止後にpullする。

## 8. 性能・安定性

10分以上、手追跡喪失、MIDI hot plug、記録を組み合わせて確認する。

- 毎フレームlogがない
- 継続的なGC allocation増加や周期的spikeがない
- background writerがrender threadを長時間blockしない
- 鍵盤と手表示に影がない
- 不要なRenderer、Collider、Rigidbody、AudioSourceがない
- Stop、app終了、component disableでwriterがflushされる

## 合格基準

- [ ] Unity compile error 0
- [ ] Missing Script 0
- [ ] Missing Reference 0
- [ ] NullReferenceException 0
- [ ] HandTrackingResearchをBuild対象にしなくても新Sceneが起動
- [ ] 左右手追跡・表示・喪失復帰が正常
- [ ] MIDI未接続起動が正常
- [ ] Start / Stop Recordingと7ファイルが正常
- [ ] EditMode 7件、PlayMode 1件がpass
- [ ] Android buildに`QuestMidiBridge.java`が含まれUSB MIDIが受信可能

## 既知の制約

- PC EditorではAndroid USB MIDI bridgeは生成されない。
- 手表示は研究データと一致する軽量joint表示で、SkinnedMeshの手モデルではない。
- UIは画面投影したIndex Tip + pinch入力で、触覚feedbackやvisible rayはない。
- CC64は記録されるが、既存設計どおりNote Off後の見た目をpedal releaseまで保持しない。
- MIDI SysEx、MIDI 2.0、音源再生、88鍵、物理鍵入力は対象外。
- 自動再接続は同一device名を前提とし、Java bridgeは最初の利用可能output portを開く。
- bounded queueが飽和するとbatchをdropし、metadataへ件数を記録する。
- Quest実機、USB piano、実利用cable/hubでの最終確認はこの手順に従って別途必要。
