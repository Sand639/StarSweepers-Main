# SpaceJunkSession（試合の係）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Match/` の `SpaceJunkSession.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

宇宙ごみ集めの**試合全体の進行役**（通信の部品）。チーム分けの席・チーム数やルール・マップの選び方を持ち、ロビーから試合を始め、ラウンドを順に進めて、終わったらロビーへ戻す。プレハブ `Prefabs/System/SpaceJunkSession.prefab` をホストが出す（`SpaceJunkSessionSpawner`）。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 行数 | 中身 |
| --- | ---: | --- |
| `SpaceJunkSession.cs` | 605 | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkSession.Bots.cs` | 140 | デバッグ用のボット |
| `SpaceJunkSession.HostSettings.cs` | 156 | ホストが設定を変える（チーム・ルール・マップ） |
| `SpaceJunkSession.LobbyZones.cs` | 149 | ロビーのチームエリアと開始のカウントダウン |
| `SpaceJunkSession.LocalPlayers.cs` | 133 | 1台のPCで複数人 |
| `SpaceJunkSession.Match.cs` | 243 | ラウンドの進行（ホストだけ） |
| `SpaceJunkSession.SlotCheck.cs` | 168 | 席の答え合わせ（ホストだけ） |

`SpaceJunkPlayerSlot`（参加者1人ぶんの席）も、この `SpaceJunkSession.cs` の先頭に入っている。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkSession.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- インスペクターに出る値は、ほとんど入口のファイルにある。
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
