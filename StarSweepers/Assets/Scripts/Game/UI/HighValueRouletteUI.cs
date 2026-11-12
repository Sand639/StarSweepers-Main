using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドイベント「期間限定高価値デブリ（高単価デブリ）」の種類を、時間の丸の下から出るルーレットで決めて見せる。
/// 説明：Documents/スクリプトの説明/Game/UI/HighValueRouletteUI.md
/// </summary>
public sealed class HighValueRouletteUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/HighValueRouletteUI.prefab）。</summary>
    public const string PrefabResourcePath = "HighValueRouletteUI";

    /// <summary>物資の種類の数（物資1〜5の画像）。</summary>
    public const int ItemKinds = SpaceJunkMaterials.Count;

    /// <summary>レールに乗せる物資の画像の数（真下と、その左右に3個ずつ）。</summary>
    public const int RailItems = 7;

    // 決まる物資の左右に、並べておく物資の数
    private const int Pad = RailItems / 2;

    // 決まった物資を、ぽんと大きくする秒数と倍率
    private const float PopSeconds = 0.3f;
    private const float PopScale = 1.3f;

    // テストで、イベントが終わってから次が始まるまでの秒数
    private const float TestGapSeconds = 1.5f;

    [Header("素材（本素材が来たら差し替える。手順は機能ドキュメント「高単価デブリのルーレットUI」）")]
    [Tooltip("物資の画像（デブリセットのUIと同じ画像）。上から 物資1＝装甲板、物資2＝回路基板、物資3＝燃料タンク、物資4＝アンテナ、物資5＝エンジン")]
    [SerializeField] private Sprite[] itemSprites = new Sprite[ItemKinds];

    [Tooltip("ルーレットのフレームの画像。空ならプレハブの画像のまま")]
    [SerializeField] private Sprite frameSprite;

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Tooltip("フレームと物資の親。出てくるときは、得点バーの裏から下りてくる")]
    [SerializeField] private RectTransform wheel;

    [Tooltip("フレームの画像")]
    [SerializeField] private Image frame;

    [Tooltip("レールに乗せる物資の画像（7つ）")]
    [SerializeField] private Image[] railItems = new Image[RailItems];

    [Header("ルーレット")]
    [Tooltip("イベントが始まってから、物資が決まる（真下で止まる）までの秒数")]
    [SerializeField, Min(0f)] private float spinSeconds = 2f;

    [Tooltip("回り始めの速さ（1秒に何個の物資が真下を通るか）。決まる時刻に向けて、だんだん遅くなる")]
    [SerializeField, Min(1f)] private float spinStartSpeed = 14f;

    [Tooltip("フレームの中心から物資の中心までの距離（レールの半径）")]
    [SerializeField, Min(0f)] private float railRadius = 200f;

    [Tooltip("レールの上の、となりの物資との角度（度）")]
    [SerializeField, Range(30f, 90f)] private float itemAngle = 45f;

    [Tooltip("出てくるとき・しまわれるときに動く秒数")]
    [SerializeField, Min(0f)] private float slideSeconds = 0.25f;

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くてもイベントをくり返す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    [Tooltip("テストのイベントが続く秒数。終わるとしまわれて、少しあとにまた始まる")]
    [SerializeField, Min(0.5f)] private float testEventSeconds = 5f;

    /// <summary>いま動いている係（ゲーム中は1つだけ）。</summary>
    public static HighValueRouletteUI Instance { get; private set; }

    /// <summary>いま画面にルーレットを出しているか（しまう途中も含む）。</summary>
    public static bool IsShowing => Instance != null && Instance.content != null && Instance.content.activeSelf;

    /// <summary>ルーレットが回っている（種類がまだ決まっていない）か。SpaceJunkMatchUI が、合図の文字で種類を先に出さないために見る。</summary>
    public static bool IsSpinning => Instance != null && Instance.shown && Instance.wasRunning &&
                                     Instance.lastTime - Instance.spinStart < Instance.spinSeconds;

    /// <summary>画面の上から、下りきったルーレットの下の端までのピクセル数。出していなければ 0（SpaceJunkMatchUI が見る）。</summary>
    public static float OccupiedScreenHeight { get; private set; }

    // レールに並ぶ物資（左右の Pad 個を含む。reel[reelCount + Pad] が決まる物資）
    private int[] reel;
    private int reelCount;
    private int shownKind = -1;
    private float spinStart;

    // 出しているか（しまう途中も含む）と、前のフレームでイベントが続いていたか
    private bool shown;
    private bool wasRunning;

    // 0＝しまわれている、1＝出ている
    private float reveal;
    private float lastTime;

    // 出ているときの位置と、しまうときに上へ動かす量
    private Vector2 shownPosition;
    private float hideOffset;
    private static readonly Vector3[] Corners = new Vector3[4];

    // テスト用
    private bool testStarted;
    private float testStart;
    private int testCycle;
    private int testKind;

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
            Debug.LogWarning($"[高単価デブリのルーレットUI] Resources/{PrefabResourcePath}.prefab が無いので、ルーレットは出ません。");
            return;
        }

        GameObject created = Instantiate(prefab);
        created.name = "HighValueRouletteUI";
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

        if (wheel != null)
        {
            // しまうときは、フレームの下の端が切り抜きの上の端（得点バーの上）より上に来るまで動かす
            shownPosition = wheel.anchoredPosition;
            hideOffset = wheel.rect.height * wheel.pivot.y - shownPosition.y + 2f;
        }

        if (frameSprite != null && frame != null)
        {
            frame.sprite = frameSprite;
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            OccupiedScreenHeight = 0f;
        }
    }

    private void Update()
    {
        Tick(Time.unscaledTime);
    }

    /// <summary>今の時刻（秒）での表示に合わせる。</summary>
    private void Tick(float now)
    {
        float delta = Mathf.Max(0f, now - lastTime);
        lastTime = now;

        if (TryRead(now, out int kind, out float sinceStart))
        {
            if (!shown || !wasRunning || kind != shownKind)
            {
                // イベントの始まりの時刻にそろえる（途中から見え始めたなら、もう止まっている）
                Begin(kind, now - Mathf.Max(0f, sinceStart));
            }

            wasRunning = true;
            reveal = slideSeconds > 0f ? Mathf.Clamp01((now - spinStart) / slideSeconds) : 1f;
        }
        else
        {
            wasRunning = false;
            reveal = slideSeconds > 0f ? Mathf.MoveTowards(reveal, 0f, delta / slideSeconds) : 0f;
            shown = shown && reveal > 0f;
        }

        SetVisible(shown);

        if (shown)
        {
            Draw(now);
        }
        else
        {
            OccupiedScreenHeight = 0f;
        }
    }

    /// <summary>ラウンドかテスト用の値から、高単価の種類を読む。イベントが続いていなければ false。</summary>
    private bool TryRead(float now, out int kind, out float sinceStart)
    {
        kind = -1;
        sinceStart = 0f;
        SpaceJunkRound round = SpaceJunkRound.Current;

        if (round != null)
        {
            if (round.Rule != SpaceJunkWinRule.Score ||
                round.ActiveEvent != SpaceJunkEventKind.HighValueDebris ||
                !round.IsEventRunning ||
                !round.TryGetHighValueKind(out SpaceJunkMaterialKind chosen))
            {
                return false;
            }

            kind = (int)chosen;
            sinceStart = round.SecondsSinceEventStarted;
            return true;
        }

        if (!testMode)
        {
            return false;
        }

        if (!testStarted)
        {
            testStarted = true;
            testStart = now;
            testCycle = -1;
        }

        // 「イベント → しまわれて少し待つ」をくり返す。回ごとに種類を選び直す
        float period = testEventSeconds + TestGapSeconds;
        float elapsed = now - testStart;
        int cycle = Mathf.FloorToInt(elapsed / period);
        if (cycle != testCycle)
        {
            testCycle = cycle;
            testKind = RandomKind(-1);
        }

        kind = testKind;
        sinceStart = elapsed - cycle * period;
        return sinceStart < testEventSeconds;
    }

    /// <summary>レールに並ぶ物資（決まる物資が、回り終わったときに真下に来る並び）を作って回し始める。</summary>
    private void Begin(int kind, float start)
    {
        shown = true;
        shownKind = kind;
        spinStart = start;
        reelCount = ReelCount(spinStartSpeed, spinSeconds);

        int length = reelCount + 1 + Pad * 2;
        if (reel == null || reel.Length != length)
        {
            reel = new int[length];
        }

        FillReel(reel, reelCount + Pad, kind, RandomKind);
    }

    /// <summary>しまう・出す動きと、レールの物資の位置を合わせる。</summary>
    private void Draw(float now)
    {
        if (wheel != null)
        {
            float eased = 1f - (1f - reveal) * (1f - reveal);
            float slide = hideOffset * (1f - eased);
            wheel.anchoredPosition = shownPosition + new Vector2(0f, slide);
            OccupiedScreenHeight = MeasureOccupiedHeight(slide);
        }

        float elapsed = now - spinStart;
        float position = DebrisSetUI.ReelPosition(elapsed, spinSeconds, reelCount);
        int center = Mathf.FloorToInt(position);
        float sinceStop = elapsed - spinSeconds;
        float pop = sinceStop >= 0f && sinceStop < PopSeconds ? Mathf.Sin(sinceStop / PopSeconds * Mathf.PI) : 0f;

        for (int s = 0; s < railItems.Length; s++)
        {
            Image image = railItems[s];
            if (image == null)
            {
                continue;
            }

            int index = center - Pad + s;
            float angle = RailAngle(position, index, itemAngle);
            bool visible = index + Pad >= 0 && index + Pad < reel.Length && Mathf.Abs(angle) < 120f;

            if (image.gameObject.activeSelf != visible)
            {
                image.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                continue;
            }

            int kind = reel[index + Pad];
            image.sprite = kind >= 0 && kind < itemSprites.Length ? itemSprites[kind] : null;
            image.rectTransform.anchoredPosition = RailPoint(angle, railRadius);

            float scale = index == reelCount ? 1f + (PopScale - 1f) * pop : 1f;
            image.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <summary>画面の上から、下りきったときのルーレットの下の端までのピクセル数（下りてくる途中でも同じ値にする）。</summary>
    private float MeasureOccupiedHeight(float slide)
    {
        wheel.GetWorldCorners(Corners);

        // 画面に重ねる UI なので、角の位置はそのまま画面のピクセル（下が 0）。いま上へずらしているぶんを戻して測る
        float slideOnScreen = wheel.parent != null ? wheel.parent.TransformVector(new Vector3(0f, slide, 0f)).y : slide;
        return Mathf.Max(0f, Screen.height - (Corners[0].y - slideOnScreen));
    }

    /// <summary>決まるまでに真下を通る物資の数（減速の曲線の出だしの速さが spinStartSpeed になる数）。</summary>
    public static int ReelCount(float spinStartSpeed, float spinSeconds)
    {
        return Mathf.Max(Pad, Mathf.RoundToInt(spinStartSpeed * spinSeconds / 2f));
    }

    /// <summary>
    /// レールの index 番目の物資の角度（度。0＝真下、プラス＝右）。物資は左から来て、右へ流れる。
    /// </summary>
    public static float RailAngle(float position, int index, float itemAngle)
    {
        return (position - index) * itemAngle;
    }

    /// <summary>角度 angle（度。0＝真下）の物資を置く場所（フレームの中心から）。</summary>
    public static Vector2 RailPoint(float angle, float radius)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * radius;
    }

    /// <summary>reel[finalIndex] を決まる物資にし、その前後を、となりと違う物資で埋める。</summary>
    public static void FillReel(int[] reel, int finalIndex, int finalKind, Func<int, int> pickOtherThan)
    {
        reel[finalIndex] = finalKind;

        for (int i = finalIndex - 1; i >= 0; i--)
        {
            reel[i] = pickOtherThan(reel[i + 1]);
        }

        for (int i = finalIndex + 1; i < reel.Length; i++)
        {
            reel[i] = pickOtherThan(reel[i - 1]);
        }
    }

    /// <summary>このマップで出る物資から、avoid 以外を1つ選ぶ（見た目だけなので、各PCでばらばらでよい）。</summary>
    private static int RandomKind(int avoid)
    {
        // 出る物資が1種類しかないマップでは、同じ物資が続く
        for (int tries = 0; tries < 16; tries++)
        {
            int pick = SpaceJunkMaterials.RandomUsedIndex();
            if (pick != avoid)
            {
                return pick;
            }
        }

        return Mathf.Max(0, avoid);
    }

    private void SetVisible(bool visible)
    {
        if (content != null && content.activeSelf != visible)
        {
            content.SetActive(visible);
        }
    }

    [ContextMenu("テスト：もう一度回す")]
    private void TestRespin()
    {
        testStarted = false;
        shown = false;
    }
}
