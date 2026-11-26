using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小惑星帯（検証用）。
///
/// 取り付け先：シーン内の AsteroidBelt（AsteroidBeltTest.unity では設定済み）。
///
/// 構造：
///   ・1層は XZ 平面で、3層を Y 軸方向に重ねる。
///     各層の高さ yOffset は、このオブジェクトの位置を基準にする。
///   ・各層を X/Z の格子に分け、1区画に1個ずつ配置する。
///     区画内のランダムなずれと層間の Y 距離で、重なりを避ける。
///   ・帯全体が -X（左）へ等速で流れ、回収ラインを越えたら右端に戻る。
///
/// Inspector の値を変更すると、再生前でも Scene ビューに配置を表示する。
/// 移動は再生中のみ。生成物は HideFlags.DontSave のため、
/// シーンファイルには保存しない。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class AsteroidBelt : MonoBehaviour
{
    [System.Serializable]
    public class LayerSetting
    {
        [Tooltip("HUD に表示する層名。Hierarchy の名前は Layer1、Layer2 などの英語名で固定する")]
        public string label = "Layer1_Top";

        [Tooltip("この層を有効にする")]
        public bool use = true;

        [Tooltip("帯の中心を基準にした Y 方向の高さ（m）。この値で各層を分ける")]
        public float yOffset = 0f;

        [Tooltip("移動方向（X）の区画数。この層の個数 = X の区画数 × Z の区画数")]
        [Min(1)] public int xColumns = 14;

        [Tooltip("奥行き方向（Z）の区画数")]
        [Min(1)] public int zRows = 2;

        [Tooltip("直径のランダム範囲（m）。X = 最小、Y = 最大。3 は直径3mで、プレハブの倍率は自動換算する")]
        public Vector2 sizeRange = new Vector2(2.2f, 3.8f);

        [Tooltip("Y 方向のランダムなずれ幅（m）。0 = 平面、0より大きい値で層に厚みを付ける")]
        [Min(0f)] public float yJitter = 0f;

        [Tooltip("基本速度に対する倍率。1 = 基本速度と同じ")]
        public float speedScale = 1f;
    }

    // ---------------------------------------------------------------- プレハブ

    [Header("小惑星のプレハブ（rock01 / rock02 / rock03 を指定）")]
    [SerializeField] private GameObject[] rockPrefabs = new GameObject[0];

    // ---------------------------------------------------------------- 帯全体

    [Header("帯全体（位置はこのオブジェクトを動かして調整）")]
    [Tooltip("X 方向の全長（m）。回収地点が画面外になるよう、カメラの表示範囲より長くする")]
    [SerializeField, Min(1f)] private float beltLength = 100f;

    [Tooltip("Z 方向の奥行き（m）。各層の水平面の大きさ")]
    [SerializeField, Min(1f)] private float beltDepth = 24f;

    [Tooltip("左へ流れる速度（m/s）。全長100m、速度1.2の場合、端まで約83秒")]
    [SerializeField, Min(0f)] private float baseSpeed = 1.2f;

    [Tooltip("帯の端から回収ラインまでの追加距離（m）。ラインを越えると右端に戻るため、画面外に置く")]
    [SerializeField, Min(0f)] private float recycleMargin = 12f;

    [Tooltip("乱数の種。配置を固定するなら変更しない。別の配置にするには数値を変更するか、コンテキストメニューの「シードを再抽選」を使う")]
    [SerializeField] private int seed = 20261125;

    // ---------------------------------------------------------------- 配置

    [Header("小惑星の配置（区画内でランダムにずらす）")]
    [Tooltip("区画間隔に対するランダムなずれの割合（0~0.45）。重ならないよう、実際のずれ幅を自動制限する")]
    [SerializeField, Range(0f, 0.45f)] private float slotJitter = 0.3f;

    [Tooltip("小惑星の表面同士に確保する最小の隙間（m）")]
    [SerializeField, Min(0f)] private float minClearance = 0.5f;

    [Header("向きと自転（指定範囲内でランダム）")]
    [Tooltip("初期の傾きの範囲（度）。0 = 傾きなし、25 = 最大25度、180 = 全方向")]
    [SerializeField, Range(0f, 180f)] private float tiltRange = 25f;

    [Tooltip("自転速度の範囲（度/秒）。正負で回転方向が変わる")]
    [SerializeField] private Vector2 spinSpeedRange = new Vector2(-15f, 15f);

    // ---------------------------------------------------------------- 3層

    [Header("3層（XZ 平面を yOffset で Y 方向に重ねる）")]
    [SerializeField] private LayerSetting[] layers = DefaultLayers();

    // ---------------------------------------------------------------- 高速隕石

    [Header("高速隕石（右ラインのランダムな点から左ラインのランダムな点へ直進）")]
    [SerializeField] private bool fastMeteorEnabled = true;

    [Tooltip("同時に飛行できる最大数")]
    [SerializeField, Min(1)] private int fastMeteorPoolSize = 3;

    [Tooltip("再出現までの待ち時間のランダム範囲（秒）")]
    [SerializeField] private Vector2 fastMeteorInterval = new Vector2(8f, 18f);

    [Tooltip("出現地点から目標地点へ直線移動する速度（m/s）")]
    [SerializeField, Min(0f)] private float fastMeteorSpeed = 14f;

    [Tooltip("直径の範囲（m）。X = 最小、Y = 最大")]
    [SerializeField] private Vector2 fastMeteorSizeRange = new Vector2(0.8f, 1.8f);

    [Tooltip("右側の出現地点の高さ範囲（帯の中心を基準にした Y、m）。初期設定では最下層より下に置く")]
    [SerializeField] private Vector2 fastMeteorSpawnYRange = new Vector2(-14f, -12f);

    [Tooltip("左側の目標地点の高さ範囲（帯の中心を基準にした Y、m）")]
    [SerializeField] private Vector2 fastMeteorTargetYRange = new Vector2(-14f, -12f);

    [Tooltip("出現地点と目標地点の Z 座標（奥行き）のランダム範囲（m）")]
    [SerializeField] private Vector2 fastMeteorZRange = new Vector2(-12f, 12f);

    // ---------------------------------------------------------------- 計測表示

    [Header("画面の計測表示")]
    [Tooltip("Game ビュー左上に、区画間隔・最小の隙間・層間距離・警告を表示する")]
    [SerializeField] private bool showHud = true;

    // ---------------------------------------------------------------- 内部データ

    private class PrefabInfo
    {
        public GameObject prefab;
        public Quaternion baseRot;
        public Vector3 baseScale;
        public float baseDiameter; // 現在の帯のスケールを適用したプレハブの元の直径（m）
    }

    private class Rock
    {
        public Transform t;
        public float speed;
        public Vector3 spinAxisLocal;
        public float spinDegPerSec;
    }

    private class Meteor
    {
        public Transform t;
        public PrefabInfo info;
        public Vector3 spinAxisLocal;
        public float spinDegPerSec;
        public Vector3 dir;      // 出現地点から目標地点への単位ベクトル
        public float timer;
        public bool flying;
    }

    private const string ContainerPrefix = "Generated_";

    private readonly List<Rock> _rocks = new List<Rock>();
    private readonly List<Meteor> _meteors = new List<Meteor>();
    private readonly List<string> _report = new List<string>();
    private readonly List<string> _warnings = new List<string>();

    private List<PrefabInfo> _prefabInfos;
    private System.Random _rng;
    private bool _dirty;
    private GUIStyle _hudStyle;
    private string _hudText = "";
    private int _hudLines;
    private int _builtRockCount;

    // ---------------------------------------------------------------- Unity イベント

    private void Reset()
    {
        layers = DefaultLayers();
        _dirty = true;
    }

    private void OnEnable()
    {
        _dirty = true;
    }

    private void OnDisable()
    {
        SafeClear();
    }

    private void OnDestroy()
    {
        SafeClear();
    }

    /// <summary>
    /// シーンのアンロード中は、OnDisable での破棄が例外になる場合がある。
    /// その場合は一覧だけを空にし、次回の Rebuild でも残った生成物を片付ける。
    /// </summary>
    private void SafeClear()
    {
        try
        {
            ClearGenerated();
        }
        catch (System.Exception)
        {
            _rocks.Clear();
            _meteors.Clear();
        }
    }

    private void OnValidate()
    {
        // OnValidate 内でシーンを直接変更せず、次の更新で再配置する。
        _dirty = true;
    }

    [ContextMenu("シードを再抽選（配置を変更）")]
    private void RandomizeSeed()
    {
        seed = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
        _dirty = true;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void Update()
    {
        if (_dirty)
        {
            _dirty = false;
            Rebuild();
        }

        if (!Application.isPlaying) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float limit = beltLength * 0.5f + recycleMargin;
        float span = limit * 2f;

        for (int i = 0; i < _rocks.Count; i++)
        {
            Rock r = _rocks[i];
            if (r.t == null) continue;

            Vector3 p = r.t.localPosition;
            p.x -= r.speed * dt;
            if (p.x < -limit) p.x += span;
            r.t.localPosition = p;

            if (r.spinDegPerSec != 0f)
            {
                r.t.Rotate(r.spinAxisLocal, r.spinDegPerSec * dt, Space.Self);
            }
        }

        for (int i = 0; i < _meteors.Count; i++)
        {
            Meteor m = _meteors[i];
            if (m.t == null) continue;

            if (m.flying)
            {
                Vector3 p = m.t.localPosition;
                p += m.dir * (fastMeteorSpeed * dt);
                m.t.localPosition = p;
                if (m.spinDegPerSec != 0f)
                {
                    m.t.Rotate(m.spinAxisLocal, m.spinDegPerSec * dt, Space.Self);
                }
                if (p.x < -limit)
                {
                    m.flying = false;
                    m.t.gameObject.SetActive(false);
                    m.timer = RandRange(fastMeteorInterval);
                }
            }
            else
            {
                m.timer -= dt;
                if (m.timer <= 0f) LaunchMeteor(m, limit);
            }
        }
    }

    // ---------------------------------------------------------------- 再配置

    private void Rebuild()
    {
        ClearGenerated();
        _rocks.Clear();
        _meteors.Clear();
        _report.Clear();
        _warnings.Clear();
        _builtRockCount = 0;

        _rng = new System.Random(seed);
        _prefabInfos = CollectPrefabs();

        if (_prefabInfos.Count == 0)
        {
            _warnings.Add("小惑星のプレハブが未設定です。Rock Prefabs に rock01 / rock02 / rock03 を指定してください");
            BuildReportText();
            return;
        }

        float worldScale = WorldScale();
        if (worldScale <= 0f)
        {
            _warnings.Add("このオブジェクトの Scale が0以下のため、小惑星の大きさを計算できません");
            BuildReportText();
            return;
        }

        foreach (PrefabInfo info in _prefabInfos)
        {
            info.baseDiameter = MeshMaxDimension(info) * Mathf.Abs(info.baseScale.x) * worldScale;
        }

        float halfBelt = beltLength * 0.5f;
        float halfDepth = beltDepth * 0.5f;

        int n = layers == null ? 0 : layers.Length;
        float[] yLo = new float[n];
        float[] yHi = new float[n];
        float[] maxSize = new float[n];
        bool[] used = new bool[n];

        for (int li = 0; li < n; li++)
        {
            LayerSetting L = layers[li];
            if (L == null) continue;

            L.sizeRange = Sorted(L.sizeRange);
            maxSize[li] = L.sizeRange.y;
            yLo[li] = L.yOffset - L.yJitter;
            yHi[li] = L.yOffset + L.yJitter;
            used[li] = L.use && L.xColumns > 0 && L.zRows > 0;

            if (!used[li]) continue;

            // 表示ラベルと分けて、Hierarchy の名前は常に英語にする。
            Transform container = NewContainer("Layer" + (li + 1), new Vector3(0f, L.yOffset, 0f));

            float xSlot = beltLength / L.xColumns;
            float zSlot = beltDepth / L.zRows;
            float slackX = xSlot - maxSize[li] - minClearance;
            float slackZ = zSlot - maxSize[li] - minClearance;
            float jitterX = JitterLimit(xSlot, slackX);
            float jitterZ = JitterLimit(zSlot, slackZ);

            if (slackX < 0f)
            {
                _warnings.Add(string.Format(
                    "{0}：X 間隔 {1:F2}m に最大直径 {2:F2}m + 隙間 {3:F2}m が収まらず、重なります。X Columns を減らす、Size Range を小さくする、または Belt Length を長くしてください",
                    L.label, xSlot, maxSize[li], minClearance));
            }
            if (slackZ < 0f)
            {
                _warnings.Add(string.Format(
                    "{0}：Z 間隔 {1:F2}m に最大直径 {2:F2}m + 隙間 {3:F2}m が収まらず、重なります。Z Rows を減らす、Size Range を小さくする、または Belt Depth を大きくしてください",
                    L.label, zSlot, maxSize[li], minClearance));
            }

            float gapX = xSlot - 2f * jitterX - maxSize[li];
            float gapZ = zSlot - 2f * jitterZ - maxSize[li];
            int count = L.xColumns * L.zRows;

            for (int ix = 0; ix < L.xColumns; ix++)
            {
                for (int iz = 0; iz < L.zRows; iz++)
                {
                    float x = -halfBelt + xSlot * (ix + 0.5f) + RandRange(new Vector2(-jitterX, jitterX));
                    float z = -halfDepth + zSlot * (iz + 0.5f) + RandRange(new Vector2(-jitterZ, jitterZ));
                    float y = L.yJitter > 0f ? RandRange(new Vector2(-L.yJitter, L.yJitter)) : 0f;
                    SpawnRock(container, new Vector3(x, y, z), RandRange(L.sizeRange), baseSpeed * L.speedScale);
                }
            }

            _report.Add(string.Format(
                "{0}  y={1,6:F1}  {2,2}×{3,2}={4,3}個  X間隔 {5,5:F2}  Z間隔 {6,5:F2}  直径 {7:F2}~{8:F2}m  最小隙間 X{9,5:F2} Z{10,5:F2}  {11}",
                L.label, L.yOffset, L.xColumns, L.zRows, count, xSlot, zSlot,
                L.sizeRange.x, L.sizeRange.y, gapX, gapZ,
                (gapX >= minClearance - 0.001f && gapZ >= minClearance - 0.001f) ? "OK" : "重なりあり"));
        }

        // ---- 層同士の Y 方向の距離を確認
        for (int a = 0; a < n; a++)
        {
            if (!used[a]) continue;
            for (int b = a + 1; b < n; b++)
            {
                if (!used[b]) continue;
                float gap = BandGap(yLo[a], yHi[a], yLo[b], yHi[b]);
                float need = (maxSize[a] + maxSize[b]) * 0.5f + minClearance;
                if (gap < need)
                {
                    _warnings.Add(string.Format(
                        "{0} と {1} の Y 距離は {2:F2}m ですが、重なりを避けるには {3:F2}m 必要です。Y Offset を離してください",
                        layers[a].label, layers[b].label, gap, need));
                }
                else
                {
                    _report.Add(string.Format("層間距離 {0} ↔ {1}：{2:F2}m（必要 {3:F2}m）OK",
                        layers[a].label, layers[b].label, gap, need));
                }
            }
        }

        // ---- 高速隕石
        if (fastMeteorEnabled)
        {
            fastMeteorInterval = Sorted(fastMeteorInterval);
            fastMeteorSizeRange = Sorted(fastMeteorSizeRange);
            fastMeteorSpawnYRange = Sorted(fastMeteorSpawnYRange);
            fastMeteorTargetYRange = Sorted(fastMeteorTargetYRange);
            fastMeteorZRange = Sorted(fastMeteorZRange);

            float mLo = Mathf.Min(fastMeteorSpawnYRange.x, fastMeteorTargetYRange.x);
            float mHi = Mathf.Max(fastMeteorSpawnYRange.y, fastMeteorTargetYRange.y);
            float maxMeteor = fastMeteorSizeRange.y;

            Transform container = NewContainer("FastMeteors", Vector3.zero);
            for (int i = 0; i < fastMeteorPoolSize; i++)
            {
                PrefabInfo info = PickPrefab();
                if (info == null) break;

                GameObject go = Instantiate(info.prefab, container);
                go.hideFlags = HideFlags.DontSave;
                go.name = info.prefab.name + "_fast" + i;
                go.SetActive(false);

                _meteors.Add(new Meteor
                {
                    t = go.transform,
                    info = info,
                    timer = RandRange(fastMeteorInterval),
                    flying = false,
                });
            }

            // 直線経路が通る Y 範囲と、各層の Y 範囲を比較する。
            for (int a = 0; a < n; a++)
            {
                if (!used[a]) continue;
                float gap = BandGap(mLo, mHi, yLo[a], yHi[a]);
                float need = (maxMeteor + maxSize[a]) * 0.5f + minClearance;
                if (gap < need)
                {
                    _warnings.Add(string.Format(
                        "高速隕石の高さ {0:F1}~{1:F1} は {2} に重なる可能性があります（距離 {3:F2}m、必要 {4:F2}m）。出現/目標地点の高さを帯の外に移してください",
                        mLo, mHi, layers[a].label, gap, need));
                }
            }

            _report.Add(string.Format(
                "高速隕石  最大 {0}個  {1:F1}m/s  待ち時間 {2:F0}~{3:F0}s  高さ {4:F1}~{5:F1} → {6:F1}~{7:F1}",
                _meteors.Count, fastMeteorSpeed, fastMeteorInterval.x, fastMeteorInterval.y,
                fastMeteorSpawnYRange.x, fastMeteorSpawnYRange.y,
                fastMeteorTargetYRange.x, fastMeteorTargetYRange.y));
        }

        _builtRockCount = _rocks.Count;
        BuildReportText();
    }

    private float JitterLimit(float slot, float slack)
    {
        if (slotJitter <= 0f) return 0f;
        return Mathf.Min(slot * slotJitter, Mathf.Max(0f, slack * 0.5f));
    }

    /// <summary>2つの区間の最短距離。重なっている場合は0。</summary>
    private static float BandGap(float aLo, float aHi, float bLo, float bHi)
    {
        if (aHi < bLo) return bLo - aHi;
        if (bHi < aLo) return aLo - bHi;
        return 0f;
    }

    private GameObject InstantiateRock(PrefabInfo info, Transform parent, string suffix)
    {
        GameObject go = Instantiate(info.prefab, parent);
        go.hideFlags = HideFlags.DontSave;
        go.name = info.prefab.name + "_" + suffix;
        return go;
    }

    private Transform NewContainer(string label, Vector3 localPos)
    {
        var go = new GameObject(ContainerPrefix + (string.IsNullOrEmpty(label) ? "layer" : label));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private void SpawnRock(Transform parent, Vector3 localPos, float diameter, float speed)
    {
        PrefabInfo info = PickPrefab();
        if (info == null) return;

        GameObject go = InstantiateRock(info, parent, _rocks.Count.ToString());
        Transform t = go.transform;
        t.localPosition = localPos;
        t.localRotation = info.baseRot * TiltRotation();
        t.localScale = info.baseScale * SizeMultiplier(info, diameter);

        _rocks.Add(new Rock
        {
            t = t,
            speed = speed,
            spinAxisLocal = RandomAxis(),
            spinDegPerSec = RandRange(spinSpeedRange),
        });
    }

    private void LaunchMeteor(Meteor m, float limit)
    {
        float diameter = RandRange(fastMeteorSizeRange);

        Vector3 from = new Vector3(limit, RandRange(fastMeteorSpawnYRange), RandRange(fastMeteorZRange));
        Vector3 to = new Vector3(-limit, RandRange(fastMeteorTargetYRange), RandRange(fastMeteorZRange));
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 1e-6f) delta = new Vector3(-1f, 0f, 0f);

        Transform t = m.t;
        t.localPosition = from;
        t.localRotation = m.info.baseRot * TiltRotation();
        t.localScale = m.info.baseScale * SizeMultiplier(m.info, diameter);
        m.dir = delta.normalized;
        m.spinAxisLocal = RandomAxis();
        m.spinDegPerSec = RandRange(spinSpeedRange) * 2f;
        m.flying = true;
        t.gameObject.SetActive(true);
    }

    private void ClearGenerated()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if ((child.hideFlags & HideFlags.DontSave) == 0) continue;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        _rocks.Clear();
        _meteors.Clear();
    }

    // ---------------------------------------------------------------- 補助処理

    private static LayerSetting[] DefaultLayers()
    {
        return new[]
        {
            new LayerSetting { label = "Layer1_Top",   yOffset = 0f,   xColumns = 14, zRows = 2, sizeRange = new Vector2(2.2f, 3.8f), yJitter = 0f, speedScale = 1f },
            new LayerSetting { label = "Layer2_Middle",   yOffset = -4.5f, xColumns = 16, zRows = 3, sizeRange = new Vector2(1.6f, 2.8f), yJitter = 0f, speedScale = 1f },
            new LayerSetting { label = "Layer3_Bottom",   yOffset = -9f,  xColumns = 18, zRows = 3, sizeRange = new Vector2(0.9f, 1.8f), yJitter = 0f, speedScale = 1f },
        };
    }

    private List<PrefabInfo> CollectPrefabs()
    {
        var list = new List<PrefabInfo>();
        if (rockPrefabs == null) return list;

        for (int i = 0; i < rockPrefabs.Length; i++)
        {
            GameObject p = rockPrefabs[i];
            if (p == null) continue;
            if (p.GetComponentInChildren<MeshFilter>() == null)
            {
                _warnings.Add("プレハブ " + p.name + " に MeshFilter がないため、生成対象から除外しました");
                continue;
            }
            list.Add(new PrefabInfo
            {
                prefab = p,
                baseRot = p.transform.localRotation,
                baseScale = p.transform.localScale,
            });
        }
        return list;
    }

    private static float MeshMaxDimension(PrefabInfo info)
    {
        MeshFilter mf = info.prefab.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 1f;
        Vector3 s = mf.sharedMesh.bounds.size;
        return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
    }

    private float SizeMultiplier(PrefabInfo info, float targetDiameter)
    {
        if (info.baseDiameter <= 0.0001f) return 1f;
        return targetDiameter / info.baseDiameter;
    }

    private PrefabInfo PickPrefab()
    {
        if (_prefabInfos == null || _prefabInfos.Count == 0) return null;
        return _prefabInfos[_rng.Next(_prefabInfos.Count)];
    }

    private float WorldScale()
    {
        Vector3 ls = transform.lossyScale;
        if (Mathf.Abs(ls.x - ls.y) > 0.0001f || Mathf.Abs(ls.x - ls.z) > 0.0001f)
        {
            _warnings.Add("このオブジェクトの Scale が均等でないため、直径の計算がずれます。1,1,1 に戻すことを推奨します");
        }
        return ls.x;
    }

    private Quaternion TiltRotation()
    {
        float ty = Rand01() * 360f;
        if (tiltRange <= 0f) return Quaternion.Euler(0f, ty, 0f);
        float tx = RandRange(new Vector2(-tiltRange, tiltRange));
        float tz = RandRange(new Vector2(-tiltRange, tiltRange));
        return Quaternion.Euler(tx, ty, tz);
    }

    private Vector3 RandomAxis()
    {
        var v = new Vector3(Rand01() * 2f - 1f, Rand01() * 2f - 1f, Rand01() * 2f - 1f);
        if (v.sqrMagnitude < 1e-6f) v = Vector3.up;
        return v.normalized;
    }

    private static Vector2 Sorted(Vector2 v)
    {
        return v.x <= v.y ? v : new Vector2(v.y, v.x);
    }

    private float Rand01()
    {
        return (float)_rng.NextDouble();
    }

    private float RandRange(Vector2 v)
    {
        return Mathf.Lerp(v.x, v.y, Rand01());
    }

    // ---------------------------------------------------------------- 計測表示

    private void BuildReportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("小惑星帯（各層は XZ 平面、Y 方向に3層）\n");
        sb.AppendFormat("中心（ワールド座標） {0}\n", transform.position.ToString("F1"));
        sb.AppendFormat("X {0:F1}~{1:F1}   Z {2:F1}~{3:F1}   回収ライン ±{4:F1}\n",
            -beltLength * 0.5f, beltLength * 0.5f, -beltDepth * 0.5f, beltDepth * 0.5f,
            beltLength * 0.5f + recycleMargin);
        sb.AppendFormat("基本速度 {0:F2} m/s（左へ）   シード {1}\n", baseSpeed, seed);
        sb.AppendFormat("生成済みの小惑星 {0} 個\n", _builtRockCount);

        for (int i = 0; i < _report.Count; i++) sb.Append(_report[i]).Append('\n');
        for (int i = 0; i < _warnings.Count; i++) sb.Append("! ").Append(_warnings[i]).Append('\n');

        _hudText = sb.ToString();
        _hudLines = _report.Count + _warnings.Count + 5;

        if (_warnings.Count > 0)
        {
            for (int i = 0; i < _warnings.Count; i++)
            {
                Debug.LogWarning("[AsteroidBelt] " + _warnings[i], this);
            }
        }
    }

    private void OnGUI()
    {
        if (!showHud) return;
        if (Event.current == null || GUI.skin == null) return;
        if (string.IsNullOrEmpty(_hudText)) return;

        if (_hudStyle == null)
        {
            _hudStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                richText = false,
            };
            _hudStyle.normal.textColor = Color.white;
        }

        string text = _hudText;
        if (Application.isPlaying && Time.unscaledDeltaTime > 0.0001f)
        {
            text += string.Format("FPS {0:F0}\n", 1f / Time.unscaledDeltaTime);
        }

        GUI.Box(new Rect(8f, 8f, 700f, 20f * (_hudLines + 2)), text, _hudStyle);
    }

    // ---------------------------------------------------------------- Gizmos

    private void OnDrawGizmos()
    {
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(beltLength, 0.05f, beltDepth));
        Gizmos.matrix = prev;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        float halfBelt = beltLength * 0.5f;
        float limit = halfBelt + recycleMargin;

        Color[] palette =
        {
            new Color(0.35f, 1f, 0.45f),
            new Color(1f, 0.85f, 0.3f),
            new Color(0.4f, 0.8f, 1f),
            new Color(1f, 0.5f, 0.9f),
        };

        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                LayerSetting L = layers[i];
                if (L == null) continue;

                float maxS = Mathf.Max(L.sizeRange.x, L.sizeRange.y);
                float h = Mathf.Max(0.02f, L.yJitter * 2f + maxS);
                Gizmos.color = L.use ? palette[i % palette.Length] : Color.gray;
                Gizmos.DrawWireCube(new Vector3(0f, L.yOffset, 0f), new Vector3(beltLength, h, beltDepth));
            }
        }

        if (fastMeteorEnabled)
        {
            float mLo = Mathf.Min(fastMeteorSpawnYRange.x, fastMeteorTargetYRange.x) - fastMeteorSizeRange.y * 0.5f;
            float mHi = Mathf.Max(fastMeteorSpawnYRange.y, fastMeteorTargetYRange.y) + fastMeteorSizeRange.y * 0.5f;
            float zLo = Mathf.Min(fastMeteorZRange.x, fastMeteorZRange.y);
            float zHi = Mathf.Max(fastMeteorZRange.x, fastMeteorZRange.y);
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            Gizmos.DrawWireCube(
                new Vector3(0f, (mLo + mHi) * 0.5f, (zLo + zHi) * 0.5f),
                new Vector3(beltLength, Mathf.Max(0.02f, mHi - mLo), Mathf.Max(0.02f, zHi - zLo)));
        }

        // 帯の端（白）と回収ライン（赤）
        Gizmos.color = Color.white;
        Gizmos.DrawLine(new Vector3(-halfBelt, -18f, 0f), new Vector3(-halfBelt, 4f, 0f));
        Gizmos.DrawLine(new Vector3(halfBelt, -18f, 0f), new Vector3(halfBelt, 4f, 0f));

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(-limit, -18f, 0f), new Vector3(-limit, 4f, 0f));
        Gizmos.DrawLine(new Vector3(limit, -18f, 0f), new Vector3(limit, 4f, 0f));

        Gizmos.matrix = prev;
    }
}
