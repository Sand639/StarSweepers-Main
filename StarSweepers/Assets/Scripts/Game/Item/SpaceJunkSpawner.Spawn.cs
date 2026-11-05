using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSpawner の「出す物を選んで出す」（SpaceJunkSpawner.cs から分けた部分）。</summary>
public partial class SpaceJunkSpawner
{
    /// <summary>ふだんの1回ぶん（上限あり）。出せなくても何もしない。</summary>
    private void TrySpawnOne()
    {
        SpawnOne(ignoreLimit: false);
    }

    /// <summary>
    /// 1つ出す。流れ：何を出すか選ぶ → 上限を見る → 空いた場所を探す → 置く（通信中なら全員の画面へ）。
    /// 出せなかった回は null。<paramref name="ignoreLimit"/> が true なら、数の上限を見ず、爆弾も選ばない（イベント用）。
    /// </summary>
    private GameObject SpawnOne(bool ignoreLimit)
    {
        // 爆弾は、素材の数とは別に数えて上限までだけ出す（イベント用の追加では出さない）
        bool bombsAllowed = !ignoreLimit && CanSpawnBomb();
        GameObject prefab = ChoosePrefab(bombsAllowed);

        if (prefab == null)
        {
            return null;
        }

        // 素材を選んだが、マップの素材がもう上限なら、この回は出さない
        // （上限のときに爆弾ばかりにならないよう、選び直さない）
        if (!IsBomb(prefab) && !ignoreLimit && CountMaterials() >= maxObjects)
        {
            return null;
        }

        if (!TryFindPlace(ZonesFor(prefab), out Vector3 position))
        {
            return null;
        }

        // 向きは水平にランダム（0〜360度）
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject spawned = Instantiate(prefab, position, rotation);

        SpawnOnNetwork(spawned, prefab);

        return spawned;
    }

    /// <summary>
    /// 通信中なら、出した物を全員の画面に出す（Netcode の Spawn）。通信していなければ何もしない。
    /// プレハブに NetworkObject が無いと、ホストの画面にしか出ない（警告だけ出して続ける）。
    /// </summary>
    private static void SpawnOnNetwork(GameObject spawned, GameObject prefab)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsListening)
        {
            return;
        }

        NetworkObject networkObject = spawned.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
        }
        else
        {
            Debug.LogWarning($"[JUNK] {prefab.name} に NetworkObject が無いため、ホストの画面にしか出ません。" +
                             "オンライン用のプレハブを入れてください。");
        }
    }

    /// <summary>
    /// マップにある素材の数。**素材の目印（SpaceJunkMaterial）が付いた物だけ数え、特殊デブリは数えない**
    /// （爆弾・アンカー・<see cref="SpaceJunkBombPoints"/> で置いた特殊デブリで、出せる素材の数が減らないようにする）。
    /// </summary>
    private static int CountMaterials()
    {
        int count = 0;
        foreach (HookableObject item in HookableObject.All)
        {
            if (item != null && !item.IsVanished && item.GetComponent<SpaceJunkMaterial>() != null &&
                item.GetComponent<SpaceJunkBonusDebris>() == null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>爆弾を出してよいか（プレハブが入っていて、マップの爆弾が上限より少ない）。</summary>
    private bool CanSpawnBomb()
    {
        if (maxBombs <= 0)
        {
            return false;
        }

        int count = 0;
        foreach (HookableObject item in HookableObject.All)
        {
            if (item != null && !item.IsVanished && item.GetComponent<ExplosiveObject>() != null)
            {
                count++;
            }
        }
        return count < maxBombs;
    }

    /// <summary>
    /// **出やすさの割合で、Spawn List から1つ選ぶ。**
    /// <paramref name="bombsAllowed"/> が false なら、爆弾は選ばない（マップの爆弾が上限・イベント用の追加）。
    /// </summary>
    private GameObject ChoosePrefab(bool bombsAllowed)
    {
        // Awake より先に（他の部品から）呼ばれても、古い書き方のシーンで一覧が空のままにならないように
        MigrateLegacy();

        // 出やすさの合計のうち、どこに当たったかで決める（ルーレット方式）
        float total = 0f;
        foreach (SpawnEntry entry in spawnList)
        {
            total += Weight(entry, bombsAllowed);
        }

        if (total <= 0f)
        {
            return null;
        }

        float roll = Random.value * total;
        GameObject last = null;

        foreach (SpawnEntry entry in spawnList)
        {
            float weight = Weight(entry, bombsAllowed);
            if (weight <= 0f)
            {
                continue;
            }

            last = entry.prefab;
            roll -= weight;
            if (roll < 0f)
            {
                return entry.prefab;
            }
        }

        return last;
    }

    /// <summary>その1つ分の出やすさ。プレハブが無い・爆弾を選ばない回の爆弾なら 0。</summary>
    private static float Weight(SpawnEntry entry, bool bombsAllowed)
    {
        if (entry == null || entry.prefab == null || entry.weight <= 0f)
        {
            return 0f;
        }

        if (!bombsAllowed && IsBomb(entry.prefab))
        {
            return 0f;
        }

        return entry.weight;
    }

    /// <summary>爆弾（ExplosiveObject 付き）か。</summary>
    private static bool IsBomb(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<ExplosiveObject>() != null;
    }
}
