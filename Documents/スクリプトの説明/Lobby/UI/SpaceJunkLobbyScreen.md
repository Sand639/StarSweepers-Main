# SpaceJunkLobbyScreen（ロビーの画面）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Lobby/UI/` の `SpaceJunkLobbyScreen.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

**ロビーの画面**（ゲーム設定・参加者とマップの一覧・パスワード・ゲームを始める・左上のふだんの表示）。画面そのものはプレハブ `Resources/LobbyScreen.prefab` で、部品に付いた印を頼りに動かす。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 中身 |
| --- | --- |
| `SpaceJunkLobbyScreen.cs` | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkLobbyScreen.Hud.cs` | ふだんの表示（左上） |
| `SpaceJunkLobbyScreen.NumberInputs.cs` | 数字の入力欄 |
| `SpaceJunkLobbyScreen.Password.cs` | パスワード（ホストが変える） |
| `SpaceJunkLobbyScreen.Settings.cs` | ゲーム設定（ルール・プレイヤー・マップの一覧） |
| `SpaceJunkLobbyScreen.StartGame.cs` | ゲームを始める |

## 変数の置き場所

**変数・定数は、すべて入口のファイル（`SpaceJunkLobbyScreen.cs`）にある。** 分けたファイルには関数だけが入っている。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkLobbyScreen.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- 変数がどのファイルにあるかは、上の「変数の置き場所」を見る。**新しい変数は、使う機能のファイルに置き、上の表にも足す。**
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
| 2026/11/7 | Claude Code | 「変数の置き場所」を足し、ずれやすい行数の列を表から消した（大槻さんの依頼。AIが読んで気づいた弱点の修正） |
