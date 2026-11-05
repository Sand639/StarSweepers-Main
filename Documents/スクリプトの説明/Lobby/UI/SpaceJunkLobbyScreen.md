# SpaceJunkLobbyScreen（ロビーの画面）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Lobby/UI/` の `SpaceJunkLobbyScreen.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

**ロビーの画面**（ゲーム設定・参加者とマップの一覧・パスワード・ゲームを始める・左上のふだんの表示）。画面そのものはプレハブ `Resources/LobbyScreen.prefab` で、部品に付いた印を頼りに動かす。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 行数 | 中身 |
| --- | ---: | --- |
| `SpaceJunkLobbyScreen.cs` | 465 | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkLobbyScreen.Hud.cs` | 111 | ふだんの表示（左上） |
| `SpaceJunkLobbyScreen.NumberInputs.cs` | 141 | 数字の入力欄 |
| `SpaceJunkLobbyScreen.Password.cs` | 74 | パスワード（ホストが変える） |
| `SpaceJunkLobbyScreen.Settings.cs` | 307 | ゲーム設定（ルール・プレイヤー・マップの一覧） |
| `SpaceJunkLobbyScreen.StartGame.cs` | 105 | ゲームを始める |

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkLobbyScreen.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- インスペクターに出る値は、ほとんど入口のファイルにある。
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
