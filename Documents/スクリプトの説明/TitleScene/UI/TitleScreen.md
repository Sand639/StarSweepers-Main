# TitleScreen（タイトル画面）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/TitleScene/UI/TitleScreen.cs`（まとめ役）と、同じフォルダの係のスクリプト10本（下の表） |
| 付いている場所 | `Assets/Resources/TitleScreen.prefab`（`Scenes/MainLoop/TitleScene.unity` に置いてある）。**プレハブに付いているのは `TitleScreen` だけ**で、係はコードの中で作られる |
| 遊ぶ人から見た説明 | `機能ドキュメント/タイトル画面.md` |
| 関係するスクリプト | 印：`TitleMenuButton`・`TitlePart`・`TitleInput`・`TitleSettingRow`（同じフォルダ）／タイトルとロビーの行き来：`TitleSceneFlow`／つなぐ係：`InternetConnection`・`LanRoomDiscovery`・`SteamFriendsService` |

## 何をするスクリプトか

ゲームを起動して最初に出るタイトル画面を動かす。
画面の部品はプレハブに置いてあり、部品に付いた「印」を頼りに、ボタンや文字を動かす。
部屋を作る・入る処理そのものは `InternetConnection`（インターネット）と NetworkManager（LAN）に頼み、つながったら `TitleSceneFlow` がロビーへ移す。

## ファイルの分け方（2026/11/6 から）

もとは `TitleScreen.cs` 1本（2,500行）だったのを、**機能ごとの係**に分けた。
`TitleScreen` は画面の切り替えと、ボタンを押したときに係へ振り分けるだけ。係は Unity の部品（MonoBehaviour）ではなく、`TitleScreen` が最初に作るふつうのクラス。

| ファイル | 係 | 主な関数 |
| --- | --- | --- |
| `TitleScreen.cs` | **まとめ役**。係を作る・画面を切り替える・ボタンを係へ振り分ける・戻る | `Initialize`・`Do`・`ShowPage`・`GoBack` |
| `TitleParts.cs` | 部品（印の付いた物）を探して、文字・出し入れ・選ばれた見た目を変える補助。コントローラーで項目を選ぶ補助 | `Object`・`SetText`・`SetActive`・`SetSelectedLook`・`SelectForGamepad` |
| `TitleInputFields.cs` | 入力欄（サーバーの名前・パスワード・部屋番号）の値を持ち、欄とつなぐ。文字数の表示 | `Refresh` |
| `TitleTablet.cs` | 右から出てくるタブレットの枠と、右下の操作の案内 | `Show`・`Update` |
| `TitleCreateRoom.cs` | 部屋を作る（人数・□パブリック □プライベート □LAN・作成） | `Refresh`・`Create` |
| `TitleRoomList.cs` | 部屋を探す（LAN・公開・フレンドの部屋の一覧、部屋番号の検索、■のチェック） | `Open`・`Update` |
| `TitleJoinDialog.cs` | 参加の確認の窓（参加しますか？／パスワード） | `Open`・`Confirm`・`TryShowFailure` |
| `TitleQuickPlay.cs` | クイックマッチ（人の多い順に入り、無ければ作る） | `Begin`・`OpenSessionsFullestFirst` |
| `TitleBusyOverlay.cs` | つないでいる途中の表示（説明・失敗の理由・戻る／OK／やめる）。ホストに切られた理由 | `Update`・`TakeDisconnectReason` |
| `TitleSettingsPage.cs` | 設定（タブ・音量などの行・解像度の適用） | `Open`・`ShowTab`・`ApplyScreen` |
| `TitleConnection.cs` | つなぐ処理のまとめ（プラクティス・LAN のホストと参加・Steam から頼まれた参加・このPCのIPアドレス） | `StartPractice`・`JoinLanRoom`・`LocalAddresses` |

係どうしのつながり（矢印は「使う」）：

```
TitleScreen ─┬→ TitleCreateRoom ──┐
             ├→ TitleRoomList ────┼→ TitleJoinDialog
             ├→ TitleQuickPlay ───┤   （一覧の部屋を押すと窓を出す）
             ├→ TitleBusyOverlay ─┘   （窓から入って失敗したら、窓に理由を出させる）
             ├→ TitleSettingsPage
             └→ TitleTablet
どの係も TitleParts（部品）・TitleInputFields（入力欄）・TitleConnection（つなぐ）を使う
```

## 画面と行き来

```
メイン ─┬─ マルチプレイ ─┬─ 部屋を探す
        │               └─ 部屋を作る
        └─ 設定
```

| 画面 | コードの名前（`Page`） | プレハブの印（`TitlePartRole`） | 中身を動かす係 |
| --- | --- | --- | --- |
| メイン | `Main` | `PageMain` | `TitleScreen` |
| マルチプレイ | `MultiMenu` | `PageMultiMenu` | `TitleScreen`（クイックマッチは `TitleQuickPlay`） |
| 部屋を探す | `FindRoom` | `PageMulti`（名前は昔のまま） | `TitleRoomList` |
| 部屋を作る | `Create` | `PageCreate` | `TitleCreateRoom` |
| 設定 | `Settings` | `PageSettings` | `TitleSettingsPage` |

- マルチプレイ・部屋を探す・部屋を作るは、**右から出てくるタブレットの中**に出し、後ろにメインを出したままにする。
- **戻る**（Esc／コントローラーの B）は、上の木を1つ上へ戻る（`GoBack`）。参加の確認の窓が開いていれば、窓を閉じるだけ。

## 流れ

1. **出たとき**（`Awake` / `OnEnable`）
   - 1回だけ `Initialize`：日本語フォントに差し替え → 係を作る（このとき各係が自分の部品をつなぎ、最初は隠す物を隠す）→ ボタンを `Do` につなぐ
   - メインの画面を出す。ホストに切られて戻ってきたなら、その理由を取っておく
2. **毎フレーム**（`Update`）
   - Steam から頼まれた参加を受ける → つないでいる途中の表示を出し入れ → 部屋を探す画面なら一覧を取り直す → タブレットを動かす → 戻るが押されたら `GoBack`
3. **ボタンを押したとき**：どのボタンも `Do` に来て、Action ごとに係へ振り分ける。
   例外：一覧のカードは `TitleRoomList`、つないでいる途中の表示のボタンは `TitleBusyOverlay`、設定の「＜」「＞」とスライダーは `TitleSettingsPage` が自分でつなぐ。
4. **つながったあと**：`TitleSceneFlow` がロビーへ移し、この画面はシーンごと消える。

## 部屋の一覧（`TitleRoomList`）

- 部屋を探す画面を開いている間、毎フレーム `Update` を呼ぶ。
- 公開の部屋は5秒ごと（取れなかったら15秒後）、フレンドの部屋は10秒ごとに取り直す。短くすると、インターネットの係に頼みすぎて断られる。
- 一覧の中身を1本の文字にして、**変わったときだけカードを作り直す**。毎フレーム作り直すと、コントローラーで選んでいたカードが外れるため。
- 並び順は LAN → 公開 → フレンド。検索欄に文字があれば、番号がその文字で始まる部屋 → 途中に入っている部屋の順に絞る。
- 番号を6文字すべて入れて見つからなければ、「この番号で入ってみる」行を出す（プライベートの部屋は一覧に出ないため）。
- ■パスワード不要 なら、パスワードのかかった部屋を出さない。検索のチェックは PlayerPrefs（`StarSweepers.SearchPublic` など）に保存する。

## つないでいる途中の表示（`TitleBusyOverlay`）

上の条件ほど優先する。

1. つながった → 「ロビーに移動しています…」
2. インターネットの係が作業中（一覧を取っている間は除く）→ その説明
3. クイックマッチで次の部屋を試す間 → 「ほかの部屋を探しています…」
4. 参加の確認の窓から入って失敗 → 表示は出さず、**窓の中**に理由を出す（`TitleJoinDialog.TryShowFailure`）
5. それ以外の失敗 → 理由と「戻る」
6. ホストに切られて戻ってきた → 理由と「OK」
7. LAN でつなぎに行っている途中 → 説明と「やめる」

## 設定（`TitleSettingsPage`）

- 音量・マウス感度・フレームレート上限・垂直同期・画質・操作タイプは、変えたらすぐ `GameSettings` に入れる（すぐ効き、保存もされる）。
- **解像度と画面モードは「適用」を押すまで効かない**（`ApplyScreen`）。タブを切り替えると、選びかけの値は今の画面の値に戻る。

## 気をつけること

- **部品は印で探している。** 名前・並び順・入れ物を変えても動くが、印（`TitlePart` の Role など）を外すと、その部品は動かなくなる（エラーにはならず、何もしない）。
- **印の種類（`TitleButtonAction` などの enum）は、プレハブに番号で保存されている。** 値を消したり、番号を変えたりしない。足すときは空いている番号を使う。
- **新しいボタンを足すとき**は、`TitleScreen.Do` に1行足して、係の関数を呼ぶ。係の中身は係のファイルに書く。
- 係は Unity の部品ではないので、`await` のあとに画面が消えたかは、作るときに渡した `owner`（`TitleScreen`）が `null` かで確かめる。
- インターネットの係（`InternetConnection`）は1度に1つのことしかできない。一覧を取っている間（`TitleRoomList.Searching`）は、クイックマッチは待ち、参加は「少し待って」と出す。
- `TitleQuickPlay.OpenSessionsFullestFirst` は、クイックマッチが試す順番の自動テスト（旧リポジトリの `QuickPlayOrderTests`）から呼ぶため public にしてある。
- ロビーの画面（`SpaceJunkLobbyScreen`）は、このPCのIPアドレスを `TitleConnection.LocalAddresses` から読んでいる。

### 使われなくなった印（2026/11/6 時点）

次の値はプレハブで使われておらず、どの係も処理しない。enum には番号を守るために残っている。

- `TitleButtonAction`：OpenFind・PracticeSolo・UseInternet・UseLan・TogglePrivate・OpenFindPrivate／Public／Lan／Friends・JoinPrivate・JoinLan・SearchPublic・JoinFriendServer・RefreshFriends・Start・ShowMulti・OpenCreateRoom・CreateRoomInternet・CreateRoomLan・CloseWindow・JoinRoom・ConfirmJoinPassword
- `TitleInputKind`：PlayerName・LanAddress
- `TitlePartRole`：昔の画面（PageFind 系・PageStart・PageMode）と、昔の一覧・窓の部品（PlayerNameText・PublicList 系・FriendList 系・RoomList 系・LanList 系 など）
- `JoinListedServer`（一覧のカードのボタン）はプレハブにあるが、`Do` では処理しない（カードを作るときに直接つなぐ）

プレハブを作るツール（`TitleScene/UI/Editor/TitleScreenPrefabBuilder.cs`）は、古い版のプレハブを今の形へ組み替える処理を多く含んでいて、まだ整理していない。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。`TitleScreen.cs` の古い仕様の処理を消し、コードのコメントを1行にしたので、詳しい説明をここに移した |
| 2026/11/6 | Claude Code | スクリプトのフォルダ分け直し（TitleScene・Lobby・Game・Common）に合わせて、スクリプトの場所の書き込みを新しい場所に直した（対応は `実装計画/スクリプトのシーン別フォルダの対応表.md`） |
| 2026/11/6 | Claude Code | `TitleScreen.cs` を機能ごとの係（10本）に分けたので、「ファイルの分け方」を足し、全体を今の形に書き直した（大槻さんの依頼。動きは変えていない） |
