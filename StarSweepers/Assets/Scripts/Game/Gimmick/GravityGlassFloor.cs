using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **重力・踏んだ回数・乗っている累計時間で割れるガラスの床。**
/// CounterBreak はプレイヤーごとの乗り直しで別々の起点にヒビを作り、合計回数で割れる。
/// StayTime は乗っている人数ぶん時間を足し、各プレイヤーの足元からヒビを伸ばす。
/// 降りても累計時間とヒビは残り、Restore でまとめてリセットする。
/// 以下は StepBreak（従来の重力モード）の説明。
///
/// | 段階 | 何が起きるか |
/// | --- | --- |
/// | ふつう | 乗っても何も起きない |
/// | **ヒビ** | **重力が決めた値以上のときに踏むと、ヒビが入る。** ヒビは広がっていき、ふちが赤く点滅する |
/// | **割れる** | ヒビが入ってから決めた秒数で割れる。**破片に当たり判定は無い**ので、乗っていた人は落ちる |
///
/// **一度ヒビが入ったら止まらない。** 途中で重力が軽くなっても割れる。
///
/// ## 使い方
///
/// **ガラスのマテリアルを付けた Cube に、これを付けるだけ。**
/// 大きさは Cube の Scale で決める（ヒビも破片も、大きさに合わせて作られる）。
/// メニューの `Tools > StarSweepers > アーカイブ > 月面運動会 > 重力で割れるガラスの床を置く` でも置ける。
///
/// ## 「踏んだ」の見分け方
///
/// **床の上面のすぐ上に、人（CharacterController）がいるか**を毎フレーム調べている。
/// 当たった瞬間を拾う方式（OnCollision）は、CharacterController では呼ばれないため使っていない。
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Game/Gimmick/GravityGlassFloor.md。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public partial class GravityGlassFloor : MonoBehaviour
{

    /// <summary>床の状態。</summary>
    public enum FloorState
    {
        /// <summary>割れていない</summary>
        Intact,
        /// <summary>ヒビが入った（もうすぐ割れる）</summary>
        Cracked,
        /// <summary>割れた</summary>
        Broken,
    }

    public enum GlassType
    {
        StepBreak,
        CounterBreak,
        StayTime,
    }

    [Header("ガラスのタイプ")]
    [SerializeField] private GlassType glasstype;

    [Header("割れる条件")]
    [Tooltip("**ヒビが入ってから割れるまでの秒数**")]
    [Min(0f)]
    [SerializeField] private float secondsUntilBreak = 1.5f;

    [Header("割れる条件")]
    [Tooltip("乗り直した回数。プレイヤーごとに数え、合計がこの回数に達すると割れる")]
    [Min(1)]
    [SerializeField] private int breakCount = 3;

    [Tooltip("爆発で割れるまでの回数。0なら爆発の影響を受けない")]
    [Min(0)]
    [SerializeField] private int blastHitsToBreak;

    [Header("割れる条件")]
    [Min(0f)]
    [SerializeField] private float stayTime = 3f;

    [Tooltip("**この重力以上のときに踏むと、ヒビが入る。** 正の数で入れる。地球は9.81")]
    [Min(0f)]
    [SerializeField] private float breakGravity = 12f;

    [Tooltip("ONなら**人（CharacterController）だけ**を数える。OFFにすると、置いた箱などでも割れる")]
    [SerializeField] private bool onlyCharacters = true;

    [Tooltip("上面からどれだけ上までを「乗っている」とみなすか（メートル）")]
    [Min(0.05f)]
    [SerializeField] private float detectHeight = 0.3f;

    [Header("ヒビの見た目")]
    [Tooltip("ヒビの線に使うマテリアル。**白い不透明なもの**が見えやすい")]
    [SerializeField] private Material crackMaterial;

    [Tooltip("ヒビの線の本数（踏んだ場所から放射状に伸びる）")]
    [Range(3, 16)]
    [SerializeField] private int crackLines = 7;

    [Tooltip("ヒビの線の太さ（メートル）")]
    [Min(0.005f)]
    [SerializeField] private float crackWidth = 0.035f;

    [Tooltip("ヒビが入ったときの、ふちの色。**割れる直前ほど速く点滅する**")]
    [SerializeField] private Color crackEdgeColor = new Color(1f, 0.35f, 0.25f, 1f);

    [Header("割れたときの見た目")]
    [Tooltip("破片の数（縦×横）。3なら 3×3＝9枚")]
    [Range(1, 6)]
    [SerializeField] private int piecesPerSide = 3;

    [Tooltip("破断直前にヒビをふちまで伸ばす秒数。0なら従来の破片を使う")]
    [Min(0f)]
    [SerializeField] private float fractureExtensionSeconds;

    [Tooltip("破片が消えるまでの秒数")]
    [Min(0.1f)]
    [SerializeField] private float pieceLifeSeconds = 3f;

    [Tooltip("破片が散らばる勢い")]
    [Min(0f)]
    [SerializeField] private float scatterSpeed = 1.2f;

    [Header("元に戻る")]
    [Tooltip("**割れてから元に戻るまでの秒数。** 0なら戻らない（やり直しのときだけ戻る）")]
    [Min(0f)]
    [SerializeField] private float respawnSeconds;

    [Tooltip("割れるたびにコンソールへ書き出す")]
    [SerializeField] private bool logChanges = true;

    /// <summary>いまの状態。</summary>
    public FloorState State { get; private set; } = FloorState.Intact;

    /// <summary>割れるまでの残り秒数。StayTime は残りの延べ滞在秒数。CounterBreak では使わない。</summary>
    public float BreakRemaining { get; private set; }

    /// <summary>
    /// 「乗っている人」を探すときの入れ物。**毎フレーム作り直さないよう、全部の床で使い回している。**
    /// 一度に拾えるのは16個まで（床の上にそれ以上の物が重なることは想定していない）。
    /// </summary>
    private static readonly Collider[] Hits = new Collider[16];

    /// <summary>ガラスのシェーダーの「ふちの色」の欄。名前で毎回探すと遅いので、番号にしておく。</summary>
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

    private BoxCollider box;
    private MeshRenderer meshRenderer;

    /// <summary>
    /// ふちの色を、**この床だけ**変えるための入れ物。
    /// マテリアルを直接書き換えると、同じマテリアルを使う他の床まで赤くなってしまうため。
    /// </summary>
    private MaterialPropertyBlock propertyBlock;

    /// <summary>マテリアルにもともと入っていた、ふちの色。点滅はこの色と赤の間を行き来する。</summary>
    private Color originalEdgeColor = Color.white;

    /// <summary>マテリアルに「ふちの色」の欄があるか。無ければ点滅させない（ガラス以外のマテリアルでも動くように）。</summary>
    private bool hasEdgeColor;

    /// <summary>ヒビの線をまとめて入れておく親。消すときはこれごと消す。</summary>
    private Transform crackRoot;

    /// <summary>ヒビの線の一覧。**踏んだ場所に近い順**に並べてあり、前から順に見せていく。</summary>
    private readonly List<GameObject> crackSegments = new List<GameObject>();

    /// <summary>割れてから元に戻るまでの残り秒数。</summary>
    private float respawnTimer;

    private int stepCount;
    private int blastHits;
    private float fractureTime;
    private CrackPattern finalCracks;
    private readonly List<(Vector2 Point, Vector2 Direction)> crackTips = new();
    private float accumulatedStayTime;
    private readonly Dictionary<int, Vector3> steppers = new Dictionary<int, Vector3>();
    private readonly HashSet<int> previousSteppers = new HashSet<int>();
    private readonly Dictionary<int, CrackPattern> standingCracks = new Dictionary<int, CrackPattern>();
    private readonly List<CrackPattern> crackPatterns = new List<CrackPattern>();
    private readonly List<int> departedSteppers = new List<int>();

    // 各起点の線を保存し、線の根元を固定したまま先端を滑らかに伸ばす。
    private class CrackPattern
    {
        public readonly List<CrackSegment> Segments = new List<CrackSegment>();
        public float Progress;
    }

    private struct CrackSegment
    {
        public Transform Transform;
        public Vector3 Start;
        public Vector3 End;
        public float Length;
        public float StartProgress;
        public float EndProgress;
    }

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();

        // ふちの色を持つマテリアル（ガラスのシェーダー）のときだけ、点滅させる
        if (meshRenderer != null && meshRenderer.sharedMaterial != null &&
            meshRenderer.sharedMaterial.HasProperty(EdgeColorId))
        {
            hasEdgeColor = true;
            originalEdgeColor = meshRenderer.sharedMaterial.GetColor(EdgeColorId);
        }
    }

    private void OnEnable()
    {
        StageReset.Requested += Restore;
    }

    private void OnDisable()
    {
        StageReset.Requested -= Restore;
    }

    private void Update()
    {
        if (GamePause.IsPaused)
        {
            return;
        }

        if (finalCracks != null)
        {
            fractureTime += Time.deltaTime;
            finalCracks.Progress = Mathf.Clamp01(fractureTime / fractureExtensionSeconds);
            RevealPattern(finalCracks);
            if (finalCracks.Progress >= 1f)
            {
                FinishBreak();
            }
            return;
        }

        if (State == FloorState.Broken)
        {
            UpdateBroken();
            return;
        }

        switch (glasstype)
        {
            case GlassType.CounterBreak:
                UpdateCounterBreak();
                return;
            case GlassType.StayTime:
                UpdateStayTime(Time.deltaTime);
                return;
        }

        // StepBreak は従来どおり、重力でヒビが入り、その後は時間で割れる。
        switch (State)
        {
            case FloorState.Intact:
                CheckStep();
                break;

            case FloorState.Cracked:
                UpdateCracked();
                break;

        }
    }

    private void UpdateCounterBreak()
    {
        FindSteppers();
        foreach (KeyValuePair<int, Vector3> stepper in steppers)
        {
            if (previousSteppers.Contains(stepper.Key))
            {
                continue;
            }

            stepCount++;
            State = FloorState.Cracked;
            CrackPattern pattern = BuildCracks(stepper.Value, true);
            if (stepCount >= Mathf.Max(1, breakCount))
            {
                Break();
                return;
            }
        }

        previousSteppers.Clear();
        previousSteppers.UnionWith(steppers.Keys);
        foreach (CrackPattern pattern in crackPatterns)
        {
            pattern.Progress = Mathf.MoveTowards(pattern.Progress, 1f, Time.deltaTime * 3f);
            RevealPattern(pattern);
        }
        if (State == FloorState.Cracked)
        {
            BlinkEdge((float)stepCount / Mathf.Max(1, breakCount));
        }
    }

    private void UpdateStayTime(float deltaTime)
    {
        FindSteppers();

        // 降りた起点は残すが、成長は止める。再び乗ると新しい足元に起点を作る。
        departedSteppers.Clear();
        foreach (int id in standingCracks.Keys)
        {
            if (!steppers.ContainsKey(id)) departedSteppers.Add(id);
        }
        foreach (int id in departedSteppers) standingCracks.Remove(id);

        float duration = Mathf.Max(0f, stayTime);
        float addedTime = Mathf.Min(deltaTime * steppers.Count, Mathf.Max(0f, duration - accumulatedStayTime));
        accumulatedStayTime += addedTime;
        BreakRemaining = Mathf.Max(0f, duration - accumulatedStayTime);

        foreach (KeyValuePair<int, Vector3> stepper in steppers)
        {
            State = FloorState.Cracked;
            if (!standingCracks.TryGetValue(stepper.Key, out CrackPattern pattern))
            {
                pattern = BuildCracks(stepper.Value, true);
                standingCracks.Add(stepper.Key, pattern);
            }

            // 人数が増えると、すべての在席者のヒビも同じ速度で速く伸びる。
            pattern.Progress = duration > 0f ? Mathf.Clamp01(pattern.Progress + addedTime / duration) : 1f;
            RevealPattern(pattern);
        }

        if (State == FloorState.Cracked)
        {
            BlinkEdge(duration > 0f ? accumulatedStayTime / duration : 1f);
        }
        if (steppers.Count > 0 && accumulatedStayTime >= duration)
        {
            Break();
        }
    }

    private void OnDrawGizmosSelected()
    {
        BoxCollider collider = GetComponent<BoxCollider>();

        if (collider == null)
        {
            return;
        }

        // 「乗っている」とみなす範囲を、水色の箱で示す
        // （TryFindStepper で調べている箱と同じ。横は床の 96%、高さは detectHeight）
        Vector3 size = Vector3.Scale(collider.size, transform.lossyScale);

        Gizmos.matrix = Matrix4x4.TRS(
            transform.TransformPoint(collider.center) + transform.up * (size.y * 0.5f + detectHeight * 0.5f),
            transform.rotation, Vector3.one);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x * 0.96f, detectHeight, size.z * 0.96f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
