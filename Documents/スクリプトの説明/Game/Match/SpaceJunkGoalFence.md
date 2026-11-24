# SpaceJunkGoalFence（ゴールの輸送機の手すり）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `Assets/Scripts/Game/Match/SpaceJunkGoalFence.cs` |
| メニュー | `Assets/Scripts/Game/Match/Editor/SpaceJunkGoalFenceSetup.cs`（`Tools > StarSweepers > ゴールの手すりの検証シーンを作る`） |
| 付いている場所 | **シーンには置いていない。** 再生時、シーンを読み込んだときに、`Fence_Front` を子に持つ物（輸送機 `SM_Transporter`）へ自動で付く。手で付けてあれば、それを使う |
| 関係するドキュメント | `機能ドキュメント/ゴールの輸送機の手すり.md` |

## 何をするスクリプトか

輸送機の4方向の手すり（`Fence_Front`・`Fence_Back`・`Fence_Left`・`Fence_Right`）のうち、**となりに床がある側の手すりを、着地したときに床下へしまう**。
残す手すりには当たり判定を入れる。手すりの材質は本体の材質（チームの色）にそろえる。

## 流れ

| いつ | 関数 | 何をするか |
| --- | --- | --- |
| ゲームの起動時 | `HookSceneLoaded` | シーンを読み込むたびに `AttachTo` が呼ばれるようにする |
| シーンを読み込んだとき | `AttachTo` | `Fence_Front` を子に持つ物に、この部品を付ける（付いていれば何もしない） |
| 付いたとき | `Awake` | 4枚の手すりを名前で探し、上げた位置・しまう長さ（形の高さ＋0.02m）・手すりの長さを覚える。当たり判定（箱）が無ければ足し、**切っておく** |
| 最初のフレーム | `Start` | 同じシーンの進行役（`SpaceJunkRound`）を探す |
| 毎フレーム | `Update` | 着地の時刻（`TryGetSecondsUntilLanding`）を過ぎたら `DecideSides` で、しまう手すりを決め、`ApplyProgress` で下げていく |
| 毎フレーム（あと） | `LateUpdate` → `SyncMaterials` | 手すりの材質を本体の材質にそろえる |

### 着地の時刻（`TryGetSecondsUntilLanding`）

始まりの演出の部品（`SpaceJunkGoalFlyIntro`・`SpaceJunkLandingIntro`）と同じ時計（`SpaceJunkRound.ServerNow`）で数える。

| 演出の種類（`SpaceJunkRound.IntroStyle`） | 着地の時刻 |
| --- | --- |
| `GoalFlyIn` | `IntroStartServerTime` ＋ `FlyInSeconds` ＋ `FlyInLandSeconds` |
| `LandingTour` | `IntroStartServerTime` ＋ `LandingSeconds` |
| `CameraTour` | `IntroStartServerTime`（輸送機は飛ばないので、演出の始まり） |
| 演出なし（`PlayStartServerTime` が 0） | すぐ |
| 進行役が無いシーン | すぐ（そこから `No Round Delay` 秒あとに動き出す） |
| 進行役はあるが、通信なしで再生した | 3 秒待っても進行役がつながらなければ、進行役が無いシーンとして扱う |

途中から入った（着地の時刻をとうに過ぎていた）ときは、動きの途中から始まる（過ぎていれば、すぐしまい終わった形になる）。
同じシーンのまま `PlayStartServerTime` が変わったら（次のラウンド）、`Raise` で上げ直して、また着地を待つ。

### 床があるか（`HasGroundBeside`・`IsGround`）

- 手すりの位置から、輸送機の中心と反対の向き（外側）へ `Probe Distance` m 進んだ所を、手すりの中央と両端寄り（手すりの長さの 8 割の所）の3か所調べる
- それぞれ、真下へ光線を飛ばし、**輸送機の床の高さの上下 `Height Tolerance` m 以内**に当たり判定の面があれば「床」
- 数えない物：輸送機自身（子も）・すり抜ける当たり判定・プレイヤーの体（CharacterController）・動く物（物理で動く Rigidbody）
- **3か所のうち2か所以上が床なら、しまう**

## 気をつけること

- **手すりの名前を変えると動かない。** モデルを作り直すときも `Fence_Front` などの名前と、「本体の子」という形は変えないこと
- 手すりの当たり判定は、**しまうかどうか決まるまで切ってある**。ボットの歩ける床の地図（`SpaceJunkBotNavMesh`）は始まりの演出のうちに作られるので、手すりは地図に入らない（前も、手すりは輸送機の形の当たり判定の中にあって、読み取り不可の設定のため地図に入っていなかった）
- 始まりの演出で飛んでいるのは、輸送機の見た目の写し（`SpaceJunkGoalCarrier.BuildCopy`）。写しを作るときの手すりの位置・材質がそのまま写るので、手すりは着地まで上げたままにし、材質は `LateUpdate`（演出の部品より先に動く）でそろえている
- 輸送機の本体の当たり判定（シーンで足した形の当たり判定と箱）は、手すりを含まなくなった（手すりはモデルの別の部品になったため）。手すりの当たり判定は、このスクリプトが足す箱だけ

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/12 | Claude Code（依頼：小野田 喬） | 新規作成 |
