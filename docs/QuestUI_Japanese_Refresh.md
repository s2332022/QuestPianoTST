# Quest UI 日本語化・検証報告（2026-10-26）

## 作業保護とUnity環境

- 基準コミット：`f104f1ba048ed015634cc2222eaab64f4207ac0e`。対象ブランチ：`feature/quest-rule-hand-pose`。
- 作業前の5ファイルとバイナリ差分パッチ：`C:\Users\Yuki_\QuestUIBackups\20261026-190055`。旧APKも同じフォルダへ保存。
- 復元タグ：`ui-jp-prevalidation-20261026`。前回のタグ作成失敗は、worktreeの共有Git管理領域がプロジェクト外にあり、通常のサンドボックス権限では書き込めなかったため。今回は権限付き実行で作成できた。
- 元のPID 20212はUnity Editor、21956はそのAsset Import Worker。両方とも `C:\UnityProjects\QuestPianoMotion\QuestPianoMotion` に属する。終了・編集操作はしていない。
- 対象worktreeはAssets・Libraryが独立しており、開いているEditorがなかったため、Unity 6000.5.10f1のバッチモードで順番に検証した。
- インストール済みのUnity-Skills 2.8.3はBesty提供の外部パッケージ。元Editorのサービスを検出したが、そこへの書き込み操作はしていない。検証は既存Unity Test Framework・ビルダーを使用し、パッケージ追加はしていない。

## 実装内容

- メイン画面にMIDI接続、鍵盤位置合わせ、記録状態、補正ON/OFFと方式、左右の手、対象指を集約。
- 詳細画面のボタンとカテゴリタブ、確認操作、MIDI中の操作ロック表示を日本語化。
- 既存の詳細ページ構成を継承。MIDI接続タブ内にPC接続とA/B/C較正、BLE MIDIタブにBLE接続、補正設定タブにパラメータ・新方式のルール・手動割当、ほかに記録・診断、UI配置を配置。診断と補正詳細は通常・折りたたみ画面から隠す。
- 方式切替を「旧方式／新方式」の二択に整理。起動時のLegacy既定値は変更していない。
- 既存のRay + Pinch、移動ハンドル、演奏中ロック、記録停止・較正保存の確認処理を維持。
- 接続エラーと鍵盤位置合わせの未完了を画面に表示。
- 検証で見つかった確認ボタンの内部参照名の破損を修正。`CONFIRM` / `CANCEL`を維持して表示文字のみ日本語にした。
- 補正詳細と準備状況の重なりを修正し、パラメータ・移動ハンドルの残存英語表示を修正。
- Androidビルダーの固定絶対パスをプロジェクト基準に変更。元プロジェクトのAPKを上書きせず、対象worktreeへ出力する。

## 主な表示語

| 英語 | 日本語 |
|---|---|
| CORRECTION | 補正設定 |
| OLD / NEW | 旧方式 / 新方式 |
| HAND / FINGER | 使用する手 / 使用する指 |
| CALIBRATION | 鍵盤位置合わせ |
| START RAW REC / STOP RAW REC | 記録開始 / 記録停止 |
| DETAIL SETTINGS | 詳細設定 |
| ENGAGE / RELEASE | 補正開始 / 補正解除 |
| RECENTER UI | UIを正面に移動 |
| CONFIRM / CANCEL | 確認 / キャンセル |

## 画面とQuest操作

通常画面で実験の準備状況を確認し、記録開始・停止、補正ON/OFFを操作します。詳細画面はカテゴリごとに開き、鍵盤較正、ルール・割当、診断、配置を調整します。Rayを対象へ合わせてPinchします。演奏中は設定がロックされ、鍵盤とPinchを離すと解除されます。

実験の順序：MIDI接続確認 → 鍵盤位置合わせと保存 → 手と指の選択 → 旧方式/新方式の選択 → 補正ON → 記録開始 → 演奏 → 記録停止。

## フォント

プロジェクト内の `Assets/Research/Resources/QuestUi/NotoSansJP.ttf` を既存のTextMeshProフォールバック機構で使用します。付属ライセンスはSIL Open Font License 1.1、Font Importerの `includeFontData` は有効です。Unity Editorの実描画で日本語を確認し、実際の状態文に対して字形と文字切れのテストが成功しています。Quest Androidでの字形・読める大きさ・折返しは実機確認が必要です。

## 検証結果

- Unityスクリプトコンパイル：成功。前回の.NET単体MSBuildのWindows SDKアクセス拒否は、Unity側の検証を妨げなかった。
- 最終EditMode：286件成功、失敗・スキップ0（19:14 JST）。
- 最終PlayMode：72件成功、失敗・スキップ0（19:15 JST）。
- 初回PlayModeは66/72件成功。確認ボタンの内部参照破損と、英語表示・旧方式巡回順を前提にした期待値を修正して再検証した。
- 旧方式/新方式の往復、補正ON/OFF、左右の手、全5指、記録中の日本語表示、確認を経た記録停止、演奏中の操作ロックを検証。既存のMIDI、較正、Raw記録、全指補正、CSV整合、配置、XRI操作のテストも成功。
- ボタンの重なり・文字切れ検査は詳細ページ0～7を対象に成功。通常・折りたたみ・詳細ページ・日本語診断のPNGも生成し、主要画面を目視確認。
- ローカル検証成果物：`Logs/UI-EditMode-Final.xml`、`Logs/UI-PlayMode-Final.xml`、`Logs/UI-validation-summary.json`、`Logs/UI-Previews/`。ログ・画像はコミット対象外。
- CSV、プロトコル、補正アルゴリズム、研究Sceneは変更していない。

## Android APK

- 結果：成功（Unityプロセス終了コード0）。
- 出力：`C:\Users\Yuki_\QuestRulePoseWork\Builds\Android\PianoDistributedQuest.apk`
- 更新日時：2026-10-26 19:22:38.674 JST（10:22:38.674 UTC）。ビルド開始は19:15:41 JST。
- サイズ：57,852,332バイト。
- SHA-256：`f7e30a62d7f8e13e2618c3d267f978e2d5d6ac1bc07d6e3d856823efae5dc995`
- Scene：`Assets/Research/Scenes/PianoDistributedQuest.unity` のみ。
- Android / IL2CPP / ARM64。APK内のネイティブABIは `arm64-v8a` のみ、`libil2cpp.so` のELF MachineはAArch64（183）。
- 日本語フォント同梱：APK内 `assets/bin/Data/d88bd6e3ed98505498470044369a6bcc` に、元Noto Sans JPフォント9,589,900バイトの完全一致を確認。検査結果は `Logs/UI-APK-Inspection.json`。
- 旧APKは2026-10-21の57,846,028バイトのものをバックアップしており、今回のAPKとはSHA-256も異なる。
- 既存警告：旧APIのCS0618、端末上のSSAO負荷、クラッシュ解析用Debug Symbolsの推奨、TMPの大きなメソッドのC++分割。ビルド失敗はない。描画・XR設定の自動シリアライズ差分はバックアップ後に元へ戻し、コミットに含めない。
- ユーザー指定の順序に従いビルド後にコミットするため、APK内のGitビルドメタデータは基準コミット＋dirty状態を示す。検証後に変更したのは報告書と自動生成設定の復元のみ。

## Quest 2実機での未確認事項

- 日本語フォントの実機描画、視認距離、ボタンの押しやすさ、Ray + Pinchと移動ハンドル。
- 実際のBLE MIDI/PC経由接続と再接続、物理鍵盤の較正・保存・復元。
- 旧方式/新方式の演奏時挙動、手の表示と位置、非貫通・和音手動割当、記録停止後の実機ファイル。
- 一部のBLE診断・補正理由コードなどは既存の英語技術表示を維持している。記録時間の新設や画面カテゴリの追加再編は今回の検証作業に含めていない。
- PC経由のMIDI表示は既存の `PcConnected`（PC通信状態）に基づく。PC側の物理MIDI機器の接続はPC側でも確認すること。
