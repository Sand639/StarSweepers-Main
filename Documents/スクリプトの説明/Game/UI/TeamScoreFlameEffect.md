# TeamScoreFlameEffect（チームの得点バーの炎）

| 項目 | 内容 |
| --- | --- |
| スクリプト | `StarSweepers/Assets/Scripts/Game/UI/TeamScoreFlameEffect.cs`（演出）・`StarSweepers/Assets/Art/Shaders/TeamScoreBarFire.shader`（炎の見た目。`StarSweepers/UI/TeamScoreBarFire`） |
| 付いている場所 | `Prefabs/UI/TeamScoreBarUI.prefab` の一番上（`TeamScoreBarUI` と同じ物）。シェーダーは `Art/Materials/TeamScoreBarFire.mat` として、帯（`Segment0`〜`3`）の Image に付いている |
| 関係するドキュメント | `機能ドキュメント/チームの得点バー.md`・`スクリプトの説明/Game/UI/TeamScoreBarUI.md` |

## 何をするスクリプトか

チームの帯を**まるごとアニメ風の炎**にし（シェーダー）、**得点が入ったチームの帯を燃え上がらせ、得点の数字をポンと大きくする**（スクリプト）。
炎は画像を使わず、シェーダーの計算だけで作っている。

## 流れ（スクリプト）

1. `Awake` … 帯ごとにマテリアルを複製して付け直す（`_Boost` を帯ごとに変えるため）。数字の元の文字の大きさを覚える
2. `OnEnable` / `OnDisable` … `TeamScoreBarUI.ScoreChanged` につなぐ・外す
3. `OnScoreChanged` … 得点が**増えた**チームの燃え上がりと数字の拡大を 1 に戻す（減ったときは何もしない）
4. `LateUpdate`（得点バーの `Update` のあと）… 両方を Boost Seconds・Punch Seconds で 0 へ減らし、`EaseOut` で曲げて、帯のマテリアルの `_Boost` と数字の `fontSize` に入れる

## シェーダーの作り

| 部分 | 作り方 |
| --- | --- |
| 四角の使い方 | Image の四角は「帯の高さ ＋ 炎が立ちのぼるぶん」。下から `_BarRatio` までが帯、その上が炎の舌の所 |
| 横の位置 | Canvas の中のピクセル座標で計算する。帯の幅が変わっても炎の舌の太さがそろい、隣のチームの帯とも模様がつながる |
| 炎の舌 | 横に細かく・縦にゆっくり変わるノイズ（バリューノイズを3枚重ねた fbm）を上へ流し、縦長の舌にする。コントラストを強めて舌と舌の間にすき間を作る |
| 炎の濃さ | 帯の上では上へ行くほど薄く、帯の中では根元（下）ほど濃い。四角の上の端に近づくと必ず細らせる（燃え上がっても先が平らに切れない） |
| 塗り分け | 濃さのしきい値で3段（外側＝チームの色、内側ほど白に近い）。舌のふちにチームの色を暗くした線 |
| 色 | Image の Color（＝チームの色。`TeamScoreBarUI` が毎フレーム入れる） |
| 燃え上がり | `_Boost` を 0→1 にすると、舌が `_BoostHeight` ぶん高く、`_BoostBright` ぶん明るくなる |
| マスク | UI の標準シェーダー（UI/Default）と同じステンシル・切り抜き（`UNITY_UI_CLIP_RECT`）に対応 |

## 気をつけること

- 帯の色を変えたいときは、マテリアルではなく `TeamScoreBarUI`（Use Custom Colors）で変える。マテリアルの Tint は全チームに掛かる
- `_BarRatio` は手で変えない（`TeamScoreBarUISetup` がバーの大きさから入れる）
- `_Time` を使うので、`Time.timeScale = 0` の間は炎のゆれが止まる（燃え上がりの減り方は `unscaledDeltaTime` なので止まらない）

## 変更ログ

| 日付 | 変更者 | 内容 |
| --- | --- | --- |
| 2026/11/11 | Claude Code（依頼：ソウホイチョン） | 新規作成（旧リポジトリから移したときに、コードとシェーダーの長いコメントをここへ移した） |
