# RoundCountdownImage（カウントダウンの画像）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/RoundCountdownImage.cs`（画像を出す部品）・`StarSweepers/Assets/Scripts/Game/UI/Editor/RoundCountdownImageSetup.cs`（プレハブを作り直すメニュー） |
| 付いている場所 | `Assets/Resources/RoundCountdownImage.prefab`。シーンには置かない（始まりの演出が、文字の画面を作るときに一緒に読み込む） |
| 画像 | `Assets/Art/Sprites/CountDown/UI_CD_1.png`・`UI_CD_2.png`・`UI_CD_3.png`・`UI_CD_Start.png`（デザイナーさんの素材。元は `共有フォルダ/20_デザイン素材/10_画像素材/GAME_UI/CountDown/`） |
| 関係するドキュメント | `機能ドキュメント/ラウンドの始まりの演出.md` |

## 何をするスクリプトか

ラウンドの始まりの演出で出る「**3・2・1・START!**」を、文字の代わりに**デザイナーさんの画像**で出す。
始まりの演出は3種類（`SpaceJunkRoundIntro`・`SpaceJunkGoalFlyIntro`・`SpaceJunkLandingIntro`）あり、どれも同じ部品を使う。

## 流れ

1. 始まりの演出が文字の画面（Canvas）を作るときに、`RoundCountdownImage.Create(画面)` で `Resources/RoundCountdownImage` を読み込み、画面の子として作る
2. 演出は毎フレーム、大きな文字（マップの名前・`3`・`2`・`1`・`START!`・空）を `Show(文字)` に渡す
3. `Show` は、文字が `1`〜`3` か `START!` なら、その画像を出して true を返す。演出はそのとき大きな文字を空にする（文字と画像が重ならない）。それ以外の文字なら画像を隠して false を返し、演出は今までどおり文字で出す
4. 演出が終わると、文字の画面ごと消える

## 欄

| 欄 | 中身 |
| --- | --- |
| Number Sprites | 1・2・3 の画像（上から 1, 2, 3 の順） |
| Start Sprite | START! の画像 |
| Scale | 画像の大きさの倍率。1 で画像そのままの大きさ（1920×1080 の画面での大きさ）。実装したときの初期値（目安）は 0.6（数字の高さが約 290、START! の幅が約 650） |

位置はプレハブの一番上の RectTransform（実装したときは画面の真ん中より 60 上。前の文字と同じ所）。

## 気をつけること

- **プレハブが無いと、3・2・1・START! は今までどおり文字で出る**（警告がログに出る）。
- 画像ごとに縦横の大きさが違うので、**画像を変えるたびに画像そのままの大きさ×Scale に合わせ直している**。プレハブの RectTransform の Width・Height を手で変えても、カウントダウンが出るときに上書きされる。大きさは Scale で変える
- 画像の取り込み設定は **Texture Type を Sprite (2D and UI)** にしておくこと（そうでないとプレハブに入れられない）
- 同じ名前の画像で上書きすれば、プレハブはそのまま差し替わる。名前を変えた・増やしたときは `Tools > StarSweepers > カウントダウンの画像のプレハブを作り直す`（このメニューは位置と Scale も初期値に戻す）
- `4` 以上の数字（カウントダウンの秒数を 4 秒以上にしたとき）は画像が無いので、その数字だけ文字で出る

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/25 | Claude Code（依頼：大槻 海斗） | 新規作成（カウントダウンを画像にしたとき） |
