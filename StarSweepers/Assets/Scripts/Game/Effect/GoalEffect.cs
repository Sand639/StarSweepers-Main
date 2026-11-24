using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>ゴールの演出の見た目の種類（インスペクターのプルダウン）。</summary>
public enum GoalEffectStyle
{
    [InspectorName("渦")] Vortex,
    [InspectorName("光の柱")] Pillar,
}

/// <summary>ゴールに入った物資の動き（インスペクターのプルダウン。見た目とは別に選べる）。</summary>
public enum GoalAbsorbMotion
{
    [InspectorName("見た目に合わせる")] MatchStyle,
    [InspectorName("渦の吸い込み（回って中心へ）")] Vortex,
    [InspectorName("光の柱（上へ運んで光って消える）")] Pillar,
}

/// <summary>
/// ゴールに常に出ている演出（渦か光の柱）。円柱の判定に入った物資を、小さくしながら吸い込む（吸い込み終わったら得点）。
/// 1つのシーンに1つ置けば、そのシーンの全部のゴールに出る。
/// 説明は Documents/スクリプトの説明/Game/Effect/GoalEffect.md
/// </summary>
public class GoalEffect : MonoBehaviour
{
    [Header("種類")]
    [Tooltip("ゴールの演出の見た目。渦＝黒い立体の渦（物資は回りながら真ん中へ吸い込まれる）／光の柱＝光る筒（物資は上へ浮かんで消える）。判定と得点は同じ")]
    [SerializeField] private GoalEffectStyle style = GoalEffectStyle.Vortex;

    [Tooltip("ゴールに入った物資の動き。見た目に合わせる＝渦なら渦の吸い込み、光の柱なら光の柱の動き。見た目とちがう動きも選べる（試合中に変えたら、次に入った物資から）")]
    [SerializeField] private GoalAbsorbMotion absorbMotion = GoalAbsorbMotion.MatchStyle;

    [Header("渦の見た目")]
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

    [Header("光の柱の見た目")]
    [Tooltip("光の柱の見た目（Art/Materials/GoalPillar。筋・きらめきの濃さはマテリアルで変える）")]
    [SerializeField] private Material pillarMaterial;

    [Tooltip("光の柱（筒）の高さ（m）。物資を吸い込む円柱より高くはならない")]
    [SerializeField] private float pillarHeight = 1.5f;

    [Tooltip("光の柱をチームの色にするか（オフなら Pillar Color）。2026/11/17 の大槻さんの回答でオン")]
    [SerializeField] private bool pillarUseTeamColor = true;

    [Tooltip("チームの色を使わないときの、光の柱の色（参考画像の緑）")]
    [SerializeField] private Color pillarColor = new Color(0.35f, 1f, 0.55f);

    [Tooltip("光の柱の明るさ")]
    [SerializeField] private float pillarBrightness = 1.2f;

    [Tooltip("筒の中を下から上へ流れる、光の筋の数")]
    [Range(0, 40)]
    [SerializeField] private int streakCount = 14;

    [Tooltip("筒の中から上へ立ちのぼる、十字のきらめきの数")]
    [Range(0, 64)]
    [SerializeField] private int sparkleCount = 22;

    [Header("光の柱のときの物資の動き")]
    [Tooltip("ゴールに入った物資を、真ん中の上（光の柱の一番上あたり）へ一直線に運ぶ秒数。大きさはそのまま、当たり判定は無くなる")]
    [SerializeField] private float pillarMoveSeconds = 0.4f;

    [Tooltip("真ん中の上に着いてから、ゆっくり上げる秒数。上げ終わったら光って消え、得点になる")]
    [SerializeField] private float pillarRiseSeconds = 1.2f;

    [Tooltip("ゆっくり上げる高さ（m）")]
    [SerializeField] private float pillarRiseDistance = 0.6f;

    [Tooltip("消える前に、物資のまわりが光り始める秒数（上げ終わる何秒前からか）")]
    [SerializeField] private float pillarGlowSeconds = 0.35f;

    [Tooltip("消えた瞬間の光が広がって消えるまでの秒数")]
    [SerializeField] private float pillarFlashSeconds = 0.3f;

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

    /// <summary>いまの見た目の種類（インスペクターのプルダウン）。テストからも切り替えられる。</summary>
    public GoalEffectStyle Style
    {
        get => style;
        set => style = value;
    }

    /// <summary>そのゴールの光の柱の高さ（m）。円柱の高さを超えない。</summary>
    public float PillarHeightOf(Area area) => Mathf.Clamp(pillarHeight, 0f, Mathf.Max(0f, area.Top - area.Center.y));

    /// <summary>光の柱の一番上の高さ（世界の y）。光の柱のとき、物資をまずここへ運ぶ。</summary>
    public float PillarTopY(Area area) => area.Center.y + PillarHeightOf(area);

    public float PillarMoveSeconds => Mathf.Max(0f, pillarMoveSeconds);
    public float PillarRiseSeconds => Mathf.Max(0f, pillarRiseSeconds);
    public float PillarRiseDistance => pillarRiseDistance;

    /// <summary>いまの見た目の種類での、吸い込みの長さ（秒）。光の柱なら「運ぶ＋上げる」の秒数。</summary>
    public float CurrentAbsorbSeconds => UsesPillarMotion ? PillarMoveSeconds + PillarRiseSeconds : absorbSeconds;

    /// <summary>物資の動きのプルダウン（テストからも切り替えられる）。</summary>
    public GoalAbsorbMotion AbsorbMotion
    {
        get => absorbMotion;
        set => absorbMotion = value;
    }

    /// <summary>いま吸い込みを始めると、光の柱の動き（上へ運んで光って消える）になるか。false なら渦の吸い込み。</summary>
    public bool UsesPillarMotion => absorbMotion == GoalAbsorbMotion.Pillar ||
                                    (absorbMotion == GoalAbsorbMotion.MatchStyle && style == GoalEffectStyle.Pillar);

    /// <summary>消えた瞬間の光1つ分。</summary>
    private struct Flash
    {
        public Vector3 Position;
        public Color Color;
        public float Start;
        public float Size;
    }

    private readonly List<Flash> flashes = new List<Flash>();

    /// <summary>光の柱のとき、物資が消えた瞬間の光を出す（どのPCでも、そのPCの吸い込みが終わったときに呼ばれる）。</summary>
    public void PlayFlash(Vector3 position, float size)
    {
        flashes.Add(new Flash { Position = position, Color = ColorNear(position), Start = Time.time, Size = size });
    }

    /// <summary>その場所にいちばん近いゴールの、光の柱の色。</summary>
    private Color ColorNear(Vector3 position)
    {
        Color best = pillarColor;
        float bestDistance = float.MaxValue;
        foreach (GoalView view in Goals())
        {
            if (TryGetArea(view.Goal, out Area area))
            {
                float distance = (new Vector2(area.Center.x, area.Center.z) - new Vector2(position.x, position.z)).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pillarUseTeamColor ? view.TeamColor : pillarColor;
                }
            }
        }
        return best;
    }

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
    private static Mesh cylinderMesh;
    private Mesh streakMesh;
    private int streakMeshCount = -1;
    private Mesh sparkleMesh;
    private int sparkleMeshCount = -1;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int VortexTimeId = Shader.PropertyToID("_VortexTime");
    private static readonly int AppearId = Shader.PropertyToID("_Appear");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int PartId = Shader.PropertyToID("_Part");
    private static readonly int FunnelId = Shader.PropertyToID("_Funnel");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static Mesh flashMesh;

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
        NetSupply supply = item.GetComponent<NetSupply>();
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
            float seconds = CurrentAbsorbSeconds;
            bool pillarMotion = UsesPillarMotion;
            GoalAbsorb.Begin(item, area, start, seconds, pillarMotion, done =>
            {
                Absorbed?.Invoke(goal);
                onAbsorbed?.Invoke(done);
            });

            // 参加者のPCへも、どちらの動きかを送る（参加者のPCでプルダウンの値がちがっても、ホストと同じ動きになるように）
            if (supply != null && supply.IsSpawned)
            {
                supply.ServerBeginAbsorb(start, seconds, pillarMotion);
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
        block ??= new MaterialPropertyBlock();
        if (style == GoalEffectStyle.Pillar)
        {
            DrawPillars(now, targetCamera);
        }
        else
        {
            DrawVortices(now, targetCamera);
        }

        DrawFlashes(targetCamera);
    }

    /// <summary>
    /// 光の柱のときの、物資が光って消える光を描く。消える少し前から物資のまわりが光り始め（Pillar Glow Seconds）、
    /// 消えた瞬間にぱっと光って広がり、Pillar Flash Seconds で消える。
    /// </summary>
    private void DrawFlashes(Camera targetCamera)
    {
        if (pillarMaterial == null)
        {
            flashes.Clear();
            return;
        }

        flashMesh ??= CreateFlashMesh();
        RenderParams renderParams = new RenderParams(pillarMaterial)
        {
            matProps = block,
            camera = targetCamera,
            layer = gameObject.layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
        };
        block.SetFloat(PartId, 4f);
        block.SetFloat(AppearId, 1f);
        block.SetFloat(BrightnessId, pillarBrightness);
        block.SetFloat(VortexTimeId, Time.time);

        // 消える前の光（物資のまわり）
        float total = Mathf.Max(0.05f, PillarMoveSeconds + PillarRiseSeconds);
        foreach (GoalAbsorb absorb in GoalAbsorb.All)
        {
            if (absorb == null || !absorb.IsPillar || absorb.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            float remaining = (1f - absorb.Progress) * total;
            if (remaining >= pillarGlowSeconds || pillarGlowSeconds <= 0f)
            {
                continue;
            }

            float amount = 1f - remaining / pillarGlowSeconds;
            block.SetColor(ColorId, ColorNear(absorb.transform.position));
            block.SetVector(FlashId, new Vector4(0f, amount, absorb.ItemSize * (0.8f + 0.4f * amount), 0f));
            Graphics.RenderMesh(renderParams, flashMesh, 0, Matrix4x4.Translate(absorb.transform.position));
        }

        // 消えた瞬間の光
        for (int i = flashes.Count - 1; i >= 0; i--)
        {
            float p = (Time.time - flashes[i].Start) / Mathf.Max(0.05f, pillarFlashSeconds);
            if (p >= 1f)
            {
                flashes.RemoveAt(i);
                continue;
            }

            block.SetColor(ColorId, flashes[i].Color);
            block.SetVector(FlashId, new Vector4(1f, Mathf.Clamp01(p), flashes[i].Size * (1.2f + 1.6f * p), 0f));
            Graphics.RenderMesh(renderParams, flashMesh, 0, Matrix4x4.Translate(flashes[i].Position));
        }
    }

    /// <summary>光の柱を描く（床の光・筒・光の筋・十字のきらめき）。</summary>
    private void DrawPillars(float now, Camera targetCamera)
    {
        if (pillarMaterial == null)
        {
            return;
        }

        discMesh ??= CreateDiscMesh();
        cylinderMesh ??= CreateCylinderMesh();
        if (streakMesh == null || streakMeshCount != streakCount)
        {
            DestroyMesh(streakMesh);
            streakMeshCount = streakCount;
            streakMesh = CreateBillboardMesh(streakCount, 11, 0.85f);
        }
        if (sparkleMesh == null || sparkleMeshCount != sparkleCount)
        {
            DestroyMesh(sparkleMesh);
            sparkleMeshCount = sparkleCount;
            sparkleMesh = CreateBillboardMesh(sparkleCount, 23, 0.8f);
        }

        RenderParams renderParams = new RenderParams(pillarMaterial)
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

            float height = PillarHeightOf(area);
            Vector3 floorPoint = area.Center + Vector3.up * lift;
            block.SetColor(ColorId, pillarUseTeamColor ? view.TeamColor : pillarColor);
            block.SetFloat(VortexTimeId, now);
            block.SetFloat(AppearId, amount);
            block.SetFloat(BrightnessId, pillarBrightness);
            block.SetVector(FunnelId, new Vector4(area.Radius, height, 0f, 0f));

            // 床の光（筒より少し外まで、もやのように広がる）
            float glowDiameter = area.Radius * 2f * 1.35f;
            block.SetFloat(PartId, 0f);
            Graphics.RenderMesh(renderParams, discMesh, 0, Matrix4x4.TRS(floorPoint, Quaternion.identity, new Vector3(glowDiameter, 1f, glowDiameter)));

            Matrix4x4 tube = Matrix4x4.TRS(floorPoint, Quaternion.identity, new Vector3(area.Radius, height * amount, area.Radius));
            block.SetFloat(PartId, 1f);
            Graphics.RenderMesh(renderParams, cylinderMesh, 0, tube);

            if (streakCount > 0)
            {
                block.SetFloat(PartId, 2f);
                Graphics.RenderMesh(renderParams, streakMesh, 0, tube);
            }

            if (sparkleCount > 0)
            {
                block.SetFloat(PartId, 3f);
                Graphics.RenderMesh(renderParams, sparkleMesh, 0, tube);
            }
        }
    }

    /// <summary>渦を描く（床の渦・すり鉢・らせんの帯・かけら）。</summary>
    private void DrawVortices(float now, Camera targetCamera)
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

            block.SetColor(ColorId, useTeamColor ? view.TeamColor : neutralColor);
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

    /// <summary>演出を出すゴール1つ分（どのゴールか・チームの色・いま見えているか）。</summary>
    private struct GoalView
    {
        public Component Goal;
        public Color TeamColor;
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
                views.Add(new GoalView { Goal = goal, TeamColor = SpaceJunkTeams.TeamColor(goal.OwnerTeam), Shown = IsVisualShown(goal) });
            }
        }

        foreach (SpaceJunkLobbyGoal goal in SpaceJunkLobbyGoal.All)
        {
            if (goal != null && goal.gameObject.scene == scene)
            {
                views.Add(new GoalView { Goal = goal, TeamColor = goal.BaseColor, Shown = true });
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
        DestroyMesh(streakMesh);
        DestroyMesh(sparkleMesh);
    }

    /// <summary>消えるときの光の板（1枚。4つの角はどれも原点で、カメラのほうを向く大きさはシェーダーが決める）。</summary>
    private static Mesh CreateFlashMesh()
    {
        Mesh mesh = new Mesh { name = "GoalPillarFlash" };
        mesh.vertices = new Vector3[4];
        mesh.uv = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 6f);
        return mesh;
    }

    /// <summary>光の柱の筒（半径 1・高さ 1。上も下も開いている）。形の中の位置の y が高さ（シェーダーが使う）。</summary>
    private static Mesh CreateCylinderMesh()
    {
        const int around = 48;
        var vertices = new Vector3[(around + 1) * 2];
        var triangles = new int[around * 6];
        for (int i = 0; i <= around; i++)
        {
            float a = i * Mathf.PI * 2f / around;
            vertices[i * 2] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a));
        }
        for (int i = 0; i < around; i++)
        {
            int v = i * 2;
            int k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
            triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
        }
        Mesh mesh = new Mesh { name = "GoalPillarTube" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// カメラのほうを向く板を count 枚（光の筋・きらめき用）。どれも足もとは筒の中（半径 spread 以内）の床の上で、
    /// 大きさ・動きはシェーダーが決める。UV は板の角（−1〜1）、2つ目の UV が板ごとの乱数。乱数は毎回同じ（どのPCでも同じ）。
    /// </summary>
    private static Mesh CreateBillboardMesh(int count, int seed, float spread)
    {
        var random = new System.Random(seed);
        var vertices = new Vector3[count * 4];
        var uvs = new Vector2[count * 4];
        var seeds = new Vector2[count * 4];
        var triangles = new int[count * 6];
        Vector2[] corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };
        for (int i = 0; i < count; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)random.NextDouble()) * spread;
            Vector3 root = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Vector2 s = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
            for (int c = 0; c < 4; c++)
            {
                vertices[i * 4 + c] = root;
                uvs[i * 4 + c] = corners[c];
                seeds[i * 4 + c] = s;
            }
            int v = i * 4;
            int k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 2; triangles[k + 2] = v + 1;
            triangles[k + 3] = v + 2; triangles[k + 4] = v + 3; triangles[k + 5] = v + 1;
        }
        Mesh mesh = new Mesh { name = "GoalPillarBillboards" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = seeds;
        mesh.triangles = triangles;
        // 頂点はシェーダーで動かすので、筒より上まで入る大きさの箱にしておく
        mesh.bounds = new Bounds(new Vector3(0f, 1.2f, 0f), new Vector3(3f, 3f, 3f));
        return mesh;
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
