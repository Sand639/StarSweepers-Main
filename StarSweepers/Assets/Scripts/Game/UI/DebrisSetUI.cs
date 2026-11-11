using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドイベント「デブリセット」のお題を、物資の画像で横に並べて出す（始まりにスロットの演出、入れた物資にチェック）。
/// 説明：Documents/スクリプトの説明/Game/UI/DebrisSetUI.md
/// </summary>
public sealed class DebrisSetUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/DebrisSetUI.prefab）。</summary>
    public const string PrefabResourcePath = "DebrisSetUI";

    /// <summary>出せるチームの最大数。</summary>
    public const int MaxTeams = SpaceJunkTeams.MaxTeams;

    /// <summary>1チームのお題の、いちばん多い数（並べるマスの数）。</summary>
    public const int MaxSlots = SpaceJunkRound.MaxSetSize;

    /// <summary>物資の種類の数（物資1〜5の画像）。</summary>
    public const int ItemKinds = SpaceJunkMaterials.Count;

    /// <summary>お題の1マスの部品。</summary>
    [Serializable]
    public sealed class SlotParts
    {
        [Tooltip("このマス（横並びの1つ）")]
        public RectTransform root;

        [Tooltip("物資の画像（スロット中は、いま真ん中を流れている物資）")]
        public Image item;

        [Tooltip("スロット中だけ使う、下から上がってくる次の物資の画像")]
        public Image nextItem;

        [Tooltip("入れた物資の右下に出すチェックマーク")]
        public Image check;
    }

    /// <summary>1チームぶんの枠の部品。</summary>
    [Serializable]
    public sealed class PanelParts
    {
        [Tooltip("このチームの枠ごと出し入れする物")]
        public GameObject root;

        [Tooltip("フレームの画像")]
        public Image frame;

        [Tooltip("お題のマス。左から順に、最大8つ")]
        public SlotParts[] slots = new SlotParts[MaxSlots];
    }

    [Header("素材（本素材が来たら差し替える。手順は機能ドキュメント「デブリセットのUI」）")]
    [Tooltip("物資の画像。上から 物資1＝装甲板、物資2＝回路基板、物資3＝燃料タンク、物資4＝アンテナ、物資5＝エンジン")]
    [SerializeField] private Sprite[] itemSprites = new Sprite[ItemKinds];

    [Tooltip("フレームの画像。空ならプレハブの画像のまま。9スライス（Sprite Editor の Border）を決めておくと、物資の数が変わっても角がつぶれない")]
    [SerializeField] private Sprite frameSprite;

    [Tooltip("チェックマークの画像。空ならプレハブの画像のまま")]
    [SerializeField] private Sprite checkSprite;

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Tooltip("チームの枠。0＝左上、1＝右上、2＝左の2段目、3＝右の2段目")]
    [SerializeField] private PanelParts[] panels = new PanelParts[MaxTeams];

    [Header("見た目")]
    [Tooltip("入れた物資の画像にかける色。白ならそのまま、灰色に近いほど暗く灰色っぽくなる")]
    [SerializeField] private Color collectedTint = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Tooltip("オフなら 左＝青チーム・右＝赤チーム。オンなら左右を入れ替える（3・4チーム目の段も入れ替わる）")]
    [SerializeField] private bool swapLeftRight;

    [Header("スロットの演出")]
    [Tooltip("いちばん右のマスが止まるまでの秒数。スロットは、ちょうどこの秒数で全部止まる")]
    [SerializeField, Min(0f)] private float spinSeconds = 1f;

    [Tooltip("いちばん左のマスが止まる時刻（Spin Seconds に対する割合）。そこから右へ、等しい間隔で順に止まる")]
    [SerializeField, Range(0f, 1f)] private float firstStopRatio = 0.5f;

    [Tooltip("回り始めの速さ（1秒に何個の物資が流れるか）。止まる時刻に向けて、だんだん遅くなる")]
    [SerializeField, Min(1f)] private float spinStartSpeed = 24f;

    [Tooltip("お題をそろえて次のお題が出たときにも、スロットを回すか")]
    [SerializeField] private bool spinOnNewSet = true;

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くても下の値で出す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    [Tooltip("テストのチーム数")]
    [SerializeField, Range(1, MaxTeams)] private int testTeamCount = 2;

    [Tooltip("テストのお題の物資の数")]
    [SerializeField, Range(1, MaxSlots)] private int testSetSize = 5;

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

    // チームごとの、画面に出しているお題とスロットの状態
    private sealed class TeamState
    {
        public int size;
        public readonly int[] kinds = new int[MaxSlots];
        public readonly bool[] collected = new bool[MaxSlots];
        public int version;
        public bool seen;
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
            spinStart = float.NegativeInfinity;
            Array.Clear(collected, 0, MaxSlots);
            Array.Clear(reels, 0, MaxSlots);
        }
    }

    private readonly TeamState[] states = new TeamState[MaxTeams];
    private readonly int[] readKinds = new int[MaxSlots];
    private readonly int[] readProgress = new int[ItemKinds];
    private readonly int[] kindBuffer = new int[ItemKinds];
    private readonly System.Random random = new System.Random();
    private bool hasState;

    // テスト用のお題
    private readonly int[,] testKinds = new int[MaxTeams, MaxSlots];
    private readonly int[] testSizes = new int[MaxTeams];
    private readonly bool[,] testCollected = new bool[MaxTeams, MaxSlots];
    private readonly int[] testVersions = new int[MaxTeams];
    private bool testStarted;
    private float testEventStart;
    private float testNextAction;

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

        for (int team = 0; team < MaxTeams; team++)
        {
            states[team] = new TeamState();
        }

        ApplySprites();
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
            SetVisible(false);
            ResetStates();
            return;
        }

        SetVisible(true);
        hasState = true;

        for (int panel = 0; panel < panels.Length; panel++)
        {
            PanelParts parts = panels[panel];
            if (parts == null || parts.root == null)
            {
                continue;
            }

            int team = TeamOfPanel(panel);
            bool used = team >= 0 && team < teamCount;

            if (parts.root.activeSelf != used)
            {
                parts.root.SetActive(used);
            }

            if (used)
            {
                DrawPanel(parts, states[team], now);
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

                MarkCollected(state.kinds, state.size, round.SetOrdered, round.SetStepOf(team), readProgress, state.collected);
            }

            return true;
        }

        teamCount = 0;
        if (!testMode)
        {
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
        state.seen = true;
        state.version = version;
        state.size = size;
        Array.Copy(kinds, state.kinds, size);
        Array.Clear(state.collected, 0, MaxSlots);

        if (first)
        {
            // イベントの始まりの時刻にそろえる（途中から見え始めたなら、もう止まっている）
            StartSpin(state, now - Mathf.Max(0f, sinceEventStart), now);
        }
        else if (spinOnNewSet)
        {
            StartSpin(state, now, now);
        }
        else
        {
            state.spinStart = float.NegativeInfinity;
        }
    }

    /// <summary>レーンごとに、流れる物資の並び（最後がお題の物資）を作って回し始める。</summary>
    private void StartSpin(TeamState state, float start, float now)
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
            reel[count] = state.kinds[n];

            // 隣どうしが同じ物資だと流れて見えないので、違う物資にする
            for (int i = count - 1; i >= 0; i--)
            {
                reel[i] = RandomKind(reel[i + 1]);
            }

            state.reels[n] = reel;
        }
    }

    /// <summary>1チームの枠を描く。</summary>
    private void DrawPanel(PanelParts parts, TeamState state, float now)
    {
        float elapsed = now - state.spinStart;

        for (int n = 0; n < parts.slots.Length; n++)
        {
            SlotParts slot = parts.slots[n];
            if (slot == null || slot.root == null)
            {
                continue;
            }

            bool used = n < state.size;
            if (slot.root.gameObject.activeSelf != used)
            {
                slot.root.gameObject.SetActive(used);
            }

            if (!used)
            {
                continue;
            }

            int[] reel = state.reels[n];
            float stop = LaneStopTime(n, state.size, spinSeconds, firstStopRatio);

            if (reel != null && elapsed < stop)
            {
                // 回っている：物資を下から上へ流す
                float position = ReelPosition(elapsed, stop, reel.Length - 1);
                int index = Mathf.Clamp(Mathf.FloorToInt(position), 0, reel.Length - 1);
                float fraction = position - index;
                float height = slot.root.rect.height;

                ShowItem(slot.item, reel[index], fraction * height, false);
                SetActive(slot.nextItem, true);
                ShowItem(slot.nextItem, reel[Mathf.Min(index + 1, reel.Length - 1)], (fraction - 1f) * height, false);
                SetActive(slot.check, false);
            }
            else
            {
                // 止まっている：お題の物資と、入れたかどうか
                ShowItem(slot.item, state.kinds[n], 0f, state.collected[n]);
                SetActive(slot.nextItem, false);
                SetActive(slot.check, state.collected[n]);
            }
        }
    }

    private void ShowItem(Image image, int kind, float y, bool collected)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = kind >= 0 && kind < itemSprites.Length ? itemSprites[kind] : null;
        image.color = collected ? collectedTint : Color.white;
        image.rectTransform.anchoredPosition = new Vector2(0f, y);
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

    /// <summary>回り始めてから elapsed 秒で、物資がいくつ流れたか（だんだん遅くなり、止まる時刻に count ちょうど）。</summary>
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
        return swapLeftRight ? panel ^ 1 : panel;
    }

    /// <summary>出していたお題を忘れる（次にイベントが始まったら、またスロットから）。</summary>
    private void ResetStates()
    {
        if (!hasState)
        {
            return;
        }

        hasState = false;
        foreach (TeamState state in states)
        {
            state.Reset();
        }
    }

    private void ApplySprites()
    {
        foreach (PanelParts parts in panels)
        {
            if (parts == null)
            {
                continue;
            }

            if (frameSprite != null && parts.frame != null)
            {
                parts.frame.sprite = frameSprite;
                parts.frame.type = frameSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            }

            if (checkSprite == null || parts.slots == null)
            {
                continue;
            }

            foreach (SlotParts slot in parts.slots)
            {
                if (slot != null && slot.check != null)
                {
                    slot.check.sprite = checkSprite;
                }
            }
        }
    }

    private void SetVisible(bool visible)
    {
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

    // ---------------------------------------------------------------- テスト用

    /// <summary>テスト用のお題を進める（最初に全チームのお題を引き、そのあと一定の間隔で1個ずつ入れる）。</summary>
    private void UpdateTest(float now, int teamCount)
    {
        if (!testStarted)
        {
            testStarted = true;
            testEventStart = now;
            testNextAction = now + spinSeconds + testCollectInterval;

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
                state.collected[n] = n < size && testCollected[team, n];
            }
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
}
