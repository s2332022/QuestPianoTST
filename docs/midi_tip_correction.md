# MIDI打鍵情報による表示指先補正

研究題目：MIDI打鍵情報を用いたVR環境における手指・鍵盤間の表示ずれ補正

## 調査結果と作業範囲

開始時ブランチ：`feature/quest-ble-midi`。HEAD：`8128ac6e24db84eebf00e125a1d159681ea2fcf5`。
GitHub remote：`github`（s2332022/QuestPianoTST）。GitLab remote：`gitlab`。
追跡対象に未コミット変更はなかった。既存の未追跡 .agents、BuildMetadata、Screenshots、Temp、Recovery 等は保持し、コミット対象から除外する。
変更前チェックポイント：`checkpoint/pre-midi-tip-correction-20261019`。
ソースzip・開始時status・既存APKバックアップはソース外 `%USERPROFILE%/QuestMidiCorrectionWork/` に保存。

XRHandPoseProviderは公式XRHandSubsystem.updatedHandsからTrackingOriginLocalの関節を取得し、
RawFrameUpdatedを通知した後、IHandPoseProcessorで別配列へコピーしてDisplayFrameUpdatedを通知する。
HandPoseFrameは再利用される可変データであるため、処理・CSV文字列化は同期実行し、フレーム参照を別スレッドへ渡さない。

既存Questシーンの手表示はMinimalHandVisualizerの関節マーカーのみだった。
導入済み公式XR Hands 1.8.1 HandVisualizerサンプルの左右スキンメッシュと関節マッピングを再利用する。
標準XRHandSkeletonDriverはRawのXRHand入力を購読するため、そのままでは研究用の表示コピーを渡せない。
派生プレハブからRaw購読コンポーネントを除去し、DisplayHandSkeletonで表示コピーだけを骨へ適用する。
公式URP Unlit材質を使用し、照明に依存しない不透明表示とする。新規外部パッケージは追加しない。

Raw取得・4D Raw解析・MIDI入力本体・UDP v1・旧CSV列・較正の保存復元・既存シーン・Packages・ProjectSettingsは変更しない。
StandaloneQuest経路も保持する。新機能は既存DistributedQuestClientの経路に接続する。

## 変更ファイル

Scripts以下は`Assets/Research/Scripts/`、Editor/Resources/Testsは`Assets/Research/`を基準とする。
新規Unity資産には対応するmetaファイルを含める。

| ファイル | 役割 |
|---|---|
| Hands/MidiTipCorrectionProcessor.cs（新規） | 固定指、MIDI所有状態、制約、状態遷移、評価サンプル |
| Hands/DisplayFingerSolver.cs（新規） | 選択指だけの骨長保持 |
| Hands/DisplayHandSkeleton.cs（新規） | 表示姿勢を公式スキンメッシュへ適用 |
| Keyboard/PianoKeySurface.cs（新規） | 有効領域の和集合と最近点 |
| Keyboard/VirtualPianoKeyboard.cs | 静止境界・有効鍵面の取得API追加 |
| Hands/MinimalHandVisualizer.cs | メッシュ表示モード追加 |
| Distributed/DistributedQuestClient.cs | 実MIDI、補正プロセッサ、解除、ログを接続 |
| Distributed/DistributedQuestComposition.cs | 起動時に左右の表示メッシュを配置 |
| Distributed/DistributedQuestUi.cs | 既存MIDIロックを利用する補正設定パネル |
| Logging/QuestRawSessionLog.cs | 新しいCSVとメタデータ。旧列は保持 |
| Editor/CorrectionHandAssetBuilder.cs（新規） | 公式資産から表示用プレハブを生成 |
| Editor/QuestPianoMotion.Research.Editor.asmdef | ビルダー用XR Hands参照追加 |
| Resources/CorrectionHands/（新規） | 左右プレハブと公式URP材質 |
| Tests/EditMode/MidiTipCorrectionTests.cs（新規） | 幾何・状態・安全条件・左右5指 |
| Tests/PlayMode/MidiTipCorrectionRuntimeTests.cs（新規） | メッシュ、BLE経路、UDPソケット、CSV |
| docs/midi_tip_correction.md（新規） | 本資料 |
| docs/raw_hand_4d_analysis.md | 本資料へのリンク追加 |

## 処理経路とRawの保護

`XR Hands Raw → Rawログ/送信 → Identityで表示コピー → MIDI制約 → 選択指の関節調整 → DisplayFrameUpdated → DisplayHandSkeleton → SkinnedMeshRenderer`

Raw配列とRaw姿勢は変更しない。補正OFFではローカル表示コピーをそのまま使う。
従来のPC表示モードはRemoteDisplayPoseProcessorを利用し、その間ローカルMIDI補正を停止する。
異なる端末の時計や、異なる時刻のPC姿勢とローカルRawを混合しない。

BLE経路：Android Direct GATT → BleMidiPacketParser → BleMidiSession → BleMidiInput.ResearchEvent → 補正。
既存のMessageReceived → VirtualPianoKeyboard.BindBleMidiは独立に維持する。
UDP経路：UdpTransport → セッション/sequence検査 → NetworkMidiInput.Enqueue成功 → 補正。
鍵盤は従来どおりFlushPending → MessageReceived → ApplyMidiで更新する。
KeyboardStateTracker、BLE独立所有状態、CC64、velocity=0のNoteOff正規化、切断時AllNotesOffは保持する。

PianoCalibrationManagerのA/B/C、保存・復元・最後の有効較正の保持は変更しない。
較正取得中と較正無効時は補正を停止し、較正点へ補正姿勢が混入しないようにする。

## 座標・鍵面・補正量

Tracking T：ResearchServices.TrackingOrigin（CameraFloorOffsetObject）。World W：Unity World。
Keyboard K：KeyboardRoot。+Xは左右、+Yは面法線上向き、+Zは奥行であり、既存の軸を保持する。

`pW = TrackingOrigin.localToWorld * pT`

`pK = KeyboardRoot.worldToLocal * pW`

KeyboardGeometryの較正scaleX/scaleZと原点オフセットを含む、既存キューブの静止時8頂点から各鍵の境界を求める。
TrackingOriginとKeyboardRootは単位長・直交・右手系の変換を要求し、非単位スケールやshear時は補正しない。
この条件でKの1単位は1 mとなる。Rawログの任意行列保存方針は変更しない。

### 鍵面内

黒鍵は上面矩形、白鍵は上面矩形から黒鍵の静止時XZ投影を切り抜いた矩形の和集合を使う。
現在のモデルはキューブであり、実鍵の丸み、台形、曲面、実指の指腹、遮蔽の精密モデルではない。

Tip-to-contactオフセットはK座標の一定ベクトルとして設定する。指の回転に追従する指腹モデルではない。
`contactK = rawTipK + offsetK`。
XZ平面で各有効矩形へclampし、最小距離の位置を選ぶ。
内部または領域までの距離が平面許容幅以内ならXZ補正は0。
範囲外だけ実領域の最近点へ向かい、鍵の中心へ吸着しない。

### 法線・強度・上限

contactK.yを`[restTopY - belowTolerance, restTopY + aboveTolerance]`へclampする。
現モデルの白鍵上面は0、黒鍵上面は24.5 mm。較正の法線スケールは1。
既存8 mm打鍵アニメーションは仮想表示モデルの移動であり、実鍵の実測変位ではない。
補正基準は静止時上面とし、連続的な物理沈み込みを推定しない。

`targetTipK = constrainedContactK - offsetK`

`baseDelta = strength * (targetTipK - rawTipK)`

`boundedDelta = ClampMagnitude(baseDelta, dMax)`

`requestedDelta = stateGain * boundedDelta`

gainは0〜1の経過時間ベースsmoothstep。
上限をgainの前に適用し、上限が働く場合も滑らかに開始・解除する。
ソルバー後の実際の指先変位にもdMaxを再検査する。
強度、上限、到達可能性により、有効領域まで到達しない場合の残差も記録する。
ログのeffective_strengthは上限・IK適用前の`strength * gain`であり、実変位とは区別する。

### 関節と表示

選択指のProximal（MCP）を固定し、Intermediate/Distal/Tipへ短いFABRIKを適用する。
親指はProximal/Distal/Tip。Rawの各骨長を保持し、セグメント方向の変化で表示回転を更新する。
手首・手根・他指・反対の手は変更しない。
到達不能なら到達可能な位置までとし、reach_limitedと実際の補正後残差を記録する。
ソルバー失敗・過大変位ではRawを保持する。解剖学的関節角制限、手掌変形、衝突は未対応。

メッシュ骨には親から子の順にorigin.TransformPoint(displayPosition)とorigin.rotation * displayRotationを一度だけ適用する。
派生プレハブにはRawを購読するXRHandSkeletonDriver/XRHandTrackingEvents/XRHandMeshControllerを含めない。
関節不正時・250 ms以上姿勢通知がない場合はメッシュを非表示にし、古い手を残さない。
既存HAND GAMEOBJECTS/HAND GPUは診断表示として保持し、それを選ぶとメッシュは非表示になる。

## 状態・時刻・安全解除

Inactive → Engaging → Active → Releasing → Inactive。
NoteOff/velocity0、CC120/123、BLE合成切断解除、UDP切断、セッション終了、保持タイムアウトは解除方向へ遷移する。
追跡無効、較正無効/較正取得中、変換不正、設定不正、指選択変更は直ちにRawへ戻す。
条件復旧だけでは押下中の補正を再開せず、新しい実NoteOnを必要とする。

BLE/UDP・channel別に押下所有を追跡する。
2音以上、重複NoteOn、複数入力元の同時押下は曖昧として即時停止し、全NoteOffまでラッチする。
残った1音へ指を割り当て直さない。保持タイムアウト後も実NoteOffまたは入力元切断が必要。
UDP鍵状態snapshotの修復は解除と所有状態の同期にだけ使い、新しい補正を開始しない。
修復で導入された押下状態も全解放まで曖昧として保持する。
CC64から物理的な押下継続を推定しない。
自動推定は予約モードであり、選択するとautomatic_unavailableとして補正を停止する。

BLE受信時刻は既存のJava nanoTime → Quest Stopwatch変換を再利用する。
13bit送信時刻は既存MIDI研究CSVに保持する。
UDP受信はQuest socket clock、PC送信時刻は独立クロックのまま保存し、直接引き算しない。
補正はUnityがNoteOnを観測した後の姿勢からであり、受信前・処理前のRawへ遡及しない。
フレーム時刻の減少を拒否し、Dynamic/BeforeRenderとも経過時間で制御する。

## 最小実験モード

既存PianoDistributedQuestシーンを使う。
初期値は補正OFF、右手人差し指、強度0.5、最大15 mm、平面許容2 mm、法線許容 -3/+8 mm、接触オフセット0。
Engaging 80 ms、Releasing 120 ms、最大保持3 s、姿勢停止監視250 ms。
予備実験用の初期値であり、精度改善を実証した値ではない。

1. 有効なA/B/C較正を適用し、実BLE MIDIまたはPC→UDPを接続する。
2. 右側MIDI TIPパネルで左右、NEXT FINGER、ON/OFF、強度、最大量を選択する。
3. PAD YとY BANDで接触オフセットYと上下許容幅を調整できる。
   全XYZオフセット、平面許容幅、時間定数、保持時間はNetwork ClientのCorrection Settingsから設定する。
4. HAND MESHを選ぶ。Questではこの公式メッシュ表示が初期表示となる。
5. START RAW RECし、選択した指でC4〜C5（MIDI 60〜72）の単音を弾く。
   AはOFF、BはON、Cは強度0/0.25/0.5/0.75/1を比較する。少なくともC4とC#4を含める。
6. 条件間で指・鍵・較正を揃える。STOP RAW REC後にセッション全体を取得する。
   PCのStart/Stopでも同じ追加ファイルが開閉される。

全新ボタンは既存PerformanceUiSafety.Runと親CanvasGroupを使う。
演奏中・MIDIクールダウン・pinch保持中のUIロックを維持する。
鍵とpinchを放してから設定を変更する。比較実験では条件ごとに別セッションを推奨する。

## 研究ログ

既存の`Application.persistentDataPath/PianoResearch/DistributedSessions/<session>_Quest/`へ追加する。

- quest_tip_correction.csv：Dynamicコールバックごとの選択指のRaw/Correctedペア。
- tip_correction_metadata.json：座標、単位、時刻、モデル形状、評価の限界。

CSVにはsession ID、source/event_index/generation、event type、note/channel、受信・Unity観測時刻、
左右指・選択方式、Raw/Corrected Tip K、key_id、XZ領域距離前後、符号付き法線残差前後、
XYZ変位・ノルム、状態・gain・設定強度・実効強度、上限適用、到達制約、追跡/座標/ジオメトリ有効性、
transform snapshot ID、calibration ID、未適用理由、全補正設定を保存する。

座標系はKeyboardRootLocal、単位はm。RawとCorrectedは同じRaw時刻・callback_index・snapshot_idから計算する。
normal_residualは接触オフセット適用後のcontactY-restTopYであり、許容帯の列と併せて解釈する。
不正座標は空欄。PC表示モードは同時刻比較ができないため補正CSVの座標を空欄にして理由を残す。
フレーム間の各MIDIイベントは既存quest_midi_research.csvに残り、補正CSVのイベント列と併用する。
クエリの結合キーはsource/event_index/generation。変換はtransform_snapshots.jsonlのsnapshot_idを参照する。

文字列化した行だけを既存の有界キューへ渡し、overflow検出と終了summaryを共有する。
既存Raw/PC CSVの列は変更しない。4D Raw解析は従来どおり未補正のRawを対象にする。
記録値は鍵盤モデルに対する表示整合性であり、実指の絶対誤差、真の接触時刻、精度改善の実証ではない。
Raw仕様はraw_hand_4d_analysis.md、取得手順はlog_retrieval.mdを参照する。

## 検証結果とビルド

Unity 6000.5.10f1。集中EditMode/PlayMode → 全EditMode/PlayMode → 既存Androidビルダーの順で実施。
テストのBLEは既存パーサー/セッションへの注入であり、実BLE機器確認とは区別する。

| 検証 | 2026-10-19の結果 |
|---|---|
| 集中EditMode | 33/33合格。左右5指、制約、上限、Raw保持、解除、安全条件、UDP修復 |
| 集中PlayMode | 2/2合格。骨とBakeMesh後の頂点変化、BLE経路、実UDP loopback、CSV |
| 全EditMode | 1,102合格、4スキップ、失敗0 |
| 全PlayMode | 59/59合格、失敗0 |
| Raw解析Python | 9/9合格 |
| Android APK | BUILD_RESULT_PENDING |
| Quest実機・実MIDI機器 | 未検証。ADB接続デバイスなし |

全EditModeの4スキップは未導入Addressables設定を要する2件とUnix系OS限定2件。
全EditMode後の変更は強度0/上限0の未適用理由コードの区別だけであり、集中33件と全PlayModeで再検証した。
試験ログ/XMLはソース外 `%USERPROFILE%/QuestMidiCorrectionWork/` に保存し、コミットしない。

ビルドはPianoDistributedSceneBuilder.BuildAndroidValidationを使用する。
PianoDistributedQuestシーンのみ、Android / IL2CPP / ARM64。
出力：Builds/Android/PianoDistributedQuest.apk。既存ビルダーが設定と生成メタデータを復元する。
実装コミットを記録してビルドする。既存未追跡ファイルを保持するため、ビルドメタデータのdirtyはtrueになり得る。

## 実機未確認事項と次の作業

ADBにQuest接続がないため、実BLE機器、Quest上の手の視認性、両眼表示、物理鍵盤との整合、
自然さ、体感遅延、追跡復帰、長時間性能は合格扱いにしない。

APK導入後は右手人差し指のC4単音A/B/C、C#4、左右全5指、上限、NoteOff解除を観察する。
較正中の停止、追跡喪失/復帰、BLE切断/再接続、UDP切断/復帰、NoteOff欠落、セッション終了も確認する。
既存UIロック、透過鍵盤、鍵盤アニメーション、CC64、PCセッションとCSVのwriter_closedも点検する。

現在実装済み：手動1指、C4〜C5単音、表示補正、ON/OFF、強度/上限、解除、追加CSV、公式手メッシュ。
未実装：自動打鍵指推定、和音補正、複数指対応、解剖学的関節角制限、高度なIK、実鍵の連続変位計測。
次は実機A/B/Cと閾値調整、屈曲平面・関節角制限の改善、性能測定、外部基準による評価を行う。
