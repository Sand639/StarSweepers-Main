# DebrisSetUI（デブリセットのお題の、腕と枠とカードの表示）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/DebrisSetUI.cs`（表示）・`StarSweepers/Assets/Scripts/Game/UI/Editor/DebrisSetUISetup.cs`（プレハブ・検証シーン・物資の仮の画像を作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/DebrisSetUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/DebrisSetUI.prefab` の一番上（Canvas と同じ物）。検証シーン `Scenes/Test/EventUI_SetDebris.unity`・`Scenes/Test/DebrisSetUITest.unity` にはプレハブのまま置いてある（Test Mode だけオン） |
| 関係するドキュメント | `機能ドキュメント/デブリセットのUI.md`・`機能ドキュメント/宇宙ごみのラウンドイベント.md`・`スクリプトの説明/Game/Match/SpaceJunkRound.md` |

## 何をするスクリプトか

ラウンドイベント「デブリセット」のお題を、チームごとに「腕が画面の外から持ってくる枠」に、物資のカードで横に並べる。
イベントが始まったら 腕が入る → 枠が開く → カードが上から落ちる（スロット）。入れた物資には白いカードとチェックをかぶせる。イベントが終わったら逆回しでしまう。**表示するだけで、何も送らない。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/DebrisSetUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。チームごとの状態（`TeamState`）を作り、文字のフォント（Title Font、空なら `UiFont.Find`）を入れる。最初は隠す
3. `Update` → `Tick(Time.unscaledTime)` … 毎フレーム
   1. `TryRead` … ラウンドがあれば、デブリセットが起きている間だけ、チームごとのお題（`SetSizeOf`・`SetItemAt`）とそろえた回数（`SetsCompletedOf`）を読む。ラウンドが無く Test Mode がオンなら、テスト用のお題を使う（Test Event Running がオフなら「イベントなし」）
   2. `Receive` … お題が前と違えば覚え直し、`StartSpin` でスロットを始める。**このイベントで初めて見たお題は、イベントの開始時刻（`SecondsSinceEventStarted`）を `introStart` にし、スロットは「腕と枠の演出（Arm Slide Seconds ＋ Open Seconds）」のあとから始める**（途中から見え始めたら、もう止まっている）
   3. `MarkCollected` → `UpdateCollected` … マスごとに「入れたか」を決め、新しく入れたマスはその時刻（`collectedAt`。チェックがポンと出る演出に使う）を覚える
   4. `DrawAll` → `DrawPanel` … 枠ごとに、腕・枠・金具・文字・カードを並べる
4. 読めなかったとき（イベントが終わった）→ `TickClosing` … 表示していて、ラウンドがまだある（かテストモード）なら、`closeStart` から「カードが消える（Card Fade Seconds）→ 枠が閉じる → 腕が出ていく」を描いてから隠し、状態を忘れる。ラウンドごと無くなったら（シーンが変わった）すぐ隠す。しまう途中でまたイベントが始まったら、最初から出し直す

## 並べ方（`DrawPanel`）

- 位置はすべて「画像の元の大きさのピクセル」で計算し、チームの表示ごとの Scale（プレハブで 0.75）で縮める。Panel0〜3 は画面の角に付いていて、中の物の位置は毎フレームここで決める（**シーンで動かしても次のフレームで戻る**。変えたいときは「並べ方」の欄）
- 出てきてからの秒数 `intro` から、腕の入り具合 `slide`（0〜Arm Slide Seconds）と、枠の開き具合 `open`（そのあと Open Seconds）を出す（どちらも 3乗の減速）。しまうときは `intro` を逆に減らす
- 腕：画面の端から Arm Hide Distance だけ外にいた所から入る。枠の見える幅は `Closed Width → 枠の横幅` を `open` で補間し、`Image.Type.Filled`（横）の `fillAmount` で腕の側から見せる（伸び縮みさせないので、角の丸みがつぶれない）。金具：見えている枠の端に付いて動き、**閉じている間も出す**（閉じた形のまま入ってくる）。文字・カードは開ききってから出す
- 枠の横幅は `Card Margin ＋ Card Pitch × お題の数`（3個で素材の幅 347）
- **右の枠（Panel1・3）は x を反転**し、腕と金具の画像だけ `localScale.x = -1` で左右反転する。文字とカードは反転しない。カードは画面で見て左から1枚目（腕から遠い側）がお題の1個目になるように並べる（順番どおりのお題が、どちらの枠でも左から読める）

## カード（`DrawCard`）

- カードは `code` で描く：物資の番号（0〜4）。**入れ終わったカードは +100**（白いカードとチェックを出す）。-1 なら出さない
- カードの地（`background`）は、Tint Card By Item がオンなら白いカード（Plain Card Sprite ＝ `UI_SET_01`）を物資の色（`SpaceJunkMaterials.Color`）に染める。オフなら水色のカード（Card Sprite ＝ `UI_SET_02`）のまま
- チェック：入れた時刻から Check Pop Seconds で、大きさ Check Pop Scale → 1、白いカードの透明度 0 → 1
- カードごとの `CanvasGroup` で、しまうときにまとめて薄くする

## 枠の左右（`UpdateSides`）

- 枠は場所で決まっている（`Panel0`＝左上、`Panel1`＝右上、`Panel2`・`Panel3`＝その下の段）。どの枠にどのチームを出すかを、段ごとの `rowSwapped` で決める（`TeamOfPanel`。入れ替えなら `panel ^ 1`）
- 毎フレーム、段の2チーム（0と1、2と3）のゴール（`SpaceJunkGoal.All` の `OwnerTeam`）を `Camera.main` の画面の横位置（0〜1）に直し、`DecideSwap` で決める。番号の小さいチームのゴールが右に見えれば入れ替える
- 2つの横位置の差が `SideMargin`（画面の幅の 0.1）より小さいときは前のまま。イベントの始まりで近いときは Swap Left Right の並び
- ラウンドが無いとき（検証シーン）・Follow Goal Side がオフ・ゴールやカメラが見つからないときは Swap Left Right の並び

## ほかのスクリプトとのつながり

- `IsShowing`（static）… いまお題を出しているか（しまう途中も含む）。仮の画面 `SpaceJunkMatchUI` の `DrawEventProgress` がこれを見て、出ている間は「お題 1/3 → +10 ■□…」の文字の行を出さない

## スロットの計算

| 関数 | 何を返すか |
| --- | --- |
| `LaneStopTime(左から何番目, マスの数, Spin Seconds, First Stop Ratio)` | そのマスが止まる時刻（スロットの始まりから）。左端は Spin Seconds × First Stop Ratio、そこから右へ等しい間隔。**いちばん右は必ず Spin Seconds ちょうど** |
| `ReelPosition(経過秒, 止まる時刻, 流れる数)` | 何枚流れたか。`流れる数 × (1 − (1 − t)²)`。止まる時刻にちょうど「流れる数」になり、そこで止まる |
| `TitleDots(秒, Dot Seconds, Max Dots)` | 文字の後ろの点の数。1 → Max Dots → 1 をくり返す（6 なら 1,2,3,4,5,6,5,4,3,2,1,…） |

- `StartSpin` は、マスごとに「流れるカードの並び」（`reels`）を作る。**最初が前のお題のカード**（初めて出すときは -1＝無し。そろえて次のお題になったときは、前のお題を全部 +100 のチェック付きに）、**最後が新しいお題のカード**、間は隣どうし違う物資。流れる数は、出だしの速さ（Spin Start Speed）× 止まる時刻 ÷ 2
- 描くときは、マス1つにカードを2枚（`card` と `nextCard`）使う。`card` は「いま真ん中から下へ抜けていくカード」、`nextCard` は「上から降りてくる次のカード」（間隔 Reel Step）。**切り抜かない**ので、枠の上下にはみ出して見える（設計図に合わせた）
- 流れるカードは見た目だけなので、各PCで `System.Random` でばらばらに決める。このマップで出ない物資は流さない（`SpaceJunkMaterials.IsUsed`）

## チェックの付け方（`MarkCollected`）

- 順番どおり（`SetOrdered`）… 左から `SetStepOf(team)` 個
- 順不同 … 種類ごとの「入れた数」（`SetProgressOf`）だけ、その種類のマスを左から埋める（同じ種類が2つあれば左から）

## テスト用（Test Mode）

`UpdateTest` が、最初に全チームのお題を引き（`TestDraw`）、そのあと Test Collect Interval ごとにランダムな1チームを進める（`TestStep`。そろっていれば次のお題、まだなら1個入れる。スロットが回っている間は待つ）。読んだあとは本番と同じ `Receive` と描画を通る。
Test Event Running をオフにすると「イベントが終わった」扱いになり、しまう演出に入る。オンに戻すと、お題を引き直して腕が入ってくるところから。

## プレハブの作り（`DebrisSetUISetup`）

```
DebrisSetUI（Canvas：画面に重ねる・並び順 20、CanvasScaler：1920×1080 基準、DebrisSetUI）
└ Content（表示ごと隠す）
   ├ Panel0（左上の角。Scale 0.75）
   │  ├ Frame（枠 UI_SET_04_01。Filled・横）
   │  ├ Title（Text「特注依頼...」。左上が基準）
   │  ├ Slot0〜7
   │  │  ├ Card（CanvasGroup。101×112）
   │  │  │  ├ Base（カードの地 UI_SET_02。物資の色に染めるときは UI_SET_01）
   │  │  │  ├ Item（物資の画像 72×72）
   │  │  │  ├ Cover（白いカード UI_SET_01）
   │  │  │  └ Check（チェック UI_SET_00）
   │  │  └ NextCard（同じ作り。スロット中だけ）
   │  ├ Bracket（反対側の金具 UI_SET_03_02）
   │  └ Arm（腕 UI_SET_03_01）
   ├ Panel1（右上の角）
   ├ Panel2（左の2段目）
   └ Panel3（右の2段目）
```

- 重なり順は、あとに作った物ほど手前（枠 → 文字 → カード → 金具 → 腕）。カードの中は設計図の番号どおり 枠(3) の上に カード(2) → 白いカード(1) → チェック(0)
- 画像はどれも `SetNativeSize`（素材の元の大きさ）。素材が足りないとプレハブは作らず、エラーを出す
- 保存する前に `LayoutPreview` を呼び、開ききってお題が3個止まった形（1枚目にチェック）に並べておく（プレハブを開いたときに形が分かるように）。文字はフォントが再生中にしか入らないので、プレハブを開いても見えない
- 物資の仮の画像は `EnsureSprite` が**ファイルが無いときだけ**描いて作る（素材の色の丸にサイコロの目）
- `PlaceInEventScene` は `EventUI_SetDebris` にテストモードのプレハブを置く（置いてあれば何もしない）

## 気をつけること

- **Test Mode は、Resources のプレハブではオフのまま**にする（オンだと、タイトルやロビーでもテストのお題が出てしまう）。自動テスト `Prefab_IsInResourcesAndWired` が確かめる
- Panels の割り当ては、メニューで作り直すと元に戻る。枠やカードを手で増やした・名前を変えたときは、スクリプトの欄の割り当ても直す
- `Tick` は時刻を引数で受け取る（`Time.unscaledTime`）。ポーズで `timeScale` が 0 になっても演出は止まらない
- 画面に重ねる UI（Screen Space - Overlay）はカメラに写らないので、画面なしの Unity で画像を撮るときは、撮る間だけ Canvas を Screen Space - Camera にする（`AIの申し送り.md`）。2026/11/18 は、プレハブを置いて `Awake` と `Tick(時刻)` をリフレクションで呼び、0.05 秒ずつ時刻を進めて撮った

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/11 | Claude Code | 新規作成（大槻さんの依頼） |
| 2026/11/11 | Claude Code | `IsShowing` と、仮の画面がそれを見てお題の文字の行を出さないことを書いた |
| 2026/11/11 | Claude Code | 枠の左右をゴールの見え方で決める `UpdateSides`・`DecideSwap` を書いた |
| 2026/11/12 | Claude Code | デブリセットのお題を3個にしたのに合わせて、テストのお題の数（Test Set Size）とプレハブの見本のマスを3個にしたことを書いた（大槻さんの依頼） |
| 2026/11/18 | Claude Code | デザイナーさんの素材と設計図の動き（腕 → 枠が開く → カードが落ちる → チェック → しまう）に作り替えたのに合わせて、ほぼ全部を書き直した（大槻さんの依頼）。カードの地を物資の色に染めること、`TitleDots`・`TickClosing`・`LayoutPreview`・`PlaceInEventScene` を書いた |
| 2026/11/18 | Claude Code | 閉じた形（細い枠＋反対側の金具）で入ってくるようにしたのに合わせて、枠と金具の動きの説明を直した（大槻さんの依頼） |
