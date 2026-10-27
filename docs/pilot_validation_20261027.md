# 評価システムv2の検証結果（2026-10-27）

開始時のブランチ：`feature/quest-rule-hand-pose`、HEAD：`9cf68d0476336d259534d42a1b2b9fc51fe32b8e`、worktreeはクリーン。変更前checkpoint：`checkpoint/before-pilot-evaluation-20261027`。

## 変更範囲

- `tools/evaluate_pilot.py`：A制約、Bコスト、C連続性、D非打鍵影響、5区間、試行集計、較正・同期、5試行品質gate、閾値lock、証跡照合。
- `tools/analyze_rule_effect.py`：共通ペア検証へ後方互換のexpected_method引数を追加。既定は従来どおりFullRules。
- `PianoDistributedSceneBuilder.cs`：既存のビルド時metadataを保持し、成功APKのSHA-256等をsidecarへ保存。補正器やRuntimeログは変更しない。
- 解析テスト、Editor証跡テスト、実験手順・条件・較正・同期・閾値テンプレートを追加。

既存Raw/MIDI仕様、Legacy、FullRules solver、較正、BLE/UDP、88鍵、Passthrough、日本語UIは変更していない。新しい外部パッケージも追加していない。Session02等の元実測データは解析結果として捏造せず、書き換えていない。

## 自動テスト

| 検証 | 結果 | ローカル証跡（Git対象外） |
|---|---|---|
| Python全解析 | **65成功、失敗0** | `Logs/Pilot-Python-Final.log` |
| 追加Editor証跡の焦点テスト | 1成功 | `Logs/Pilot-EditMode-Focused.xml` |
| 補正の焦点PlayMode | 2成功 | `Logs/Pilot-PlayMode-Focused.xml` |
| 全EditMode | **1143成功、失敗0、既存スキップ4**（計1147） | `Logs/Pilot-EditMode-Full.xml` |
| 全PlayMode | **73成功、失敗0、スキップ0** | `Logs/Pilot-PlayMode-Full.xml` |
| Unityコンパイル | 成功、C#コンパイルエラーなし | 上記Unityログ |

EditModeの4スキップは既存Unity-SkillsのAddressables未導入/未設定2件とUnix/Linux/macOS専用2件。今回の解析・補正・証跡テストのスキップではない。

Pythonは既存40件と追加25件。追加分で、全5指・5区間、未来鍵による接近区間評価の禁止、停止/解除時の実表示保持、無効追跡の欠測、非等間隔の中点時刻差分、snapshot/間隔/欠測境界、骨長・干渉、微小改善時の比未定義、Raw OFF・Legacy識別、BLE固定、実On/Off欠落、開始/解除打切り・解除後増加、試行p95/最大、1人の試行を複数被験者にしないこと、未使用較正点、同一座標の水増し拒否、独立同期/表示proxyの区別、APK取り違え、事前lockのハッシュ/時刻・上書き拒否、callbackまるごと欠落の検出を確認した。

合成5試行を用いて品質gateの真偽をテストしたが、`data_kind=synthetic_test`で明示し、実測・本実験readyにはしない。テストログは一時領域で作成し、実測結果の代用にしない。

## APKビルド

| 項目 | 結果 |
|---|---|
| 成否 | **成功** |
| 出力 | `Builds/Android/PianoDistributedQuest.apk` |
| サイズ | **57,852,332 bytes** |
| 最終更新 | **2026-10-27 16:24:21 JST** |
| SHA-256 | `3ea11eb9216c7640a2bdb5cc0a3bc58f31d5ec6e5b297b0d04e8e976332c35a4` |
| シーン | `Assets/Research/Scenes/PianoDistributedQuest.unity`のみ |
| プラットフォーム | Android / IL2CPP / ARM64。APK内native ABIはarm64-v8aのみ |
| 関連警告 | 既存SSAOの実機負荷警告、Diagnostics Dataに対するDebug Symbols推奨。設定は変更していない |

証跡：`Builds/Android/PianoDistributedQuest.apk.provenance.json`、`Logs/Pilot-AndroidBuild.log`。
ビルド時の元commitは`9cf68d0476336d259534d42a1b2b9fc51fe32b8e`、**dirty=true**、Unity 6000.5.10f1、ビルド時刻2026-10-27T07:22:43.4365933Z。ビルド時点の未コミット変更を隠さずsidecarに記録した。完了後commitをビルド元に遡及して記載しない。Unityライセンスのaccess-token更新警告は出たが、ライセンス接続後にビルドは終了コード0で成功した。

既存APKは別領域`.utmp/pilot-build-backup/PianoDistributedQuest-before.apk`へ退避コピーし保持。元APKのSHA-256は`f7e30a62d7f8e13e2618c3d267f978e2d5d6ac1bc07d6e3d856823efae5dc995`。

Unityテスト/ビルドで自動変更されたフォントキャッシュ等は作業前HEADと照合して保護対象を戻し、生成された内容も`.utmp/pilot-generated-settings-preserved/`へ保管した。ProjectSettings、XR設定、RP設定、フォントの対象8ファイルはHEADとのバイト一致を確認した。APK、sidecar、テストXML/log、生成設定、元CSV・動画はcommitしない。

## 空テンプレートと実機で未検証の項目

初期化・空解析を実行し、`.utmp/pilot_study_preregistered_20261027/empty_results_final/`へCSVとSVGを生成した。`measured=false, observed_trials=0, ready_for_main_experiment=false`。SVGはXML検証に加え、残差とコストの別軸図を既存Edgeのheadless描画で確認した。

実機5試行、物理較正の4点検証、独立時計アンカーまたは描画proxyの遅延上限検証、固定映像の運指/接触注釈、実際にインストールしたAPKとの照合、表示の自然さ、解除と追跡復帰、BLE安定性、総Quest負荷・FPS・知覚遅延は未検証。自動gateを実機確認の代用にしない。手順と事前閾値は[pilot_evaluation_v2.md](pilot_evaluation_v2.md)を使用する。
