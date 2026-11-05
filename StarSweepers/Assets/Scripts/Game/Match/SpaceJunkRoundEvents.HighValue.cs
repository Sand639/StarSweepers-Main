using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「ラウンドイベント：期間限定高価値デブリ」（SpaceJunkRoundEvents.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // 期間限定高価値デブリの状態
    // ------------------------------------------------------------

    /// <summary>高価値に選ばれたデブリの種類（番号）。-1 ならまだ選ばれていない。</summary>
    private readonly NetworkVariable<int> highValueKind = new NetworkVariable<int>(-1);

    /// <summary>高価値の種類を1個入れたときに、1点とは別に入る得点（ホストの設定を全員へ配る）。</summary>
    private readonly NetworkVariable<int> highValueBonus = new NetworkVariable<int>(2);

    /// <summary>高価値に選ばれたデブリの種類。まだ選ばれていなければ false。</summary>
    public bool TryGetHighValueKind(out SpaceJunkMaterialKind kind)
    {
        kind = SpaceJunkMaterials.FromIndex(Mathf.Max(0, highValueKind.Value));
        return highValueKind.Value >= 0;
    }

    /// <summary>高価値の種類を1個入れたときに、1点とは別に入る得点。</summary>
    public int HighValueBonus => highValueBonus.Value;

    // ------------------------------------------------------------
    // 期間限定高価値デブリ
    // ------------------------------------------------------------

    /// <summary>
    /// **高価値に選ばれた種類なら、1点とは別に +2点**（初期値）を返す。
    /// </summary>
    private int ServerHighValueCollect(int team, SpaceJunkMaterialKind kind)
    {
        if (highValueKind.Value != (int)kind)
        {
            return 0;
        }

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が高価値の {SpaceJunkMaterials.Name(kind)} を入れました（+{highValueBonus.Value} 点）。");
        return highValueBonus.Value;
    }

    /// <summary>
    /// **マップの外へ落ちて消えた重いデブリを、一覧から外す**。
    /// 外さないと「あと ○個」が減らないまま残る。<paramref name="weights"/> は同じ並びの一覧（重さ）。無ければ null。
    /// </summary>
    private void ServerPruneLost(NetworkList<ulong> ids, NetworkList<float> weights)
    {
        if (ids.Count == 0 || NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return;
        }

        // 後ろから見る（途中で外しても、まだ見ていない番号がずれないように）
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            bool alive = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(ids[i], out NetworkObject networkObject)
                         && networkObject != null
                         && networkObject.TryGetComponent(out HookableObject item)
                         && !item.IsVanished;

            if (!alive)
            {
                ids.RemoveAt(i);
                if (weights != null && i < weights.Count)
                {
                    weights.RemoveAt(i);
                }
            }
        }
    }
}
