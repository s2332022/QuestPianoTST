# アーカイブ候補

## 今回の扱い

今回は既存ファイルを移動・削除していない。特に`HandTrackingResearch.unity`と、その参照先である旧診断scriptは復旧・比較に必要なため維持する。`Assets/_Archive`もまだ作成していない。

下表はAssets内の自作・template由来アセットを対象に、Scene YAML、GUID、Build Settings、Build Profile、script/test参照を調べた結果である。文字列による動的loadや将来用途まで断定できないものは判断保留とした。

| パス | 種類 | 参照元 | 不要と判断した理由 | 移動可能 | 削除可能 | 判断保留 |
|---|---|---|---|---|---|---|
| `Assets/Scenes/SampleScene.unity` | Unity URP template Scene | `ProjectSettings/EditorBuildSettings.asset`（現在disabled）、`ProjectSettings/ProjectSettings.asset`の`templateDefaultScene`、`Assets/Settings/Build Profiles/Meta Quest.asset`内snapshot | 最小研究Sceneでは使わない標準Camera/Light/Volume Scene | 参照を手動で外し、比較不要を確認後に`Assets/_Archive/OldScenes`へ移動可能 | 現時点では不可 | はい |
| `Assets/Readme.asset` | Unity template説明asset | Readme Inspector / TutorialInfo editor導線 | runtime研究機能に不要 | `Assets/TutorialInfo`と一式で、Editor起動確認後に`Assets/_Archive/SampleOnlyScripts`へ移動可能 | backupと一定期間のarchive後のみ | はい |
| `Assets/TutorialInfo/` | Unity template用Editor script・icon・layout | `Assets/Readme.asset`、内部相互参照 | runtime研究機能に不要 | `Assets/Readme.asset`と一式なら条件付きで移動可能 | backupと一定期間のarchive後のみ | はい |
| `Assets/Settings/SampleSceneProfile.asset` | Volume Profile | `SampleScene.unity`に加え`PC_RPAsset`と`Mobile_RPAsset`から参照 | 名前上はsampleだがrender pipelineから参照中 | 現時点では不可 | 不可 | はい |

## 維持確定（アーカイブ候補外）

- `Assets/Research/Scenes/HandTrackingResearch.unity`: ユーザー指定の復旧・比較Scene。
- `Assets/Research/Scripts/HandTrackingDiagnostics.cs`: `HandTrackingResearch.unity`から直接参照。
- `Assets/Research/Scripts/XRHandSampleReader.cs`
- `Assets/Research/Scripts/HandTrackingSample.cs`
- `Assets/Research/Scripts/IHandTrackingSampleSink.cs`
- `Assets/Research/Scripts/SampleIntervalGate.cs`: 旧診断経路または既存EditMode testから参照。
- `Assets/Plugins/Android/QuestMidiBridge.java`と`.meta`: Android MIDI必須。Android限定PluginImporter設定を含む。
- `Assets/InputSystem_Actions.inputactions`: Editor Build Settings configから参照。
- `Assets/XR`、`Assets/XRI`、`Assets/Settings`: platform、OpenXR、render pipeline設定として使用。

## 今回は判断対象外

`Assets/Samples/**`と`Assets/TextMesh Pro/Examples & Extras/**`はPackage/sample由来のため移動・変更しない。`Assets/CompositionLayers/**`、`Assets/MeshingFeaturePlugin/**`もplatform機能または出自・参照範囲を断定できないため、一括整理しない。

## 完全削除してよいと判断できる条件

次をすべて満たした場合だけ、archiveから完全削除へ進める。

1. UnityのFind References、GUID検索、Scene dependency、Build Profile、ProjectSettings、docs、testsの参照がすべて0。
2. `Resources.Load`、Addressables、reflection、文字列パスなどの動的参照がない。
3. archiveへ移動した状態でclean import後のコンパイルエラー、Missing Script、Missing Referenceが0。
4. EditMode / PlayMode testが全件pass。
5. PianoMinimalTestだけのAndroid buildとQuest実機受入試験がpass。
6. HandTrackingResearchを開いた比較・復旧試験が不要、または代替手段があると研究責任者が確認。
7. version controlまたは外部backupから復旧できる。
8. archiveで一定期間運用した後、責任者が削除を承認。

