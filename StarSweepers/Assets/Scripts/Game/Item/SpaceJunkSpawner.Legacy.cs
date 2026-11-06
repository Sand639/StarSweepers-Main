using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSpawner の「古い書き方からの移し替え」（SpaceJunkSpawner.cs から分けた部分）。</summary>
public partial class SpaceJunkSpawner
{
    /// <summary>
    /// **古い書き方（3種類＋追加の素材＋爆弾）を Spawn List へ移し替える。** Spawn List が空のときだけ。
    /// 爆弾の「確率」は、素材の出やすさの合計から、同じ割合になる出やすさに直す。移し替えたら true。
    /// </summary>
    private bool MigrateLegacy()
    {
        if (spawnList == null)
        {
            spawnList = new List<SpawnEntry>();
        }

        if (spawnList.Count > 0)
        {
            return false;
        }

        float materialTotal = 0f;
        materialTotal += AddLegacy(platePrefab, plateWeight);
        materialTotal += AddLegacy(circuitPrefab, circuitWeight);
        materialTotal += AddLegacy(fuelPrefab, fuelWeight);
        if (extraMaterials != null)
        {
            foreach (ExtraMaterial extra in extraMaterials)
            {
                if (extra != null)
                {
                    materialTotal += AddLegacy(extra.prefab, extra.weight);
                }
            }
        }

        if (bombPrefab != null && bombChance > 0f)
        {
            float chance = Mathf.Clamp(bombChance, 0.01f, 0.95f);
            float weight = Mathf.Round(materialTotal * chance / (1f - chance) * 100f) / 100f;
            spawnList.Add(new SpawnEntry { prefab = bombPrefab, weight = weight });
        }

        return spawnList.Count > 0;
    }

    /// <summary>古い書き方の1項目を Spawn List に足し、足した出やすさを返す（プレハブが無ければ足さずに 0）。</summary>
    private float AddLegacy(GameObject prefab, int weight)
    {
        if (prefab == null)
        {
            return 0f;
        }

        spawnList.Add(new SpawnEntry { prefab = prefab, weight = Mathf.Max(0, weight) });
        return Mathf.Max(0, weight);
    }
}
