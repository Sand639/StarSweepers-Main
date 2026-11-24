using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインの物資1つ分。** 誰が引っ掛けているかを整理し、投げを成立させる。
///
/// ## 誰が動かすか（ここが一番大事）
///
/// | 場面 | 動かす人 | 理由 |
/// | --- | --- | --- |
/// | 引き寄せ中 | **引っ掛けた本人** | ゲージと物資の動きがずれると、スキルチェックが成立しない |
/// | 投げたあとの飛び方 | **ホスト** | どこに落ちたか・どのゴールに入ったかを、全員で一致させる必要がある |
/// | 二重に引っ掛けられないか | **ホスト** | 早い者勝ちをホストが決める |
///
/// 引っ掛けた瞬間に**持ち主をその人へ移す**（`ChangeOwnership`）ので、
/// 引き寄せの動きは光線の巻き取り（<see cref="SweeperReel"/>）がそのまま動かせる。
/// 投げるときはホストへ知らせ、**ホストが力を加えて持ち主を取り戻す。**
///
/// 物理を回すのは「そのときの持ち主」だけ。それ以外のPCでは止めてある
/// （全員が物理を回すと、少しずつずれて位置がバラバラになるため）。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HookableObject))]
public class NetSupply : NetworkBehaviour
{
    /// <summary>いま引っ掛けている人の参加番号。**ホストが決める。** -1で誰も掛けていない。</summary>
    private readonly NetworkVariable<int> hookedByPlayerIndex = new NetworkVariable<int>(-1);

    /// <summary>最後に投げた人の参加番号。**共通ゴールの点を誰に入れるか**の判断に使う。</summary>
    private readonly NetworkVariable<int> lastThrownByPlayerIndex = new NetworkVariable<int>(-1);

    // 爆発前の「まだ届いていなかった鉤・投げ・解除」の指示を、爆発後に採用しない。

    /// <summary>
    /// **この物資が「爆風などで強制的に外された」回数。ホストが増やして全員に配る。**
    /// 引っ掛け・投げ・離すの指示には、頼んだ時点のこの値を添えてもらい、
    /// ホストは**いまの値と違う指示は古いものとして捨てる**（爆発の前に出されて遅れて届いた指示を、爆発後に採用しないため）。
    /// </summary>
    private readonly NetworkVariable<uint> explosionRevision = new NetworkVariable<uint>(0);

    /// <summary>自分が最後に引っ掛けを頼んだときの <see cref="explosionRevision"/>（このPCの控え）。投げ・離すの指示に添える。</summary>
    private uint localClaimRevision;

    /// <summary>いま誰かに引っ掛けられているか。</summary>
    public bool IsClaimed => hookedByPlayerIndex.Value >= 0;

    /// <summary>いま引っ掛けている人の参加番号（ホストの記録）。誰も掛けていなければ -1。</summary>
    public int HookedByPlayerIndex => hookedByPlayerIndex.Value;

    /// <summary>最後に投げた人の参加番号。誰も投げていなければ -1。</summary>
    public int LastThrownByPlayerIndex => lastThrownByPlayerIndex.Value;

    private HookableObject hookable;
    private Rigidbody body;

    private void Awake()
    {
        hookable = GetComponent<HookableObject>();
        body = GetComponent<Rigidbody>();
    }

    // 生まれたときと、持ち主が移るたびに「物理を回すのは誰か」を決め直す

    public override void OnNetworkSpawn()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnGainedOwnership()
    {
        ApplyPhysicsAuthority();
    }

    public override void OnLostOwnership()
    {
        ApplyPhysicsAuthority();
    }

    /// <summary>
    /// **物理を回すのは、いまの持ち主のPCだけ。** それ以外では止める。
    /// 止めた側は `NetworkTransform` が配ってくる位置に従うだけになる。
    /// </summary>
    private void ApplyPhysicsAuthority()
    {
        if (body == null)
        {
            return;
        }

        // 引き寄せ中は巻き取り（SweeperReel）が自分で止める（キネマティックにする）ので、
        // ここでは「持ち主かどうか」だけで決める
        body.isKinematic = !IsOwner;

        if (!IsOwner)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // ------------------------------------------------------------
    // 引っ掛ける（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 引っ掛けたいとホストへ頼む。<see cref="SweeperReel"/> から呼ばれる。
    /// **早い者勝ちの判定はホストが行う**ので、断られることがある
    /// （断られたら記録が自分にならないので、<see cref="SweeperReel"/> がそれを見てやめる）。
    /// </summary>
    public void RequestClaim(int playerIndex)
    {
        localClaimRevision = explosionRevision.Value;
        RequestClaimServerRpc(playerIndex, localClaimRevision);
    }

    /// <summary>
    /// ホスト側の受け口。まだ誰も掛けていなければ、**持ち主をその人へ移す。**
    ///
    /// `RequireOwnership = false` は「持ち主でなくても呼んでよい」の意味。
    /// 引っ掛ける前の持ち主はホストなので、これが無いと参加者から頼めない。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestClaimServerRpc(int playerIndex, uint claimRevision, ServerRpcParams rpcParams = default)
    {
        // **同じ人からの掛け直しは受け付ける**（2026/10/6）。
        // 遅れて「離した」が届く前に同じ人がもう一度引っ掛けた場合や、
        // ホストの記録が「その人が引っ掛け中」のまま残っていた場合に、本人までつかめなくなっていた
        bool takenByOther = IsClaimed && hookedByPlayerIndex.Value != playerIndex;

        if (claimRevision != explosionRevision.Value || takenByOther || hookable.IsVanished)
        {
            // すでに誰かが掛けている。記録は変えない（頼んだ人は、記録が自分でないのを見て手を離す）
            return;
        }

        ServerGrantClaim(playerIndex, rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// **引っ掛けをその人のものにする**（ホストだけが呼ぶ）。記録を更新し、持ち主もその人のPCへ移す。
    /// 頼んできたのがホスト自身なら、持ち主はもうホストなので移さない。
    /// </summary>
    private void ServerGrantClaim(int playerIndex, ulong senderClientId)
    {
        hookedByPlayerIndex.Value = playerIndex;

        if (senderClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(senderClientId);
        }
    }

    // ------------------------------------------------------------
    // 投げる・離す（本人 → ホスト）
    // ------------------------------------------------------------

    /// <summary>
    /// 投げをホストへ知らせる。<see cref="SweeperReel"/> から呼ばれる。
    /// **力を加えるのはホスト。** そうしないと、どこへ飛んだかが人によって変わる。
    /// </summary>
    public void RequestThrow(Vector3 direction, float force, float lift, int playerIndex,
        bool preserveVelocity = false, Vector3 inheritedVelocity = default,
        Vector3 inheritedAngularVelocity = default, bool hasReleasePosition = false, Vector3 releasePosition = default)
    {
        RequestThrowServerRpc(direction, force, lift, playerIndex, localClaimRevision,
            preserveVelocity, inheritedVelocity, inheritedAngularVelocity, hasReleasePosition, releasePosition);
    }

    /// <summary>
    /// ホスト側の受け口。持ち主を取り戻し、離した位置に置き直してから、ホストの物理で力を加える。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestThrowServerRpc(Vector3 direction, float force, float lift,
        int playerIndex, uint claimRevision, bool preserveVelocity, Vector3 inheritedVelocity,
        Vector3 inheritedAngularVelocity, bool hasReleasePosition, Vector3 releasePosition,
        ServerRpcParams rpcParams = default)
    {
        // 掛けている本人からの指示だけを受け付ける（爆発をまたいだ古い指示も捨てる）
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();

        // **手を離した位置から飛ばす**（2026/10/13）。ホストに届いている位置は少し遅れているので、
        // そのまま力を加えると、離した場所より手前から飛び出したように見える（引っ張った後の飛び方がおかしい、の一因）
        if (hasReleasePosition && body != null)
        {
            body.position = releasePosition;
            transform.position = releasePosition;
        }

        hookedByPlayerIndex.Value = -1;
        lastThrownByPlayerIndex.Value = playerIndex;

        // **ここからはホストが物資を動かすので、ホストでも「投げた本人の体に当たらない」ようにする**
        // （本人のPCだけでやっても、ホスト側で頭にぶつかって跳ねてしまう。2026/9/30）
        ThrowPassThrough.Apply(gameObject, FindThrowerCollider(playerIndex));

        if (body != null && !body.isKinematic)
        {
            if (!preserveVelocity)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            else
            {
                // タイプBは引きずっていた本人の速度をホストへ渡し、所有権移行後も慣性を保つ。
                body.linearVelocity = inheritedVelocity;
                body.angularVelocity = inheritedAngularVelocity;
            }
            body.AddForce(direction * force + Vector3.up * lift, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// 引っ掛けをやめる（ミスしたとき・爆発したとき）ことをホストへ知らせる。
    /// <paramref name="pullForce"/> だけプレイヤー側へ引き寄せて終わる。
    /// </summary>
    public void RequestRelease(Vector3 direction, float pullForce, int playerIndex)
    {
        RequestReleaseServerRpc(direction, pullForce, playerIndex, localClaimRevision);
    }

    /// <summary>ホスト側の受け口。持ち主を取り戻して記録を消し、頼まれた力があればプレイヤーのほうへ引く。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseServerRpc(Vector3 direction, float pullForce,
        int playerIndex, uint claimRevision, ServerRpcParams rpcParams = default)
    {
        // 掛けている本人からの指示だけを受け付ける（爆発をまたいだ古い指示も捨てる）
        if (claimRevision != explosionRevision.Value || hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        TakeBackOwnership();
        hookedByPlayerIndex.Value = -1;

        if (body != null && !body.isKinematic && pullForce > 0f)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction * pullForce, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// 爆風で脱鉤して吹き飛ばす。各PCの爆弾が呼んでもホストだけが処理する。
    /// </summary>
    public void ServerApplyExplosion(Vector3 velocity)
    {
        if (!IsSpawned || !IsServer || hookable.IsVanished || body == null)
        {
            return;
        }

        // 「強制的に外した回数」を進める。これより前の番号を添えて届く指示は、古いものとして捨てられる
        explosionRevision.Value += 1;
        ReleaseForExplosionClientRpc(explosionRevision.Value);
        // ホストと専用サーバーはここで手元の状態を戻す。
        hookable.SetHooked(false);
        hookedByPlayerIndex.Value = -1;
        TakeBackOwnership();
        body.AddForce(velocity, ForceMode.VelocityChange);
    }

    /// <summary>参加者の画面で、つかんでいた人の引き寄せを止める（ホストは <see cref="ServerApplyExplosion"/> の中で直接止めている）。</summary>
    [ClientRpc]
    private void ReleaseForExplosionClientRpc(uint releasedRevision)
    {
        // 爆発後の新しい鉤まで、遅れて届いた脱鉤の合図で外さない。
        if (!IsServer && localClaimRevision < releasedRevision)
        {
            hookable.SetHooked(false);
        }
    }

    /// <summary>参加番号 <paramref name="playerIndex"/> の人の体（CharacterController）。見つからなければ null。</summary>
    private static Collider FindThrowerCollider(int playerIndex)
    {
        foreach (SweeperNetPlayer player in SweeperNetPlayer.All)
        {
            if (player != null && player.PlayerIndex == playerIndex)
            {
                return player.GetComponent<CharacterController>();
            }
        }

        return null;
    }

    /// <summary>ホストが持ち主を取り戻し、物理を自分で回せる状態にする。</summary>
    private void TakeBackOwnership()
    {
        if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId)
        {
            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        }

        ApplyPhysicsAuthority();
    }

    // ------------------------------------------------------------
    // 答え合わせ（遅延で知らせを取りこぼしても、ずれたまま残らないように。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// **答え合わせで「ホストの記録では自分が引っ掛け中なのに、手に持っていない」と分かったとき**に、本人のPCから呼ぶ。
    /// 普通の「離した」（<see cref="RequestRelease"/>）と違い、爆発の回数（revision）が食い違っていても受け付ける
    /// （食い違っていたせいで「離した」が捨てられ、ずれたまま残ったときの立て直し用）。
    /// </summary>
    public void RequestReconcileRelease(int playerIndex)
    {
        RequestReconcileReleaseServerRpc(playerIndex);
    }

    /// <summary>ホスト側の受け口。頼んできた人が本当にその参加番号の人なら、記録を消して持ち主を取り戻す。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestReconcileReleaseServerRpc(int playerIndex, ServerRpcParams rpcParams = default)
    {
        if (hookedByPlayerIndex.Value != playerIndex)
        {
            return;
        }

        // **頼んできた人が、本当にその参加番号の人か**を確かめる（ほかの人の引っ掛けを外させない）
        // 1台で複数人のときは、同じPCに何人もいるので「参加番号の人が、頼んできたPCの人か」で確かめる
        SweeperNetPlayer sender = FindPlayerByIndex(playerIndex);
        if (sender == null || sender.OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            return;
        }

        Debug.LogWarning($"[NET][答え合わせ] {name}：参加番号 {playerIndex} の人は持っていないのに、" +
                         "ホストの記録では引っ掛け中のままでした。外しました。");

        hookedByPlayerIndex.Value = -1;
        TakeBackOwnership();
    }

    /// <summary>ホストが答え合わせをする間隔（秒）。</summary>
    private const float ServerCheckInterval = 1f;

    /// <summary>前回の答え合わせからの経過秒数。</summary>
    private float serverCheckTimer;

    /// <summary>「持ち主と引っ掛けた人が食い違う」が何回続いたか。</summary>
    private int ownerMismatchCount;

    /// <summary>
    /// ホストだけが動かす。誰かが引っ掛けている間、<see cref="ServerCheckInterval"/> 秒ごとに答え合わせをする。
    /// ポーズ中も数えるよう、時間は unscaledDeltaTime で測る。
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer || !IsClaimed || despawnScheduled)
        {
            ownerMismatchCount = 0;
            return;
        }

        serverCheckTimer += Time.unscaledDeltaTime;
        if (serverCheckTimer < ServerCheckInterval)
        {
            return;
        }
        serverCheckTimer = 0f;

        ServerCheckClaim();
    }

    /// <summary>
    /// **ホストの答え合わせ。** 引っ掛け中の記録がおかしくないかを見る。
    /// ・引っ掛けた人がもういない → 外す（誰もつかめないまま残らないように）
    /// ・物の持ち主（物理を動かす人）と、引っ掛けた人が続けて食い違う → 引っ掛けた人を持ち主にする
    /// </summary>
    private void ServerCheckClaim()
    {
        SweeperNetPlayer claimer = FindPlayerByIndex(hookedByPlayerIndex.Value);

        if (claimer == null)
        {
            Debug.LogWarning($"[NET][答え合わせ] {name}：引っ掛けていた参加番号 {hookedByPlayerIndex.Value} の人が" +
                             "もういないので、引っ掛けを外しました。");
            hookedByPlayerIndex.Value = -1;
            TakeBackOwnership();
            ownerMismatchCount = 0;
            return;
        }

        if (NetworkObject.OwnerClientId == claimer.OwnerClientId)
        {
            ownerMismatchCount = 0;
            return;
        }

        // 引っ掛けた直後は、持ち主が移る途中のことがあるので、続けて食い違ったときだけ直す
        ownerMismatchCount++;
        if (ownerMismatchCount < 2)
        {
            return;
        }

        Debug.LogWarning($"[NET][答え合わせ] {name}：引っ掛けた人（参加番号 {claimer.PlayerIndex}）と" +
                         "物の持ち主が食い違っていたので、引っ掛けた人を持ち主にしました。");
        NetworkObject.ChangeOwnership(claimer.OwnerClientId);
        ownerMismatchCount = 0;
    }

    /// <summary>参加番号 <paramref name="playerIndex"/> の人。いなければ null。</summary>
    private static SweeperNetPlayer FindPlayerByIndex(int playerIndex)
    {
        foreach (SweeperNetPlayer player in SweeperNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.PlayerIndex == playerIndex)
            {
                return player;
            }
        }
        return null;
    }

    // ------------------------------------------------------------
    // 消す（ホスト → 全員）
    // ------------------------------------------------------------

    /// <summary>
    /// 消えてから、全員の画面から実際に削除するまでの秒数。
    ///
    /// **すぐ削除しない理由：** 爆発物は各PCが自分で導火線を数えて爆発する。
    /// ホストの爆発と同時に削除すると、少し遅れて数えている参加者の画面では
    /// **爆発する前に消えてしまい、スタンや爆発の見た目が出ない**ため。
    /// </summary>
    private const float DespawnDelaySeconds = 1f;

    /// <summary>もう削除の予約をしたか（二重に予約しないため）。</summary>
    private bool despawnScheduled;

    /// <summary>
    /// **ホストだけが呼ぶ。** 全員の画面で見た目を消し、少し待ってから削除する。
    /// <see cref="HookableObject.Vanish"/> から呼ばれる。
    /// </summary>
    public void ServerDespawnSoon()
    {
        if (!IsServer || despawnScheduled)
        {
            return;
        }

        despawnScheduled = true;
        hookedByPlayerIndex.Value = -1;
        HideClientRpc();
        StartCoroutine(DespawnLater());
    }

    /// <summary><see cref="DespawnDelaySeconds"/> 秒待ってから、全員の画面から削除する。待っている間に消えていたら何もしない。</summary>
    private System.Collections.IEnumerator DespawnLater()
    {
        yield return new WaitForSeconds(DespawnDelaySeconds);

        if (IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    /// <summary>参加者の画面でも見た目を消す（ゴールに入ったときなど、ホストだけが判定した場合）。</summary>
    [ClientRpc]
    private void HideClientRpc()
    {
        if (!IsServer)
        {
            hookable.Vanish();
        }
    }

    /// <summary>
    /// **ホストだけが呼ぶ。** ゴールの渦に吸い込まれ始めたことを、参加者のPCへ知らせる（小さくなる動きを同じ時刻で出すため）。
    /// 位置と回転はホストが動かし、NetworkTransform で配られる。
    /// </summary>
    public void ServerBeginAbsorb(float startTime, float seconds, bool pillarMotion)
    {
        if (!IsServer)
        {
            return;
        }

        hookedByPlayerIndex.Value = -1;
        BeginAbsorbClientRpc(startTime, seconds, pillarMotion);
    }

    /// <summary>pillarMotion：光の柱の動き（上へ運んで光って消える）か、渦の吸い込みか（ホストが決めた物）。</summary>
    [ClientRpc]
    private void BeginAbsorbClientRpc(float startTime, float seconds, bool pillarMotion)
    {
        if (!IsServer)
        {
            GoalAbsorb.Begin(hookable, default, startTime, seconds, pillarMotion, null);
        }
    }

    /// <summary>ホストが点を入れたあとに、投げた人の記録を消す。</summary>
    public void ClearLastThrower()
    {
        if (IsServer)
        {
            lastThrownByPlayerIndex.Value = -1;
        }
    }
}
