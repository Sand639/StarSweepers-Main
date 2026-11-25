using UnityEngine;

/// <summary>
/// **引っ張り合い（2人が同じ物資を光線でつかんだとき）の物理。**（2026/11/24・小野田さんの依頼）
///
/// 2本の光線を「プレイヤーの手から物資へ張ったばね」と考え、物資に2つの力を加えて動かす。
///
/// | 力 | 中身 |
/// | --- | --- |
/// | いつもの引く力 | 自分のほうへ Base Pull（m/秒²） |
/// | 引っぱり下がる | 物資から離れる向きに歩いた速さ × Yank Gain |
/// | 振り回す | 体を回す速さ（光線の先の速さ。Max Swing Tip まで）× Swing Gain。物資を横へ振る力（Swing Side）も足す |
///
/// **どちらが切れるか**：光線ごとに「引きたいのに物資が離れていった距離」をためる。**先に Snap Strain を超えた側の光線が切れる**
/// ＝相手のほうが力が強かったということ（いつもの「光線がちぎれる」と同じ考え）。
/// どちらかの手前（Win Distance）まで来たら、引き寄せた側の勝ち。光線の長さ（Max Length）を超えて離れた側も切れる。
/// Max Seconds たっても決まらなければ、両方切れる。
///
/// 誰が動かすか：通信なしならこのPC、オンラインなら**ホスト**（<see cref="NetSupply"/> が持ち主を取り戻してから始める）。
/// 両方のプレイヤーの位置と向きは、ホストにも届いているので、追加の通信は要らない。
/// 詳しくは `Documents/機能ドキュメント/引っ張り合い.md`。
/// </summary>
public class SupplyTug : MonoBehaviour
{
    /// <summary>終わり方：つかんでいた人の光線が切れた（後から来た人の勝ち）。</summary>
    public const int HolderLost = 0;

    /// <summary>終わり方：後から来た人の光線が切れた（つかんでいた人の勝ち）。</summary>
    public const int ChallengerLost = 1;

    /// <summary>終わり方：決着がつかず、両方切れた。</summary>
    public const int BothLost = 2;

    /// <summary>引っ張り合いの値（つかんでいた人の <see cref="SweeperReel"/> のインスペクターから渡す）。</summary>
    [System.Serializable]
    public struct Settings
    {
        public float basePull;
        public float yankGain;
        public float swingGain;
        public float maxSwingTip;
        public float swingSide;
        public float drag;
        public float maxSpeed;
        public float snapStrain;
        public float strainRecovery;
        public float maxSeconds;
        public float winDistance;
        public float maxLength;
        public float handHeight;
    }

    private struct Side
    {
        public Transform body;
        public Vector3 lastPosition;
        public float lastYaw;
        public float strain;
    }

    private HookableObject item;
    private Rigidbody body;
    private Settings settings;
    private Side[] sides;
    private System.Action<int> onEnd;
    private float startTime;
    private float holdY;
    private Vector3 velocity;
    private bool running;

    // 毎ステップ作らないよう使い回す入れ物（光線ごとの、物資からプレイヤーへの向きと距離）
    private readonly Vector3[] towards = new Vector3[2];
    private readonly float[] distances = new float[2];

    /// <summary>いま引っ張り合いの最中か。</summary>
    public bool IsRunning => running;

    /// <summary>光線ごとのたまった力（0 がつかんでいた人、1 が後から来た人）。0〜Snap Strain。表示・テスト用。</summary>
    public float StrainOf(int side) => sides != null && side >= 0 && side < sides.Length ? sides[side].strain : 0f;

    /// <summary>
    /// 引っ張り合いを始める。物資に付いていた前の引っ張り合いは止める。
    /// <paramref name="ended"/> は終わったときに、終わり方（<see cref="HolderLost"/> など）を渡して呼ばれる。
    /// </summary>
    public static SupplyTug Begin(HookableObject target, Transform holder, Transform challenger, Settings values,
        System.Action<int> ended)
    {
        if (target == null || target.Body == null || holder == null || challenger == null)
        {
            return null;
        }

        SupplyTug tug = target.GetComponent<SupplyTug>();
        if (tug == null)
        {
            tug = target.gameObject.AddComponent<SupplyTug>();
        }

        tug.item = target;
        tug.body = target.Body;
        tug.settings = values;
        tug.onEnd = ended;
        tug.startTime = Time.time;
        tug.holdY = target.Body.position.y;
        tug.velocity = Vector3.zero;
        tug.sides = new[] { MakeSide(holder), MakeSide(challenger) };
        tug.running = true;
        tug.enabled = true;
        return tug;
    }

    /// <summary>その物資でいま動いている引っ張り合い（無ければ null）。</summary>
    public static SupplyTug Of(HookableObject target)
    {
        SupplyTug tug = target != null ? target.GetComponent<SupplyTug>() : null;
        return tug != null && tug.running ? tug : null;
    }

    private static Side MakeSide(Transform body)
    {
        return new Side { body = body, lastPosition = body.position, lastYaw = body.eulerAngles.y, strain = 0f };
    }

    /// <summary>終わりの知らせを出さずに止める（物資が消えた・爆風で外されたなど）。</summary>
    public void Stop()
    {
        running = false;
        onEnd = null;
        enabled = false;
    }

    /// <summary>片方が自分から手を離した（スタンなど）。相手の勝ちで終わる。</summary>
    public void Abandon(int side)
    {
        if (!running)
        {
            return;
        }

        End(side == 0 ? HolderLost : ChallengerLost);
    }

    private void End(int result)
    {
        running = false;
        enabled = false;
        System.Action<int> ended = onEnd;
        onEnd = null;
        ended?.Invoke(result);
    }

    private void FixedUpdate()
    {
        if (!running)
        {
            enabled = false;
            return;
        }

        if (item == null || body == null || item.IsVanished || !item.gameObject.activeInHierarchy)
        {
            Stop();
            return;
        }

        float dt = Time.fixedDeltaTime;
        int result = Step(dt);
        if (result >= 0)
        {
            End(result);
        }
    }

    /// <summary>1ステップ進める。決着したら終わり方、まだなら -1。</summary>
    private int Step(float dt)
    {
        // どちらかの体がいなくなった（抜けた・消えた）ら、その人の負け
        if (sides[0].body == null || !sides[0].body.gameObject.activeInHierarchy)
        {
            return HolderLost;
        }
        if (sides[1].body == null || !sides[1].body.gameObject.activeInHierarchy)
        {
            return ChallengerLost;
        }

        Vector3 itemPosition = body.position;
        Vector3 accel = Vector3.zero;

        for (int i = 0; i < 2; i++)
        {
            Transform puller = sides[i].body;
            Vector3 anchor = puller.position + Vector3.up * settings.handHeight;
            Vector3 toward = anchor - itemPosition;
            toward.y = 0f;
            float distance = toward.magnitude;
            toward = distance > 0.001f ? toward / distance : Vector3.forward;
            towards[i] = toward;
            distances[i] = distance;

            // 体が動いた速さと、回った速さ（このステップぶん）
            Vector3 moved = puller.position - sides[i].lastPosition;
            moved.y = 0f;
            Vector3 pullerVelocity = moved / dt;
            float yaw = puller.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(sides[i].lastYaw, yaw) / dt;
            sides[i].lastPosition = puller.position;
            sides[i].lastYaw = yaw;

            // toward は物資からプレイヤーへの向きなので、同じ向きに動いている＝物資から離れている
            float yank = Mathf.Max(0f, Vector3.Dot(pullerVelocity, toward));
            float tipSpeed = Mathf.Min(Mathf.Abs(yawRate) * Mathf.Deg2Rad * distance, settings.maxSwingTip);
            float pull = settings.basePull + settings.yankGain * yank + settings.swingGain * tipSpeed;

            // 振り回すと、物資は回した向きへ横にも振られる（真上から見て、回した向きの接線）
            Vector3 tangent = Vector3.Cross(Vector3.up, toward) * -Mathf.Sign(yawRate);
            accel += toward * pull + tangent * (settings.swingSide * settings.swingGain * tipSpeed);
        }

        velocity += accel * dt;
        velocity *= Mathf.Exp(-settings.drag * dt);
        if (velocity.magnitude > settings.maxSpeed)
        {
            velocity = velocity.normalized * settings.maxSpeed;
        }

        ApplyVelocity(dt);

        // 光線ごとに「引きたいのに離れていった距離」をためる
        for (int i = 0; i < 2; i++)
        {
            float coming = Vector3.Dot(velocity, towards[i]);
            if (coming < 0f)
            {
                sides[i].strain += -coming * dt;
            }
            else
            {
                sides[i].strain = Mathf.Max(0f, sides[i].strain - settings.strainRecovery * dt);
            }
        }

        // 決着：手前まで引き寄せた側の勝ち → 光線が伸びきった側の負け → たまった力が先に超えた側の負け → 時間切れ
        if (distances[0] <= settings.winDistance)
        {
            return ChallengerLost;
        }
        if (distances[1] <= settings.winDistance)
        {
            return HolderLost;
        }
        if (settings.maxLength > 0f && distances[0] > settings.maxLength)
        {
            return HolderLost;
        }
        if (settings.maxLength > 0f && distances[1] > settings.maxLength)
        {
            return ChallengerLost;
        }

        bool holderSnaps = sides[0].strain >= settings.snapStrain;
        bool challengerSnaps = sides[1].strain >= settings.snapStrain;
        if (holderSnaps || challengerSnaps)
        {
            if (holderSnaps && challengerSnaps)
            {
                return sides[0].strain >= sides[1].strain ? HolderLost : ChallengerLost;
            }
            return holderSnaps ? HolderLost : ChallengerLost;
        }

        if (settings.maxSeconds > 0f && Time.time - startTime >= settings.maxSeconds)
        {
            return BothLost;
        }

        return -1;
    }

    /// <summary>決めた速さで物資を動かす。上下は、始めたときの高さより上なら落とし、下へは沈ませない（重力の設定に関係なく）。</summary>
    private void ApplyVelocity(float dt)
    {
        float vy = body.isKinematic ? 0f : body.linearVelocity.y;
        float above = body.position.y - holdY;
        if (above > 0.05f)
        {
            vy += Physics.gravity.y * dt;
            if (vy < 0f && body.position.y + vy * dt < holdY)
            {
                vy = (holdY - body.position.y) / dt;
            }
        }
        else
        {
            // 持つ高さ以下：沈み続けないよう下向きはすぐ止め、上向きはゆっくり止める
            vy = vy < 0f ? 0f : Mathf.MoveTowards(vy, 0f, 20f * dt);
        }

        Vector3 next = new Vector3(velocity.x, vy, velocity.z);
        if (body.isKinematic)
        {
            body.MovePosition(body.position + next * dt);
            return;
        }

        // 物資の重力が入っていても同じ動きになるよう、このステップで加わる重力を先に打ち消しておく
        if (body.useGravity)
        {
            next.y -= Physics.gravity.y * dt;
        }

        body.linearVelocity = next;
        body.angularVelocity *= Mathf.Exp(-4f * dt);
    }
}
