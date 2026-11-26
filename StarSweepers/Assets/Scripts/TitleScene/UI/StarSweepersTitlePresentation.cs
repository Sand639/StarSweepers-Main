using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>StarSweepersTitle のシーンで使うノートと、浮かぶロボットの演出。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TitleScreen))]
public sealed class StarSweepersTitlePresentation : MonoBehaviour
{
    public const string SceneName = "StarSweepersTitle";
    public const float NotebookAngle = 4f;
    public const float NotebookSlideSeconds = 0.55f;

    [SerializeField] private Texture2D background;
    [SerializeField] private Texture2D[] robotArt;
    [SerializeField] private Texture2D notebook;
    [SerializeField] private Texture2D binding;
    [SerializeField] private Texture2D titleTape;
    [SerializeField] private Texture2D hand;
    [SerializeField] private Texture2D keychain;
    [SerializeField] private Texture2D menuHighlight;
    [SerializeField] private Texture2D matchWell;
    [SerializeField] private Texture2D roomWell;
    [SerializeField] private Texture2D quickHighlight;
    [SerializeField] private Texture2D roomNormal;
    [SerializeField] private Texture2D roomHighlight;

    private static readonly Color Cream = new Color32(243, 230, 217, 255);
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    private readonly List<RectTransform> charms = new List<RectTransform>();
    private readonly List<RectTransform> robots = new List<RectTransform>();
    private readonly List<Vector2> robotHomes = new List<Vector2>();
    private readonly List<Object> generated = new List<Object>();
    private RectTransform robotGroup;
    private float swingTime;
    private float swingStrength;

    internal void Build()
    {
        TitleParts parts = new TitleParts(gameObject);
        GameObject main = parts.Object(TitlePartRole.PageMain);
        BuildBackground();
        BuildMain(main.transform);

        TitleScreenTheme mainTheme = MakeTheme(null, menuHighlight, Color.clear);
        foreach (TitleMenuButton item in main.GetComponentsInChildren<TitleMenuButton>(true))
        {
            StyleMainButton(item, mainTheme);
        }

        TitleScreenTheme quickTheme = MakeTheme(quickHighlight, quickHighlight, Color.white);
        TitleScreenTheme roomTheme = MakeTheme(roomNormal, roomHighlight, Color.white);
        TitleScreenTheme backTheme = MakeTheme(null, null, Color.clear, new Color(1f, 1f, 1f, 0.2f));
        BuildNotebook(parts.Object(TitlePartRole.PageMultiMenu), TitlePartRole.PageMultiMenu, quickTheme, roomTheme, backTheme);
        BuildNotebook(parts.Object(TitlePartRole.PageMulti), TitlePartRole.PageMulti, quickTheme, roomTheme, backTheme);
        BuildNotebook(parts.Object(TitlePartRole.PageCreate), TitlePartRole.PageCreate, quickTheme, roomTheme, backTheme);
    }

    internal void ShowPage(bool notebookOpen, bool opening)
    {
        robotGroup.gameObject.SetActive(!notebookOpen);
        if (opening)
        {
            Swing(-29f);
        }
    }

    private void Update()
    {
        float time = Time.unscaledTime;
        if (robotGroup != null && robotGroup.gameObject.activeInHierarchy)
        {
            for (int i = 0; i < robots.Count; i++)
            {
                float bob = Mathf.Sin(time * (1.45f + i * 0.21f) + i * 2.1f) * (10f + i * 3f);
                robots[i].anchoredPosition = robotHomes[i] + Vector2.up * bob;
            }
        }

        swingTime += Time.unscaledDeltaTime;
        float angle = Mathf.Sin(swingTime * 10.2f) * Mathf.Exp(-swingTime * 2.9f) * swingStrength;
        foreach (RectTransform charm in charms)
        {
            charm.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void Swing(float strength)
    {
        swingTime = 0f;
        swingStrength = strength;
    }

    private void OnDestroy()
    {
        foreach (Object item in generated)
        {
            Destroy(item);
        }
    }

    private void BuildBackground()
    {
        Transform original = transform.Find("Background");
        if (original != null)
        {
            original.gameObject.SetActive(false);
        }

        Transform tint = transform.Find("Tint");
        if (tint != null)
        {
            tint.gameObject.SetActive(false);
        }

        RawImage sky = Picture("TitleSky", transform, background);
        sky.transform.SetAsFirstSibling();
        AspectRatioFitter fit = sky.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = (float)background.width / background.height;
    }

    private void BuildMain(Transform main)
    {
        RectTransform buttons = (RectTransform)main.Find("Buttons");
        Stretch(buttons);

        Text title = main.Find("Title").GetComponent<Text>();
        title.text = "星掃\nSTAR\nSWEEPERS";
        title.fontSize = 88;
        title.fontStyle = FontStyle.Bold;
        title.lineSpacing = 0.9f;
        title.alignment = TextAnchor.UpperLeft;
        title.color = Cream;
        Place(title.rectTransform, TopLeft, new Vector2(112f, -92f), new Vector2(610f, 353f), TopLeft);

        Outline outline = title.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }

        Color32[] stripeColors = { new Color32(135, 183, 178, 255), new Color32(224, 171, 83, 255), new Color32(167, 75, 51, 255) };
        for (int i = 0; i < stripeColors.Length; i++)
        {
            float y = -472f - i * 6f;
            Stripe(main, 120f, y, 398f, stripeColors[i]);
            Stripe(main, 533f, y, 16f, stripeColors[i]);
            Stripe(main, 557f, y, 10f, stripeColors[i]);
            Stripe(main, 575f, y, 7f, stripeColors[i]);
        }

        robotGroup = new GameObject("FloatingRobots", typeof(RectTransform)).GetComponent<RectTransform>();
        robotGroup.SetParent(main, false);
        robotGroup.SetAsFirstSibling();
        Stretch(robotGroup);
        Vector2[] positions = { new Vector2(-259f, 209f), new Vector2(-781f, 175f), new Vector2(-461f, -258f) };
        float[] sizes = { 413f, 624f, 409f };
        for (int i = 0; i < positions.Length; i++)
        {
            RawImage robot = Picture($"Robot{i + 1}", robotGroup, robotArt[i]);
            Place(robot.rectTransform, new Vector2(1f, 0.5f), positions[i], Vector2.one * sizes[i], Center);
            if (i == 1)
            {
                robot.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }

            robots.Add(robot.rectTransform);
            robotHomes.Add(positions[i]);
        }
    }

    private static void StyleMainButton(TitleMenuButton item, TitleScreenTheme theme)
    {
        float top;
        switch (item.Action)
        {
            case TitleButtonAction.OpenMulti:
                item.Label.text = "オンライン対戦";
                top = 556f;
                break;
            case TitleButtonAction.PlaySolo:
                top = 663f;
                break;
            case TitleButtonAction.OpenSettings:
                top = 735f;
                break;
            case TitleButtonAction.Quit:
                item.Label.text = "終了";
                top = 807f;
                break;
            default:
                return;
        }

        bool primary = item.Action == TitleButtonAction.OpenMulti;
        Place((RectTransform)item.transform, TopLeft, new Vector2(92f, -top),
            new Vector2(516f, primary ? 96f : 64f), TopLeft);
        item.Label.alignment = TextAnchor.MiddleLeft;
        item.Label.fontSize = primary ? 49 : 37;
        Stretch(item.Label.rectTransform);
        item.Label.rectTransform.offsetMin = new Vector2(22f, 0f);
        item.ApplyPresentationTheme(theme);
    }

    private void BuildNotebook(GameObject page, TitlePartRole role, TitleScreenTheme quickTheme,
        TitleScreenTheme roomTheme, TitleScreenTheme backTheme)
    {
        foreach (TitlePart part in page.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role != TitlePartRole.TabletPanel)
            {
                continue;
            }

            RectTransform panel = (RectTransform)part.transform;
            Image frame = panel.GetComponent<Image>();
            frame.color = Color.clear;
            frame.raycastTarget = false;
            Place(panel, new Vector2(1f, 0.5f), new Vector2(-646f, -1f), new Vector2(923f, 992f), Center);
            panel.localScale = Vector3.one * 0.95f;
            panel.localRotation = Quaternion.Euler(0f, 0f, NotebookAngle);

            RectTransform board = (RectTransform)panel.Find("Board");
            board.GetComponent<Image>().color = Color.clear;
            board.GetComponent<Image>().raycastTarget = false;
            // Keep the original content width for the room list and form.
            Place(board, Center, new Vector2(10f, 0f), new Vector2(928f, 928f), Center);
            board.localScale = new Vector3(0.92f, 1f, 1f);

            RawImage cover = Picture("NotebookCover", panel, notebook);
            Stretch(cover.rectTransform);
            cover.transform.SetAsFirstSibling();
            // Sample the blank part of the cover under the printed controller legend.
            RawImage legend = Picture("NotebookLegendBacking", panel, notebook);
            legend.uvRect = new Rect(580f / 923f, 21f / 992f, 1f / 923f, 54f / 992f);
            Place(legend.rectTransform, TopLeft, new Vector2(584f, -917f), new Vector2(280f, 54f), TopLeft);
            legend.transform.SetSiblingIndex(1);

            ImagePart("OnlineTitle", panel, titleTape, new Vector2(164f, -36f), new Vector2(336f, 81f));
            Place(Picture("Hand", panel, hand).rectTransform, TopLeft,
                new Vector2(801f, -562f), new Vector2(409f, 431f), TopLeft);

            RawImage charm = Picture("Keychain", panel, keychain);
            Vector2 anchor = new Vector2(0.61f, 0.91f);
            Place(charm.rectTransform, TopLeft, new Vector2(51f, -266f), new Vector2(223f, 325f), anchor);
            charm.raycastTarget = true;
            Button swing = charm.gameObject.AddComponent<Button>();
            swing.navigation = new Navigation { mode = Navigation.Mode.None };
            swing.transition = Selectable.Transition.None;
            swing.targetGraphic = charm;
            swing.onClick.AddListener(() => Swing(27f));
            charms.Add(charm.rectTransform);
            ImagePart("Binding", panel, binding, new Vector2(2f, -181f), new Vector2(83f, 109f));

            StyleBoard(board, role, quickTheme, roomTheme, backTheme);
            return;
        }
    }

    private void StyleBoard(RectTransform board, TitlePartRole role, TitleScreenTheme quickTheme,
        TitleScreenTheme roomTheme, TitleScreenTheme backTheme)
    {
        bool onlineMenu = role == TitlePartRole.PageMultiMenu;
        if (onlineMenu)
        {
            ImagePart("MatchWell", board, matchWell, new Vector2(42f, -98f), new Vector2(848f, 207f)).SetAsFirstSibling();
            ImagePart("RoomWell", board, roomWell, new Vector2(42f, -337f), new Vector2(848f, 405f)).SetAsFirstSibling();
        }

        if (role == TitlePartRole.PageMulti)
        {
            ImagePart("RoomListWell", board, roomWell, new Vector2(37f, -164f), new Vector2(854f, 589f)).SetAsFirstSibling();
        }

        foreach (Transform child in board)
        {
            if (child.name == "Line")
            {
                child.gameObject.SetActive(false);
            }

            if (child.name == "Heading" && role != TitlePartRole.PageMulti)
            {
                child.gameObject.SetActive(false);
            }
        }

        foreach (TitleMenuButton item in board.GetComponentsInChildren<TitleMenuButton>(true))
        {
            switch (item.Action)
            {
                case TitleButtonAction.QuickPlay:
                    PlaceMenuButton(item, quickTheme, -123f, new Vector2(807f, 156f), 56);
                    break;
                case TitleButtonAction.OpenFindRoom:
                    PlaceMenuButton(item, roomTheme, -378f, new Vector2(594f, 126f), 44);
                    break;
                case TitleButtonAction.OpenCreate:
                    PlaceMenuButton(item, roomTheme, -541f, new Vector2(594f, 126f), 44);
                    break;
                case TitleButtonAction.JoinListedServer:
                case TitleButtonAction.RefreshRoomList:
                case TitleButtonAction.CreateServer:
                    item.ApplyPresentationTheme(roomTheme);
                    break;
                case TitleButtonAction.Back:
                    item.Label.text = string.Empty;
                    Place((RectTransform)item.transform, TopLeft, new Vector2(25f, -10f), new Vector2(86f, 80f), TopLeft);
                    item.ApplyPresentationTheme(backTheme);
                    break;
            }
        }

        foreach (TitlePart part in board.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == TitlePartRole.TabletHint)
            {
                Text hint = part.GetComponent<Text>();
                hint.color = Cream;
                Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-37f, -4f),
                    new Vector2(425f, 40f), new Vector2(1f, 0f));
            }
        }
    }

    private static void PlaceMenuButton(TitleMenuButton item, TitleScreenTheme theme, float top, Vector2 size, int fontSize)
    {
        Place((RectTransform)item.transform, new Vector2(0.5f, 1f),
            new Vector2(0f, top - size.y / 2f), size, Center);
        item.Label.fontSize = fontSize;
        item.ApplyPresentationTheme(theme, 1.025f);
    }

    private TitleScreenTheme MakeTheme(Texture2D normal, Texture2D highlighted, Color normalColor, Color? highlightColor = null)
    {
        TitleScreenTheme theme = ScriptableObject.CreateInstance<TitleScreenTheme>();
        generated.Add(theme);
        theme.button = MakeSprite(normal);
        theme.buttonHover = normal == highlighted ? theme.button : MakeSprite(highlighted);
        theme.buttonColor = normalColor;
        theme.buttonHoverColor = highlightColor ?? Color.white;
        theme.buttonOutlineColor = Color.clear;
        theme.buttonTextColor = Cream;
        theme.buttonHoverTextColor = Cream;
        return theme;
    }

    private Sprite MakeSprite(Texture2D art)
    {
        if (art == null)
        {
            return null;
        }

        Sprite sprite = Sprite.Create(art, new Rect(0f, 0f, art.width, art.height), Center, 100f, 0, SpriteMeshType.FullRect);
        generated.Add(sprite);
        return sprite;
    }

    private static void Stripe(Transform parent, float x, float y, float width, Color color)
    {
        RawImage stripe = Picture("LogoStripe", parent, null);
        stripe.color = color;
        Place(stripe.rectTransform, TopLeft, new Vector2(x, y), new Vector2(width, 6f), TopLeft);
    }

    private static RectTransform ImagePart(string name, Transform parent, Texture2D texture, Vector2 position, Vector2 size)
    {
        RawImage picture = Picture(name, parent, texture);
        Place(picture.rectTransform, TopLeft, position, size, TopLeft);
        return picture.rectTransform;
    }

    private static RawImage Picture(string name, Transform parent, Texture2D texture)
    {
        RawImage image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
