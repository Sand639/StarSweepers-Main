using UnityEngine;

/// <summary>GravityGlassFloor の「ボットに教える情報」（読むだけ。床の動きは変えない）。</summary>
public partial class GravityGlassFloor
{
    /// <summary>ガラスのタイプ（踏むと割れる・乗り直しの回数・乗っている時間）。</summary>
    public GlassType Type => glasstype;

    /// <summary>割れている（当たり判定が無い）か。</summary>
    public bool IsBroken => State == FloorState.Broken || finalCracks != null;

    /// <summary>新しく乗ると、その場で割れてしまうか（乗り直しの回数のタイプで、あと1回のとき）。</summary>
    public bool BreaksOnNextEntry => glasstype == GlassType.CounterBreak && stepCount + 1 >= Mathf.Max(1, breakCount);

    /// <summary>
    /// これから乗っても、<paramref name="stay"/> 秒は割れずにいられるか。
    /// 踏むと割れるタイプは、乗った瞬間から Seconds Until Break で割れるので、ヒビが入っていなくてもその秒数までしか持たない。
    /// </summary>
    public bool CanStandFor(float stay)
    {
        if (IsBroken || BreaksOnNextEntry)
        {
            return false;
        }

        switch (glasstype)
        {
            case GlassType.CounterBreak:
                return true;
            case GlassType.StayTime:
                return Mathf.Max(0f, stayTime) - accumulatedStayTime > stay;
            default:
                if (State == FloorState.Cracked)
                {
                    return BreakRemaining > stay;
                }
                // 重力が軽いうちは、乗ってもヒビが入らない
                return Mathf.Abs(WorldGravity.Value) < breakGravity || secondsUntilBreak > stay;
        }
    }

    /// <summary>乗っている人が、あと何秒で落ちるか（割れる予定が無ければ大きな数）。</summary>
    public float SecondsUntilFall
    {
        get
        {
            if (IsBroken)
            {
                return 0f;
            }

            switch (glasstype)
            {
                case GlassType.StayTime:
                    return State == FloorState.Cracked || accumulatedStayTime > 0f
                        ? Mathf.Max(0f, stayTime - accumulatedStayTime)
                        : float.MaxValue;
                case GlassType.StepBreak:
                    return State == FloorState.Cracked ? Mathf.Max(0f, BreakRemaining) : float.MaxValue;
                default:
                    return float.MaxValue;
            }
        }
    }

    /// <summary>上面の真ん中（ワールド座標）と、上から見た広さの半分（ローカルの x・z、m）。</summary>
    public Vector3 TopCenter => transform.TransformPoint(box.center) + transform.up * (Vector3.Scale(box.size, transform.lossyScale).y * 0.5f);

    /// <summary>床の大きさ（ワールドの m。x・z が広さ、y が厚み）。</summary>
    public Vector3 WorldSize
    {
        get
        {
            Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
            return new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }
    }

    /// <summary>真上から見て、<paramref name="point"/> からいちばん近い床のふちまでの距離（m。床の外なら 0）。</summary>
    public float DistanceToEdge(Vector3 point)
    {
        Vector3 local = transform.InverseTransformPoint(point) - box.center;
        Vector3 scale = transform.lossyScale;
        float x = (box.size.x * 0.5f - Mathf.Abs(local.x)) * Mathf.Abs(scale.x);
        float z = (box.size.z * 0.5f - Mathf.Abs(local.z)) * Mathf.Abs(scale.z);
        return Mathf.Max(0f, Mathf.Min(x, z));
    }

    /// <summary><paramref name="point"/> が、この床の上（真上から見て床の中、高さは上面の近く）にあるか。</summary>
    public bool Contains(Vector3 point, float above = 1.2f, float below = 0.6f)
    {
        if (box == null)
        {
            return false;
        }

        Vector3 local = transform.InverseTransformPoint(point) - box.center;
        Vector3 half = box.size * 0.5f;
        if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.z) > half.z)
        {
            return false;
        }

        float height = Vector3.Dot(point - TopCenter, transform.up);
        return height <= above && height >= -below;
    }
}
