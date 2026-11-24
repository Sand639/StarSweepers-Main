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
/// **ぶつかられた人のPCが、自分の体についてだけ判定する**（爆弾の被害と同じ考え方。<see cref="FishingNetBomb"/>）。
/// 自分の画面で当たったら自分が飛ぶので、操作が止まるまでの遅れが無い。ボットの体はホストのPCで判定する。
/// 「誰が引っ張っているか」は、物資の持ち主の番号（<see cref="FishingNetSupply.HookedByPlayerIndex"/>）で全員にそろっている。
/// 爆弾の即爆発だけは、ホスト経由で全員に配る（<see cref="FishingNetBomb.RequestDetonate"/>）。
///
/// この部品はプレハブに付けなくても、<see cref="SweeperReel"/> が起動時に自分で付ける（値を変えたいときだけプレハブに付ける）。
/// 詳しくは `Documents/機能ドキュメント/引っ張った物がぶつかった人のスタン.md`。
/// </summary>
public class SweeperPulledHit : MonoBehaviour
{
    [Header("ぶつかったときの効果")]
    [Tooltip("ぶつかった人が動けなくなる秒数（移動・光線・持つことができない）")]
    [Min(0f)]
    [SerializeField] private float stunSeconds = 1.5f;

    [Tooltip("横方向に吹き飛ばす速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float knockbackSpeed = 9f;

    [Tooltip("上向きに飛ばす速さ（m/秒）。0 で横だけ")]
    [Min(0f)]
    [SerializeField] private float knockbackLift = 4f;

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
    private FishingPlayerController controller;
    private SweeperReel reel;
    private SweeperShooter shooter;
    private PlayerStun stun;
    private FishingNetPlayer netPlayer;

    // この時刻までは当たらない（スタン中・スタン直後）
    private float immuneUntil;

    /// <summary>最後にぶつかった物（検証・テスト用）。</summary>
    public HookableObject LastHitItem { get; private set; }

    /// <summary>この体を引っ張っている物の巻き取り（自分のPCで動いているときだけ。オンラインのほかの人の体では動いていない）。</summary>
    public SweeperReel Reel => reel;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        controller = GetComponent<FishingPlayerController>();
        reel = GetComponent<SweeperReel>();
        shooter = GetComponent<SweeperShooter>();
        stun = GetComponent<PlayerStun>();
        netPlayer = GetComponent<FishingNetPlayer>();
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
        if (!IsControlledHere() || Time.time < immuneUntil || (stun != null && stun.IsStunned))
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

        // 吹き飛ばしの受け口（FishingPlayerController）が止まっている体には効かせられない
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
                puller = other;
                pullerIndex = other.netPlayer != null && other.netPlayer.IsSpawned ? other.netPlayer.PlayerIndex : -1;
                return true;
            }
        }

        // オンラインで、ほかのPCの人が引っ張っている物（その人の巻き取りはこのPCでは動いていない）
        FishingNetSupply supply = item.GetComponent<FishingNetSupply>();
        if (supply != null && supply.IsSpawned && supply.IsClaimed)
        {
            pullerIndex = supply.HookedByPlayerIndex;
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
            return FishingTeams.TeamOf(pullerIndex) != netPlayer.TeamIndex;
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
        ElectricDebris electric = item.GetComponent<ElectricDebris>();
        float hitStun = electric != null ? electric.StunSeconds : stunSeconds;
        float hitSpeed = electric != null ? electric.KnockbackSpeed : knockbackSpeed;
        float hitLift = electric != null ? electric.KnockbackLift : knockbackLift;

        Vector3 from = item.Body != null ? item.Body.worldCenterOfMass : item.transform.position;
        Vector3 velocity = KnockbackVelocity(from, transform.position, transform.forward, hitSpeed, hitLift);
        MeteorStrikeEvent.ApplyPlayerImpact(controller, stun, velocity, hitStun);
        immuneUntil = Time.time + hitStun + rehitGrace;

        if (electric != null)
        {
            electric.PlayShock(transform, hitStun);
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

        FishingNetBomb netBomb = item.GetComponent<FishingNetBomb>();
        if (netBomb != null && netBomb.IsSpawned)
        {
            netBomb.RequestDetonate();
        }
        else
        {
            bomb.ExplodeNow();
        }
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
