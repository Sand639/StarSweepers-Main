using UnityEngine;
using UnityEngine.UI;

/// <summary>同じ行のボタンの間を、選択中の下地だけ滑らせる。</summary>
public sealed class TitleSelectionRail
{
    private const float Duration = 0.2f;
    private readonly RectTransform indicator;
    private RectTransform target;
    private Vector2 start;
    private float elapsed;

    private TitleSelectionRail(TitleMenuButton[] buttons)
    {
        var image = new GameObject("SelectionRail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
            .GetComponent<Image>();
        image.gameObject.layer = buttons[0].gameObject.layer;
        indicator = image.rectTransform;
        indicator.SetParent(buttons[0].transform.parent, false);
        indicator.SetAsFirstSibling();
        image.raycastTarget = false;
        foreach (TitleMenuButton button in buttons)
        {
            button.UseSelectionRail(this, image);
        }
    }

    /// <summary>指定したボタンが同じ行にそろう新しい画面だけに付ける。</summary>
    public static TitleSelectionRail Create(GameObject page, params TitleButtonAction[] actions)
    {
        if (page == null) return null;
        var buttons = new TitleMenuButton[actions.Length];
        var candidates = page.GetComponentsInChildren<TitleMenuButton>(true);
        for (int i = 0; i < actions.Length; i++)
        {
            foreach (TitleMenuButton candidate in candidates)
            {
                if (candidate.Action == actions[i])
                {
                    buttons[i] = candidate;
                    break;
                }
            }
            if (buttons[i] == null || (i > 0 && buttons[i].transform.parent != buttons[0].transform.parent))
                return null;
        }
        return new TitleSelectionRail(buttons);
    }

    public void Select(RectTransform next)
    {
        if (target == next) return;
        bool first = target == null;
        target = next;
        indicator.anchorMin = target.anchorMin;
        indicator.anchorMax = target.anchorMax;
        indicator.pivot = target.pivot;
        indicator.sizeDelta = target.sizeDelta;
        start = indicator.anchoredPosition;
        elapsed = 0f;
        if (first || !indicator.gameObject.activeInHierarchy)
            Snap();
    }

    public void Update(float deltaTime)
    {
        if (target == null) return;
        if (!indicator.gameObject.activeInHierarchy)
        {
            Snap();
            return;
        }
        elapsed = Mathf.Min(elapsed + deltaTime, Duration);
        float t = elapsed / Duration;
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        indicator.anchoredPosition = Vector2.LerpUnclamped(start, target.anchoredPosition, eased);
        indicator.sizeDelta = target.sizeDelta;
    }

    private void Snap()
    {
        indicator.anchoredPosition = target.anchoredPosition;
        start = target.anchoredPosition;
        elapsed = Duration;
    }
}
