# GoalEffect・GoalAbsorb（ゴールの渦と光の柱）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Effect/GoalEffect.cs`（渦の係）・`GoalAbsorb.cs`（吸い込まれる係）・`GoalEffectTestDriver.cs`（検証シーン専用） |
| 付いている場所 | 渦の係はプレハブ `Prefabs/Effect/GoalEffect.prefab`。それを**進行役のプレハブ `Prefabs/System/SpaceJunkRound.prefab` の子**と、**ロビー `Scenes/MainLoop/Lobby.unity`** に置いてある。検証シーン `Scenes/Test/GoalEffectTest.unity` にも1つ。吸い込まれる係は、吸い込みが始まった物資に**再生中に付き、終わったら外れる**（どこにも置かない） |
| 見た目 | シェーダー `Art/Shaders/GoalVortex.shader`・マテリアル `Art/Materials/GoalVortex.mat` |
| 一緒に使うスクリプト | `SpaceJunkGoal.cs`・`SpaceJunkLobbyGoal.cs`（ゴール。物資を渦に渡す）、`HookableObject.cs`（吸い込み中の印 `IsAbsorbing`）、`NetSupply.cs`（参加者への知らせ `BeginAbsorbClientRpc`）、`SpaceJunkGoalScoreEffect.cs`（床の四角の測り方 `TryGetGoalArea` を借りる）、`Game/Match/Editor/GoalEffectSetup.cs`（メニュー）、`GoalEffectTests.cs`（自動テスト） |
| 関係するドキュメント | `機能ドキュメント/ゴールの渦と光の柱.md` |

## 何をするスクリプトか

> 2026/11/17 に名前を変えた（旧：`SpaceJunkGoalVortex`・`SpaceJunkVortexAbsorb`・`SpaceJunkGoalVortexTestDriver`・`SpaceJunkGoalVortexSetup`・`SpaceJunkGoalVortexTests`）。.meta の番号は同じなので、プレハブ・シーンの参照は前のまま。

- **見た目の種類（`GoalEffectStyle`）**：`style` のプルダウンで **渦（Vortex）** と **光の柱（Pillar）** を選ぶ。`DrawAt` は種類によって `DrawVortices`（渦）か `DrawPillars`（光の柱）を呼ぶ。判定の円柱・引き寄せ・吸い込みの始まりと終わり・得点の流れ（`ServerFeed`）は共通。
- **光の柱（`DrawPillars`）**：床の光（円の板。筒の 1.35 倍まで）・筒（`CreateCylinderMesh`。半径 1・高さ 1 の筒を、渦の半径×高さ×渦の半径にする）・光の筋・きらめき（`CreateBillboardMesh`。カメラのほうを向く板。足もとは筒の中の床で、高さと動きはシェーダーが決める）を `GoalPillar.shader` の `_Part` 0〜3 で描く。色は `pillarUseTeamColor` ならチームの色、そうでなければ `pillarColor`。`_Funnel` に（半径, 高さ）を渡す。
- **物資の動きの選び方（`absorbMotion`・`UsesPillarMotion`）**：`GoalAbsorbMotion` のプルダウン（見た目に合わせる・渦の吸い込み・光の柱）。`UsesPillarMotion` は「光の柱」を選んだとき、または「見た目に合わせる」で見た目が光の柱のとき true。ホストの `ServerFeed` が吸い込みを始めるときにこれを読み、`GoalAbsorb.Begin(…, pillarMotion, …)` に渡し、`NetSupply.ServerBeginAbsorb(開始時刻, 秒数, pillarMotion)` で参加者のPCへも送る（参加者は自分のプルダウンを見ない）。吸い込みの長さ `CurrentAbsorbSeconds` もこれで決まる。
- **光の柱のときの物資の動き（`GoalAbsorb.ApplyPillar`）**：吸い込みの長さは `CurrentAbsorbSeconds`＝Pillar Move Seconds＋Pillar Rise Seconds（ホストが参加者へ送る秒数もこれ）。前半は、始めの位置から「真ん中の上（`PillarTopY`＝床＋光の柱の高さ）」へ一直線に運ぶ（だんだん遅く）。後半は、そこから Pillar Rise Distance だけゆっくり上げる（smoothstep）。**大きさは変えない**（当たり判定は渦と同じく、始まったときに切る）。位置はホストだけが動かす。どのPCでも、そのPCの吸い込みが終わったときに `GoalEffect.PlayFlash` で消えた瞬間の光を出す（ホストは `Finish`、参加者は進み具合が 1 になったとき）。光の柱の動きかどうかは、吸い込みが始まったときにホストが決めた値（`IsPillar`）。
- **消える光（`DrawFlashes`）**：見た目の種類にかかわらず毎回描く。吸い込み中の物資（`GoalAbsorb.All`）のうち光の柱の物は、残りが Pillar Glow Seconds より短くなったら、物資の位置にやわらかい光の玉（`_Flash.x`＝0）。`PlayFlash` で足した光は、Pillar Flash Seconds の間、白っぽい芯・十字・広がる輪（`_Flash.x`＝1）。色は、いちばん近いゴールの光の柱の色（`ColorNear`）。`GoalPillar.shader` の `_Part`＝4 で、カメラのほうを向く板（`CreateFlashMesh`）に描く。

- **渦の係（`GoalEffect`）**：同じシーンの全部のゴール（試合のゴール `SpaceJunkGoal` と、ロビーの練習用ゴール `SpaceJunkLobbyGoal`）の床に黒い渦を、その上に半透明のすり鉢（立体の渦）・らせんの帯・黒いかけらを描く（どれも黒と灰色。Use Team Color がオンなら明るい所がチームの色）。ホストからは、ゴールの中の物資について「渦の円柱に入っていれば吸い込みを始める／外なら渦のほうへ引き寄せる」を受け持つ。
- **吸い込まれる係（`GoalAbsorb`）**：吸い込み中の物資1つの動き。ホスト（1人用ならそのPC）は位置・回転・大きさを、参加者のPCは大きさだけを動かす。時間が来たら、ホストがゴールに知らせる（ゴールが点を数えて物資を消す）。

## 流れ

| いつ | 何をするか |
| --- | --- |
| ホストで、物資がゴールの判定の中にいる間（`OnTriggerStay`） | ゴールの `TryCollect` → `GoalEffect.In(シーン).ServerFeed(ゴール, 物資, Collect)`。渦がシーンに無ければ、その場で `Collect`（入った瞬間に数えて消す） |
| `ServerFeed` | 消えた・吸い込み中・引っ掛けられている物、持ち主がまだホストに戻っていない物（`NetSupply.IsOwner` が false）は何もしない。`TryGetArea` で円柱を求め、物資の重心が中なら吸い込みを始める（`GoalAbsorb.Begin` → `NetSupply.ServerBeginAbsorb`。`AbsorbCount` を1増やす）。外なら `Pull` |
| `Pull` | 渦の真ん中への水平の向きに、Pull Acceleration の加速度を加える。物資は当たり判定が複数あると1回の物理の更新で何度も呼ばれるので、`Time.fixedTime` で1回だけにする |
| `GoalAbsorb.Begin`（ホスト） | 物資に係を付け、`HookableObject.SetAbsorbing(true)`、当たり判定を全部切り、物理を止める（Kinematic）。始めの位置を、渦の真ん中からの半径・角度・高さで覚える |
| `BeginAbsorbClientRpc`（参加者のPC） | 同じく係を付ける（位置は動かさないので、真ん中の位置は渡さない）。印を付け、当たり判定を切る |
| 係の `Update` | 進み具合 t＝（いまの時計 − 始めの時刻）÷ Absorb Seconds。大きさは `Lerp(1, End Scale, t^1.6)` 倍。ホストは半径を `1 − smoothstep(t)` 倍に縮め、角度を `Spin Turns × 2π × t²` 進め（上から見て左回り）、高さを「いまの半径でのすり鉢の面の高さ＋0.2m（`FunnelSurfaceY`）」と「始めの高さ」の低いほうへ寄せてから、最後に Sink Depth だけ沈める（すり鉢の面に沿って下りて見える）。自分の回転は `−Self Spin Degrees × t²`（上から見て左回り） |
| t が 1 になった（ホスト） | 印を外し、大きさを元に戻してから、ゴールの `Collect`（`Absorbed` も出す）。`Collect` → `SpaceJunkRound.ServerCollect` → 数えたら `FlashClientRpc`（得点の光る演出）→ `HookableObject.Vanish`（全員の画面から消える）。戻した大きさは、同じフレームで見えなくなる |
| t が 1 になった（参加者） | 小さいまま、ホストが消すのを待つ。消されたら（`Vanish` で印が外れる）大きさを戻して係を外す。3秒たっても消されなければ、元に戻す |
| 吸い込み中に、ほかの理由で `Vanish` された（爆発など） | `Vanish` が印を外すので、係はそれに気づいて大きさを戻して外れる。**得点にはしない** |
| 渦の係の `Update` → `DrawAt(Time.time)` | ゴールごとに「出ている量」（0〜1）を Appear Seconds で増減し、円の板（`_Part`＝0。**Show Floor Vortex がオンのときだけ**。2026/11/17 からオフ）と、すり鉢（`_Part`＝1。高さは Funnel Height×出ている量）・らせんの帯（`_Part`＝2。すり鉢と同じ大きさ）・かけら（`_Part`＝3。大きさはそのまま m で、位置はシェーダーが決める）を `Graphics.RenderMesh` で描く。すり鉢から先は `_Funnel`（底の割合・曲がり具合・渦の半径・高さ）も渡す。閉ざされたゴールには描かない。試合のゴールは、**始まりの演出の間（`SpaceJunkRound.Current.IsIntro`。START! より前）**と、見た目（`IntroVisual`）の MeshRenderer が全部切られている間（始まりの演出でゴールが飛んでいる間）は出ている量を 0 へ（START! で出し始める。2026/11/25） |

### 時計（`GoalAbsorb.ClockOf`）

物資がオンラインで出ていれば、その物資の `NetworkManager.ServerTime`（全員で合わせたホストの時計）。そうでなければ `Time.time`。
ホストは吸い込みを始めた時刻をこの時計で決めて参加者へ送り、参加者も同じ時計で進み具合を求めるので、小さくなる速さがそろう。

### 円柱の測り方（`MeasureArea`。ゴールごとに、はじめて聞かれたときに測って覚える）

1. 試合のゴールは `SpaceJunkGoalScoreEffect.TryGetGoalArea(ゴール, 0)` で床の四角（真ん中・幅・奥行き）を求める。ロビーの練習用ゴールは、判定の箱（BoxCollider）の大きさ。
2. 半径＝四角の短い辺の半分×Fill。
3. 上の端＝ゴールの判定の上の端。
4. 足もと＝ゴールの真ん中の真上（判定の上の端）から真下へ光線を飛ばし、当たった物のうち**いちばん高い面**（輸送機なら荷台、床のゴールなら色を塗る床）。物資・プレイヤー（CharacterController）・動く Rigidbody は数えない。見つからなければ四角の底の高さ。
5. 円柱の下の端は、足もとから Depth Below Floor だけ下。

エディタでプレハブ（の置かれたシーンの物）を選ぶと、円柱が紫の線で見える（`OnDrawGizmosSelected`）。

### すり鉢の形（`CreateFunnelMesh`）

上のふちの半径 1・高さ 1・底の半径 Funnel Bottom の筒（上も下も開いている）を、周り 48・高さ 14 に分けて作る。高さ v での半径は `底＋(1−底)×v^Funnel Curve`（下ほど細く、上ほど外へ開く）。Funnel Bottom・Funnel Curve を変えたら作り直す。描くときに（渦の半径, 高さ, 渦の半径）倍にする。高さは `FunnelHeightOf`（Funnel Height と円柱の高さの小さいほう）。

### らせんの帯とかけらの形（`CreateRibbonMesh`・`CreateDebrisMesh`）

- 帯：Ribbon Count 本。帯ごとに、上の端（高さ 0.75〜1）から下の端（0.02〜0.3）へ、上から見て左回りに Ribbon Turns 周前後巻きつく細い帯。すり鉢の面より少し外（1.02〜1.14 倍）。2つ目の UV の x が回る速さ（0.12〜0.6 回転/秒）、y が帯の乱数。乱数の種は決まっているので、どのPCでも同じ形。
- かけら：Debris Count 個の、少しずついびつな三角すい（面ごとに向き＝法線を持つ）。2つ目の UV がかけらごとの乱数2つ。

### 見た目（`GoalVortex.shader`）

`_Part` で描く部品を分ける（0＝床の渦、1＝すり鉢の壁、2＝らせんの帯、3＝かけら）。**光らせない**：色は `_Color`（灰色かチームの色）×1 未満で、アルファを掛けて出す（色を足して明るくしない）。

**らせんの帯**：頂点を、帯ごとの速さで真ん中の軸のまわりに回す。濃さは、長さの両端と幅の両側で消え、長さ方向に流れる縞と細かいまだらで「かすれ」を出す。帯の乱数で、黒い帯（0.08）と灰色の帯（0.7）に分ける。

**かけら**：かけらごとに、進み具合（時刻×Debris Speed＋乱数の端数）で、すり鉢のふちから底へ、らせん（角度は進み具合の2乗で速くなる）を描いて下りる。下りるほど小さくなり、出てすぐと底に着く直前は消える。自分もくるくる回る。色は面の向きで、黒（0.04）〜暗い灰色（0.38）。

**すり鉢の壁**：形の中の位置の y が高さ v（0〜1）。角度×Arms − v×Funnel Twist − 時刻の回転×1.5 の縞で、筋が上から見て左回りに回りながら下へ流れる。濃さは下ほど濃く・縞の上ほど濃く、上のふちへ向かって消える（×Funnel Opacity×Darkness）。上のふちと縞のふちに、ごく薄くチームの色。

**床の渦**：床に寝かせた正方形の板の中に、円を描く。UV は真ん中が 0、ふちが ±1。

- 渦の腕：対数の渦巻き（角度×Arms − Twist×log(半径)×2 − 時刻の回転）。時間とともに、上から見て左回りに回りながら、模様が真ん中へ流れ込む。
- 黒さ（アルファ）：腕の上ほど・真ん中ほど濃い。ふちの近くは少し薄くして床につなぐ。
- ふちの光：外のふちにチームの色（`_Color`）。腕の間にも、ごく薄く色をのせて筋を見せる。
- 光の粒：角度で分けた筋の上を、ふちから真ん中へ流れる小さな点。
- 出てくるとき（`_Appear`）：真ん中から外へ円が広がる。
- 重ね方は `Blend One OneMinusSrcAlpha`（黒い所は下を暗くし、光る所は色を足す）。

## 変数の置き場所

| 変数 | ファイル | 意味 |
| --- | --- | --- |
| `style`（`Style`） | `GoalEffect.cs` | 見た目の種類（渦・光の柱）。テスト・検証シーンからも切り替える |
| `absorbMotion`（`AbsorbMotion`） | `GoalEffect.cs` | 物資の動き（見た目に合わせる・渦の吸い込み・光の柱）。`UsesPillarMotion` が、いま始めると光の柱の動きになるか |
| `pillarMaterial`・`pillarHeight`・`pillarUseTeamColor`・`pillarColor`・`pillarBrightness`・`streakCount`・`sparkleCount` | `GoalEffect.cs` | 光の柱（インスペクター）。`cylinderMesh`・`streakMesh`・`sparkleMesh` は作った形 |
| `pillarMoveSeconds`・`pillarRiseSeconds`・`pillarRiseDistance`・`pillarGlowSeconds`・`pillarFlashSeconds` | `GoalEffect.cs` | 光の柱のときの物資の動きと、消える光。`flashes` は出している途中の光 |
| `pillar`・`startPosition`・`itemSize`、`All`（static） | `GoalAbsorb.cs` | 光の柱の動きか・始めの位置・物資のおおよその大きさ、吸い込み中の物資の一覧 |
| `switchStyleSeconds` | `GoalEffectTestDriver.cs` | 検証シーンで、渦と光の柱を入れ替える秒数（0 なら入れ替えない） |
| `vortexMaterial`・`fill`・`lift`・`appearSeconds`・`brightness` | `GoalEffect.cs` | 見た目（インスペクター） |
| `funnelHeight`・`funnelBottom`・`funnelCurve` | `GoalEffect.cs` | すり鉢の形（インスペクター）。`funnelMesh`・`funnelMeshShape` は作った形と、そのときの値 |
| `useTeamColor`・`neutralColor` | `GoalEffect.cs` | 明るい所の色（チームの色か、灰色か） |
| `showFloorVortex` | `GoalEffect.cs` | 床の平らな渦巻き模様を描くか（見た目だけ。判定の円柱には関係ない） |
| `ribbonCount`・`ribbonTurns`・`ribbonWidth`・`debrisCount` | `GoalEffect.cs` | らせんの帯とかけら（インスペクター）。`ribbonMesh`・`debrisMesh` は作った形 |
| `absorbSeconds`・`spinTurns`・`selfSpinDegrees`・`endScale`・`sinkDepth` | `GoalEffect.cs` | 吸い込みの動き（インスペクター。吸い込まれる係が読む） |
| `pullAcceleration`・`depthBelowFloor` | `GoalEffect.cs` | 引き寄せと、円柱の下の端（インスペクター） |
| `areas`・`appear`・`visuals`・`lastPull` | `GoalEffect.cs` | ゴールごとの円柱・出ている量・見た目の Renderer、物資ごとの最後に引き寄せた時刻 |
| `All`（static） | `GoalEffect.cs` | シーンにある渦の係（`In(シーン)` で探す） |
| `Absorbed`（static event） | `GoalEffect.cs` | ホストで吸い込み終わった瞬間（検証シーン・確認用） |
| `startTime`・`seconds`・`center`・`area`・`startRadius`・`startAngle`・`startY`・`startScale`・`startRotation` | `GoalAbsorb.cs` | 吸い込み1回ぶんの始めの状態 |
| `authority` | `GoalAbsorb.cs` | 位置を動かすPCか（ホスト・1人用） |
| `IsAbsorbing` | `HookableObject.cs` | 吸い込み中の印。**true の間は `IsVanished` も true**（フック・ボット・仕掛けが触らないように）。`Vanish` は、ほんとうに消えたか（`vanished`）だけを見る |
| `All`（static）・`BaseColor` | `SpaceJunkLobbyGoal.cs` | ロビーの練習用ゴールの一覧と、ふちの色 |

## 気をつけること

- **`IsVanished` の意味が少し広がった**（吸い込み中も true）。「もう無い物」として扱う所（フック・ボット・ベルトコンベア・列車・隕石・スポナーの数）は、それでよい。**「ほんとうに無くなったか」を知りたい所では `IsVanished && !IsAbsorbing` を使う**（例：重いデブリのイベントの一覧から外す処理 `SpaceJunkRound.ServerPruneLost`）。
- 検証シーンの係（`GoalEffectTestDriver`）は、`NetworkManager` が無ければ作ってホストとして始める（ゴールが数えるのはホストだけのため）。物資の代わりの箱は通信の部品を持たないので、吸い込みはそのPCだけで動き、得点も入らない（試合の進行役が無いため）。代わりに `Absorbed` を受けて、得点の光る演出を出す。
- 画像で確かめるときは、`DrawAt(時刻, カメラ)` で好きなカメラに描いてから撮る（`AIの申し送り.md` の 2026/11/12「`Graphics.RenderMesh` で描くもの」）。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 新規作成 |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | すり鉢（立体の渦）の形・描き方と、吸い込みの高さをすり鉢の面に沿わせたことを書き足した |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 黒と灰色・光らせない描き方、らせんの帯、黒いかけら、チームの色の切り替え（Use Team Color）を書き足した |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 床の平らな渦を描くかの切り替え（Show Floor Vortex。いまはオフ）を書き足した |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 名前を変えた（このファイルも `SpaceJunkGoalVortex.md` から `GoalEffect.md` に）。見た目の種類のプルダウンと光の柱、光の柱のときの物資の動きを書き足した |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 光の柱のときの物資の動き（真ん中の上へ運ぶ → ゆっくり上げる → 光って消える）と、消える光（`DrawFlashes`・`PlayFlash`）を書いた |
| 2026/11/17 | Claude Code（依頼：大槻 海斗） | 物資の動きのプルダウン（`absorbMotion`）と、ホストが決めた動きを参加者へ送るようにしたことを書いた |
| 2026/11/24 | Claude Code（依頼：小野田 喬） | 名前を変えたスクリプト（`FishingNetSupply` → `NetSupply` など）に合わせて書き換えた |
| 2026/11/25 | Claude Code（依頼：大槻 海斗） | START! より前（`IsIntro`）は演出を出さないようにしたことを書いた |
