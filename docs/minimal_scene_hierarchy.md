# PianoMinimalTest Scene Hierarchy

## 保存済みHierarchy

```text
PianoMinimalTest (Scene)
├── XR Origin
│   └── Camera Offset
│       └── Main Camera
├── Research Runtime
├── Piano Root
├── Research UI
└── EventSystem
```

Scene上のrootは上記5個だけである。

## Play開始後

```text
PianoMinimalTest
├── XR Origin [XROrigin]
│   └── Camera Offset
│       └── Main Camera [Camera, AudioListener, TrackedPoseDriver]
├── Research Runtime [PianoResearchRuntime]
│   ├── (components) XRHandPoseProvider / AndroidMidiInput
│   ├── (components) VirtualPianoKeyboard / PianoCalibrationManager
│   ├── (components) SynchronizedSessionRecorder
│   └── (components) MinimalHandVisualizer / MinimalResearchStatusPanel
├── Piano Root
│   └── Virtual Piano Keyboard C4-C5 (runtime)
│       ├── Key 60 White
│       ├── ...
│       └── Key 72 White
├── Research UI
│   └── Minimal Research Canvas (runtime)
│       └── Panel
│           ├── Status (7 lines)
│           └── 11 Buttons
└── EventSystem [EventSystem, MinimalHandUiInputModule]
```

## Rootごとの責務

| Root | 責務 | 意図的に持たないもの |
|---|---|---|
| XR Origin | tracking spaceとHMD camera | Locomotion、Teleport、controller、Interactor |
| Research Runtime | 研究serviceを同一GameObjectへ一意に生成・配線 | serviceごとの重複GameObject |
| Piano Root | runtime生成13鍵の親、calibration poseの適用先 | Collider、Rigidbody、AudioSource、88鍵 |
| Research UI | world-spaceの最小status/button Canvasの親 | 説明、履歴、joint統計、追加Canvas |
| EventSystem | 左右Index Tip + pinch UI入力 | mouse module、controller/ray/poke Interactor |

## Main Camera

`TrackedPoseDriver`の埋め込みInput Actionは次のbindingだけを持つ。

- Position: `<XRHMD>/centerEyePosition`
- Rotation: `<XRHMD>/centerEyeRotation`
- Tracking State: `<XRHMD>/trackingState`

Tracking Origin ModeはDevice、Camera Y Offsetは0である。SceneにCameraとAudioListenerは各1個だけである。

## 手表示

`MinimalHandVisualizer`は`XRHandPoseProvider.DisplayFrameUpdated`だけを購読する。補正前のRaw frameを直接描画せず、現在のIdentity処理後のDisplay frameを描画する。左右の有効jointは軽量なoctahedron meshで色分けし、追跡喪失時は該当側の描画数を0にする。Sample hand prefab、SkinnedMesh、sample HandProcessor、gesture detectorには依存しない。

## UI入力

`MinimalHandUiInputModule`は左右それぞれのIndex TipをCamera screen座標へ投影し、Thumb Tipとの距離が22 mm未満でpress、32 mm以上でreleaseとする。配列と`PointerEventData`は再利用し、XRI Interaction ManagerやRay/Poke Interactorを必要としない。

## 参照と独立性

新Sceneからの自作参照は`PianoResearchRuntime`と`MinimalHandUiInputModule`だけで、残る静的componentはUnity/XR packageの`XROrigin`、`TrackedPoseDriver`、`EventSystem`である。研究service、鍵、Canvas、手表示はruntimeが生成するため、`HandTrackingResearch.unity`やそのHands Demo hierarchyをロードしなくても動作する。

## Scene確認チェック

- root名とroot数が上記どおり
- `PianoResearchRuntime`、Main Camera、EventSystemが各1個
- Missing Script / Missing Referenceが0
- Play後の鍵が13個、MIDI 60～72
- 鍵にCollider、Rigidbody、AudioSourceがない
- Light、床、机、sample objectがない
- statusは7行、buttonは11個

