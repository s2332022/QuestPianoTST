# 1鍵1指・1指1鍵の補正割当

## 調査と実装

既存の`noteToFinger[128]`は各ノートに指を1本だけ設定できるが、実行時の競合判定は指ごとの接触アニメーションと変更可能なマップに依存していた。BLE/UDP・MIDIチャンネルをまたいだ同一の鍵の占有判定がなく、解除中・タイムアウト・設定変更による補正停止と、MIDIの占有期間を分離する必要があった。UIも既存マップを直接上書きしていた。

既存のMIDI入力、`HandCorrectionRouter`、`RuleHandPoseSolver`、TextMesh Proと安全ゲートを再利用した。導入済みXR Hands 1.8.1、OpenXR 1.17.1、XRI 3.5.1および公式サンプルは維持した。物理鍵と打鍵指の排他割当は研究固有のため、既存プロセッサ内に必要な状態管理だけを追加した。外部パッケージは追加していない。

- 割当は受理したNote Onで確定する。鍵はMIDIノート番号で識別し、ソース・チャンネルをまたいでも最大1指とする。指も同時に最大1鍵を占有する。
- 占有にはソース・チャンネル・世代を保持する。対応するNote Off、velocity=0のNote On、CC120/123、ソース切断で解除する。拒否した別ソース／別チャンネルのNote Offは既存の占有を解除しない。
- 接触アニメーションの終了、最大保持時間による補正解除、追跡異常、モード／設定変更、緊急停止では、押下中の占有を消さない。Note Off欠落時に別指へ引き継がない。切断では当該ソースの占有と補正姿勢を消去する。
- 同一ストリームの重複Note Onは再割当せず、安全側に補正を停止し、全鍵のNote Offを待つ。別ストリームから既に占有中の鍵へのNote Onは既存割当を維持して拒否する。拒否済みの未解除イベントが残る鍵から新しい割当を推測しない。
- UDPのスナップショット修復は押下状態だけを復元し、指を推測しない。新世代のMIDIは旧世代の当該ソースの状態を除去し、古い世代のイベントは無視する。UDPの接続・順序判定は既存の受信経路を維持する。
- 異なる指による和音を許可する。Note Off直後に同じ鍵を別指で再打鍵する場合、前指の解除アニメーションを消してから新指の補正を開始する。新しい鍵を担当する指についても前回の補正オフセットを引き継がない。
- UIの割当操作は既存設定の別指への上書きと、押下中の設定変更を日本語で拒否する。変更する場合はNote Off後に設定を解除してから再設定する。複数の非同時鍵を同じ指へ事前設定することは可能だが、同時の占有は禁止する。
- 「ノート割当／上限」画面に現在の占有の「鍵→指」と直近の拒否理由を日本語表示する。表示する占有は、タイムアウト等で補正を停止していてもNote Off待ちの鍵を含む。

接触目標を与える指以外の重みは従来どおり0とし、FullRulesの非貫通・関節・指間干渉制約を維持する。非打鍵指へ鍵の接触目標を追加しない。RawとLegacyの経路、ソルバー、CSV列、UDP形式、Session02の元実験データは変更しない。

## 検証

Unity 6000.5.10f1を使用する。関連EditMode／PlayMode、全EditMode／PlayMode、既存`PianoDistributedSceneBuilder.BuildAndroidValidation`によるQuest向けAPKを順に検証する。ローカル証跡はGit対象外の`Logs/ExclusiveAssignmentValidation/`に保存する。

EditModeでは1鍵1指（ソース／チャンネルをまたぐ競合）、1指1鍵、5音和音、同音連打、解除アニメーションと占有の分離、欠落Note Off、重複Note On、velocity=0、CC120/123、切断／世代変更、スナップショット、緊急停止とUI割当APIを検証する。PlayModeではFullRulesで和音・競合・同音再打鍵・重複・欠落・切断復帰をフレーム更新し、UIボタン経由の拒否と日本語表示も検証する。既存のRaw／Legacy／ログ／環境制約テストも全体検証の対象に含む。

初回の全EditModeでは、実行中のソース変更がコンパイルを要求し、Unity-Skillsの`ReviewFixRouterTests.Batch_*`の5件が期待HTTP応答と異なる503等で失敗した。コードを固定して同クラスを再実行し、48件すべて成功した。初回のUI拡張テストでは、テスト側が手動マップを有効にしておらず、5指分の割当表示の期待に対して単一指の表示となった。テストの前提を修正し、実行時の各割当を明示的に検証するようにした。これらの初回失敗証跡も`initial-full-edit.*`と`initial-ui-play.*`に保存する。

| 最終検証 | 結果 | ローカル証跡 |
|---|---|---|
| 関連EditMode (`RuleHand`) | 40成功、失敗0 | `focused-edit.xml` / `.log` |
| 関連PlayMode (`RuleHand`) | 6成功、失敗0 | `focused-play.xml` / `.log` |
| パッケージ失敗箇所の再検証 | 48成功、失敗0 | `retry-package.xml` / `.log` |
| 全EditMode | 1155成功、失敗0、既存スキップ4（計1159） | `full-edit.xml` / `.log` |
| 全PlayMode | 74成功、失敗0、スキップ0 | `full-play.xml` / `.log` |
| Unityコンパイル | 成功、C#コンパイルエラーなし | 上記Unityログ |

全EditModeのスキップは、Addressablesの導入／設定を要求する既存2件と、Unix/Linux/macOS専用の既存2件。Unityの集計XMLのresultは`Skipped:Ignored`、Unityプロセスの終了コードは0、failedは0である。

## Quest APKビルド

| 項目 | 結果 |
|---|---|
| 成否 | 成功、Unity終了コード0 |
| 出力 | `Builds/Android/PianoDistributedQuest.apk` |
| サイズ | 57,852,164 bytes |
| 最終更新 | 2026-10-27 19:01:53 JST |
| SHA-256 | `b43628df87f82426089433023daa419757d58e9d537e61d6874a68acac3f12a2` |
| 収録シーン | `Assets/Research/Scenes/PianoDistributedQuest.unity`のみ |
| プラットフォーム | Android / IL2CPP / ARM64。APK内のnative ABIは`arm64-v8a`のみ、`libil2cpp.so`を確認 |
| 関連警告 | 既存SSAOの実機性能警告、Diagnostics Data用Debug Symbols推奨、既存APIのCS0618警告。今回の変更で設定や既存APIを変更していない |

証跡は`android-build.log`と`Builds/Android/PianoDistributedQuest.apk.provenance.json`。ビルド時HEADは`020148e49d66b3fc3e063ef207402049cf21e590`、`git_dirty=true`であり、検証後のコミットをビルド元に遡及して記載しない。元APKとsidecarは`.utmp/exclusive-assignment-build-backup/`に保持する。Unityが更新したURP設定・フォントキャッシュ・テスト生成metaは`.utmp/exclusive-assignment-generated/`に保存し、追跡対象の自動更新ファイルは作業開始時HEADの内容へ戻す。シーン・パッケージ・ProjectSettings・XR設定・既存CSV／実験データ・APK・ローカル証跡をコミット対象に含めない。

実機での手の表示・鍵盤位置合わせ・同一鍵への二重吸着の観察・追跡復帰・BLE/UDP接続復帰・体感遅延・UIの読みやすさは未検証。自動テストは実機確認の代用にはしない。
