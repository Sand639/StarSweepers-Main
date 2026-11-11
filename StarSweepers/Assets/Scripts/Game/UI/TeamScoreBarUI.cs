using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面の上に、得点の比率で幅が変わるチームの帯と、真ん中の丸に残り時間を出す（表示だけ）。
/// 説明：Documents/スクリプトの説明/Game/UI/TeamScoreBarUI.md
/// </summary>
public sealed class TeamScoreBarUI : MonoBehaviour
{
    /// <summary>出せるチームの最大数（宇宙ごみのチームの最大数と同じ）。</summary>
    public const int MaxTeams = SpaceJunkTeams.MaxTeams;

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト。このスクリプトの付いた物は止めないため、子を切り替える")]
    [SerializeField] private GameObject content;

    [Tooltip("チームの色の帯。左から順に、最大4本")]
    [SerializeField] private Image[] segmentImages = new Image[MaxTeams];

    [Tooltip("帯の上に出す得点の数字（帯と同じ並び）。空でもよい")]
    [SerializeField] private TMP_Text[] scoreTexts = new TMP_Text[MaxTeams];

    [Tooltip("真ん中の丸の中の、残り時間の文字")]
    [SerializeField] private TMP_Text timeText;

    [Header("見た目")]
    [Tooltip("帯の幅が新しい比率に追いつく速さ。大きいほど速い。0 だとすぐ切り替わる")]
    [SerializeField, Min(0f)] private float smoothSpeed = 6f;

    [Tooltip("得点が 0 のチームでも、最低これだけの幅は残す（バー全体を 1 とした割合）。0 なら完全に消える")]
    [SerializeField, Range(0f, 0.2f)] private float minShare = 0.05f;

    [Tooltip("オンなら、チーム番号の大きい順に左から並べる（2チームなら 左＝赤・右＝青。デザイン案の並び）")]
    [SerializeField] private bool reverseOrder = true;

    [Tooltip("帯の上に得点の数字を出すか")]
    [SerializeField] private bool showScoreNumbers = true;

    [Tooltip("オンなら、下の Custom Team Colors の色を使う。オフならチームの色（SpaceJunkTeams）を使う")]
    [SerializeField] private bool useCustomColors;

    [Tooltip("チーム番号ごとの色（0=青チーム、1=赤チーム、2=緑チーム、3=黄チーム）")]
    [SerializeField] private Color[] customTeamColors =
    {
        new Color(0.05f, 0.55f, 1f),
        new Color(1f, 0.33f, 0.33f),
        new Color(0.45f, 0.85f, 0.45f),
        new Color(0.95f, 0.80f, 0.30f),
    };

    [Tooltip("残り時間がこの秒数を切ったら、時間の文字を警告の色にする")]
    [SerializeField, Min(0f)] private float warnSeconds = 10f;

    [SerializeField] private Color timeColor = Color.white;
    [SerializeField] private Color warnTimeColor = new Color(1f, 0.45f, 0.4f);

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くても下の値で表示する。検証シーン用。本番のステージではオフにする")]
    [SerializeField] private bool testMode;

    [Tooltip("テストのチーム数")]
    [SerializeField, Range(1, MaxTeams)] private int testTeamCount = 2;

    [Tooltip("テストの得点（チーム番号順）。再生中に書き換えると、バーがその比率へ動く")]
    [SerializeField] private int[] testScores = new int[MaxTeams];

    [Tooltip("テストの制限時間（秒）。再生すると、ここから減っていく")]
    [SerializeField, Min(0f)] private float testRoundSeconds = 120f;

    [Tooltip("オンなら、一定の間隔でランダムなチームに 1 点ずつ足す（動きを見るため）")]
    [SerializeField] private bool testAutoScore = true;

    [Tooltip("自動で得点を足す間隔（秒）")]
    [SerializeField, Min(0.1f)] private float testAutoScoreInterval = 1.5f;

    /// <summary>得点が変わったときに呼ぶ（チーム番号, 新しい得点, 増えた量）。</summary>
    public event Action<int, int, int> ScoreChanged;

    /// <summary>いま表示しているチーム数。</summary>
    public int TeamCount { get; private set; }

    /// <summary>画面の上から時間の丸の下の端までのピクセル数。出していなければ 0（SpaceJunkMatchUI が見る）。</summary>
    public static float OccupiedScreenHeight { get; private set; }

    /// <summary>いま画面に出している帯の幅（チーム番号順。バー全体を 1 とした割合。なめらかに動いている途中の値）。</summary>
    public float DisplayedShareOf(int team) => team >= 0 && team < MaxTeams ? displayedShares[team] : 0f;

    /// <summary>そのチームの得点（いま表示している値）。</summary>
    public int ScoreOf(int team) => team >= 0 && team < MaxTeams ? lastScores[team] : 0;

    /// <summary>そのチームが、左から何番目の帯に出ているか。出ていなければ -1。</summary>
    public int SlotOfTeam(int team)
    {
        if (team < 0 || team >= TeamCount)
        {
            return -1;
        }

        return reverseOrder ? TeamCount - 1 - team : team;
    }

    /// <summary>左から slot 番目の帯の Image。無ければ null。</summary>
    public Image SegmentOfSlot(int slot) => slot >= 0 && slot < segmentImages.Length ? segmentImages[slot] : null;

    /// <summary>左から slot 番目の帯の、得点の数字。無ければ null。</summary>
    public TMP_Text ScoreTextOfSlot(int slot) => slot >= 0 && slot < scoreTexts.Length ? scoreTexts[slot] : null;

    /// <summary>そのチームの色（バーの帯と同じ色）。</summary>
    public Color TeamColorOf(int team) => ColorOf(team);

    // 表示中の帯の幅（なめらかに目標へ近づける）
    private readonly float[] displayedShares = new float[MaxTeams];

    // 目指している帯の幅
    private readonly float[] targetShares = new float[MaxTeams];

    // 前のフレームの得点（増えたかどうかを見るため）
    private readonly int[] lastScores = new int[MaxTeams];

    // 今回のフレームで読んだ得点
    private readonly int[] currentScores = new int[MaxTeams];

    // テスト用の残り時間と、自動で得点を足すまでの時間
    private float testRemaining;
    private float testAutoTimer;

    // 前のフレームで表示していたか（出し始めの1回目は、なめらかにせずにすぐ合わせる）
    private bool wasVisible;

    private void OnEnable()
    {
        testRemaining = testRoundSeconds;
        testAutoTimer = testAutoScoreInterval;
        wasVisible = false;
    }

    private void OnDisable()
    {
        // 消えたら、仮の画面に「もう上は空いている」と伝える（シーンが切り替わったときもここを通る）
        OccupiedScreenHeight = 0f;
    }

    /// <summary>画面の上から、時間の丸の下の端までのピクセル数を測る。</summary>
    private float MeasureOccupiedHeight()
    {
        RectTransform circle = timeText != null ? timeText.rectTransform.parent as RectTransform : null;
        if (circle == null)
        {
            return 0f;
        }

        var corners = new Vector3[4];
        circle.GetWorldCorners(corners);

        // corners[0] が左下。画面の座標は下が 0 なので、上からの距離に直す
        return Mathf.Max(0f, Screen.height - corners[0].y);
    }

    private void Update()
    {
        if (!TryReadValues(out int teamCount, out float remaining))
        {
            SetVisible(false);
            wasVisible = false;
            OccupiedScreenHeight = 0f;
            return;
        }

        SetVisible(true);
        OccupiedScreenHeight = MeasureOccupiedHeight();

        bool snap = !wasVisible || teamCount != TeamCount;
        TeamCount = teamCount;

        NotifyScoreChanges(snap);
        UpdateShares(snap);
        ApplyLayout();
        ApplyTime(remaining);

        wasVisible = true;
    }

    /// <summary>ラウンドかテスト用の値から、出す値を読む。どちらも無ければ false。</summary>
    private bool TryReadValues(out int teamCount, out float remaining)
    {
        SpaceJunkRound round = SpaceJunkRound.Current;

        // 宇宙ごみの対戦中（得点制のときだけ。3種類ルールは得点が無いので出さない）
        if (round != null)
        {
            teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;
            remaining = round.RemainingSeconds;

            for (int team = 0; team < MaxTeams; team++)
            {
                currentScores[team] = team < teamCount ? round.ScoreOf(team) : 0;
            }

            return round.Rule == SpaceJunkWinRule.Score;
        }

        teamCount = 0;
        remaining = 0f;

        if (!testMode)
        {
            return false;
        }

        // ---- ここから下はテストモード ----
        teamCount = Mathf.Clamp(testTeamCount, 1, MaxTeams);

        // 時間を減らす（0 になったら制限時間に戻して、もう一周）
        testRemaining -= Time.deltaTime;
        if (testRemaining <= 0f)
        {
            testRemaining = testRoundSeconds;
        }

        remaining = testRemaining;

        // 一定の間隔で、ランダムなチームに 1 点
        if (testAutoScore)
        {
            testAutoTimer -= Time.deltaTime;
            if (testAutoTimer <= 0f)
            {
                testAutoTimer = testAutoScoreInterval;
                EnsureTestScoresLength();
                testScores[UnityEngine.Random.Range(0, teamCount)]++;
            }
        }

        EnsureTestScoresLength();

        for (int team = 0; team < MaxTeams; team++)
        {
            currentScores[team] = team < teamCount ? Mathf.Max(0, testScores[team]) : 0;
        }

        return true;
    }

    /// <summary>インスペクターで配列の長さを変えられても困らないよう、テストの得点を 4 チームぶんにそろえる。</summary>
    private void EnsureTestScoresLength()
    {
        if (testScores == null || testScores.Length < MaxTeams)
        {
            Array.Resize(ref testScores, MaxTeams);
        }
    }

    /// <summary>得点が変わったチームがあれば <see cref="ScoreChanged"/> を呼ぶ。出し始め（snap）は呼ばずに覚えるだけ。</summary>
    private void NotifyScoreChanges(bool snap)
    {
        for (int team = 0; team < MaxTeams; team++)
        {
            int score = currentScores[team];
            int diff = score - lastScores[team];
            lastScores[team] = score;

            if (!snap && diff != 0 && team < TeamCount)
            {
                ScoreChanged?.Invoke(team, score, diff);
            }
        }
    }

    /// <summary>得点から帯の幅の目標を出し、表示中の幅をそこへ近づける。</summary>
    private void UpdateShares(bool snap)
    {
        int total = 0;
        for (int team = 0; team < TeamCount; team++)
        {
            total += currentScores[team];
        }

        // 最低幅を全チームぶん取ると 1 を超えてしまう場合は、等分を上限にする
        float floor = Mathf.Min(minShare, 1f / TeamCount);
        float rest = 1f - floor * TeamCount;

        for (int team = 0; team < MaxTeams; team++)
        {
            if (team >= TeamCount)
            {
                targetShares[team] = 0f;
                continue;
            }

            float raw = total > 0 ? (float)currentScores[team] / total : 1f / TeamCount;
            targetShares[team] = floor + rest * raw;
        }

        // なめらかに近づける（フレームの速さに関係なく同じ速さで動くよう、指数で近づける）
        // ポーズ中でも表示は動かしたいので、止まらない時間（unscaledDeltaTime）を使う
        float t = snap || smoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime);

        for (int team = 0; team < MaxTeams; team++)
        {
            displayedShares[team] = Mathf.Lerp(displayedShares[team], targetShares[team], t);
        }
    }

    /// <summary>帯を左から並べ、色と得点の数字を合わせる。</summary>
    private void ApplyLayout()
    {
        // 幅の合計（近づける途中で 1 からずれることがあるので、割って 1 にそろえる）
        float sum = 0f;
        for (int team = 0; team < TeamCount; team++)
        {
            sum += displayedShares[team];
        }

        if (sum <= 0f)
        {
            sum = 1f;
        }

        float x = 0f;

        for (int slot = 0; slot < MaxTeams; slot++)
        {
            Image segment = slot < segmentImages.Length ? segmentImages[slot] : null;
            TMP_Text label = slot < scoreTexts.Length ? scoreTexts[slot] : null;
            bool used = slot < TeamCount;

            if (segment != null)
            {
                segment.gameObject.SetActive(used);
            }

            if (label != null)
            {
                label.gameObject.SetActive(used && showScoreNumbers);
            }

            if (!used)
            {
                continue;
            }

            // 左から何番目の帯に、どのチームを出すか
            int team = reverseOrder ? TeamCount - 1 - slot : slot;
            float width = displayedShares[team] / sum;

            if (segment != null)
            {
                RectTransform rect = segment.rectTransform;
                rect.anchorMin = new Vector2(x, 0f);
                rect.anchorMax = new Vector2(x + width, 1f);

                // 横の余白だけ 0 にする。縦の余白はプレハブのまま残す
                // （帯の Image は、炎が上へ立ちのぼるぶん、上に高くしてあるため）
                rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
                rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
                segment.color = ColorOf(team);
            }

            if (label != null && showScoreNumbers)
            {
                label.text = lastScores[team].ToString();

                // 文字の中身はチームの色（赤チームなら赤）。外側の黒い縁取りは文字のマテリアル（TeamScoreNumber.mat）で付けている
                label.color = ColorOf(team);

                // 左端の帯は左寄せ、右端の帯は右寄せ（真ん中の時間の丸に隠れないように）
                label.alignment = slot == 0 && TeamCount > 1
                    ? TextAlignmentOptions.MidlineLeft
                    : slot == TeamCount - 1 && TeamCount > 1
                        ? TextAlignmentOptions.MidlineRight
                        : TextAlignmentOptions.Midline;
            }

            x += width;
        }
    }

    /// <summary>真ん中の丸に、残り時間を「分:秒」で出す。</summary>
    private void ApplyTime(float remaining)
    {
        if (timeText == null)
        {
            return;
        }

        // 切り上げて出す（残り 0.3 秒は「0:01」。0:00 になった瞬間に時間切れになるように）
        int total = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        timeText.text = $"{total / 60}:{total % 60:00}";
        timeText.color = remaining <= warnSeconds ? warnTimeColor : timeColor;
    }

    /// <summary>チームの色。Use Custom Colors がオンならインスペクターの色、オフならチーム共通の色。</summary>
    private Color ColorOf(int team)
    {
        if (useCustomColors && customTeamColors != null && team < customTeamColors.Length)
        {
            return customTeamColors[team];
        }

        return SpaceJunkTeams.TeamColor(team);
    }

    private void SetVisible(bool visible)
    {
        if (content != null && content.activeSelf != visible)
        {
            content.SetActive(visible);
        }
    }
}
