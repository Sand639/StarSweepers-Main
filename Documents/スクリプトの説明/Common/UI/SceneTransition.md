# SceneTransition（シーンを切り替えるときの演出）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Common/UI/` の `SceneTransition.cs`（本体）・`SceneTransitionEffect.cs`（種類の一覧とひな形）・`FadeTransitionEffect.cs`・`WipeTransitionEffect.cs`（種類ごと） |
| 付いている場所 | シーンには置かない。ゲームの開始時に `Resources/SceneTransition.prefab` から自動で作られ、DontDestroyOnLoad で残る |
| 関係するドキュメント | `機能ドキュメント/シーンを切り替えるときの演出.md` |

## 何をするスクリプトか

画面を覆う → シーンを切り替える → 次のシーンで画面を開く、を行う。
本体（`SceneTransition`）が時間・シーンの読み込み待ち・通信をまとめて受け持ち、**種類ごとのクラスは見た目だけを書く**。

## 呼び方

| 関数 | 使う場面 |
| --- | --- |
| `SceneTransition.Play(onCovered)` | 自分の画面だけ（自分だけタイトルへ戻る、など） |
| `SceneTransition.PlayForEveryone(onCovered)` | **ホストが全員のシーンを切り替えるとき**（Netcode の `NetworkManager.SceneManager.LoadScene`）。つないでいない・ホストでないときは `Play` と同じ |

- `onCovered` は**画面を覆いきったときに呼ばれる**。この中でシーンを切り替える
- どちらも2つ目の引数で種類を指定できる（`SceneTransition.Play(Load, SceneTransitionType.Wipe)`）。省くとプレハブのプルダウンの種類
- 演出の途中に呼ぶと何もせず `false` を返す（`onCovered` も呼ばれない）

いま呼んでいるのは `SpaceJunkSession.ServerStartMatch`（`SpaceJunkSession.Match.cs`）の1か所だけ。
ほかの場面に付けるときは、シーンを切り替えている行をこう包めばよい。

| 場面 | 今の行（`SpaceJunkSession.Match.cs`） | 包んだ形 |
| --- | --- | --- |
| 次のラウンドへ | `LoadNextMap();`（`NextRoundAfterDelay`） | `SceneTransition.PlayForEveryone(LoadNextMap);` |
| 試合が終わってロビーへ | `LoadScene(lobbySceneName);`（`ReturnToLobbyAfterDelay`） | `SceneTransition.PlayForEveryone(() => LoadScene(lobbySceneName));` |

ただし、マップの読み込みに失敗したとき（`LoadNextMap` の中の `AbortToLobby`）のように、`onCovered` の戻り値を使う処理は、包むと戻り値を受け取れなくなる。いまの `LoadNextMap` は戻り値が無いので問題ない。

## 流れ

1. `Play`／`PlayForEveryone` が呼ばれる。`PlayForEveryone` なら、ホスト以外の全員へ名前付きのメッセージ `StarSweepers.SceneTransition.Cover`（中身は種類の番号）を送る
2. `SceneTransitionEffect.Create(種類)` で演出を作り、`Build` で部品（画像）を作る
3. 覆い具合を 0→1 に動かす（Cover Seconds）。毎フレーム `Apply(覆い具合, false)`
4. `onCovered` を呼ぶ（参加者の画面では何もしない。ホストがシーンを切り替えるのを待つ）
5. **次のシーンが読み込まれるまで待つ**（`SceneManager.sceneLoaded` の Single）。Max Wait Seconds を過ぎたら、読み込まれなくても開く
6. Hold Seconds 待つ
7. 覆い具合を 1→0 に動かす（Reveal Seconds）。毎フレーム `Apply(覆い具合, true)`
8. 画面を隠し、部品を消す

時間は `Time.unscaledDeltaTime` で数える（ポーズで時間が止まっても進む）。1フレームの長さは 1/30 秒までに抑えるので、シーンの読み込みで1フレームが長くなっても、演出が一気に飛ばない。

## 種類を増やす（3か所）

1. `SceneTransitionEffect.cs` の `SceneTransitionType` に1行足す（**一番下に**。プレハブには番号で保存されるので、途中に足すと選んであった種類がずれる）。`[InspectorName("…")]` がプルダウンに出る名前
2. `SceneTransitionEffect` を継いだクラスを新しいファイルに書く。書くのは2つだけ
   - `Build(root, color)`：部品を作る。`root` は画面全体を覆う枠。一色で覆う画像は `CreateFullScreenImage` で作れる
   - `Apply(amount, revealing)`：`amount`（0＝何も覆っていない、1＝全部覆った）に合わせて見た目を変える。`revealing` は開いていくとき true
3. `SceneTransitionEffect.Create` の switch に1行足す

足し忘れは自動テスト（`SceneTransitionTests.EveryType_HasItsOwnEffect`）で分かる（ほかの種類と同じ演出になっていると失敗する）。

## 気をつけること

- **画面は Screen Space Overlay の Canvas で、並び順は `short.MaxValue - 1`**（画面のポインター `UiPointer` だけはさらに手前）。覆っている間は、下の画面のボタンを押せない
- 通信の受け口は、つないだとき（`NetworkManager.IsListening`）に作り、つなぎ直したら作り直す（`ReactionRelay` と同じ作り）。ホストからのメッセージだけを受け付ける
- 参加者は、ホストのシーンの切り替えより少し遅れて覆い始める（通信の遅れの分）。覆いきる前にシーンが読み込まれたら、覆いきってから開く
- プレハブ（`Resources/SceneTransition.prefab`）が無くても初期値で動くが、自動テスト（`Prefab_IsInResources`）が失敗する
- `SpaceJunkSession.ServerStartMatch` は、**状態を「試合中」にしてから約0.5秒後にマップへ移る**ようになった（覆っている間）。その間にもう一度呼ばれても、演出が動いているので2回目のマップ移動は起きない

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/10 | Claude Code | 新規作成（大槻さんの依頼。ロビーから試合へ移るときの演出） |
