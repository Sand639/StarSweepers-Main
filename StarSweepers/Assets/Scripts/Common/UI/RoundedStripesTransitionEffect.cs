using UnityEngine;
using UnityEngine.UI;

/// <summary>丸い色帯が右から覆い、読み込み後は左へ抜ける。</summary>
public sealed class RoundedStripesTransitionEffect : SceneTransitionEffect
{
    private static readonly Color[] Colors =
    {
        new Color(0.48f, 0.64f, 0.59f),
        new Color(0.82f, 0.59f, 0.22f),
        new Color(0.49f, 0.19f, 0.14f),
    };
    private static readonly float[] Delays = { 0.12f, 0.04f, 0.08f, 0.16f, 0.06f, 0f };
    private readonly RectTransform[] rows = new RectTransform[6];
    private readonly RectTransform[] bodies = new RectTransform[6];
    private readonly RectTransform[] leftCaps = new RectTransform[6];
    private readonly RectTransform[] rightCaps = new RectTransform[6];
    private RectTransform parent;

    public override void Build(RectTransform root, Color color)
    {
        parent = root;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new GameObject("RoundedStripe", typeof(RectTransform)).GetComponent<RectTransform>();
            rows[i].SetParent(root, false);
            rows[i].anchorMin = rows[i].anchorMax = new Vector2(0, 1f - (i + 0.5f) / rows.Length);
            rows[i].pivot = new Vector2(0, 0.5f);
            Color stripeColor = Colors[i % Colors.Length];
            Image body = CreateFullScreenImage(rows[i], "StripeBody", stripeColor);
            bodies[i] = body.rectTransform;
            leftCaps[i] = Cap(rows[i], stripeColor);
            rightCaps[i] = Cap(rows[i], stripeColor);
        }
    }

    private static RectTransform Cap(RectTransform row, Color color)
    {
        WinnerResultSymbol cap = new GameObject("RoundEnd", typeof(RectTransform)).AddComponent<WinnerResultSymbol>();
        cap.rectTransform.SetParent(row, false);
        cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0, 0.5f);
        cap.Symbol = WinnerResultSymbol.Shape.Circle;
        cap.color = color;
        cap.raycastTarget = true;
        return cap.rectTransform;
    }

    public override void Apply(float amount, bool revealing)
    {
        float width = parent.rect.width;
        float height = parent.rect.height / rows.Length + 2f;
        float length = width + height * 2;
        float progress = revealing ? 1f - amount : amount;
        for (int i = 0; i < rows.Length; i++)
        {
            float t = Mathf.Clamp01((progress - Delays[i]) / (1f - Delays[i]));
            float x = revealing
                ? Mathf.Lerp(-height, -height - length, t)
                : Mathf.Lerp(width + height, -height, t);
            rows[i].anchoredPosition = new Vector2(x, 0);
            rows[i].sizeDelta = new Vector2(length, height);
            bodies[i].offsetMin = new Vector2(height * 0.5f, 0);
            bodies[i].offsetMax = new Vector2(-height * 0.5f, 0);
            leftCaps[i].sizeDelta = rightCaps[i].sizeDelta = Vector2.one * height;
            leftCaps[i].anchoredPosition = new Vector2(height * 0.5f, 0);
            rightCaps[i].anchoredPosition = new Vector2(length - height * 0.5f, 0);
        }
    }
}
