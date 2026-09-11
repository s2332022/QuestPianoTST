# Meta Quest 2 ハンドトラッキング導入チェックリスト

対象プロジェクト: `QuestPianoMotion`  
調査日: 2026-09-08 (Asia/Tokyo)

## 調査・変更記録

- [x] Unity Editor: `6000.5.10f1` (`ProjectSettings/ProjectVersion.txt`)
- [x] `6000.5.10f1` は Unity 6.5 Update release であり、LTSリリースそのものではないことを確認した。既存プロジェクトを無断で別Editorへ移行しないため、今回はバージョンを変更していない
- [x] Render Pipeline: Universal Render Pipeline `17.5.0`
- [x] Input System: `1.20.0`、Player の Active Input Handling は Input System (`activeInputHandler: 1`)
- [x] Android の既存値: IL2CPP (`scriptingBackend Android: 1`)、ARM64 (`AndroidTargetArchitectures: 2`)、Minimum API Level 26
- [x] 旧 Oculus XR Plugin (`com.unity.xr.oculus`) に依存していない
- [x] 次の直接依存を `Packages/manifest.json` に追加した

| Package | 固定バージョン | 用途 |
|---|---:|---|
| `com.unity.xr.management` | `4.6.1` | XR provider の初期化・Player設定 |
| `com.unity.xr.openxr` | `1.17.1` | OpenXR provider と Meta Quest Support |
| `com.unity.xr.interaction.toolkit` | `3.5.1` | Hands Interaction Demo とXR操作 |
| `com.unity.xr.hands` | `1.8.1` | 手首・全指関節・追跡状態を扱う標準API |
| `com.unity.xr.meta-openxr` | `2.5.1` | Meta固有OpenXR拡張（旧 Oculus XR Plugin ではない） |

バージョンは「最新版」を推測せず、インストール済み Unity `6000.5.10f1` の Package Manager メタデータが、この Editor に対して提示するバージョンを固定した。`Packages/packages-lock.json` は手編集していない。Unity Editorを開いて Package Manager が正常に解決した時点のファイルを、再現性のため `manifest.json` と一緒にバージョン管理する。

## Unity Editorで行う作業

### 0. Editor系列を固定

- [ ] 研究要件が「Unity 6系列のproduction-ready版」なら、現状の `6000.5.10f1` を固定して以降のチェックを進める。
- [ ] 要件が厳密に「LTSリリース」である場合は、Unity 6.3 LTSなど採用するLTSを責任者と決める。既存プロジェクトを古いEditorで直接開くダウングレードは避け、バックアップ／別ブランチで移行検証し、Package互換性と全実機試験をやり直す。

### 1. Package解決を確認

- [ ] Unity Hub から、必ず Unity `6000.5.10f1` でこのフォルダーを開く。
- [ ] Package Manager の解決が完了するまで待ち、Console に package error や compile error がないことを確認する。
- [ ] `Window > Package Manager > In Project` で上表の5パッケージとバージョンを確認する。
- [ ] `com.unity.xr.oculus`（Oculus XR Plugin）が追加されていないことを確認する。
- [ ] Unityが更新した `Packages/packages-lock.json` を `Packages/manifest.json` と一緒にコミットする。

### 2. Android / Quest 2向けPlayer設定

- [ ] `File > Build Profiles` で Android に Switch Platform する。Android Build Support、SDK/NDK、OpenJDK が不足する場合は Unity Hub から、このEditor用モジュールを追加する。
- [ ] `Project Settings > Player > Android` で Product Name と Package Name を研究用の固有値に変更する。現在の Package Name はテンプレート既定値 `com.UnityTechnologies.com.unity.template.urpblank` のままなので、そのまま配布しない。
- [ ] Scripting Backend が `IL2CPP`、Target Architectures が `ARM64` であることを確認する（現在はいずれも設定済み）。
- [ ] Minimum API Level と Target API Level は、接続する Quest 2 のOSおよび配布先の当時の要件に合わせる。変更時は値と変更日を実験ログに残す。
- [ ] Graphics API と URP Asset が Android 向けになっていることを確認し、不要な高負荷機能を無効化する。最初は既存設定のまま実機動作を優先し、性能調整は測定後に行う。

### 3. XR Plug-in Management / OpenXR

- [ ] `Edit > Project Settings > XR Plug-in Management` の Android タブで `OpenXR` のみを有効にする。Oculus provider は有効にしない。
- [ ] `Initialize XR on Startup` が有効であることを確認する。
- [ ] `XR Plug-in Management > OpenXR` の Android タブで `Meta Quest Support` を有効にする。
- [ ] 同じ画面で `Hand Tracking Subsystem` と `Meta Hand Tracking Aim` を有効にする。
- [ ] コントローラーも併用する場合は Interaction Profiles に `Oculus Touch Controller Profile` を追加する。ハンドのみの最小確認でも、Project Validation が要求する項目を確認する。
- [ ] `Project Settings > XR Plug-in Management > Project Validation` を開き、Androidタブのエラーをすべて解消する。自動 Fix を押した場合は、変更されたファイルを記録・レビューする。
- [ ] OpenXR の Render Mode は URP/Quest 向けの既定推奨値を使い、変更した場合はモード名を実験ログに残す。

### 4. 公式Samplesを順番どおりImport

SamplesはUnity Package Managerから安全にImportし、CLIでコピーしない。

- [ ] `Window > Package Manager > XR Interaction Toolkit > Samples` から `Starter Assets` を先に Import する。
- [ ] `Window > Package Manager > XR Hands > Samples` から `HandVisualizer` を Import する。
- [ ] `Window > Package Manager > XR Interaction Toolkit > Samples` から `Hands Interaction Demo` を Import する。
- [ ] `Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/HandsDemoScene` を開く。
- [ ] Project Validation を再実行し、Starter Assets、XR Hands、HandVisualizer の不足警告がないことを確認する。
- [ ] Hands Demo Scene を Build Profile の Scene List に追加する。既存 `SampleScene` を無断で削除せず、必要なら無効化だけ行う。

### 5. Quest 2実機確認

- [ ] Quest 2で Developer Mode とUSB debuggingを有効にし、接続時のRSA許可を承認する。
- [ ] Quest本体の設定で Hand Tracking を有効にする。
- [ ] `Build And Run` で Quest 2へ配置し、左右の手のメッシュ／関節表示が追従することを確認する。
- [ ] 左右それぞれで、手が画角外に出たとき表示が消え、再検出時に復帰することを確認する。
- [ ] pinch、poke、ray pinch と Demo 内UI／オブジェクト操作を確認する。
- [ ] Consoleおよび `adb logcat` に OpenXR feature、permission、shader、package 関連エラーがないことを確認する。

## 手首・全指関節データ取得の次段階

- [ ] `XRHandSubsystem` を取得し、`leftHand` / `rightHand` の `isTracked` をフレームごとに記録する。
- [ ] `XRHandJointID.Wrist` を含む各 `XRHandJointID` について `XRHand.GetJoint(...)` と `TryGetPose` を使用し、取得成功/失敗も関節単位の追跡状態として保存する。手全体の `XRHand.rootPose` は手首Jointと同一だと仮定せず、別フィールドとして扱う。
- [ ] Poseの座標系（XR Originのローカル座標かワールド座標か）、単位（m）、左右、joint ID、Unity frame count をデータ仕様に明記する。
- [ ] `updatedHands` 等のXR Hands更新コールバックで取得し、`Update` の描画時刻と混同しない。更新種別（Dynamic / BeforeRender）も記録する。
- [ ] MIDI/IMU同期に進む前に、単調増加時計を基準に `host timestamp`、`Unity realtime`、`XR sample/update phase` を同時記録する設計を決める。端末・PC間同期方法とドリフト補正も実験プロトコルに残す。

## 再現性のため各実験で保存する項目

- [ ] Unity Editorの完全なバージョンとrevision
- [ ] `Packages/manifest.json` と解決済み `Packages/packages-lock.json`
- [ ] OpenXR feature / interaction profile の設定ファイル差分
- [ ] Android Player Settings、Quest OSバージョン、端末識別子（個人情報を含めない研究用ID）
- [ ] 使用Scene、Git commit ID、ビルド日時、APKのハッシュ
- [ ] サンプリング方式、欠損判定、座標変換、時刻同期方式
