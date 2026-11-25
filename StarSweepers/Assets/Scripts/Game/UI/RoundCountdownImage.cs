using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 始まりの演出の「3・2・1・START!」を、文字の代わりに画像で出す部品（2026/11/25・大槻さん）。
/// 説明：Documents/スクリプトの説明/Game/UI/RoundCountdownImage.md
/// </summary>
[RequireComponent(typeof(Image))]
public class RoundCountdownImage : MonoBehaviour
{
    /// <summary>Resources に置いたプレハブの名前。</summary>
    public const string PrefabResourcePath = "RoundCountdownImage";

    [SerializeField, Tooltip("1・2・3 の画像（上から 1, 2, 3 の順）")]
    private Sprite[] numberSprites = new Sprite[3];

    [SerializeField, Tooltip("START! の画像")]
    private Sprite startSprite;

    [SerializeField, Tooltip("画像の大きさの倍率。1 で画像そのままの大きさ（1920×1080 の画面での大きさ）")]
    private float scale = 0.6f;

    private Image image;

    /// <summary>いま出している文字（同じ文字なら入れ直さない）。</summary>
    private string shown;

    /// <summary>プレハブを読み込んで、<paramref name="parent"/>（演出の画面）の子として作る。プレハブが無ければ null（そのときは文字のまま）。</summary>
    public static RoundCountdownImage Create(Transform parent)
    {
        RoundCountdownImage prefab = Resources.Load<RoundCountdownImage>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[カウントダウン] Resources/{PrefabResourcePath}.prefab が無いので、3・2・1・START! は文字で出します。");
            return null;
        }

        return Instantiate(prefab, parent, false);
    }

    private void Awake()
    {
        image = GetComponent<Image>();
        image.enabled = false;
    }

    /// <summary>
    /// 演出の大きな文字を渡す。「1」「2」「3」「START!」なら画像を出して true を返す（呼んだ側は文字を消す）。
    /// それ以外（マップの名前・空など）は画像を隠して false を返す。
    /// </summary>
    public bool Show(string label)
    {
        if (label == shown)
        {
            return image.enabled;
        }

        shown = label;
        Sprite sprite = SpriteFor(label);
        image.enabled = sprite != null;
        if (sprite != null)
        {
            // 画像ごとに縦横の大きさが違うので、画像そのままの大きさに倍率をかける
            image.sprite = sprite;
            image.SetNativeSize();
            image.rectTransform.sizeDelta *= scale;
        }

        return sprite != null;
    }

    private Sprite SpriteFor(string label)
    {
        if (label == "START!")
        {
            return startSprite;
        }

        if (int.TryParse(label, out int number) && number >= 1 && number <= numberSprites.Length)
        {
            return numberSprites[number - 1];
        }

        return null;
    }
}
