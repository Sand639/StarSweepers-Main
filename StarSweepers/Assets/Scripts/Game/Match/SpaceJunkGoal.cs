using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// **素材を投げ入れるゴール1つ分。** ステージの四方に4つ置く。
///
/// ## 誰の点になるか
///
/// **そのゴールを持つチームのもの。** 誰が投げ入れても持ち主のチームに入る。
/// つまり「自分たちの陣地へ運ぶ」形になり、**間違えて相手のゴールへ入れると相手を助けてしまう。**
///
/// 釣りにあった「どのチームでも入る共通ゴール」は無い。
/// **3チームのときは、余る1つのゴールを閉ざす**（入れても何も起きない）。
///
/// ## 判定するのはホストだけ
///
/// 参加者のPCでは何も数えない（それぞれが数えると、同じ素材で何度も数えてしまう）。
/// 数えるのは <see cref="SpaceJunkRound"/>。
///
/// フックで引っ張ったまま持ち込んだ素材は数えない（引きずり込みで稼げないようにするため）。
///
/// ## 渦に吸い込まれたら数える（2026/11/17）
///
/// ゴールに入った素材は、ゴールの黒い渦（<see cref="GoalEffect"/>）の円柱に入ると吸い込まれ、
/// 中心で消えた瞬間に数える。渦の外で止まった素材は渦のほうへ引き寄せる。
/// （前は「ゴールの中に1秒いつづけたら数えて消す」だった。渦が無いシーンでは、入った瞬間に数えて消す）
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class SpaceJunkGoal : NetworkBehaviour
{
    /// <summary>シーンにあるゴール全部。<see cref="SpaceJunkRound"/> が持ち主を配るために使う。</summary>
    public static readonly List<SpaceJunkGoal> All = new List<SpaceJunkGoal>();

    [Header("このゴールの場所")]
    [Tooltip("ゴールの並び順の番号（0=北 / 1=東 / 2=南 / 3=西）。持ち主の割り当てに使う")]
    [SerializeField] private int goalIndex = 0;

    [Header("見た目")]
    [Tooltip("チームの色に塗る床の Renderer")]
    [SerializeField] private Renderer padRenderer;

    [Tooltip("新しい種類が入ったときに一瞬光らせる色")]
    [SerializeField] private Color flashColor = Color.white;

    [Tooltip("光っている秒数")]
    [SerializeField] private float flashSeconds = 0.4f;

    [Tooltip("ラウンドの始まりの演出で飛ばす見た目のまとまり（2026/10/27）。空なら Pad Renderer の床だけを飛ばす。" +
             "ゴールエリアを1つの3Dモデルに替えたら、そのモデルを入れる")]
    [SerializeField] private Transform introVisual;

    /// <summary>ゴールの並び順の番号。</summary>
    public int GoalIndex => goalIndex;

    /// <summary>始まりの演出で飛ばす見た目のまとまり。設定が無ければ色を塗る床。どちらも無ければ null。</summary>
    public Transform IntroVisual => introVisual != null ? introVisual : padRenderer != null ? padRenderer.transform : null;

    /// <summary>
    /// 始まりの演出で飛ばす見た目（<see cref="IntroVisual"/>）の、世界での範囲。見た目が無ければ false。
    /// 演出中は本物の見た目を隠すので、Renderer の範囲ではなく形（メッシュ）から求める。
    /// </summary>
    public bool TryGetIntroVisualBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        Transform root = IntroVisual;
        if (root == null)
        {
            return false;
        }

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            // 形の箱の8つの角を、世界の位置に直して包む
            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = toWorld.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign));

                if (found)
                {
                    bounds.Encapsulate(corner);
                }
                else
                {
                    bounds = new Bounds(corner, Vector3.zero);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// **そのシーンにある、そのチームのゴールのうち、番号がいちばん小さいもの**（無ければ null）。
    /// 1チームで全部のゴールが自分たちのものでも、**どのPCでも同じゴールを選べる**ように番号で決める
    /// （<see cref="All"/> の並びはPCによって変わりうるため）。
    /// </summary>
    public static SpaceJunkGoal FirstOwnedBy(int team, Scene scene)
    {
        SpaceJunkGoal found = null;

        foreach (SpaceJunkGoal goal in All)
        {
            if (goal != null && goal.gameObject.scene == scene && goal.OwnerTeam == team &&
                (found == null || goal.goalIndex < found.goalIndex))
            {
                found = goal;
            }
        }

        return found;
    }

    /// <summary>このゴールを持っているチーム。<see cref="SpaceJunkTeams.ClosedGoal"/> なら閉ざされている。</summary>
    public int OwnerTeam { get; private set; } = SpaceJunkTeams.ClosedGoal;

    /// <summary>閉ざされたゴールか（3チームのときに1つだけ出る）。</summary>
    public bool IsClosed => OwnerTeam == SpaceJunkTeams.ClosedGoal;

    private Color teamColor = Color.white;
    private float flashUntil = -999f;

    /// <summary>ゴールの中にいる素材ごとの「最後に中で見かけた時刻」（ホストだけが使う。眠らせないため）。</summary>
    private readonly Dictionary<HookableObject, float> staying = new Dictionary<HookableObject, float>();

    /// <summary>
    /// この秒数より長く見かけなかった素材は「ゴールから出た」とみなす。
    /// OnTriggerExit は、素材に当たり判定が複数あると1つ抜けただけで呼ばれるので、見かけた時刻で判断する。
    /// </summary>
    private const float LeftAfterSeconds = 0.1f;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;

        // **入れ忘れていたら、子どもの見た目を自動で使う。**
        // 入っていないとチームの色が出ず、**自分のゴールが見分けられない**
        if (padRenderer == null)
        {
            padRenderer = GetComponentInChildren<Renderer>();
        }

        if (padRenderer == null)
        {
            Debug.LogWarning(
                $"{name}: チームの色に塗る床（Pad Renderer）がありません。" +
                "ゴールが何色か分からなくなるので、**色を塗る板を子どもに置いてください。**", this);
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }

        // **ラウンドの進行役が先に生まれていたら、その時点の持ち主をもらう。**
        // 生まれる順番はシーンによって変わるので、両側から取りに行く形にしてある
        if (SpaceJunkRound.Current != null)
        {
            ApplyOwnerTeam(SpaceJunkRound.Current.OwnerTeamOfGoal(goalIndex));
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>持ち主のチームを受け取り、床の色を変える。<see cref="SpaceJunkRound"/> から呼ばれる。</summary>
    public void ApplyOwnerTeam(int team)
    {
        OwnerTeam = team;
        teamColor = SpaceJunkTeams.TeamColor(team);

        if (padRenderer != null)
        {
            padRenderer.material.color = teamColor;
        }
    }

    /// <summary>光っている間（素材が入った直後）だけ Flash Color、それ以外はチームの色で床を塗る。</summary>
    private void Update()
    {
        if (padRenderer == null)
        {
            return;
        }

        bool lit = Time.time < flashUntil;
        padRenderer.material.color = lit ? flashColor : teamColor;
    }

    /// <summary>
    /// 待っている素材の後始末（ホストだけ）。出ていった・消えた素材は忘れる。
    /// 中で止まった素材は物理の計算が眠って OnTriggerStay が来なくなるので、起こしておく。
    /// </summary>
    private void FixedUpdate()
    {
        if (staying.Count == 0)
        {
            return;
        }

        List<HookableObject> gone = null;
        foreach (KeyValuePair<HookableObject, float> pair in staying)
        {
            if (pair.Key == null || pair.Key.IsVanished || Time.time - pair.Value > LeftAfterSeconds)
            {
                (gone ??= new List<HookableObject>()).Add(pair.Key);
                continue;
            }

            Rigidbody body = pair.Key.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.WakeUp();
            }
        }

        if (gone != null)
        {
            foreach (HookableObject item in gone)
            {
                staying.Remove(item);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    /// <summary>
    /// **中に入ったまま、あとから数えられる状態になった物も拾う**（2026/9/29）。
    /// 重いデブリはフックで引きずられて（つかまれたまま）ゴールに入り、中で手を離されるので、
    /// 入った瞬間（OnTriggerEnter）だけ見ていると数えそこねる。
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        // **数えるのはホストだけ。**
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // 閉ざされたゴールは何もしない（素材もそのまま残す）
        if (IsClosed)
        {
            return;
        }

        HookableObject item = other.GetComponentInParent<HookableObject>();

        if (item == null || item.IsVanished)
        {
            return;
        }

        // 素材以外（宇宙ごみの飾りなど）は数えない
        if (item.GetComponent<SpaceJunkMaterial>() == null)
        {
            return;
        }

        // 中にいる間は眠らせない（FixedUpdate）
        staying[item] = Time.time;

        // 引っ掛けたまま持ち込まれたものは数えない（手を離されたら渦が吸い込む）
        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();
        if (item.IsHooked || (netSupply != null && netSupply.IsClaimed))
        {
            return;
        }

        // **渦の円柱に入っていれば吸い込み、吸い込み終わったら数える。** 外なら渦のほうへ引き寄せる
        GoalEffect vortex = GoalEffect.In(gameObject.scene);
        if (vortex != null)
        {
            vortex.ServerFeed(this, item, Collect);
        }
        else
        {
            Collect(item);
        }
    }

    /// <summary>素材を数えて消す（ホストだけ）。**もう持っている種類でも、素材は消える（何も起きない）。**</summary>
    private void Collect(HookableObject item)
    {
        if (item == null)
        {
            return;
        }

        staying.Remove(item);
        SpaceJunkMaterial material = item.GetComponent<SpaceJunkMaterial>();
        FishingNetSupply netSupply = item.GetComponent<FishingNetSupply>();

        bool counted = !IsClosed && material != null && SpaceJunkRound.Current != null
                    && SpaceJunkRound.Current.ServerCollect(OwnerTeam, material.Kind, item.GetComponent<NetworkObject>());

        netSupply?.ClearLastThrower();

        if (counted)
        {
            FlashClientRpc();
        }

        item.Vanish();
    }

    /// <summary>得点が入った瞬間（全員のPCで呼ばれる）。ゴールの得点の演出（SpaceJunkGoalScoreEffect）が受け取る。</summary>
    public static event System.Action<SpaceJunkGoal> Scored;

    /// <summary>全員の画面で床を一瞬光らせる。</summary>
    [ClientRpc]
    private void FlashClientRpc()
    {
        ShowScored();
    }

    /// <summary>得点が入ったときの見た目を、このPCで出す（床を一瞬光らせ、得点の演出に知らせる）。検証シーンからも呼ぶ。</summary>
    public void ShowScored()
    {
        flashUntil = Time.time + flashSeconds;
        Scored?.Invoke(this);
    }
}
