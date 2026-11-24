# SpaceJunkGoalScoreEffect（ゴールの得点の演出）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Effect/SpaceJunkGoalScoreEffect.cs` |
| 付いている場所 | プレハブ `Prefabs/Effect/SpaceJunkGoalScoreEffect.prefab`。それを**進行役のプレハブ `Prefabs/System/SpaceJunkRound.prefab` の子**に入れてある（全ステージ）。検証シーン `Scenes/Test/GoalScoreEffectTest.unity` にも1つ |
| 見た目 | シェーダー `Art/Shaders/GoalScoreGlow.shader`・マテリアル `Art/Materials/GoalScoreGlow.mat` |
| 一緒に使うスクリプト | `SpaceJunkGoal.cs`（得点の知らせ `Scored` を出す）、`SpaceJunkGoalScoreEffectTestDriver.cs`（検証シーン専用）、`Game/Match/Editor/SpaceJunkGoalScoreEffectSetup.cs`（メニュー） |
| 関係するドキュメント | `機能ドキュメント/ゴールの得点の演出.md` |

## 何をするスクリプトか

ゴールに得点が入った知らせ（`SpaceJunkGoal.Scored`）を受け取り、そのゴールのまわりを**持ち主のチームの色**で 1.3 秒ほど光らせる。
光は「光の壁・光の輪・光の粒」の3つで、どれも `Graphics.RenderMesh` でシーンに物を作らずに描く。

## 流れ

| いつ | 何をするか |
| --- | --- |
| ホストで得点が数えられたとき | `SpaceJunkGoal.TryCollect` → `SpaceJunkRound.ServerCollect` が true なら `FlashClientRpc`（全員のPCで動く）→ `ShowScored`（床を Flash Color にする時刻を決め、`Scored` を出す） |
| `OnEnable` / `OnDisable` | `SpaceJunkGoal.Scored` を受け取り始める／やめる（やめるときは光っている途中の物も消す） |
| `OnGoalScored` | 知らせのゴールが**自分と同じシーン**なら `Play(goal, Time.time)` |
| `Play(goal, 開始時刻)` | `TryGetGoalArea` で足もとの真ん中・向き・囲む四角の大きさを求め、色（`SpaceJunkTeams.TeamColor(goal.OwnerTeam)`）と一緒に「光っている途中の物」の一覧に足す。同じゴールで Max Per Goal を超えたら古い物から消す。`PlayCount` を1つ増やす |
| `Update` → `DrawAt(Time.time)` | 光っている途中の物を、それぞれの進み具合（0〜1）の姿で描く。1 を過ぎた物は一覧から消す |

### 大きさの測り方（`TryGetGoalArea`）

1. ゴールの向きは**左右の回転（Y）だけ**を使う。
2. ゴールの見た目（`SpaceJunkGoal.IntroVisual`。設定が無ければ Pad Renderer）の下の MeshFilter の形の箱の8つの角を、ゴールの向きで見た座標に直して包む（始まりの演出で見た目を隠しても測れるよう、Renderer ではなくメッシュから測る）。
3. 見た目が無ければ、ゴールの判定（Collider）の箱を使う。
4. 足もとの高さは判定の箱の底。`FloorHeightAround` で、囲む四角の4辺の 0.4m 外を上（0.6m）から下（0.5m）へ光線で調べ、**動かない床の上面のいちばん高い所**があればそこに合わせる（物資＝`HookableObject`・動く Rigidbody・プレイヤー＝CharacterController は数えない。**物資は参加者のPCでは通信の仕組みで Kinematic になる**ので、Rigidbody の状態だけで見分けるとPCによって高さが変わってしまう。そのため `HookableObject` の付いた物は必ず外す。0.6m より高い壁は、光線がその中から始まるので当たらない）。
5. 囲む四角は、測った箱に Wall Outset を両側に足した大きさ。

測った値（2026/11/12）：床のゴールは 5m×4m（足もと 0m）、Hans さんの輸送機のゴールは 3.08m 四方（足もと 0m、電車マップ2は床に合わせて 0.25m）。

### 描き方（`DrawBurst`）

1つのマテリアルを、`MaterialPropertyBlock` の値を変えながら3回描く。

| 部品 | `_Part` | 形（コードで作る） | 置き方 | 時間での変化 |
| --- | --- | --- | --- | --- |
| 光の壁 | 0 | 幅・奥行き 1、高さ 0〜1 の四角い筒（上下は開いている）。UV の y が高さ | 足もとの真ん中に、囲む四角の大きさ × 壁の高さで拡大。上の端はシェーダーが Wall Flare（m）だけ外へ開く（`_Spark.w`。伸びるのに合わせて開く） | 高さは Wall Rise Seconds で 1 割 → 全部へ伸び、後半で少し縮む |
| 光の輪 | 1 | 床に寝かせた 1m 四方の板 | 足もとの 0.03m 上に、囲む四角＋Ring Spread の2倍の大きさで | 輪の半径 `_Size.w` が 0 → Ring Spread へ（はじめ速い）。明るさは (1−t)² |
| 光の粒 | 2 | Spark Count 枚の板。足もとは四角のふちの上（乱数の種は 4096 で固定）、`uv2` に粒ごとの乱数 | 縦は拡大しない（のぼる高さを m で扱うため） | シェーダーが時間差でのぼらせ、カメラのほうへ向ける |

明るさは「はじめの 0.12 秒は最大 1.8 倍」「0.3 を過ぎたらなめらかに 0 へ」。

### シェーダー（`GoalScoreGlow.shader`）

- 重ね方は `Blend One OneMinusSrcAlpha`。光の分を足しつつ、下の床を Cover の強さで少し暗くするので、明るい床の上でもチームの色が残る。`ZWrite Off`・`Cull Off`・`Queue = Transparent+10`。
- 壁：頂点を高さに比例して外へずらす（`sign(xz) × Wall Flare ÷ 大きさ × 高さ`。角は斜めに、辺はまっすぐ開く）。下ほど明るい（(1−v)^1.7）＋下のふちの白っぽい線（exp(−22v)）＋縦の筋（ふちに沿った位置の sin）と上へ流れる帯（`_Progress` で動く）。上の端の手前でなめらかに消す。
- 輪：角を丸めた四角からの距離で、半径 `_Size.w` の所に輪、ふちのすぐ外ににじみ（出始めだけ）。板のふちの手前で必ず消す。
- 粒：頂点シェーダーで、時間差（乱数 × 0.4）と寿命（0.45〜1.0）からのぼる高さを決め、外へも少し広げ、ビュー空間で板を広げてカメラに向ける。

## 気をつけること

- **Renderer を作らないこと。** 全体カメラ（`SpaceJunkWholeStageCamera`）はシーンの Renderer からステージの大きさを測るので、Renderer を作ると大きさが変わる。バリアの波紋と同じく `Graphics.RenderMesh` で描く。
- `DrawAt(時刻, カメラ)` は画像で確かめるための入口。**再生していない Unity では、`Graphics.RenderMesh` の描く予約が消えずに次の `Render()` にも残る**ので、時刻ごとに別のカメラを作り、そのカメラを渡して撮ること（`AIの申し送り.md` 2026/11/12）。
- 光っている途中の物は各PCで別々に持つ（通信しない）。時刻は各PCの `Time.time`。
- 光の粒の形（Spark Count）を再生中に変えると、形を作り直す。
- 得点を数える処理（`TryCollect`・`ServerCollect`）には触っていない。`SpaceJunkGoal` に足したのは `Scored` と `ShowScored` だけ。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/12 | Claude Code（依頼：大槻 海斗） | 新規作成 |
