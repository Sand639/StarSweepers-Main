# SpaceJunkPlayerSetup（宇宙ごみ用のプレイヤーの上乗せ）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Player/` の `SpaceJunkPlayerSetup.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

**宇宙ごみ用に、プレイヤーへ上乗せする部品。** チームの色、シーンが変わったときの置き直し、出てくる場所、復活の待ち時間、ロビーでの体の見せ方、名前のタグやレーダーの用意をする。古い体と新しい体の両方のプレハブに付いている。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 中身 |
| --- | --- |
| `SpaceJunkPlayerSetup.cs` | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkPlayerSetup.LobbyReveal.cs` | ロビーで体を隠す・見せる |
| `SpaceJunkPlayerSetup.Placement.cs` | シーンが切り替わったときの置き直し |
| `SpaceJunkPlayerSetup.Respawn.cs` | 復活時間 |
| `SpaceJunkPlayerSetup.Spawn.cs` | 出てくる場所を決める |

## 変数の置き場所

**インスペクターに出る値と、ほとんどの状態は、入口のファイル（`SpaceJunkPlayerSetup.cs`）にある。** 次の物だけ、使う機能のファイルに置いてある。

| ファイル | 置いてある変数・定数 |
| --- | --- |
| `SpaceJunkPlayerSetup.Placement.cs` | 置き直しを待っている間の状態（`placementPending`・`placementWaitTime`・`placementFramesWaited`・`placedByFallback`・`fallbackRetryTimer`）と、待つ長さ（`MinPlacementFrames`・`MaxPlacementWaitSeconds`・`FallbackRetrySeconds`） |

`LobbyReveal.cs`・`Respawn.cs`・`Spawn.cs` には変数は無い。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkPlayerSetup.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- 変数がどのファイルにあるかは、上の「変数の置き場所」を見る。**新しい変数は、使う機能のファイルに置き、上の表にも足す。**
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
| 2026/11/7 | Claude Code | 「変数の置き場所」を足し、ずれやすい行数の列を表から消した（大槻さんの依頼。AIが読んで気づいた弱点の修正） |
