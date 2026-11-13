# SweeperBot（新しい体のボットの頭）と、ギミックを教える係

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Bot/` の `SweeperBot.cs`・`SweeperBot.Gimmicks.cs`・`SweeperBotGimmicks.cs`・`SpaceJunkBotNavMesh.cs` |
| 付いている場所 | シーンには置かない。試合にボットを足すと、ホストのPCで体に `SweeperBot.AttachTo` で付く（`SpaceJunkSession.Bots.cs`） |
| 関係するドキュメント | `機能ドキュメント/宇宙ごみを集めるボット.md`（遊ぶ人から見た動き）・`機能ドキュメント/プレイヤーの本実装.md`（体の操作） |

## 何をするスクリプトか

**プレイヤーと同じ操作（見えないコントローラー `VirtualPlayerInput`）だけで、素材を拾って自分のゴールへ投げるボット。**
歩く道は、再生中にマップの当たり判定から作る「歩ける床の地図」（NavMesh。`SpaceJunkBotNavMesh`）で決める。
**ガラス・ベルトコンベア・列車・上下する床・回る床**のことは、`SweeperBotGimmicks` が教える（2026/11/12・小野田さんの依頼）。

## ファイルの分け方

| ファイル | 中身 |
| --- | --- |
| `SweeperBot.cs` | **入口**（設定の値・素材を選ぶ・立つ場所を決める・撃つ・投げる・投げた距離を覚える・歩く） |
| `SweeperBot.Gimmicks.cs` | ギミックを使う・避ける（列車とガラスから逃げる・流れる素材の先読み・上下する床に乗る・線路の手前で待つ・違う段の素材を後回しにする）と、**体が動いていないのを見つける**（`IsBodyStuck`） |
| `SweeperBotGimmicks.cs` | 別のクラス（`static`）。ステージのギミックを集めて、ボットの質問に答える。地図の材料を直す・ガラスの穴・つなぎ（NavMeshLink） |
| `SpaceJunkBotNavMesh.cs` | 別のクラス（`static`）。歩ける床の地図を作る・作り直す・道を調べる |

ギミック側には、ボットが読むだけの関数を足してある（動きは変えていない）。

| ギミック | 足した物 |
| --- | --- |
| ガラス | `GravityGlassFloor.BotInfo.cs`（`CanStandFor`・`SecondsUntilFall`・`Contains` など） |
| 列車 | `SpaceJunkTrain.cs` の下の「ボットに教える情報」（`InBand`・`SecondsUntilPasses`） |
| 上下する床 | `SpaceJunkLiftFloor.cs` の下の「ボットに教える情報」（`SecondsLeftAtBottom`・`SecondsLeftAtTop` など） |
| ベルト・回る床 | 足していない（ベルトは `FlowDirection`、回る床は向き（`transform.rotation`）を見るだけ） |

## 変数の置き場所

| 変数・定数 | ファイル |
| --- | --- |
| 設定の値（インスペクター）・素材・道・投げの記録 | `SweeperBot.cs` |
| `BeamSpeed`・`WalkSpeed`・`TrainDangerSeconds`・`BeltReplanSeconds`・`GlassReleaseSeconds`・`GlassThrowSeconds`・`OtherLevelPenalty`・`ridingLift`・`bodyAnchor`・`bodyAnchorTime` | `SweeperBot.Gimmicks.cs` |

## ギミックごとの考え方

| ギミック | ボットがすること | どこで |
| --- | --- | --- |
| **ガラス**（割れる） | ①もうすぐ割れる・割れたガラスは、地図に**穴をあける**（ガラスごとに `NavMeshObstacle` の Carve を ON/OFF）ので、道がそこを通らない ②ガラスは「歩きたくない場所」（地図の場所の種類 3、1m を 3m 分と数える）なので、硬い床があればそちらを通る ③立って撃つ場所は硬い床を選ぶ（ガラスは減点） ④割れるガラスの上では考える時間を待たない ⑤足もとのガラスがあと少しで割れるなら、硬い床か割れていないガラスへ逃げる（巻き取り中はゆっくりしか歩けないので早めに） | `SweeperBotGimmicks.UpdateGlassHoles`・`CheckStandPoint`・`TryFindGlassEscape`、`SweeperBot.ApplySafetyMove` |
| **ベルトコンベア** | ①ベルトで流れている素材も狙う（前は「動いている物」として無視していた）。着くころの場所を先読みして立つ場所を決め、0.4 秒ごとに決め直す。カーソルも光線が届くころの場所へ ②その先に床が無く、もうすぐ落ちる素材は狙わない ③ベルトの上には立たない。通るのも避ける（場所の種類 4） | `TryGetBeltMotion`、`SweeperBot.PlanItemPosition`・`AimPoint` |
| **列車** | ①線路の帯（列車の幅＋0.8m）には立たない。通るのも避ける（場所の種類 5） ②帯の中にいて 2 秒以内に列車が来るなら、帯の外へ出る ③帯を渡る前に、渡りきる前に列車が来るなら手前で待つ ④列車そのものは地図に入れない | `SecondsUntilTrain`・`TryFindTrackExit`・`ShouldWaitForTrain` |
| **上下する床** | ①地図では、床を「上にいるとき」の場所に置く（上の段とつながる） ②まわりの地面（乗り口）と床の上を、つなぎ（`NavMeshLink`）で結ぶ ③道が上の段へ上がるところでは、床が下で止まっている間に乗り、上に着くまで立って待つ（来ていなければ乗り口で待つ） ④床の上には立って撃たない。下りるときは、ふつうに歩いて段から降りる | `AdjustSources`・`AddLiftLinks`、`SweeperBot.HandleLiftRide` |
| **回る床** | ①回り終わって止まったら、地図を作り直す（作り直しの間も前の地図を使う） ②一本道の両はしと、すき間の向こうの床をつなぎで結ぶ（すき間は地図の上ではつながらないため） | `WatchSpinners`・`AddSpinnerLinks`、`SpaceJunkBotNavMesh.RequestRebuild` |

### 体が動いていないのを見つける（2026/11/13）

もともとの「進めなくなった」の判定（`IsStuck`）は、「目的地までの残りの道が縮んだか」で見ている。
ところが止まっている素材でも 1.5 秒ごとに道を引き直していて、そのたびに判定が戻るので、**地図に無い物（土台の上のノックバック爆弾など）に引っかかると、いつまでも押し続けていた**（STAGE_Simple の土台の上で見つかった。前のボットは土台に上がれなかったので出なかった）。
そこで、**体が Stuck Seconds＋1 秒のあいだ 0.5m も動いていなければ、その素材をあきらめる**判定（`IsBodyStuck`）を足した。撃っている・投げたあと休んでいる・撃つ場所で止まっている・床や列車を待っている・逃げている間は数えない（`ResetBodyStuck`）。

## 気をつけること

- **地図の「場所の種類」3・4・5 は、ボットだけが使っている。** プロジェクトの Navigation の Areas に名前は付けていない（番号だけで使える）。ほかで NavMesh を使うときは、番号がぶつからないようにする。
- ボットはホストのPCでだけ考える。ギミックの時計は全員で同じ（`ServerTime`）なので、ボットの読む「あと何秒」はホストの画面と同じ。
- ガラスの穴（`BotGlassHole_〜`）は、再生中にマップのシーンに作る。保存はされない。
- 新しいギミックを作ったら、ボットにどう見えるかを `SweeperBotGimmicks` に足す（動く床は、地図を作った瞬間の場所のまま入るため）。
- 確かめ方：画面なしの Unity でボット3人の試合を動かし、落ちた回数・投げた回数・点を数える（`Documents/調査の道具/ボットの試合を数字で測る.cs.txt`）。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/12 | Claude Code | 新規作成（小野田さんの依頼で、ボットがガラス・ベルト・列車・上下する床・回る床を使えるようにしたとき） |
| 2026/11/13 | Claude Code | 「体が動いていないのを見つける」を足した（土台の上の爆弾に引っかかって押し続けていたのを直したとき） |
