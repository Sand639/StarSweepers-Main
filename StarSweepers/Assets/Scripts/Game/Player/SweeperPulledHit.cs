using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **引っ張られている物にぶつかった人を、吹き飛ばして動けなくする。**（2026/11/21・小野田さんの依頼）
///
/// ほかの人が光線で引っ張っている物資（<see cref="SweeperReel"/> の巻き取り中・重い物の引きずり中）が
/// 自分の体にぶつかると、次のことが起きる。
///
/// | 起きること | 中身 |
/// | --- | --- |
/// | 吹き飛ぶ | 物資から離れる向き＋少し上へ（<see cref="ILaunchable.Launch"/>。爆弾・列車と同じ仕組み） |
/// | 動けなくなる | <see cref="PlayerStun"/> で Stun Seconds 秒。移動も、光線を出すこと（溜め・伸ばす）もできない |
/// | 持っている物を落とす | 自分が巻き取り中なら、投げずにその場で離す（<see cref="SweeperShooter.CancelShot"/>） |
/// | **ぶつかった物が爆弾なら** | **その場ですぐ爆発する**（<see cref="ExplosiveObject.ExplodeNow"/>）。ぶつからなければ今までどおり時限式 |
/// | **ぶつかった物が電気のデブリなら** | <see cref="ElectricDebris"/> の値を使う（**長く痺れる・ほぼ吹き飛ばない**）。痺れている間、体に電気が走る |
///
/// 引っ張っている本人には当たらない。投げたあと（飛んでいる物）は対象外（「引っ張っている最中」だけ）。
///
/// ### どのPCが判定するか（オンライン）
///
/// **ぶつかられた人のPCが、自分の体についてだけ判定する**（爆弾の被害と同じ考え方。<see cref="NetBomb"/>）。
/// 自分の画面で当たったら自分が飛ぶので、操作が止まるまでの遅れが無い。ボットの体はホストのPCで判定する。
/// 「誰が引っ張っているか」は、物資の持ち主の番号（<see cref="NetSupply.HookedByPlayerIndex"/>）で全員にそろっている。
/// 爆弾の即爆発だけは、ホスト経由で全員に配る（<see cref="NetBomb.RequestDetonate"/>）。
///
/// この部品はプレハブに付けなくても、<see cref="SweeperReel"/> が起動時に自分で付ける（値を変えたいときだけプレハブに付ける）。
/// 詳しくは `Documents/機能ドキュメント/引っ張った物がぶつかった人のスタン.md`。
/// </summary>
public class SweeperPulledHit : MonoBehaviour
{
    [Header("ぶつかったときの効果（いちばん速くぶつかったとき）")]
    [Tooltip("ぶつかった人が動けなくなる秒数（移動・光線・持つことができない）。Speed Scaling が ON なら、Max Stun Speed 以上でぶつかったときの秒数")]
    [Min(0f)]
    [SerializeField] private float stunSeconds = 1.8f;

    [Tooltip("横方向に吹き飛ばす速さ（m/秒）。Speed Scaling が ON なら、いちばん速くぶつかったときの値")]
    [Min(0f)]
    [SerializeField] private float knockbackSpeed = 11f;

    [Tooltip("上向きに飛ばす速さ（m/秒）。0 で横だけ。Speed Scaling が ON なら、いちばん速くぶつかったときの値")]
    [Min(0f)]
    [SerializeField] private float knockbackLift = 4.5f;

    [Header("ぶつかった速さで強さを変える（2026/11/24）")]
    [Tooltip("ON：引っ張られている物の速さで、スタンの秒数と吹き飛ばしを変える（遅ければスタンしない）。OFF：いつも上のいちばん強い値")]
    [SerializeField] private bool speedScaling = true;

    [Tooltip("これより遅い物（m/秒）にぶつかっても、スタンも吹き飛ばしもしない（爆弾は速さに関係なく即爆発する）")]
    [Min(0f)]
    [SerializeField] private float minStunSpeed = 1.5f;

    [Tooltip("この速さ（m/秒）以上でぶつかったら、いちばん強い（上の値）")]
    [Min(0.01f)]
    [SerializeField] private float maxStunSpeed = 9f;

    [Tooltip("Min Stun Speed ちょうどでぶつかったときのスタンの秒数")]
    [Min(0f)]
    [SerializeField] private float minStunSeconds = 0.6f;

    [Tooltip("Min Stun Speed ちょうどでぶつかったときの横の吹き飛ばし（m/秒）")]
    [Min(0f)]
    [SerializeField] private float minKnockbackSpeed = 4f;

    [Tooltip("Min Stun Speed ちょうどでぶつかったときの上向きの速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float minKnockbackLift = 2f;

    [Tooltip("つかまれてからこの秒数の間は、ぶつかっても当たらない（つかんだ直後の、まだ動き出していない物で止まらないように）")]
    [Min(0f)]
    [SerializeField] private float grabGraceSeconds = 0.25f;

    [Tooltip("スタンが解けてから、また当たるようになるまでの秒数（同じ物に何度も当たり続けないように）")]
    [Min(0f)]
    [SerializeField] private float rehitGrace = 0.5f;

    [Header("当たる相手")]
    [Tooltip("ON：同じチームの人が引っ張っている物にも当たる。OFF：相手チームの物にだけ当たる")]
    [SerializeField] private bool hitByTeammates = true;

    [Tooltip("ON：引っ張られている爆弾が人にぶつかったら、その場ですぐ爆発する")]
    [SerializeField] private bool bombsExplodeOnHit = true;

    [Header("当たり判定")]
    [Tooltip("体の当たり判定より、どれだけ大きく見るか（m）。物は体に押し返されてめり込まないので、少し広げて「触れた」を拾う")]
    [Min(0f)]
    [SerializeField] private float contactPadding = 0.2f;

    /// <summary>いま有効なこの部品の一覧。「この物資を引っ張っているのは誰か」を探すのに使う。</summary>
    private static readonly List<SweeperPulledHit> All = new List<SweeperPulledHit>();

    // 当たり判定の結果を受け取る入れ物（毎回作らないよう使い回す）
    private static readonly Collider[] Overlaps = new Collider[32];

    private CharacterController characterController;
    private SweeperMover controller;
    private SweeperReel reel;
    private SweeperShooter shooter;
    private PlayerStun stun;
    private SweeperNetPlayer netPlayer;

    // この時刻までは当たらない（スタン中・スタン直後）
    private float immuneUntil;

    /// <summary>最後にぶつかった物（検証・テスト用）。</summary>
    public HookableObject LastHitItem { get; private set; }

    /// <summary>この体を引っ張っている物の巻き取り（自分のPCで動いているときだけ。オンラインのほかの人の体では動いていない）。</summary>
    public SweeperReel Reel => reel;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        controller = GetComponent<SweeperMover>();
        reel = GetComponent<SweeperReel>();
        shooter = GetComponent<SweeperShooter>();
        stun = GetComponent<PlayerStun>();
        netPlayer = GetComponent<SweeperNetPlayer>();
    }

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void FixedUpdate()
    {
        // スタン中・スタン後の無敵の間（PlayerStun の Invincible Seconds）は当たらない（2026/11/27）
        if (!IsControlledHere() || Time.time < immuneUntil || (stun != null && (stun.IsStunned || stun.IsInvincible)))
        {
            return;
        }

        HookableObject item = FindPulledItemTouching(out SweeperPulledHit puller, out int pullerIndex);
        if (item != null)
        {
            Hit(item, puller, pullerIndex);
        }
    }

    /// <summary>
    /// この体を、このPCで動かしているか（自分の体・ホストのボット）。
    /// オンラインのほかの人の体は当たり判定や移動が止めてあるので、判定しない（その人のPCが判定する）。
    /// </summary>
    private bool IsControlledHere()
    {
        if (characterController == null || !characterController.enabled)
        {
            return false;
        }

        // 吹き飛ばしの受け口（SweeperMover）が止まっている体には効かせられない
        return controller != null && controller.isActiveAndEnabled;
    }

    /// <summary>体に触れている「ほかの人が引っ張っている物」を探す。無ければ null。</summary>
    private HookableObject FindPulledItemTouching(out SweeperPulledHit puller, out int pullerIndex)
    {
        puller = null;
        pullerIndex = -1;

        // CharacterController の円柱（両端が丸い）を少し広げて調べる
        float radius = characterController.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) + contactPadding;
        float height = Mathf.Max(characterController.height * transform.lossyScale.y, radius * 2f);
        Vector3 center = transform.TransformPoint(characterController.center);
        float half = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half;

        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, Overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            HookableObject item = Overlaps[i].GetComponentInParent<HookableObject>();
            if (item == null || item.IsVanished || item.IsAbsorbing)
            {
                continue;
            }

            // つかまれた直後・遅すぎる物には当たらない（爆弾は遅くても当たったら即爆発させる。2026/11/24）
            if (Time.time - item.PulledSince < grabGraceSeconds)
            {
                continue;
            }
            bool bomb = item.GetComponent<ExplosiveObject>() != null;
            if (speedScaling && !bomb && item.MeasuredSpeed < minStunSpeed)
            {
                continue;
            }

            if (TryFindPuller(item, out puller, out pullerIndex) && CanBeHitBy(puller, pullerIndex))
            {
                return item;
            }
        }

        puller = null;
        pullerIndex = -1;
        return null;
    }

    /// <summary>
    /// この物資をいま引っ張っている人を探す。
    /// まずこのPCで動いている巻き取り（1人用・自分・ホストのボット）を見て、無ければオンラインの持ち主の番号を見る。
    /// </summary>
    private static bool TryFindPuller(HookableObject item, out SweeperPulledHit puller, out int pullerIndex)
    {
        puller = null;
        pullerIndex = -1;

        foreach (SweeperPulledHit other in All)
        {
            SweeperReel otherReel = other.reel;
            if (otherReel != null && otherReel.IsActive && !otherReel.IsWaitingForClaim && otherReel.Item == item)
            {
                // 両手で持っている物は「引っ張っている最中」ではない（2026/11/24）
                if (otherReel.IsCarrying)
                {
                    return false;
                }

                puller = other;
                pullerIndex = other.netPlayer != null && other.netPlayer.IsSpawned ? other.netPlayer.PlayerIndex : -1;
                return true;
            }
        }

        // オンラインで、ほかのPCの人が引っ張っている物（その人の巻き取りはこのPCでは動いていない）
        NetSupply supply = item.GetComponent<NetSupply>();
        if (supply != null && supply.IsSpawned && supply.IsClaimed)
        {
            pullerIndex = supply.HookedByPlayerIndex;
            if (SweeperNetSync.IsPlayerCarrying(pullerIndex))
            {
                pullerIndex = -1;
                return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>この人が引っ張っている物に、自分が当たるか（自分自身・設定で外した味方には当たらない）。</summary>
    private bool CanBeHitBy(SweeperPulledHit puller, int pullerIndex)
    {
        if (puller == this)
        {
            return false;
        }

        bool online = netPlayer != null && netPlayer.IsSpawned;
        if (online && pullerIndex >= 0 && pullerIndex == netPlayer.PlayerIndex)
        {
            return false;
        }

        if (!hitByTeammates && online && pullerIndex >= 0)
        {
            // チームはロビーでホストが決めたもの（参加番号の並びとは関係ない）
            int pullerTeam = SweeperNetPlayer.TeamOfPlayerIndex(pullerIndex);
            return pullerTeam < 0 || pullerTeam != netPlayer.TeamIndex;
        }

        return true;
    }

    /// <summary>ぶつかった：吹き飛んで、動けなくなり、持っている物を落とす。爆弾ならすぐ爆発させる。</summary>
    private void Hit(HookableObject item, SweeperPulledHit puller, int pullerIndex)
    {
        LastHitItem = item;

        // 持っている物（巻き取り中の物）を、投げずにその場で離す
        if (shooter != null)
        {
            shooter.CancelShot();
        }

        // 電気のデブリなら、そちらの値（長く痺れる・ほぼ吹き飛ばない）を使う
        // 速いほど強い（2026/11/24）。いちばん遅い（Min Stun Speed）で 0、Max Stun Speed 以上で 1
        float power = speedScaling ? StrengthForSpeed(item.MeasuredSpeed, minStunSpeed, maxStunSpeed) : 1f;
        ElectricDebris electric = item.GetComponent<ElectricDebris>();
        float hitStun = electric != null ? electric.StunSeconds : Mathf.Lerp(minStunSeconds, stunSeconds, power);
        float hitSpeed = electric != null ? electric.KnockbackSpeed : Mathf.Lerp(minKnockbackSpeed, knockbackSpeed, power);
        float hitLift = electric != null ? electric.KnockbackLift : Mathf.Lerp(minKnockbackLift, knockbackLift, power);

        Vector3 from = item.Body != null ? item.Body.worldCenterOfMass : item.transform.position;
        Vector3 velocity = KnockbackVelocity(from, transform.position, transform.forward, hitSpeed, hitLift);
        MeteorStrikeEvent.ApplyPlayerImpact(controller, stun, velocity, hitStun);
        immuneUntil = Time.time + hitStun + rehitGrace;

        if (electric != null)
        {
            electric.PlayShock(transform, hitStun);
            // ほかの人の画面にも、体に電気が走るのを出す（2026/11/24）
            GetComponent<SweeperNetSync>()?.BroadcastShock(hitStun);
        }

        if (bombsExplodeOnHit)
        {
            DetonateIfBomb(item);
        }
    }

    /// <summary>ぶつかった物が爆弾なら、すぐ爆発させる。オンラインではホスト経由で全員の画面で爆発させる。</summary>
    private static void DetonateIfBomb(HookableObject item)
    {
        ExplosiveObject bomb = item.GetComponent<ExplosiveObject>();
        if (bomb == null)
        {
            return;
        }

        NetBomb netBomb = item.GetComponent<NetBomb>();
        if (netBomb != null && netBomb.IsSpawned)
        {
            netBomb.RequestDetonate();
        }
        else
        {
            bomb.ExplodeNow();
        }
    }

    /// <summary>ぶつかった速さから、強さ（0〜1）を出す。<paramref name="min"/> 以下で 0、<paramref name="max"/> 以上で 1。</summary>
    public static float StrengthForSpeed(float speed, float min, float max)
    {
        return max > min ? Mathf.Clamp01((speed - min) / (max - min)) : (speed >= min ? 1f : 0f);
    }

    /// <summary>
    /// 物資から人へ向かう水平の向き × 横の速さ ＋ 上向きの速さ。
    /// ちょうど重なっていて向きが決まらないときは、その人の後ろへ飛ばす。
    /// </summary>
    public static Vector3 KnockbackVelocity(Vector3 itemPosition, Vector3 playerPosition, Vector3 playerForward,
        float horizontal, float lift)
    {
        Vector3 direction = playerPosition - itemPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = -playerForward;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.back;
        }

        return direction.normalized * horizontal + Vector3.up * lift;
    }
}
