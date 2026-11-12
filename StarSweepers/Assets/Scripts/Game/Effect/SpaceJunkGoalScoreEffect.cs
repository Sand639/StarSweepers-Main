using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ゴールに得点が入った瞬間、そのゴールのまわりをチームの色で光らせる演出。
/// 説明は Documents/スクリプトの説明/Game/Effect/SpaceJunkGoalScoreEffect.md
/// </summary>
public class SpaceJunkGoalScoreEffect : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("光の見た目（Art/Materials/GoalScoreGlow。筋の濃さ・輪の太さ・白っぽさはマテリアルで変える）")]
    [SerializeField] private Material glowMaterial;

    [Tooltip("光ってから消えるまでの秒数")]
    [SerializeField] private float duration = 1.3f;

    [Tooltip("全体の明るさ")]
    [SerializeField] private float brightness = 1.3f;

    [Header("光の壁（ゴールのまわりを囲む）")]
    [Tooltip("光の壁の高さ（m）")]
    [SerializeField] private float wallHeight = 3f;

    [Tooltip("ゴールのふちから、光の壁までのすき間（m）")]
    [SerializeField] private float wallOutset = 0.12f;

    [Tooltip("光の壁の上の端が、外へ開く距離（m）。上から見下ろすカメラでも光の帯が見えるように")]
    [SerializeField] private float wallFlare = 0.8f;

    [Tooltip("光の壁がのびきるまでの秒数")]
    [SerializeField] private float wallRiseSeconds = 0.2f;

    [Header("光の輪（床の高さで外へ広がる）")]
    [Tooltip("光の輪が、ゴールのふちから外へ広がる距離（m）")]
    [SerializeField] private float ringSpread = 2.2f;

    [Header("光の粒（ふちから立ちのぼる）")]
    [Tooltip("光の粒の数")]
    [SerializeField] private int sparkCount = 64;

    [Tooltip("光の粒がのぼる高さ（m）")]
    [SerializeField] private float sparkRise = 3.2f;

    [Tooltip("光の粒の大きさ（m）")]
    [SerializeField] private float sparkSize = 0.2f;

    [Tooltip("光の粒が外へ広がる距離（m）")]
    [SerializeField] private float sparkSpread = 0.7f;

    [Header("その他")]
    [Tooltip("1つのゴールで同時に出す演出の数の上限（続けて入ったとき、古いものから消す）")]
    [SerializeField] private int maxPerGoal = 3;

    /// <summary>光らせている途中の演出1つぶん。</summary>
    private struct Burst
    {
        public Object Key;       // どのゴールの演出か（上限を数えるため）
        public Vector3 Base;     // 足もとの真ん中（世界の位置）
        public Quaternion Yaw;   // ゴールの向き（左右の回転だけ）
        public Vector2 Size;     // 光の壁が囲む四角の幅と奥行き（m）
        public Color Color;
        public float StartTime;
    }

    private readonly List<Burst> bursts = new List<Burst>();
    private MaterialPropertyBlock block;
    private static Mesh wallMesh;
    private static Mesh ringMesh;
    private Mesh sparkMesh;
    private int sparkMeshCount = -1;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int PartId = Shader.PropertyToID("_Part");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int SparkId = Shader.PropertyToID("_Spark");

    /// <summary>出した演出の数（確かめる用）。</summary>
    public int PlayCount { get; private set; }

    /// <summary>いま光っている途中の演出の数（確かめる用）。</summary>
    public int ActiveCount => bursts.Count;

    private void OnEnable()
    {
        SpaceJunkGoal.Scored += OnGoalScored;
    }

    private void OnDisable()
    {
        SpaceJunkGoal.Scored -= OnGoalScored;
        bursts.Clear();
    }

    private void OnDestroy()
    {
        DestroyMesh(sparkMesh);
    }

    /// <summary>作った形を消す（画像で確かめるときは再生していないので、そのときは DestroyImmediate）。</summary>
    private static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }
    }

    /// <summary>ゴールに得点が入った知らせ（全員のPCで届く）。同じシーンのゴールだけ光らせる。</summary>
    private void OnGoalScored(SpaceJunkGoal goal)
    {
        if (goal != null && goal.gameObject.scene == gameObject.scene)
        {
            Play(goal, Time.time);
        }
    }

    /// <summary>そのゴールのまわりを、持ち主のチームの色で光らせる。</summary>
    public void Play(SpaceJunkGoal goal, float startTime)
    {
        if (goal == null || !TryGetGoalArea(goal, wallOutset, out Vector3 basePoint, out Quaternion yaw, out Vector2 size))
        {
            return;
        }

        Play(goal, basePoint, yaw, size, SpaceJunkTeams.TeamColor(goal.OwnerTeam), startTime);
    }

    /// <summary>決めた場所・大きさ・色で光らせる（key は上限を数えるための目印。null でもよい）。</summary>
    public void Play(Object key, Vector3 basePoint, Quaternion yaw, Vector2 size, Color color, float startTime)
    {
        // 同じゴールで上限を超えたら、いちばん古いものを消す
        int same = 0;
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            if (bursts[i].Key == key && ++same >= Mathf.Max(1, maxPerGoal))
            {
                bursts.RemoveAt(i);
            }
        }

        bursts.Add(new Burst { Key = key, Base = basePoint, Yaw = yaw, Size = size, Color = color, StartTime = startTime });
        PlayCount++;
    }

    /// <summary>
    /// ゴールの足もとの真ん中・向き・囲む四角の大きさを求める。
    /// 大きさは見た目（Intro Visual。無ければ Pad Renderer）の形から、高さはゴールの判定の底から取る。
    /// </summary>
    public static bool TryGetGoalArea(SpaceJunkGoal goal, float outset, out Vector3 basePoint, out Quaternion yaw, out Vector2 size)
    {
        yaw = Quaternion.Euler(0f, goal.transform.eulerAngles.y, 0f);
        Matrix4x4 toLocal = Matrix4x4.TRS(goal.transform.position, yaw, Vector3.one).inverse;

        bool found = false;
        Bounds local = default;
        Transform visual = goal.IntroVisual;
        if (visual != null)
        {
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh != null)
                {
                    EncapsulateMesh(filter.sharedMesh.bounds, toLocal * filter.transform.localToWorldMatrix, ref local, ref found);
                }
            }
        }

        // ゴールの判定（当たり判定）。見た目が無いときは大きさにも使う
        Collider area = goal.GetComponent<Collider>();
        if (!found && area != null)
        {
            Bounds b = area.bounds;
            EncapsulateMesh(new Bounds(b.center, new Vector3(b.size.x, 0f, b.size.z)), toLocal, ref local, ref found);
        }

        if (!found)
        {
            basePoint = goal.transform.position;
            size = Vector2.zero;
            return false;
        }

        // 足もとの高さ：判定があれば、その底（素材が入る面）。無ければ見た目の上面。
        // まわりの床がそれより少し高い・低いマップ（電車マップ2は床が 0.25m 高い）では、床の上面に合わせる（光の根元が床に埋もれないように）
        float baseY = area != null ? area.bounds.min.y : goal.transform.position.y + local.max.y;
        Vector3 centerWorld = Matrix4x4.TRS(goal.transform.position, yaw, Vector3.one).MultiplyPoint3x4(new Vector3(local.center.x, 0f, local.center.z));
        size = new Vector2(local.size.x + outset * 2f, local.size.z + outset * 2f);
        baseY = FloorHeightAround(new Vector3(centerWorld.x, baseY, centerWorld.z), yaw, size, baseY);
        basePoint = new Vector3(centerWorld.x, baseY, centerWorld.z);
        return true;
    }

    /// <summary>床を探す範囲：足もとの高さから上へ何 m・下へ何 m まで見るか。これより高い物は壁とみなして無視する。</summary>
    private const float FloorSearchAbove = 0.6f;
    private const float FloorSearchBelow = 0.5f;
    private static readonly RaycastHit[] floorHits = new RaycastHit[16];

    /// <summary>
    /// 囲む四角の4辺のすぐ外の、動かない床の上面のいちばん高い所。見つからなければ fallback のまま。
    /// 物資（HookableObject。参加者のPCでは Kinematic になる）・動く Rigidbody・プレイヤー（CharacterController）は床に数えない。
    /// </summary>
    private static float FloorHeightAround(Vector3 center, Quaternion yaw, Vector2 size, float fallback)
    {
        float best = float.NegativeInfinity;
        Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        foreach (Vector3 side in sides)
        {
            Vector3 offset = yaw * Vector3.Scale(side, new Vector3(size.x * 0.5f + 0.4f, 0f, size.y * 0.5f + 0.4f));
            Vector3 from = center + offset + Vector3.up * FloorSearchAbove;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, floorHits, FloorSearchAbove + FloorSearchBelow, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = floorHits[i].collider;
                if (hit is CharacterController || (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) ||
                    hit.GetComponentInParent<HookableObject>() != null)
                {
                    continue;
                }

                best = Mathf.Max(best, floorHits[i].point.y);
            }
        }

        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    /// <summary>形の箱の8つの角を、matrix で動かした先で包む。</summary>
    private static void EncapsulateMesh(Bounds meshBounds, Matrix4x4 matrix, ref Bounds into, ref bool found)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            Vector3 corner = matrix.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents, sign));
            if (found)
            {
                into.Encapsulate(corner);
            }
            else
            {
                into = new Bounds(corner, Vector3.zero);
                found = true;
            }
        }
    }

    private void Update()
    {
        DrawAt(Time.time);
    }

    /// <summary>
    /// 光っている演出を、その時刻の姿で描く（シーンに物を作らずに描く）。Update から呼ぶ。
    /// 画像で確かめるときは、好きな時刻と写すカメラを決めて呼べる（camera が null なら全部のカメラに写る）。
    /// </summary>
    public void DrawAt(float now, Camera targetCamera = null)
    {
        if (glowMaterial == null)
        {
            bursts.Clear();
            return;
        }

        if (wallMesh == null)
        {
            wallMesh = CreateWallMesh();
            ringMesh = CreateRingMesh();
        }

        if (sparkMesh == null || sparkMeshCount != sparkCount)
        {
            DestroyMesh(sparkMesh);
            sparkMeshCount = sparkCount;
            sparkMesh = CreateSparkMesh(Mathf.Max(0, sparkCount));
        }

        block ??= new MaterialPropertyBlock();
        RenderParams renderParams = new RenderParams(glowMaterial)
        {
            matProps = block,
            camera = targetCamera,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
        };

        float seconds = Mathf.Max(0.05f, duration);
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            Burst burst = bursts[i];
            float t = (now - burst.StartTime) / seconds;
            if (t >= 1f)
            {
                bursts.RemoveAt(i);
                continue;
            }

            if (t < 0f)
            {
                continue;
            }

            DrawBurst(renderParams, burst, t);
        }
    }

    /// <summary>演出1つを、進み具合 t（0〜1）の姿で描く。</summary>
    private void DrawBurst(RenderParams renderParams, Burst burst, float t)
    {
        // はじめにぱっと明るく、そのあと少し残ってから消える
        float flash = 1f + 0.8f * (1f - Mathf.Clamp01(t / 0.12f));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, t));
        float intensity = brightness * flash * fade;
        float elapsed = t * Mathf.Max(0.05f, duration);

        block.SetColor(ColorId, burst.Color);
        block.SetFloat(ProgressId, t);

        // 光の壁：のびながら出て、消えるときは少し縮む
        float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, wallRiseSeconds)), 3f);
        float height = wallHeight * (0.1f + 0.9f * rise) * (1f - 0.25f * Mathf.InverseLerp(0.5f, 1f, t));
        block.SetFloat(PartId, 0f);
        block.SetFloat(IntensityId, intensity);
        block.SetVector(SizeId, new Vector4(burst.Size.x, burst.Size.y, ringSpread, 0f));
        block.SetVector(SparkId, new Vector4(sparkRise, sparkSize, sparkSpread, wallFlare * rise));
        Graphics.RenderMesh(renderParams, wallMesh, 0, Matrix4x4.TRS(burst.Base, burst.Yaw, new Vector3(burst.Size.x, height, burst.Size.y)));

        // 光の輪：ふちから外へ広がりながら薄くなる（床とちらつかないよう少し浮かせる）
        float ringRadius = ringSpread * (1f - (1f - t) * (1f - t));
        block.SetFloat(PartId, 1f);
        block.SetFloat(IntensityId, brightness * flash * (1f - t) * (1f - t));
        block.SetVector(SizeId, new Vector4(burst.Size.x, burst.Size.y, ringSpread, ringRadius));
        Vector3 ringScale = new Vector3(burst.Size.x + ringSpread * 2f, 1f, burst.Size.y + ringSpread * 2f);
        Graphics.RenderMesh(renderParams, ringMesh, 0, Matrix4x4.TRS(burst.Base + Vector3.up * 0.03f, burst.Yaw, ringScale));

        // 光の粒：ふちから時間差で立ちのぼる（高さは m のまま使うので、縦は拡大しない）
        if (sparkMesh != null && sparkCount > 0)
        {
            block.SetFloat(PartId, 2f);
            block.SetFloat(IntensityId, brightness * flash * fade);
            block.SetVector(SparkId, new Vector4(sparkRise, sparkSize, sparkSpread, wallFlare));
            Graphics.RenderMesh(renderParams, sparkMesh, 0, Matrix4x4.TRS(burst.Base, burst.Yaw, new Vector3(burst.Size.x, 1f, burst.Size.y)));
        }
    }

    /// <summary>四角い筒（幅・奥行き 1、高さ 0〜1、上と下は開いている）。UV の y が高さ。</summary>
    private static Mesh CreateWallMesh()
    {
        Vector3[] corners =
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f),
        };

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int side = 0; side < 4; side++)
        {
            Vector3 a = corners[side];
            Vector3 b = corners[(side + 1) % 4];
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(a + Vector3.up);
            vertices.Add(b + Vector3.up);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));
            triangles.AddRange(new[] { start, start + 2, start + 1, start + 2, start + 3, start + 1 });
        }

        Mesh mesh = new Mesh { name = "GoalScoreWall" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>床に寝かせた板（幅・奥行き 1、真ん中が原点）。</summary>
    private static Mesh CreateRingMesh()
    {
        Mesh mesh = new Mesh { name = "GoalScoreRing" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 光の粒の板（count 枚）。どれも足もとは四角（幅・奥行き 1）のふちの上で、のぼり方はシェーダーが決める。
    /// 乱数は毎回同じ（どのPCでも同じ並び）。
    /// </summary>
    private static Mesh CreateSparkMesh(int count)
    {
        var random = new System.Random(4096);
        var vertices = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var seeds = new Vector2[count * 4];
        var triangles = new int[count * 6];
        Vector2[] cornersUv = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };

        for (int i = 0; i < count; i++)
        {
            // ふちの上の場所（4辺を1周する位置）
            float around = (float)random.NextDouble() * 4f;
            int side = Mathf.FloorToInt(around) % 4;
            float along = around - Mathf.Floor(around) - 0.5f;
            Vector3 root = side switch
            {
                0 => new Vector3(along, 0f, -0.5f),
                1 => new Vector3(0.5f, 0f, along),
                2 => new Vector3(-along, 0f, 0.5f),
                _ => new Vector3(-0.5f, 0f, -along),
            };
            Vector2 seed = new Vector2((float)random.NextDouble(), (float)random.NextDouble());

            for (int c = 0; c < 4; c++)
            {
                vertices[i * 4 + c] = root;
                uvs[i * 4 + c] = cornersUv[c];
                seeds[i * 4 + c] = seed;
            }

            int v = i * 4;
            int k = i * 6;
            triangles[k] = v;
            triangles[k + 1] = v + 2;
            triangles[k + 2] = v + 1;
            triangles[k + 3] = v + 2;
            triangles[k + 4] = v + 3;
            triangles[k + 5] = v + 1;
        }

        Mesh mesh = new Mesh { name = "GoalScoreSparks" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = seeds;
        mesh.triangles = triangles;
        // 頂点はシェーダーで動かすので、のぼる高さと広がりのぶんまで大きめの箱にしておく（画面の外と判定されて消えないように）
        mesh.bounds = new Bounds(new Vector3(0f, 4f, 0f), new Vector3(3f, 10f, 3f));
        return mesh;
    }
}
