using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>勝ったチームと両チームの勝ち数を見せる、独立した2D結果画面。</summary>
public sealed class RoundWinnerResultUI : MonoBehaviour
{
    public enum ResultStyle { StarAward, Collision, HookThrow }

    [SerializeField] private Sprite redCharacter;
    [SerializeField] private Sprite blueCharacter;

    private static readonly Color Gold = new Color(1f, 0.78f, 0.25f);
    private readonly RectTransform[] badges = new RectTransform[2];
    private readonly TextMeshProUGUI[] scores = new TextMeshProUGUI[2];
    private readonly List<WinnerResultSymbol>[] stars =
        { new List<WinnerResultSymbol>(), new List<WinnerResultSymbol>() };
    private readonly RectTransform[] lines = new RectTransform[2];
    private readonly RectTransform[] hooks = new RectTransform[2];
    private readonly int[] totals = new int[2];
    private RectTransform root;
    private RectTransform motion;
    private WinnerResultSymbol flyingStar;
    private WinnerResultSymbol burst;
    private TextMeshProUGUI title;
    private Image background;
    private double startTime;
    private int winner;
    private ResultStyle style;
    private bool awarded;

    public bool IsVisible => root != null && root.gameObject.activeSelf;
    public bool IsFinished { get; private set; }

    /// <summary>勝ち数は今回の勝利を含む値。青=0、赤=1。完了後も表示を保つ。</summary>
    public void Show(int winnerTeam, int redWins, int blueWins, int winsNeeded, ResultStyle animation)
    {
        if (winnerTeam < 0 || winnerTeam > 1 || redWins < 0 || blueWins < 0 || winsNeeded < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(winnerTeam));
        }
        EnsureUI();
        winner = winnerTeam;
        style = animation;
        background.color = Color.Lerp(SpaceJunkTeams.TeamColor(winner), new Color(0.10f, 0.11f, 0.15f), 0.4f);
        totals[0] = blueWins;
        totals[1] = redWins;
        awarded = false;
        IsFinished = false;
        title.text = "ROUND RESULT";
        title.color = Color.white;
        for (int team = 0; team < 2; team++)
        {
            int displayed = Mathf.Max(0, totals[team] - (team == winner ? 1 : 0));
            EnsureStars(team, Mathf.Max(winsNeeded, totals[team]));
            SetScore(team, displayed);
        }
        startTime = Time.unscaledTimeAsDouble;
        root.gameObject.SetActive(true);
        Draw(0f);
    }

    public void Hide()
    {
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
        IsFinished = false;
    }

    private void EnsureUI()
    {
        if (root != null)
        {
            return;
        }
        GameObject canvasObject = new GameObject("RoundResultCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        root = canvasObject.GetComponent<RectTransform>();
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29999;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        background = Rect("Background", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
        background.color = new Color(0.10f, 0.11f, 0.15f);
        background.raycastTarget = false;
        background.rectTransform.anchorMin = Vector2.zero;
        background.rectTransform.anchorMax = Vector2.one;
        background.rectTransform.sizeDelta = Vector2.zero;

        title = Text("ResultTitle", root, new Vector2(0, 420), new Vector2(1400, 100), 62);
        motion = Rect("Animation", root, Vector2.zero, new Vector2(1920, 1080));
        for (int team = 0; team < 2; team++)
        {
            float x = TeamX(team);
            scores[team] = Text("Score", root, new Vector2(x, -330), new Vector2(520, 90), 60);
            TextMeshProUGUI teamName = Text("Team", root, new Vector2(x, 335), new Vector2(500, 65), 40);
            teamName.text = team == 1 ? "RED" : "BLUE";
            teamName.color = SpaceJunkTeams.TeamColor(team);
            WinnerResultSymbol badge = Symbol("TeamBadge", motion, new Vector2(x, -20),
                280, WinnerResultSymbol.Shape.Circle, Color.Lerp(SpaceJunkTeams.TeamColor(team), Color.white, 0.45f));
            badges[team] = badge.rectTransform;
            Sprite character = team == 1 ? redCharacter : blueCharacter;
            if (character != null)
            {
                Image image = Rect("Character", badges[team], Vector2.zero, new Vector2(240, 240)).gameObject.AddComponent<Image>();
                image.sprite = character;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI placeholder = Text("CharacterPlaceholder", badges[team], Vector2.zero, new Vector2(250, 150), 52);
                placeholder.text = team == 1 ? "RED" : "BLUE";
                placeholder.color = new Color(0.12f, 0.14f, 0.18f);
            }
            lines[team] = Rect("HookLine", motion, Vector2.zero, Vector2.one);
            Image line = lines[team].gameObject.AddComponent<Image>();
            line.color = SpaceJunkTeams.TeamColor(team);
            line.raycastTarget = false;
            hooks[team] = Symbol("Hook", motion, Vector2.zero, 55,
                WinnerResultSymbol.Shape.Hook, SpaceJunkTeams.TeamColor(team)).rectTransform;
        }
        flyingStar = Symbol("AwardStar", motion, Vector2.zero, 80, WinnerResultSymbol.Shape.Star, Gold);
        burst = Symbol("Impact", motion, Vector2.zero, 200, WinnerResultSymbol.Shape.Burst, Gold);
    }

    private void EnsureStars(int team, int count)
    {
        while (stars[team].Count < count)
        {
            stars[team].Add(Symbol("ScoreStar", root, Vector2.zero, 55, WinnerResultSymbol.Shape.Star, Gold));
        }
        float spacing = Mathf.Min(70f, 470f / Mathf.Max(1, count));
        for (int i = 0; i < stars[team].Count; i++)
        {
            stars[team][i].gameObject.SetActive(i < count);
            stars[team][i].rectTransform.anchoredPosition =
                new Vector2(TeamX(team) + (i - (count - 1) * 0.5f) * spacing, 245);
        }
    }

    private void SetScore(int team, int score)
    {
        scores[team].text = score.ToString("00");
        for (int i = 0; i < stars[team].Count; i++)
        {
            stars[team][i].color = i < score ? Gold : new Color(0.35f, 0.37f, 0.43f);
        }
    }

    private void Update()
    {
        if (IsVisible && !IsFinished)
        {
            Draw((float)(Time.unscaledTimeAsDouble - startTime));
        }
    }

    private void Draw(float time)
    {
        motion.anchoredPosition = Vector2.zero;
        for (int team = 0; team < 2; team++)
        {
            badges[team].anchoredPosition = new Vector2(TeamX(team), -20);
            badges[team].localRotation = Quaternion.identity;
            badges[team].localScale = Vector3.one * Pop(time / 0.5f);
            lines[team].gameObject.SetActive(false);
            hooks[team].gameObject.SetActive(false);
        }
        flyingStar.gameObject.SetActive(false);
        burst.gameObject.SetActive(false);

        switch (style)
        {
            case ResultStyle.StarAward: DrawStarAward(time); break;
            case ResultStyle.Collision: DrawCollision(time); break;
            case ResultStyle.HookThrow: DrawHookThrow(time); break;
        }
        float awardAt = style == ResultStyle.StarAward ? 2f : 2.7f;
        if (!awarded && time >= awardAt)
        {
            awarded = true;
            SetScore(winner, totals[winner]);
            title.text = winner == 1 ? "RED WINS!" : "BLUE WINS!";
            title.color = SpaceJunkTeams.TeamColor(winner);
        }
        if (time >= 3.6f)
        {
            IsFinished = true;
            motion.anchoredPosition = Vector2.zero;
        }
    }

    private void DrawStarAward(float time)
    {
        flyingStar.gameObject.SetActive(time >= 0.5f && time < 2f);
        Vector2 destination = new Vector2(TeamX(winner), 245);
        if (totals[winner] > 0)
        {
            destination = stars[winner][totals[winner] - 1].rectTransform.anchoredPosition;
        }
        float fall = Ease((time - 0.5f) / 0.7f);
        Vector2 from = Vector2.Lerp(new Vector2(0, 620), new Vector2(0, 160), fall);
        float fly = Ease((time - 1.2f) / 0.8f);
        flyingStar.rectTransform.anchoredPosition = Vector2.Lerp(from, destination, fly) + Vector2.up * Mathf.Sin(fly * Mathf.PI) * 140;
        flyingStar.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(120, 0, fall));
        float settle = Ease((time - 2f) / 0.8f);
        badges[winner].localScale *= Mathf.Lerp(1, 1.3f, settle);
        badges[1 - winner].localScale *= 1f - settle;
    }

    private void DrawCollision(float time)
    {
        float approach = Ease((time - 0.5f) / 0.4f);
        float launch = Mathf.Clamp01((time - 1.9f) / 0.8f);
        for (int team = 0; team < 2; team++)
        {
            float side = team == 1 ? -1 : 1;
            Vector2 position = new Vector2(Mathf.Lerp(TeamX(team), side * 130, approach), -20);
            if (team == winner)
            {
                position.x = Mathf.Lerp(position.x, 0, Ease(launch));
            }
            else
            {
                position += new Vector2(side * 1500 * launch * launch, 420 * launch);
                badges[team].localRotation = Quaternion.Euler(0, 0, -side * launch * 540);
            }
            badges[team].anchoredPosition = position;
        }
        Impact(time, 0.9f, 18f, Vector2.zero);
        Impact(time, 1.9f, 30f, new Vector2((winner == 1 ? 1 : -1) * 130, 0));
    }

    private void DrawHookThrow(float time)
    {
        float cast = Ease((time - 0.5f) / 0.45f);
        float launch = Mathf.Clamp01((time - 1.95f) / 0.75f);
        float side = winner == 1 ? 1 : -1;
        float struggle = time >= 0.95f && time < 1.95f ? Mathf.Sin(time * 55f) * 9f : 0;
        for (int team = 0; team < 2; team++)
        {
            badges[team].anchoredPosition += Vector2.right * (team == 1 ? struggle : -struggle);
        }
        badges[1 - winner].anchoredPosition += new Vector2(side * 1500 * launch * launch, Mathf.Sin(launch * Mathf.PI * 0.7f) * 520);
        badges[1 - winner].localRotation = Quaternion.Euler(0, 0, -side * launch * 620);
        badges[winner].localRotation = Quaternion.Euler(0, 0, side * Mathf.Sin(launch * Mathf.PI) * 22);
        for (int team = 0; team < 2; team++)
        {
            bool visible = time >= 0.5f && launch < 0.35f;
            lines[team].gameObject.SetActive(visible);
            hooks[team].gameObject.SetActive(visible);
            Vector2 start = badges[team].anchoredPosition + Vector2.up * (team == 1 ? 30 : -30);
            Vector2 target = badges[1 - team].anchoredPosition + Vector2.up * (team == 1 ? 30 : -30);
            Vector2 end = Vector2.Lerp(start, target, cast);
            Vector2 delta = end - start;
            lines[team].anchoredPosition = (start + end) * 0.5f;
            lines[team].sizeDelta = new Vector2(delta.magnitude, 7);
            lines[team].localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            hooks[team].anchoredPosition = end;
        }
        Impact(time, 1.95f, 28f, badges[winner].anchoredPosition);
    }

    private void Impact(float time, float at, float strength, Vector2 position)
    {
        float age = time - at;
        if (age < 0 || age > 0.32f)
        {
            return;
        }
        float remaining = 1 - age / 0.32f;
        motion.anchoredPosition = new Vector2(Mathf.Sin(age * 190), Mathf.Cos(age * 157)) * strength * remaining;
        burst.gameObject.SetActive(true);
        burst.rectTransform.anchoredPosition = position;
        burst.rectTransform.localScale = Vector3.one * (0.5f + age * 5);
        burst.color = new Color(1, 0.85f, 0.4f, remaining);
    }

    private static float TeamX(int team) => team == 1 ? -360 : 360;
    private static float Ease(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
    private static float Pop(float t)
    {
        t = Mathf.Clamp01(t);
        float u = t - 1;
        return 1 + 2.70158f * u * u * u + 1.70158f * u * u;
    }

    private static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static TextMeshProUGUI Text(string name, RectTransform parent, Vector2 position, Vector2 size, float fontSize)
    {
        TextMeshProUGUI text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static WinnerResultSymbol Symbol(string name, RectTransform parent, Vector2 position, float size,
        WinnerResultSymbol.Shape shape, Color color)
    {
        WinnerResultSymbol symbol = Rect(name, parent, position, Vector2.one * size).gameObject.AddComponent<WinnerResultSymbol>();
        symbol.Symbol = shape;
        symbol.color = color;
        symbol.raycastTarget = false;
        symbol.SetVerticesDirty();
        return symbol;
    }
}
