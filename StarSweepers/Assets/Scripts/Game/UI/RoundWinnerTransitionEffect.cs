using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドの終わりに、勝ったチームの色が上の得点バーから画面いっぱいに広がり「RED win!」を出し、左上へ縮んで次のマップを見せる（仮の演出）。
/// 説明：Documents/スクリプトの説明/Game/UI/RoundWinnerTransitionEffect.md
/// </summary>
public class RoundWinnerTransitionEffect : SceneTransitionEffect
{
    // 広がり始める所（画面に対する割合）。上の真ん中の得点バーのあたり
    private static readonly Vector2 StartMin = new Vector2(0.27f, 0.90f);
    private static readonly Vector2 StartMax = new Vector2(0.73f, 0.96f);

    // 引き分けのときの色
    private static readonly Color DrawColor = new Color(0.45f, 0.45f, 0.48f);

    // チーム番号ごとの英語の名前（0＝青、1＝赤、2＝緑、3＝黄）
    private static readonly string[] TeamWords = { "BLUE", "RED", "GREEN", "YELLOW" };

    private RectTransform panel;
    private TextMeshProUGUI label;

    public override void Build(RectTransform root, Color color)
    {
        int winner = WinnerTeam();

        Color panelColor = winner >= 0 ? SpaceJunkTeams.TeamColor(winner) : DrawColor;
        panel = CreateFullScreenImage(root, "WinnerPanel", panelColor).rectTransform;

        // 文字は画面の真ん中に固定（色の板が縮んでも動かない）
        var labelObject = new GameObject("WinnerText", typeof(RectTransform));
        labelObject.transform.SetParent(root, false);
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = new Vector2(0.1f, 0.35f);
        labelRect.anchorMax = new Vector2(0.9f, 0.65f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = winner >= 0 ? $"{WordOf(winner)} win!" : "DRAW";
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 24f;
        label.fontSizeMax = 200f;
        label.raycastTarget = false;
    }

    public override void Apply(float amount, bool revealing)
    {
        if (revealing)
        {
            // 開くとき：左上の角へ縮んでいく
            panel.anchorMin = new Vector2(0f, 1f - amount);
            panel.anchorMax = new Vector2(amount, 1f);
        }
        else
        {
            // 覆うとき：上の得点バーのあたりから画面いっぱいへ広がる
            panel.anchorMin = Vector2.Lerp(StartMin, Vector2.zero, amount);
            panel.anchorMax = Vector2.Lerp(StartMax, Vector2.one, amount);
        }

        // 文字は覆いきる少し前から出し、開き始めたらすぐ消す
        float alpha = revealing
            ? Mathf.InverseLerp(0.85f, 1f, amount)
            : Mathf.InverseLerp(0.7f, 1f, amount);
        label.alpha = alpha;
    }

    /// <summary>このラウンドを取ったチーム（全員に配られている値）。引き分けは -1。試合の外（検証シーン）では赤チーム。</summary>
    private static int WinnerTeam()
    {
        if (SpaceJunkSession.Current != null)
        {
            return SpaceJunkSession.Current.LastRoundWinner;
        }

        return 1;
    }

    private static string WordOf(int team)
    {
        return team >= 0 && team < TeamWords.Length ? TeamWords[team] : $"TEAM {team + 1}";
    }
}
