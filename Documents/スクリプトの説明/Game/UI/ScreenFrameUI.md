# ScreenFrameUI（試合の画面の枠）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/ScreenFrameUI.cs`（出す・隠す）・`StarSweepers/Assets/Scripts/Game/UI/Editor/ScreenFrameUISetup.cs`（プレハブ・検証シーンを作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/ScreenFrameUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/ScreenFrameUI.prefab` の一番上（Canvas と同じ物） |
| 関係するドキュメント | `機能ドキュメント/試合の画面の枠.md` |

## 何をするスクリプトか

試合中（`SpaceJunkRound.Current` がある間）だけ、画面のふちに枠の画像 `UI_MainFrame_02` を重ねる。**表示するだけ。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/ScreenFrameUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。最初は Test Mode のときだけ出す
3. `Update` … Test Mode か、`SpaceJunkRound.Current` があれば `Content` を出し、無ければ隠す

## プレハブの作り（`ScreenFrameUISetup`）

```
ScreenFrameUI（Canvas：画面に重ねる・並び順 40、CanvasScaler：1920×1080 基準、ScreenFrameUI）
└ Content（表示ごと隠す）
   └ Frame（枠の画像。9分割（Sliced）。画面いっぱいから右に 5、下に 3 はみ出す）
```

- 画像は 1925×1083 で、枠はその左上の 1920×1080。右と下の透明な余白のぶん、`Frame` を画面の外へはみ出させて、枠のふちを画面のふちに合わせる（`RightMargin`・`BottomMargin`）
- 画像の取り込みの設定に 9 分割の境目（四方 110。角の丸みより広い）を付けてある。境目が無いときは、ツールは引き伸ばし（Simple）で作る

## 気をつけること

- **Test Mode は、Resources のプレハブではオフのまま**にする（オンだと、タイトルやロビーでも枠が出る）。自動テスト `Prefab_IsInResourcesAndWired` が確かめる
- 並び順 40 より手前に出したい試合のUIができたら、そちらの並び順を 40 より大きくする（いまは始まりの演出 85・ポーズ画面 100・勝敗の切り替え 30000）
- 2026/11/26 のプレハブは、Unity を使わずにファイルを直接作った（本人が Unity を開いていたため）。メニューで作り直すと、ツールの通りに作り直される

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 新規作成 |
