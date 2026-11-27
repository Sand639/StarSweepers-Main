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

    [Header("頭のまわりを回る星（2026/11/24）")]
    [Tooltip("ON：スタン中、頭のまわりに星（★）を回す。そのときは上の頭の上の印は出さない")]
    [SerializeField] private bool useStars = true;

    [Tooltip("星の数")]
    [Min(1)]
    [SerializeField] private int starCount = 3;

    [Tooltip("星が回る高さ（足元から m）")]
    [SerializeField] private float starHeight = 1.35f;

    [Tooltip("星が回る円の半径（m）")]
    [SerializeField] private float starOrbitRadius = 0.38f;

    [Tooltip("星の大きさ（m）")]
    [SerializeField] private float starSize = 0.24f;

    [Tooltip("星が回る速さ（1秒あたりの度）")]
    [SerializeField] private float starOrbitSpeed = 320f;

    [Tooltip("星の色")]
    [SerializeField] private Color starColor = new Color(1f, 0.86f, 0.25f, 1f);

    [Tooltip("星に使うマテリアル（Art/Materials/SweeperRing.mat）。空なら再生したときに作るが、ビルドでは出ないことがある")]
    [SerializeField] private Material starMaterial;

    [Header("スタン後の無敵（2026/11/27）")]
    [Tooltip("スタンが解けてから、この秒数のあいだは新しくスタンしない・吹き飛ばされない（続けてスタンし続けないように）")]
    [Min(0f)]
    [SerializeField] private float invincibleSeconds = 2f;

    [Tooltip("スタンし始めてからこの秒数の間だけは、重ねて食らったら長いほうに延ばす（引っ張った爆弾がぶつかって即爆発した、のような同じ出来事の重なり）。過ぎたらスタン中は延びない")]
    [Min(0f)]
    [SerializeField] private float stackWindow = 0.3f;

    [Tooltip("ON：無敵の間、体を白っぽく点滅させる")]
    [SerializeField] private bool blinkWhileInvincible = true;

    [Tooltip("無敵の間に点滅する色")]
    [SerializeField] private Color invincibleColor = new Color(1f, 1f, 1f);

    [Tooltip("無敵の間、1秒に何回点滅させるか")]
    [Min(0.1f)]
    [SerializeField] private float invincibleBlinkPerSecond = 10f;

    /// <summary>
    /// スタンした瞬間に呼ばれる（渡ってくるのは秒数）。<see cref="SweeperNetSync"/> がほかの人の画面へ知らせるのに使う。
    /// ほかの人の画面で見た目だけ出すとき（<see cref="Stun(float, bool)"/> の notify が false）は呼ばれない。
    /// </summary>
    public event System.Action<float> Stunned;

    private SweeperStunStars stars;

    /// <summary>いま動けない状態か。</summary>
    public bool IsStunned => remaining > 0f;

    /// <summary>スタンが解けるまでの残り秒数。</summary>
    public float Remaining => Mathf.Max(0f, remaining);

    private float remaining;

    // スタンし始めた時刻（重ねて食らったときに延ばしてよいかを見る）
    private float stunStartTime = -100f;

    // この時刻までは無敵（スタンが解けた瞬間から Invincible Seconds）
    private float invincibleUntil = -100f;

    // 無敵の点滅中か（終わったら色を戻すため）
    private bool invincibleBlinking;

    /// <summary>スタンが解けたあとの無敵の間か（新しくスタンしない・吹き飛ばされない）。</summary>
    public bool IsInvincible => !IsStunned && Time.time < invincibleUntil;

    /// <summary>
    /// いま新しくスタン・吹き飛ばしを受けるか。無敵の間・スタン中（食らい始めの Stack Window を過ぎたあと）は false。
    /// 吹き飛ばしの共通の入口（<see cref="MeteorStrikeEvent.ApplyPlayerImpact"/>）と、引っ張った物の当たり判定が見る。
    /// </summary>
    public bool CanTakeHit => IsStunned ? Time.time - stunStartTime <= stackWindow : !IsInvincible;

    /// <summary>スタン後の無敵の秒数（インスペクターの値）。</summary>
    public float InvincibleSeconds => invincibleSeconds;

    // 点滅が終わったら戻す色。
    // **スタンが始まる瞬間に、その時点の色を控える。**
    // Awake で控えると、あとからチームの色（SweeperNetPlayer）が塗られても
    // プレハブの元の色（青）に戻ってしまうため
    private Color[] originalColors = new Color[0];

    // 体ごとに「どの名前の色を塗るか」。-1 は色を持たないので塗らない。
    // material.color は "_Color"（か [MainColor] の付いた色）しか見ないため、
    // キャラ用シェーダー（SG_CharacterColor）のように色の名前が "_SuitColor" だと
    // 「_Color がありません」というエラーが出てしまう。そのためマテリアルごとに探す
    private int[] colorPropertyIds = new int[0];

    // 体ごとに、服の色を塗る部品（Hans さんの CharacterColorController）。無い体は null。
    // 服の色はチームの色を MaterialPropertyBlock で上書きしているので、マテリアルを塗っても隠れて見えない。
    // そのため、この部品がある体は同じく MaterialPropertyBlock で赤く塗り、戻すときは部品に塗り直してもらう
    private CharacterColorController[] suitPainters = new CharacterColorController[0];
    private MaterialPropertyBlock suitBlock;

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
        suitPainters = new CharacterColorController[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            colorPropertyIds[i] = -1;
            originalColors[i] = Color.white;

            if (bodyRenderers[i] == null)
            {
                continue;
            }

            // 服の色の部品がある体は、マテリアルを複製せずに服の色の項目を塗る（戻すのは部品に任せる）
            suitPainters[i] = FindSuitPainter(bodyRenderers[i]);
            if (suitPainters[i] != null)
            {
                colorPropertyIds[i] = Shader.PropertyToID(suitPainters[i].colorProperty);
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

    /// <summary>この体の服の色を塗っている部品を探す。無ければ null。</summary>
    private CharacterColorController FindSuitPainter(Renderer body)
    {
        foreach (CharacterColorController painter in GetComponentsInChildren<CharacterColorController>(true))
        {
            if (painter.characterRenderer == body)
            {
                return painter;
            }
        }
        return null;
    }

    /// <summary>i 番目の体を、この色で塗る。色を持たないマテリアルなら何もしない。</summary>
    private void Paint(int i, Color color)
    {
        if (bodyRenderers[i] == null || i >= colorPropertyIds.Length || colorPropertyIds[i] == -1)
        {
            return;
        }

        CharacterColorController painter = i < suitPainters.Length ? suitPainters[i] : null;
        if (painter != null)
        {
            suitBlock ??= new MaterialPropertyBlock();
            bodyRenderers[i].GetPropertyBlock(suitBlock, painter.bodyMaterialIndex);
            suitBlock.SetColor(colorPropertyIds[i], color);
            bodyRenderers[i].SetPropertyBlock(suitBlock, painter.bodyMaterialIndex);
            return;
        }

        bodyRenderers[i].material.SetColor(colorPropertyIds[i], color);
    }

    /// <summary>i 番目の体を元の色に戻す。服の色の部品がある体は、部品にいまのチームの色で塗り直してもらう。</summary>
    private void PaintOriginal(int i)
    {
        CharacterColorController painter = i < suitPainters.Length ? suitPainters[i] : null;
        if (painter != null)
        {
            painter.ApplyColor();
            return;
        }

        Paint(i, originalColors[i]);
    }

    /// <summary>
    /// この秒数だけ動けなくする。
    /// すでにスタン中なら、**残りが長いほうを採用**する（短い爆発で上書きしない）。
    /// </summary>
    public void Stun(float seconds)
    {
        Stun(seconds, true);
    }

    /// <summary>
    /// この秒数だけ動けなくする。<paramref name="notify"/> が false のときは <see cref="Stunned"/> を呼ばない
    /// （オンラインで、ほかの人の体に見た目だけ出すとき。ほかの人の体はこのPCでは動かしていないので、動けなくなる処理は関係ない）。
    /// </summary>
    public void Stun(float seconds, bool notify)
    {
        if (seconds <= 0f)
        {
            return;
        }

        // 無敵の間・スタン中（食らい始めを過ぎたあと）は食らわない（2026/11/27）。
        // ほかの人の画面の見た目だけのスタン（notify が false）は、本人のPCで決まったことなのでそのまま出す
        if (notify && !CanTakeHit)
        {
            return;
        }

        // スタン中に重ねて食らったときは控え直さない（点滅中の赤を控えてしまうため）
        if (!IsStunned)
        {
            // 無敵の点滅の途中なら、先に元の色へ戻してから控える
            if (invincibleBlinking)
            {
                RestoreColors();
                invincibleBlinking = false;
            }
            invincibleUntil = -100f;
            stunStartTime = Time.time;
            CaptureColors();

            // 転ぶ音は、新しくスタンしたときだけ（重ねて食らったときは鳴らさない）
            GameAudio.PlaySe(GameSe.PlayerStun);
        }

        remaining = Mathf.Max(remaining, seconds);
        ShowMarker(true);

        if (notify)
        {
            Stunned?.Invoke(seconds);
        }
    }

    /// <summary>頭の上の印（星を使うなら星）を出す／隠す。</summary>
    private void ShowMarker(bool visible)
    {
        if (useStars)
        {
            if (visible && stars == null)
            {
                stars = SweeperStunStars.Create(transform, starCount, starHeight, starOrbitRadius, starSize,
                    starOrbitSpeed, starColor, starMaterial);
            }

            if (stars != null)
            {
                stars.SetVisible(visible);
            }

            if (stunMarker != null)
            {
                stunMarker.SetActive(false);
            }
            return;
        }

        if (stunMarker != null)
        {
            stunMarker.SetActive(visible);
        }
    }

    private void Update()
    {
        if (remaining <= 0f)
        {
            UpdateInvincibleBlink();
            return;
        }

        remaining -= Time.deltaTime;

        // このフレームで解けた：色と印を元に戻し、無敵の間に入る
        if (remaining <= 0f)
        {
            remaining = 0f;
            RestoreColors();
            ShowMarker(false);
            invincibleUntil = Time.time + invincibleSeconds;
            return;
        }

        // まだスタン中：体を点滅させ、頭の上の印を回す
        Blink();

        if (stunMarker != null && !useStars)
        {
            stunMarker.transform.Rotate(Vector3.up, markerSpinSpeed * Time.deltaTime, Space.World);
        }
    }

    /// <summary>無敵の間は体を白っぽく点滅させ、終わったら元の色に戻す。</summary>
    private void UpdateInvincibleBlink()
    {
        bool active = blinkWhileInvincible && IsInvincible;
        if (!active)
        {
            if (invincibleBlinking)
            {
                RestoreColors();
                invincibleBlinking = false;
            }
            return;
        }

        invincibleBlinking = true;
        bool on = Mathf.Repeat(Time.time * invincibleBlinkPerSecond, 1f) < 0.5f;
        for (int i = 0; i < originalColors.Length; i++)
        {
            if (on)
            {
                Paint(i, invincibleColor);
            }
            else
            {
                PaintOriginal(i);
            }
        }
    }

    /// <summary>体を赤と元の色で交互に塗る。</summary>
    private void Blink()
    {
        // 1回の点滅（1 ÷ blinkPerSecond 秒）のうち、前半を赤、後半を元の色にする
        bool on = Mathf.Repeat(Time.time * blinkPerSecond, 1f) < 0.5f;

        for (int i = 0; i < originalColors.Length; i++)
        {
            if (on)
            {
                Paint(i, stunColor);
            }
            else
            {
                PaintOriginal(i);
            }
        }
    }

    private void RestoreColors()
    {
        for (int i = 0; i < originalColors.Length; i++)
        {
            PaintOriginal(i);
        }
    }
}
