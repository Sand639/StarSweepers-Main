using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 得点が入ったチームの帯の炎を燃え上がらせ、得点の数字をポンと大きくする（得点バーと同じ物に付ける）。
/// 説明：Documents/スクリプトの説明/Game/UI/TeamScoreFlameEffect.md
/// </summary>
[RequireComponent(typeof(TeamScoreBarUI))]
public sealed class TeamScoreFlameEffect : MonoBehaviour
{
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

    private void Awake()
    {
        bar = GetComponent<TeamScoreBarUI>();

        materials = new Material[TeamScoreBarUI.MaxTeams];
        baseFontSizes = new float[TeamScoreBarUI.MaxTeams];

        for (int slot = 0; slot < TeamScoreBarUI.MaxTeams; slot++)
        {
            // 元のマテリアルを直接書き換えると、全部の帯が同じ動きになるので帯ごとに複製する
            Image segment = bar.SegmentOfSlot(slot);
            if (segment != null && segment.material != null)
            {
                materials[slot] = new Material(segment.material);
                segment.material = materials[slot];
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

    // 得点バーが Update で帯と数字を決めたあとに、燃え上がりの強さと数字の大きさを入れる
    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        for (int team = 0; team < boost.Length; team++)
        {
            boost[team] = Mathf.Max(0f, boost[team] - dt / boostSeconds);
            punch[team] = Mathf.Max(0f, punch[team] - dt / punchSeconds);

            int slot = bar.SlotOfTeam(team);
            if (slot < 0)
            {
                continue;
            }

            if (materials[slot] != null)
            {
                materials[slot].SetFloat(BoostId, EaseOut(boost[team]));
            }

            // 数字の文字の大きさ（ポンと大きくして戻す）
            TMP_Text label = bar.ScoreTextOfSlot(slot);
            if (label != null && baseFontSizes[slot] > 0f)
            {
                label.fontSize = baseFontSizes[slot] * (1f + punchScale * EaseOut(punch[team]));
            }
        }
    }

    /// <summary>1→0 へ減る値を、はじめは大きいまま・終わりで素早く消えるように曲げる。</summary>
    private static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }
}
