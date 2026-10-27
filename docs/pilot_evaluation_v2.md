# 本実験前の評価システム v2：BLE予備検証

対象：Quest 2 / Unity 6000.5.10f1 / `feature/quest-rule-hand-pose`。FullRulesの優位性を仮定せず、制約残差と代償を分離して観測する。外部手指3D Ground Truthは使っていない。距離減少は**表示と既知鍵盤形状の整合性**であり、実手指への絶対精度を示さない。実機5試行はまだ未実施。

従来の`analyze_rule_effect.py`とschema 1条件JSONはそのまま使用可能。今回の主入口は`tools/evaluate_pilot.py`、条件schemaは2。先の65試行計画を実施する前に、本書の5試行品質検証を通す。今回の条件比較にはRaw・Legacy・FullRulesを含められる。以前の「Legacyを主比較対象にしない」というschema 1の計画は履歴として保持する。

## 調査結果と再利用

`HandCorrectionRouter`、`RuleHandPoseProcessor`、`MidiTipCorrectionProcessor`、`RuleHandPoseSolver`、`RuleKeyboardGeometry`、`QuestRawSessionLog.RulePose`、既存解析・テスト・ビルダーを確認した。既存XR Hands 1.8.1 / OpenXR 1.17.1 / XRI 3.5.1と公式サンプルを維持し、外部パッケージは追加しない。公式XR機能は追跡・入力・表示を提供するが、本研究の区間別試行評価は提供しないためPython標準ライブラリで補う。

補正器は毎回Rawを表示コピーへ複製する。FullRulesはdisabled、追跡/較正/変換無効、割当競合等でRawへ戻る経路を持ち、Releasingでは直前の補正角度・変位を減衰させる。Legacyも停止時のHardResetとNote Off後のReleasingを持つ。したがって状態名だけから表示座標を推定できない。**常に同callbackの`quest_rule_pose_joints.csv`のcorrected座標を評価する。** Raw代入で停止サンプルを作り直さない。

Rawをsnapshot行列から独立にKeyboardRootLocalへ再構成し、ペア内Rawと2 µm未満で照合する。session、callback、時刻、hand、joint、snapshot、設定revision、requested/executed method、較正IDを検証する。Rootの非剛体変換、未確認運指、異方式・設定変更の混入、不完全記録、UDP混入は拒否する。元CSVの列・意味は一切変更しない。Session02のように新しい全関節ペアがない記録から、存在しない全指補正を復元しない。

ログはDynamic callbackの補正処理後姿勢である。BeforeRenderの別更新、レンダリング完了時刻、画面上の光子時刻まで記録したものではない。この限界を保ち、映像・実機で表示と一致することを確認する。

## A：制約充足度

以下は各フレームのRawと実表示後の両方について計算する。単位は内部m、出力mm。白鍵は黒鍵の静止投影を除いた矩形和集合を使い、最近点の同距離時は距離二乗、点X、点Z、矩形tupleの辞書順で一意にする。

指腹モデル点p、対象鍵有効面S、XZ最近点q、静止上面hとして：

| 指標 | 計算・解釈 |
|---|---|
| X/Z逸脱 | `abs(p.x-q.x)`, `abs(p.z-q.z)`。面内なら0 |
| Y符号付き距離 | `p.y-h`。上正、下負。絶対値、浮き、貫通を別列で保存 |
| 上面残差 | `sqrt(E_X² + E_Y² + E_Z²)`。時間は合算しない |
| 許容帯逸脱 | `max(0, Y-aboveTolerance, -belowTolerance-Y)` |
| 対象鍵制約違反 | XZ距離がplanarToleranceを超過、またはY許容帯超過。判定の数値許容0.01 mm |
| 鍵盤貫通 | 各3骨の端点/中点で、半径rの球と静止鍵上面の残差を計算。`max(0,h+sqrt(r²-dXZ²)-permittedDepth-p.y)`、dXZ≤r。最大を採る |
| 骨長保持違反 | 対応するRaw/表示骨長の差の絶対値。各指3骨の最大。0.1 mm超を違反とする。Raw骨長を物理真値とは呼ばない |
| 関節角違反 | 位置から計算した符号なし屈曲角の包絡超過と、Raw骨方向からの最大変化角超過を別計算。設定上限＋数値許容0.1度で判定 |
| 指間干渉 | 異なる指の全3×3線分対の最近距離dから`max(0,2*interferenceRadius-d)`。指ごとの最大。4本の相手指が有効なときのみ全指評価として採用 |
| 未充足率 | 制約ごとの超過率と、いずれかの違反率。未評価制約は`constraint_coverage_rate`へ記録。未評価を充足0へ置換しない |

貫通と干渉はRuntimeと同じ**近似モデルを独立再計算**する。連続メッシュ衝突の真値ではなく、鍵上面より下の半空間を含むモデルである。臨床的な関節ROMや絶対骨長の正しさを示さない。設定OFFでも幾何量は測定するため、設定ON/OFFと残差を別に読む。FullRulesの予備品質検証では4制約すべてONを要求する。

指腹オフセットはRuntimeと整合させ、Legacyは共通オフセット、FullRules/Raw経路は共通＋指別オフセットを使う。方式間で同じ研究上のモデル点を比べる場合は、指別オフセットを0にして全条件で統一する等、**実験前に同じ点を定義**する。異なるオフセット・較正・強度の条件を方式差だけと解釈しない。

## B：補正コスト

各関節の`δp = p_display - p_raw`、XYZの符号付き成分、`||δp||`を保存する。各指4点のRMS、記録された手全体の全関節RMS、手首・掌の変位を別計算する。手全体RMSは全対象関節が有効なときのみ計算し、有効関節だけに黙って縮小しない。単音では非打鍵4指、和音では実際に打鍵していない指を別集計する。

補助比は`tip_cost_mm / (Raw上面残差 - 表示上面残差)`。改善量が**0.1 mmを超える正値**の場合のみ定義し、ゼロ近傍・悪化では空欄にする。主結果は比で順位付けせず、`residual_vs_cost.svg`で**残差とコストを別軸**にする。1点は1試行平均である。Rawからの逸脱は補正の代償であり物理的な真値誤差ではない。

## C：時間的連続性

Raw、表示、補正ベクトルδpの3系列を分ける。実測時刻t_iから、位置差、速度、加速度、ジャークを求める。

- `v_i=(p_i-p_(i-1))/(t_i-t_(i-1))`、速度の代表時刻は2点の中点。
- `a_i=(v_i-v_(i-1))/(tau_v_i-tau_v_(i-1))`、加速度時刻も対応する速度時刻の中点。
- ジャークは加速度差をその代表時刻差で割る。

等間隔サンプリングは仮定しない。追跡無効、snapshot変更、dt<1 ms、dt>100 msで履歴を切り、微分を作らない。dt分布もtrial_metricsへ出す。平滑化していないため、特にジャークは追跡ノイズと時刻揺らぎを増幅する。高次差分の大小だけで知覚的な自然さを断定しない。

急変率は補正ベクトルのフレーム間変化>5 mmまたは速度>500 mm/sを事前定義する。演奏者の意図したRaw運動を補正の急変に混ぜない。開始/解除の境界変位、最初の適用callbackまでの時間、Active状態までの時間、Note Off直前の最後の変位、解除後最大増加量、解除中の増加率を記録する。

解除完了はNote Off後に変位≤0.5 mmが100 ms以上・3サンプル以上続いた区間の先頭。追跡無効・間隔超過・snapshot変更で連続判定を切る。次のNote On、観測終了、欠測、未完了、実Note Off欠落は打切りとして保存し、0 msを作らない。時刻はcallbackによる区間推定で、物理接触の真値へ補間しない。`transitions.csv`のイベント値も試行ごとに平均・中央値・p95・最大・打切り率へ要約する。

## D：非打鍵指・非打鍵区間

全5指を毎callback評価し、実運指からpressing/nonpressingを区分する。非打鍵指のδp、各指RMS、手首・掌変位、骨長・角度、干渉、静止時や解除後の姿勢変化を別に読む。非押鍵鍵への貫通は観測されたMIDI状態に基づき、`observed_released`と`unknown`を別列にする。未観測source/channelの「打鍵なし」を真値として仮定しない。欠測指を0変位で埋めない。

## 区間と停止の扱い

| 区間 | 選び方 |
|---|---|
| approach | 実際の打鍵指について、観測Note On前250 ms。事後の区間ラベルのみ。未来の鍵を使った対象鍵残差は計算しない。誤補正コストを見る |
| engage | 観測Onから設定engageSecondsまで。適用失敗でもこの区間に残す |
| hold | 上記以降〜実Note Off。実際のstate/gain/理由も別列に残す |
| release | Off後1秒以内、または次の同指Onまで。補正が残る実表示を評価する |
| nonpressing | それ以外。各区間にrole=nonpressingも存在し、他指や無打鍵区間を集計できる |

補正停止は、押下区間で補正が要求される条件において、追跡・割当・enabled・solver有効・gain>0を満たす接触適用状態が確認できないこと。Raw条件では停止率を失敗として扱わない。FullRulesの非打鍵指に環境制約だけが適用されることはあり、接触適用フラグがfalseでも実変位は測定する。制約残留は適用状態とは別指標であり、適用率を全制約成功率と呼ばない。

無効追跡は欠測率・停止率の分母に残すが、残差・変位・微分には架空値を入れない。記録callback間の100 ms超の空白と試行境界の空白も品質判定に使う。ログに存在しないフレーム数を推測して補完しない。

## 較正の検証

1. 従来のA/B/C較正を完了する。
2. 較正に使っていない既知点V1〜V4以上を用意する。例：中央白鍵の前端、右寄り白鍵の前端、中央寄り白鍵の奥側、黒鍵の上面。A/B/Cを検証点へ再利用しない。同じ座標の点を名前だけ変えて4点にしない。
3. 定規・治具の寸法と基準鍵を記録し、KeyboardRootLocalでのreference_keyboard_mを定義する。鍵面高さ・黒鍵高さも区別する。座標0のテンプレートを測定値として使わない。
4. Quest指先を点へ慎重に合わせたcallbackを記録し、`pilot_calibration.example.json`へpoint_id、較正ID、callback、hand/joint、物理寸法資料を記入する。Rawから独立再構成してXYZ差とノルムをmmで保存する。これは**較正＋Quest追跡＋指の置き方を含む登録残差**であり、独立した純粋な較正誤差ではない。
5. 外部測定器を使える場合は、外部系からKeyboardRootへの登録方法・登録ID・計測不確かさとobserved_keyboard_mを保存する。Quest由来測定とは別methodで集計し、両者を混ぜて4点に数えない。外部測定に対応する構成だが、今回の外部測定は未実施。

## 映像・BLE・Quest時刻の検証

固定カメラは鍵面と実指の接触を判読できる位置に設置し、動画FPSを記録する。別の表示録画または同じ画角の画面で、対応する仮想鍵ハイライトを確認できるようにする。開始・中間・終了に識別打鍵列を入れ、**演奏試行の外側を含む3点以上・10秒以上の時刻アンカー**を採る。全アンカーは試行を挟み、外挿しない。

`Quest秒 = intercept + slope × 映像秒`を最小二乗推定し、残差p95、clock drift ppm、動画の半フレーム量子化誤差とアンカー不確かさを保存する。

- 独立した共通イベントを両時計で記録できる外部トリガ等がある場合だけ`independent_shared_event`を指定し、その証拠資料を記録する。
- 現行の仮想鍵ハイライトを使う場合は`display_event_proxy`。対応するBLEイベントを示し、描画遅延の事前上限も不確かさへ加算する。これは表示経路のバイアスを含む見かけの時計合わせである。独立した接触→MIDI遅延や無線転送遅延と断定しない。
- **物理接触そのものとNote Onを同じ時刻アンカーにはしない。** それをすると測りたい遅延を同期オフセットに吸収する。

そのうえで、映像で注釈した物理接触の映像秒をQuestへ変換し、接触→MIDI受信、接触→Unity観測、受信→観測、観測→最近の手指callbackのオフセットを別計算する。最近callbackは時刻対応の診断値であり、因果的な表示遅延ではない。試行ごとに中央値・標本SD・p95を出す。負値もそのまま残す。ソースはBLEに固定し、UDPを1件でも含むセッションは今回の予備解析へ混ぜない。

## 事前合格基準と5試行

初期基準は`pilot_protocol.defaults.json`に定義した。生理学的な有効性基準ではなく、収録・登録・同期・適用確認の**運用上の予備品質基準**である。

| 項目 | 固定した基準 |
|---|---|
| 試行 | 5課題を各1試行、計5試行。FullRulesを含む。推奨は5試行ともFullRules |
| ログ | 既存8ファイル、writer_closed=true、overflow=0、errorなし。ペア・設定・較正・MIDI参照が検証可能 |
| MIDI | BLEのみ。実On/Off対応あり。未対応、押下中切断、generationリセット、CCによる代用解除なし |
| フレーム | 全指の有効率≥95%、callback空白≤100 ms（境界含む） |
| 停止・適用 | FullRules接触適用率≥90%、補正要求中の停止率≤10%。実変位数も別報告。改善量の正値は合格条件にしない |
| 制約設定 | FullRulesのcontact/joints/nonpenetration/interferenceすべてON。数値設定はrevision JSONに存在 |
| 較正検証 | 同一測定methodで異なる未使用4点以上、残差p95≤10 mm。不確かさとQuest依存性を併記 |
| 同期 | 試行を挟む3アンカー以上・10秒以上、当てはめ残差p95≤20 ms、合成不確かさ≤50 ms、絶対drift≤2000 ppm、各Note Onに接触映像注釈あり |
| ビルド | 実際のAPK SHA-256・サイズ・ビルド時コミット/dirty・Unity版・ビルド時刻がsidecarとsession metadataで一致 |
| Raw比較追加時 | OFF設定が確認でき、実表示変位≤0.01 mm。実値は残し、不一致をRawへ書き換えない |

開始前に閾値を凍結し、lockのSHA-256と日時を台帳へ保存する。lockをバックアップし、可能なら測定前にそのハッシュをGitの実験計画記録などへ残す。自己申告のPC日時だけが第三者によるタイムスタンプ証明になるわけではない。閾値変更は新しい事前計画・新しい収録として扱い、既存データへ遡って合格にしない。解析はpayloadハッシュと条件JSONの期待ハッシュを照合し、session startより後のlockを拒否する。

推奨の5試行（右手、各試行を別の20秒程度の収録にすると管理しやすい）：

1. 白鍵60、人差し指1回、1秒保持。
2. 黒鍵61、人差し指1回、1秒保持。
3. 5指音階60,62,64,65,67,65,64,62,60、指1,2,3,4,5,4,3,2,1、60 BPM。
4. 60を人差し指で10回、60 BPM、On/Offを明確にする。
5. 和音60/64/67を親指/中指/小指で同時に1回、1秒保持。

各収録で較正を確認→必要ノートを手動Map→FullRules 4制約ON→固定映像開始→記録開始→同期用識別列→試行→全鍵解除後1秒以上→終了側識別列→記録停止。同期アンカーを試行の外側へ置き、運指映像を確認する。開始/終了は絶対Quest秒で指定し、On/Offと解除尾部を区間内に含める。運指が不明なら確認済みにせず理由を記録する。5試行は効果量の確証実験ではない。

## 実行と生成物

```powershell
# 新しいローカルデータ領域。初期閾値のlockと空の条件・検証ファイルを作成
python tools/evaluate_pilot.py init-study --output C:\Users\Yuki_\QuestPilotV2

# 初期閾値を変更するなら、測定前に別領域で新しいlockを作る
python tools/evaluate_pilot.py lock-protocol protocol.json --output protocol.lock.json

# 回収後、従来のinventoryで時刻・revision・MIDIキーを調べる
python tools/analyze_rule_effect.py --inspect-session C:\Users\Yuki_\QuestPilotV2\sessions\UUID_Quest --output C:\Users\Yuki_\QuestPilotV2\inventory\UUID

# templatesを参考にconditions.json、calibration_verification.json、synchronization.jsonを記入
python tools/evaluate_pilot.py analyze C:\Users\Yuki_\QuestPilotV2\conditions.json --output C:\Users\Yuki_\QuestPilotV2\results01
python -m unittest discover -s tools -p "test_*.py" -v
```

`init-study`はtrials=[]なので、収録前にも空の図・表を生成できる。実測なしと表示し、ready=falseにする。データが入った場合も、`data_kind=synthetic_test`は全図にテスト専用表示を付け、本実験へのready判定をtrueにしない。

使用テンプレート：`pilot_conditions.example.json`、`pilot_calibration.example.json`、`pilot_sync.example.json`。パスは条件JSON基準。Raw OFFをFullRules経路で収録した場合、`method=Raw, logged_method=FullRules`とし、common.enabled=falseを実ログで検証する。LegacyとFullRulesはrequested/executedも一致を要求する。方式・revision・較正を試行途中で変えない。

| 出力 | 内容 |
|---|---|
| phase_frames.csv / joint_costs.csv | 全5指・区間・役割別の独立再計算値、全関節変位、停止・欠測・実状態 |
| trial_metrics.csv | 指別/全指、区間/役割ごとの平均・中央値・p95・最大・SD、違反/停止/有効/打切り率。単位を明示 |
| method_summary.csv | 方式・課題・左右手・全設定SHAごとに分離し、被験者内試行平均→被験者等重みで集約。階層bootstrap CI、人数・試行数・探索的フラグ |
| transitions.csv | 各On/Off周辺の変位、開始/解除時間、解除後増加、打切り |
| calibration_residuals.csv | Quest由来と外部由来を識別した検証点XYZ残差 |
| synchronization_summary.csv / contact_midi_offsets.csv | 時計写像、残差、drift、不確かさ、接触/MIDI/手指時刻対応 |
| pilot_quality.csv / pilot_gate.json | 各試行の事前基準pass/fail、停止・欠測・適用数。5課題5試行の品質判定 |
| A/B/C/DのSVG、residual_vs_cost.svg | 制約、コスト、連続性、副作用を分離。各値は試行要約 |
| conditions/protocol_snapshot.json、analysis_manifest.json | 固定条件、閾値、コード/全入力/APKのSHA-256。入力は解析後も照合 |

同一callbackのRaw/表示後は対応観測である。フレーム数や指数を独立被験者数にしない。各試行を先に要約し、同一被験者の試行をその人の中で平均してから集約する。95% CIは被験者→試行の階層percentile bootstrap（2000回、seed=20261027）。1人なら個人内の探索的CI、1試行ならCI未定義。p95/最大は試行内のフレーム分布で、被験者間分布とは区別する。停止・欠測のある試行を除外して見栄えを良くしない。

追加のRaw/Legacy比較を行う場合は各方式について同じ指定課題・運指・較正・設定で別収録し、条件を分けたJSONで評価する。**別演奏を同一Rawの対応再生と呼ばない。** method_summaryの各方式値は別試行であり、同一人を別被験者として扱わない。5試行gateは5試行用なので、15試行の比較データを一括入力した場合、数が5でない旨のready=falseは仕様である。

## オフライン再実行の可否

現環境で可能なのは、保存済みRawと実表示後姿勢の幾何・コスト・時間挙動の再評価である。厳密なRaw/MIDI→複数solver再実行は今回実装しない。ログにはDynamic Rawがある一方、すべてのBeforeRender入力、watchdog呼出順、callback間の設定変更時刻、開始前のsolver内部状態、平滑化/解除の全内部変数、描画完了時刻が揃っていない。これらを無視したオフライン再実行を実機表示の厳密な再現と断言できない。将来の再生器は状態初期化、イベント順序、時計、設定履歴、更新種別を仕様化してから独立に検証する。

## APK証跡と保護

既存ビルダーは実ビルド前にcommit/branch/dirtyを取得してAPKへ埋め込む。追加した`.apk.provenance.json`はその時点の値を保持し、ビルド成功後のAPK SHA-256、サイズ、Unity版、シーン、target、ビルド時刻を保存する。APK自身のハッシュをAPK内部へ埋める自己参照は行わない。

回収する8ファイルは従来どおり（Raw、rule pose、rule joints、MIDI、transform snapshots、rule settings、session metadata、recording summary）。これに**実際にインストールしたAPKとsidecar、条件/lock、較正検証、同期注釈、固定映像**をローカル保存する。sidecarと実行ログのcommit/dirty/Unity/ビルド時刻を照合し、取り違えを品質failにする。APK、動画、個人情報、元実験CSV、テストXML/logはcommitしない。

現在のビルドは検証中の作業ツリーから作ったため、ビルド時commitは`9cf68d0476336d259534d42a1b2b9fc51fe32b8e`、dirty=trueである。作業完了後のcommitを、遡ってAPKビルド時commitとして記載しない。既存APKは`.utmp/pilot-build-backup/PianoDistributedQuest-before.apk`へ保存してからビルドした。

自動品質passは実機の自然さ・接触表示・追跡復帰・BLE安定性・カメラ注釈精度を証明しない。`physical_device_verified=false`を維持し、これらは実験者が別途確認する。補正の優位性・有意差・自動運指推定の必要性は実測結果を得てから判断する。
