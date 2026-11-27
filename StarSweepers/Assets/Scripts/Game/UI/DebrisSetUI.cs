using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドイベント「デブリセット」のお題を、腕が持ってくる枠にカードで並べて出す
/// （腕が画面の外から入る → 枠が開く → カードが上から落ちてくる → 入れた物資にチェック）。
/// 説明：Documents/スクリプトの説明/Game/UI/DebrisSetUI.md
/// </summary>
public sealed class DebrisSetUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/DebrisSetUI.prefab）。</summary>
    public const string PrefabResourcePath = "DebrisSetUI";

    /// <summary>出せるチームの最大数。</summary>
    public const int MaxTeams = SpaceJunkTeams.MaxTeams;

    /// <summary>1チームのお題の、いちばん多い数（並べるカードの数）。</summary>
    public const int MaxSlots = SpaceJunkRound.MaxSetSize;

    /// <summary>物資の種類の数（物資1〜5の画像）。</summary>
    public const int ItemKinds = SpaceJunkMaterials.Count;

    // スロットの並びの中で「入れ終わったカード（白いカード＋チェック）」を表す印。物資の番号にこれを足す
    private const int CollectedCode = 100;

    /// <summary>カード1枚の部品（重なり順は 奥から カード → 物資 → 白いカード → チェック）。</summary>
    [Serializable]
    public sealed class CardParts
    {
        [Tooltip("このカードごと動かす物")]
        public RectTransform root;

        [Tooltip("カードごと薄くするための物")]
        public CanvasGroup group;

        [Tooltip("カードの地の画像（物資の色に染める）")]
        public Image background;

        [Tooltip("物資の画像")]
        public Image item;

        [Tooltip("入れた物資の上にかぶせる白いカード")]
        public Image cover;

        [Tooltip("入れた物資の上に出すチェックマーク")]
        public Image check;
    }

    /// <summary>お題の1マスの部品。スロット中は2枚のカードを上から下へ流す。</summary>
    [Serializable]
    public sealed class SlotParts
    {
        [Tooltip("このマスごと出し入れする物")]
        public RectTransform root;

        [Tooltip("いま真ん中にあるカード（止まったらお題のカード）")]
        public CardParts card;

        [Tooltip("スロット中だけ使う、上から降りてくる次のカード")]
        public CardParts nextCard;
    }

    /// <summary>1チームぶんの部品。</summary>
    [Serializable]
    public sealed class PanelParts
    {
        [Tooltip("このチームの表示ごと出し入れする物（画面の角に付けてある）")]
        public GameObject root;

        [Tooltip("腕・枠・カードをまとめて回す・動かす物（腕が回りながら入ってくる演出に使う）")]
        public RectTransform body;

        [Tooltip("腕（左の金具つき）の画像")]
        public Image arm;

        [Tooltip("腕の棒（画面の外の付け根から、腕の金具までの斜めの棒）の画像。腕とは別に回る")]
        public Image rod;

        [Tooltip("お題を並べる枠の画像")]
        public Image panel;

        [Tooltip("枠の反対側の金具の画像")]
        public Image bracket;

        [Tooltip("「特注依頼…」の文字")]
        public Text title;

        [Tooltip("お題のマス。左から順に、最大8つ")]
        public SlotParts[] slots = new SlotParts[MaxSlots];
    }

    [Header("素材（Assets/Art/Sprites/EventUI/SetDebris）")]
    [Tooltip("物資の画像。上から 物資1＝装甲板、物資2＝回路基板、物資3＝燃料タンク、物資4＝アンテナ、物資5＝エンジン")]
    [SerializeField] private Sprite[] itemSprites = new Sprite[ItemKinds];

    [Tooltip("オンなら、カードの地を物資の色（ゲームの中の物資の色）に染める。物資の画像の本素材が来るまでの目印")]
    [SerializeField] private bool tintCardByItem = true;

    [Tooltip("カードの地の画像（染めないとき）")]
    [SerializeField] private Sprite cardSprite;

    [Tooltip("物資の色に染めるときのカードの地の画像。色がそのまま出るよう、白い画像を使う")]
    [SerializeField] private Sprite plainCardSprite;

    [Tooltip("文字のフォント。空ならパソコンに入っている日本語のフォントを使う（デザイナーさんの指定のフォントが来たら入れる）")]
    [SerializeField] private Font titleFont;

    [Tooltip("枠の上に出す文字（後ろに点が増えたり減ったりして付く）")]
    [SerializeField] private string titleText = "特注依頼";

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Tooltip("チームの表示。0＝左上、1＝右上（左右反対向き）、2＝左の2段目、3＝右の2段目")]
    [SerializeField] private PanelParts[] panels = new PanelParts[MaxTeams];

    [Header("置き方")]
    [Tooltip("オンなら、チームのゴールが画面の左右どちらに見えるかで、枠を置く側を決める（カメラを切り替えても合わせる）")]
    [SerializeField] private bool followGoalSide = true;

    [Tooltip("ゴールで決められないとき（検証シーン、2つのゴールが同じくらいの横位置に見えるとき）の並び。オンなら 左＝赤チーム・右＝青チーム")]
    [SerializeField] private bool swapLeftRight = true;

    [Header("並べ方（画像の元の大きさのピクセル。枠ごとの大きさは Panel0〜3 の Scale で変える）")]
    [Tooltip("腕の画像の左端の位置。腕・棒・枠の位置は、どれも棒の左端（腕と棒が1枚の画像だったころの左端）から測る")]
    [SerializeField] private float armImageLeft = 27f;

    [Tooltip("棒の画像の中心の位置（棒の左端から。下はマイナス）")]
    [SerializeField] private Vector2 rodCenter = new Vector2(25.5f, -103.5f);

    [Tooltip("棒の左端から、枠の左端までの距離（枠の左端が腕の金具の下に入る）")]
    [SerializeField] private float panelLeft = 72f;

    [Tooltip("枠の中心の、上からの距離")]
    [SerializeField] private float bodyCenterY = 82f;

    [Tooltip("閉じているときに見える、枠の幅（腕の金具と反対側の金具の間）。腕はこの閉じた形で入ってくる")]
    [SerializeField] private float closedWidth = 24f;

    [Tooltip("枠の右端に、反対側の金具がどれだけかぶるか")]
    [SerializeField] private float bracketOverlap = 12f;

    [Tooltip("枠の左端から1枚目のカードまでのすき間")]
    [SerializeField] private float cardMargin = 11f;

    [Tooltip("カードの横の間隔（カードの幅＋すき間）。枠の横幅は「すき間＋この値×お題の数」になる")]
    [SerializeField] private float cardPitch = 112f;

    [Tooltip("カードの中心の、上からの距離")]
    [SerializeField] private float cardCenterY = 95f;

    [Tooltip("スロット中に流れるカードの縦の間隔")]
    [SerializeField] private float reelStep = 124f;

    [Tooltip("文字の左上の位置（枠の左上から）")]
    [SerializeField] private Vector2 titleOffset = new Vector2(16f, 4f);

    [Tooltip("入ってくる前の腕が、画面の外へどれだけ下がっているか")]
    [SerializeField] private float armHideDistance = 160f;

    [Tooltip("棒と腕のつなぎ目（オレンジの金具の中）の位置。腕・枠・カードも棒も、ここを中心に回る")]
    [SerializeField] private Vector2 armJoint = new Vector2(40f, -82f);

    [Tooltip("入ってくるときの腕の傾き（度。プラス＝上の端が画面の外の側へ傾く）。まっすぐに起き上がってから、反対側へ揺れて落ち着く")]
    [SerializeField] private float armSwingAngle = 25f;

    [Tooltip("起き上がったあと、反対側へ揺れる傾き（度。0＝揺れない）")]
    [SerializeField, Min(0f)] private float armOvershootAngle = 20f;

    [Tooltip("腕が入りきって、まっすぐに起き上がる時刻（Arm Slide Seconds に対する割合）。そのあとは揺れて落ち着くだけ")]
    [SerializeField, Range(0.1f, 0.9f)] private float armUprightRatio = 0.45f;

    [Tooltip("入ってくるときの棒の向き（度。画像の向きから。プラス＝下へ垂れる）")]
    [SerializeField] private float rodStartAngle = 30f;

    [Tooltip("落ち着いたときの棒の向き（度。画像の向きから。-45 で水平になり、画面の外へのびる）")]
    [SerializeField] private float rodRestAngle = -45f;

    [Header("出てくる・しまう演出")]
    [Tooltip("腕が画面の外から入ってきて、揺れが落ち着くまでの秒数")]
    [SerializeField, Min(0f)] private float armSlideSeconds = 0.7f;

    [Tooltip("枠が開く秒数（反対側の金具が外へ動き、枠が伸びる）。開ききってからカードが落ちてくる")]
    [SerializeField, Min(0f)] private float openSeconds = 0.25f;

    [Tooltip("イベントが終わったとき、カードが消える秒数（そのあと枠が閉じて腕が出ていく）")]
    [SerializeField, Min(0f)] private float cardFadeSeconds = 0.15f;

    [Tooltip("チェックが大きく出てから元の大きさに落ち着くまでの秒数")]
    [SerializeField, Min(0.01f)] private float checkPopSeconds = 0.25f;

    [Tooltip("チェックが出た瞬間の大きさ（1＝元の大きさ）")]
    [SerializeField, Min(1f)] private float checkPopScale = 1.8f;

    [Tooltip("文字の後ろの点が1つ増える（減る）秒数")]
    [SerializeField, Min(0.05f)] private float dotSeconds = 0.2f;

    [Tooltip("文字の後ろの点の、いちばん多い数（1個からこの数まで増えて、また減る）")]
    [SerializeField, Range(1, 12)] private int maxDots = 6;

    [Header("スロットの演出")]
    [Tooltip("いちばん右のカードが止まるまでの秒数。スロットは、ちょうどこの秒数で全部止まる")]
    [SerializeField, Min(0f)] private float spinSeconds = 1f;

    [Tooltip("いちばん左のカードが止まる時刻（Spin Seconds に対する割合）。そこから右へ、等しい間隔で順に止まる")]
    [SerializeField, Range(0f, 1f)] private float firstStopRatio = 0.5f;

    [Tooltip("回り始めの速さ（1秒に何枚のカードが流れるか）。止まる時刻に向けて、だんだん遅くなる")]
    [SerializeField, Min(1f)] private float spinStartSpeed = 10f;

    [Tooltip("お題をそろえて次のお題が出たときにも、スロットを回すか（チェックの付いたカードが下へ流れ、次のお題が上から来る）")]
    [SerializeField] private bool spinOnNewSet = true;

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くても下の値で出す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    [Tooltip("テストで、イベントが起きていることにするか。再生中にオフにすると、しまう演出が見られる。オンに戻すと最初から")]
    [SerializeField] private bool testEventRunning = true;

    [Tooltip("テストのチーム数")]
    [SerializeField, Range(1, MaxTeams)] private int testTeamCount = 2;

    [Tooltip("テストのお題の物資の数")]
    [SerializeField, Range(1, MaxSlots)] private int testSetSize = 3;

    [Tooltip("テストで、何個入れたらそろうか（そろうと次のお題を引いて、スロットを回す）")]
    [SerializeField, Min(1)] private int testGoal = 3;

    [Tooltip("オンなら、お題の左から順に入れたことにする（本番の Debris Set Ordered と同じ）")]
    [SerializeField] private bool testOrdered;

    [Tooltip("オンなら、一定の間隔で、どこかのチームが物資を1個入れたことにする")]
    [SerializeField] private bool testAutoCollect = true;

    [Tooltip("物資を1個入れたことにする間隔（秒）")]
    [SerializeField, Min(0.1f)] private float testCollectInterval = 0.8f;

    /// <summary>いま動いている係（ゲーム中は1つだけ）。</summary>
    public static DebrisSetUI Instance { get; private set; }

    /// <summary>いま画面にお題を出しているか（SpaceJunkMatchUI が、同じお題の文字の行を出さないために見る）。</summary>
    public static bool IsShowing => Instance != null && Instance.content != null && Instance.content.activeSelf;

    // チームごとの、画面に出しているお題とスロットの状態
    private sealed class TeamState
    {
        public int size;
        public readonly int[] kinds = new int[MaxSlots];
        public readonly bool[] collected = new bool[MaxSlots];
        public readonly float[] collectedAt = new float[MaxSlots];
        public int version;
        public bool seen;
        public float introStart;
        public float spinStart;
        public readonly int[][] reels = new int[MaxSlots][];

        public TeamState()
        {
            Reset();
        }

        public void Reset()
        {
            size = 0;
            version = -1;
            seen = false;
            introStart = float.NegativeInfinity;
            spinStart = float.NegativeInfinity;
            Array.Clear(collected, 0, MaxSlots);
            Array.Clear(reels, 0, MaxSlots);
            for (int n = 0; n < MaxSlots; n++)
            {
                collectedAt[n] = float.NegativeInfinity;
            }
        }
    }

    private readonly TeamState[] states = new TeamState[MaxTeams];
    private readonly int[] readKinds = new int[MaxSlots];
    private readonly int[] readProgress = new int[ItemKinds];
    private readonly bool[] readCollected = new bool[MaxSlots];
    private readonly int[] kindBuffer = new int[ItemKinds];
    private readonly System.Random random = new System.Random();
    private bool hasState;
    private int shownTeams;

    // しまう演出を始めた時刻（しまっていなければ NaN）
    private float closeStart = float.NaN;

    // 段（上＝チーム0と1、下＝チーム2と3）ごとに、左右を入れ替えているか
    private readonly bool[] rowSwapped = new bool[MaxTeams / 2];
    private bool sidesDecided;

    // 2つのゴールの横位置がこれ以上（画面の幅に対する割合）離れていないときは、左右を切り替えない（ちらつかないように）
    private const float SideMargin = 0.1f;

    // テスト用のお題
    private readonly int[,] testKinds = new int[MaxTeams, MaxSlots];
    private readonly int[] testSizes = new int[MaxTeams];
    private readonly bool[,] testCollected = new bool[MaxTeams, MaxSlots];
    private readonly int[] testVersions = new int[MaxTeams];
    private bool testStarted;
    private float testEventStart;
    private float testNextAction;

    /// <summary>腕が入ってきてから、カードが落ち始めるまでの秒数。</summary>
    private float IntroSeconds => armSlideSeconds + openSeconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        // シーンに置かれていれば、それを使う（検証シーン）
        if (Instance != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[デブリセットUI] Resources/{PrefabResourcePath}.prefab が無いので、お題の画像は出ません。");
            return;
        }

        GameObject created = Instantiate(prefab);
        created.name = "DebrisSetUI";
        DontDestroyOnLoad(created);
    }

    private void Awake()
    {
        // 2つ目は使わない（ステージに置かれていた場合など）
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureStates();
        ApplyFont();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        Tick(Time.unscaledTime);
    }

    /// <summary>今の時刻（秒）での表示に合わせる。</summary>
    private void Tick(float now)
    {
        if (!TryRead(now, out int teamCount))
        {
            TickClosing(now);
            return;
        }

        // しまう途中でまたイベントが始まったら、最初から出し直す
        if (!float.IsNaN(closeStart))
        {
            closeStart = float.NaN;
            ResetStates();
            TryRead(now, out teamCount);
        }

        SetVisible(true);

        // 出てきた瞬間（前のフレームまで出ていなかった）に、腕が入ってくる機械の音を鳴らす
        if (!hasState)
        {
            GameAudio.PlaySe(GameSe.UiDebrisSet);
        }

        hasState = true;
        shownTeams = teamCount;
        UpdateSides(SpaceJunkRound.Current != null);
        DrawAll(now, float.NaN);
    }

    /// <summary>イベントが終わったあと：しまう演出をしてから隠す。</summary>
    private void TickClosing(float now)
    {
        // 表示していなかった・ラウンドごと無くなった（シーンが変わった）ときは、すぐ隠す
        bool canAnimate = hasState && content != null && content.activeSelf && (SpaceJunkRound.Current != null || testMode);
        if (!canAnimate)
        {
            closeStart = float.NaN;
            SetVisible(false);
            ResetStates();
            return;
        }

        if (float.IsNaN(closeStart))
        {
            closeStart = now;
        }

        float closing = now - closeStart;
        if (closing >= cardFadeSeconds + IntroSeconds)
        {
            closeStart = float.NaN;
            SetVisible(false);
            ResetStates();
            return;
        }

        DrawAll(now, closing);
    }

    /// <summary>全チームの表示を描く。closing はしまい始めてからの秒数（しまっていなければ NaN）。</summary>
    private void DrawAll(float now, float closing)
    {
        for (int panel = 0; panel < panels.Length; panel++)
        {
            PanelParts parts = panels[panel];
            if (parts == null || parts.root == null)
            {
                continue;
            }

            int team = TeamOfPanel(panel);
            bool used = team >= 0 && team < shownTeams;

            if (parts.root.activeSelf != used)
            {
                parts.root.SetActive(used);
            }

            if (used)
            {
                DrawPanel(parts, states[team], now, panel % 2 == 1, closing);
            }
        }
    }

    /// <summary>ラウンドかテスト用の値から、チームごとのお題を読む。出さないときは false。</summary>
    private bool TryRead(float now, out int teamCount)
    {
        SpaceJunkRound round = SpaceJunkRound.Current;

        if (round != null)
        {
            teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;
            teamCount = Mathf.Clamp(teamCount, 1, MaxTeams);

            if (round.Rule != SpaceJunkWinRule.Score ||
                round.ActiveEvent != SpaceJunkEventKind.DebrisSet ||
                !round.IsEventRunning)
            {
                return false;
            }

            float sinceEventStart = round.SecondsSinceEventStarted;

            for (int team = 0; team < teamCount; team++)
            {
                int size = round.SetSizeOf(team);
                for (int n = 0; n < size; n++)
                {
                    readKinds[n] = round.SetItemAt(team, n);
                }

                TeamState state = states[team];
                Receive(state, size, readKinds, round.SetsCompletedOf(team), now, sinceEventStart);

                for (int k = 0; k < ItemKinds; k++)
                {
                    readProgress[k] = round.SetProgressOf(team, SpaceJunkMaterials.FromIndex(k));
                }

                MarkCollected(state.kinds, state.size, round.SetOrdered, round.SetStepOf(team), readProgress, readCollected);
                UpdateCollected(state, readCollected, now);
            }

            return true;
        }

        teamCount = 0;
        if (!testMode)
        {
            return false;
        }

        if (!testEventRunning)
        {
            testStarted = false;
            return false;
        }

        teamCount = Mathf.Clamp(testTeamCount, 1, MaxTeams);
        UpdateTest(now, teamCount);
        return true;
    }

    /// <summary>読んだお題が新しければ覚えて、スロットを回し始める。</summary>
    private void Receive(TeamState state, int size, int[] kinds, int version, float now, float sinceEventStart)
    {
        bool changed = !state.seen || state.version != version || state.size != size;
        for (int n = 0; !changed && n < size; n++)
        {
            changed = state.kinds[n] != kinds[n];
        }

        if (!changed)
        {
            return;
        }

        bool first = !state.seen;
        bool completed = !first && state.version != version;

        // 前のお題を、流れ出ていくカードとして覚えておく（そろえたなら全部チェック付き）
        Span<int> outgoing = stackalloc int[MaxSlots];
        for (int n = 0; n < MaxSlots; n++)
        {
            outgoing[n] = !first && n < state.size
                ? state.kinds[n] + (completed || state.collected[n] ? CollectedCode : 0)
                : -1;
        }

        state.seen = true;
        state.version = version;
        state.size = size;
        Array.Copy(kinds, state.kinds, size);
        Array.Clear(state.collected, 0, MaxSlots);
        for (int n = 0; n < MaxSlots; n++)
        {
            state.collectedAt[n] = float.NegativeInfinity;
        }

        if (first)
        {
            // イベントの始まりの時刻にそろえる（途中から見え始めたなら、もう開いて止まっている）
            state.introStart = now - Mathf.Max(0f, sinceEventStart);
            StartSpin(state, state.introStart + IntroSeconds, now, outgoing);
        }
        else if (spinOnNewSet)
        {
            StartSpin(state, now, now, outgoing);
        }
        else
        {
            state.spinStart = float.NegativeInfinity;
        }
    }

    /// <summary>マスごとに、流れるカードの並び（最初が前のカード、最後がお題のカード）を作って回し始める。</summary>
    private void StartSpin(TeamState state, float start, float now, Span<int> outgoing)
    {
        state.spinStart = start;

        for (int n = 0; n < MaxSlots; n++)
        {
            state.reels[n] = null;
        }

        if (now - start >= spinSeconds)
        {
            return;
        }

        for (int n = 0; n < state.size; n++)
        {
            float stop = LaneStopTime(n, state.size, spinSeconds, firstStopRatio);

            // 減速の曲線（1 − (1 − t)²）の出だしの速さは「流れる数 × 2 ÷ 止まる時刻」なので、そこから流れる数を決める
            int count = Mathf.Max(2, Mathf.RoundToInt(spinStartSpeed * stop / 2f));
            var reel = new int[count + 1];
            reel[0] = outgoing[n];
            reel[count] = state.kinds[n];

            // 隣どうしが同じ物資だと流れて見えないので、違う物資にする
            for (int i = count - 1; i >= 1; i--)
            {
                reel[i] = RandomKind(reel[i + 1]);
            }

            state.reels[n] = reel;
        }
    }

    /// <summary>入れたかどうかを写し、新しく入れたマスは、チェックが出た時刻を覚える。</summary>
    private static void UpdateCollected(TeamState state, bool[] collected, float now)
    {
        for (int n = 0; n < MaxSlots; n++)
        {
            if (collected[n] && !state.collected[n])
            {
                state.collectedAt[n] = now;
            }

            state.collected[n] = collected[n];
        }
    }

    /// <summary>1チームの表示を描く。mirrored なら左右反対（腕が右）。closing はしまい始めてからの秒数（しまっていなければ NaN）。</summary>
    private void DrawPanel(PanelParts parts, TeamState state, float now, bool mirrored, float closing)
    {
        // 出てきてからの秒数。しまうときは、カードが消えたあと逆回しにする
        bool isClosing = !float.IsNaN(closing);
        float intro = isClosing ? IntroSeconds - Mathf.Max(0f, closing - cardFadeSeconds) : now - state.introStart;
        float cardAlpha = isClosing ? 1f - Clamp01Div(closing, cardFadeSeconds) : 1f;

        float slideTime = Clamp01Div(intro, armSlideSeconds);
        float open = EaseOutCubic(Clamp01Div(intro - armSlideSeconds, openSeconds));
        bool opened = open >= 1f;

        float side = mirrored ? -1f : 1f;
        float width = cardMargin + cardPitch * Mathf.Max(1, state.size);
        float left = panelLeft;

        // 腕は傾いたまま画面の外から入り、起き上がる → 反対側へ揺れる → 落ち着く。
        // 棒はつなぎ目を中心に、下へ垂れた向きから水平へ回る（腕とは別に回る）
        {
            float arrive = EaseOutCubic(Mathf.Clamp01(slideTime / armUprightRatio));
            Vector2 joint = Mirror(armJoint, side);
            Vector2 jointNow = joint + new Vector2((arrive - 1f) * armHideDistance * side, 0f);

            if (parts.rod != null)
            {
                // 棒は腕より遅れて回り始める（入り始めは下へ垂れたまま）
                float rodTurn = Mathf.SmoothStep(0f, 1f, slideTime / armUprightRatio);
                float rodAngle = Mathf.Lerp(rodStartAngle, rodRestAngle, rodTurn) * side;
                Quaternion rodRotation = Quaternion.Euler(0f, 0f, rodAngle);
                Vector2 center = jointNow + (Vector2)(rodRotation * (Mirror(rodCenter, side) - joint));
                parts.rod.rectTransform.localRotation = rodRotation;
                Place(parts.rod.rectTransform, center.x, center.y, side);
            }

            if (parts.body != null)
            {
                float headAngle = ArmSwing(slideTime, armUprightRatio, armSwingAngle, armOvershootAngle) * side;
                Quaternion headRotation = Quaternion.Euler(0f, 0f, headAngle);
                parts.body.localRotation = headRotation;
                parts.body.anchoredPosition = jointNow - (Vector2)(headRotation * joint);
            }
        }

        if (parts.arm != null)
        {
            RectTransform arm = parts.arm.rectTransform;
            Place(arm, side * (armImageLeft + arm.rect.width * 0.5f), -bodyCenterY, side);
        }

        if (parts.panel != null)
        {
            RectTransform panel = parts.panel.rectTransform;
            panel.sizeDelta = new Vector2(width, panel.sizeDelta.y);
            Place(panel, side * (left + width * 0.5f), -bodyCenterY, 1f);
            parts.panel.fillOrigin = mirrored ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            parts.panel.fillAmount = Mathf.Lerp(Mathf.Min(closedWidth, width), width, open) / width;
        }

        if (parts.bracket != null)
        {
            RectTransform bracket = parts.bracket.rectTransform;
            float x = left + Mathf.Lerp(Mathf.Min(closedWidth, width), width, open) + bracket.rect.width * 0.5f - bracketOverlap;
            Place(bracket, side * x, -bodyCenterY, side);
        }

        if (parts.title != null)
        {
            // 文字は枠の画面で見て左上（左右反対でも、文字は反転させない）
            float x = mirrored ? -(left + width) + titleOffset.x : left + titleOffset.x;
            Place(parts.title.rectTransform, x, -titleOffset.y, 1f);
            parts.title.text = titleText + new string('.', TitleDots(now - state.introStart, dotSeconds, maxDots));
            SetActive(parts.title, opened && cardAlpha > 0f);
        }

        float elapsed = now - state.spinStart;

        for (int n = 0; n < parts.slots.Length; n++)
        {
            SlotParts slot = parts.slots[n];
            if (slot == null || slot.root == null)
            {
                continue;
            }

            bool used = n < state.size && opened && cardAlpha > 0f;
            if (slot.root.gameObject.activeSelf != used)
            {
                slot.root.gameObject.SetActive(used);
            }

            if (!used)
            {
                continue;
            }

            // 画面で見て左から n 番目（左右反対の枠では、腕から遠い側が1枚目）
            int fromArm = mirrored ? state.size - 1 - n : n;
            float x = side * (left + cardMargin + cardPitch * (fromArm + 0.5f) - (cardPitch - CardWidth(slot)) * 0.5f);
            float y = -cardCenterY;

            int[] reel = state.reels[n];
            float stop = LaneStopTime(n, state.size, spinSeconds, firstStopRatio);

            if (reel != null && elapsed < stop)
            {
                // 回っている：カードを上から下へ流す
                float position = ReelPosition(elapsed, stop, reel.Length - 1);
                int index = Mathf.Clamp(Mathf.FloorToInt(position), 0, reel.Length - 1);
                float fraction = position - index;

                DrawCard(slot.card, reel[index], x, y - fraction * reelStep, cardAlpha, 1f);
                DrawCard(slot.nextCard, reel[Mathf.Min(index + 1, reel.Length - 1)], x, y + (1f - fraction) * reelStep, cardAlpha, 1f);
            }
            else
            {
                // 止まっている：お題のカードと、入れたかどうか
                int code = state.kinds[n] + (state.collected[n] ? CollectedCode : 0);
                float pop = Clamp01Div(now - state.collectedAt[n], checkPopSeconds);
                DrawCard(slot.card, code, x, y, cardAlpha, pop);
                DrawCard(slot.nextCard, -1, x, y, cardAlpha, 1f);
            }
        }
    }

    /// <summary>カードを1枚描く。code は物資の番号（入れ終わったカードは +100、-1 なら出さない）。pop はチェックの出方（0＝出た瞬間、1＝落ち着いた）。</summary>
    private void DrawCard(CardParts card, int code, float x, float y, float alpha, float pop)
    {
        if (card == null || card.root == null)
        {
            return;
        }

        bool shown = code >= 0;
        if (card.root.gameObject.activeSelf != shown)
        {
            card.root.gameObject.SetActive(shown);
        }

        if (!shown)
        {
            return;
        }

        int kind = code % CollectedCode;
        bool collected = code >= CollectedCode;

        card.root.anchoredPosition = new Vector2(x, y);
        if (card.group != null)
        {
            card.group.alpha = alpha;
        }

        if (card.item != null)
        {
            card.item.sprite = kind < itemSprites.Length ? itemSprites[kind] : null;
        }

        if (card.background != null)
        {
            bool tint = tintCardByItem && plainCardSprite != null && kind < ItemKinds;
            card.background.sprite = tint ? plainCardSprite : cardSprite != null ? cardSprite : card.background.sprite;
            card.background.color = tint ? SpaceJunkMaterials.Color(SpaceJunkMaterials.FromIndex(kind)) : Color.white;
        }

        SetActive(card.cover, collected);
        SetActive(card.check, collected);

        if (!collected)
        {
            return;
        }

        float eased = EaseOutCubic(pop);
        if (card.cover != null)
        {
            Color color = card.cover.color;
            color.a = eased;
            card.cover.color = color;
        }

        if (card.check != null)
        {
            float scale = Mathf.Lerp(checkPopScale, 1f, eased);
            card.check.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private static float CardWidth(SlotParts slot)
    {
        return slot.card != null && slot.card.root != null ? slot.card.root.rect.width : 0f;
    }

    private static void Place(RectTransform rect, float x, float y, float flipX)
    {
        rect.anchoredPosition = new Vector2(x, y);
        Vector3 scale = rect.localScale;
        scale.x = Mathf.Abs(scale.x) * flipX;
        rect.localScale = scale;
    }

    /// <summary>左右反対（side が -1）なら、横の位置を反対にする。</summary>
    private static Vector2 Mirror(Vector2 point, float side)
    {
        return new Vector2(side * point.x, point.y);
    }

    private static float Clamp01Div(float value, float duration)
    {
        return duration <= 0f ? (value >= 0f ? 1f : 0f) : Mathf.Clamp01(value / duration);
    }

    // sin(πs)×(1−s) のいちばん大きな値（反対側への揺れを、ちょうど overshoot 度にするための割り算に使う）
    private const float SwingPeak = 0.5792f;

    /// <summary>
    /// 入ってくる腕の傾き（度）。t は入ってくる時間に対する割合（0〜1）。
    /// 0 で start 度 → upright で 0 度（まっすぐ） → 反対側へ overshoot 度まで揺れる → 1 で 0 度に止まる。
    /// </summary>
    public static float ArmSwing(float t, float upright, float start, float overshoot)
    {
        t = Mathf.Clamp01(t);
        if (t < upright)
        {
            return start * Mathf.Cos(t / upright * Mathf.PI * 0.5f);
        }

        // 終わりで速さが 0 になる揺れ（止まるときに、かくっとしない）
        float s = upright >= 1f ? 1f : (t - upright) / (1f - upright);
        return -overshoot * Mathf.Sin(s * Mathf.PI) * (1f - s) / SwingPeak;
    }

    private static float EaseOutCubic(float t)
    {
        float rest = 1f - Mathf.Clamp01(t);
        return 1f - rest * rest * rest;
    }

    /// <summary>
    /// 文字の後ろの点の数。1個から max 個まで1つずつ増え、また1個まで減るのをくり返す（秒数 t、1段 step 秒）。
    /// </summary>
    public static int TitleDots(float t, float step, int max)
    {
        if (max <= 1 || step <= 0f)
        {
            return Mathf.Max(1, max);
        }

        int period = (max - 1) * 2;
        int phase = Mathf.FloorToInt(Mathf.Max(0f, t) / step) % period;
        if (phase < 0)
        {
            phase += period; // とても大きな秒数で int があふれたとき
        }

        return phase < max ? phase + 1 : period - phase + 1;
    }

    /// <summary>
    /// 左から lane 番目のマスが止まる時刻（秒）。いちばん右は必ず spinSeconds ちょうど。
    /// </summary>
    public static float LaneStopTime(int lane, int laneCount, float spinSeconds, float firstStopRatio)
    {
        if (laneCount <= 1 || lane >= laneCount - 1)
        {
            return spinSeconds;
        }

        float t = Mathf.Clamp01((float)lane / (laneCount - 1));
        return spinSeconds * Mathf.Lerp(Mathf.Clamp01(firstStopRatio), 1f, t);
    }

    /// <summary>回り始めてから elapsed 秒で、カードが何枚流れたか（だんだん遅くなり、止まる時刻に count ちょうど）。</summary>
    public static float ReelPosition(float elapsed, float stopTime, int count)
    {
        if (stopTime <= 0f || elapsed >= stopTime)
        {
            return count;
        }

        // 3乗だと最後の 1〜2 割は止まって見えるので、2乗にして止まる時刻まで動いて見えるようにした
        float rest = 1f - Mathf.Clamp01(elapsed / stopTime);
        return count * (1f - rest * rest);
    }

    /// <summary>
    /// お題のマスごとに、もう入れたかを決める。順番どおりなら左から step 個、
    /// 順不同なら種類ごとに入れた数だけ、その種類のマスを左から埋める。
    /// </summary>
    public static void MarkCollected(int[] kinds, int size, bool ordered, int step, int[] progressByKind, bool[] result)
    {
        Span<int> remain = stackalloc int[ItemKinds];
        for (int k = 0; k < ItemKinds && k < progressByKind.Length; k++)
        {
            remain[k] = progressByKind[k];
        }

        for (int n = 0; n < result.Length; n++)
        {
            if (n >= size)
            {
                result[n] = false;
            }
            else if (ordered)
            {
                result[n] = n < step;
            }
            else
            {
                int kind = kinds[n];
                result[n] = kind >= 0 && kind < ItemKinds && remain[kind] > 0;
                if (result[n])
                {
                    remain[kind]--;
                }
            }
        }
    }

    /// <summary>このマップで出る物資から、avoid 以外を1つ選ぶ（見た目だけなので、各PCでばらばらでよい）。</summary>
    private int RandomKind(int avoid)
    {
        int count = 0;
        for (int k = 0; k < ItemKinds; k++)
        {
            if (SpaceJunkMaterials.IsUsed(SpaceJunkMaterials.FromIndex(k)))
            {
                kindBuffer[count++] = k;
            }
        }

        if (count == 0)
        {
            for (int k = 0; k < ItemKinds; k++)
            {
                kindBuffer[count++] = k;
            }
        }

        if (count == 1)
        {
            return kindBuffer[0];
        }

        int pick;
        do
        {
            pick = kindBuffer[random.Next(count)];
        } while (pick == avoid);

        return pick;
    }

    /// <summary>左上から何番目の枠に、どのチームを出すか。</summary>
    private int TeamOfPanel(int panel)
    {
        int row = panel / 2;
        bool swapped = row < rowSwapped.Length ? rowSwapped[row] : swapLeftRight;
        return swapped ? panel ^ 1 : panel;
    }

    /// <summary>段ごとに、2チームのゴールが画面のどちら側に見えるかで左右を決める。</summary>
    private void UpdateSides(bool inRound)
    {
        for (int row = 0; row < rowSwapped.Length; row++)
        {
            int even = row * 2;
            float evenX = 0f;
            float oddX = 0f;
            bool found = inRound && followGoalSide &&
                         TryGoalScreenX(even, out evenX) && TryGoalScreenX(even + 1, out oddX);

            rowSwapped[row] = found
                ? DecideSwap(sidesDecided ? rowSwapped[row] : swapLeftRight, evenX, oddX)
                : swapLeftRight;
        }

        sidesDecided = true;
    }

    /// <summary>
    /// 番号の小さいチーム（青・緑）のゴールが右に見えれば入れ替える。横位置が近いときは current のまま。
    /// </summary>
    public static bool DecideSwap(bool current, float evenTeamX, float oddTeamX)
    {
        float dx = evenTeamX - oddTeamX;
        return Mathf.Abs(dx) < SideMargin ? current : dx > 0f;
    }

    /// <summary>そのチームのゴールが、画面の横のどこに見えるか（0＝左端、1＝右端）。カメラの後ろのゴールは数えない。</summary>
    private static bool TryGoalScreenX(int team, out float x)
    {
        x = 0f;
        Camera view = Camera.main;
        if (view == null)
        {
            return false;
        }

        int count = 0;
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal == null || goal.OwnerTeam != team)
            {
                continue;
            }

            Vector3 point = view.WorldToViewportPoint(goal.transform.position);
            if (point.z > 0f)
            {
                x += point.x;
                count++;
            }
        }

        if (count == 0)
        {
            return false;
        }

        x /= count;
        return true;
    }

    /// <summary>出していたお題を忘れる（次にイベントが始まったら、また腕が入ってくるところから）。</summary>
    private void ResetStates()
    {
        if (!hasState)
        {
            return;
        }

        hasState = false;
        sidesDecided = false;
        foreach (TeamState state in states)
        {
            state.Reset();
        }
    }

    private void EnsureStates()
    {
        for (int team = 0; team < MaxTeams; team++)
        {
            states[team] ??= new TeamState();
        }
    }

    private void ApplyFont()
    {
        Font font = titleFont != null ? titleFont : UiFont.Find(24);
        foreach (PanelParts parts in panels)
        {
            if (parts != null && parts.title != null)
            {
                parts.title.font = font;
            }
        }
    }

    private void SetVisible(bool visible)
    {
        visible &= !RoundResultPlayback.IsPresenting;
        if (content != null && content.activeSelf != visible)
        {
            content.SetActive(visible);
        }
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null && component.gameObject.activeSelf != active)
        {
            component.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// プレハブを開いたときの見本：全チームが開ききって、お題が3個止まっている形に並べる（エディタのメニューから呼ぶ）。
    /// </summary>
    public void LayoutPreview()
    {
        EnsureStates();
        shownTeams = MaxTeams;
        Array.Clear(rowSwapped, 0, rowSwapped.Length);

        foreach (TeamState state in states)
        {
            state.Reset();
            state.introStart = 0f;
            state.spinStart = 0f;
            state.size = Mathf.Min(3, MaxSlots);
            for (int n = 0; n < state.size; n++)
            {
                state.kinds[n] = n % ItemKinds;
            }

            state.collected[0] = true;
        }

        DrawAll(1000f, float.NaN);
    }

    // ---------------------------------------------------------------- テスト用

    /// <summary>テスト用のお題を進める（最初に全チームのお題を引き、そのあと一定の間隔で1個ずつ入れる）。</summary>
    private void UpdateTest(float now, int teamCount)
    {
        if (!testStarted)
        {
            testStarted = true;
            testEventStart = now;
            testNextAction = now + IntroSeconds + spinSeconds + testCollectInterval;

            for (int team = 0; team < MaxTeams; team++)
            {
                TestDraw(team);
            }
        }

        if (testAutoCollect && now >= testNextAction)
        {
            testNextAction = now + testCollectInterval;
            TestStep(random.Next(teamCount), now);
        }

        for (int team = 0; team < teamCount; team++)
        {
            int size = testSizes[team];
            for (int n = 0; n < size; n++)
            {
                readKinds[n] = testKinds[team, n];
            }

            TeamState state = states[team];
            Receive(state, size, readKinds, testVersions[team], now, now - testEventStart);

            for (int n = 0; n < MaxSlots; n++)
            {
                readCollected[n] = n < size && testCollected[team, n];
            }

            UpdateCollected(state, readCollected, now);
        }
    }

    /// <summary>そのチームのお題を1つ進める。そろっていれば次のお題を引く。</summary>
    private void TestStep(int team, float now)
    {
        // スロットが回っている間は待つ
        if (now - states[team].spinStart < spinSeconds)
        {
            return;
        }

        int size = testSizes[team];
        int done = 0;
        for (int n = 0; n < size; n++)
        {
            done += testCollected[team, n] ? 1 : 0;
        }

        if (done >= Mathf.Min(testGoal, size))
        {
            TestDraw(team);
            return;
        }

        int slot = -1;
        if (testOrdered)
        {
            slot = done;
        }
        else
        {
            int left = size - done;
            int pick = random.Next(left);
            for (int n = 0; n < size; n++)
            {
                if (!testCollected[team, n] && pick-- == 0)
                {
                    slot = n;
                    break;
                }
            }
        }

        if (slot >= 0 && slot < size)
        {
            testCollected[team, slot] = true;
        }
    }

    /// <summary>そのチームのテスト用のお題を引き直す。</summary>
    private void TestDraw(int team)
    {
        testSizes[team] = Mathf.Clamp(testSetSize, 1, MaxSlots);
        testVersions[team]++;

        for (int n = 0; n < MaxSlots; n++)
        {
            testKinds[team, n] = n < testSizes[team] ? RandomKind(-1) : -1;
            testCollected[team, n] = false;
        }
    }

    [ContextMenu("テスト：全チームのお題を引き直す")]
    private void TestRedrawAll()
    {
        for (int team = 0; team < MaxTeams; team++)
        {
            TestDraw(team);
        }
    }

    [ContextMenu("テスト：しまう／出す を切り替える")]
    private void TestReopen()
    {
        testEventRunning = !testEventRunning;
    }
}
