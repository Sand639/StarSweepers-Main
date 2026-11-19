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
    private readonly float[] coverDelays = new float[6];
    private readonly float[] revealDelays = new float[6];
    private readonly float[] coverDurations = new float[6];
    private readonly float[] revealDurations = new float[6];
    private readonly RectTransform[] rows = new RectTransform[6];
    private readonly RectTransform[] bodies = new RectTransform[6];
    private readonly RectTransform[] leftCaps = new RectTransform[6];
    private readonly RectTransform[] rightCaps = new RectTransform[6];
    private RectTransform parent;

    public override void Build(RectTransform root, Color color)
    {
        parent = root;
        var random = new System.Random(System.Guid.NewGuid().GetHashCode());
        RandomizeTiming(random, coverDelays, coverDurations);
        RandomizeTiming(random, revealDelays, revealDurations);
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

    private static void RandomizeTiming(System.Random random, float[] delays, float[] durations)
    {
        int[] order = { 0, 1, 2, 3, 4, 5 };
        for (int i = order.Length - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            int temporary = order[i];
            order[i] = order[other];
            order[other] = temporary;
        }
        for (int slot = 0; slot < order.Length; slot++)
        {
            int row = order[slot];
            // 出発時刻が固まらないよう、ばらした枠内で時刻と速さを抽選する。
            delays[row] = slot * 0.12f + (float)random.NextDouble() * 0.08f;
            durations[row] = 0.24f + (float)random.NextDouble() * 0.08f;
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
            float delay = revealing ? revealDelays[i] : coverDelays[i];
            float duration = revealing ? revealDurations[i] : coverDurations[i];
            float t = Mathf.Clamp01((progress - delay) / duration);
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
