# DebrisSetUI（デブリセットのお題の画像とスロット）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/DebrisSetUI.cs`（表示）・`StarSweepers/Assets/Scripts/Game/UI/Editor/DebrisSetUISetup.cs`（プレハブ・検証シーン・仮の画像を作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/DebrisSetUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/DebrisSetUI.prefab` の一番上（Canvas と同じ物）。検証シーン `Scenes/Test/DebrisSetUITest.unity` にはプレハブのまま置いてある（Test Mode だけオン） |
| 関係するドキュメント | `機能ドキュメント/デブリセットのUI.md`・`機能ドキュメント/宇宙ごみのラウンドイベント.md`・`スクリプトの説明/Game/Match/SpaceJunkRound.md` |

## 何をするスクリプトか

ラウンドイベント「デブリセット」のお題を、チームごとの枠に物資の画像で横に並べる。
お題が新しく出たら1秒のスロットの演出をし、入れた物資にはチェックを付けて画像を暗くする。**表示するだけで、何も送らない。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/DebrisSetUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。チームごとの状態（`TeamState`）を作り、素材の欄の画像（Frame Sprite・Check Sprite）を全部の枠・チェックに入れる。最初は隠す
3. `Update` → `Tick(Time.unscaledTime)` … 毎フレーム
   1. `TryRead` … ラウンドがあれば、デブリセットが起きている間だけ、チームごとのお題（`SetSizeOf`・`SetItemAt`）とそろえた回数（`SetsCompletedOf`）を読む。ラウンドが無く Test Mode がオンなら、テスト用のお題を使う。どちらでもなければ隠して、覚えていたお題を忘れる（次のイベントでまたスロットから）
   2. `Receive` … お題が前と違えば（中身・数・そろえた回数のどれか）覚え直し、`StartSpin` でスロットを始める。**このイベントで初めて見たお題は、イベントの開始時刻（`SecondsSinceEventStarted`）にそろえる**（途中から見え始めたら、もう止まっている）
   3. `MarkCollected` … マスごとに「入れたか」を決める（下）
   4. `DrawPanel` … 枠ごとに、マスを出し入れし、回っているマスは物資を流し、止まったマスはお題の物資とチェックを出す

## 枠の左右（`UpdateSides`）

- 枠は場所で決まっている（`Panel0`＝左上、`Panel1`＝右上、`Panel2`・`Panel3`＝その下の段）。どの枠にどのチームを出すかを、段ごとの `rowSwapped` で決める（`TeamOfPanel`。入れ替えなら `panel ^ 1`）
- 毎フレーム、段の2チーム（0と1、2と3）のゴール（`SpaceJunkGoal.All` の `OwnerTeam`）を `Camera.main` の画面の横位置（0〜1）に直し、`DecideSwap` で決める。番号の小さいチームのゴールが右に見えれば入れ替える
- 2つの横位置の差が `SideMargin`（画面の幅の 0.1）より小さいときは前のまま（チームカメラで2つのゴールが縦に並ぶときに、ちらつかないように）。イベントの始まりで近いときは Swap Left Right の並び
- ラウンドが無いとき（検証シーン）・Follow Goal Side がオフ・ゴールやカメラが見つからないときは Swap Left Right の並び

## ほかのスクリプトとのつながり

- `IsShowing`（static）… いまお題を出しているか。仮の画面 `SpaceJunkMatchUI` の `DrawEventProgress` がこれを見て、出ている間は「お題 1/3 → +10 ■□…」の文字の行を出さない（同じお題が2か所に出て、左の枠と重なるため）

## スロットの計算

| 関数 | 何を返すか |
| --- | --- |
| `LaneStopTime(左から何番目, マスの数, Spin Seconds, First Stop Ratio)` | そのマスが止まる時刻。左端は Spin Seconds × First Stop Ratio、そこから右へ等しい間隔。**いちばん右は必ず Spin Seconds ちょうど** |
| `ReelPosition(経過秒, 止まる時刻, 流れる数)` | いくつ流れたか。`流れる数 × (1 − (1 − t)²)`。止まる時刻にちょうど「流れる数」になり、そこで止まる |

- `StartSpin` は、マスごとに「流れる物資の並び」（`reels`）を作る。最後がお題の物資で、隣どうしは違う物資にする（同じだと流れて見えない）。流れる数は、出だしの速さ（Spin Start Speed）× 止まる時刻 ÷ 2（2乗の曲線の出だしの速さが「流れる数 × 2 ÷ 止まる時刻」のため）
- 描くときは、マス1つに画像を2枚（`item` と `nextItem`）使う。`item` は「いま真ん中から上へ抜けていく物資」、`nextItem` は「下から上がってくる次の物資」。マスの `Reel` に付けた `RectMask2D` で、マスの外にはみ出した部分を切る
- 流れる物資は見た目だけなので、各PCで `System.Random` でばらばらに決める（ゲームの `UnityEngine.Random` の並びを乱さないため）。このマップで出ない物資は流さない（`SpaceJunkMaterials.IsUsed`）

## チェックの付け方（`MarkCollected`）

- 順番どおり（`SetOrdered`）… 左から `SetStepOf(team)` 個
- 順不同 … 種類ごとの「入れた数」（`SetProgressOf`）だけ、その種類のマスを左から埋める（同じ種類が2つあれば左から）

## テスト用（Test Mode）

`UpdateTest` が、最初に全チームのお題を引き（`TestDraw`）、そのあと Test Collect Interval ごとにランダムな1チームを進める（`TestStep`。そろっていれば次のお題、まだなら1個入れる。スロットが回っている間は待つ）。読んだあとは本番と同じ `Receive` と描画を通る。

## プレハブの作り（`DebrisSetUISetup`）

```
DebrisSetUI（Canvas：画面に重ねる・並び順 20、CanvasScaler：1920×1080 基準、DebrisSetUI）
└ Content（表示ごと隠す）
   ├ Panel0（左上）  … Image（フレーム・9スライス）＋ Horizontal Layout Group ＋ Content Size Fitter
   │  ├ Slot0〜7（58×58）
   │  │  ├ Reel（RectMask2D）
   │  │  │  ├ Item（物資の画像）
   │  │  │  └ NextItem（スロット中だけ）
   │  │  └ Check（右下。切り抜かない）
   ├ Panel1（右上）
   ├ Panel2（左の2段目）
   └ Panel3（右の2段目）
```

- 枠の横幅は Content Size Fitter が、出ているマスの数に合わせて変える。右の枠は右端を、左の枠は左端を基準にして伸びる
- 並び順 20 は、得点バー（10）より手前、始まりの演出（85）・ポーズ画面（100）より奥
- 仮の画像は `EnsureSprite` が**ファイルが無いときだけ**ピクセルを描いて作る（物資＝素材の色の丸にサイコロの目、チェック＝線2本を太らせた形、フレーム＝角丸の四角。どれも縁取り付き）。フレームには 9スライスの Border（26）を入れる

## 気をつけること

- **Test Mode は、Resources のプレハブではオフのまま**にする（オンだと、タイトルやロビーでもテストのお題が出てしまう）。自動テスト `Prefab_IsInResourcesAndWired` が確かめる
- Panels の割り当ては、メニューで作り直すと元に戻る。枠やマスを手で増やした・名前を変えたときは、スクリプトの欄の割り当ても直す
- `Tick` は時刻を引数で受け取る（`Time.unscaledTime`）。ポーズで `timeScale` が 0 になってもスロットは止まらない
- 画面に重ねる UI（Screen Space - Overlay）はカメラに写らないので、画面なしの Unity で画像を撮るときは、撮る間だけ Canvas を Screen Space - Camera にする（`AIの申し送り.md`）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/11 | Claude Code | 新規作成（大槻さんの依頼） |
| 2026/11/11 | Claude Code | `IsShowing` と、仮の画面がそれを見てお題の文字の行を出さないことを書いた |
| 2026/11/11 | Claude Code | 枠の左右をゴールの見え方で決める `UpdateSides`・`DecideSwap` を書いた |
