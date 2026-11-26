# EventBannerUI（イベント開始の警告バナー）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/EventBannerUI.cs`（表示）・`StarSweepers/Assets/Scripts/Game/UI/Editor/EventBannerUISetup.cs`（プレハブ・検証シーンを作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/EventBannerUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/EventBannerUI.prefab` の一番上（Canvas と同じ物）。検証シーン `Scenes/Test/EventBannerUITest.unity` にはプレハブのまま置いてある（Test Mode だけオン） |
| 関係するドキュメント | `機能ドキュメント/イベント開始の警告バナー.md`・`機能ドキュメント/宇宙ごみのラウンドイベント.md` |

## 何をするスクリプトか

ラウンドイベントが始まる Lead Seconds（5）秒前から、赤い半透明の帯（`UI_EventBanner_base`）と「緊急連絡！」の文字を出し、Message Seconds（1.5）秒後にイベントごとの文字（Event Messages。「デブリセット発生！」など）に切り替え、イベントが始まったら閉じる。**表示するだけで、何も送らない。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/EventBannerUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。帯の元の濃さと、文字の元の位置を覚える。文字に Message とフォント（Font が空ならパソコンの日本語フォント `UiFont.Find`）を入れる。最初は隠す
3. `Update` … 毎フレーム
   1. `ReadSecondsShown` … 出始めてからの秒数（始まる Lead Seconds 秒前が 0）。ラウンドがあれば、試合中で `SecondsUntilEvent` が 0〜Lead Seconds なら「Lead Seconds − 残り」、始まったあとは「Lead Seconds ＋ `SecondsSinceEventStarted`」。どちらでもなければ -1。あわせて、起きるイベントの種類（始まっていれば `ActiveEvent`、まだなら `UpcomingEvent`）を返す。ラウンドが無く Test Mode がオンなら、Test Interval 秒ごとにくり返す秒数
   2. 0 以上で Lead + Close 秒より前なら出し、`Draw` で見た目を決める。それ以外は隠す
4. `Draw(秒数)` … 帯の縦の大きさ（開く：3乗の減速、閉じる：3乗の加速）、帯の濃さ（開ききったあと cos で点滅）、文字（Message Seconds までは Message、そのあとは `TryGetEventText` で探したイベントの文字。無ければ Message のまま）、文字の横の位置（右から滑り込む。切り替わったら、もう一度滑り込む）と濃さ（閉じるのに合わせて消える）を、秒数と種類だけから決める

## プレハブの作り（`EventBannerUISetup`）

```
EventBannerUI（Canvas：画面に重ねる・並び順 30、CanvasScaler：1920×1080 基準、EventBannerUI）
└ Content（表示ごと隠す）
   ├ Banner（帯の画像。横は画面いっぱい＋左右 40 ずつはみ出す・高さ 216。画面の真ん中から 280 下）
   └ Label（Text「緊急連絡！」96・太字・白、薄い影。帯と同じ高さ）
```

- `Label` は `Banner` の子にしない（帯が上下に開くとき、文字までつぶれないように）
- 帯の画像が読めないときは、エラーで止まる

## 気をつけること

- **Test Mode は、Resources のプレハブではオフのまま**にする（オンだと、タイトルやロビーでも帯が出る）。自動テスト `Prefab_IsInResourcesAndWired` が確かめる
- 見た目は秒数だけで決まるので、途中から見え始めた人にも、同じ時刻の見た目が出る
- `RoundResultPlayback.IsPresenting`（ラウンドの結果を見せている間）は出さない
- 文字はフォントが再生中にしか入らないので、プレハブを開いても見えない
- 画面に重ねる UI はカメラに写らないので、画面なしの Unity で画像を撮るときは、撮る間だけ Canvas を Screen Space - Camera にする（`AIの申し送り.md`）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 新規作成 |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | イベントの5秒前から出して始まったら閉じる形（`ReadSecondsShown`・Lead Seconds）に書き直した |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 「緊急連絡！」のあとにイベントごとの文字へ切り替える（Message Seconds・Event Messages・`TryGetEventText`）ことを書いた |
