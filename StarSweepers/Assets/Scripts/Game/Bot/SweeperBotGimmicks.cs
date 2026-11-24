using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// **ボット（<see cref="SweeperBot"/>）に、ステージのギミック（ガラス・ベルト・列車・上下する床・回る床）のことを教える係。**
/// 詳しい説明は Documents/スクリプトの説明/Game/Bot/SweeperBot.md。
/// </summary>
public static class SweeperBotGimmicks
{
    /// <summary>歩ける床の地図で、ガラス・ベルト・線路に付ける「場所の種類」の番号（0〜2 は Unity が使っている）。</summary>
    public const int GlassArea = 3;
    public const int BeltArea = 4;
    public const int TrackArea = 5;

    /// <summary>その場所を1m歩くのを、ふつうの床の何m分と数えるか（大きいほど、道を選ぶときに避ける）。</summary>
    private const float GlassCost = 3f;
    private const float BeltCost = 4f;
    private const float TrackCost = 6f;

    /// <summary>線路の帯を、列車の幅よりどれだけ広く「危ない」とみなすか（m）。</summary>
    public const float TrackMargin = 0.8f;

    /// <summary>乗っているガラスが、あと何秒で割れそうなら逃げるか（ふつうに歩けるとき／巻き取り中でゆっくりしか歩けないとき）。</summary>
    public const float GlassEscapeSeconds = 0.35f;
    public const float GlassEscapeSecondsSlow = 0.65f;

    /// <summary>あと何秒しか持たないガラスを、地図から外す（穴にする）か。</summary>
    private const float GlassHoleSeconds = 0.7f;

    /// <summary>上下する床が、上がる前に何秒は下で止まっていてほしいか（乗り込むのに使う時間）。</summary>
    private const float LiftBoardSeconds = 0.6f;

    /// <summary>上下する床で上がるための「乗り口（下）→ 床の真ん中（上）」。</summary>
    public struct LiftLink
    {
        public SpaceJunkLiftFloor lift;
        public Vector3 entry;
        public Vector3 top;
    }

    private static int sceneHandle = -1;
    private static int refreshedFrame = -1;
    private static GravityGlassFloor[] glasses = new GravityGlassFloor[0];
    private static NavMeshObstacle[] glassHoles = new NavMeshObstacle[0];
    private static SpaceJunkTrain[] trains = new SpaceJunkTrain[0];
    private static SpaceJunkConveyor[] belts = new SpaceJunkConveyor[0];
    private static SpaceJunkLiftFloor[] lifts = new SpaceJunkLiftFloor[0];
    private static SpaceJunkRotatingFloor[] spinners = new SpaceJunkRotatingFloor[0];
    private static Quaternion[] spinnerBuilt = new Quaternion[0];
    private static Quaternion[] spinnerLast = new Quaternion[0];
    private static float spinnerStillSince;

    private static readonly List<NavMeshLinkInstance> links = new List<NavMeshLinkInstance>();
    private static readonly List<LiftLink> liftLinks = new List<LiftLink>();
    private static readonly RaycastHit[] rayHits = new RaycastHit[8];

    /// <summary>ステージにガラスがあるか（ボットの考える時間を変えるのに使う）。</summary>
    public static bool HasGlass => glasses.Length > 0;

    // ------------------------------------------------------------
    // 毎フレーム
    // ------------------------------------------------------------

    /// <summary>ギミックの一覧を最新にし、割れたガラスを地図の穴にし、回る床が止まったら地図を作り直す（1フレームに1回だけ働く）。</summary>
    public static void Refresh()
    {
        if (refreshedFrame == Time.frameCount)
        {
            return;
        }
        refreshedFrame = Time.frameCount;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.handle != sceneHandle)
        {
            Scan(scene);
        }

        UpdateGlassHoles();
        WatchSpinners();
    }

    /// <summary>いまのマップのギミックを集める。ガラスごとに、地図に穴をあける部品（NavMeshObstacle）を用意する。</summary>
    private static void Scan(Scene scene)
    {
        sceneHandle = scene.handle;
        RemoveLinks();

        glasses = Object.FindObjectsByType<GravityGlassFloor>(FindObjectsSortMode.None);
        trains = Object.FindObjectsByType<SpaceJunkTrain>(FindObjectsSortMode.None);
        belts = Object.FindObjectsByType<SpaceJunkConveyor>(FindObjectsSortMode.None);
        lifts = Object.FindObjectsByType<SpaceJunkLiftFloor>(FindObjectsSortMode.None);
        spinners = Object.FindObjectsByType<SpaceJunkRotatingFloor>(FindObjectsSortMode.None);

        spinnerBuilt = new Quaternion[spinners.Length];
        spinnerLast = new Quaternion[spinners.Length];
        for (int i = 0; i < spinners.Length; i++)
        {
            spinnerBuilt[i] = spinners[i].transform.rotation;
            spinnerLast[i] = spinnerBuilt[i];
        }

        glassHoles = new NavMeshObstacle[glasses.Length];
        for (int i = 0; i < glasses.Length; i++)
        {
            glassHoles[i] = CreateHole(glasses[i]);
        }
    }

    /// <summary>ガラス1枚ぶんの穴（ふだんは OFF）。ガラスの子にはしない（ガラスの「乗っている人」の判定に混ざらないように）。</summary>
    private static NavMeshObstacle CreateHole(GravityGlassFloor glass)
    {
        GameObject hole = new GameObject($"BotGlassHole_{glass.name}");
        SceneManager.MoveGameObjectToScene(hole, glass.gameObject.scene);
        hole.transform.SetPositionAndRotation(glass.TopCenter, glass.transform.rotation);

        Vector3 size = glass.WorldSize;
        NavMeshObstacle obstacle = hole.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(size.x, 1.2f, size.z);
        obstacle.center = Vector3.zero;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        obstacle.enabled = false;
        return obstacle;
    }

    private static void UpdateGlassHoles()
    {
        for (int i = 0; i < glasses.Length; i++)
        {
            if (glasses[i] == null || glassHoles[i] == null)
            {
                continue;
            }

            bool hole = !glasses[i].CanStandFor(GlassHoleSeconds);
            if (glassHoles[i].enabled != hole)
            {
                glassHoles[i].enabled = hole;
            }
        }
    }

    /// <summary>回る床が回り終わって止まったら、歩ける床の地図を作り直してもらう。</summary>
    private static void WatchSpinners()
    {
        if (spinners.Length == 0)
        {
            return;
        }

        bool moving = false;
        bool changed = false;
        for (int i = 0; i < spinners.Length; i++)
        {
            if (spinners[i] == null)
            {
                continue;
            }

            Quaternion now = spinners[i].transform.rotation;
            if (Quaternion.Angle(now, spinnerLast[i]) > 0.05f)
            {
                moving = true;
            }
            if (Quaternion.Angle(now, spinnerBuilt[i]) > 2f)
            {
                changed = true;
            }
            spinnerLast[i] = now;
        }

        if (moving)
        {
            spinnerStillSince = Time.time;
            return;
        }

        if (changed && Time.time - spinnerStillSince > 0.2f && SpaceJunkBotNavMesh.RequestRebuild())
        {
            for (int i = 0; i < spinners.Length; i++)
            {
                if (spinners[i] != null)
                {
                    spinnerBuilt[i] = spinners[i].transform.rotation;
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 地図を作るとき
    // ------------------------------------------------------------

    /// <summary>
    /// 地図の材料を、ギミックに合わせて直す。列車は外す・上下する床は「上にいるとき」の場所にする・
    /// ガラス／ベルト／線路の帯に、場所の種類（歩きたくない度合い）を付ける。
    /// </summary>
    public static void AdjustSources(List<NavMeshBuildSource> sources)
    {
        Refresh();

        for (int i = sources.Count - 1; i >= 0; i--)
        {
            NavMeshBuildSource source = sources[i];
            if (!(source.component is Component component))
            {
                continue;
            }

            if (component.GetComponentInParent<SpaceJunkTrain>() != null)
            {
                sources.RemoveAt(i);
                continue;
            }

            SpaceJunkLiftFloor lift = component.GetComponentInParent<SpaceJunkLiftFloor>();
            if (lift != null)
            {
                float rise = lift.RiseHeight - lift.CurrentHeight;
                source.transform = Matrix4x4.Translate(Vector3.up * rise) * source.transform;
                sources[i] = source;
            }
        }

        foreach (GravityGlassFloor glass in glasses)
        {
            if (glass != null)
            {
                Vector3 size = glass.WorldSize;
                AddArea(sources, glass.TopCenter, glass.transform.rotation, new Vector3(size.x, 1.2f, size.z), GlassArea);
            }
        }

        foreach (SpaceJunkConveyor belt in belts)
        {
            if (belt != null && belt.TryGetComponent(out BoxCollider box))
            {
                Vector3 size = Vector3.Scale(box.size, Abs(belt.transform.lossyScale));
                AddArea(sources, belt.transform.position, belt.transform.rotation, new Vector3(size.x, 1.2f, size.z), BeltArea);
            }
        }

        foreach (SpaceJunkTrain train in trains)
        {
            if (train != null)
            {
                Vector3 size = new Vector3((train.HalfWidth + TrackMargin) * 2f, 6f, train.RunHalfLength * 2f);
                AddArea(sources, train.transform.position, train.transform.rotation, size, TrackArea);
            }
        }
    }

    private static void AddArea(List<NavMeshBuildSource> sources, Vector3 center, Quaternion rotation, Vector3 size, int area)
    {
        sources.Add(new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.ModifierBox,
            transform = Matrix4x4.TRS(center, rotation, Vector3.one),
            size = size,
            area = area
        });
    }

    /// <summary>道を調べる条件に、ガラス・ベルト・線路の「歩きたくない度合い」を入れる。</summary>
    public static void ApplyAreaCosts(ref NavMeshQueryFilter filter)
    {
        filter.SetAreaCost(GlassArea, GlassCost);
        filter.SetAreaCost(BeltArea, BeltCost);
        filter.SetAreaCost(TrackArea, TrackCost);
    }

    /// <summary>地図ができたら、上下する床と回る床の「つなぎ（NavMeshLink）」を作り直す。</summary>
    public static void OnNavMeshBuilt(int agentTypeID)
    {
        RemoveLinks();

        foreach (SpaceJunkLiftFloor lift in lifts)
        {
            if (lift != null)
            {
                AddLiftLinks(lift, agentTypeID);
            }
        }

        foreach (SpaceJunkRotatingFloor spinner in spinners)
        {
            if (spinner != null)
            {
                AddSpinnerLinks(spinner, agentTypeID);
            }
        }
    }

    /// <summary>上下する床の、下の地面（乗り口）と、上にいるときの床の真ん中をつなぐ。乗り口は床の4辺のうち、地面がある辺。</summary>
    private static void AddLiftLinks(SpaceJunkLiftFloor lift, int agentTypeID)
    {
        Vector3 scale = Abs(lift.transform.lossyScale);
        Vector3 bottom = lift.BottomPosition;
        float groundY = bottom.y + scale.y * 0.5f;
        Vector3 topPoint = new Vector3(bottom.x, groundY + lift.RiseHeight, bottom.z);
        if (!NavMesh.SamplePosition(topPoint, out NavMeshHit topHit, 0.6f, SpaceJunkBotNavMesh.Filter))
        {
            return;
        }

        Vector3[] sides = { lift.transform.right, -lift.transform.right, lift.transform.forward, -lift.transform.forward };
        float[] halves = { scale.x * 0.5f, scale.x * 0.5f, scale.z * 0.5f, scale.z * 0.5f };
        for (int i = 0; i < sides.Length; i++)
        {
            Vector3 entry = new Vector3(bottom.x, groundY, bottom.z) + sides[i] * (halves[i] + 0.7f);
            if (!NavMesh.SamplePosition(entry, out NavMeshHit entryHit, 0.4f, SpaceJunkBotNavMesh.Filter) ||
                Mathf.Abs(entryHit.position.y - groundY) > 0.4f)
            {
                continue;
            }

            float width = Mathf.Min(scale.x, scale.z) * 0.6f;
            if (AddLink(entryHit.position, topHit.position, width, agentTypeID))
            {
                liftLinks.Add(new LiftLink { lift = lift, entry = entryHit.position, top = topHit.position });
            }
        }
    }

    /// <summary>回る一本道の両はしと、すき間の向こうの床をつなぐ（すき間は地図の上ではつながらないため）。</summary>
    private static void AddSpinnerLinks(SpaceJunkRotatingFloor spinner, int agentTypeID)
    {
        Vector3 scale = Abs(spinner.transform.lossyScale);
        bool alongX = scale.x >= scale.z;
        Vector3 axis = alongX ? spinner.transform.right : spinner.transform.forward;
        axis.y = 0f;
        axis.Normalize();
        float half = (alongX ? scale.x : scale.z) * 0.5f;
        float width = (alongX ? scale.z : scale.x) * 0.6f;
        Vector3 center = spinner.transform.position + Vector3.up * (scale.y * 0.5f);

        foreach (float sign in new[] { 1f, -1f })
        {
            Vector3 inner = center + axis * sign * (half - 0.8f);
            if (!NavMesh.SamplePosition(inner, out NavMeshHit innerHit, 0.6f, SpaceJunkBotNavMesh.Filter))
            {
                continue;
            }

            for (float beyond = 0.8f; beyond <= 2f; beyond += 0.3f)
            {
                Vector3 outer = center + axis * sign * (half + beyond);
                if (NavMesh.SamplePosition(outer, out NavMeshHit outerHit, 0.4f, SpaceJunkBotNavMesh.Filter) &&
                    Mathf.Abs(outerHit.position.y - innerHit.position.y) < 0.4f)
                {
                    AddLink(innerHit.position, outerHit.position, width, agentTypeID);
                    break;
                }
            }
        }
    }

    private static bool AddLink(Vector3 start, Vector3 end, float width, int agentTypeID)
    {
        NavMeshLinkInstance link = NavMesh.AddLink(new NavMeshLinkData
        {
            startPosition = start,
            endPosition = end,
            width = width,
            costModifier = -1f,
            bidirectional = true,
            area = 0,
            agentTypeID = agentTypeID
        });

        if (!NavMesh.IsLinkValid(link))
        {
            return false;
        }

        links.Add(link);
        return true;
    }

    private static void RemoveLinks()
    {
        foreach (NavMeshLinkInstance link in links)
        {
            if (NavMesh.IsLinkValid(link))
            {
                NavMesh.RemoveLink(link);
            }
        }
        links.Clear();
        liftLinks.Clear();
    }

    // ------------------------------------------------------------
    // ボットが聞くこと
    // ------------------------------------------------------------

    /// <summary><paramref name="point"/> の足もとのガラス（無ければ null）。</summary>
    public static GravityGlassFloor GlassAt(Vector3 point)
    {
        foreach (GravityGlassFloor glass in glasses)
        {
            if (glass != null && glass.Contains(point))
            {
                return glass;
            }
        }
        return null;
    }

    /// <summary><paramref name="point"/> の足もとのベルト（無ければ null）。</summary>
    public static SpaceJunkConveyor BeltAt(Vector3 point)
    {
        foreach (SpaceJunkConveyor belt in belts)
        {
            if (belt == null || !belt.TryGetComponent(out BoxCollider box))
            {
                continue;
            }

            Vector3 local = belt.transform.InverseTransformPoint(point);
            Vector3 half = box.size * 0.5f;
            float height = Vector3.Dot(point - belt.transform.position, belt.transform.up);
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z && height > -0.6f && height < 1.5f)
            {
                return belt;
            }
        }
        return null;
    }

    /// <summary>
    /// **そこに立って撃ってよいか。** 線路の帯の中・ベルトの上・もうすぐ割れるガラスは false。
    /// 割れるガラスの上は立ってよいが、<paramref name="penalty"/> で減点する（できれば硬い床から撃つ）。
    /// </summary>
    public static bool CheckStandPoint(Vector3 floor, out float penalty)
    {
        penalty = 0f;

        foreach (SpaceJunkTrain train in trains)
        {
            if (train != null && train.InBand(floor, TrackMargin))
            {
                return false;
            }
        }

        if (BeltAt(floor) != null)
        {
            return false;
        }

        foreach (SpaceJunkLiftFloor lift in lifts)
        {
            if (lift != null && IsOnLift(lift, floor))
            {
                return false; // 撃っている間に上がり下がりしてしまう
            }
        }

        GravityGlassFloor glass = GlassAt(floor);
        if (glass != null)
        {
            if (!glass.CanStandFor(GlassHoleSeconds))
            {
                return false;
            }
            if (glass.Type == GravityGlassFloor.GlassType.CounterBreak)
            {
                penalty += 2f;
            }
            else
            {
                // 撃っている間に割れるので、すぐとなりへ逃げられる「ふちの近く」がよい
                penalty += 8f + glass.DistanceToEdge(floor) * 6f;
            }
        }

        return true;
    }

    /// <summary>足もとのガラスが、乗っていると割れるタイプか（立ち止まって考えている時間を省くのに使う）。</summary>
    public static bool OnBreakingGlass(Vector3 point)
    {
        GravityGlassFloor glass = GlassAt(point);
        return glass != null && glass.Type != GravityGlassFloor.GlassType.CounterBreak;
    }

    /// <summary>足もとのガラスが割れて落ちるまでの秒数（ガラスでない・割れる予定が無ければ float.MaxValue）。</summary>
    public static float SecondsUntilFallAt(Vector3 point)
    {
        GravityGlassFloor glass = GlassAt(point);
        return glass != null ? glass.SecondsUntilFall : float.MaxValue;
    }

    /// <summary>
    /// 足もとのガラスがもうすぐ割れるなら、逃げる先（硬い床か、割れていないガラス）を探す。逃げなくてよければ false。
    /// </summary>
    public static bool TryFindGlassEscape(Vector3 me, float withinSeconds, out Vector3 destination)
    {
        destination = me;
        GravityGlassFloor under = GlassAt(me);
        if (under == null || under.SecondsUntilFall > withinSeconds)
        {
            return false;
        }

        float best = float.MaxValue;
        for (int ring = 1; ring <= 4; ring++)
        {
            float distance = 0.6f * ring;
            for (int i = 0; i < 12; i++)
            {
                Vector3 point = me + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * distance;
                if (!TryGetSurface(point, me.y, out Collider surface))
                {
                    continue;
                }

                GravityGlassFloor glass = surface.GetComponentInParent<GravityGlassFloor>();
                if (glass == under || (glass != null && !glass.CanStandFor(0.9f)))
                {
                    continue;
                }

                float score = distance + (glass != null ? 1.5f : 0f);
                if (score < best)
                {
                    best = score;
                    destination = point;
                }
            }
        }

        return best < float.MaxValue;
    }

    /// <summary>真下に、乗れる床（人・物資・ベルト・線路の列車でない、動かない当たり判定）があるか。</summary>
    private static bool TryGetSurface(Vector3 point, float footY, out Collider surface)
    {
        surface = null;
        int count = Physics.RaycastNonAlloc(new Vector3(point.x, footY + 1f, point.z), Vector3.down, rayHits, 2f, ~0,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider hit = rayHits[i].collider;
            if (hit == null || hit is CharacterController || hit.GetComponentInParent<HookableObject>() != null ||
                hit.GetComponentInParent<CharacterController>() != null || hit.GetComponentInParent<SpaceJunkTrain>() != null)
            {
                continue;
            }

            Rigidbody body = hit.attachedRigidbody;
            if (body != null && !body.isKinematic)
            {
                continue;
            }

            if (rayHits[i].distance < nearest && Mathf.Abs(rayHits[i].point.y - footY) < 0.5f)
            {
                nearest = rayHits[i].distance;
                surface = hit;
            }
        }

        return surface != null && BeltAt(point) == null;
    }

    /// <summary>列車が <paramref name="point"/> を通るまでの秒数（来なければ float.MaxValue）。</summary>
    public static float SecondsUntilTrain(Vector3 point, float horizon)
    {
        float soonest = float.MaxValue;
        foreach (SpaceJunkTrain train in trains)
        {
            if (train != null)
            {
                soonest = Mathf.Min(soonest, train.SecondsUntilPasses(point, TrackMargin, horizon));
            }
        }
        return soonest;
    }

    /// <summary>線路の帯の中にいるなら、いちばん近い帯の外（床がある側）。帯の外なら false。</summary>
    public static bool TryFindTrackExit(Vector3 me, out Vector3 exit)
    {
        exit = me;
        foreach (SpaceJunkTrain train in trains)
        {
            if (train == null || !train.InBand(me, TrackMargin))
            {
                continue;
            }

            Vector3 right = train.transform.right;
            right.y = 0f;
            right.Normalize();
            float side = Vector3.Dot(me - train.transform.position, right);
            float edge = train.HalfWidth + TrackMargin + 0.6f;
            float first = side >= 0f ? 1f : -1f;

            foreach (float sign in new[] { first, -first })
            {
                Vector3 candidate = me + right * (sign * edge - side);
                if (TryGetSurface(candidate, me.y, out _))
                {
                    exit = candidate;
                    return true;
                }
            }

            exit = me + right * (first * edge - side);
            return true;
        }

        return false;
    }

    /// <summary><paramref name="point"/> から <paramref name="ahead"/> へ進むと、列車が来る前に線路の帯を渡りきれないか。</summary>
    public static bool ShouldWaitForTrain(Vector3 point, Vector3 ahead, float speed)
    {
        if (trains.Length == 0)
        {
            return false;
        }

        Vector3 step = ahead - point;
        step.y = 0f;
        if (step.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 look = point + step.normalized * 1f;
        foreach (SpaceJunkTrain train in trains)
        {
            if (train == null || train.InBand(point, TrackMargin) || !train.InBand(look, TrackMargin))
            {
                continue;
            }

            float cross = (train.HalfWidth + TrackMargin) * 2f + 1.5f;
            float needed = cross / Mathf.Max(1f, speed) + 0.8f;
            if (train.SecondsUntilPasses(look, TrackMargin, needed) <= needed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ベルトに乗って流れている物資なら、流れる速さと、ベルトのはしで落ちるまでの秒数（落ちない・次のベルトがあるなら大きな数）。
    /// ベルトに乗っていない・もうすぐ場外へ落ちるなら false。
    /// </summary>
    public static bool TryGetBeltMotion(HookableObject item, out Vector3 velocity, out float secondsToEnd)
    {
        velocity = Vector3.zero;
        secondsToEnd = float.MaxValue;
        if (item == null || belts.Length == 0)
        {
            return false;
        }

        SpaceJunkConveyor belt = BeltAt(item.transform.position);
        if (belt == null || !belt.TryGetComponent(out BoxCollider box))
        {
            return false;
        }

        Vector3 flow = belt.FlowDirection;
        flow.y = 0f;
        Rigidbody body = item.Body;
        float speed = body != null ? Vector3.Dot(body.linearVelocity, flow.normalized) : 0f;
        if (speed < 0.5f)
        {
            return false;
        }

        velocity = flow.normalized * speed;

        // ベルトのはしまでの距離。その先に床が無ければ、そこで落ちる
        Vector3 local = belt.transform.InverseTransformPoint(item.transform.position);
        float halfLength = box.size.z * 0.5f * Mathf.Abs(belt.transform.lossyScale.z);
        float along = local.z * Mathf.Abs(belt.transform.lossyScale.z);
        float toEnd = Vector3.Dot(flow, belt.transform.forward) >= 0f ? halfLength - along : halfLength + along;
        Vector3 end = item.transform.position + flow.normalized * (toEnd + 1f);
        if (!Physics.Raycast(end + Vector3.up * 1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            secondsToEnd = Mathf.Max(0f, toEnd) / speed;
        }

        return secondsToEnd > 1.2f;
    }

    /// <summary>
    /// 道の「<paramref name="from"/> → <paramref name="to"/>」が、上下する床で上がるところなら、その床。
    /// </summary>
    public static bool TryGetLiftAscent(Vector3 from, Vector3 to, out LiftLink link)
    {
        foreach (LiftLink candidate in liftLinks)
        {
            if (candidate.lift != null && Flat(from - candidate.entry) < 0.6f && Flat(to - candidate.top) < 0.6f && to.y > from.y + 0.5f)
            {
                link = candidate;
                return true;
            }
        }

        link = default;
        return false;
    }

    /// <summary><paramref name="point"/> が、上下する床の上（真上から見て床の中）にあるか。</summary>
    public static bool IsOnLift(SpaceJunkLiftFloor lift, Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(lift.transform.rotation) * (point - lift.transform.position);
        Vector3 half = Abs(lift.transform.lossyScale) * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>上下する床が下で止まっていて、乗り込む時間があるか。</summary>
    public static bool CanBoard(SpaceJunkLiftFloor lift, float walkSeconds)
    {
        return lift.SecondsLeftAtBottom() > walkSeconds + LiftBoardSeconds;
    }

    private static float Flat(Vector3 v)
    {
        v.y = 0f;
        return v.magnitude;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
