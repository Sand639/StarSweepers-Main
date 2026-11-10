using UnityEngine;

/// <summary>
/// **プレイヤーのスタン（動けない状態）を管理する。**
///
/// 爆発に巻き込まれると、ここに <see cref="Stun"/> が呼ばれて一定時間動けなくなる。
/// スタン中だと分かるように、体を赤く点滅させ、頭の上に印を出す。
///
/// 「動けない」の判定を持つだけで、移動を止めるのは <see cref="FishingPlayerController"/>、
/// フックを止めるのは <see cref="HookController"/> の側で <see cref="IsStunned"/> を見て行う。
/// </summary>
public class PlayerStun : MonoBehaviour
{
    [Header("見せ方")]
    [Tooltip("スタン中に赤く点滅させる見た目。プレイヤーの体の Renderer を入れる")]
    [SerializeField] private Renderer[] bodyRenderers;

    [Tooltip("点滅するときの色")]
    [SerializeField] private Color stunColor = new Color(1f, 0.2f, 0.15f);

    [Tooltip("1秒に何回点滅させるか")]
    [SerializeField] private float blinkPerSecond = 6f;

    [Tooltip("スタン中だけ出す、頭の上の印")]
    [SerializeField] private GameObject stunMarker;

    [Tooltip("頭の上の印を回す速さ（1秒あたりの度）")]
    [SerializeField] private float markerSpinSpeed = 240f;

    /// <summary>いま動けない状態か。</summary>
    public bool IsStunned => remaining > 0f;

    /// <summary>スタンが解けるまでの残り秒数。</summary>
    public float Remaining => Mathf.Max(0f, remaining);

    private float remaining;

    // 点滅が終わったら戻す色。
    // **スタンが始まる瞬間に、その時点の色を控える。**
    // Awake で控えると、あとからチームの色（FishingNetPlayer）が塗られても
    // プレハブの元の色（青）に戻ってしまうため
    private Color[] originalColors = new Color[0];

    // 体ごとに「どの名前の色を塗るか」。-1 は色を持たないので塗らない。
    // material.color は "_Color"（か [MainColor] の付いた色）しか見ないため、
    // キャラ用シェーダー（SG_CharacterColor）のように色の名前が "_SuitColor" だと
    // 「_Color がありません」というエラーが出てしまう。そのためマテリアルごとに探す
    private int[] colorPropertyIds = new int[0];

    // 探す順番。左ほど優先
    private static readonly int[] FallbackColorIds =
    {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_SuitColor"),
    };

    private void Awake()
    {
        // 入れ忘れでも、点滅の for 文が落ちないよう空の配列にしておく
        if (bodyRenderers == null)
        {
            bodyRenderers = new Renderer[0];
        }

        if (stunMarker != null)
        {
            stunMarker.SetActive(false);
        }
    }

    /// <summary>いまの体の色を控える。<see cref="Stun"/> の最初の1回で呼ばれる（理由は <see cref="originalColors"/> のコメント）。</summary>
    private void CaptureColors()
    {
        originalColors = new Color[bodyRenderers.Length];
        colorPropertyIds = new int[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            colorPropertyIds[i] = -1;
            originalColors[i] = Color.white;

            if (bodyRenderers[i] == null)
            {
                continue;
            }

            Material material = bodyRenderers[i].material;
            int id = FindColorProperty(material);
            colorPropertyIds[i] = id;
            if (id != -1)
            {
                originalColors[i] = material.GetColor(id);
            }
        }
    }

    /// <summary>
    /// このマテリアルで「体の色」に当たる色の名前（ID）を探す。見つからなければ -1。
    /// シェーダーで「メインの色」に指定された色があればそれを、無ければ <see cref="FallbackColorIds"/> の順に探す。
    /// </summary>
    private static int FindColorProperty(Material material)
    {
        if (material == null || material.shader == null)
        {
            return -1;
        }

        Shader shader = material.shader;
        int count = shader.GetPropertyCount();
        for (int p = 0; p < count; p++)
        {
            if (shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Color &&
                (shader.GetPropertyFlags(p) & UnityEngine.Rendering.ShaderPropertyFlags.MainColor) != 0)
            {
                return shader.GetPropertyNameId(p);
            }
        }

        foreach (int id in FallbackColorIds)
        {
            if (material.HasColor(id))
            {
                return id;
            }
        }
        return -1;
    }

    /// <summary>i 番目の体を、この色で塗る。色を持たないマテリアルなら何もしない。</summary>
    private void Paint(int i, Color color)
    {
        if (bodyRenderers[i] == null || i >= colorPropertyIds.Length || colorPropertyIds[i] == -1)
        {
            return;
        }
        bodyRenderers[i].material.SetColor(colorPropertyIds[i], color);
    }

    /// <summary>
    /// この秒数だけ動けなくする。
    /// すでにスタン中なら、**残りが長いほうを採用**する（短い爆発で上書きしない）。
    /// </summary>
    public void Stun(float seconds)
    {
        // スタン中に重ねて食らったときは控え直さない（点滅中の赤を控えてしまうため）
        if (!IsStunned)
        {
            CaptureColors();
        }

        remaining = Mathf.Max(remaining, seconds);

        if (stunMarker != null)
        {
            stunMarker.SetActive(true);
        }
    }

    private void Update()
    {
        if (remaining <= 0f)
        {
            return;
        }

        remaining -= Time.deltaTime;

        // このフレームで解けた：色と印を元に戻して終わり
        if (remaining <= 0f)
        {
            remaining = 0f;
            RestoreColors();

            if (stunMarker != null)
            {
                stunMarker.SetActive(false);
            }
            return;
        }

        // まだスタン中：体を点滅させ、頭の上の印を回す
        Blink();

        if (stunMarker != null)
        {
            stunMarker.transform.Rotate(Vector3.up, markerSpinSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>体を赤と元の色で交互に塗る。</summary>
    private void Blink()
    {
        // 1回の点滅（1 ÷ blinkPerSecond 秒）のうち、前半を赤、後半を元の色にする
        bool on = Mathf.Repeat(Time.time * blinkPerSecond, 1f) < 0.5f;

        for (int i = 0; i < originalColors.Length; i++)
        {
            Paint(i, on ? stunColor : originalColors[i]);
        }
    }

    private void RestoreColors()
    {
        for (int i = 0; i < originalColors.Length; i++)
        {
            Paint(i, originalColors[i]);
        }
    }
}
