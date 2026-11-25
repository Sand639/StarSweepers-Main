# MatchVictoryShowcase（勝利演出のメンバー紹介）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/MatchVictoryShowcase.cs`（演出）・`StarSweepers/Assets/Scripts/Game/UI/MatchVictoryShowcaseTester.cs`（検証シーン用） |
| 付いている場所 | どこにも置いていない。試合では `SpaceJunkSession.VictoryShowcase.cs` が `PlayNew` で、検証シーン `Scenes/Test/MatchVictoryShowcaseTest.unity` では `MatchVictoryShowcaseTester` が作る |
| 関係するドキュメント | `機能ドキュメント/勝利演出（メンバー紹介）.md`・`スクリプトの説明/Game/Match/SpaceJunkSession.md` |

## 何をするスクリプトか

勝ったチームのメンバーの体の写しを3Dの台に並べ、カメラで1人ずつ紹介してから「WIN!」で締める。**表示するだけで、何も送らない**（終わったら `onFinished` を1回呼ぶだけ）。

## 流れ

| 関数 | すること |
| --- | --- |
| `PlayNew(チーム, メンバー, 終わったとき)` | いまのシーンに新しく作って始める。シーンが切り替わると一緒に消える |
| `CollectTeam(チーム)` | シーンにいる `SweeperPlayer` から、そのチームの人を参加番号の順に集める（ボットも入る） |
| `Build` | 台・背景・明かり・カメラ・画面（名前の帯・WIN!・紙吹雪）を作り、体を写す（`CopyBody`） |
| `DrawAt(秒)` | **その時刻の見た目を全部決める。** `Update` はこれを呼ぶだけ。カメラ（`DrawCamera`）・体のジャンプ（`DrawMembers`）・名前の帯（`DrawCard`）・WIN!（`DrawWin`）・紙吹雪（`DrawConfetti`） |

時刻の区切りは `MembersEnd`（紹介が終わる）・`FinaleStart`（WIN! が出る、その0.3秒後）・`TotalSeconds`（終わり）。

## 気をつけること

- **3D は層 29 に、マップから離れた高さ 3000m に作る。** 結果画面（`RoundWinnerResultUI`）は層 30 を使っているので重ねない。明かりも層 29 だけを照らす。
- 3D はカメラで `RenderTexture` に描き、画面（並び順 30001）に貼る。**結果画面の色帯（30000）より手前、`SceneTransition`（32766）より奥**の順番を崩さないこと。崩すと、ロビーへ戻る色帯の上に演出が残る。
- 体の写しは、背の高さを Member Height にそろえ、足を台に乗せる（モデルの大きさや縮んでいる最中でもそろう）。服の色は `CharacterColorController` の値ごと写るので、塗り直さなくてよい。
- **Enter で飛ばすと** `Update` が始まりの時刻をずらして最後の画にし、すぐ `onFinished` を呼ぶ。`WasSkipped` が true になる。ホストのPCで true になると、`SpaceJunkSession.PresentMatchVictory` がほかの人を待たずに先へ進む。
- 再生していないとき（画面なしの Unity で撮るとき）も動くよう、消すのは `DestroySafe` を使う。
- 検証シーンの `MatchVictoryShowcaseTester` は、プレハブ `SweeperPlayer` から体のモデルだけを写して服を塗り、見えない所にしまってから渡す（試合でプレイヤーの体を写すのと同じ形）。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/25 | Claude Code | 新規作成（大槻さんの依頼で勝利演出を試作したため） |
| 2026/11/25 | Claude Code | Enter で飛ばす仕組み（`WasSkipped`）を書き足した |
