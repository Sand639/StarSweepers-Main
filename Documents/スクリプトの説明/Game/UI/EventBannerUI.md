# EventBannerUI（イベント開始の警告バナー）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/EventBannerUI.cs`（表示）・`StarSweepers/Assets/Scripts/Game/UI/Editor/EventBannerUISetup.cs`（プレハブ・検証シーンを作るメニュー）・`StarSweepers/Assets/Scripts/Game/UI/Editor/EventBannerUITests.cs`（自動テスト） |
| 付いている場所 | `Resources/EventBannerUI.prefab` の一番上（Canvas と同じ物）。検証シーン `Scenes/Test/EventBannerUITest.unity` にはプレハブのまま置いてある（Test Mode だけオン） |
| 関係するドキュメント | `機能ドキュメント/イベント開始の警告バナー.md`・`機能ドキュメント/宇宙ごみのラウンドイベント.md` |

## 何をするスクリプトか

ラウンドイベントが始まる Lead Seconds（5）秒前に、赤い半透明の帯（`UI_EventBanner_base`）と「緊急連絡！」を出して2回点滅させ、「緊急連絡！」を右へ送り出してイベントの見出しと説明（Event Messages）を左から入れ、帯ごと消す（全部で 4.5 秒）。**表示するだけで、何も送らない。**

## 流れ

1. `CreateOnStart`（`RuntimeInitializeOnLoadMethod`。ゲームの開始時に1回）… シーンにまだ無ければ、`Resources/EventBannerUI.prefab` から作って `DontDestroyOnLoad` にする
2. `Awake` … 2つ目なら消える。帯の元の濃さと、3つの文字（`Label`・`EventTitle`・`EventSubtitle`）の元の位置を覚え、フォント（Font が空ならパソコンの日本語フォント `UiFont.Find`）を入れる。最初は隠す
3. `Update` … 毎フレーム
   1. `ReadSecondsShown` … 出始めてからの秒数（始まる Lead Seconds 秒前が 0）と、起きるイベントの種類（始まっていれば `ActiveEvent`、まだなら `UpcomingEvent`）。ラウンドがあれば、試合中で `SecondsUntilEvent` が 0〜Lead Seconds なら「Lead Seconds − 残り」、始まったあとは「Lead Seconds ＋ `SecondsSinceEventStarted`」。どちらでもなければ -1。ラウンドが無く Test Mode がオンなら、Test Interval 秒ごとにくり返す秒数（回ごとに Event Messages の種類を順に使う）
   2. 0 以上で Fade At + Fade Seconds 秒より前なら出し、`Draw` で見た目を決める。それ以外は隠す
4. `Draw(秒数, 種類)` … 濃さ（`IsBlinkOff` の時刻は 0、Fade At からは薄くなる）を帯と文字に入れる。`TryGetEventText` でその種類の見出しと説明があれば、Switch At から Switch Seconds かけて（`EaseInOutCubic`）、「緊急連絡！」を右へ Switch Distance だけ動かし、見出しと説明を左から元の位置へ動かす。無ければ「緊急連絡！」のまま

## プレハブの作り（`EventBannerUISetup`）

```
EventBannerUI（Canvas：画面に重ねる・並び順 30、CanvasScaler：1920×1080 基準、EventBannerUI）
└ Content（表示ごと隠す）
   ├ Banner（帯の画像。横は画面いっぱい＋左右 40 ずつはみ出す・高さ 216。画面の真ん中から 280 下）
   ├ Label（「緊急連絡！」96・太字・白、薄い影。帯の真ん中より 26 上＝帯の赤い部分の真ん中に見える高さ）
   ├ EventTitle（見出し 88・太字。帯の真ん中より 47 上。最初は隠しておく）
   └ EventSubtitle（説明 30・太字。帯の真ん中より 33 下。最初は隠しておく）
```

- 文字は `Banner` の子にしない（帯とは別に横へ動かすため）
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
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | デザイン案の演出（点滅2回 → 入れ替わり → 消える）に作り替えたのに合わせて、流れとプレハブの作りを書き直した（`EventTitle`・`EventSubtitle`・`IsBlinkOff`） |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 時間割りを5秒版にした（Lead Seconds 5、全部で 4.5 秒） |
| 2026/11/26 | Claude Code（依頼：大槻 海斗） | 文字の高さを、帯の赤い部分の真ん中に見えるように上げた（帯の影とフォントの余白のぶん。`LabelOffsetY` など） |
