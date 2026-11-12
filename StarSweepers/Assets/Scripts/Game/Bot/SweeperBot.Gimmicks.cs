using UnityEngine;

/// <summary>SweeperBot の「ギミックを使う・避ける」（SweeperBot.cs から分けた部分。2026/11/12）。</summary>
public partial class SweeperBot
{
    /// <summary>光線が伸びる速さの目安（m/秒。<see cref="SweeperShooter"/> の Extend Speed の初期値）。流れる物資を狙うときの先読みに使う。</summary>
    private const float BeamSpeed = 30f;

    /// <summary>ふつうに歩く速さの目安（m/秒。<see cref="SweeperMover"/> の Move Speed の初期値）。</summary>
    private const float WalkSpeed = 6f;

    /// <summary>列車が来るまでこの秒数より短ければ、線路の帯から出る。</summary>
    private const float TrainDangerSeconds = 2f;

    /// <summary>ベルトで流れている素材を狙うとき、何秒ごとに立つ場所を決め直すか。</summary>
    private const float BeltReplanSeconds = 0.4f;

    /// <summary>乗って上がるのを待っている、上下する床（乗っていなければ null）。</summary>
    private SpaceJunkLiftFloor ridingLift;

    // ------------------------------------------------------------
    // 安全（列車・割れるガラス）
    // ------------------------------------------------------------

    /// <summary>列車が来る・足もとのガラスが割れそうなら、逃げる向きにスティックを倒す。逃げたら true。</summary>
    private bool ApplySafetyMove(SweeperShooter.ShotPhase phase)
    {
        if (phase == SweeperShooter.ShotPhase.Extending || phase == SweeperShooter.ShotPhase.Retracting)
        {
            return false; // 光線が伸びている・戻っている間は歩けない
        }

        Vector3 me = transform.position;
        bool idle = phase == SweeperShooter.ShotPhase.Idle;

        if (SweeperBotGimmicks.SecondsUntilTrain(me, TrainDangerSeconds) < TrainDangerSeconds &&
            SweeperBotGimmicks.TryFindTrackExit(me, out Vector3 exit))
        {
            Steer(exit, idle);
            ResetStuck();
            return true;
        }

        float within = phase == SweeperShooter.ShotPhase.Reeling
            ? SweeperBotGimmicks.GlassEscapeSecondsSlow
            : SweeperBotGimmicks.GlassEscapeSeconds;
        if (SweeperBotGimmicks.TryFindGlassEscape(me, within, out Vector3 safe))
        {
            Steer(safe, idle);
            ResetStuck();
            return true;
        }

        return false;
    }

    /// <summary>スティックだけを <paramref name="destination"/> へ倒す（狙っている間は、カーソルは動かさない）。</summary>
    private void Steer(Vector3 destination, bool moveCursor)
    {
        if (moveCursor)
        {
            WalkTo(destination);
            return;
        }

        Vector3 direction = destination - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Camera view = Camera.main;
        float yaw = view != null ? view.transform.eulerAngles.y : 0f;
        Vector3 stick = Quaternion.Euler(0f, -yaw, 0f) * direction.normalized;
        pad.Move = new Vector2(stick.x, stick.z);
    }

    /// <summary>足もとのガラスがこの秒数より早く割れるなら、光線を出さずに溜めたまま逃げる（伸びている間は歩けないため）。</summary>
    private const float GlassReleaseSeconds = 0.5f;

    /// <summary>巻き取り中、足もとのガラスがこの秒数より早く割れるなら、すぐ投げる（投げ終われば速く歩ける）。</summary>
    private const float GlassThrowSeconds = 0.35f;

    private bool ShouldHoldShotForGlass() => SweeperBotGimmicks.SecondsUntilFallAt(transform.position) < GlassReleaseSeconds;

    private bool ShouldThrowNowForGlass() => SweeperBotGimmicks.SecondsUntilFallAt(transform.position) < GlassThrowSeconds;

    /// <summary>撃つ場所に着いてから撃ち始めるまでの秒数。割れるガラスの上では待たない。</summary>
    private float ThinkSeconds => SweeperBotGimmicks.OnBreakingGlass(transform.position) ? 0f : thinkSeconds;

    // ------------------------------------------------------------
    // ベルトで流れている素材
    // ------------------------------------------------------------

    /// <summary>狙う候補にしてよいほど止まっているか（ベルトで流れているだけなら、動いていても狙う）。</summary>
    private static bool IsSettled(HookableObject item)
    {
        return !IsMoving(item) || SweeperBotGimmicks.TryGetBeltMotion(item, out _, out _);
    }

    /// <summary>立つ場所を決めるときの素材の場所。ベルトで流れているなら、着くころの場所を先読みする。</summary>
    private Vector3 PlanItemPosition(HookableObject item)
    {
        Vector3 position = item.transform.position;
        if (!SweeperBotGimmicks.TryGetBeltMotion(item, out Vector3 velocity, out float secondsToEnd))
        {
            return position;
        }

        float lead = FlatDistance(transform.position, position) / WalkSpeed + 0.8f;
        return position + velocity * Mathf.Min(lead, secondsToEnd - 0.5f);
    }

    /// <summary>カーソルを合わせる場所。ベルトで流れているなら、光線が届くころの場所を先読みする。</summary>
    private Vector3 AimPoint(HookableObject item)
    {
        Vector3 position = item.transform.position;
        if (SweeperBotGimmicks.TryGetBeltMotion(item, out Vector3 velocity, out _))
        {
            position += velocity * (FlatDistance(transform.position, position) / BeamSpeed + 0.1f);
        }
        return position;
    }

    /// <summary>次に立つ場所を決め直すまでの秒数（流れている素材は、こまめに決め直す）。</summary>
    private float ReplanDelay(HookableObject item)
    {
        return SweeperBotGimmicks.TryGetBeltMotion(item, out _, out _) ? BeltReplanSeconds : ReplanSeconds;
    }

    // ------------------------------------------------------------
    // 道の途中（上下する床・線路）
    // ------------------------------------------------------------

    /// <summary>
    /// 道の途中でギミックを待つ・使う（上下する床に乗って上がる・列車が通り過ぎるまで線路の手前で待つ）。
    /// 自分でスティックを決めたら true（そのときは、ふつうに道をたどらない）。
    /// </summary>
    private bool HandlePathGimmicks()
    {
        if (HandleLiftRide())
        {
            return true;
        }

        if (pathCorners.Count == 0)
        {
            return false;
        }

        Vector3 next = pathCorners[cornerIndex];
        if (SweeperBotGimmicks.ShouldWaitForTrain(transform.position, next, WalkSpeed))
        {
            pad.Move = Vector2.zero;
            PointAt(next);
            ResetStuck();
            return true;
        }

        return false;
    }

    /// <summary>上下する床で上の段へ行くところなら、床が下に来るのを待って乗り、上に着くまで立っている。</summary>
    private bool HandleLiftRide()
    {
        if (ridingLift != null)
        {
            bool onLift = SweeperBotGimmicks.IsOnLift(ridingLift, transform.position);
            if (!onLift || ridingLift.SecondsLeftAtTop() >= 0f)
            {
                // 上に着いた（または落ちた）。道の「上下する床の真ん中」の角を済ませて、先へ進む
                if (onLift && cornerIndex < pathCorners.Count - 1 &&
                    FlatDistance(pathCorners[cornerIndex], ridingLift.BottomPosition) < 0.6f)
                {
                    cornerIndex++;
                }
                ridingLift = null;
                return false;
            }

            pad.Move = Vector2.zero;
            ResetStuck();
            return true;
        }

        if (cornerIndex < 1 || cornerIndex >= pathCorners.Count ||
            !SweeperBotGimmicks.TryGetLiftAscent(pathCorners[cornerIndex - 1], pathCorners[cornerIndex], out SweeperBotGimmicks.LiftLink link))
        {
            return false;
        }

        Vector3 center = link.lift.BottomPosition;
        if (SweeperBotGimmicks.IsOnLift(link.lift, transform.position) && link.lift.SecondsLeftAtBottom() >= 0f)
        {
            // 下にいる床に乗れた。上がるのを待つ
            ridingLift = link.lift;
            pad.Move = Vector2.zero;
            ResetStuck();
            return true;
        }

        float walkSeconds = FlatDistance(transform.position, center) / WalkSpeed;
        if (SweeperBotGimmicks.CanBoard(link.lift, walkSeconds))
        {
            WalkTo(center);
        }
        else
        {
            // 床が上にいる・もうすぐ上がる。乗り口で待つ
            if (FlatDistance(transform.position, link.entry) > 0.3f)
            {
                WalkTo(link.entry);
            }
            else
            {
                pad.Move = Vector2.zero;
                PointAt(center);
            }
        }

        ResetStuck();
        return true;
    }
}
