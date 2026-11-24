using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **物や体のまわりに、電気がバチバチ走っているような火花の線を出す演出。**（2026/11/21・小野田さんの依頼）
///
/// 付けた物の見た目（Renderer）を囲む箱の表面から表面へ、ギザギザの短い稲妻を何本か走らせ、
/// 短い間隔でランダムに付けたり消したり形を変えたりする。稲妻の形の計算は <see cref="LightningShape"/>。
///
/// - 電気のデブリ（<see cref="ElectricDebris"/>）には、ずっと出しっぱなしで付く
/// - 電気のデブリに当たって痺れた人には、<see cref="Spawn"/> で痺れている秒数だけ付く
///
/// 線は付けた物の子の座標で書くので、物が速く動いても置いていかれない。
/// 詳しくは `Documents/機能ドキュメント/電気のデブリ.md`。
/// </summary>
public class ElectricSparks : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("稲妻の本数（同時に出る最大の本数）")]
    [Min(1)]
    [SerializeField] private int arcCount = 4;

    [Tooltip("稲妻の色（真ん中）")]
    [SerializeField] private Color coreColor = new Color(0.85f, 0.95f, 1f, 1f);

    [Tooltip("稲妻の色（端。だんだんこの色に薄れる）")]
    [SerializeField] private Color edgeColor = new Color(0.35f, 0.65f, 1f, 0.6f);

    [Tooltip("稲妻の太さ（m）")]
    [Min(0.001f)]
    [SerializeField] private float width = 0.04f;

    [Tooltip("ギザギザの大きさ（稲妻の長さに対する割合）")]
    [Min(0f)]
    [SerializeField] private float jitter = 0.12f;

    [Tooltip("形を作り直す間隔（秒）。小さいほど激しくバチバチする")]
    [Min(0.01f)]
    [SerializeField] private float refreshInterval = 0.06f;

    [Tooltip("1回の作り直しで、1本の稲妻が見えている確率（0〜1）。小さいほどまばらに光る")]
    [Range(0f, 1f)]
    [SerializeField] private float visibleChance = 0.55f;

    [Tooltip("稲妻を、見た目を囲む箱よりどれだけ外に出すか（m）")]
    [Min(0f)]
    [SerializeField] private float padding = 0.05f;

    [Tooltip("この秒数で消える。0 以下なら消えない（ずっと出す）")]
    [SerializeField] private float lifetime;

    [Tooltip("ON：見た目より先に当たり判定（Collider）で大きさを測る。体のように、名札などの飾りが付いている物に使う")]
    [SerializeField] private bool preferColliders;

    // 稲妻1本あたりの目印の点の数と、目印の間を何個の点で埋めるか
    private const int ControlCount = 4;
    private const int SamplesPerSegment = 3;

    private static Material sharedMaterial;

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private Vector3[] controlPoints = new Vector3[ControlCount];
    private Vector3[] points;
    private Transform root;
    private Bounds localBounds;
    private bool hasBounds;
    private float nextRefresh;
    private float endTime = -1f;

    /// <summary>
    /// <paramref name="target"/> に、<paramref name="seconds"/> 秒だけ火花を出す（痺れた人の演出）。
    /// すでに付いていれば、消えるまでの時間だけ延ばす。
    /// </summary>
    public static ElectricSparks Spawn(Transform target, float seconds)
    {
        if (target == null)
        {
            return null;
        }

        // 出しっぱなしの物（電気のデブリ本体）とは別に、時間で消える物を1つだけ持つ
        foreach (ElectricSparks existing in target.GetComponents<ElectricSparks>())
        {
            if (existing.lifetime > 0f)
            {
                existing.Extend(seconds);
                return existing;
            }
        }

        ElectricSparks sparks = target.gameObject.AddComponent<ElectricSparks>();
        sparks.arcCount = 6;
        sparks.width = 0.05f;
        sparks.visibleChance = 0.7f;
        sparks.preferColliders = true;
        sparks.lifetime = Mathf.Max(0.01f, seconds);
        sparks.endTime = Time.time + sparks.lifetime;
        return sparks;
    }

    /// <summary>消えるまでの時間を、いまから <paramref name="seconds"/> 秒後まで延ばす（短くはしない）。</summary>
    public void Extend(float seconds)
    {
        lifetime = Mathf.Max(lifetime, seconds);
        endTime = Mathf.Max(endTime, Time.time + seconds);
        enabled = true;
    }

    private void Awake()
    {
        points = new Vector3[LightningShape.PointCount(ControlCount, SamplesPerSegment)];
    }

    private void OnEnable()
    {
        if (root != null)
        {
            root.gameObject.SetActive(true);
        }
    }

    private void OnDisable()
    {
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (root != null)
        {
            Destroy(root.gameObject);
        }
    }

    private void Start()
    {
        if (lifetime > 0f && endTime < 0f)
        {
            endTime = Time.time + lifetime;
        }

        BuildLines();
    }

    private void LateUpdate()
    {
        if (lifetime > 0f && Time.time >= endTime)
        {
            Destroy(this);
            return;
        }

        if (Time.time < nextRefresh)
        {
            return;
        }

        nextRefresh = Time.time + refreshInterval * Random.Range(0.6f, 1.4f);

        if (!hasBounds)
        {
            hasBounds = TryMeasureBounds(out localBounds);
            if (!hasBounds)
            {
                return;
            }
        }

        foreach (LineRenderer line in lines)
        {
            bool show = Random.value < visibleChance;
            line.enabled = show;
            if (show)
            {
                Reshape(line);
            }
        }
    }

    /// <summary>稲妻の線（LineRenderer）を本数ぶん作る。線は子の物にまとめて付ける。</summary>
    private void BuildLines()
    {
        var rootObject = new GameObject("ElectricSparks");
        root = rootObject.transform;
        root.SetParent(transform, false);

        Material material = SharedMaterial();
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(edgeColor, 0f), new GradientColorKey(coreColor, 0.5f), new GradientColorKey(edgeColor, 1f) },
            new[] { new GradientAlphaKey(edgeColor.a * 0.3f, 0f), new GradientAlphaKey(coreColor.a, 0.5f), new GradientAlphaKey(edgeColor.a * 0.3f, 1f) });

        for (int i = 0; i < arcCount; i++)
        {
            var lineObject = new GameObject("Arc" + i);
            lineObject.transform.SetParent(root, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.colorGradient = gradient;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = points.Length;
            line.enabled = false;
            lines.Add(line);
        }
    }

    /// <summary>箱の表面の2点を、ギザギザの稲妻でつなぐ。</summary>
    private void Reshape(LineRenderer line)
    {
        Vector3 from = RandomPointOnBox(localBounds, padding);
        Vector3 to = RandomPointOnBox(localBounds, padding);

        // 近すぎると点にしか見えないので、反対側寄りへ寄せる
        if ((to - from).sqrMagnitude < localBounds.size.sqrMagnitude * 0.05f)
        {
            to = localBounds.center * 2f - from;
        }

        float length = (to - from).magnitude;
        for (int i = 0; i < ControlCount; i++)
        {
            float t = i / (float)(ControlCount - 1);
            Vector3 point = Vector3.Lerp(from, to, t);

            // 間の目印は、箱の外へ少しふくらませる（稲妻が体の中に埋まって見えなくならないように）
            if (i > 0 && i < ControlCount - 1)
            {
                Vector3 outward = point - localBounds.center;
                point += outward.normalized * (length * 0.15f) + Random.insideUnitSphere * (length * 0.15f);
            }

            controlPoints[i] = point;
        }

        int count = LightningShape.SampleHermite(controlPoints, ControlCount, SamplesPerSegment, points);
        LightningShape.Jag(points, count, length * jitter);
        line.positionCount = count;
        line.SetPositions(points);
    }

    /// <summary>
    /// 付けた物の見た目（Renderer）を、付けた物の座標で囲む箱を測る。
    /// 見た目が無ければ当たり判定（Collider）で測る（Prefer Colliders のときは逆の順）。どちらも無ければ false。
    /// </summary>
    private bool TryMeasureBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        if (preferColliders)
        {
            MeasureColliders(ref bounds, ref found);
        }

        if (!found)
        {
            MeasureRenderers(ref bounds, ref found);
        }

        if (!found)
        {
            MeasureColliders(ref bounds, ref found);
        }

        return found && bounds.size.sqrMagnitude > 0.0001f;
    }

    private void MeasureRenderers(ref Bounds bounds, ref bool found)
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            // 自分の稲妻や、ほかの演出（線・パーティクル）は数えない
            if (renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            Encapsulate(ref bounds, ref found, renderer.transform, renderer.localBounds);
        }
    }

    private void MeasureColliders(ref Bounds bounds, ref bool found)
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || !collider.enabled)
            {
                continue;
            }

            // Collider.bounds はワールド座標の箱
            Encapsulate(ref bounds, ref found, null, collider.bounds);
        }
    }

    /// <summary>箱（<paramref name="owner"/> の座標。null ならワールド座標）の8つの角を、付けた物の座標に直して足す。</summary>
    private void Encapsulate(ref Bounds bounds, ref bool found, Transform owner, Bounds box)
    {
        Vector3 min = box.min;
        Vector3 max = box.max;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            Vector3 world = owner != null ? owner.TransformPoint(corner) : corner;
            Vector3 local = transform.InverseTransformPoint(world);

            if (!found)
            {
                bounds = new Bounds(local, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(local);
            }
        }
    }

    /// <summary>箱の6つの面のどれかの上の、ランダムな1点（<paramref name="pad"/> だけ外へ出す）。</summary>
    public static Vector3 RandomPointOnBox(Bounds box, float pad)
    {
        Vector3 extents = box.extents + Vector3.one * pad;
        Vector3 point = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));

        // どれか1つの軸を、面の上（+1 か -1）にそろえる
        int axis = Random.Range(0, 3);
        point[axis] = Random.value < 0.5f ? -1f : 1f;

        return box.center + Vector3.Scale(point, extents);
    }

    /// <summary>稲妻のマテリアル（全部で1つを使い回す）。いつもビルドに入っている Sprites/Default で、色は線の色をそのまま使う。</summary>
    private static Material SharedMaterial()
    {
        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            sharedMaterial = new Material(shader) { name = "ElectricSparks (自動)" };
        }

        return sharedMaterial;
    }
}
