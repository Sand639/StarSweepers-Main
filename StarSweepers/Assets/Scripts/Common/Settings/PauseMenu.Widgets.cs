using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>PauseMenu の「部品づくり（項目・スライダー・文字・画像）」（PauseMenu.cs から分けた部分）。</summary>
public partial class PauseMenu
{
    /// <summary>押せる項目を1つ作る。マウスが重なったときの見せ方もここで付ける。</summary>
    private void CreateMenuItem(Transform parent, string label, float y, UnityEngine.Events.UnityAction onClick)
    {
        GameObject item = CreateImage(label, parent, itemColor);

        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rect.anchoredPosition = new Vector2(0f, y);

        Button button = item.AddComponent<Button>();
        button.targetGraphic = item.GetComponent<Image>();

        // 色は自分で変えるので、ボタンの色替えは切っておく（二重に変わると濁る）
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);

        CreateText("Label", item.transform, label, fontSize);

        // マウスが重なったときに出る印
        GameObject marker = CreateText("Marker", item.transform, "▶", fontSize);
        RectTransform markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0f, 0.5f);
        markerRect.anchorMax = new Vector2(0f, 0.5f);
        markerRect.sizeDelta = new Vector2(40f, ItemHeight);
        markerRect.anchoredPosition = new Vector2(26f, 0f);
        marker.SetActive(false);

        MenuHoverHighlight highlight = item.AddComponent<MenuHoverHighlight>();
        highlight.Setup(item.GetComponent<Image>(), itemColor, itemHoverColor, marker);
    }

    /// <summary>設定用のスライダーを1本作る。</summary>
    private void CreateSlider(Transform parent, string label, float y, float value,
        float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject row = new GameObject(label, typeof(RectTransform));
        row.transform.SetParent(parent, false);

        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0.5f);
        rowRect.anchorMax = new Vector2(0.5f, 0.5f);
        rowRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        rowRect.anchoredPosition = new Vector2(0f, y);

        // 横長の行の左に名前、右にスライダーを置く（どちらも行の端からの距離で位置を決める）
        GameObject labelObject = CreateText("Label", row.transform, label, fontSize - 4);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(0f, 0.5f);
        labelRect.sizeDelta = new Vector2(180f, ItemHeight);
        labelRect.anchoredPosition = new Vector2(100f, 0f);
        labelObject.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;

        // ----- スライダー本体 -----
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(row.transform, false);

        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(1f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.sizeDelta = new Vector2(200f, 16f);
        sliderRect.anchoredPosition = new Vector2(-110f, 0f);

        GameObject background = CreateImage("Background", sliderObject.transform, itemColor);
        Stretch(background.GetComponent<RectTransform>());

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        Stretch(fillArea.GetComponent<RectTransform>());

        // 値のところまで色を塗る部分。右端は Slider が値に合わせて動かす
        GameObject fill = CreateImage("Fill", fillArea.transform, itemHoverColor);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        GameObject handle = CreateImage("Handle", sliderObject.transform, Color.white);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20f, 32f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.minValue = min;
        slider.maxValue = max;

        // 今の値を先に入れてから、変わったときの処理をつなぐ（逆だと、作っただけで保存が走る）
        slider.value = value;
        slider.onValueChanged.AddListener(onChanged);
    }

    // ------------------------------------------------------------
    // 部品づくり
    // ------------------------------------------------------------

    /// <summary>単色の板を1枚作る（項目の背景・暗くする板・スライダーの部品に使う）。</summary>
    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject image = new GameObject(name, typeof(RectTransform), typeof(Image));
        image.transform.SetParent(parent, false);
        image.GetComponent<Image>().color = color;

        return image;
    }

    /// <summary>借りてきた日本語フォントで文字を1つ作る。親いっぱいに広げ、真ん中にそろえる。</summary>
    private GameObject CreateText(string name, Transform parent, string content, int size)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = size;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleCenter;

        // 文字はクリックの邪魔をしない（下のボタンに通す）
        text.raycastTarget = false;

        Stretch(textObject.GetComponent<RectTransform>());

        return textObject;
    }

    /// <summary>親の大きさいっぱいに広げる。</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// **パソコンに入っている日本語フォントを借りてくる。**
    ///
    /// Unityに最初から入っている文字は日本語を持っていないので、
    /// そのままだと項目名が四角（□□□）になってしまう。
    /// </summary>
    private Font FindFont()
    {
        return UiFont.Find(fontSize, fontNames);
    }
}
