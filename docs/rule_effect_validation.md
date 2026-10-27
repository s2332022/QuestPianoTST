# 表示整合性解析環境の検証記録

実施日：2026-10-27。作業ブランチ：`feature/quest-rule-hand-pose`。
開始時HEAD：`23efac7288c55e115a0577a43165d50251972db5`。開始時worktreeはクリーン。
変更前checkpoint：`checkpoint/before-xyz-analysis-20261027`（開始時HEADを保持）。

## 結果

```text
python -m unittest discover -s tools -p "test_analyze*.py"
Ran 40 tests
OK
```

Python 3.14.0 / Windowsで実行。既存Raw解析9件＋追加解析31件、失敗0。
全テストは明示した合成データであり、実測の補正効果やQuest実機の動作確認ではない。

確認内容：

- 同一callback・関節ID・snapshot・時刻・設定revision・方式・較正IDの対応と不一致拒否。
- Tracking→World→Keyboard独立再構成、移動した変換、m/mm、非剛体変換の拒否。
- 白鍵の黒鍵切り欠き、黒鍵高さ、矩形和集合、面内XZゼロ、最近点同距離時の決定性。
- Y正負、浮き・貫通・上下許容帯、改善量・改善率、Rawゼロ時の空欄。
- 改善・悪化・不変率、試行単位の対応集計、階層CIの再現性、1試行CI未定義、フレーム水増しで試行数が増えないこと。
- 無効追跡を分母に保持。snapshot変更や欠測をまたぐ微分を作らないこと。
- Note On/Off、速度ゼロOff、generation遷移、channel別All Notes Off、受信と観測時刻の区別、欠落/未来MIDI参照拒否。
- 手動Mapと映像で確認した実運指を区別。未割当、競合、gainゼロ、solver無効を成功に数えないこと。
- 同時2音・2指の和音ペア評価。実行CLIのinventoryが指を自動割当しないこと。
- writer未終了、overflow、writer error、混在session、重複行、CSV行の欠損、ペア欠落を拒否。
- 入力のバイト単位保持、解析出力の上書き拒否、入力フォルダとの重複拒否。
- 集計CSV、静止区間ジッタ、SVGのXML妥当性、空テンプレートに実測なしと表示すること。

## 実測の適格性

プロジェクト内、既知のDownloads/Documents候補、および元Unityプロジェクトの検索では、今回のRaw/FullRules主比較に使えるセッションを発見していない。既存`docs/RuleHandPoseValidation.md`に記載されたSession02は旧方式のため今回の主比較に採用しない。未発見のデータが存在しないという意味ではない。回収後は`--inspect-session`で適格性を検証し、映像確認済みの条件JSONで解析する。

`docs/rule_effect_conditions.empty.json`から空の集計CSVとSVGを生成した。出力manifestは`measured=false, frames=0, valid_frames=0`。生成物はGit対象外の`.utmp/rule_effect_deliverables_20261027/`に保存し、元CSV・実測データ・APK・動画・個人情報をcommitしない。図のレイアウトは既存Edgeのheadless描画によるPNGで確認した。初回のsandbox内描画はGPU起動エラーとなり、許可された外側のheadless描画で確認できた。SVGの構文も自動検証した。

## 実機で残る確認

Unity Runtime、既存CSV、補正ロジック、通信、較正、シーン、Packages/ProjectSettingsは変更していないため、今回Unity EditMode/PlayModeおよびAPK再ビルドは実行していない。

Quest 2での表示位置、追跡復帰、正しい指のMap、和音の制約残留、補正開始/解除の自然さ、BLE/UDP実接続、較正の実鍵一致、総フレーム負荷と知覚遅延は未検証。手順書に沿った65試行/片手/人の収録が必要。CPUログは補正器内の処理時間であり端末全体の負荷ではない。95% CIを含む実測の改善量・有意差・自動押鍵指推定の必要性はまだ結論しない。
