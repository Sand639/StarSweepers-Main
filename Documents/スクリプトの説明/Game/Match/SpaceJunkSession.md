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
| `SpaceJunkSession.LobbyTest.cs` | 検証用ロビーの準備完了、乗る位置、出発時刻 |
| `SpaceJunkSession.LocalPlayers.cs` | 1台のPCで複数人 |
| `SpaceJunkSession.ResultPresentation.cs` | 赤青の結果演出の配信と全員の完了待ち |
| `SpaceJunkSession.VictoryShowcase.cs` | 試合に勝ったチームのメンバー紹介（勝利演出）の配信と全員の完了待ち |
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
| `SpaceJunkSession.VictoryShowcase.cs` | メンバー紹介の完了待ち（`victoryViewersFinished`・`victoryShowcaseId`・`localVictoryShowcase`＝このPCの演出。ホストが Enter で飛ばしたかを見る） |

逆に、**ロビーのカウントダウンの変数（`playersInTeamZones`・`lobbyCountdown…`）は入口のファイルにあるが、使うのは `SpaceJunkSession.LobbyZones.cs`**。

準備完了した席と乗り込み時刻（`testReadyPlayers`・`testBoardingTime`）は `LobbyTest.cs` にある。入口の `Update` は検証用ロビーで出発を進め、それ以外では従来のカウントダウンを進める。席の答え合わせも定期的に呼ぶ。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkSession.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- 変数がどのファイルにあるかは、上の「変数の置き場所」を見る。**新しい変数は、使う機能のファイルに置き、上の表にも足す。**
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。
- **ボットの頭は、新しい体用の `SweeperBot` だけ**（`SpaceJunkSession.Bots.cs`）。古い体用のボットは 2026/11/7 に消したので、新しい体のプレハブが見つからず古い体で出たボットは、頭が付かず動かない（警告が出る）。
- **試合を始める（`ServerStartMatch`）と、すぐにはマップへ移らない。** 状態を「試合中」にしてから `SceneTransition.PlayForEveryone(LoadNextMap)` で全員の画面を覆い、覆いきったら（約0.5秒後）`LoadNextMap` が呼ばれる（`SpaceJunkSession.Match.cs`）。演出の仕組みは `スクリプトの説明/Common/UI/SceneTransition.md`。
- **試合の勝ちが決まると、ロビーへ戻る前に2つの演出を順に待つ**（`ReturnToLobbyAfterDelay`）。①結果画面（`PresentRoundResult`）②勝ったチームのメンバー紹介（`PresentMatchVictory`。ホストが Enter で飛ばすか、25秒で打ち切り）。演出そのものは `スクリプトの説明/Game/UI/MatchVictoryShowcase.md`。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
| 2026/11/7 | Claude Code | 「変数の置き場所」を足し、ずれやすい行数の列を表から消した（大槻さんの依頼。AIが読んで気づいた弱点の修正） |
| 2026/11/7 | Claude Code | `SpaceJunkSession.Bots.cs`：古い体用のボット（`SpaceJunkBot`）を消したので、ボットの頭を付ける所を `SweeperBot` だけにした。気をつけることに書き足した |
| 2026/11/10 | Claude Code | `SpaceJunkSession.Match.cs`：試合を始めるとき、トランジション（`SceneTransition`）で全員の画面を覆ってからマップへ移るようにした。気をつけることに書き足した |
| 2026/11/17 | Codex | 検証ロビーの準備完了と出発の担当ファイル・変数を追記 |

結果演出の変数はResultPresentation.csのresultPlayback・resultViewersFinished・resultPresentationId・roundResultActive。Match.csが演出を待ち、色帯で画面を覆ってから次のStageまたはLobbyへ移る。

| 2026/11/19 | Codex | 結果演出の担当ファイル、同期と完了待ちの変数を追記 |
| 2026/11/25 | Claude Code | `SpaceJunkSession.VictoryShowcase.cs`（勝利演出のメンバー紹介）を足し、ファイルの分け方・変数の置き場所・気をつけることに書き足した |
| 2026/11/27 | Codex（依頼：ソウホイチョン） | `ServerStartMatch` と `ReturnToLobbyAfterDelay` でモニターを使う。`ShowVictoryAfterSavingResult` は勝利紹介の直前に結果の画を記録する |
