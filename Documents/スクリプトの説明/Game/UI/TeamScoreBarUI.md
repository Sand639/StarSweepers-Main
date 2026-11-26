# TeamScoreBarUI（チームの得点バー）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/TeamScoreBarUI.cs`（本体）・`Editor/TeamScoreBarUISetup.cs`（プレハブ・検証シーンを作り、ステージに置くメニュー） |
| 付いている場所 | `Prefabs/UI/TeamScoreBarFrameUI.prefab`（フレーム版。**2026/11/17 から全ステージに置いてあるのはこちら**）と `Prefabs/UI/TeamScoreBarUI.prefab`（今までの版。いまはどのステージにも置いていない）の一番上（Canvas ごとのプレハブ）。`TeamScoreFlameEffect` と一緒に付いている |
| 関係するドキュメント | `機能ドキュメント/チームの得点バー.md`・`スクリプトの説明/Game/UI/TeamScoreFlameEffect.md` |

## 何をするスクリプトか

画面の上の真ん中に、**得点の比率で幅が変わるチームの帯**を並べ、真ん中の丸に**残り時間**を出す。
**表示するだけ**で、得点を数える・勝敗を決める・通信することはしない。

## 流れ

毎フレームの `Update` で、次の順に動く。

1. `TryReadValues` … 出す値を読む
   - `SpaceJunkRound.Current` があれば、`ScoreOf(チーム)`・`RemainingSeconds`・`SpaceJunkSession.Current.TeamCount` を読む。**得点制（Rule = Score）のときだけ**出す
   - 無くて Test Mode がオンなら、インスペクターの「テスト用」の値（時間が減り、Test Auto Score なら一定の間隔で点が入る）
   - どちらでもなければ、子の `Content` を隠して終わり
2. `OccupiedScreenHeight` を測る（時間の丸の下の端まで、画面の上から何ピクセルか）
3. `NotifyScoreChanges` … 前のフレームから得点が変わったチームがあれば `ScoreChanged(チーム, 新しい得点, 増えた量)` を呼ぶ
4. `UpdateShares` … 帯の幅の目標を出し、表示中の幅を近づける
5. `ApplyLayout` … 帯（`Segment0`〜`3`）の `anchorMin.x`・`anchorMax.x` を左から順に入れ、色と得点の数字を合わせる
6. `ApplyTime` … 残り時間を「分:秒」で出す（切り上げ。Warn Seconds を切ったら警告の色）

### 幅の決め方

- 目標の幅 = `Min Share + (1 - Min Share × チーム数) × 得点 / 合計`。全チーム 0 点なら等分
- 表示中の幅は `1 - exp(-Smooth Speed × 経過時間)` の割合で目標へ近づける（フレームの速さに関係なく同じ速さ）。`unscaledDeltaTime` なのでポーズ中も動く
- Reverse Order がオンなら、チーム番号の大きい順に左から並べる（2チームなら 左＝赤・右＝青）

### 出し始めの1フレーム（snap）

出し始めとチーム数が変わったときは、幅をすぐ目標に合わせ、**`ScoreChanged` は呼ばずに得点を覚えるだけ**にする。
シーンを読み込んだ直後・途中から入った人の画面で、いきなり「得点が入った」演出が出ないようにするため。

## ほかのスクリプトとのつながり

| 相手 | つながり |
| --- | --- |
| `SpaceJunkRound` / `SpaceJunkSession` / `SpaceJunkTeams` | 値を読むだけ（得点は NetworkList、残り時間はサーバーの時刻から計算、チーム数は NetworkVariable なので、**どのPCでも同じ値**） |
| `TeamScoreFlameEffect` | `ScoreChanged` を受けて炎を燃え上がらせる。`SlotOfTeam`・`SegmentOfSlot`・`ScoreTextOfSlot` で帯と数字を探す |
| `SpaceJunkMatchUI`（仮の画面） | `OccupiedScreenHeight` が 0 より大きい間、残り時間を出さない（バーの丸に任せる）。見出し・イベントの一行・左のチームの行は、2026/11/25〜26 にやめた |

## メニュー（`TeamScoreBarUISetup`）

| メニュー | すること |
| --- | --- |
| `Tools > StarSweepers > チーム得点バーの検証シーンを作る` | フレーム版のプレハブが**無ければ**作り（**今までの版のプレハブには触らない**。ステージが使っているため）、`Scenes/Test/TeamScoreBarUITest.unity` を作り直す（フレーム版の「色＋炎」＝上、「色だけ」＝下。どちらも Test Mode オン） |
| `Tools > StarSweepers > チーム得点バーのプレハブを更新` | 今までの版のプレハブだけ作り直す |
| `Tools > StarSweepers > チーム得点バー（フレーム版）のプレハブを作り直す` | フレーム版のプレハブを作り直す（**手で直した FillArea などは消える**） |
| `Tools > StarSweepers > チーム得点バー（フレーム版）にデジタル数字のフォントを付ける` | `EnsureDsegFont` で DSEG の ttf から TextMeshPro のフォントを作り（無ければ）、`LoadPrefabContents` でフレーム版のプレハブを開いて、`RoundText`・`TimeText` のフォントだけを替えて保存する（ほかは触らない） |
| `Tools > StarSweepers > チーム得点バー（フレーム版）に炎の層を足す` | `AddFireLayer` で、今あるフレーム版のプレハブに `Frame/FireArea`（FillArea と同じ四角・マスク無し）と `Fire0〜3` を足し、色の帯から炎のマテリアルを外す。**すでに FireArea があれば何もしない**。炎のマテリアルはあればそのまま使う（調整した値は消えない） |

### DSEG のフォント（`EnsureDsegFont`）

- 元の ttf：`Assets/TextMesh Pro/Fonts/DSEG14Classic-Bold.ttf`（ROUND。14セグなので英字が出る）・`DSEG7Classic-Bold.ttf`（時間。7セグ）
- 作るフォント：同じ場所の `DSEG14ClassicBoldSDF.asset`・`DSEG7ClassicBoldSDF.asset`。90pt・SDF・**1024×1024**。数字・大文字の英字・空白・`:.-` を焼き込んでから**静的**にする（ビルドで ttf を読みに行かない）。**512 だと半分ほどしか入らず、残りの字が別のフォント（LiberationSans）で出た**
- 足りない文字は、作るときに `入らなかった文字` の警告がログに出る。焼き込む文字を増やすなら `DsegCharacters` に足し、`.asset` を消してメニューを実行し直す
- DSEG の空白は細いので、板の文字は `wordSpacing = 60`（「ROUND」と「01」の間）。字間（characterSpacing）は 0、太字の指定はしない（もともと Bold の字なので）
| `Tools > StarSweepers > チーム得点バー（フレーム版）を全ステージに置く` | `PlaceInStages`。`StageFolder`（`Assets/Scenes/Stage`）の中のシーンを `FindAssets("t:Scene")` で全部開き、置いてある得点バーのプレハブの元を `GetCorrespondingObjectFromSource` で見分けて、**今までの版なら外し、フレーム版が無ければ置いて**保存する。**ステージのほかの物には触らない**。何度実行してもよい |

- 今までの版のプレハブは毎回組み立て直すので、プレハブを手で直した部分は消える。**フレーム版は、無いときだけ作る**（手で直す前提のため）
- マテリアル（`TeamScoreBarFire.mat`・`TeamScoreNumber.mat`）と丸の画像は、**無いときだけ作る**（インスペクターで調整した値を残すため）。ただし炎のマテリアルの `_BarRatio` は帯の高さで決まる値なので、毎回入れ直す
- 帯の Image は、帯の高さより上へ `FireRise`（44）だけ高くしてある（炎の舌が立ちのぼる所）。得点の数字は、その 44 を除いた帯の高さの中に置く

### フレーム版（`TeamScoreBarFrameUI.prefab`）の組み立て

| 部品 | 中身 |
| --- | --- |
| `Content/Frame` | 枠の画像と同じ縦横比（幅 900）。画面の上の真ん中。`occupiedArea` に入れてあるので、ステージに置いたときは枠の下の端までが「使っている範囲」になる |
| `Frame/FillArea` | 得点の枠の内側（画像のピクセル x 126〜2043・y 263〜487 を、アンカーの割合で置く）。**RectMask2D** 付きで、炎がこの四角の外に出ない。**大きさは手で変えてよい** |
| `FillArea/Segments/Segment0〜3` | ただの色の帯。FillArea いっぱい。マテリアル無し（2026/11/12 から。炎は下の FireArea） |
| `Frame/FireArea` | 炎だけの層。FillArea のすぐ後（フレームの画像より奥）。**マスク無し**で、作ったときは FillArea と同じ四角。**手で広げて枠からはみ出させてよい** |
| `FireArea/Fire0〜3` | 帯ごとの炎（`TeamScoreBarFireFrame.mat`。Bar Ratio 0.5）。横の位置・幅は `TeamScoreFlameEffect` が毎フレーム色の帯に合わせる |
| `Frame/FrameImage` | 枠の画像。帯より後（手前）に描くので、帯のはみ出しを隠す |
| `Frame/RoundText`・`Frame/TimeText` | 上の板・下の板の暗い部分に合わせた文字（Auto Size。板の大きさに合わせて縮む） |

画像の中のピクセルの位置は、`TeamScoreBarUISetup` の `FillPixels`・`RoundPixels`・`TimePixels`（枠の画像を測った値）。画像を差し替えて位置が変わったら、ここを直して `フレーム版のプレハブを作り直す` を実行する。

### 2026/11/12 に足した欄

| 欄 | 中身 |
| --- | --- |
| Round Text / Round Format | ラウンドの番号の文字と書き方（`ROUND {0:00}` → `ROUND 01`）。番号は `SpaceJunkSession.CurrentRound`、ラウンドが無いときは Test Round |
| Time Style / Seconds Digits | 残り時間の書き方。`MinutesSeconds`＝`1:23`（今までの版）、`Seconds`＝秒だけを決めた桁数で（3桁なら `035`。フレーム版） |
| Occupied Area | 画面の上で使っている範囲（`OccupiedScreenHeight` を測る相手）。空なら時間の文字の親（今までの版の丸） |

## 気をつけること

- `ApplyLayout` は帯の**横の余白だけ** 0 にする。縦の余白（炎の舌の所）を 0 にすると、炎がバーの中に押し込まれる
- 数字の大きさを変えるとき（`TeamScoreFlameEffect`）は `fontSize` を使う。`localScale` だと、帯いっぱいに広がった文字の枠ごと大きくなり、数字がずれる
- 数字の文字のマテリアルは、TextMeshPro の Mobile のシェーダーなので **`OUTLINE_ON` が入っていないと縁取りが出ない**
- `OccupiedScreenHeight` は static。バーが消えたとき（`OnDisable`・表示しないとき）は 0 に戻す

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/11 | Claude Code（依頼：ソウホイチョン） | 新規作成（旧リポジトリから移したときに、コードの長いコメントをここへ移した） |
| 2026/11/12 | Claude Code（依頼：ソウホイチョン） | フレーム版（新しいデザイン案）の組み立てと、足した欄（ラウンドの文字・時間の書き方・使っている範囲）、メニューの変更を書いた |
| 2026/11/12 | Claude Code（依頼：ソウホイチョン） | DSEG のフォントの作り方と、フォントを付けるメニューを書いた |
| 2026/11/12 | Claude Code（依頼：ソウホイチョン） | フレーム版の炎の層（FireArea）と、それを足すメニューを書いた |
| 2026/11/17 | Claude Code（依頼：ソウホイチョン） | ステージに置くメニューを、フレーム版を全ステージに置く（今までの版と入れ替える）形に直したので書き直した |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 仮の画面が `OccupiedScreenHeight` を見て行うことを、残り時間を出さないことだけに直した（イベントの一行などをやめたため） |
