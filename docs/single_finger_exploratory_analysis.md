# 本人申告の単一指セッション診断

`tools/explore_single_finger_session.py`は、正式解析に必要な固定映像による運指確認と手動ノートマップがない単一指収録を、本人申告を前提に探索的に調べる読み取り専用の別処理である。`analyze_rule_effect.py`の適格性条件は変更しない。正式解析の`video_verified`はfalseのまま照合し、本人申告を映像確認済みとして扱わない。Unity Runtime、記録仕様、較正とMIDI処理は変更しない。

導入済みXR Hands／OpenXR／XRIや公式サンプルは追跡・入力・表示用であり、この研究固有の同一callback比較と本人申告運指の探索的集計を提供しない。既存の`analyze_rule_effect.py`の入力検証、Rawの変換再構成、白鍵面からの黒鍵領域の差し引き、最小距離とSVG棒グラフ、`evaluate_pilot.py`の関節角と非等間隔差分を再利用する。追加パッケージは不要。

```powershell
python tools/explore_single_finger_session.py <one-session-directory> --output <new-output-directory> --source BLE --generation <generation> --onset-first <first-onset-event-index> --onset-last <last-onset-event-index> --settings-revision <revision> --hand Right --finger Index
python -m unittest discover -s tools -p "test_explore_single_finger_session.py" -v
```

入力はwriter完了、queue overflowなし、同一session、Dynamic callback、堅牢な座標変換、MIDI受信／観測順、方法・設定revision・座標系、関節と接触ログのRaw再構成を検証する。出力は入力と重ならない新規ディレクトリに限定し、8入力ファイルのSHA-256を読み取り前後に照合する。各Note Onに対し同一ソース・世代・チャンネル・ノートの明示的Note Offを要求し、次の選択されたNote Onより前のOffが欠落または曖昧なら拒否する。任意の自動運指推定、欠落Offの補完、条件の後付け検証は行わない。

主集計は各MIDI観測Note Onを含みNote Offを含まない区間の全有効ペア。未適用や無変位を成功例だけの抽出で除かず、非適用の理由、姿勢欠測、指先が1 µmを超えて移動した割合を別に記録する。1指1鍵の単音連続のみ対象とし、和音・重複オンやNote Off欠落の解釈をこのツールで推定しない。

X/Zは白鍵から黒鍵を差し引いた有効静止面へのXZ最近点の成分、Yは静止面への符号付き距離と絶対距離。符号付きYの正負をそれぞれ浮き／面下として別集計する。実鍵の動き、物理接触や外部3D基準は計測していない。Raw平均が0の改善率は未定義。比較の改善・悪化・不変の分類だけに1 µmの閾値を用い、差の平均そのものは丸めない。

`axis_summary.csv`はフレーム重み付きの比較と、打鍵ごとの等重み平均・中央値・標本標準偏差を併記する。`strike_summary.csv`は各打鍵のOn/Off、適用率・移動率、XYZ・変位・状態を保持する。`paired_frames.csv`の`raw_pos_*`／`corrected_pos_*`は座標、`raw_x_mm`等は鍵面からの距離成分であり、両者を区別する。`release_summary.csv`はNote Off以後の最大0.3秒を次のNote Onで打ち切り、解除状態と変位を保存する。その他に全指の非打鍵移動、制約理由、骨長・屈曲角・有限差分、9個のSVG、manifestを出力する。

骨長と屈曲角は関節座標から再計算する。非貫通・指間干渉・CPU時間は記録器のモデル内診断であり独立した物理真値ではない。CPU時間は描画・ログ・伝送・知覚遅延を含まない。時間微分は有効連続callback間で1–100 msの間隔のみを用い、断続をまたがない。静止区間を本人が指定していない限り、演奏の速度や加速度をジッタと呼ばない。

フレームは独立標本ではなく、各打鍵にも時間的・演奏上の依存がある。1人の探索的統計は、参加者集団への一般化、統計的有意性、真の手指位置の追跡精度を示さない。実験データ、生成グラフ、映像、個人情報、APKはコミットしない。
