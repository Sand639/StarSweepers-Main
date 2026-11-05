# SpaceJunkRound（ラウンドの係とラウンドイベント）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/Match/` の `SpaceJunkRound.cs` と、分けたファイル（下の表） |

## 何をするスクリプトか

**1ラウンドの進行役**（通信の部品）。始まりの演出の時刻、点数と集めた素材、決着、ゴールの持ち主を持つ。ラウンドの途中で起きるイベント（デブリセット・期間限定高価値デブリ・重いデブリ）も同じクラスで、`SpaceJunkRoundEvents*.cs` に分けて書いてある。

## ファイルの分け方（2026/11/6 から）

長かったので、**1つのクラスのまま、ファイルだけを機能ごとに分けた**（C# の partial という書き方）。
どのファイルも同じクラスの一部なので、ほかのファイルの変数や関数をそのまま使える。プレハブ・シーン・通信の動きは、分ける前と同じ。

| ファイル | 行数 | 中身 |
| --- | ---: | --- |
| `SpaceJunkRound.cs` | 622 | **入口**（設定の値・状態・出入り・Unity から呼ばれる関数・共通の補助） |
| `SpaceJunkRound.Collect.cs` | 102 | 素材を受け取る（ホストだけ） |
| `SpaceJunkRound.Finish.cs` | 88 | 決着（ホストだけ） |
| `SpaceJunkRound.GoalOwners.cs` | 97 | ゴールの持ち主と、操作の止め方 |
| `SpaceJunkRound.IntroSettings.cs` | 120 | 演出の設定を演出の部品へ渡す窓口 |
| `SpaceJunkRoundEvents.cs` | 370 | ラウンドイベントの全体（イベントを選ぶ・始める・終わらせる。ホストだけ） |
| `SpaceJunkRoundEvents.DebrisSet.cs` | 227 | ラウンドイベント：デブリセット |
| `SpaceJunkRoundEvents.Heavy.cs` | 276 | ラウンドイベント：特殊デブリ（重いデブリ）と、その見た目 |
| `SpaceJunkRoundEvents.HighValue.cs` | 76 | ラウンドイベント：期間限定高価値デブリ |

もともと `SpaceJunkRound.cs` と `SpaceJunkRoundEvents.cs` の2つに分かれていた（同じクラス）。

## 気をつけること

- **新しい処理は、その機能のファイルに書く。** どれにも当てはまらなければ、入口のファイル（`SpaceJunkRound.cs`）に書く。
- Unity から呼ばれる関数（`Awake`・`Update` など）は、入口のファイルにまとめてある。
- インスペクターに出る値は、ほとんど入口のファイルにある。
- クラスの説明（`///` のコメント）は、入口のファイルの先頭にある。

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/6 | Claude Code | 新規作成（大槻さんの依頼）。長いスクリプトを機能ごとのファイルに分けたので、分け方を書いた |
