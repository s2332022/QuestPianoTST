# XR Hands 研究用診断

`HandTrackingDiagnostics` は、XR HandsのDynamic更新から左右の追跡状態と次の関節Poseを一定間隔で取得します。

- Wrist
- Palm
- ThumbTip
- IndexTip
- MiddleTip
- RingTip
- LittleTip

PoseはXR Handsが返すtracking-space座標です。`TryGetPose` が失敗した関節は `valid=False` として記録され、Poseを有効値として扱いません。時刻 `t` はUTCではなく、`Time.realtimeSinceStartupAsDouble` によるPlayer起動後の単調増加秒です。

## シーンへの追加

1. `Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/HandsDemoScene` を開きます。
2. Hierarchyで空のGameObjectを作り、`Research Hand Tracking Diagnostics` など識別しやすい名前にします。
3. Inspectorの `Add Component` から `Hand Tracking Diagnostics` を追加します。
4. `Log Left Hand` と `Log Right Hand` で、Consoleへ出す手を個別に選びます。
5. `Log Interval Seconds` を設定します。実機のConsole負荷を抑えるため、最初は `1.0` 秒以上を推奨します。
6. `Subsystem Retry Interval Seconds` は通常 `1.0` 秒のまま使用します。
7. 変更したシーンをサンプル直下へ上書きせず、研究用Sceneとして `Assets/Research/Scenes` などへ `Save As` して保存してください。

OpenXRのAndroid設定で `Meta Quest Support`、`Hand Tracking Subsystem`、`Meta Hand Tracking Aim` が有効であることも確認してください。

## Console出力

1回のサンプルを1つのConsoleメッセージにまとめます。各関節には次が含まれます。

- `valid`: `TryGetPose` の成否
- `state`: `XRHandJointTrackingState`
- `p`: tracking-space位置（m）。`valid=True` の場合のみ出力
- `q`: tracking-space回転Quaternion。`valid=True` の場合のみ出力

Console出力は診断用です。高頻度・長時間の研究記録には使用しないでください。

## CSVへの拡張

値型の `HandTrackingSample` に取得処理を集約し、出力先は `IHandTrackingSampleSink` で分離しています。CSV記録を追加するときはsinkを実装し、`HandTrackingDiagnostics.RegisterSink` で登録します。XR更新コールバック内でファイルI/Oを直接ブロックせず、固定長バッファや別スレッドへの受け渡しを検討してください。

## EditModeテスト

`Window > General > Test Runner > EditMode` から `QuestPianoMotion.Research.Tests` を実行します。テストはXRデバイスを必要とせず、ログ間隔制御と7関節のデータ対応を検査します。
