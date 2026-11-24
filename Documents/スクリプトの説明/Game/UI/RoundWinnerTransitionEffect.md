# RoundWinnerTransitionEffect（ラウンドの終わりの「勝ったチームの色」の演出）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/RoundWinnerTransitionEffect.cs` |
| 付いている場所 | どこにも置かない。トランジションの係（`SceneTransition`）が、種類 `SceneTransitionType.RoundWinner` のときに作る |
| 呼んでいる所 | `SpaceJunkSession.NextRoundAfterDelay`（`SpaceJunkSession.Match.cs`）。ラウンドの結果を見せたあと、次のマップへ移るとき |
| 関係するドキュメント | `機能ドキュメント/シーンを切り替えるときの演出.md`・`スクリプトの説明/Common/UI/SceneTransition.md` |

## 何をするスクリプトか

トランジションの種類の1つ（`SceneTransitionEffect` を継いだクラス）。**勝ったチームの色の板**が、上の得点バーのあたりから画面いっぱいに広がって「**RED win!**」を出し、次のマップでは**左上の角へ縮んで**消える。メンバーの絵コンテに合わせた**仮の演出**。

時間の数え方・シーンの読み込みを待つこと・通信で全員に出すことは、`SceneTransition` がする。このクラスは見た目だけ。

## 流れ

| 関数 | すること |
| --- | --- |
| `Build` | 勝ったチームを `SpaceJunkSession.LastRoundWinner`（全員に配られている値）から読み、その色（`SpaceJunkTeams.TeamColor`）の板と、画面の真ん中の文字（「BLUE／RED／GREEN／YELLOW win!」。引き分けは灰色で「DRAW」）を作る。試合が無いとき（検証シーン）は赤チームとして出す |
| `Apply`（覆うとき） | 板の四角を、上の真ん中の得点バーのあたり（画面の横 27〜73%・縦 90〜96%）から画面いっぱいへ広げる。文字は覆い具合 0.7 から出始める |
| `Apply`（開くとき） | 板の四角を、左上の角を残したまま縮める（左下の角が左上へ寄っていく）。文字は開き始めたらすぐ消える |

## 気をつけること

- 文字は TextMeshPro の標準のフォント（LiberationSans）。Auto Size で画面の大きさに合わせる
- 勝ったチームは `Build` のとき（覆い始めるとき）に読む。参加者の画面では、ホストからの「覆って」の通信が届いたときに読むので、そのときにはもう `LastRoundWinner` が配られている（結果の表示の約5秒のあとに覆い始めるため）
- 種類の番号は `SceneTransitionType.RoundWinner = 2`。**`SceneTransitionType` の途中に種類を足すと番号がずれる**ので、足すときは一番下に
- 「RED win!」が見えるのは、覆いきってから開き始めるまで。長くしたいときは `SceneTransition` の Hold Seconds（全部の種類に効く）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/17 | Claude Code（依頼：ソウホイチョン） | 新規作成 |
