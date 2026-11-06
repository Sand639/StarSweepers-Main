# SpaceJunkSession（試合の係）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Match/` の `SpaceJunkSession.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

宇宙ごみ集めの**試合全体の進行役**（通信の部品）。チーム分けの席・チーム数やルール・マップの選び方を持ち、ロビーから試合を始め、ラウンドを順に進めて、終わったらロビーへ戻す。プレハブ `Prefabs/System/SpaceJunkSession.prefab` をホストが出す（`SpaceJunkSessionSpawner`）。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 中身 |
| --- | --- |
| `SpaceJunkSession.cs` | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkSession.Bots.cs` | デバッグ用のボット |
| `SpaceJunkSession.HostSettings.cs` | ホストが設定を変える（チーム・ルール・マップ） |
| `SpaceJunkSession.LobbyZones.cs` | ロビーのチームエリアと開始のカウントダウン |
| `SpaceJunkSession.LocalPlayers.cs` | 1台のPCで複数人 |
| `SpaceJunkSession.Match.cs` | ラウンドの進行（ホストだけ） |
| `SpaceJunkSession.SlotCheck.cs` | 席の答え合わせ（ホストだけ） |

`SpaceJunkPlayerSlot`（参加者1人ぶんの席）も、この `SpaceJunkSession.cs` の先頭に入っている。

## 変数の置き場所

**通信の値（全員へ配る値）とインスペクターに出る値は、すべて入口のファイル（`SpaceJunkSession.cs`）にある。** 次の物だけ、使う機能のファイルに置いてある。

| ファイル | 置いてある変数・定数 |
| --- | --- |
| `SpaceJunkSession.SlotCheck.cs` | 答え合わせの間隔と回数（`slotCheckTimer`・`slotMismatchCount`・`SlotCheckInterval`・`SlotMismatchRepairCount`） |
| `SpaceJunkSession.Bots.cs` | ボットの番号の始まりと数の上限（`BotSeatStart`・`MaxBots`） |
| `SpaceJunkSession.HostSettings.cs` | ホストが入れられる値の範囲（`MaxRoundsToWin`・`MinRoundSeconds`・`MaxRoundSeconds`） |

逆に、**ロビーのカウントダウンの変数（`playersInTeamZones`・`lobbyCountdown…`）は入口のファイルにあるが、使うのは `SpaceJunkSession.LobbyZones.cs`**。

入口の `Update` は、毎フレーム `LobbyZones.cs` の `UpdateLobbyStartCountdown` と、1秒ごとに `SlotCheck.cs` の `ServerCheckSlots` を呼んでいる。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkSession.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- 変数がどのファイルにあるかは、上の「変数の置き場所」を見る。**新しい変数は、使う機能のファイルに置き、上の表にも足す。**
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
| 2026/11/7 | Claude Code | 「変数の置き場所」を足し、ずれやすい行数の列を表から消した（大槻さんの依頼。AIが読んで気づいた弱点の修正） |
