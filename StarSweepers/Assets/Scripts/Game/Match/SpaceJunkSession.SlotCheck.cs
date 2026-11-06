using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「席の答え合わせ（ホストだけ）」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    // ------------------------------------------------------------
    // 席の答え合わせ（ホストだけ。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>席の答え合わせの間隔（秒）。</summary>
    private const float SlotCheckInterval = 1f;

    /// <summary>次の答え合わせまでの数え上げ（秒）。</summary>
    private float slotCheckTimer;

    /// <summary>「席とつながっている人が食い違う」が何回続いたか。</summary>
    private int slotMismatchCount;

    /// <summary>
    /// 食い違いが何回続いたら席を直すか。
    /// 入ってきた直後は席ができるまで一瞬食い違うので、1回だけでは直さない。
    /// </summary>
    private const int SlotMismatchRepairCount = 2;

    /// <summary>
    /// **席の一覧と、実際につながっている人の一覧を比べて合わせる。**
    /// 入ってきた／抜けたの知らせ（OnClientConnected / OnClientDisconnected）を取りこぼしても、
    /// 「抜けた人の席が残る（そのチームが誰もいないのに数えられる）」「席の無い人がいる（チームの色が付かない）」
    /// のまま残らないようにする。入ってきた直後は一瞬食い違うので、続けて食い違ったときだけ直す。
    /// </summary>
    private void ServerCheckSlots()
    {
        IReadOnlyList<ulong> connected = NetworkManager.ConnectedClientsIds;

        if (!HasSlotMismatch(connected))
        {
            slotMismatchCount = 0;
            return;
        }

        slotMismatchCount++;
        if (slotMismatchCount < SlotMismatchRepairCount)
        {
            return;
        }
        slotMismatchCount = 0;

        ServerRepairSlots(connected);
    }

    /// <summary>
    /// 席の一覧と、つながっている人が食い違っているか。
    /// 「抜けた人の席が残っている」「席の無い人がいる」のどちらかがあれば true。
    /// </summary>
    private bool HasSlotMismatch(IReadOnlyList<ulong> connected)
    {
        // 抜けた人の席が残っていないか
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (!SlotStillValid(connected, slot.ClientId))
            {
                return true;
            }
        }

        // つながっている人全員に席があるか
        foreach (ulong clientId in connected)
        {
            if (TeamSlotIndex(clientId) < 0)
            {
                return true;
            }
        }

        // 1台で複数人のときの2人目以降（ホストが追加で出したプレイヤー）にも、席があるか
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (IsExtraLocalPlayer(player) && TeamSlotIndex(player.PlayerKey) < 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>食い違いを直す。抜けた人の席を消し、席の無い人（2人目以降も含む）の席を作る。</summary>
    private void ServerRepairSlots(IReadOnlyList<ulong> connected)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (!SlotStillValid(connected, slots[i].ClientId))
            {
                Debug.LogWarning($"[NET][答え合わせ] もうつながっていない人（{slots[i].ClientId}）の席が残っていたので消しました。");
                slots.RemoveAt(i);
            }
        }

        foreach (ulong clientId in connected)
        {
            if (TeamSlotIndex(clientId) < 0)
            {
                Debug.LogWarning($"[NET][答え合わせ] 席の無い人（{clientId}）がいたので、席を作りました。");
                AddSlot(clientId);
            }
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (IsExtraLocalPlayer(player) && TeamSlotIndex(player.PlayerKey) < 0)
            {
                Debug.LogWarning($"[NET][答え合わせ] 席の無い人（{player.PlayerKey}）がいたので、席を作りました。");
                AddSlot(player.PlayerKey);
            }
        }
    }

    /// <summary>接続番号の一覧に、その番号が入っているか。</summary>
    private static bool ContainsId(IReadOnlyList<ulong> ids, ulong id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// その席がまだ有効か。1人目（接続番号そのまま）は、そのPCがつながっていれば有効。
    /// 1台で複数人の2人目以降は、そのプレイヤーがまだいれば有効。
    /// </summary>
    private static bool SlotStillValid(IReadOnlyList<ulong> connected, ulong key)
    {
        if (!ContainsId(connected, LocalMultiplayer.ClientOf(key)))
        {
            return false;
        }

        return LocalMultiplayer.SeatOf(key) == 0 || FindPlayerByKey(key) != null;
    }

    /// <summary>1台で複数人のときに、ホストが追加で出した2人目以降のプレイヤーか。</summary>
    private static bool IsExtraLocalPlayer(FishingNetPlayer player)
    {
        return player != null && player.IsSpawned && player.LocalSeat > 0;
    }

    /// <summary>人ごとの番号から、そのプレイヤーの体を探す。いなければ null。</summary>
    private static FishingNetPlayer FindPlayerByKey(ulong key)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.PlayerKey == key)
            {
                return player;
            }
        }
        return null;
    }
}
