using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 得点が入ったチームの帯の炎を燃え上がらせ、得点の数字をポンと大きくする（得点バーと同じ物に付ける）。
/// 説明：Documents/スクリプトの説明/Game/UI/TeamScoreFlameEffect.md
/// </summary>
[RequireComponent(typeof(TeamScoreBarUI))]
public sealed class TeamScoreFlameEffect : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("ただの色の帯を出すか（炎の層があるときだけ効く）。再生中に切り替えてよい")]
    [SerializeField] private bool showSolid = true;

    [Tooltip("炎を出すか。再生中に切り替えてよい")]
    [FormerlySerializedAs("fireEnabled")]
    [SerializeField] private bool showFire = true;

    [Tooltip("炎の不透明度（0＝見えない、1＝そのまま）")]
    [SerializeField, Range(0f, 1f)] private float fireOpacity = 1f;

    [Header("炎")]
    [Tooltip("帯ごとの炎だけの Image（左から順）。空なら帯の Image そのものを炎にする（今までの版）")]
    [SerializeField] private Image[] fireImages = new Image[TeamScoreBarUI.MaxTeams];

    [Tooltip("炎のマテリアル。空なら Image にもともと付いているマテリアルを使う")]
    [SerializeField] private Material fireMaterial;

    [Header("得点が入った瞬間")]
    [Tooltip("炎が燃え上がってから元に戻るまでの秒数")]
    [SerializeField, Min(0.05f)] private float boostSeconds = 1.0f;

    [Tooltip("数字がポンと大きくなる量（0.6 なら 1.6 倍）")]
    [SerializeField, Range(0f, 2f)] private float punchScale = 0.6f;

    [Tooltip("数字が元の大きさに戻るまでの秒数")]
    [SerializeField, Min(0.05f)] private float punchSeconds = 0.35f;

    private TeamScoreBarUI bar;

    // 帯ごとの、複製したマテリアル（_Boost を帯ごとに変えるため）
    private Material[] materials;

    // 得点の数字の、元の文字の大きさ（ポンと大きくしたあと戻すため）
    private float[] baseFontSizes;

    // チームごとの演出の残り（1 = 入った瞬間 → 0 = 終わり）
    private readonly float[] boost = new float[TeamScoreBarUI.MaxTeams];
    private readonly float[] punch = new float[TeamScoreBarUI.MaxTeams];

    private static readonly int BoostId = Shader.PropertyToID("_Boost");

    /// <summary>炎だけの層を持っているか（フレーム版）。</summary>
    private bool HasFireLayer => FireImageOf(0) != null;

    private void Awake()
    {
        bar = GetComponent<TeamScoreBarUI>();

        materials = new Material[TeamScoreBarUI.MaxTeams];
        baseFontSizes = new float[TeamScoreBarUI.MaxTeams];

        for (int slot = 0; slot < TeamScoreBarUI.MaxTeams; slot++)
        {
            // 炎を描く Image（炎の層があればそちら、無ければ帯そのもの）のマテリアルを帯ごとに複製する
            Image target = HasFireLayer ? FireImageOf(slot) : bar.SegmentOfSlot(slot);
            Material source = fireMaterial != null ? fireMaterial : target != null ? target.material : null;
            if (target != null && source != null)
            {
                materials[slot] = new Material(source);
                target.material = materials[slot];
            }

            TMP_Text label = bar.ScoreTextOfSlot(slot);
            baseFontSizes[slot] = label != null ? label.fontSize : 0f;
        }
    }

    private void OnEnable()
    {
        if (bar == null)
        {
            bar = GetComponent<TeamScoreBarUI>();
        }

        bar.ScoreChanged += OnScoreChanged;
    }

    private void OnDisable()
    {
        bar.ScoreChanged -= OnScoreChanged;
    }

    private void OnDestroy()
    {
        if (materials == null)
        {
            return;
        }

        foreach (Material material in materials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    /// <summary>得点が入ったら、そのチームの演出を最初から始める。減ったとき（取られたなど）は何もしない。</summary>
    private void OnScoreChanged(int team, int score, int diff)
    {
        if (diff <= 0 || team < 0 || team >= boost.Length)
        {
            return;
        }

        boost[team] = 1f;
        punch[team] = 1f;
    }

    // 得点バーが Update で帯と数字を決めたあとに、炎と数字の大きさを合わせる
    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        for (int team = 0; team < boost.Length; team++)
        {
            boost[team] = Mathf.Max(0f, boost[team] - dt / boostSeconds);
            punch[team] = Mathf.Max(0f, punch[team] - dt / punchSeconds);
        }

        for (int slot = 0; slot < TeamScoreBarUI.MaxTeams; slot++)
        {
            int team = TeamAtSlot(slot);

            if (HasFireLayer)
            {
                ApplyLayered(slot, team);
            }
            else
            {
                ApplySingle(slot, team);
            }

            // 数字の文字の大きさ（ポンと大きくして戻す）
            TMP_Text label = bar.ScoreTextOfSlot(slot);
            if (team >= 0 && label != null && baseFontSizes[slot] > 0f)
            {
                label.fontSize = baseFontSizes[slot] * (1f + punchScale * EaseOut(punch[team]));
            }
        }
    }

    /// <summary>炎の層があるとき：色の帯と炎を別々に出し、炎を帯と同じ横幅に合わせる。</summary>
    private void ApplyLayered(int slot, int team)
    {
        Image segment = bar.SegmentOfSlot(slot);
        Image fire = FireImageOf(slot);
        bool used = team >= 0 && segment != null && segment.gameObject.activeSelf;

        if (segment != null)
        {
            segment.enabled = showSolid;
        }

        if (fire == null)
        {
            return;
        }

        bool fireShown = used && showFire && fireOpacity > 0f;
        if (fire.gameObject.activeSelf != fireShown)
        {
            fire.gameObject.SetActive(fireShown);
        }

        if (!fireShown)
        {
            return;
        }

        // 横は帯と同じ割合（炎の層の四角の中で）、縦は炎の層いっぱい
        RectTransform rect = fire.rectTransform;
        rect.anchorMin = new Vector2(segment.rectTransform.anchorMin.x, 0f);
        rect.anchorMax = new Vector2(segment.rectTransform.anchorMax.x, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Color color = bar.TeamColorOf(team);
        color.a = fireOpacity;
        fire.color = color;

        if (materials[slot] != null)
        {
            materials[slot].SetFloat(BoostId, EaseOut(boost[team]));
        }
    }

    /// <summary>炎の層が無いとき（今までの版）：帯の Image に炎のマテリアルを付ける・外す。</summary>
    private void ApplySingle(int slot, int team)
    {
        Image segment = bar.SegmentOfSlot(slot);
        if (segment == null || team < 0)
        {
            return;
        }

        Material wanted = showFire ? materials[slot] : null;
        if (segment.material != (wanted != null ? wanted : segment.defaultMaterial))
        {
            segment.material = wanted;
        }

        if (showFire)
        {
            Color color = segment.color;
            color.a = fireOpacity;
            segment.color = color;

            if (materials[slot] != null)
            {
                materials[slot].SetFloat(BoostId, EaseOut(boost[team]));
            }
        }
    }

    /// <summary>左から slot 番目の炎だけの Image。無ければ null。</summary>
    private Image FireImageOf(int slot)
    {
        return fireImages != null && slot >= 0 && slot < fireImages.Length ? fireImages[slot] : null;
    }

    /// <summary>左から slot 番目の帯に出ているチーム。無ければ -1。</summary>
    private int TeamAtSlot(int slot)
    {
        for (int team = 0; team < bar.TeamCount; team++)
        {
            if (bar.SlotOfTeam(team) == slot)
            {
                return team;
            }
        }

        return -1;
    }

    /// <summary>1→0 へ減る値を、はじめは大きいまま・終わりで素早く消えるように曲げる。</summary>
    private static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }
}
