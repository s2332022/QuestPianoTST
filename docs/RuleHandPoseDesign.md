# 全指ルールベース表示姿勢補正

## 保護・比較

基準コミットは `2b5733ea3c8d1cd51d8ccb44b519125633a9ff66`。復元ブランチは `checkpoint/before-rule-hand-pose-20261021`。
旧 `MidiTipCorrectionProcessor` / `DisplayFingerSolver` は変更せず、OLDを既定値とする。
Session02を含む既存実測ファイルは読み書きしない。新方式の結果として流用しない。
Raw→記録→表示コピー→新旧いずれかの補正→表示スケルトン。Raw送信・既存MIDI/CSV列・較正は維持する。

## Phase 1: 姿勢計算

TrackingOriginLocalメートルで計算。親指はThumbMetacarpal–Proximal–Distal–Tip、他4指はProximal–Intermediate–Distal–Tip。
掌・手首・他4指のMetacarpal・各鎖の基部はRaw固定。親指CMCの並進と手首の移動は求めない。
各骨方向に対するCCDの回転後、前進投影でRawからの角度変化・隣接骨の曲がり角を制限。
骨長を毎フレームの有効Rawから取り、回転・方向再構築で保持する。Cartesianな関節位置の線形補間はしない。
同一フレーム全指をまとめて解き、手全体RMS変位制限を適用する。指ごとのMAXだけを並列適用する構造ではない。

## 制約優先順位

1. 有限値、有効追跡、因果時刻、固定基部、Rawの骨長。
2. 指ごとの全関節最大変位、手全体RMS最大変位、Rawからの角度逸脱上限。
3. 関節の曲がり角・観測できる曲がり側の維持。
4. 鍵盤の非貫通、割当済み打鍵指の接触。
5. 指同士の干渉低減、Raw姿勢への近さ、時間方向の連続性。

1・2は出力の安全条件として検査する。3–5は投影順・接触指の重み付けによる優先であり、厳密な辞書式最適化や全制約同時充足を保証するものではない。

Rawが既に範囲外、固定基部が鍵盤内部、MAX=0等で矛盾する場合は残差を許す。
「必ず全制約を満たす」とはしない。安全上限への角度縮小では骨長を保持し、必要ならRawへ戻る。
迭代数は1–16（既定8）で固定上限。フレームを保持せず固定長配列のみを使用する。

## 関節角の意味・根拠

初期値：MCP相当90°、PIP110°、DIP90°、親指は各90°を上限とする調整可能な位置ベースの包絡。
Rawからの各骨方向変化は25°以内。この25°は臨床標準ではなく、過補正を避ける工学上の初期値。
親指は広い個人差を踏まえ、単一の「正常値」を強制しない。

参考となる一次研究：

- [The Normal Active Range of Motion of the Index, Middle, Ring, and Little Fingers in a Sample of Indian Population](https://pmc.ncbi.nlm.nih.gov/articles/PMC11436331/)。指ごとの差・個人差があり、PIPは110°を超える個体もある。既定値は全個人に対する正常域の断定ではない。
- [Motion and morphology of the thumb metacarpophalangeal joint](https://pubmed.ncbi.nlm.nih.gov/14507504/)。親指可動域の大きな変動を示す。親指90°を人口の普遍的上限と扱わない。

本ソルバーの角度は隣接骨ベクトルの符号なし角度であり、臨床の解剖学的中立位からの屈曲・伸展角とは一致しない。
Rawの曲がり側が明確な場合に逆向き反転を抑制するが、直線姿勢で符号軸を識別できない場合は解剖学的な過伸展判定を保証しない。
関節ごとの完全な3自由度モデル・被験者別ROM較正は将来の検証事項とする。

## 評価の意味

仮想面との幾何学的残差・骨長・角度包絡・干渉量を測る。これらの減少はモデル内部整合性であり、独立した物理追跡精度改善を意味しない。
Quest 2の速度・姿勢の自然さ・実鍵盤整合・ピンチ操作は実機評価まで未確認。

## Phase 2: 環境と指間の結合

`RuleKeyboardGeometry` は既存の静止鍵bounds・有効上面を再利用し、白鍵より高い黒鍵、鍵間の隙間、Origin/Keyboard変換を考慮する。
鍵盤・Originを動かさず追跡座標との剛体変換だけを更新する。Geometry変化時のみ鍵上面を再構築する。
非貫通は各骨の両端・中央の球サンプル（半径既定3mm）による上面支持高さの近似。
許容深さ既定3mmは設定可能で、実鍵盤の沈下実測値ではない。静止仮想上面の基準を8mmの表示押下アニメーションへ置き換えない。
連続カプセル対鍵メッシュの厳密衝突ではなく、細い交差やサンプル間の貫通を見逃す可能性を残す。

指間は全10指対の各骨3×3区間の最近点を求め、カプセル半径既定5mmの重なりを軟制約で低減。
打鍵指と非打鍵指が衝突する場合、非打鍵指を主に逃がすが、両指の基部・変位・ROM上限を優先する。
非打鍵指の上面への軽い接触は許す。接触禁止フラグで全指を持ち上げる方式ではない。
解けない違反は `constraints_remaining` と実残差に残す。全制約を満たさない状態を成功0として記録しない。

## Phase 3: 因果的な開始・解除

Quest受信・Unity観測時刻のみで適用開始を判断する。PC senderの時計をQuest時刻へ無条件に混ぜない。
Dynamicで状態機械を進め、BeforeRenderでは直近Dynamicの角度補正を現在Rawの骨長へ再適用するだけとし、状態・gain・研究ログは進めない。
通常時は目標オフセットを低域通過・速度制限し、骨方向にも前回の角度補正からの回転速度上限を掛ける。
Rawそのものの追跡ジャンプを隠す機能ではなく、追跡喪失、時刻逆行、250ms超の欠測、座標変換変更ではRawへ復帰して新しい帰属を待つ。

Note Off時に最後の実角度補正を保存する。解除中は鍵上面へ再ターゲットせず、その角度をsmoothstepで恒等回転へ減衰する。
現在Rawの骨長で再構築し、各関節のRawからの変位ノルムを前回実変位×減衰率以下へ制限する。
これによりRawが上へ動いても解除中の接触補正変位が増えない。骨長維持を優先し、許容誤差は浮動小数点丸めの0.1µm以内。
Release中の指は環境ルールで押し直さない。他の指は引き続き独立に環境制約を受ける。
解除終了後に同じ指が再び貫通している場合、非打鍵指ルールが通常の速度制限下で再開する。全将来時刻の手全体変位が単調減少するという仕様ではない。

設定不正・対象指の追跡無効・Origin非剛体・較正未完了では表示を安全なRawへ戻す。
非有限入力はRawを変更せず、表示側だけPoseValid=false、Pose.identityとする。ゼロ補正では元のPoseをそのまま返し、再構築の丸めを発生させない。
既定の80ms engage/120ms release、25ms低域通過、0.25m/sのオフセット速度上限は調整可能な工学的初期値であり、Quest 2の知覚遅延を実測して最適化した値ではない。

## Phase 4: 明示割当と実行経路

補正対象は選択した片手の5本。両手10本の同時割当・自動打鍵指推定は含まない。
ノート0–127をThumb/Index/Middle/Ring/Littleへ手動割当できる。表示鍵がないノートは `key_unavailable`。
Manual Note Map OFFでは選択した1指・単音のみ。ONでは最大5音の明確な割当を受け付ける。
同じ指への同時複数ノート、未割当ノート、重複Note On、途中の割当変更は推測せずRawへ戻し、全Note Offと新しいOnを待つ。
BLE/UDP・channel・generationで所有を識別し、古いBLE世代は無視する。UDP snapshotから新規の打鍵指を捏造しない。
CC120/123・disconnectは解除し、CC64の音響的持続を指の接触保持と混同しない。

`HandCorrectionRouter`だけが表示経路を選択する。OLDとNEWを直列に重ねない。
OLDが既定で、既存のpublic Correction APIも保持。PC Display Pose選択時は既存のPC表示経路を優先し、NEWの結果として扱わない。

## Quest UIの使い方

1. 従来どおり鍵盤較正し、CORRECTIONタブで左右・選択指・Strength・共通MAX・共通PAD/Y BANDを設定。
2. `NEW RULE SETTINGS` → `METHOD`で条件を巡回する。
   - OLD（Legacy、初期値）
   - ContactOnly：NEW接触のみ（骨長・基部・変位安全制約は常に有効）
   - JointConstraints：NEW接触＋関節包絡
   - FullRules：上記＋非貫通・指間干渉
   - Raw：補正OFF
3. R1/ROM/R2/INTERFERENCEを個別変更できる。method名だけで条件を推定せず、設定ログの実フラグも評価に使う。
4. `NOTE MAP / LIMITS`でMap ON、ノート番号を選び、現在の選択指へASSIGN。別指の選択はCORRECTION画面または`MORE JOINTS / PAD`のNEXT FINGER。
5. 同時に保持するノートが同じ指へ割当されないようにする。単音の場合はMap OFFでも5指を選択可能。
6. 各指のMAX・Raw角度包絡・曲がり角上限・指腹オフセットXYZ、手全体RMS上限、反復数、開始/解除/平滑化時間を調整可能。

共通MAXと各指MAXの**小さい方**が全関節に適用される。RMS上限は選択手の有効な15可動関節（5指×3、固定基部を除く）の変位ノルム二乗平均平方根。
StrengthはR1要求と環境投影の更新量へ掛ける。0なら新たな環境補正を発生させず、既存の角度補正は速度制限下でRawへ近づく。複数制約と上限があるため、Strengthに対する実変位の線形比例は保証しない。
無効指がある場合は有効関節だけを分母とする。掌・手首は変位0で固定。
既存のMIDIロック、選択解除待ち、確認ダイアログ、パネル移動ハンドルは共通のまま。
Raw記録中の方式変更自体は記録を停止しない。設定revisionが変わるため解析時は異なる条件を分離する。

## 追加ログ（既存ファイル・列は変更しない）

- `quest_rule_pose.csv`：Dynamic callbackごとに選択手5指の行。requested/executed method、enabled、Raw/Corrected tipと変位、割当ノート・source/channel/generation・On/Off参照、接触水平/符号付き法線/許容帯逸脱、非貫通・干渉の前後、骨長誤差、角度包絡違反、反復回数、目標残差、NEW処理時間、設定revision。
- `quest_rule_pose_joints.csv`：同じcallbackの選択手全26関節。Raw/Corrected位置はKeyboardRootLocal、Corrected回転はTrackingOriginLocalと列に明示。Raw回転は従来Rawログへjoin。
- `rule_pose_settings.jsonl`：設定が変わったcallbackだけを記録。比較は全設定のビット列を比較しておりハッシュ衝突には依存しない。common/rule設定はJSON文字列として保存し、異常設定もJSONL自体を壊さず表現する。

`schema_version=1`。session_id、callback_index、snapshot_idでRaw/変換と結合する。
OLDも専用新ログへ比較用指標を記録するが、既存 `quest_tip_correction.csv` の意味・列は変えない。
NEW実行中の既存単一指CSVはヘッダーのみとなり、NEWをOLDとして記録しない。
非打鍵指はnote=-1。onset_event_indexはNEWの割当Onを指し、event_indexは対応Offが来ればそのイベントへ更新。
OLDのonset_event_index・onset時刻・NEW処理時間は取得できないため空欄または-1とし、存在しない値を補完しない。
MIDI参照列はnote>=0かつsourceが非空の行に対して解釈する。未割当行のMIDI初期値0は実際の受信・観測時刻として使わない。
反復数・処理時間は**手全体の1 callbackの値を5行へ反復**しており、指別値として合計しない。
接触指標は有効な割当鍵がある場合のみ。座標が無効な場合は数値欄を空欄とする。solver_finger_validはソルバーで有効な鎖かを別に示す。
幾何学的metricsは有効座標・形状に対して計算できるため、`coordinates_valid`は補正が実行されたという意味ではない。

tip_target_residualは平滑化したR1目標（非打鍵指はRaw目標）への距離であり、複数軟制約すべてをまとめた最適化の収束証明ではない。
angular_violation_degは符号なし曲がり角上限の残差であり、Rawからの変化角・臨床ROM全自由度を包括する値ではない。
target_cappedはR1の要求ベクトルに対するMAX適用、solver_limitedは手全体の安全縮小を示す別のフラグ。解除では新たなR1目標を生成しないためtarget_capped=falseとする。
キューへ渡すのは同期シリアライズ済み文字列のみ。HandPoseFrameや配列の参照を非同期保持しない。
記録自体のCPU/GC・ストレージ負荷もQuest 2で別途評価する。

## 再現可能な検証手順

Unity 6000.5.10f1。既存パッケージのみ。新規外部パッケージなし。

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -nographics -projectPath '<project>' -runTests -testPlatform EditMode -testFilter RuleHand -testResults '<out>/focused-edit.xml' -logFile '<out>/focused-edit.log'
& '<Unity Editor>/Unity.exe' -batchmode -nographics -projectPath '<project>' -runTests -testPlatform PlayMode -testFilter RuleHand -testResults '<out>/focused-play.xml' -logFile '<out>/focused-play.log'
# 最後にtestFilterを外して全EditMode / PlayModeを1回ずつ実行
& '<Unity Editor>/Unity.exe' -batchmode -nographics -quit -projectPath '<project>' -executeMethod QuestPianoMotion.Research.Editor.PianoDistributedSceneBuilder.BuildAndroidValidation -logFile '<out>/android-build.log'
```

既存builderのQuestシーンのみ、Android/IL2CPP/ARM64を使用。生成APK・実験CSV・キャッシュをコミットしない。
