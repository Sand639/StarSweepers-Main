using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSpawner の「出す場所を決める（1巡法・床の確認）」（SpaceJunkSpawner.cs から分けた部分）。</summary>
public partial class SpaceJunkSpawner
{
    /// <summary>範囲の中から、何も無い場所を探す。</summary>
    private bool TryFindPlace(Transform[] zones, out Vector3 position)
    {
        // 1巡法：この回に出すゾーンを、1巡の残りから1つ取る
        Transform bagZone = null;
        if (cycleZones && zones == spawnZones && UsableZoneCount(zones) >= 2)
        {
            bagZone = NextZoneFromBag(zones);
        }

        for (int i = 0; i < placementTries; i++)
        {
            Vector3 candidate = bagZone != null ? PointInZone(bagZone) : RandomPointInArea(zones);

            // 一段高い床の上なら、その床の高さを基準にする（Spawn On Raised Floor が ON のときだけ）
            float floorY = transform.position.y;
            if (spawnOnRaisedFloor && TryFindRaisedFloor(candidate, out float raisedY) && raisedY > floorY)
            {
                floorY = raisedY;
                candidate.y = Mathf.Max(candidate.y, floorY + dropHeight);
            }

            // 落とす高さから床の少し上まで、縦に長く調べる（プレイヤーや障害物の真上を避ける）。
            // 下端は床に当たらないよう、半径ぶん＋0.1m 浮かせる。全レイヤー（~0）を見る
            Vector3 bottom = new Vector3(candidate.x, floorY + clearRadius + 0.1f, candidate.z);
            bool blocked = Physics.CheckCapsule(bottom, candidate, clearRadius, ~0, QueryTriggerInteraction.Ignore);

            if (!blocked && HasGroundBelow(candidate))
            {
                position = candidate;
                return true;
            }
        }

        // 空いた場所が無かった。1巡法なら、このゾーンを次の回に回す（1巡の中で抜けが出ないように）
        if (bagZone != null)
        {
            zoneBag.Insert(0, bagZone);
        }

        position = Vector3.zero;
        return false;
    }

    // ------------------------------------------------------------
    // 1巡法（Cycle Zones）
    // ------------------------------------------------------------

    /// <summary>使えるゾーン（入っていて、広さがある）の数。</summary>
    private static int UsableZoneCount(Transform[] zones)
    {
        int count = 0;
        foreach (Transform zone in zones)
        {
            if (ZoneArea(zone) > 0f)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// **1巡の残りから、次のゾーンを1つ取る。** 残りが無くなったら、全ゾーンを入れ直してシャッフルする
    /// （テトリスの7種1巡と同じ。1巡の中では、どのゾーンにも1回ずつ出る）。
    /// </summary>
    private Transform NextZoneFromBag(Transform[] zones)
    {
        // ゾーンが消された・入れ替えられたときに、古いものを使わない
        zoneBag.RemoveAll(zone => ZoneArea(zone) <= 0f || System.Array.IndexOf(zones, zone) < 0);

        if (zoneBag.Count == 0)
        {
            foreach (Transform zone in zones)
            {
                if (ZoneArea(zone) > 0f)
                {
                    zoneBag.Add(zone);
                }
            }

            // 並びをシャッフル（フィッシャー–イェーツ）
            for (int i = zoneBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (zoneBag[i], zoneBag[j]) = (zoneBag[j], zoneBag[i]);
            }
        }

        Transform next = zoneBag[0];
        zoneBag.RemoveAt(0);
        return next;
    }

    /// <summary>
    /// ゾーン（四角）の中のランダムな1点（高さは「床＋Drop Height」）。
    /// 四角の中の1点は、-0.5〜0.5 を四角の大きさと向きに合わせて広げて決める（Scale 1 の立方体の中、と同じ）。
    /// </summary>
    private Vector3 PointInZone(Transform zone)
    {
        Vector3 local = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
        Vector3 point = zone.TransformPoint(local);
        point.y = transform.position.y + dropHeight;
        return point;
    }

    /// <summary>
    /// 真下にある床（地形）の上面の高さ。プレイヤーや、物理で動く物（物資・爆弾）は床とみなさず、通り抜けて下を見る。
    /// いくつも当たったときは、一番高いもの。
    /// </summary>
    private static bool TryFindRaisedFloor(Vector3 candidate, out float floorY)
    {
        floorY = float.MinValue;
        bool found = false;

        // 真下 50m まで、全レイヤー（~0）を見る
        foreach (RaycastHit hit in Physics.RaycastAll(candidate, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            // プレイヤー（CharacterController）と、物理で動く物（Rigidbody が kinematic でない）は床ではない
            if (hit.collider is CharacterController ||
                (hit.rigidbody != null && !hit.rigidbody.isKinematic))
            {
                continue;
            }

            if (hit.point.y > floorY)
            {
                floorY = hit.point.y;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 真下に床があるか（Require Ground Below が OFF なら常に true）。
    /// 出す場所の周りは空いていると確かめてあるので、真下に最初に当たる物が床になる。
    /// </summary>
    private bool HasGroundBelow(Vector3 candidate)
    {
        if (!requireGroundBelow)
        {
            return true;
        }

        // 落とす高さより 2m 余分に下まで見る（床がスポナーより少し低くても見つかるように）
        return Physics.Raycast(candidate, Vector3.down, dropHeight + 2f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// このプレハブを出す範囲。Kind Zones にその種類があればその範囲、無ければ Spawn Zones。
    /// </summary>
    private Transform[] ZonesFor(GameObject prefab)
    {
        if (prefab != null && prefab.TryGetComponent(out SpaceJunkMaterial material))
        {
            foreach (KindZone kindZone in kindZones)
            {
                if (kindZone != null && kindZone.kind == material.Kind && kindZone.zones != null &&
                    kindZone.zones.Length > 0)
                {
                    return kindZone.zones;
                }
            }
        }

        return spawnZones;
    }

    /// <summary>
    /// 出す候補の場所を1つ選ぶ（高さは「床＋Drop Height」）。
    /// 範囲（<paramref name="zones"/>）があれば、**その四角のどれか**（広いものほど選ばれやすい）の中。
    /// 無ければ、Spawn Circle Radius の円か、Area Half Size の四角の中。
    /// </summary>
    private Vector3 RandomPointInArea(Transform[] zones)
    {
        float totalArea = 0f;
        foreach (Transform zone in zones)
        {
            totalArea += ZoneArea(zone);
        }

        if (totalArea <= 0f && spawnCircleRadius > 0f)
        {
            // 円の中で、どこも同じくらいの出やすさになるように選ぶ
            Vector2 inCircle = Random.insideUnitCircle * spawnCircleRadius;
            return transform.position + new Vector3(inCircle.x, dropHeight, inCircle.y);
        }

        if (totalArea <= 0f)
        {
            return transform.position + new Vector3(
                Random.Range(-areaHalfSize.x, areaHalfSize.x),
                dropHeight,
                Random.Range(-areaHalfSize.y, areaHalfSize.y));
        }

        // 広さの合計のうち、どこに当たったかでゾーンを決める（広いほど選ばれやすい）
        float roll = Random.Range(0f, totalArea);
        foreach (Transform zone in zones)
        {
            float area = ZoneArea(zone);
            if (area <= 0f)
            {
                continue;
            }

            if (roll <= area)
            {
                // 四角の中の1点（-0.5〜0.5 を、四角の大きさと向きに合わせて広げる）
                return PointInZone(zone);
            }
            roll -= area;
        }

        // 丸め誤差などでどのゾーンにも当たらなかったときの保険（ふつうは通らない）
        return transform.position + Vector3.up * dropHeight;
    }

    /// <summary>範囲の四角の広さ（大きさ Scale の X × Z）。</summary>
    private static float ZoneArea(Transform zone)
    {
        if (zone == null)
        {
            return 0f;
        }

        Vector3 size = zone.lossyScale;
        return Mathf.Abs(size.x * size.z);
    }
}
