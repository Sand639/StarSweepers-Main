# TeamScoreBarUI（チームの得点バー）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/TeamScoreBarUI.cs`（本体）・`Editor/TeamScoreBarUISetup.cs`（プレハブ・検証シーンを作り、ステージに置くメニュー） |
| 付いている場所 | `Prefabs/UI/TeamScoreBarUI.prefab` の一番上（Canvas ごとのプレハブ）。`TeamScoreFlameEffect` と一緒に付いている。ステージ8つにプレハブのまま置いてある |
| 関係するドキュメント | `機能ドキュメント/チームの得点バー.md`・`スクリプトの説明/Game/UI/TeamScoreFlameEffect.md` |

## 何をするスクリプトか

画面の上の真ん中に、**得点の比率で幅が変わるチームの帯**を並べ、真ん中の丸に**残り時間**を出す。
**表示するだけ**で、得点を数える・勝敗を決める・通信することはしない。

## 流れ

毎フレームの `Update` で、次の順に動く。

1. `TryReadValues` … 出す値を読む
   - `SpaceJunkRound.Current` があれば、`ScoreOf(チーム)`・`RemainingSeconds`・`SpaceJunkSession.Current.TeamCount` を読む。**得点制（Rule = Score）のときだけ**出す
   - 無くて Test Mode がオンなら、インスペクターの「テスト用」の値（時間が減り、Test Auto Score なら一定の間隔で点が入る）
   - どちらでもなければ、子の `Content` を隠して終わり
2. `OccupiedScreenHeight` を測る（時間の丸の下の端まで、画面の上から何ピクセルか）
3. `NotifyScoreChanges` … 前のフレームから得点が変わったチームがあれば `ScoreChanged(チーム, 新しい得点, 増えた量)` を呼ぶ
4. `UpdateShares` … 帯の幅の目標を出し、表示中の幅を近づける
5. `ApplyLayout` … 帯（`Segment0`〜`3`）の `anchorMin.x`・`anchorMax.x` を左から順に入れ、色と得点の数字を合わせる
6. `ApplyTime` … 残り時間を「分:秒」で出す（切り上げ。Warn Seconds を切ったら警告の色）

### 幅の決め方

- 目標の幅 = `Min Share + (1 - Min Share × チーム数) × 得点 / 合計`。全チーム 0 点なら等分
- 表示中の幅は `1 - exp(-Smooth Speed × 経過時間)` の割合で目標へ近づける（フレームの速さに関係なく同じ速さ）。`unscaledDeltaTime` なのでポーズ中も動く
- Reverse Order がオンなら、チーム番号の大きい順に左から並べる（2チームなら 左＝赤・右＝青）

### 出し始めの1フレーム（snap）

出し始めとチーム数が変わったときは、幅をすぐ目標に合わせ、**`ScoreChanged` は呼ばずに得点を覚えるだけ**にする。
シーンを読み込んだ直後・途中から入った人の画面で、いきなり「得点が入った」演出が出ないようにするため。

## ほかのスクリプトとのつながり

| 相手 | つながり |
| --- | --- |
| `SpaceJunkRound` / `SpaceJunkSession` / `SpaceJunkTeams` | 値を読むだけ（得点は NetworkList、残り時間はサーバーの時刻から計算、チーム数は NetworkVariable なので、**どのPCでも同じ値**） |
| `TeamScoreFlameEffect` | `ScoreChanged` を受けて炎を燃え上がらせる。`SlotOfTeam`・`SegmentOfSlot`・`ScoreTextOfSlot` で帯と数字を探す |
| `SpaceJunkMatchUI`（仮の画面） | `OccupiedScreenHeight` が 0 より大きい間、残り時間を出さず、見出し・イベントの一行・左のチームの行をバーの下へずらす。0 のときは前と同じ位置 |

## メニュー（`TeamScoreBarUISetup`）

| メニュー | すること |
| --- | --- |
| `Tools > StarSweepers > チーム得点バーの検証シーンを作る` | プレハブを作り直し、`Scenes/Test/TeamScoreBarUITest.unity` を作り直す（Test Mode オン） |
| `Tools > StarSweepers > チーム得点バーのプレハブを更新` | プレハブだけ作り直す |
| `Tools > StarSweepers > チーム得点バーをステージに置く` | `StageScenePaths` のステージを順に開き、まだ無ければプレハブのまま置いて保存する。**ステージのほかの物には触らない**。何度実行してもよい |

- プレハブは毎回組み立て直すので、プレハブを手で直した部分は消える
- マテリアル（`TeamScoreBarFire.mat`・`TeamScoreNumber.mat`）と丸の画像は、**無いときだけ作る**（インスペクターで調整した値を残すため）。ただし炎のマテリアルの `_BarRatio` は帯の高さで決まる値なので、毎回入れ直す
- 帯の Image は、帯の高さより上へ `FireRise`（44）だけ高くしてある（炎の舌が立ちのぼる所）。得点の数字は、その 44 を除いた帯の高さの中に置く

## 気をつけること

- `ApplyLayout` は帯の**横の余白だけ** 0 にする。縦の余白（炎の舌の所）を 0 にすると、炎がバーの中に押し込まれる
- 数字の大きさを変えるとき（`TeamScoreFlameEffect`）は `fontSize` を使う。`localScale` だと、帯いっぱいに広がった文字の枠ごと大きくなり、数字がずれる
- 数字の文字のマテリアルは、TextMeshPro の Mobile のシェーダーなので **`OUTLINE_ON` が入っていないと縁取りが出ない**
- `OccupiedScreenHeight` は static。バーが消えたとき（`OnDisable`・表示しないとき）は 0 に戻す

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/11 | Claude Code（依頼：ソウホイチョン） | 新規作成（旧リポジトリから移したときに、コードの長いコメントをここへ移した） |
