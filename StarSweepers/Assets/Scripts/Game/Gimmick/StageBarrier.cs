using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ステージのまわりを囲む見えない壁（バリア）。当たった所に波紋を出す。
/// 説明は Documents/スクリプトの説明/Game/Gimmick/StageBarrier.md
/// </summary>
public class StageBarrier : MonoBehaviour
{
    [Header("囲む範囲")]
    [Tooltip("ON：再生を始めたとき、このシーンの床（動かない当たり判定）を測って、その外まわりを囲む。OFF：下の Center と Size で囲む")]
    [SerializeField] private bool autoFit = true;

    [Tooltip("自動で測るとき、床のふちから壁の内側までのすき間（m）")]
    [SerializeField] private float margin = 0.3f;

    [Tooltip("自動で測るとき、床とみなす当たり判定の上面の高さの範囲（m）。これより高い物・低い物は測らない")]
    [SerializeField] private Vector2 floorHeightRange = new Vector2(-4f, 4f);

    [Tooltip("Auto Fit が OFF のときに囲む四角の真ん中（世界の位置。Y は床の高さ）")]
    [SerializeField] private Vector3 center = Vector3.zero;

    [Tooltip("Auto Fit が OFF のときに囲む四角の大きさ（X＝東西、Y＝南北。壁の内側どうしの距離）")]
    [SerializeField] private Vector2 size = new Vector2(30f, 30f);

    [Header("壁")]
    [Tooltip("床から上の壁の高さ（m）。投げた物が越えないくらいにする")]
    [SerializeField] private float height = 12f;

    [Tooltip("床より下へ伸ばす長さ（m）。床のすぐ下をくぐって落ちないように")]
    [SerializeField] private float depthBelow = 4f;

    [Tooltip("壁の厚み（m）。速い物がすり抜けないよう、薄くしすぎない")]
    [SerializeField] private float thickness = 1f;

    [Header("波紋")]
    [Tooltip("波紋の見た目（Art/Materials/BarrierRipple。色や波の数はマテリアルで変える）")]
    [SerializeField] private Material rippleMaterial;

    [Tooltip("波紋が広がりきって消えるまでの秒数")]
    [SerializeField] private float rippleSeconds = 0.9f;

    [Tooltip("波紋が広がりきったときの半径（m）")]
    [SerializeField] private float rippleRadius = 2.5f;

    [Tooltip("同じ物が続けて当たったとき、次の波紋を出すまでの秒数")]
    [SerializeField] private float rippleCooldown = 0.3f;

    [Tooltip("壁からこの距離（m）まで近づいたら「当たった」とみなす")]
    [SerializeField] private float touchDistance = 0.15f;

    /// <summary>プレイヤーの体の半径の目安（m）。ほかの人のプレイヤーを位置で調べるときに使う。</summary>
    private const float PlayerRadius = 0.4f;

    /// <summary>プレイヤーの胸の高さ（m）。波紋を出す高さ。</summary>
    private const float PlayerChestHeight = 0.7f;

    private readonly List<BoxCollider> walls = new List<BoxCollider>();
    private readonly HashSet<Collider> stageColliders = new HashSet<Collider>();
    private readonly HashSet<Transform> touching = new HashSet<Transform>();
    private readonly HashSet<Transform> touchingNow = new HashSet<Transform>();
    private readonly Dictionary<Transform, float> lastRipple = new Dictionary<Transform, float>();
    private readonly Collider[] overlapBuffer = new Collider[64];

    /// <summary>広がっている途中の波紋1つぶん。</summary>
    private struct Ripple
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float StartTime;
    }

    private readonly List<Ripple> ripples = new List<Ripple>();
    private MaterialPropertyBlock rippleBlock;
    private static Mesh quad;
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");

    /// <summary>囲んでいる四角（壁の内側の面。世界の X と Z）。</summary>
    public Rect Area { get; private set; }

    /// <summary>出した波紋の数（確かめる用）。</summary>
    public int RippleCount { get; private set; }

    private void Start()
    {
        float lowestTop = center.y;
        float highestTop = center.y;
        Rect area = new Rect(center.x - size.x * 0.5f, center.z - size.y * 0.5f, size.x, size.y);

        if (autoFit)
        {
            if (MeasureStage(out Rect measured, out lowestTop, out highestTop, stageColliders))
            {
                area = new Rect(measured.xMin - margin, measured.yMin - margin, measured.width + margin * 2f, measured.height + margin * 2f);
            }
            else
            {
                lowestTop = center.y;
                highestTop = center.y;
                Debug.LogWarning($"[BARRIER] {gameObject.scene.name} に床が見つからないので、Center と Size で囲みます。");
            }
        }

        BuildWalls(area, lowestTop, highestTop);
    }

    /// <summary>このシーンの床を囲む四角と、上面のいちばん低い・高い高さを測る（collect に測った当たり判定を入れる）。</summary>
    public bool MeasureStage(out Rect area, out float lowestTop, out float highestTop, HashSet<Collider> collect = null)
    {
        area = default;
        lowestTop = float.MaxValue;
        highestTop = 0f;
        bool found = false;
        Bounds all = default;

        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (!IsStageCollider(c))
            {
                continue;
            }

            Bounds b = c.bounds;
            if (b.max.y < floorHeightRange.x || b.max.y > floorHeightRange.y)
            {
                continue;
            }

            collect?.Add(c);
            lowestTop = Mathf.Min(lowestTop, b.max.y);
            if (found)
            {
                all.Encapsulate(b);
            }
            else
            {
                all = b;
                found = true;
            }
        }

        if (!found)
        {
            lowestTop = 0f;
            return false;
        }

        area = new Rect(all.min.x, all.min.z, all.size.x, all.size.z);
        highestTop = all.max.y;
        return true;
    }

    /// <summary>ステージの一部（動かない床・壁）の当たり判定か。</summary>
    private bool IsStageCollider(Collider c)
    {
        if (!c.enabled || c.isTrigger || c is CharacterController || c.gameObject.scene != gameObject.scene ||
            c.transform.IsChildOf(transform))
        {
            return false;
        }

        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
        {
            return false;
        }

        Bounds b = c.bounds;
        return b.size.x < 200f && b.size.z < 200f &&
               c.GetComponentInParent<HookableObject>() == null &&
               c.GetComponentInParent<CharacterController>() == null;
    }

    /// <summary>四角のまわりに、見えない壁を4枚作る。</summary>
    private void BuildWalls(Rect area, float lowestTop, float highestTop)
    {
        Area = area;
        float bottom = lowestTop - depthBelow;
        float top = highestTop + height;
        float midY = (bottom + top) * 0.5f;
        float h = top - bottom;
        float t = thickness;

        AddWall("Wall_North", new Vector3(area.center.x, midY, area.yMax + t * 0.5f), new Vector3(area.width + t * 2f, h, t));
        AddWall("Wall_South", new Vector3(area.center.x, midY, area.yMin - t * 0.5f), new Vector3(area.width + t * 2f, h, t));
        AddWall("Wall_East", new Vector3(area.xMax + t * 0.5f, midY, area.center.y), new Vector3(t, h, area.height));
        AddWall("Wall_West", new Vector3(area.xMin - t * 0.5f, midY, area.center.y), new Vector3(t, h, area.height));

        Debug.Log($"[BARRIER] {gameObject.scene.name} を囲みました（東西 {area.width:0.0}m × 南北 {area.height:0.0}m・" +
                  $"真ん中 ({area.center.x:0.0}, {area.center.y:0.0})・床の上面の高さ {lowestTop:0.00}〜{highestTop:0.00}m・{(autoFit ? "自動" : "手で指定")}）");
    }

    private void AddWall(string wallName, Vector3 position, Vector3 worldSize)
    {
        GameObject wall = new GameObject(wallName);
        wall.transform.SetParent(transform, false);
        wall.transform.SetPositionAndRotation(position, Quaternion.identity);

        // 親の拡大・縮小を打ち消す
        Vector3 scale = wall.transform.lossyScale;
        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = new Vector3(worldSize.x / scale.x, worldSize.y / scale.y, worldSize.z / scale.z);
        walls.Add(box);
    }

    private void Update()
    {
        DetectTouches();
        DrawRipples();
    }

    /// <summary>壁に触れた物を探し、触れ始めた所に波紋を出す（位置で調べるので、どのPCでも同じように出る）。</summary>
    private void DetectTouches()
    {
        touchingNow.Clear();

        // 始まりの演出中は、プレイヤーを乗せたゴールエリアが壁を通り抜けるので、波紋を出さない
        bool quiet = SpaceJunkRound.Current != null && SpaceJunkRound.Current.IsIntro;

        foreach (BoxCollider wall in walls)
        {
            Bounds bounds = wall.bounds;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * touchDistance, overlapBuffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == wall || stageColliders.Contains(other) || other.transform.IsChildOf(transform))
                {
                    continue;
                }

                // 物資は Rigidbody、自分のプレイヤーは CharacterController で数える
                Transform key = other is CharacterController ? other.transform :
                    other.attachedRigidbody != null ? other.attachedRigidbody.transform : null;
                if (key != null)
                {
                    Touch(key, wall, other.bounds.center, quiet);
                }
            }

            // ほかの人のプレイヤーは当たり判定が切れているので、位置と距離で調べる
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player == null || !player.isActiveAndEnabled)
                {
                    continue;
                }

                Vector3 chest = player.transform.position + Vector3.up * PlayerChestHeight;
                if ((bounds.ClosestPoint(chest) - chest).sqrMagnitude <= (PlayerRadius + touchDistance) * (PlayerRadius + touchDistance))
                {
                    Touch(player.transform, wall, chest, quiet);
                }
            }
        }

        // 離れた物は、次に触れたらまた波紋を出す
        touching.RemoveWhere(key => key == null || !touchingNow.Contains(key));

        if (lastRipple.Count > 256)
        {
            List<Transform> gone = new List<Transform>();
            foreach (Transform key in lastRipple.Keys)
            {
                if (key == null)
                {
                    gone.Add(key);
                }
            }
            foreach (Transform key in gone)
            {
                lastRipple.Remove(key);
            }
        }
    }

    /// <summary>触れている物を控え、触れ始めたときだけ波紋を出す（quiet なら控えるだけ）。</summary>
    private void Touch(Transform key, BoxCollider wall, Vector3 near, bool quiet)
    {
        if (!touchingNow.Add(key) || !touching.Add(key) || quiet)
        {
            return;
        }

        if (lastRipple.TryGetValue(key, out float last) && Time.time - last < rippleCooldown)
        {
            return;
        }

        lastRipple[key] = Time.time;
        SpawnRipple(wall, near);
    }

    /// <summary>壁の内側（ステージ側）の面の、当たった物にいちばん近い所に波紋を出す。</summary>
    private void SpawnRipple(BoxCollider wall, Vector3 near)
    {
        Bounds bounds = wall.bounds;
        Vector3 toStage = new Vector3(Area.center.x, bounds.center.y, Area.center.y) - bounds.center;

        // 壁の向き：薄い向き（X か Z）のうち、ステージ側
        Vector3 normal = bounds.size.x < bounds.size.z
            ? new Vector3(Mathf.Sign(toStage.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(toStage.z));

        Vector3 point = bounds.ClosestPoint(near);
        Vector3 face = bounds.center + Vector3.Scale(normal, bounds.extents);
        point = normal.x != 0f ? new Vector3(face.x, point.y, point.z) : new Vector3(point.x, point.y, face.z);

        ripples.Add(new Ripple
        {
            Position = point + normal * 0.02f,
            Rotation = Quaternion.LookRotation(-normal, Vector3.up),
            StartTime = Time.time,
        });
        RippleCount++;
    }

    /// <summary>広がっている波紋を描く（シーンに物を作らずに描く）。</summary>
    private void DrawRipples()
    {
        if (rippleMaterial == null)
        {
            ripples.Clear();
            return;
        }

        if (quad == null)
        {
            quad = CreateQuad();
        }

        rippleBlock ??= new MaterialPropertyBlock();
        RenderParams renderParams = new RenderParams(rippleMaterial)
        {
            matProps = rippleBlock,
            layer = gameObject.layer,
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };

        float diameter = rippleRadius * 2f;
        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            float progress = (Time.time - ripples[i].StartTime) / Mathf.Max(0.01f, rippleSeconds);
            if (progress >= 1f)
            {
                ripples.RemoveAt(i);
                continue;
            }

            rippleBlock.SetFloat(ProgressId, progress);
            Matrix4x4 matrix = Matrix4x4.TRS(ripples[i].Position, ripples[i].Rotation, new Vector3(diameter, diameter, 1f));
            Graphics.RenderMesh(renderParams, quad, 0, matrix);
        }
    }

    /// <summary>1m 四方の板（真ん中が原点、UV は 0〜1）。</summary>
    private static Mesh CreateQuad()
    {
        Mesh mesh = new Mesh { name = "BarrierRippleQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>選んだとき、壁の場所（再生前は自動で測った場所）を緑の枠で見せる。</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        if (walls.Count > 0)
        {
            foreach (BoxCollider wall in walls)
            {
                Gizmos.DrawWireCube(wall.bounds.center, wall.bounds.size);
            }
            return;
        }

        Rect area = new Rect(center.x - size.x * 0.5f, center.z - size.y * 0.5f, size.x, size.y);
        float lowestTop = center.y;
        float highestTop = center.y;
        if (autoFit && MeasureStage(out Rect measured, out lowestTop, out highestTop))
        {
            area = new Rect(measured.xMin - margin, measured.yMin - margin, measured.width + margin * 2f, measured.height + margin * 2f);
        }

        float bottom = lowestTop - depthBelow;
        float top = highestTop + height;
        Gizmos.DrawWireCube(new Vector3(area.center.x, (bottom + top) * 0.5f, area.center.y), new Vector3(area.width, top - bottom, area.height));
    }
}
