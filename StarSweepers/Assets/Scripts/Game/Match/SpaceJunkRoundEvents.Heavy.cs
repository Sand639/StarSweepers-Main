using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「ラウンドイベント：特殊デブリ（重いデブリ）と、その見た目」（SpaceJunkRoundEvents.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // 特殊デブリ（重いデブリ）の状態
    // ------------------------------------------------------------

    /// <summary>重いデブリの重さの下限。設定で 0 以下にしても、0 で割るような計算にならないようにする。</summary>
    private const float MinHeavyWeight = 0.1f;

    /// <summary>重いデブリの、通信の番号（NetworkObjectId）の一覧。</summary>
    private readonly NetworkList<ulong> heavyIds = new NetworkList<ulong>();

    /// <summary>重いデブリの重さ。並びは <see cref="heavyIds"/> と同じ。</summary>
    private readonly NetworkList<float> heavyWeights = new NetworkList<float>();

    /// <summary>重いデブリを1個入れたときの得点（ホストの設定を全員へ配る。画面の表示用）。</summary>
    private readonly NetworkVariable<int> heavyPoints = new NetworkVariable<int>(20);

    /// <summary>
    /// **ステージに置いた重いデブリ**（イベントとは別。置き場所の部品 <see cref="SpaceJunkBombPoints"/> が出す。2026/10/8）の番号。
    /// 重さ・得点・見た目はイベントの重いデブリと同じ設定を使う。イベントが終わっても重いまま。
    /// </summary>
    private readonly NetworkList<ulong> stageHeavyIds = new NetworkList<ulong>();

    /// <summary>ステージに置いた重いデブリの重さ。並びは <see cref="stageHeavyIds"/> と同じ。</summary>
    private readonly NetworkList<float> stageHeavyWeights = new NetworkList<float>();

    /// <summary>このPCで、いま重い物にしているデブリ。</summary>
    private readonly HashSet<ulong> appliedHeavyIds = new HashSet<ulong>();

    /// <summary>まだ残っている重いデブリの数。</summary>
    public int HeavyRemaining => heavyIds.Count;

    /// <summary>
    /// 重さが1増えるごとに足す得点（ホストの設定を全員へ配る。画面の表示用）。**重さボーナスが OFF なら 0。**
    /// </summary>
    private readonly NetworkVariable<int> heavyBonusPerWeight = new NetworkVariable<int>(0);

    /// <summary>重いデブリを1個入れたときの得点（重さ1のとき）。</summary>
    public int HeavyPoints => heavyPoints.Value;

    /// <summary>重さが1増えるごとに足す得点。重さボーナスが OFF なら 0。</summary>
    public int HeavyBonusPerWeight => heavyBonusPerWeight.Value;

    // ------------------------------------------------------------
    // 特殊デブリ（重いデブリ）
    // ------------------------------------------------------------

    /// <summary>
    /// 重いデブリを出す。**設定の数だけ**、スポナーに新しく出させる。
    /// 重さは1個ずつ、設定の X〜Y の間からランダムに決める（<see cref="SpaceJunkEventSettings.PickHeavyWeight"/>）。
    /// 大きさ・重さは、一覧を見て各PCが付ける（<see cref="UpdateHeavyMarks"/>）。
    /// </summary>
    private void ServerSpawnHeavyDebris(SpaceJunkEventSettings settings)
    {
        SpaceJunkSpawner spawner = FindFirstObjectByType<SpaceJunkSpawner>();

        if (spawner == null)
        {
            Debug.LogWarning("[JUNK] 重いデブリを出せませんでした（スポナーが無い）。");
            return;
        }

        // 設定が 0 以下でも、最低1個は出す
        for (int n = 0; n < Mathf.Max(1, settings.heavyInitialCount); n++)
        {
            float weight = settings.PickHeavyWeight();

            GameObject spawned = spawner.ServerSpawnExtra();
            NetworkObject networkObject = spawned != null ? spawned.GetComponent<NetworkObject>() : null;

            if (networkObject == null || !networkObject.IsSpawned)
            {
                Debug.LogWarning($"[JUNK] 重さ {weight} の重いデブリを置く場所が見つからず、出せませんでした。");
                continue;
            }

            heavyIds.Add(networkObject.NetworkObjectId);
            heavyWeights.Add(Mathf.Max(MinHeavyWeight, weight));
        }
    }

    /// <summary>
    /// **ステージに置いた重いデブリとして登録する**（ホストだけ。置き場所の部品 <see cref="SpaceJunkBombPoints"/> から呼ぶ）。
    /// 重さはイベントの設定の X〜Y からランダム。見た目は一覧を見て各PCが付ける（<see cref="UpdateHeavyMarks"/>）。
    /// </summary>
    public void ServerAddStageHeavy(NetworkObject item)
    {
        if (!IsServer || !IsSpawned || item == null || !item.IsSpawned)
        {
            return;
        }

        float weight = Mathf.Max(MinHeavyWeight, EventSettings.PickHeavyWeight());
        stageHeavyIds.Add(item.NetworkObjectId);
        stageHeavyWeights.Add(weight);

        Debug.Log($"[JUNK] ステージの重いデブリ（重さ {weight:0.#}）を出しました。");
    }

    /// <summary>
    /// ステージに置いた重いデブリなら、一覧から外して true を返す。<paramref name="bonus"/> は1点との差のぶん
    /// （合計が重いデブリの得点になる。イベントの重いデブリと同じ点）。
    /// </summary>
    private bool ServerTryStageHeavyCollect(int team, NetworkObject item, out int bonus)
    {
        bonus = 0;
        if (item == null)
        {
            return false;
        }

        int index = stageHeavyIds.IndexOf(item.NetworkObjectId);
        if (index < 0)
        {
            return false;
        }

        float weight = RemoveHeavyAt(stageHeavyIds, stageHeavyWeights, index);

        int points = EventSettings.HeavyPointsFor(weight);
        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} がステージの重いデブリ（重さ {weight:0.#}）を入れました（{points} 点）。");

        bonus = Mathf.Max(0, points - PointPerItem);
        return true;
    }

    /// <summary>重いデブリなら、1点との差のぶんを返す（合計が特殊デブリの得点になる）。</summary>
    private int ServerHeavyCollect(int team, NetworkObject item)
    {
        if (item == null)
        {
            return 0;
        }

        int index = heavyIds.IndexOf(item.NetworkObjectId);
        if (index < 0)
        {
            return 0;
        }

        float weight = RemoveHeavyAt(heavyIds, heavyWeights, index);

        // 重さボーナスが ON なら、重いほど高い（設定は HeavyPointsFor の1か所）
        int points = EventSettings.HeavyPointsFor(weight);

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が重さ {weight:0.#} の特殊デブリを入れました（{points} 点）。" +
                  $"残り {heavyIds.Count} 個。");

        return Mathf.Max(0, points - PointPerItem);
    }

    /// <summary>
    /// 一覧の <paramref name="index"/> 番目の重いデブリを外し、その重さを返す
    /// （重さの一覧が短くて見つからなければ 1 とみなす）。イベントの重いデブリとステージの重いデブリで共通。
    /// </summary>
    private static float RemoveHeavyAt(NetworkList<ulong> ids, NetworkList<float> weights, int index)
    {
        float weight = index < weights.Count ? weights[index] : 1f;
        ids.RemoveAt(index);
        if (index < weights.Count)
        {
            weights.RemoveAt(index);
        }
        return weight;
    }

    // ------------------------------------------------------------
    // 見た目（**全員のPCで動かす**）
    // ------------------------------------------------------------

    /// <summary>
    /// ホストが配っている「重いデブリの一覧」に合わせて、このPCで重い物にしたり戻したりする
    /// （大きさ・質量・色。投げられなくなるのは <see cref="HeavyHookable"/> を見て操作の側が判断する）。
    /// </summary>
    private void UpdateHeavyMarks()
    {
        if (!IsSpawned || NetworkManager == null || NetworkManager.SpawnManager == null)
        {
            return;
        }

        var spawned = NetworkManager.SpawnManager.SpawnedObjects;
        SpaceJunkEventSettings settings = EventSettings;

        // イベントの重いデブリと、ステージに置いた重いデブリ（見た目は同じ）
        ApplyHeavyMarks(heavyIds, heavyWeights, spawned, settings);
        ApplyHeavyMarks(stageHeavyIds, stageHeavyWeights, spawned, settings);

        RemoveStaleHeavyMarks(spawned);
    }

    /// <summary>
    /// このPCで重い物にしていたのに、ホストの一覧から外れたデブリ（ゴールに入った・イベントが時間切れで戻した）を、
    /// ふつうのデブリの見た目に戻す。
    /// </summary>
    private void RemoveStaleHeavyMarks(Dictionary<ulong, NetworkObject> spawned)
    {
        if (appliedHeavyIds.Count == 0)
        {
            return;
        }

        // foreach の途中で HashSet から消せないので、外すものをいったん集める（外すものが無い回は作らない）
        List<ulong> removed = null;

        foreach (ulong id in appliedHeavyIds)
        {
            if (heavyIds.Contains(id) || stageHeavyIds.Contains(id))
            {
                continue;
            }

            // もう消えている（場外に落ちた など）なら、見た目を戻す相手がいないので覚え書きだけ外す
            if (spawned.TryGetValue(id, out NetworkObject networkObject) && networkObject != null)
            {
                if (networkObject.TryGetComponent(out HeavyHookable heavy))
                {
                    heavy.Remove();
                }

                if (networkObject.TryGetComponent(out SpaceJunkHighValueMark mark))
                {
                    mark.Hide();
                }
            }

            (removed ??= new List<ulong>()).Add(id);
        }

        if (removed != null)
        {
            foreach (ulong id in removed)
            {
                appliedHeavyIds.Remove(id);
            }
        }
    }

    /// <summary>一覧のうち、このPCでまだ重い物にしていないデブリを、重い物にする（大きさ・質量・色）。</summary>
    private void ApplyHeavyMarks(NetworkList<ulong> ids, NetworkList<float> weights,
        Dictionary<ulong, NetworkObject> spawned, SpaceJunkEventSettings settings)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            ulong id = ids[i];

            if (appliedHeavyIds.Contains(id) || !spawned.TryGetValue(id, out NetworkObject networkObject) || networkObject == null)
            {
                continue;
            }

            float weight = i < weights.Count ? weights[i] : 1f;

            if (!networkObject.TryGetComponent(out HeavyHookable heavy))
            {
                heavy = networkObject.gameObject.AddComponent<HeavyHookable>();
            }
            heavy.Apply(weight, settings.HeavyScale(weight));

            if (!networkObject.TryGetComponent(out SpaceJunkHighValueMark mark))
            {
                mark = networkObject.gameObject.AddComponent<SpaceJunkHighValueMark>();
            }
            mark.Show(settings.heavyColor);

            appliedHeavyIds.Add(id);
        }
    }
}
