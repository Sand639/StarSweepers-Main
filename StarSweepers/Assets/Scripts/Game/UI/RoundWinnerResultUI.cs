using System;
using System.Collections.Generic;
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
    private readonly List<WinnerResultSymbol>[] stars =
        { new List<WinnerResultSymbol>(), new List<WinnerResultSymbol>() };
    private readonly RectTransform[] hooks = new RectTransform[2];
    private readonly int[] totals = new int[2];
    private RectTransform root;
    private RectTransform motion;
    private WinnerResultSymbol flyingStar;
    private WinnerResultSymbol burst;
    private Image background;
    private double startTime;
    private int winner;
    private ResultStyle style;
    private bool awarded;
    private Camera resultCamera;
    private Vector3 cameraHome;
    private readonly LiquidBeam[] beams = new LiquidBeam[2];
    private Rigidbody thrownBody;
    private bool released;
    private const int ResultLayer = 30;
    private static readonly Vector3 ResultOrigin = new Vector3(0, 0, -1000);

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
        released = false;
        thrownBody.isKinematic = true;
        foreach (LiquidBeam beam in beams) beam.gameObject.SetActive(false);
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
            if (resultCamera != null) resultCamera.gameObject.SetActive(false);
            if (thrownBody != null) thrownBody.isKinematic = true;
            foreach (LiquidBeam beam in beams)
            {
                if (beam != null) beam.gameObject.SetActive(false);
            }
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
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 0;
        root.sizeDelta = new Vector2(1920, 1080);
        root.position = ResultOrigin;
        root.localScale = Vector3.one * 0.01f;
        GameObject cameraObject = new GameObject("ResultCamera");
        cameraObject.transform.SetParent(transform, false);
        resultCamera = cameraObject.AddComponent<Camera>();
        resultCamera.orthographic = true;
        resultCamera.orthographicSize = Mathf.Max(5.4f, 9.6f / Mathf.Max(0.1f, (float)Screen.width / Screen.height));
        resultCamera.clearFlags = CameraClearFlags.SolidColor;
        resultCamera.backgroundColor = new Color(0.10f, 0.11f, 0.15f);
        resultCamera.cullingMask = 1 << ResultLayer;
        resultCamera.depth = 100;
        resultCamera.farClipPlane = 50;
        cameraHome = ResultOrigin + Vector3.back * 20;
        cameraObject.transform.position = cameraHome;
        canvas.worldCamera = resultCamera;
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

        motion = Rect("Animation", root, Vector2.zero, new Vector2(1920, 1080));
        for (int team = 0; team < 2; team++)
        {
            float x = TeamX(team);
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
                BuildRobot(badges[team], team);
            }
            hooks[team] = Symbol("Hook", motion, Vector2.zero, 55,
                WinnerResultSymbol.Shape.Hook, SpaceJunkTeams.TeamColor(team)).rectTransform;
        }
        flyingStar = Symbol("AwardStar", motion, Vector2.zero, 80, WinnerResultSymbol.Shape.Star, Gold);
        burst = Symbol("Impact", motion, Vector2.zero, 200, WinnerResultSymbol.Shape.Burst, Gold);
        SetLayer(root);
        GameObject bodyObject = new GameObject("ThrownResultBody");
        bodyObject.transform.SetParent(transform, false);
        thrownBody = bodyObject.AddComponent<Rigidbody>();
        thrownBody.isKinematic = true;
        thrownBody.constraints = RigidbodyConstraints.FreezePositionZ;
        for (int team = 0; team < 2; team++)
        {
            GameObject beamObject = new GameObject("ResultLiquidBeam");
            beamObject.transform.SetParent(transform, false);
            beams[team] = beamObject.AddComponent<LiquidBeam>();
            beams[team].UseHeadLight = false;
            beams[team].SetColor(SpaceJunkTeams.TeamColor(team));
            SetLayer(beamObject.transform);
            beamObject.SetActive(false);
        }
    }

    private void EnsureStars(int team, int count)
    {
        while (stars[team].Count < count)
        {
            stars[team].Add(Symbol("ScoreStar", root, Vector2.zero, 55, WinnerResultSymbol.Shape.Star, Gold));
            SetLayer(stars[team][stars[team].Count - 1].transform);
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
        resultCamera.transform.position = cameraHome;
        resultCamera.gameObject.SetActive(true);
        for (int team = 0; team < 2; team++)
        {
            badges[team].anchoredPosition = new Vector2(TeamX(team), -20);
            badges[team].localRotation = Quaternion.identity;
            badges[team].localScale = Vector3.one * Pop(time / 0.5f);
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
        float awardAt = style == ResultStyle.StarAward ? 2f : 4.5f;
        if (!awarded && time >= awardAt)
        {
            awarded = true;
            SetScore(winner, totals[winner]);
        }
        if (time >= (style == ResultStyle.HookThrow ? 5.2f : 4.8f))
        {
            IsFinished = true;
            motion.anchoredPosition = Vector2.zero;
            resultCamera.transform.position = cameraHome;
            thrownBody.isKinematic = true;
            foreach (LiquidBeam beam in beams) beam.gameObject.SetActive(false);
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
        float fall = Ease((time - 0.5f) / 1.5f);
        float aboveScreen = resultCamera.orthographicSize * 100f + 100f;
        Vector2 from = new Vector2(0, aboveScreen);
        flyingStar.rectTransform.anchoredPosition = Vector2.Lerp(from, destination, fall);
        flyingStar.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(120, 0, fall));
        float settle = Ease((time - 2f) / 0.8f);
        badges[winner].localScale *= Mathf.Lerp(1, 1.3f, settle);
        badges[1 - winner].localScale *= 1f - settle;
    }

    private void DrawCollision(float time)
    {
        float approach = Ease((time - 0.5f) / 0.4f);
        float launch = Mathf.Clamp01((time - 1.9f) / 0.8f);
        bool pushing = time >= 0.9f && time < 1.9f;
        float pulse = pushing ? Mathf.Sin((time - 0.9f) * Mathf.PI * 12f) : 0f;
        float contestShift = pushing ? Mathf.Sin((time - 0.9f) * Mathf.PI * 4f) * 18f : 0f;
        for (int team = 0; team < 2; team++)
        {
            float side = team == 1 ? -1 : 1;
            Vector2 position = new Vector2(Mathf.Lerp(TeamX(team), side * 130, approach), -20);
            if (pushing)
            {
                position.x += side * pulse * 12f + contestShift;
                position.y += Mathf.Abs(pulse) * 7f;
                badges[team].localRotation = Quaternion.Euler(0, 0, side * pulse * 6f);
                badges[team].localScale = new Vector3(1f - pulse * 0.055f, 1f + pulse * 0.04f, 1f);
            }
            if (team == winner)
            {
                position = Vector2.Lerp(position, Vector2.zero, Ease(launch));
                float celebration = Pop((time - 2.7f) / 0.55f);
                badges[team].localScale *= Mathf.LerpUnclamped(1f, 1.5f, celebration);
            }
            else
            {
                position += new Vector2(side * 1500 * launch * launch, 420 * launch);
                badges[team].localRotation = Quaternion.Euler(0, 0, -side * launch * 540);
            }
            badges[team].anchoredPosition = position;
        }
        Impact(time, 0.9f, 18f, Vector2.zero);
        if (pushing)
        {
            // 押し合いの間も短い衝撃を繰り返す。
            float hitAt = 0.9f + Mathf.Floor((time - 0.9f) / 0.16f) * 0.16f;
            Impact(time, hitAt, 10f, new Vector2(contestShift, -20));
        }
        Impact(time, 1.9f, 30f, new Vector2((winner == 1 ? 1 : -1) * 130, 0));
    }

    private void DrawHookThrow(float time)
    {
        float cast = Ease((time - 0.5f) / 0.45f);
        float launch = Mathf.Clamp01((time - 1.95f) / 0.75f);
        float side = winner == 1 ? -1 : 1;
        float struggle = time >= 0.95f && time < 1.95f ? Mathf.Sin(time * 55f) * 9f : 0;
        for (int team = 0; team < 2; team++)
        {
            badges[team].anchoredPosition += Vector2.right * (team == 1 ? struggle : -struggle);
        }
        if (time >= 1.95f && !released)
        {
            released = true;
            Vector3 from = ResultOrigin + (Vector3)(badges[1 - winner].anchoredPosition * 0.01f) + Vector3.back;
            Vector3 landing = ResultOrigin + new Vector3(side * 16, from.y, -1);
            thrownBody.position = from;
            thrownBody.rotation = Quaternion.identity;
            thrownBody.isKinematic = false;
            thrownBody.linearVelocity = SweeperReel.BackThrowVelocity(from, landing, 5.5f);
            thrownBody.angularVelocity = new Vector3(0, 0, -side * 9);
        }
        if (released)
        {
            Vector3 position = thrownBody.position - ResultOrigin;
            badges[1 - winner].anchoredPosition = new Vector2(position.x, position.y) * 100;
            badges[1 - winner].localRotation = Quaternion.Euler(0, 0, thrownBody.rotation.eulerAngles.z);
        }
        badges[winner].localRotation = Quaternion.Euler(0, 0, side * Mathf.Sin(launch * Mathf.PI) * 22);
        for (int team = 0; team < 2; team++)
        {
            bool visible = time >= 0.5f && (team == winner ? time < 2.55f : time < 1.95f);
            hooks[team].gameObject.SetActive(visible);
            beams[team].gameObject.SetActive(visible);
            Vector2 start = badges[team].anchoredPosition + Vector2.up * (team == 1 ? 30 : -30);
            Vector2 target = badges[1 - team].anchoredPosition + Vector2.up * (team == 1 ? 30 : -30);
            Vector2 end = Vector2.Lerp(start, target, cast);
            hooks[team].anchoredPosition = end;
            if (visible)
            {
                Vector3 a = ResultOrigin + (Vector3)(start * 0.01f) + Vector3.back;
                Vector3 b = ResultOrigin + (Vector3)(end * 0.01f) + Vector3.back;
                Vector3 control = (a + b) * 0.5f + Vector3.up * (released ? 2.5f : 0.25f + Mathf.Abs(struggle) * 0.03f);
                beams[team].FollowCurve(a, b, control);
            }
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
        resultCamera.transform.position = cameraHome +
            new Vector3(Mathf.Sin(age * 190), Mathf.Cos(age * 157), 0) * strength * remaining * 0.01f;
        burst.gameObject.SetActive(true);
        burst.rectTransform.anchoredPosition = position;
        burst.rectTransform.localScale = Vector3.one * (0.5f + age * 5);
        burst.color = new Color(1, 0.85f, 0.4f, remaining);
    }

    private static float TeamX(int team) => team == 1 ? -360 : 360;
    private static void SetLayer(Transform parent)
    {
        parent.gameObject.layer = ResultLayer;
        foreach (Transform child in parent) SetLayer(child);
    }

    private static void BuildRobot(RectTransform parent, int team)
    {
        Color suit = new Color(0.95f, 0.94f, 0.87f);
        Color dark = new Color(0.12f, 0.18f, 0.20f);
        Color accent = SpaceJunkTeams.TeamColor(team);
        Symbol("Suit", parent, new Vector2(0, -35), 115, WinnerResultSymbol.Shape.Circle, dark);
        Symbol("SuitFill", parent, new Vector2(0, -35), 100, WinnerResultSymbol.Shape.Circle, suit);
        Symbol("HelmetOutline", parent, new Vector2(0, 30), 155, WinnerResultSymbol.Shape.Circle, dark);
        Symbol("Helmet", parent, new Vector2(0, 30), 140, WinnerResultSymbol.Shape.Circle, suit);
        WinnerResultSymbol visor = Symbol("Visor", parent, new Vector2(0, 28), 112, WinnerResultSymbol.Shape.Circle, dark);
        visor.rectTransform.localScale = new Vector3(1, 0.65f, 1);
        foreach (int sign in new[] { -1, 1 })
        {
            Image eye = Rect("Eye", parent, new Vector2(sign * 20, 30), new Vector2(9, 28)).gameObject.AddComponent<Image>();
            eye.color = new Color(0.45f, 0.95f, 0.9f);
            eye.raycastTarget = false;
            Symbol("Glove", parent, new Vector2(sign * 80, -28), 42, WinnerResultSymbol.Shape.Circle, accent);
            Symbol("Boot", parent, new Vector2(sign * 32, -91), 40, WinnerResultSymbol.Shape.Circle, dark);
        }
        Symbol("SuitBadge", parent, new Vector2(0, -40), 28, WinnerResultSymbol.Shape.Circle, accent);
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (resultCamera != null) Destroy(resultCamera.gameObject);
        if (thrownBody != null) Destroy(thrownBody.gameObject);
        foreach (LiquidBeam beam in beams)
        {
            if (beam != null) Destroy(beam.gameObject);
        }
    }
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
