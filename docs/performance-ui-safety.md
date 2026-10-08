# 演奏とUI操作の分離（Quest Distributed）

## 原因と採用した最小変更

対象は `DistributedQuestUi` が生成する Research UI（world-space Canvas）、その子の `BLE diagnostic panel`、別Canvasの `IPv4 Keyboard Canvas`。旧配置はHMDから前方1.1m・下0.1m、主パネル720×970 units×0.0009（高さ87.3cm）。下段ボタンが演奏領域へ張り出し、CONNECT/DISCONNECTなどは1クリックで実行されていた。MIDI入力中の操作保護もなかった。

`PianoDistributedQuest` と既存Builderは、公式Starter Assetsの `Right Hand UI Near-Far Interactor` を使用。near castingは既にfalse、far casting / UI interactionはtrue。`XRUIInputModule` → `TrackedDeviceGraphicRaycaster` → 標準UGUI Buttonのray＋右手pinchが実際の経路。専用のpoke interactorはない。`MinimalHandUiInputModule` は明示切替の診断用で、標準モードでは無効。

したがってpoke有効が原因とは確認できない。自然な演奏動作のpinchに似た入力とrayが重なれば、ray操作でも誤入力が起こり得る。実機での正確な発生動作は要確認。配置変更だけではこの入力経路は保護されないため、配置＋MIDIロック＋重要操作の確認を追加した。標準Interactor、入力マッピング、通信、ログ形式、手データ、鍵盤アニメーションは変更しない。

## 配置と座標

- KeyboardGeometry内の表示白鍵の左右端中心（静止時位置、鍵面高さ）をWorldへ変換してアンカーとする。
- KeyboardRootの+X＝右、+Y＝上／鍵面法線、+Z＝鍵盤奥。
- アンカーからWorldメートル換算で右0.15m、上0.40m、奥0.30m。鍵盤の回転を適用する。校正のgeometry scaleを距離に二重適用しない。
- KeyboardRoot回転 × local X軸30度で上部を奥へ傾け、表示面をユーザー／上方へ向ける。
- 主Canvasは720×970 units、uniform scale=0.00065。高さ約63cm、ボタン高さ約31mm。主Canvas中心は上40cmだが下端は鍵面から約12.7cm、奥約14.2cm。BLE子パネルも同じ配置・スケール。
- メイン、BLE、IPの合計横幅は主パネルだけより広い。実機で可読性と画面端を確認すること。
- 校正／表示鍵数変更でアンカーが変わればWorld配置を更新。通常の頭の動きには追従しない。RECENTER UIは同じ配置規則を再適用。
- 鍵盤未生成時のみ、HMD水平前方0.75m・下0.35mの仮アンカーを使い、鍵盤が現れれば再配置。共有PlaceUiは維持。
- CANCEL CAPTUREとKEYBOARD切替ボタンの旧重なりも解消（Cancelを下段右へ）。

## 操作ロック

`PerformanceUiSafety` は既存のBLE/UDP `MessageReceived` に購読するだけで、イベントを消費・書換えしない。

1. NoteOn / NoteOff / ControlChangeの受信コールバックで直ちにCanvasGroupをinteractable=false、blocksRaycasts=false、alpha=0.45にする。Otherでは延長しない。
2. 押鍵継続中はロックする。既存のBLE/UDP合成された128ノートの物理押鍵状態を読み取る。サステインのみでは物理押鍵とみなさない。
3. 最後の受信から1.0秒、かつ全鍵の離鍵まで待つ。
4. ロック中からpinchを保持していた場合は右手UI pressの解放まで待ち、新しいpinchから操作可能にする。
5. 時計はQuest内 `Time.realtimeSinceStartupAsDouble`（秒、単調増加）。PC timestampやBLE packet timestampをロック計算に使わない。研究ログの時計とデータは変更しない。
6. 主CanvasGroupをBLE子UIとIP子Canvasも継承。ボタンdelegateと公開IP編集メソッドにも同じガードを掛ける。Navigation focusは解除する。
7. 入力ロックはUIだけに適用。XR Hands描画、通信、記録、鍵盤更新は継続。

NoteOffが欠落して押鍵状態が残る場合は、既存のUDP snapshot修復／切断時release、BLE切断時releaseに従う。意図的な押鍵が続く間はUIから切断することもできないため、まず離鍵する。実機で接続喪失時の解除も必ず確認する。

## 重要操作

PC CONNECT（内部で停止→再接続）、PC DISCONNECT、BLE CONNECT/DISCONNECT、CALIBRATION A／再試行、SAVE CALIBRATION、STOP RAW RECは別のCONFIRM/CANCELボタンを表示する。再クリックだけでは確定しない。10秒で失効し、MIDI再開、画面無効化、Cancelでも取り消す。確認中は他の操作delegateを抑止し、主／BLE／IP領域を覆う標準UGUI modal Canvasでrayを遮る。接続対象やIPを確認中に変更できない。

確認待ちを残したまま演奏へ戻っても、後から遅延実行しない。確認前にIPキーパッドは閉じる。校正B/C、通常の描画切替、記録開始などは1操作のまま。

## 自動検証

- EditMode `PerformanceUiPolicyTests`: ロック期限／延長、押鍵保持、pinch解放待ち、各MIDI種別、yaw別World配置、傾斜、パネル下端。
- PlayMode `PerformanceUiRuntimeTests`: UDPキュー経由とBLE受信delegate経由のロック、イベント時刻保持、鍵盤駆動継続、押鍵保持と解除、確認1回のみ実行、キャンセル／期限、BLEボタン確認、子Canvas/IP保護、校正に追従し頭には追従しない配置。
- 既存 `XriQuestSceneStructureTests` がシーンのnear=false / far=trueを検証。
- 既存の全EditMode / PlayModeで通信、BLE、校正、88鍵、separator、passthrough構造、記録を回帰確認。
- APKは既存 `PianoDistributedSceneBuilder.BuildAndroidValidation()` のみ使用。Android / IL2CPP / ARM64。

結果とログの保存先は作業報告を参照。自動テストは実機の視認性・誤入力率・物理位置合わせを証明しない。

## Quest 2実機での確認手順

1. 実行中の研究記録を停止・退避してから新APKを導入。既存の校正／設定は保持する。Unity6000.5.10f1 / Android / IL2CPP / ARM64。
2. Passthroughと左右XR Handsの描画を確認。UIが鍵盤斜め上にあり、鍵盤、自然な手の移動、視線を遮らないことを座位／立位で確認。HMDを動かしてもUIが頭に付いて来ないことを確認。
3. 手をUIへ近づけるだけでは入力されないこと、遠方rayを意図的に狙って右手pinch→releaseで通常ボタンが1回だけ動くことを確認。必要なら診断画面／シーンでnear=false / far=trueを確認。
4. BLEのPERMISSIONS→SCAN→NEXT DEVICE→CONNECTを行う。CONNECTだけでは接続されず、CONFIRMで接続、CANCEL／10秒放置で接続されないことを確認。同様にDISCONNECTも検証。
5. BLEで単音、和音、速い演奏、長い押鍵、ペダルを試す。鍵盤は動き、UIは半透明で全ボタン、BLEボタン、IPキーが無反応であることを確認。長押鍵は1秒以上でもロック。離鍵後1秒で復帰。ペダルだけは押鍵扱いでないがCC受信で1秒ロック。
6. 演奏中からpinchを保持し離鍵する。1秒経ってもUIは解除されず、pinchを開いてから新たなray操作が必要であることを確認。
7. PC UDPでも同じ単音／和音／ロック／復帰を確認。BLEとUDPで同一ノートを押し、片方だけ離鍵しても残る側が鍵盤とロックを保持することを確認。UDP snapshot修復、BLE切断／再接続時の全離鍵も確認。
8. 確認ダイアログを開いたまま鍵盤を弾く。ダイアログが消え、離鍵後に旧操作が実行されないことを確認。IP入力を開いたまま演奏しても値が変わらないことを確認。
9. CALIBRATION A→CONFIRM→B→C、SAVE→CONFIRMを行い、正常な位置合わせ／passthrough復帰、UIの再配置を確認。失敗時の既存校正保持とretry/cancelも確認。88鍵と白鍵separator、13/88切替も確認。
10. START RAW REC→BLE/UDP演奏→離鍵→STOP RAW REC→CONFIRM。session_metadata.json、Raw joint CSV、MIDI CSV、transform snapshots、Quest timestampを回収し、演奏中もフレームとイベントが継続していることを確認。前回のRaw解析手順も実行する。
11. 以前誤入力した動作を繰り返し、意図しないボタン発火数、復帰待ち時間、文字可読性、首の負担を記録。必要なら配置距離／スケールを次回調整する。

## 2026-10-08 ??????

- ?EditMode: 1,073??1,069?????0???????4?Addressables??2?Unix??2??
- ?PlayMode???: 57?????????????????pinch????????2????????????????????
- ??EditMode??????: 13??????UI?????ray????pinch?: 15????
- ??Raw??Python???: 9????
- ??Android Validation Builder: ?????Scene?PianoDistributedQuest???Android / IL2CPP / ARM64?
- ?????????199???????SHA-256??????DistributedBuildMetadata.json?????????
- ??XML????APK?????Git????? `C:\Users\Yuki_\QuestUiValidation` ????APK?Builds/Android/PianoDistributedQuest.apk?APK??????commit????
- Quest????????????Bluetooth??????????????????????????????
