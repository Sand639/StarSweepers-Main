using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「1台のPCで複数人」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    // ------------------------------------------------------------
    // 1台のPCで複数人（2026/10/7）
    // ------------------------------------------------------------
    //
    // ふつうはつないだPC1台につき1人（NetworkManager の Player Prefab）が出る。
    // 1台で複数人のときは、2人目以降を**ホストが同じプレハブから出し、そのPCを持ち主にする**。
    // 持ち主が同じなので、そのPCで動かせる。誰がどの席（P1・P2…）の機器で動くかは LocalSeat で決まる。
    // チーム分けの席は、人ごとの番号（FishingNetPlayer.PlayerKey）で作る。

    /// <summary>
    /// **このPCで遊ぶ人数をホストへ伝える。** 足りなければ出してもらい、多ければ消してもらう。
    /// ロビーにいる間だけ効く。ホストのPCからも参加者のPCからも呼んでよい。
    /// </summary>
    public void RequestLocalPlayerCount(int count)
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsServer)
        {
            ServerApplyLocalPlayerCount(NetworkManager.LocalClientId, count);
        }
        else
        {
            RequestLocalPlayerCountServerRpc(count);
        }
    }

    /// <summary>参加者のPCから「このPCで遊ぶ人数」が届いたとき（ホストで動く）。</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestLocalPlayerCountServerRpc(int count, ServerRpcParams rpcParams = default)
    {
        ServerApplyLocalPlayerCount(rpcParams.Receive.SenderClientId, count);
    }

    /// <summary>
    /// そのPCで遊ぶ人数を合わせる（ホストだけ）。多ければ2人目以降を消し、足りなければ出す。
    /// 1人目（接続番号そのもの）はここでは触らない。
    /// </summary>
    private void ServerApplyLocalPlayerCount(ulong clientId, int count)
    {
        if (state.Value != SpaceJunkMatchState.Lobby)
        {
            Debug.LogWarning($"[JUNK] 1台で遊ぶ人数は、ロビーにいる間しか変えられません（接続番号 {clientId}）。");
            return;
        }

        count = Mathf.Clamp(count, 1, LocalMultiplayer.MaxLocalPlayers);

        ServerRemoveExtraLocalPlayers(clientId, count);
        ServerSpawnMissingLocalPlayers(clientId, count);
    }

    /// <summary>そのPCの、<paramref name="count"/> 人目より後ろのプレイヤーを消す（席も消す）。</summary>
    private void ServerRemoveExtraLocalPlayers(ulong clientId, int count)
    {
        // 後ろから回す（消すと FishingNetPlayer.All から抜けるため）
        for (int i = FishingNetPlayer.All.Count - 1; i >= 0; i--)
        {
            FishingNetPlayer player = FishingNetPlayer.All[i];

            // デバッグ用のボット（ホストのPCが持ち主）は、人数の「－」では消さない（ボットの数は別に決める）
            if (!IsExtraLocalPlayer(player) || player.OwnerClientId != clientId || player.LocalSeat < count ||
                player.LocalSeat >= BotSeatStart)
            {
                continue;
            }

            RemoveSlot(player.PlayerKey);
            Debug.Log($"[JUNK] 1台で複数人：接続番号 {clientId} の {player.LocalSeat + 1} 人目を消しました。");
            player.NetworkObject.Despawn(true);
        }
    }

    /// <summary>
    /// そのPCの2人目〜<paramref name="count"/> 人目のうち、まだ出ていない人を Player Prefab から出す（席も作る）。
    /// 定員に達したらそこでやめる。
    /// </summary>
    private void ServerSpawnMissingLocalPlayers(ulong clientId, int count)
    {
        GameObject prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("[JUNK] NetworkManager に Player Prefab が入っていないので、2人目以降を出せません。");
            return;
        }

        for (int seat = 1; seat < count; seat++)
        {
            ulong key = LocalMultiplayer.MakeKey(clientId, seat);
            if (FindPlayerByKey(key) != null)
            {
                continue;
            }

            if (slots.Count >= SpaceJunkTeams.MaxPlayers)
            {
                Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、接続番号 {clientId} の {seat + 1} 人目は出しませんでした。");
                break;
            }

            GameObject spawned = Instantiate(prefab);
            FishingNetPlayer netPlayer = spawned.GetComponent<FishingNetPlayer>();
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (netPlayer == null || networkObject == null)
            {
                Debug.LogError("[JUNK] Player Prefab に FishingNetPlayer か NetworkObject がありません。2人目以降を出せません。");
                Destroy(spawned);
                return;
            }

            // 出す前に「何人目か」を決める（出したときに全員へ一緒に届く）。
            // シーンを切り替えても消えないように出す（1人目と同じ。マップへそのまま付いていく）
            netPlayer.ServerInitLocalSeat(seat);
            networkObject.SpawnWithOwnership(clientId, false);
            AddSlot(key);

            Debug.Log($"[JUNK] 1台で複数人：接続番号 {clientId} の {seat + 1} 人目を出しました。");
        }
    }
}
