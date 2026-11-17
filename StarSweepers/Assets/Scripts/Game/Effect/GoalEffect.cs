using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// ゴールに常に出ている黒い渦。渦の円柱に入った物資を、小さくしながら回して中心へ吸い込む（吸い込み終わったら得点）。
/// 1つのシーンに1つ置けば、そのシーンの全部のゴールに出る。
/// 説明は Documents/スクリプトの説明/Game/Effect/GoalEffect.md
/// </summary>
public class GoalEffect : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("渦の見た目（Art/Materials/GoalVortex。腕の数・回る速さ・壁や帯の濃さ・かけらの大きさと速さはマテリアルで変える）")]
    [SerializeField] private Material vortexMaterial;

    [Tooltip("渦の半径＝ゴールの床の短い辺の半分×これ（1 で床いっぱい）")]
    [Range(0.3f, 1f)]
    [SerializeField] private float fill = 0.9f;

    [Tooltip("渦を床から浮かせる高さ（m。床とちらつかないように）")]
    [SerializeField] private float lift = 0.03f;

    [Tooltip("ゴールが着地してから、渦が出きるまでの秒数")]
    [SerializeField] private float appearSeconds = 0.6f;

    [Tooltip("明るい所（灰色の筋・ふち・帯）の明るさ")]
    [SerializeField] private float brightness = 1f;

    [Tooltip("ふちや帯の明るい所を、チームの色にするか（オフなら灰色。2026/11/17 に「黒と灰色で一回チームの色なしで」となったので、いまはオフ）")]
    [SerializeField] private bool useTeamColor = false;

    [Tooltip("チームの色を使わないときの、明るい所の色（灰色）")]
    [SerializeField] private Color neutralColor = new Color(0.78f, 0.78f, 0.8f);

    [Tooltip("床に平らな渦巻き模様（すり鉢の底の2Dの渦）を描くか。2026/11/17 に大槻さんの「消しちゃって大丈夫」でオフにした（判定の円柱には関係ない）")]
    [SerializeField] private bool showFloorVortex = false;

    [Header("すり鉢（床の渦の上に立つ、半透明の立体の渦）")]
    [Tooltip("すり鉢の高さ（m）。物資を吸い込む円柱より高くはならない。0 ならすり鉢を出さない")]
    [SerializeField] private float funnelHeight = 1.6f;

    [Tooltip("すり鉢の底（床）の半径＝渦の半径×これ")]
    [Range(0.05f, 0.9f)]
    [SerializeField] private float funnelBottom = 0.3f;

    [Tooltip("すり鉢の形の曲がり具合（1 でまっすぐな円すい。大きいほど底が細く深い）")]
    [Range(1f, 4f)]
    [SerializeField] private float funnelCurve = 1.8f;

    [Header("らせんの帯・かけら")]
    [Tooltip("すり鉢に巻きつく、らせんの帯の数（帯ごとに違う速さで回る）")]
    [Range(0, 12)]
    [SerializeField] private int ribbonCount = 6;

    [Tooltip("帯が、上から下までに何周巻きつくか（目安。帯ごとに少しずつ違う）")]
    [SerializeField] private float ribbonTurns = 1.3f;

    [Tooltip("帯の幅（すり鉢の高さに対する割合）")]
    [Range(0.01f, 0.3f)]
    [SerializeField] private float ribbonWidth = 0.13f;

    [Tooltip("らせんを描いて底へ吸い込まれていく、黒いかけらの数（大きさ・速さはマテリアルで変える）")]
    [Range(0, 64)]
    [SerializeField] private int debrisCount = 14;

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

    /// <summary>そのゴールのすり鉢の高さ（m）。円柱の高さを超えない。</summary>
    public float FunnelHeightOf(Area area) => Mathf.Clamp(funnelHeight, 0f, Mathf.Max(0f, area.Top - area.Center.y));

    /// <summary>渦の真ん中から水平に radius 離れた所の、すり鉢の内側の面の高さ（世界の y）。</summary>
    public float FunnelSurfaceY(Area area, float radius)
    {
        float bottom = area.Radius * funnelBottom;
        if (radius <= bottom || area.Radius <= bottom)
        {
            return area.Center.y;
        }
        float v = Mathf.Pow(Mathf.Clamp01((radius - bottom) / (area.Radius - bottom)), 1f / funnelCurve);
        return area.Center.y + v * FunnelHeightOf(area);
    }

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

    private static readonly List<GoalEffect> All = new List<GoalEffect>();
    private readonly Dictionary<Component, Area> areas = new Dictionary<Component, Area>();
    private readonly Dictionary<Component, float> appear = new Dictionary<Component, float>();
    private readonly Dictionary<Component, MeshRenderer[]> visuals = new Dictionary<Component, MeshRenderer[]>();
    private readonly Dictionary<HookableObject, float> lastPull = new Dictionary<HookableObject, float>();
    private MaterialPropertyBlock block;
    private static Mesh discMesh;
    private Mesh funnelMesh;
    private Vector2 funnelMeshShape;
    private Mesh ribbonMesh;
    private Vector4 ribbonMeshShape;
    private Mesh debrisMesh;
    private int debrisMeshCount = -1;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int VortexTimeId = Shader.PropertyToID("_VortexTime");
    private static readonly int AppearId = Shader.PropertyToID("_Appear");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int PartId = Shader.PropertyToID("_Part");
    private static readonly int FunnelId = Shader.PropertyToID("_Funnel");

    /// <summary>そのシーンの渦（無ければ null）。</summary>
    public static GoalEffect In(Scene scene)
    {
        foreach (GoalEffect vortex in All)
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
            float start = GoalAbsorb.ClockOf(item);
            GoalAbsorb.Begin(item, area, start, absorbSeconds, done =>
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
        if (funnelMesh == null || funnelMeshShape != new Vector2(funnelBottom, funnelCurve))
        {
            DestroyMesh(funnelMesh);
            funnelMeshShape = new Vector2(funnelBottom, funnelCurve);
            funnelMesh = CreateFunnelMesh(funnelBottom, funnelCurve);
        }

        Vector4 ribbonShape = new Vector4(funnelBottom, funnelCurve, ribbonCount + ribbonTurns * 100f, ribbonWidth);
        if (ribbonMesh == null || ribbonMeshShape != ribbonShape)
        {
            DestroyMesh(ribbonMesh);
            ribbonMeshShape = ribbonShape;
            ribbonMesh = CreateRibbonMesh(funnelBottom, funnelCurve, ribbonCount, ribbonTurns, ribbonWidth);
        }

        if (debrisMesh == null || debrisMeshCount != debrisCount)
        {
            DestroyMesh(debrisMesh);
            debrisMeshCount = debrisCount;
            debrisMesh = CreateDebrisMesh(debrisCount);
        }

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
            Vector3 floorPoint = area.Center + Vector3.up * lift;
            if (showFloorVortex)
            {
                block.SetFloat(PartId, 0f);
                Graphics.RenderMesh(renderParams, discMesh, 0,
                    Matrix4x4.TRS(floorPoint, Quaternion.identity, new Vector3(diameter, 1f, diameter)));
            }

            // すり鉢の壁・らせんの帯・かけら（出てくるときは、高さも下から伸びる）
            float height = FunnelHeightOf(area) * amount;
            if (height > 0.01f)
            {
                block.SetVector(FunnelId, new Vector4(funnelBottom, funnelCurve, area.Radius, height));
                Matrix4x4 funnelMatrix = Matrix4x4.TRS(floorPoint, Quaternion.identity, new Vector3(area.Radius, height, area.Radius));
                block.SetFloat(PartId, 1f);
                Graphics.RenderMesh(renderParams, funnelMesh, 0, funnelMatrix);

                if (ribbonCount > 0)
                {
                    block.SetFloat(PartId, 2f);
                    Graphics.RenderMesh(renderParams, ribbonMesh, 0, funnelMatrix);
                }

                if (debrisCount > 0)
                {
                    block.SetFloat(PartId, 3f);
                    Graphics.RenderMesh(renderParams, debrisMesh, 0, Matrix4x4.TRS(floorPoint, Quaternion.identity, Vector3.one));
                }
            }
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
                views.Add(new GoalView { Goal = goal, Color = useTeamColor ? SpaceJunkTeams.TeamColor(goal.OwnerTeam) : neutralColor, Shown = IsVisualShown(goal) });
            }
        }

        foreach (SpaceJunkLobbyGoal goal in SpaceJunkLobbyGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene)
            {
                views.Add(new GoalView { Goal = goal, Color = useTeamColor ? goal.BaseColor : neutralColor, Shown = true });
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

    private void OnDestroy()
    {
        DestroyMesh(funnelMesh);
        DestroyMesh(ribbonMesh);
        DestroyMesh(debrisMesh);
    }

    /// <summary>
    /// らせんの帯（すり鉢と同じ大きさの形の中。上のふちの半径 1・高さ 1）。帯ごとに、上の端から下の端へ、上から見て左回りに巻きつく。
    /// UV の x は長さ方向（0＝上の端）、y は幅方向。2つ目の UV の x は回る速さ（回転/秒）、y は帯の乱数。乱数は毎回同じ（どのPCでも同じ）。
    /// </summary>
    private static Mesh CreateRibbonMesh(float bottom, float curve, int count, float turns, float width)
    {
        const int segments = 72;
        var random = new System.Random(2027);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var seeds = new List<Vector2>();
        var triangles = new List<int>();

        for (int k = 0; k < count; k++)
        {
            float vTop = Mathf.Lerp(0.75f, 1f, (float)random.NextDouble());
            float vBottom = Mathf.Lerp(0.02f, 0.3f, (float)random.NextDouble());
            float myTurns = turns * Mathf.Lerp(0.75f, 1.3f, (float)random.NextDouble());
            float start = (float)random.NextDouble() * Mathf.PI * 2f;
            float outside = Mathf.Lerp(1.02f, 1.14f, (float)random.NextDouble());
            float half = width * Mathf.Lerp(0.6f, 1.4f, (float)random.NextDouble()) * 0.5f;
            Vector2 seed = new Vector2(Mathf.Lerp(0.12f, 0.6f, (float)random.NextDouble()), (float)random.NextDouble());

            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float along = i / (float)segments;
                float v = Mathf.Lerp(vTop, vBottom, along);
                float angle = start + myTurns * Mathf.PI * 2f * along;
                for (int side = 0; side < 2; side++)
                {
                    float sv = Mathf.Clamp01(v + (side == 0 ? -half : half));
                    float radius = (bottom + (1f - bottom) * Mathf.Pow(sv, curve)) * outside;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, sv, Mathf.Sin(angle) * radius));
                    uvs.Add(new Vector2(along, side));
                    seeds.Add(seed);
                }
            }

            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
        }

        Mesh mesh = new Mesh { name = "GoalVortexRibbons" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, seeds);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // シェーダーで回すので、回っても入る大きさの箱にしておく
        mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2.6f, 1.2f, 2.6f));
        return mesh;
    }

    /// <summary>
    /// 黒いかけら（count 個の、少しずついびつな三角すい。大きさ 1 前後、真ん中が原点）。
    /// 動き（らせんを描いて底へ下りる・くるくる回る）はシェーダーが決める。2つ目の UV がかけらごとの乱数。
    /// </summary>
    private static Mesh CreateDebrisMesh(int count)
    {
        var random = new System.Random(4096);
        Vector3[] corners = { new Vector3(1f, 1f, 1f), new Vector3(-1f, -1f, 1f), new Vector3(-1f, 1f, -1f), new Vector3(1f, -1f, -1f) };
        int[,] faces = { { 0, 1, 2 }, { 0, 3, 1 }, { 0, 2, 3 }, { 1, 3, 2 } };
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var seeds = new List<Vector2>();
        var triangles = new List<int>();

        for (int k = 0; k < count; k++)
        {
            Vector3 stretch = new Vector3(Mathf.Lerp(0.5f, 1.2f, (float)random.NextDouble()),
                Mathf.Lerp(0.3f, 0.9f, (float)random.NextDouble()), Mathf.Lerp(0.5f, 1.2f, (float)random.NextDouble())) * 0.5f;
            Vector2 seed = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            for (int f = 0; f < 4; f++)
            {
                Vector3 a = Vector3.Scale(corners[faces[f, 0]], stretch);
                Vector3 b = Vector3.Scale(corners[faces[f, 1]], stretch);
                Vector3 c = Vector3.Scale(corners[faces[f, 2]], stretch);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(normal, a + b + c) < 0f) normal = -normal;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                seeds.Add(seed); seeds.Add(seed); seeds.Add(seed);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
        }

        Mesh mesh = new Mesh { name = "GoalVortexDebris" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(1, seeds);
        mesh.SetTriangles(triangles, 0);
        // 位置はシェーダーで渦のまわりへ動かすので、渦が入る大きさの箱にしておく
        mesh.bounds = new Bounds(new Vector3(0f, 3f, 0f), new Vector3(10f, 8f, 10f));
        return mesh;
    }

    private static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }

    /// <summary>
    /// すり鉢の壁（上のふちの半径 1・高さ 1。底の半径は bottom。上も下も開いている）。
    /// 半径は高さ v で bottom＋(1−bottom)×v^curve（下ほど細く、上ほど外へ開く）。形の中の位置の y が高さ（シェーダーが使う）。
    /// </summary>
    private static Mesh CreateFunnelMesh(float bottom, float curve)
    {
        const int around = 48;
        const int rings = 14;
        var vertices = new Vector3[(around + 1) * (rings + 1)];
        var triangles = new int[around * rings * 6];
        for (int j = 0; j <= rings; j++)
        {
            float v = j / (float)rings;
            float radius = bottom + (1f - bottom) * Mathf.Pow(v, curve);
            for (int i = 0; i <= around; i++)
            {
                float a = i * Mathf.PI * 2f / around;
                vertices[j * (around + 1) + i] = new Vector3(Mathf.Cos(a) * radius, v, Mathf.Sin(a) * radius);
            }
        }

        int k = 0;
        for (int j = 0; j < rings; j++)
        {
            for (int i = 0; i < around; i++)
            {
                int a = j * (around + 1) + i;
                int b = a + around + 1;
                triangles[k++] = a; triangles[k++] = b; triangles[k++] = a + 1;
                triangles[k++] = a + 1; triangles[k++] = b; triangles[k++] = b + 1;
            }
        }

        Mesh mesh = new Mesh { name = "GoalVortexFunnel" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
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
