using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// ゴールに常に出ている黒い渦。渦の円柱に入った物資を、小さくしながら回して中心へ吸い込む（吸い込み終わったら得点）。
/// 1つのシーンに1つ置けば、そのシーンの全部のゴールに出る。
/// 説明は Documents/スクリプトの説明/Game/Effect/SpaceJunkGoalVortex.md
/// </summary>
public class SpaceJunkGoalVortex : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("渦の見た目（Art/Materials/GoalVortex。腕の数・回る速さ・ふちの光はマテリアルで変える）")]
    [SerializeField] private Material vortexMaterial;

    [Tooltip("渦の半径＝ゴールの床の短い辺の半分×これ（1 で床いっぱい）")]
    [Range(0.3f, 1f)]
    [SerializeField] private float fill = 0.9f;

    [Tooltip("渦を床から浮かせる高さ（m。床とちらつかないように）")]
    [SerializeField] private float lift = 0.03f;

    [Tooltip("ゴールが着地してから、渦が出きるまでの秒数")]
    [SerializeField] private float appearSeconds = 0.6f;

    [Tooltip("ふちの光の明るさ")]
    [SerializeField] private float brightness = 1f;

    [Header("吸い込み")]
    [Tooltip("渦の円柱に入ってから、中心で消えて得点になるまでの秒数")]
    [SerializeField] private float absorbSeconds = 1f;

    [Tooltip("吸い込まれる間に、渦のまわりを何周するか")]
    [SerializeField] private float spinTurns = 1.25f;

    [Tooltip("吸い込まれる間に、物資自身が何度回るか")]
    [SerializeField] private float selfSpinDegrees = 540f;

    [Tooltip("消える直前の大きさ（元の何倍か）")]
    [Range(0.01f, 1f)]
    [SerializeField] private float endScale = 0.05f;

    [Tooltip("消える直前に、床の下へ沈む深さ（m）")]
    [SerializeField] private float sinkDepth = 0.4f;

    [Header("引き寄せ（ゴールに入ったが、渦の外にいる物資）")]
    [Tooltip("渦のほうへ引き寄せる強さ（加速度 m/s²）")]
    [SerializeField] private float pullAcceleration = 9f;

    [Tooltip("渦の円柱が、床より下へ何 m まで伸びるか（床に埋まりかけた物も拾うため）")]
    [SerializeField] private float depthBelowFloor = 0.5f;

    public float AbsorbSeconds => absorbSeconds;
    public float SpinTurns => spinTurns;
    public float SelfSpinDegrees => selfSpinDegrees;
    public float EndScale => endScale;
    public float SinkDepth => sinkDepth;

    /// <summary>ホストで物資が吸い込み終わった瞬間（引数はゴール）。検証シーンが使う。</summary>
    public static event Action<Component> Absorbed;

    /// <summary>吸い込みを始めた回数（確かめる用）。</summary>
    public int AbsorbCount { get; private set; }

    /// <summary>渦の円柱1つ分。Center は床の上面の真ん中。</summary>
    public struct Area
    {
        public Vector3 Center;
        public float Radius;
        public float Top;
    }

    private static readonly List<SpaceJunkGoalVortex> All = new List<SpaceJunkGoalVortex>();
    private readonly Dictionary<Component, Area> areas = new Dictionary<Component, Area>();
    private readonly Dictionary<Component, float> appear = new Dictionary<Component, float>();
    private readonly Dictionary<Component, MeshRenderer[]> visuals = new Dictionary<Component, MeshRenderer[]>();
    private readonly Dictionary<HookableObject, float> lastPull = new Dictionary<HookableObject, float>();
    private MaterialPropertyBlock block;
    private static Mesh discMesh;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int VortexTimeId = Shader.PropertyToID("_VortexTime");
    private static readonly int AppearId = Shader.PropertyToID("_Appear");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");

    /// <summary>そのシーンの渦（無ければ null）。</summary>
    public static SpaceJunkGoalVortex In(Scene scene)
    {
        foreach (SpaceJunkGoalVortex vortex in All)
        {
            if (vortex != null && vortex.gameObject.scene == scene)
            {
                return vortex;
            }
        }
        return null;
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    // ------------------------------------------------------------
    // 円柱の判定と吸い込み（ホストだけが呼ぶ）
    // ------------------------------------------------------------

    /// <summary>
    /// **ホストが、ゴールの中にいる物資1つについて呼ぶ。** 渦の円柱に入っていれば吸い込みを始め、外なら渦のほうへ引き寄せる。
    /// 吸い込み終わったら onAbsorbed（得点を数えて消す）を呼ぶ。
    /// </summary>
    public void ServerFeed(Component goal, HookableObject item, Action<HookableObject> onAbsorbed)
    {
        if (goal == null || item == null || item.IsVanished || item.IsHooked)
        {
            return;
        }

        // 引っ掛けられている物・持ち主がまだホストに戻っていない物は、手を離されてから
        FishingNetSupply supply = item.GetComponent<FishingNetSupply>();
        if (supply != null && supply.IsSpawned && (supply.IsClaimed || !supply.IsOwner || !supply.IsServer))
        {
            return;
        }

        if (!TryGetArea(goal, out Area area))
        {
            onAbsorbed?.Invoke(item);
            return;
        }

        Vector3 point = item.Body != null ? item.Body.worldCenterOfMass : item.transform.position;
        if (IsInside(area, point))
        {
            float start = SpaceJunkVortexAbsorb.ClockOf(item);
            SpaceJunkVortexAbsorb.Begin(item, area.Center, start, absorbSeconds, done =>
            {
                Absorbed?.Invoke(goal);
                onAbsorbed?.Invoke(done);
            });

            if (supply != null && supply.IsSpawned)
            {
                supply.ServerBeginAbsorb(start, absorbSeconds);
            }

            AbsorbCount++;
            return;
        }

        Pull(item, area, point);
    }

    /// <summary>その点が渦の円柱の中か。</summary>
    public bool IsInside(Area area, Vector3 point)
    {
        Vector2 flat = new Vector2(point.x - area.Center.x, point.z - area.Center.z);
        return flat.magnitude <= area.Radius && point.y >= area.Center.y - depthBelowFloor && point.y <= area.Top;
    }

    /// <summary>渦の外にいる物資を、渦のほうへ引き寄せる（1つの物理の更新で1回だけ）。</summary>
    private void Pull(HookableObject item, Area area, Vector3 point)
    {
        Rigidbody body = item.Body;
        if (body == null || body.isKinematic)
        {
            return;
        }

        if (lastPull.TryGetValue(item, out float last) && Mathf.Approximately(last, Time.fixedTime))
        {
            return;
        }

        if (lastPull.Count > 64)
        {
            lastPull.Clear();
        }
        lastPull[item] = Time.fixedTime;

        Vector3 toCenter = area.Center - point;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.0001f)
        {
            body.AddForce(toCenter.normalized * pullAcceleration, ForceMode.Acceleration);
        }
    }

    /// <summary>そのゴールの渦の円柱（はじめて聞かれたときに測って覚えておく）。</summary>
    public bool TryGetArea(Component goal, out Area area)
    {
        if (areas.TryGetValue(goal, out area))
        {
            return true;
        }

        if (!MeasureArea(goal, fill, out area))
        {
            return false;
        }

        areas[goal] = area;
        return true;
    }

    /// <summary>
    /// ゴールの渦の円柱を測る。大きさは床（見た目）の短い辺、高さはゴールの判定の上の端まで、
    /// 足もとはゴールの真ん中の真下にある床（輸送機なら荷台）の上面。
    /// </summary>
    public static bool MeasureArea(Component goal, float fill, out Area area)
    {
        area = default;
        Collider box = goal.GetComponent<Collider>();
        Vector3 basePoint;
        Vector2 size;

        if (goal is SpaceJunkGoal junkGoal && SpaceJunkGoalScoreEffect.TryGetGoalArea(junkGoal, 0f, out basePoint, out _, out size))
        {
        }
        else if (box is BoxCollider boxCollider)
        {
            Vector3 scale = boxCollider.transform.lossyScale;
            size = new Vector2(Mathf.Abs(boxCollider.size.x * scale.x), Mathf.Abs(boxCollider.size.z * scale.z));
            basePoint = boxCollider.bounds.center;
            basePoint.y = boxCollider.bounds.min.y;
        }
        else
        {
            return false;
        }

        float top = box != null ? box.bounds.max.y : basePoint.y + 3f;
        float floorY = FloorBelow(new Vector3(basePoint.x, top, basePoint.z), basePoint.y - 1f, basePoint.y);
        area = new Area
        {
            Center = new Vector3(basePoint.x, floorY, basePoint.z),
            Radius = Mathf.Min(size.x, size.y) * 0.5f * fill,
            Top = top,
        };
        return true;
    }

    private static readonly RaycastHit[] floorHits = new RaycastHit[16];

    /// <summary>from から真下に、いちばん上にある床の上面の高さ。物資・プレイヤー・動く物は床に数えない。無ければ fallback。</summary>
    private static float FloorBelow(Vector3 from, float lowest, float fallback)
    {
        float distance = Mathf.Max(0.1f, from.y - lowest);
        int count = Physics.RaycastNonAlloc(from, Vector3.down, floorHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider hit = floorHits[i].collider;
            if (hit is CharacterController || hit.GetComponentInParent<CharacterController>() != null ||
                (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) ||
                hit.GetComponentInParent<HookableObject>() != null)
            {
                continue;
            }

            best = Mathf.Max(best, floorHits[i].point.y);
        }

        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    // ------------------------------------------------------------
    // 見た目（全員のPC）
    // ------------------------------------------------------------

    private void Update()
    {
        float step = appearSeconds > 0f ? Time.deltaTime / appearSeconds : 1f;
        foreach (GoalView view in Goals())
        {
            appear.TryGetValue(view.Goal, out float value);
            appear[view.Goal] = Mathf.MoveTowards(value, view.Shown ? 1f : 0f, step);
        }

        DrawAt(Time.time);
    }

    /// <summary>
    /// 渦を、その時刻の姿で描く（シーンに物を作らずに描く）。Update から呼ぶ。
    /// 画像で確かめるときは、好きな時刻と写すカメラを決めて呼べる（そのときは出きった姿で描く）。
    /// </summary>
    public void DrawAt(float now, Camera targetCamera = null)
    {
        if (vortexMaterial == null)
        {
            return;
        }

        discMesh ??= CreateDiscMesh();
        block ??= new MaterialPropertyBlock();
        RenderParams renderParams = new RenderParams(vortexMaterial)
        {
            matProps = block,
            camera = targetCamera,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
        };

        foreach (GoalView view in Goals())
        {
            float amount = targetCamera != null ? (view.Shown ? 1f : 0f) : appear.TryGetValue(view.Goal, out float a) ? a : 0f;
            if (amount <= 0f || !TryGetArea(view.Goal, out Area area))
            {
                continue;
            }

            block.SetColor(ColorId, view.Color);
            block.SetFloat(VortexTimeId, now);
            block.SetFloat(AppearId, amount);
            block.SetFloat(BrightnessId, brightness);
            float diameter = area.Radius * 2f;
            Graphics.RenderMesh(renderParams, discMesh, 0,
                Matrix4x4.TRS(area.Center + Vector3.up * lift, Quaternion.identity, new Vector3(diameter, 1f, diameter)));
        }
    }

    /// <summary>渦を出すゴール1つ分（どのゴールか・ふちの色・いま見えているか）。</summary>
    private struct GoalView
    {
        public Component Goal;
        public Color Color;
        public bool Shown;
    }

    private readonly List<GoalView> views = new List<GoalView>();

    /// <summary>このシーンの、渦を出すゴール。閉ざされたゴールには出さない。</summary>
    private List<GoalView> Goals()
    {
        views.Clear();
        Scene scene = gameObject.scene;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene && !goal.IsClosed)
            {
                views.Add(new GoalView { Goal = goal, Color = SpaceJunkTeams.TeamColor(goal.OwnerTeam), Shown = IsVisualShown(goal) });
            }
        }

        foreach (SpaceJunkLobbyGoal goal in SpaceJunkLobbyGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene)
            {
                views.Add(new GoalView { Goal = goal, Color = goal.BaseColor, Shown = true });
            }
        }

        return views;
    }

    /// <summary>ゴールの見た目が出ているか（始まりの演出で飛んでいる間は本物の見た目が隠れるので、渦も隠す）。</summary>
    private bool IsVisualShown(SpaceJunkGoal goal)
    {
        if (!visuals.TryGetValue(goal, out MeshRenderer[] renderers))
        {
            renderers = goal.IntroVisual != null ? goal.IntroVisual.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
            visuals[goal] = renderers;
        }

        if (renderers.Length == 0)
        {
            return true;
        }

        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer != null && renderer.enabled)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>床に寝かせた板（幅・奥行き 1、真ん中が原点）。UV は真ん中が 0、ふちが ±1。</summary>
    private static Mesh CreateDiscMesh()
    {
        Mesh mesh = new Mesh { name = "GoalVortexDisc" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
        };
        mesh.uv = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>エディタで選んだときに、渦の円柱（判定の形）を線で描く。</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.2f, 1f);
        foreach (SpaceJunkGoal goal in FindObjectsByType<SpaceJunkGoal>(FindObjectsSortMode.None))
        {
            if (goal.gameObject.scene == gameObject.scene && MeasureArea(goal, fill, out Area area)) DrawCylinderGizmo(area);
        }
        foreach (SpaceJunkLobbyGoal goal in FindObjectsByType<SpaceJunkLobbyGoal>(FindObjectsSortMode.None))
        {
            if (goal.gameObject.scene == gameObject.scene && MeasureArea(goal, fill, out Area area)) DrawCylinderGizmo(area);
        }
    }

    private void DrawCylinderGizmo(Area area)
    {
        Vector3 bottom = area.Center + Vector3.down * depthBelowFloor;
        Vector3 top = new Vector3(area.Center.x, area.Top, area.Center.z);
        const int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * area.Radius;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * area.Radius;
            Gizmos.DrawLine(bottom + p0, bottom + p1);
            Gizmos.DrawLine(top + p0, top + p1);
            if (i % 8 == 0) Gizmos.DrawLine(bottom + p0, top + p0);
        }
    }
}
