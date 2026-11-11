using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **ベルトコンベアの角（90度のカーブ）。** 乗っている物資と人を、内側の角を中心にした円に沿って運ぶ。
/// 置く場所は内側の角（カーブの中心）・高さはベルトの上面。向き 0 の右曲がりで「東から入って西へ進み、北へ出る」。
/// 説明は `Documents/スクリプトの説明/Game/Gimmick/SpaceJunkConveyorCorner.md`。
/// </summary>
public class SpaceJunkConveyorCorner : MonoBehaviour
{
    [Header("流れ方")]
    [Tooltip("流れる速さ（1秒あたりのメートル）。つながるまっすぐのベルトと同じにする。マイナスにすると逆向きに流れる")]
    [SerializeField] private float speed = 3f;

    [Tooltip("ON：右曲がり（向き 0 で、東から入って北へ出る）。OFF：左曲がり（向き 0 で、東から入って南へ出る）")]
    [SerializeField] private bool turnRight = true;

    [Tooltip("物資をベルトの速さに合わせる強さ（まっすぐのベルトの Item Grip と同じ意味）")]
    [SerializeField] private float itemGrip = 4f;

    [Tooltip("ベルトの上面から、どの高さまでを「乗っている」とみなすか（m）")]
    [SerializeField] private float carryHeight = 1.2f;

    [Header("大きさ")]
    [Tooltip("カーブの外側の半径（m）。内側の角から外側の縁まで（ロビーの曲がる部品は 2.75）")]
    [SerializeField] private float radius = 2.75f;

    [Tooltip("ベルトの厚み（m）。当たり判定は、置いた高さから下へこの厚みで作る")]
    [SerializeField] private float thickness = 0.19f;

    [Tooltip("これより中心に近い所では、この半径として回す（中心の近くで速く回りすぎないように）")]
    [SerializeField] private float minRadius = 0.5f;

    [Tooltip("角に入ったときの中心からの距離（レーン）に戻す強さ（1秒あたり）。0 なら戻さない（となりのベルトに押されて外へずれることがある）")]
    [SerializeField] private float laneKeeping = 3f;

    /// <summary>範囲の判定の遊び（m）。となりのベルトとの境目で途切れないように。</summary>
    private const float Margin = 0.05f;

    private readonly Collider[] overlapBuffer = new Collider[64];
    private readonly HashSet<Rigidbody> pushedThisStep = new HashSet<Rigidbody>();

    /// <summary>角に入ったときの、物資ごとの中心からの距離（レーン）。角から出たら消す。</summary>
    private readonly Dictionary<Rigidbody, float> lanes = new Dictionary<Rigidbody, float>();
    private readonly HashSet<Rigidbody> inCornerThisStep = new HashSet<Rigidbody>();
    private readonly List<Rigidbody> leftCorner = new List<Rigidbody>();

    private PhysicsMaterial slippery;
    private Mesh surfaceMesh;

    /// <summary>回る向き（Unity の Y 軸まわりの角度の符号。+1 で上から見て時計回り）。</summary>
    private float TurnSign => (turnRight ? 1f : -1f) * (speed >= 0f ? 1f : -1f);

    private void Awake()
    {
        // 上面の当たり判定（扇形）。すべるようにして、物資が転がらずに流れるようにする
        surfaceMesh = BuildSurfaceMesh();
        slippery = new PhysicsMaterial("ConveyorCorner")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        MeshCollider surface = gameObject.AddComponent<MeshCollider>();
        surface.sharedMesh = surfaceMesh;
        surface.convex = true;
        surface.sharedMaterial = slippery;
    }

    private void OnDestroy()
    {
        if (slippery != null) Destroy(slippery);
        if (surfaceMesh != null) Destroy(surfaceMesh);
    }

    /// <summary>その場所で流れる速さ（ワールド）。角の範囲の外なら false。</summary>
    public bool TryGetFlowVelocity(Vector3 worldPosition, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!TryGetRadial(worldPosition, out Vector3 radial, out _))
        {
            return false;
        }

        velocity = Tangent(radial) * Mathf.Abs(speed);
        return true;
    }

    // ------------------------------------------------------------
    // 運ぶ
    // ------------------------------------------------------------

    /// <summary>角の上に立っている（自分のPCで動かしている）プレイヤーを、円に沿って動かす。</summary>
    private void Update()
    {
        int count = OverlapAbove();
        for (int i = 0; i < count; i++)
        {
            FishingPlayerController player = overlapBuffer[i].GetComponentInParent<FishingPlayerController>();
            if (player == null || !player.isActiveAndEnabled)
            {
                continue;
            }

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled || controller != overlapBuffer[i])
            {
                continue;
            }

            if (!TryGetRadial(controller.transform.position, out _, out float r))
            {
                continue;
            }

            // 中心から見た位置を、進む距離の分だけ回す（まっすぐ動かすより、円からずれない）
            Vector3 offset = Vector3.ProjectOnPlane(controller.transform.position - transform.position, transform.up);
            float degrees = TurnSign * Mathf.Abs(speed) * Time.deltaTime / Mathf.Max(r, minRadius) * Mathf.Rad2Deg;
            controller.Move(Quaternion.AngleAxis(degrees, transform.up) * offset - offset);
        }
    }

    /// <summary>物資を円に沿って運ぶ。物理を動かしているPCでだけ効く（ほかのPCでは Is Kinematic になっている）。</summary>
    private void FixedUpdate()
    {
        int count = OverlapAbove();
        pushedThisStep.Clear();
        inCornerThisStep.Clear();
        float dt = Time.fixedDeltaTime;
        float grip = Mathf.Clamp01(itemGrip * dt);
        Vector3 up = transform.up;

        for (int i = 0; i < count; i++)
        {
            Rigidbody body = overlapBuffer[i].attachedRigidbody;
            if (body == null || body.isKinematic || !pushedThisStep.Add(body))
            {
                continue;
            }

            HookableObject item = body.GetComponent<HookableObject>();
            if (item != null && (item.IsHooked || item.IsVanished))
            {
                continue;
            }

            if (!TryGetRadial(body.worldCenterOfMass, out Vector3 radial, out float r))
            {
                continue;
            }

            inCornerThisStep.Add(body);
            Vector3 tangent = Tangent(radial);
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);

            // 円に沿って進んだ角度の分だけ、速さの向きも回す（これで外へふくらまずに曲がる）
            float along = Vector3.Dot(planar, tangent);
            float turnDegrees = TurnSign * along * dt / Mathf.Max(r, minRadius) * Mathf.Rad2Deg;
            Vector3 turned = Quaternion.AngleAxis(turnDegrees, up) * planar;

            // そのうえで、まっすぐのベルトと同じように、流れの速さとの差を縮める（上下の動きは物理に任せる）
            Vector3 wanted = Vector3.Lerp(turned, tangent * Mathf.Abs(speed), grip);

            // 入ったときのレーンへ戻す（入口・出口で、となりのまっすぐのベルトに押されて外へずれるのを打ち消す）
            if (!lanes.TryGetValue(body, out float lane))
            {
                lanes[body] = lane = r;
            }
            wanted -= radial * ((r - lane) * laneKeeping);

            body.AddForce(wanted - planar, ForceMode.VelocityChange);
        }

        // 角から出た物資のレーンは忘れる（また入ってきたら、そのときの距離を覚え直す）
        leftCorner.Clear();
        foreach (Rigidbody body in lanes.Keys)
        {
            if (body == null || !inCornerThisStep.Contains(body)) leftCorner.Add(body);
        }
        foreach (Rigidbody body in leftCorner)
        {
            lanes.Remove(body);
        }
    }

    // ------------------------------------------------------------
    // 形
    // ------------------------------------------------------------

    /// <summary>
    /// 角の範囲の中なら true。<paramref name="radial"/> は中心から外へ向かう向き（面に沿った長さ1）、<paramref name="r"/> は中心からの距離。
    /// 範囲は、向き 0 の右曲がりで中心の南西の4分の1の円（左曲がりは北西）。
    /// </summary>
    private bool TryGetRadial(Vector3 worldPosition, out Vector3 radial, out float r)
    {
        Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPosition - transform.position);
        Vector2 flat = new Vector2(local.x, local.z);
        r = flat.magnitude;
        radial = Vector3.zero;

        bool inQuarter = local.x <= Margin && (turnRight ? local.z <= Margin : local.z >= -Margin);
        bool inHeight = local.y >= -thickness - Margin && local.y <= carryHeight;
        if (!inQuarter || !inHeight || r > radius + Margin || r < 0.001f)
        {
            return false;
        }

        radial = transform.rotation * new Vector3(flat.x / r, 0f, flat.y / r);
        return true;
    }

    /// <summary>中心から外への向き <paramref name="radial"/> の所で、流れる向き（長さ1）。</summary>
    private Vector3 Tangent(Vector3 radial)
    {
        return Quaternion.AngleAxis(TurnSign * 90f, transform.up) * radial;
    }

    /// <summary>角の上の箱（4分の1の円を囲む四角）に入っている当たり判定を集める。数を返す。</summary>
    private int OverlapAbove()
    {
        float zSign = turnRight ? -1f : 1f;
        Vector3 center = transform.position + transform.rotation * new Vector3(-radius * 0.5f, carryHeight * 0.5f, zSign * radius * 0.5f);
        Vector3 halfExtents = new Vector3(radius * 0.5f + Margin, carryHeight * 0.5f, radius * 0.5f + Margin);
        return Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>上面の当たり判定の形（扇形の板。上面が置いた高さ、厚みは Thickness）。</summary>
    private Mesh BuildSurfaceMesh()
    {
        const int segments = 12;
        List<Vector3> vertices = new List<Vector3> { Vector3.zero, new Vector3(0f, -thickness, 0f) };
        for (int i = 0; i <= segments; i++)
        {
            Vector3 dir = ArcDirection(90f * i / segments);
            vertices.Add(dir * radius);
            vertices.Add(dir * radius + new Vector3(0f, -thickness, 0f));
        }

        // 上面と底面を扇で張る（凸の形として使うので、面の向きは問わない）
        List<int> triangles = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            int top = 2 + i * 2, nextTop = top + 2;
            triangles.AddRange(new[] { 0, top, nextTop, 1, nextTop + 1, top + 1, top, top + 1, nextTop + 1, top, nextTop + 1, nextTop });
        }

        Mesh mesh = new Mesh { name = "ConveyorCornerSurface" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>中心から見た向き（ローカル）。0 度が入口の側、90 度が出口の側。</summary>
    private Vector3 ArcDirection(float degrees)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(a), 0f, (turnRight ? -1f : 1f) * Mathf.Cos(a));
    }

    private void OnDrawGizmos()
    {
        // 流れる道を、内側と外側の弧と、真ん中の矢印で出す（まっすぐのベルトと同じ色）
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Vector3 lift = transform.up * 0.1f;
        const int segments = 12;
        float mid = radius * 0.5f;

        for (int i = 0; i < segments; i++)
        {
            Vector3 a = transform.TransformDirection(ArcDirection(90f * i / segments));
            Vector3 b = transform.TransformDirection(ArcDirection(90f * (i + 1) / segments));
            Gizmos.DrawLine(transform.position + lift + a * radius, transform.position + lift + b * radius);
            Gizmos.DrawLine(transform.position + lift + a * mid, transform.position + lift + b * mid);
        }

        // 矢じり（流れの先の側）
        bool forward = speed >= 0f;
        Vector3 tipDir = transform.TransformDirection(ArcDirection(forward ? 80f : 10f));
        Vector3 tip = transform.position + lift + tipDir * mid;
        Vector3 flow = Tangent(tipDir);
        Vector3 side = tipDir * 0.4f;
        Gizmos.DrawLine(tip, tip - flow * 0.6f + side);
        Gizmos.DrawLine(tip, tip - flow * 0.6f - side);
    }
}
