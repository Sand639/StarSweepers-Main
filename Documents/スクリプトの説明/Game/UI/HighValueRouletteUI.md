# HighValueRouletteUI（高単価デブリのルーレット）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/HighValueRouletteUI.cs`（表示）・`StarSweepers/Assets/Scripts/Game/UI/Editor/HighValueRouletteUISetup.cs`（プレハブ・検証シーンを作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/HighValueRouletteUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/HighValueRouletteUI.prefab` の一番上（Canvas と同じ物）。検証シーン `Scenes/Test/HighValueRouletteUITest.unity` にはプレハブのまま置いてある（Test Mode だけオン。得点バー（ステージと同じフレーム版 `Prefabs/UI/TeamScoreBarFrameUI.prefab`）もテストモードで置いてある） |
| 関係するドキュメント | `機能ドキュメント/高単価デブリのルーレットUI.md`・`機能ドキュメント/宇宙ごみのラウンドイベント.md`・`スクリプトの説明/Game/UI/DebrisSetUI.md`・`スクリプトの説明/Game/UI/TeamScoreBarUI.md` |

## 何をするスクリプトか

ラウンドイベント「期間限定高価値デブリ」が始まると、得点バーの時間の板の下に半円のルーレットを出し、レールの物資を回して、
**Spin Seconds（2秒）ちょうどで高単価の種類を真下に止める。** イベントが終わるとしまう。**表示するだけで、何も送らない。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/HighValueRouletteUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。`Wheel` の今の位置を「出ている位置」として覚え、しまうときに上へ動かす量（`hideOffset`。円盤の下の端が `Clip` の上の端より上に来るまで）を計算する。Frame Sprite があればフレームに入れる。最初は隠す
3. `Update` → `Tick(Time.unscaledTime)` … 毎フレーム
   1. `TryRead` … ラウンドがあれば、得点制で高単価デブリが続いている間だけ、種類（`TryGetHighValueKind`）とイベントの経過秒（`SecondsSinceEventStarted`）を読む。ラウンドが無く Test Mode がオンなら、テスト用の値（下）を使う
   2. 続いていて、まだ出していない（または前のフレームは続いていなかった・種類が変わった）なら `Begin` … **イベントの開始時刻にそろえて**回し始める（途中から見え始めたら、もう止まっている）
   3. `reveal`（0＝しまわれている、1＝出ている）… 続いている間は「回り始めてからの秒数 ÷ Slide Seconds」。終わったら Slide Seconds かけて 0 へ戻し、0 になったら隠す
   4. `Draw` … 円盤の位置（`reveal` を `1 − (1 − r)²` でなめらかにして、しまう位置との間）と、レールの物資の位置・画像・大きさを合わせる

## ほかのスクリプトとのつながり

（2026/11/26 まで、仮の画面 `SpaceJunkMatchUI` のために `OccupiedScreenHeight`（ルーレットの下の端の高さ）と `IsSpinning`（回っているか）を出していた。仮の画面のイベントの一行をやめたので、どちらも消した。）

## レールの計算

| 関数 | 何を返すか |
| --- | --- |
| `ReelCount(Spin Start Speed, Spin Seconds)` | 決まるまでに真下を通る物資の数。`出だしの速さ × 秒数 ÷ 2`（2乗の曲線の出だしの速さが「数 × 2 ÷ 秒数」のため）。最低 3 |
| `DebrisSetUI.ReelPosition(経過秒, Spin Seconds, 数)` | いくつ流れたか（デブリセットのスロットと同じ `数 × (1 − (1 − t)²)`）。Spin Seconds でちょうど「数」になり、そこで止まる |
| `RailAngle(流れた数, 何番目, Item Angle)` | その物資の角度（度）。`(流れた数 − 何番目) × Item Angle`。**0＝真下、プラス＝右**。流れた数が増えると角度が増えるので、物資は**左から来て右へ流れる** |
| `RailPoint(角度, Rail Radius)` | 円盤の中心から見た置き場所 `(sin, −cos) × 半径` |
| `FillReel(並び, 決まる場所, 種類, 選び方)` | 決まる場所に高単価の種類を入れ、前後を「となりと違う物資」で埋める |

- 並び（`reel`）の長さは `数 + 1 + 左右3個ずつ`。`reel[数 + 3]` が決まる物資。止まったあとも、その左右に物資が並んで見えるようにするため
- 画像は7枚（`Item0`〜`6`）だけを使い回す。毎フレーム、いま真下にいちばん近い物資（`流れた数` の整数の部分）を真ん中にして、その前後3個ずつに画像を当てる。角度が ±120度より外の画像は消す（円盤の上の半分は、どのみち `Clip` の外で見えない）
- 止まってから 0.3 秒、決まった物資を `sin` の山で 1.3 倍まで大きくして戻す
- 流れる物資は見た目だけなので、各PCで `SpaceJunkMaterials.RandomUsedIndex`（このマップで出る種類から）でばらばらに決める

## テスト用（Test Mode）

「Test Event Seconds 秒のイベント → しまわれて 1.5 秒待つ」をくり返す。回ごとに種類を選び直す。右クリックメニュー「テスト：もう一度回す」は、最初の回からやり直す。

## プレハブの作り（`HighValueRouletteUISetup`）

```
HighValueRouletteUI（Canvas：画面に重ねる・並び順 9、CanvasScaler：1920×1080 基準、HighValueRouletteUI）
└ Content（表示ごと隠す）
   └ Clip（RectMask2D。画面の上から 110（得点バーの横長の枠の上の端）より下、幅 800・高さ 420）
      └ Wheel（フレームの画像の大きさ 382×169。基準点は上の端の真ん中。Clip の上の端より 90 下）
         ├ Frame（フレームの画像 EventUI/HighValueDebris/UI_roulette）
         └ Window（RectMask2D。半円の上の辺（画像の上から 8）より下。物資を半円の中だけに見せる）
            └ Item0〜Item6（64×64。物資の画像。Window の上の辺の真ん中から測った位置を、スクリプトが毎フレーム決める）
```

- 並び順 9 は、得点バー（10）のすぐ奥。得点バーと時間の板が半円の上の部分を隠すので、「裏から出てくる」ように見える
- `Clip` は、得点バーの横長の枠の上の端（画面の上から 110）より上を切り抜く。しまう途中の半円が、得点バーより上に見えないように
- 物資の画像は `Assets/Art/Sprites/DebrisSetItem1〜5.png`（デブリセットのUIと同じ）を読む。無ければエラーで止まる
- フレームの画像が読めないときは、エラーで止まる

## 気をつけること

- **Test Mode は、Resources のプレハブではオフのまま**にする（オンだと、タイトルやロビーでもルーレットが出てしまう）。自動テスト `Prefab_IsInResourcesAndWired` が確かめる
- 得点バー（フレーム版）の大きさ・位置（`TeamScoreBarUISetup` の FrameWidth 900・FrameTop 8）を変えたら、`HighValueRouletteUISetup` の `ClipTop`・`WheelTop` も見直す
- `Tick` は時刻を引数で受け取る（`Time.unscaledTime`）。ポーズで `timeScale` が 0 になってもルーレットは止まらない
- 画面に重ねる UI（Screen Space - Overlay）はカメラに写らないので、画面なしの Unity で画像を撮るときは、撮る間だけ Canvas を Screen Space - Camera にする（`AIの申し送り.md`）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | フレームをデザイナーさんの本素材に差し替え、新しい得点バー（フレーム版）に合わせて置き直したのに合わせて、プレハブの作り（`Window` を足した）と置き場所を書き直した。仮のフレームを描く処理はやめた。「気をつけること」が2回書かれていたのを1回にし、この変更ログを足した |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | `OccupiedScreenHeight`・`IsSpinning`・`MeasureOccupiedHeight` を消した（使っていた仮の画面のイベントの一行をやめたため） |
