# TitleScreen（タイトル画面）

|項目|内容|
|-|-|
|スクリプト|`StarSweepers/Assets/Scripts/TitleScene/UI/TitleScreen.cs`|
|付いている場所|`Assets/Resources/TitleScreen.prefab`（`Scenes/MainLoop/TitleScene.unity` に置いてある）|
|遊ぶ人から見た説明|`機能ドキュメント/タイトル画面.md`|
|関係するスクリプト|印：`TitleMenuButton`・`TitlePart`・`TitleInput`・`TitleSettingRow`（同じ `TitleScene/UI/` フォルダ）／タイトルとロビーの行き来：`TitleSceneFlow`／つなぐ係：`InternetConnection`・`LanRoomDiscovery`・`SteamFriendsService`|

## 何をするスクリプトか

ゲームを起動して最初に出るタイトル画面の、**全部の画面（メイン・マルチプレイ・部屋を探す・部屋を作る・設定）を1つで動かす。**
画面の部品はプレハブに置いてあり、このスクリプトは部品に付いた「印」を頼りに、ボタンや文字を動かす。
部屋を作る・入る処理そのものは `InternetConnection`（インターネット）と NetworkManager（LAN）に頼み、つながったら `TitleSceneFlow` がロビーへ移す。

## 画面と行き来

```
メイン ─┬─ マルチプレイ ─┬─ 部屋を探す
        │               └─ 部屋を作る
        └─ 設定
```

|画面|コードの名前（`Page`）|プレハブの印（`TitlePartRole`）|できること|
|-|-|-|-|
|メイン|`Main`|`PageMain`|マルチプレイ／プラクティス／設定／ゲームを終了。右下にバージョン|
|マルチプレイ|`MultiMenu`|`PageMultiMenu`|クイックマッチ／部屋を探す／部屋を作る|
|部屋を探す|`FindRoom`|`PageMulti`（名前は昔のまま）|部屋番号で検索、部屋の一覧（LAN の部屋はいつも一番上）、■パブリック ■フレンド ■パスワード不要|
|部屋を作る|`Create`|`PageCreate`|最大人数（2〜8）、サーバーの名前、パスワード、□パブリック □プライベート □LAN|
|設定|`Settings`|`PageSettings`|サウンド／グラフィック／ゲーム全般のタブ|

* マルチプレイ・部屋を探す・部屋を作るは、**右から出てくるタブレットの中**に出し、後ろにメインを出したままにする（`IsTabletPage`）。
* **戻る**（Esc／コントローラーの B）は、上の木を1つ上へ戻る（`GoBack`）。参加の確認の窓が開いていれば、窓を閉じるだけ。

## 流れ

1. **出たとき**（`Awake` / `OnEnable`）

   * 1回だけ `Initialize`：日本語フォントに差し替え → 印の付いた部品を集める（`CollectParts`）→ ボタン・入力欄・設定の行に処理をつなぐ
   * メインの画面を出す。ホストに切られて戻ってきたなら、その理由を取っておく（`TakeDisconnectReason`）
2. **毎フレーム**（`Update`）

   * Steam から頼まれた参加を受ける（`JoinFromSteamRequest`）
   * つないでいる途中の表示を出し入れする（`UpdateBusyOverlay`）
   * 部屋を探す画面なら、部屋の一覧を取り直す（`UpdateJoinableList`）
   * タブレットを動かす・操作の案内を出し分ける
   * 戻るが押されたら `GoBack`
3. **ボタンを押したとき**：どのボタンも `Do` に来て、Action ごとの処理に分かれる。
例外：一覧のカードは `AddJoinableCard`、つないでいる途中の表示のボタンは `ShowBusyOverlay`、設定の「＜」「＞」は `WireSettings` でつなぐ。
4. **つながったあと**：`TitleSceneFlow` がロビーへ移し、この画面はシーンごと消える。

## つなぎ方ごとの入口

|やること|関数|中でしていること|
|-|-|-|
|プラクティス|`StartSolo`|このPCの中だけ（127.0.0.1）で待つホストになる|
|部屋を作る（LAN）|`CreateServer` → `StartLanHost`|どこからでも受け付けるホストになり、`LanRoomDiscovery.StartAdvertising` で部屋を知らせる|
|部屋を作る（インターネット）|`CreateServer`|`InternetConnection.HostGame` に名前・人数・公開のしかた・パスワードを渡す|
|クイックマッチ|`QuickPlay` → `QuickPlayAsync`|入れる公開の部屋に**人の多い順**に入ってみる。どこにも入れなければ部屋を作る|
|一覧から入る|`AddLanEntries`・`AddPublicEntries`・`AddFriendEntries`|LAN はすぐつなぐ。公開・フレンドは参加の確認の窓（`OpenJoinDialog`）を出す|
|番号で入る|`PrivateCodeEntry`|まずパスワードなしで入ってみて、パスワードで断られたら窓をパスワードを聞く形に変える|
|Steam の「ゲームに参加」|`JoinFromSteamRequest`|Steam から届いた番号の部屋に入る|

## 部屋の一覧の作り方

* 部屋を探す画面を開いている間、毎フレーム `UpdateJoinableList` を呼ぶ。
* 公開の部屋は5秒ごと（取れなかったら15秒後）、フレンドの部屋は10秒ごとに取り直す。短くすると、インターネットの係に頼みすぎて断られる。
* 一覧の中身を1本の文字（`joinableSignature`）にして、**変わったときだけカードを作り直す**。毎フレーム作り直すと、コントローラーで選んでいたカードが外れるため。
* 並び順は LAN → 公開 → フレンド（`CollectJoinable`）。検索欄に文字があれば、番号がその文字で始まる部屋 → 途中に入っている部屋の順に絞る。
* 番号を6文字すべて入れて見つからなければ、「この番号で入ってみる」行を出す（プライベートの部屋は一覧に出ないため）。
* ■パスワード不要 なら、パスワードのかかった部屋を出さない。
* 検索のチェックは PlayerPrefs（`StarSweepers.SearchPublic` など）に保存する。

## つないでいる途中の表示（`ChooseBusyContent`）

上の条件ほど優先する。

1. つながった → 「ロビーに移動しています…」
2. インターネットの係が作業中（一覧を取っている間は除く）→ その説明
3. クイックマッチで次の部屋を試す間 → 「ほかの部屋を探しています…」
4. 参加の確認の窓から入って失敗 → 表示は出さず、**窓の中**に理由を出す
5. それ以外の失敗 → 理由と「戻る」
6. ホストに切られて戻ってきた → 理由と「OK」
7. LAN でつなぎに行っている途中 → 説明と「やめる」

## 設定

* 音量・マウス感度・フレームレート上限・垂直同期・画質・操作タイプは、変えたらすぐ `GameSettings` に入れる（すぐ効き、保存もされる）。
* **解像度と画面モードは「適用」を押すまで効かない**（`ApplyScreen`）。タブを切り替えると、選びかけの値は今の画面の値に戻る。

## 気をつけること

* **部品は印で探している。** 名前・並び順・入れ物を変えても動くが、印（`TitlePart` の Role など）を外すと、その部品は動かなくなる（エラーにはならず、何もしない）。
* **印の種類（`TitleButtonAction` などの enum）は、プレハブに番号で保存されている。** 値を消したり、番号を変えたりしない。足すときは空いている番号を使う。
* `await` のあとは `this == null` を確かめる。待っている間にロビーへ移って、画面が消えていることがある。
* インターネットの係（`InternetConnection`）は1度に1つのことしかできない。一覧を取っている間（`searching`）は、クイックマッチは待ち、参加は「少し待って」と出す。
* `OpenSessionsFullestFirst` は、クイックマッチが試す順番の自動テスト（旧リポジトリの `QuickPlayOrderTests`）から呼ぶため public にしてある。

### 使われなくなった印（2026/11/6 時点）

次の値はプレハブで使われておらず、このスクリプトも処理しない。enum には番号を守るために残っている。

* `TitleButtonAction`：OpenFind・PracticeSolo・UseInternet・UseLan・TogglePrivate・OpenFindPrivate／Public／Lan／Friends・JoinPrivate・JoinLan・SearchPublic・JoinFriendServer・RefreshFriends・Start・ShowMulti・OpenCreateRoom・CreateRoomInternet・CreateRoomLan・CloseWindow・JoinRoom・ConfirmJoinPassword
* `TitleInputKind`：PlayerName・LanAddress
* `TitlePartRole`：昔の画面（PageFind 系・PageStart・PageMode）と、昔の一覧・窓の部品（PlayerNameText・PublicList 系・FriendList 系・RoomList 系・LanList 系 など）
* `JoinListedServer`（一覧のカードのボタン）はプレハブにあるが、`Do` では処理しない（カードを作るときに直接つなぐ）

プレハブを作るツール（`UI/Editor/TitleScreenPrefabBuilder.cs`）は、古い版のプレハブを今の形へ組み替える処理を多く含んでいて、まだ整理していない。

## 変更ログ

|日付|変更者|内容|
|-|-|-|
|2026/11/6|Claude Code|新規作成（大槻さんの依頼）。`TitleScreen.cs` の古い仕様の処理を消し、コードのコメントを1行にしたので、詳しい説明をここに移した|
|2026/11/6|Claude Code|スクリプトのフォルダ分け直し（TitleScene・Lobby・Game・Common）に合わせて、スクリプトの場所の書き込みを新しい場所に直した（対応は `実装計画/スクリプトのシーン別フォルダの対応表.md`）|
