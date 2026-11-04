using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// **マウスが重なった項目を、はっきり分かるように見せる。**
///
/// 3つ同時に変えている。**1つだけだと気づかれにくい**ため。
///
/// | 変わるもの | 内容 |
/// | **色** | 項目の背景が明るくなる |
/// | **大きさ** | 少しだけ大きくなる |
/// | **印** | 左側に「▶」が出る |
///
/// ## 時間の進み方に注意
///
/// ポーズ中は `Time.timeScale` が 0 なので、**`Time.deltaTime` は 0 のまま。**
/// そのまま使うと**アニメーションが一切動かない**ので、
/// 止まっていても進む `Time.unscaledDeltaTime` を使っている。
/// </summary>
public class MenuHoverHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Tooltip("色を変える背景")]
    [SerializeField] private Image background;

    [Tooltip("重なっていないときの色")]
    [SerializeField] private Color normalColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);

    [Tooltip("重なっているときの色")]
    [SerializeField] private Color hoverColor = new Color(0.30f, 0.55f, 0.85f, 1f);

    [Tooltip("重なっているときの大きさ（1.05で5%大きい）")]
    [Range(1f, 1.3f)]
    [SerializeField] private float hoverScale = 1.05f;

    [Tooltip("左に出す印。空でもよい")]
    [SerializeField] private GameObject marker;

    [Tooltip("変わるときの速さ。大きいほど速い")]
    [Range(1f, 40f)]
    [SerializeField] private float smooth = 16f;

    private bool hovered;

    // コントローラーの十字キー／スティックで選ばれているか（2026/9/22）
    private bool selectedByPad;

    /// <summary>いまマウスが重なっているか、コントローラーで選ばれているか。</summary>
    public bool IsHovered => hovered || selectedByPad;

    private void OnEnable()
    {
        // 開き直したときに、前回の状態が残らないようにする
        hovered = false;
        selectedByPad = false;
        Apply(true);
    }

    private void Update()
    {
        Apply(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
    }

    /// <summary>
    /// **コントローラーで選ばれたときも、マウスが重なったときと同じ見た目にする。**
    /// マウスでクリックしたときも「選ばれた」扱いになるが、それはマウスの重なりで足りているので数えない。
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        selectedByPad = !(eventData is PointerEventData);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selectedByPad = false;
    }

    /// <summary>いまの状態を見た目に反映する。`instant` が true なら一瞬で変える。</summary>
    private void Apply(bool instant)
    {
        float wantedScale = IsHovered ? hoverScale : 1f;
        Color wantedColor = IsHovered ? hoverColor : normalColor;

        // **止まっていても進む時間**を使う（ポーズ中に動かないと困る）
        float t = instant || smooth <= 0f
            ? 1f
            : 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);

        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * wantedScale, t);

        if (background != null)
        {
            background.color = Color.Lerp(background.color, wantedColor, t);
        }

        if (marker != null && marker.activeSelf != IsHovered)
        {
            marker.SetActive(IsHovered);
        }
    }

    /// <summary>作るときに、外から中身を渡す。</summary>
    public void Setup(Image target, Color normal, Color hover, GameObject hoverMarker)
    {
        background = target;
        normalColor = normal;
        hoverColor = hover;
        marker = hoverMarker;
    }
}
