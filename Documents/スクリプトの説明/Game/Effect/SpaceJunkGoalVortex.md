# SpaceJunkGoalVortex・SpaceJunkVortexAbsorb（ゴールの渦）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Effect/SpaceJunkGoalVortex.cs`（渦の係）・`SpaceJunkVortexAbsorb.cs`（吸い込まれる係）・`SpaceJunkGoalVortexTestDriver.cs`（検証シーン専用） |
| 付いている場所 | 渦の係はプレハブ `Prefabs/Effect/SpaceJunkGoalVortex.prefab`。それを**進行役のプレハブ `Prefabs/System/SpaceJunkRound.prefab` の子**と、**ロビー `Scenes/MainLoop/Lobby.unity`** に置いてある。検証シーン `Scenes/Test/GoalVortexTest.unity` にも1つ。吸い込まれる係は、吸い込みが始まった物資に**再生中に付き、終わったら外れる**（どこにも置かない） |
| 見た目 | シェーダー `Art/Shaders/GoalVortex.shader`・マテリアル `Art/Materials/GoalVortex.mat` |
| 一緒に使うスクリプト | `SpaceJunkGoal.cs`・`SpaceJunkLobbyGoal.cs`（ゴール。物資を渦に渡す）、`HookableObject.cs`（吸い込み中の印 `IsAbsorbing`）、`FishingNetSupply.cs`（参加者への知らせ `BeginAbsorbClientRpc`）、`SpaceJunkGoalScoreEffect.cs`（床の四角の測り方 `TryGetGoalArea` を借りる）、`Game/Match/Editor/SpaceJunkGoalVortexSetup.cs`（メニュー）、`SpaceJunkGoalVortexTests.cs`（自動テスト） |
| 関係するドキュメント | `機能ドキュメント/ゴールの渦.md` |

## 何をするスクリプトか

- **渦の係（`SpaceJunkGoalVortex`）**：同じシーンの全部のゴール（試合のゴール `SpaceJunkGoal` と、ロビーの練習用ゴール `SpaceJunkLobbyGoal`）の床に、黒い渦を描く。ホストからは、ゴールの中の物資について「渦の円柱に入っていれば吸い込みを始める／外なら渦のほうへ引き寄せる」を受け持つ。
- **吸い込まれる係（`SpaceJunkVortexAbsorb`）**：吸い込み中の物資1つの動き。ホスト（1人用ならそのPC）は位置・回転・大きさを、参加者のPCは大きさだけを動かす。時間が来たら、ホストがゴールに知らせる（ゴールが点を数えて物資を消す）。

## 流れ

| いつ | 何をするか |
| --- | --- |
| ホストで、物資がゴールの判定の中にいる間（`OnTriggerStay`） | ゴールの `TryCollect` → `SpaceJunkGoalVortex.In(シーン).ServerFeed(ゴール, 物資, Collect)`。渦がシーンに無ければ、その場で `Collect`（入った瞬間に数えて消す） |
| `ServerFeed` | 消えた・吸い込み中・引っ掛けられている物、持ち主がまだホストに戻っていない物（`FishingNetSupply.IsOwner` が false）は何もしない。`TryGetArea` で円柱を求め、物資の重心が中なら吸い込みを始める（`SpaceJunkVortexAbsorb.Begin` → `FishingNetSupply.ServerBeginAbsorb`。`AbsorbCount` を1増やす）。外なら `Pull` |
| `Pull` | 渦の真ん中への水平の向きに、Pull Acceleration の加速度を加える。物資は当たり判定が複数あると1回の物理の更新で何度も呼ばれるので、`Time.fixedTime` で1回だけにする |
| `SpaceJunkVortexAbsorb.Begin`（ホスト） | 物資に係を付け、`HookableObject.SetAbsorbing(true)`、当たり判定を全部切り、物理を止める（Kinematic）。始めの位置を、渦の真ん中からの半径・角度・高さで覚える |
| `BeginAbsorbClientRpc`（参加者のPC） | 同じく係を付ける（位置は動かさないので、真ん中の位置は渡さない）。印を付け、当たり判定を切る |
| 係の `Update` | 進み具合 t＝（いまの時計 − 始めの時刻）÷ Absorb Seconds。大きさは `Lerp(1, End Scale, t^1.6)` 倍。ホストは半径を `1 − smoothstep(t)` 倍に縮め、角度を `Spin Turns × 2π × t²` 進め（上から見て左回り）、高さを渦の床の少し上へ寄せてから最後に Sink Depth だけ沈める。自分の回転は `−Self Spin Degrees × t²`（上から見て左回り） |
| t が 1 になった（ホスト） | 印を外し、大きさを元に戻してから、ゴールの `Collect`（`Absorbed` も出す）。`Collect` → `SpaceJunkRound.ServerCollect` → 数えたら `FlashClientRpc`（得点の光る演出）→ `HookableObject.Vanish`（全員の画面から消える）。戻した大きさは、同じフレームで見えなくなる |
| t が 1 になった（参加者） | 小さいまま、ホストが消すのを待つ。消されたら（`Vanish` で印が外れる）大きさを戻して係を外す。3秒たっても消されなければ、元に戻す |
| 吸い込み中に、ほかの理由で `Vanish` された（爆発など） | `Vanish` が印を外すので、係はそれに気づいて大きさを戻して外れる。**得点にはしない** |
| 渦の係の `Update` → `DrawAt(Time.time)` | ゴールごとに「出ている量」（0〜1）を Appear Seconds で増減し、円の板を `Graphics.RenderMesh` で描く。閉ざされたゴールには描かない。試合のゴールは、見た目（`IntroVisual`）の MeshRenderer が全部切られている間（始まりの演出でゴールが飛んでいる間）は出ている量を 0 へ |

### 時計（`SpaceJunkVortexAbsorb.ClockOf`）

物資がオンラインで出ていれば、その物資の `NetworkManager.ServerTime`（全員で合わせたホストの時計）。そうでなければ `Time.time`。
ホストは吸い込みを始めた時刻をこの時計で決めて参加者へ送り、参加者も同じ時計で進み具合を求めるので、小さくなる速さがそろう。

### 円柱の測り方（`MeasureArea`。ゴールごとに、はじめて聞かれたときに測って覚える）

1. 試合のゴールは `SpaceJunkGoalScoreEffect.TryGetGoalArea(ゴール, 0)` で床の四角（真ん中・幅・奥行き）を求める。ロビーの練習用ゴールは、判定の箱（BoxCollider）の大きさ。
2. 半径＝四角の短い辺の半分×Fill。
3. 上の端＝ゴールの判定の上の端。
4. 足もと＝ゴールの真ん中の真上（判定の上の端）から真下へ光線を飛ばし、当たった物のうち**いちばん高い面**（輸送機なら荷台、床のゴールなら色を塗る床）。物資・プレイヤー（CharacterController）・動く Rigidbody は数えない。見つからなければ四角の底の高さ。
5. 円柱の下の端は、足もとから Depth Below Floor だけ下。

エディタでプレハブ（の置かれたシーンの物）を選ぶと、円柱が紫の線で見える（`OnDrawGizmosSelected`）。

### 見た目（`GoalVortex.shader`）

床に寝かせた正方形の板の中に、円を描く。UV は真ん中が 0、ふちが ±1。

- 渦の腕：対数の渦巻き（角度×Arms − Twist×log(半径)×2 − 時刻の回転）。時間とともに、上から見て左回りに回りながら、模様が真ん中へ流れ込む。
- 黒さ（アルファ）：腕の上ほど・真ん中ほど濃い。ふちの近くは少し薄くして床につなぐ。
- ふちの光：外のふちにチームの色（`_Color`）。腕の間にも、ごく薄く色をのせて筋を見せる。
- 光の粒：角度で分けた筋の上を、ふちから真ん中へ流れる小さな点。
- 出てくるとき（`_Appear`）：真ん中から外へ円が広がる。
- 重ね方は `Blend One OneMinusSrcAlpha`（黒い所は下を暗くし、光る所は色を足す）。

## 変数の置き場所

| 変数 | ファイル | 意味 |
| --- | --- | --- |
| `vortexMaterial`・`fill`・`lift`・`appearSeconds`・`brightness` | `SpaceJunkGoalVortex.cs` | 見た目（インスペクター） |
| `absorbSeconds`・`spinTurns`・`selfSpinDegrees`・`endScale`・`sinkDepth` | `SpaceJunkGoalVortex.cs` | 吸い込みの動き（インスペクター。吸い込まれる係が読む） |
| `pullAcceleration`・`depthBelowFloor` | `SpaceJunkGoalVortex.cs` | 引き寄せと、円柱の下の端（インスペクター） |
| `areas`・`appear`・`visuals`・`lastPull` | `SpaceJunkGoalVortex.cs` | ゴールごとの円柱・出ている量・見た目の Renderer、物資ごとの最後に引き寄せた時刻 |
| `All`（static） | `SpaceJunkGoalVortex.cs` | シーンにある渦の係（`In(シーン)` で探す） |
| `Absorbed`（static event） | `SpaceJunkGoalVortex.cs` | ホストで吸い込み終わった瞬間（検証シーン・確認用） |
| `startTime`・`seconds`・`center`・`startRadius`・`startAngle`・`startY`・`startScale`・`startRotation` | `SpaceJunkVortexAbsorb.cs` | 吸い込み1回ぶんの始めの状態 |
| `authority` | `SpaceJunkVortexAbsorb.cs` | 位置を動かすPCか（ホスト・1人用） |
| `IsAbsorbing` | `HookableObject.cs` | 吸い込み中の印。**true の間は `IsVanished` も true**（フック・ボット・仕掛けが触らないように）。`Vanish` は、ほんとうに消えたか（`vanished`）だけを見る |
| `All`（static）・`BaseColor` | `SpaceJunkLobbyGoal.cs` | ロビーの練習用ゴールの一覧と、ふちの色 |

## 気をつけること

- **`IsVanished` の意味が少し広がった**（吸い込み中も true）。「もう無い物」として扱う所（フック・ボット・ベルトコンベア・列車・隕石・スポナーの数）は、それでよい。**「ほんとうに無くなったか」を知りたい所では `IsVanished && !IsAbsorbing` を使う**（例：重いデブリのイベントの一覧から外す処理 `SpaceJunkRound.ServerPruneLost`）。
- 検証シーンの係（`SpaceJunkGoalVortexTestDriver`）は、`NetworkManager` が無ければ作ってホストとして始める（ゴールが数えるのはホストだけのため）。物資の代わりの箱は通信の部品を持たないので、吸い込みはそのPCだけで動き、得点も入らない（試合の進行役が無いため）。代わりに `Absorbed` を受けて、得点の光る演出を出す。
- 画像で確かめるときは、`DrawAt(時刻, カメラ)` で好きなカメラに描いてから撮る（`AIの申し送り.md` の 2026/11/12「`Graphics.RenderMesh` で描くもの」）。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 新規作成 |
