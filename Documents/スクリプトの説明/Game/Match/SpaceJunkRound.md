# SpaceJunkRound（ラウンドの係とラウンドイベント）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Match/` の `SpaceJunkRound.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

**1ラウンドの進行役**（通信の部品）。始まりの演出の時刻、点数と集めた素材、決着、ゴールの持ち主を持つ。ラウンドの途中で起きるイベント（デブリセット・期間限定高価値デブリ・重いデブリ）も同じクラスで、`SpaceJunkRoundEvents*.cs` に分けて書いてある。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 中身 |
| --- | --- |
| `SpaceJunkRound.cs` | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkRound.Collect.cs` | 素材を受け取る（ホストだけ） |
| `SpaceJunkRound.Finish.cs` | 決着（ホストだけ） |
| `SpaceJunkRound.GoalOwners.cs` | ゴールの持ち主と、操作の止め方 |
| `SpaceJunkRound.IntroSettings.cs` | 演出の設定を演出の部品へ渡す窓口 |
| `SpaceJunkRoundEvents.cs` | ラウンドイベントの全体（イベントを選ぶ・始める・終わらせる。ホストだけ） |
| `SpaceJunkRoundEvents.DebrisSet.cs` | ラウンドイベント：デブリセット |
| `SpaceJunkRoundEvents.Heavy.cs` | ラウンドイベント：特殊デブリ（重いデブリ）と、その見た目 |
| `SpaceJunkRoundEvents.HighValue.cs` | ラウンドイベント：期間限定高価値デブリ |

もともと `SpaceJunkRound.cs` と `SpaceJunkRoundEvents.cs` の2つに分かれていた（同じクラス）。

## 変数の置き場所

**ラウンドそのものの値（始まりの演出の設定・始まりの時刻・段階・集めた素材と点数）は、入口のファイル（`SpaceJunkRound.cs`）にある。**
**ラウンドイベントの値は、通信の値（全員へ配る値）も含めて、`SpaceJunkRoundEvents*.cs` のほうにある。**

| ファイル | 置いてある変数・定数 |
| --- | --- |
| `SpaceJunkRoundEvents.cs` | どのイベントが起きているか・始まりと終わりの時刻・画面に出す名前と説明（通信の値 `activeEvent`・`event…`）、`serverEventFinished` |
| `SpaceJunkRoundEvents.DebrisSet.cs` | デブリセットのお題・進み具合・ボーナス（通信の値 `set…`）、`serverSetSize`・`MaxSetSize` |
| `SpaceJunkRoundEvents.Heavy.cs` | 重いデブリの番号・重さ・得点（通信の値 `heavy…`・`stageHeavy…`）、`appliedHeavyIds`・`MinHeavyWeight` |
| `SpaceJunkRoundEvents.HighValue.cs` | 高価値デブリの種類とボーナス（通信の値 `highValue…`） |

`SpaceJunkRound.Collect.cs`・`Finish.cs`・`GoalOwners.cs`・`IntroSettings.cs` には変数は無い。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkRound.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- 変数がどのファイルにあるかは、上の「変数の置き場所」を見る。**新しい変数は、使う機能のファイルに置き、上の表にも足す。**
- **`SpaceJunkRoundEvents*.cs` は名前が違うが、同じ `SpaceJunkRound` クラスの一部。** `SpaceJunkRound.*.cs` で探すと出てこないので、全部のファイルを探すときは `partial class SpaceJunkRound` で検索する。
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
| 2026/11/7 | Claude Code | 「変数の置き場所」を足し、ずれやすい行数の列を表から消した（大槻さんの依頼。AIが読んで気づいた弱点の修正） |
