# QuestPianoMotion：Raw / FullRules 表示整合性の予備実験

目的はMIDIと鍵盤形状を利用した補正によるXYZ方向の**表示上の幾何学的整合性**の変化を定量化すること。外部3D Ground Truthは使わず、実際の手指に対する絶対位置精度とは呼ばない。RawとFullRulesは同じ演奏・同じDynamic callbackの対であり、Raw条件の別演奏は主比較に使用しない。旧方式は保持するが比較対象から除外する。

## 再利用した実装と変更範囲

`QuestRawSessionLog`の8ファイル、既存`analyze_raw_session.py`のRaw読込・行列逆変換・関節鎖を再利用する。XR Hands 1.8.1、OpenXR 1.17.1、XRI 3.5.1、公式HandVisualizer等の既存パッケージ・サンプルを確認した。これらは収録・表示を担い、手動運指を確認した試行の対応統計機能は提供しないため、標準Pythonによる研究専用のオフライン解析のみ追加した。外部パッケージを追加せず、Unity Runtime、シーン、Packages、ProjectSettings、既存ログ仕様、補正器は変更しない。

主実装は`tools/analyze_rule_effect.py`。統計・表・SVG生成を一つのCLIで再現できる。CSV/JSON入力は書き換えず、入力SHA-256、条件ファイルと解析コードのSHA-256、乱数seedをmanifestに保存し、解析後にも入力ハッシュを照合する。既存出力ディレクトリへの上書きも拒否する。長時間ログはメモリを使うため短い収録に分ける。

## Questでの操作順

1. 既存のQuest 2用`PianoDistributedQuest`を起動。手の追跡、Passthrough、88鍵表示を確認する。PC表示姿勢ではなくQuestローカル補正を使用する。今回の解析変更だけならAPK再ビルドは不要。
2. BLE MIDIまたはUDP MIDIを一つ選び接続。ノート60を押してOn/Offと切断表示を確認。両経路から同じ演奏を二重送信しない。CC64は原則OFFで統一する。
3. 「位置合わせ」で保存済み較正を読み込むか、既存A/B/Cの3点較正を行う。Aは基準白鍵の前左、Bは右、Cは奥。白鍵・黒鍵の端で表示を確認する。較正ID、位置確認、日付、Quest設定を条件ファイルに残す。較正失敗時に既存値を上書きしない。
4. 「補正設定」で対象の右/左手を選択。「新方式のルール設定」→「補正方式」を新方式にする。接触、関節、非貫通、指間干渉の4制約をすべてON。「指先補正 ON/OFF」をON。ログのrequested/executedが**FullRules**であることが最終的な条件判定となる。
5. 「指の割当 / 補正制限」→「手動の打鍵指割当」をON。「すべての割当を解除」後、MIDIノートを選択し、対象指を「次の指」または「RULE NEXT FINGER」で選び「選択した指を割当」。必要な全ノートを設定する。画面の現在値で指とノートの対応を再確認する。白鍵単音60、黒鍵単音61も指を変えるたび再設定する。
6. 強度、最大変位、全指RMS上限、許容帯、接触オフセット、平滑化を記録する。予備実験中は同じ設定を維持し、設定変更時は全鍵とPinchを離して新しい試行にする。設定revisionが異なる記録を一試行に混ぜない。指腹はログの共通オフセット＋指別オフセットを適用したモデル点であり、実測接触点とは断言しない。
7. 固定カメラを手背側の斜め上に置き、両手・鍵番号・指が隠れない画角にする。可能ならQuest表示も別収録。匿名の試行IDを音声またはカードで記録し、冒頭にゆっくり3回の識別打鍵を入れてMIDIと映像の対応を確認する。この識別打鍵は分析区間外とする。映像は運指確認用で、3D位置真値や接触時刻の厳密な正解にはしない。
8. ローカル「記録開始」。1秒以上待ってから演奏し、全鍵を離した後も1秒以上待つ。追跡喪失・競合・停止を見ても収録を捨てない。「記録停止」を押し、停止確認とエラーなしを確認してから回収する。PC主導収録を使う場合はPC側Start/Stopで統一し、ローカル停止と混在しない。
9. 試行終了後、固定映像で各Note Onの**実際の指**を確認し、source/generation/event_indexと対応したassignmentsを記入する。誤運指は実際の指として記録し、設定との不一致は未適用理由として残す。確認不能ならvideo_verifiedをfalseのままにし、主解析に採用しない。除外理由・欠測試行数は実験台帳に残す。

UIの日本語表示は`DistributedQuestUi`に対応する。手動Mapは現在の対象片手の5指向けで、両手10指同時条件は今回扱わない。左手を行う場合は別収録・別試行とする。

## 演奏課題と回数

最初は1人・右手で操作予備実験を行う。実施可能なら匿名の3人程度に拡張するが、小標本の探索的結果として報告する。被験者数はフレーム数で代用しない。各課題の前に2回の練習を行い分析から外す。60 BPMを基本として疲労時に休憩し、テンポを変えたら条件を分ける。

| 課題ID | 指定演奏（右手、親指から1〜5） | 本試行 |
|---|---|---|
| white_single | MIDI60を各指1〜5で個別に1回、1秒程度保持、全鍵解除 | 各指5試行＝25試行 |
| black_single | MIDI61を各指1〜5で個別に1回、1秒程度保持、全鍵解除 | 各指5試行＝25試行 |
| five_finger | 60,62,64,65,67,65,64,62,60を1,2,3,4,5,4,3,2,1で演奏 | 5試行、各9打鍵 |
| repeated | MIDI60を人差し指で10回、保持と解除を明確にする | 5試行、各10打鍵 |
| chord | 60/64/67を親指/中指/小指で同時押下、約1秒保持、全鍵解除 | 5試行、各1和音 |

合計**65試行／片手／人**。左右両方なら130試行／人で、左右結果も条件として分ける。白鍵・黒鍵単音の指順と課題ブロック順は被験者ごとに事前に無作為化し、順序とseedを台帳へ記録する。各5試行を1ブロックとしてもよいが、JSONでは試行境界を分ける。各試行は前後の無打鍵区間を含め、絶対Quest秒で指定し、同じsession内で区間を重複させない。

和音の実施前に3音を正しい3指に割り当てる。未割当・同一指への重複割当・追跡喪失で補正が停止した試行は主記録から捨てず、適用可能率と状態理由を評価する。必要なら追加のストレス条件として運指変更・手動Map切替時間を別台帳に残すが、主5課題に混ぜない。自動押鍵指推定は実装しない。

## 回収ファイル

Quest側の`Application.persistentDataPath/PianoResearch/DistributedSessions/<UUID>_Quest/`をディレクトリごと回収する。旧`PianoResearch/sessions`と混同しない。

```powershell
adb devices
adb shell ls /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/DistributedSessions
adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/PianoResearch/DistributedSessions C:\Users\Yuki_\QuestResearchData\sessions
```

アプリIDが変わっている場合は実機のIDに置き換える。元データはコピー後も保管し、Gitへ追加しない。最低限回収する8ファイル：

- `quest_hand_raw.csv`：TrackingOriginLocalのRaw、callback、snapshot、追跡状態。
- `quest_rule_pose.csv`：5指の状態、MIDI対応、補正方式、設定revision、処理時間。
- `quest_rule_pose_joints.csv`：同callbackの全関節のRaw/表示後位置。
- `quest_midi_research.csv`：source、generation、イベント、受信時刻、Unity観測時刻。
- `transform_snapshots.jsonl`：追跡原点と鍵盤の変換・静止鍵形状。
- `rule_pose_settings.jsonl`：共通設定と指別設定の実値。
- `session_metadata.json`：session_id、較正・ビルド・デバイス情報。
- `research_recording_summary.json`：writer_closed、queue_overflows、error。

併せて固定映像、表示録画、実験台帳、必要なら較正保存JSONとPC側のネットワークログも安全なローカル領域へ保存する。個人情報、映像、APK、元CSVはcommitしない。

## 解析の実行

プロジェクトルートでPython 3.10以上を使う。検証環境はPython 3.14。外部依存なし。

```powershell
python tools/analyze_rule_effect.py --inspect-session C:\Users\Yuki_\QuestResearchData\sessions\UUID_Quest --output C:\Users\Yuki_\QuestResearchData\inventory\UUID
```

出力`session_inventory.json`でイベント・設定revision・時刻範囲を確認する。指は推定せずnullになる。`docs/rule_effect_conditions.example.json`をデータ領域の`conditions.json`へコピーし、匿名被験者、試行ID、task、session_dir、session_id、開始/終了秒、設定revision、較正IDと確認結果を記入する。`session_dir`の相対パスは**条件JSONのある場所**が基準。taskは上表の5種のみ。assignmentsは各正速度Note Onごとに実映像で確認したsource、generation、onset_event_index、note、hand、fingerを記入する。fingerはThumb/Index/Middle/Ring/Little。和音も各音に別entryを作る。テンプレートの時刻・ID・運指をそのまま実測条件として使わない。

`static_intervals`は試行内で実験者が静止と確認した絶対時刻区間の配列（例：`[[123.1,124.1]]`）。指定しなければジッタ図は未測定テンプレートになる。演奏中の速度・加速度をそのままジッタと呼ばない。区間両端は収録callback範囲内とし、開始を含み終了を含まない。

```powershell
python tools/analyze_rule_effect.py C:\Users\Yuki_\QuestResearchData\conditions.json --output C:\Users\Yuki_\QuestResearchData\analysis\run01
python -m unittest discover -s tools -p "test_analyze*.py" -v
```

新しい出力フォルダを毎回指定する。欠落、記録途中、混在session、座標不一致、Raw再構成不一致、異なる方式、較正ID/設定revisionの不一致、未確認運指を拒否する。FullRulesというmethod名でも制約フラグが欠けるアブレーションは主解析に採用しない。補正OFF・強度0・手動Map誤り・競合・追跡喪失は、方式/設定条件が適格ならサンプルの状態として保持し、成功ペアだけの抽出はしない。

実測がない場合：

```powershell
python tools/analyze_rule_effect.py docs/rule_effect_conditions.empty.json --output .utmp\rule_effect_empty
```

ヘッダーのみの集計CSVと`NO MEASURED DATA; template only`を表示したSVGが生成される。合成テストログは一時ディレクトリ内だけで作り、実測グラフに流用しない。

## 指標と対応付け

`session_id / callback_index / hand / joint_name`でRawと表示後を結び、同じQuest時刻、snapshot、設定revision、FullRules、較正IDを検証する。Rawを`inverse(KeyboardRoot.localToWorld) × TrackingOrigin.localToWorld × Raw`で独立に再構成し、CSV内のRaw値とも2 µm未満の許容差で照合する。スケール/せん断のある追跡原点・鍵盤Rootはmの意味が変わるので拒否する。KeyboardGeometryの較正寸法はsnapshotの静止鍵boundsに既に反映されている。座標軸はX=右、Y=上、Z=奥。

白鍵矩形から全黒鍵の静止投影矩形を差し引き、複数矩形の和集合を作る。黒鍵は静止上面矩形。最近点はXZユークリッド距離で選択し、同距離なら最近点X、Z、矩形tupleの辞書順で一意にする。鍵中心は使用しない。最近点のX/Z成分までの絶対距離をE_X/E_Zとし、面内なら双方0。最近点が切り欠きの側方になる場合、独立軸ごとの単純bounds逸脱とは異なる。

Yは静止上面との差を符号付きで保存（上正、下負）。主比較E_Yはその絶対値。浮き=max(Y,0)、貫通=max(-Y,0)、許容帯逸脱=max(0,Y-above,-below-Y)を別指標にする。これらのY距離は対象鍵面の基準平面に対する量であり、XZが面外でも計算する。物理的な接触・鍵内部体積への侵入とは区別する。実鍵の押し下がりは測定せず、仮想8 mmアニメーションも物理真値として使わない。

Δ=E_Raw−E_FullRulesをmで計算し、表・図ではmm。改善率はΔ/E_Raw×100、E_Raw=0は未定義（CSV空欄、JSON null）。試行の平均誤差からの改善率と、定義されるフレーム改善率の平均を別列にする。改善/悪化/不変は距離差1e-9 mを数値上の同一判定閾値として用いる。未定義率を0へ置き換えない。

主対象は映像で確認された実押鍵指で、MIDI観測Note On〜Note Offの区間。CC64で押下区間を延長しない。速度0 Note OnはOff、CC120/123は当該channelを解除、合成切断とgeneration遷移はsource状態を無効化する。Note Onは物理接触時刻ではない。MIDI受信、Unity観測、手姿勢callback時刻を別列に保存し、送信側時計と混ぜない。描画表示時刻そのものは現ログから復元できず、callback内表示予定姿勢の時刻を記録する。表示遅延を測るには別途映像・Profiler計測が必要。

追跡無効は主誤差を欠測とし、予定サンプル数・未適用理由には残す。追跡有効で補正停止している場合は表示後の実値を評価する。`applicable`は正しいMap/MIDI割当、enabled、solver有効、gain>0、追跡有効を満たす適用状態で、**全制約を満たした成功率ではない**。制約残留・cap・solver_limitedは別の診断列で確認する。補正成功率を必要とする場合は事前に各残差の成功閾値を定義して別集計する。

## 統計と出力

| 出力 | 用途 |
|---|---|
| paired_frames.csv | フレーム・実押鍵指・音ごとの独立再計算誤差、改善量、改善率、追跡/適用/理由、受信/観測時間 |
| trial_statistics.csv | 試行×全体/指/鍵種/課題×方向の平均、中央値、標本SD、改善量、改善率、改善/悪化/不変率、適用率、有効数 |
| effect_summary.csv | 被験者を等重みとした試行対応結果、95% CI、有効試行数・被験者数、悪化試行率 |
| applicability.csv | 予定の指音フレーム数を分母とする適用/未適用理由の度数 |
| side_effects.csv | 独立再計算の骨長・骨長変化、屈曲制約超過、関節移動、速度/加速度、静止ジッタ、非押鍵面への指腹侵入、logger近似診断 |
| solver_diagnostics.csv | 全5指の内部診断を明示区分して保存。主幾何指標の代用にしない |
| processing_time.csv | callback当たり1行のFullRules処理CPU ms。全指で重複計数しない |
| event_trajectories.csv | 各イベント前後250 ms、押下保持と解除後250 msの軌跡、Raw/補正後、受信/観測相対時刻 |
| analysis_manifest.json | 入力・コード・条件ハッシュ、評価定義、再現設定、限界 |

連続フレームは独立標本としない。各試行内の有効ペアを先に平均し、試行を被験者内で平均、被験者を等重みとして集約する。95% CIは被験者を復元抽出し、その被験者内の試行も復元抽出する階層percentile bootstrap（2000回、seed=20261027）。被験者1名なら個人内試行の探索的CIであり母集団推論をしない。試行1件ではCIを未定義にする。同一フレーム内の和音指音も独立被験者に数えない。中央値・SDはtrial_statisticsのフレーム記述統計、集約のtrial_delta_median/SDは試行平均改善量の分布であり区別する。p値・有意差の自動主張は行わない。多方向・多数の副次指標から都合のよいものだけを選ばない。

入力の被験者・試行数が十分かは研究計画で判断する。今回は予備実験として常にexploratory=trueを出す。欠測が補正状態と関連し得るため、適用率・追跡欠測率と併記する。追加条件を混ぜる場合は条件JSONを分けて解析し、設定違いを黙って一つの効果へ集約しない。

## 卒論へ使える図

SVGはベクトル図で軸・単位・方式・サンプル数を含む。実測解析後の以下を使用できる（空テンプレートは結果図に使用不可）。

1. `01_xyz_comparison.svg`：Raw/FullRulesのX/Y/Z平均表示ずれ。
2. `02_xyz_improvement.svg`：XYZ改善量。正が改善、負が悪化。
3. `03_finger.svg`：5指別改善量。
4. `04_key_type.svg`：白/黒鍵比較。
5. `05_task.svg`：単音・5指音階・連打・和音比較。
6. `06_*.svg`：骨長変化、角度制約、非押鍵指移動、手首掌移動、速度/加速度、静止ジッタ、非押鍵面侵入、近似干渉、CPU処理時間。loggerと名のつく図は補正器内診断である旨を残す。
7. `07_event_*.svg`：イベントごとのXYZ軌跡。観測時刻を基準とし、青Raw/赤FullRules。snapshot変更・追跡欠測・100 ms超の間隔で線を切る。

図注に被験者数・試行数・有効/予定ペア数、較正、テンポ、運指、設定revision、使用条件JSONとmanifestを示す。CIの数値はeffect_summary.csvの対応する列から表へ転記する。グラフは平均の比較で、エラーバーは描いていない。異なる単位の骨長・角度・時間を一つの距離に合算しない。

副作用の指間干渉はloggerのカプセル近似、非貫通のlogger値は端点/中点サンプル近似であり、連続メッシュ衝突の真値ではない。独立非押鍵評価は有効面下の指腹点に限り、MIDI状態がunknownなら非押鍵と断言しない。observed_releasedも観測されたsource/channelに限る。速度・加速度は平滑化なしの有限差分で、時間刻みや追跡ノイズの影響を受ける。現在の骨長・屈曲は幾何学的整合性で臨床的ROM評価ではない。

## 自然さ・時間連続性・Quest負荷の補助評価

表示録画で試行IDを匿名化し、可能なら順番を無作為化して2名程度に5段階で「指の自然さ」「押鍵時の一致」「開始/解除の跳び」を評価してもらう。Raw表示映像が同時に得られない場合、同一演奏の知覚的な対応比較とは呼ばず、FullRulesの自然さの補助評価として扱う。評価者、採点基準、判定不能数を残す。

開始/解除は07の軌跡と06の速度/加速度を合わせ、Note On、Off、補正状態遷移をevent_trajectories.csvで照合する。時刻tをXYZに加えて距離としない。補正処理CPUはprocessing_time.csvで平均・最大・分位を別途確認し、端末負荷はQuest実機のProfiler/フレーム時刻/GC/記録キュー・温度による変化を収録中と非収録時に確認して台帳へ記録する。CPUログだけからFPS改善、描画遅延、総負荷を推定しない。

鍵盤制約への残差減少は補正の構造から予想される。これだけで提案手法全体の有効性を証明せず、副作用、失敗理由、適用率、時間挙動、映像評価を併記する。自動運指推定の判断は[判断テンプレート](rule_effect_decision_template.md)で行い、未測定段階で結論を決めない。
