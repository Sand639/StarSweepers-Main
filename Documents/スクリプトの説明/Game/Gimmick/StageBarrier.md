# StageBarrier（ステージを囲むバリア）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Gimmick/StageBarrier.cs` |
| 付いている場所 | プレハブ `Prefabs/Gimmick/StageBarrier.prefab`。それを**進行役のプレハブ `Prefabs/System/SpaceJunkRound.prefab` の子**に入れてある（全ステージ）。ロビー `MainLoop/Lobby.unity` には1つ置いてある |
| 見た目 | シェーダー `Art/Shaders/BarrierRipple.shader`・マテリアル `Art/Materials/BarrierRipple.mat` |
| 一緒に使うスクリプト | `StageBarrierTestLauncher.cs`（検証シーン専用の玉投げ）、`Editor/StageBarrierSetup.cs`（メニュー） |
| 関係するドキュメント | `機能ドキュメント/ステージを囲むバリア.md` |

## 何をするスクリプトか

再生を始めたときに、そのシーンの床の外まわりを測り、**見えない壁（箱の当たり判定）を4枚**立てる。
壁に物やプレイヤーが触れ始めたら、触れた所の壁の内側の面に**波紋**を描く。

## 流れ

| いつ | 何をするか |
| --- | --- |
| `Start` | Auto Fit が ON なら `MeasureStage` で床を囲む四角を測り、Margin だけ広げる。OFF なら Center と Size。`BuildWalls` で子に `Wall_North`／`South`／`East`／`West` を作る（北と南の壁を厚みぶん長くして、角にすき間を作らない）。高さは「いちばん低い床の上面 − Depth Below」から「いちばん高い床の上面 ＋ Height」まで |
| `Start` → `CollectConveyorGaps` | Open For Conveyors が ON なら、同じシーンのベルトコンベア（`SpaceJunkConveyor`）のうち、流れる向き（`forward`）が壁に向いていて、当たり判定の端が壁から 1m 以内に届いている物を探し、その辺の「穴」の範囲（ベルトの幅＋左右 0.5m）を集める。`AddSide` が、辺を穴の所で区切って、ふつうの壁（`Wall_〜`）と穴（`Gap_〜`）を作る |
| `Update` → `LetItemsThroughGaps` | 穴を 2m 広げた箱で `Physics.OverlapBox` し、近づいた物資（Rigidbody の付いた物。プレイヤーは除く）を `Physics.IgnoreCollision` で**全部の穴と当たらないように**する。プレイヤー（CharacterController）は穴でも止まる |
| `Update` → `DetectTouches` | 壁を Touch Distance だけ広げた箱で `Physics.OverlapBox` し、触れている物を集める（物資は Rigidbody、自分のプレイヤーは CharacterController）。ほかの人のプレイヤーは `FishingNetPlayer.All` の位置と壁の距離で調べる。前のフレームに触れていなかった物だけ `Touch` で波紋を出す |
| `Update` → `DrawRipples` | 広がっている途中の波紋を、1m 四方の板として `Graphics.RenderMesh` で描く。進み具合は `MaterialPropertyBlock` の `_Progress`（0〜1） |

### 床として測る当たり判定（`IsStageCollider`）

同じシーンにあり、有効で、すり抜けない（トリガーでない）、CharacterController でない、Rigidbody が無いか Kinematic、
物資（`HookableObject`）とプレイヤーの一部でない、横と奥行きが 200m 未満、上面の高さが Floor Height Range（-4〜4m）の中。
測った当たり判定は `stageColliders` に控え、壁に触れていても波紋を出さない。

## 気をつけること

- **物理の当たりの知らせ（`OnCollisionEnter`）を使っていない。** 参加者のPCでは物資が物理で動いていない（位置だけ配られる）ことと、CharacterController では知らせが来ないため。位置で調べるので、通信を足さなくても全員のPCで同じように波紋が出る
- **ほかの人のプレイヤーは、当たり判定（CharacterController）が切れている**（自分のPCで動かしていないため）。だから `FishingNetPlayer.All` の位置で調べている。半径 0.4m・胸の高さ 0.7m の目安を使う
- **始まりの演出中（`SpaceJunkRound.Current.IsIntro`）は波紋を出さない。** 演出では、プレイヤーを乗せたゴールエリアが奥から飛んできて壁を通り抜ける（プレイヤーは当たり判定を切って位置だけ動かされる）。上の「位置で調べる」がそれを当たりと数えて、演出のたびに波紋が出ていた（2026/11/12・大槻さんの報告。ガラスマップ3で再現）。演出中は「触れている」ことだけ控えるので、演出が終わった瞬間に触れ始めたことにもならない
- **波紋はシーンに物を作らずに描く**（`Graphics.RenderMesh`）。Renderer の付いた物を作ると、全体を映すカメラ（`SpaceJunkWholeStageCamera`）がそれもステージの大きさに数えて、カメラが動いてしまうため
- 壁は**再生を始めたときに一度だけ**作る。動く床の、動いた先までは追わない
- **穴（`Gap_〜`）は、物資にとっては無いのと同じ・プレイヤーにとってはふつうの壁。** 物資を通すのは `Physics.IgnoreCollision`（物資1つずつ、近づいたときに一度だけ）。穴では、物資が通っても波紋を出さない（プレイヤーが当たったときだけ出す）
- 波紋を出す面の向きは、壁を作るときに `inwardOf` に控えている（壁が穴で細かく分かれても、向きを取り違えないため）
- 壁はふつうの当たり判定（Default レイヤー）。全レイヤーを見る判定（`~0`。素材のスポナーの空き確認、プレイヤーの光線など）にも当たる
- 囲めるのは向きをそろえた四角だけ（親が回っていても、壁は世界の東西南北に立つ。親の拡大・縮小は打ち消している）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/12 | Claude Code | 新規作成。同じ日に、始まりの演出中に波紋が出ていたのを直した（演出中は控えるだけにした） |
| 2026/11/12 | Claude Code | ベルトコンベアの所の穴（`CollectConveyorGaps`・`AddSide`・`LetItemsThroughGaps`）を足した |
