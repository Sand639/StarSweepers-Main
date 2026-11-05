using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「デバッグ用のボット」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    // ------------------------------------------------------------
    // デバッグ用のボット（2026/10/21・大槻さんの依頼）
    // ------------------------------------------------------------
    //
    // 人数がそろわなくても複数人・チーム戦を確かめられるよう、ホストが**ボットのプレイヤー**を足せる。
    // ボットは「ホストのPCの、何人目か」が BotSeatStart 以上のプレイヤーとして出す（1台で複数人の仕組みをそのまま使う）。
    // だから席（チーム分け）・マップへの移動・出てくる場所は、人のプレイヤーと同じ流れで決まる。
    //
    // 体は人と同じ Player Prefab。**ホストのPCでだけ**、見えないコントローラー（VirtualPlayerInput）と
    // ボットの頭（SpaceJunkBot）を付けて動かす。ほかのPCには、ふつうの「ほかの人」として届く。

    /// <summary>
    /// ボットに使う「何人目か」の番号の始まり。**1台で複数人の人数（最大 <see cref="LocalMultiplayer.MaxLocalPlayers"/>）と
    /// 重ならない大きさ**にしてある。人ごとの番号の上の桁（8ビット）に入るので、255 までなら使える。
    /// </summary>
    public const int BotSeatStart = 16;

    /// <summary>出せるボットの数の上限（全体の定員 <see cref="SpaceJunkTeams.MaxPlayers"/> にも止められる）。</summary>
    public const int MaxBots = 7;

    /// <summary>その人ごとの番号が、デバッグ用のボットか。</summary>
    public static bool IsBotKey(ulong key)
    {
        return LocalMultiplayer.SeatOf(key) >= BotSeatStart;
    }

    /// <summary>いま出ているボットの数。</summary>
    public int BotCount
    {
        get
        {
            int count = 0;
            foreach (FishingNetPlayer player in FishingNetPlayer.All)
            {
                if (player != null && player.IsSpawned && player.LocalSeat >= BotSeatStart)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// **ボットの数を合わせる（ホストだけ・ロビーにいる間だけ）。** 多ければ後ろから消し、足りなければ出す。
    /// 定員（<see cref="SpaceJunkTeams.MaxPlayers"/>）に達したら、それ以上は出さない。
    /// </summary>
    public void ServerSetBotCount(int count)
    {
        if (!IsServer)
        {
            return;
        }

        if (state.Value != SpaceJunkMatchState.Lobby)
        {
            Debug.LogWarning("[JUNK] ボットの数は、ロビーにいる間しか変えられません。");
            return;
        }

        count = Mathf.Clamp(count, 0, MaxBots);
        ulong hostId = NetworkManager.LocalClientId;

        // 多すぎるぶんを消す（後ろから回す。消すと FishingNetPlayer.All から抜けるため）
        for (int i = FishingNetPlayer.All.Count - 1; i >= 0; i--)
        {
            FishingNetPlayer player = FishingNetPlayer.All[i];
            if (player == null || !player.IsSpawned || player.LocalSeat < BotSeatStart + count)
            {
                continue;
            }

            RemoveSlot(player.PlayerKey);
            Debug.Log($"[JUNK] ボット：{NameOf(player.PlayerKey)} を消しました。");
            player.NetworkObject.Despawn(true);
        }

        // ボットも人と同じ体で出す（2026/11/5 から新しい体。頭は体に合わせて付け替える。下の AttachBotBrain）
        GameObject prefab = SweeperOnlineSwitch.BotPrefabOr(NetworkManager.NetworkConfig.PlayerPrefab);
        if (prefab == null)
        {
            Debug.LogError("[JUNK] NetworkManager に Player Prefab が入っていないので、ボットを出せません。");
            return;
        }

        // 足りないぶんを出す
        for (int seat = BotSeatStart; seat < BotSeatStart + count; seat++)
        {
            ulong key = LocalMultiplayer.MakeKey(hostId, seat);
            if (FindPlayerByKey(key) != null)
            {
                continue;
            }

            if (slots.Count >= SpaceJunkTeams.MaxPlayers)
            {
                Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、これ以上ボットを出せません。");
                break;
            }

            GameObject spawned = Instantiate(prefab);
            FishingNetPlayer netPlayer = spawned.GetComponent<FishingNetPlayer>();
            NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
            if (netPlayer == null || networkObject == null)
            {
                Debug.LogError("[JUNK] Player Prefab に FishingNetPlayer か NetworkObject がありません。ボットを出せません。");
                Destroy(spawned);
                return;
            }

            // 1台で複数人の2人目以降と同じ出し方（持ち主はホストのPC。シーンを切り替えても消さない）
            netPlayer.ServerInitLocalSeat(seat);
            networkObject.SpawnWithOwnership(hostId, false);
            AddSlot(key);

            // ホストのPCでだけ、ボットの頭を付ける（見えないコントローラーも一緒に付く）。
            // 新しい体（SweeperShooter が付いている）なら新しい体のボット、今の体（フック）なら今までのボット（2026/11/5）
            if (spawned.GetComponent<SweeperShooter>() != null)
            {
                SweeperBot.AttachTo(spawned);
            }
            else
            {
                SpaceJunkBot.AttachTo(spawned);
            }

            Debug.Log($"[JUNK] ボット：{NameOf(key)} を出しました（チーム {TeamOf(key)}）。");
        }
    }
}
