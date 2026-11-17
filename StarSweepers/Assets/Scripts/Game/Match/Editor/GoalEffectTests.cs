using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// ゴールの渦（<see cref="GoalEffect"/>）の自動テスト。円柱の測り方と、
/// 1つの Unity の中でホストと参加者をつないで「渦に入った物資が、両方のPCで小さくなって消える」「渦の外の物資は引き寄せられる」を確かめる。
/// </summary>
public class GoalEffectTests
{
    private const string SupplyPrefabPath = "Assets/Prefabs/Item/SpaceJunkPlate.prefab";
    private const ushort Port = 17992;

    private readonly List<GameObject> made = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in made)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        made.Clear();
    }

    [Test]
    public void Area_IsACircleInsideTheGoalPad_StandingOnTheFloor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        made.Add(floor);
        floor.transform.position = new Vector3(10f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        var root = new GameObject("VortexGoalTest");
        made.Add(root);
        root.transform.position = new Vector3(10f, 2.5f, 0f);
        var box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 5f, 5f);
        root.AddComponent<NetworkObject>();
        var goal = root.AddComponent<SpaceJunkGoal>();

        // 色を塗る床（5m×4m、厚さ 0.1m、当たり判定あり）
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.transform.SetParent(root.transform, false);
        pad.transform.position = new Vector3(10f, 0.05f, 0f);
        pad.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        pad.transform.localScale = new Vector3(5f, 0.1f, 4f);
        var so = new SerializedObject(goal);
        so.FindProperty("padRenderer").objectReferenceValue = pad.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();

        Assert.That(GoalEffect.MeasureArea(goal, 0.9f, out GoalEffect.Area area), Is.True);
        Assert.That(area.Radius, Is.EqualTo(1.8f).Within(0.02f), "半径は床の短い辺（4m）の半分×0.9");
        Assert.That(area.Center.x, Is.EqualTo(10f).Within(0.01f));
        Assert.That(area.Center.z, Is.EqualTo(0f).Within(0.01f));
        Assert.That(area.Center.y, Is.EqualTo(0.1f).Within(0.01f), "足もとは色を塗る床の上面");
        Assert.That(area.Top, Is.EqualTo(5f).Within(0.01f), "上の端はゴールの判定の上の端");
    }

    [UnityTest]
    public IEnumerator Online_ItemShrinksOnBothPcs_AndCornerItemIsPulledIn()
    {
        yield return new EnterPlayMode();
        yield return RunOnline(GoalEffectStyle.Vortex);
        yield return new ExitPlayMode();
    }

    /// <summary>光の柱のときは、物資が上へ浮かびながら小さくなって消える（判定・引き寄せ・得点の流れは渦と同じ）。</summary>
    [UnityTest]
    public IEnumerator Online_Pillar_ItemRisesAndShrinksOnBothPcs()
    {
        yield return new EnterPlayMode();
        yield return RunOnline(GoalEffectStyle.Pillar);
        yield return new ExitPlayMode();
    }

    /// <summary>
    /// 中身。**再生を始めてから呼ぶ別の関数にしてある**（同じ関数の中でラムダに渡す値を作ると、
    /// 再生の切り替えのときに入れ物が消えて NullReferenceException になるため。SweeperOnlineTests と同じ）。
    /// </summary>
    private static IEnumerator RunOnline(GoalEffectStyle style)
    {
        GameObject supplyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SupplyPrefabPath);
        Assert.That(supplyPrefab, Is.Not.Null);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        var vortexObject = new GameObject("VortexUnderTest");
        GoalEffect vortex = vortexObject.AddComponent<GoalEffect>();
        vortex.Style = style;
        GameObject goalObject = null;
        int absorbedEvents = 0;
        System.Action<Component> onAbsorbed = _ => absorbedEvents++;
        GoalEffect.Absorbed += onAbsorbed;

        var managers = new List<NetworkManager>();
        try
        {
            NetworkManager host = CreateManager("VortexTestHost", supplyPrefab);
            managers.Add(host);
            Assert.That(host.StartHost(), Is.True);
            NetworkManager client = CreateManager("VortexTestClient", supplyPrefab);
            managers.Add(client);
            Assert.That(client.StartClient(), Is.True);
            yield return WaitFor("connect", () => client.IsConnectedClient);

            // ゴールが「ホストか」を見るのは NetworkManager.Singleton なので、ホストにしておく（本番は1台に1つ）
            host.SetSingleton();

            // ゴール（4m 四方・高さ 5m の判定。床は地面）。渦の半径は 4×0.5×0.9 = 1.8m
            goalObject = new GameObject("VortexTestGoal");
            goalObject.transform.position = new Vector3(0f, 2.5f, 0f);
            var box = goalObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 5f, 4f);
            goalObject.AddComponent<NetworkObject>();
            SpaceJunkGoal goal = goalObject.AddComponent<SpaceJunkGoal>();
            goal.ApplyOwnerTeam(0);
            yield return Wait(0.2f);

            // ---- 1個目：渦の真上から落とす ----
            (GameObject hostItem, GameObject clientItem) = Spawn(host, client, supplyPrefab, new Vector3(0.5f, 3f, 0.3f));
            yield return WaitFor("client copy", () => clientItem != null || FindClientCopy(client, hostItem) != null);
            clientItem = FindClientCopy(client, hostItem);
            IgnoreEachOther(hostItem, clientItem);
            Vector3 clientStartScale = clientItem.transform.localScale;

            yield return WaitFor("host absorbs", () => hostItem == null || hostItem.GetComponent<GoalAbsorb>() != null);
            Assert.That(hostItem, Is.Not.Null, "吸い込みが見える前に消えた");
            Assert.That(hostItem.GetComponent<HookableObject>().IsVanished, Is.True, "吸い込み中は引っ掛けられない（消えた扱い）");

            yield return WaitFor("client absorbs", () => clientItem == null || clientItem.GetComponent<GoalAbsorb>() != null);
            Assert.That(clientItem, Is.Not.Null, "参加者のPCで吸い込みが始まる前に消えた");
            Assert.That(clientItem.GetComponent<HookableObject>().IsAbsorbing, Is.True, "参加者のPCでも吸い込み中の印が付く");
            Assert.That(clientItem.GetComponent<Collider>().enabled, Is.False, "参加者のPCでも当たり判定が消える");

            // 消えるまで、参加者のPCでの大きさと、ホストでの中心からの距離を見ておく
            float minClientScale = 1f;
            float startDistance = Flat(hostItem.transform.position).magnitude;
            float minHostDistance = startDistance;
            float startY = hostItem.transform.position.y;
            float maxHostY = startY;
            float deadline = Time.realtimeSinceStartup + 6f;
            while ((hostItem != null || clientItem != null) && Time.realtimeSinceStartup < deadline)
            {
                if (clientItem != null && clientItem.GetComponent<Renderer>() is Renderer r && r.enabled)
                {
                    minClientScale = Mathf.Min(minClientScale, clientItem.transform.localScale.x / clientStartScale.x);
                }
                if (hostItem != null && hostItem.GetComponent<GoalAbsorb>() != null)
                {
                    minHostDistance = Mathf.Min(minHostDistance, Flat(hostItem.transform.position).magnitude);
                    maxHostY = Mathf.Max(maxHostY, hostItem.transform.position.y);
                }
                yield return null;
            }

            Assert.That(hostItem == null, Is.True, "ホストで物資が消えていない");
            Assert.That(clientItem == null, Is.True, "参加者のPCで物資が消えていない");
            Assert.That(minClientScale, Is.LessThan(0.5f), "参加者のPCで、消える前に小さくなっていない");
            Assert.That(minHostDistance, Is.LessThan(startDistance * 0.3f + 0.05f), "ホストで真ん中へ寄っていない");
            if (style == GoalEffectStyle.Pillar)
            {
                Assert.That(maxHostY, Is.GreaterThan(startY + 0.4f), "光の柱のときに、ホストで上へ浮かんでいない");
            }
            Assert.That(absorbedEvents, Is.EqualTo(1));

            // ---- 2個目：ゴールの角（渦の外）に落とす → 引き寄せられて吸い込まれる ----
            // 参加者の写しが出たらすぐ、2つの写しが押し合わないようにする（押されてゴールの外へ出ると、引き寄せられない）
            (GameObject cornerItem, _) = Spawn(host, client, supplyPrefab, new Vector3(1.5f, 1.2f, 1.5f));
            yield return WaitFor("corner copy", () => cornerItem == null || FindClientCopy(client, cornerItem) != null);
            IgnoreEachOther(cornerItem, FindClientCopy(client, cornerItem));
            Assert.That(new Vector2(1.5f, 1.5f).magnitude, Is.GreaterThan(1.8f), "テストの置き場所が渦の外になっていない");
            yield return WaitFor("corner absorbed", () => cornerItem == null, 8f);
            Assert.That(absorbedEvents, Is.EqualTo(2));
            Assert.That(vortex.AbsorbCount, Is.EqualTo(2));
        }
        finally
        {
            GoalEffect.Absorbed -= onAbsorbed;
            for (int i = managers.Count - 1; i >= 0; i--)
            {
                if (managers[i] == null) continue;
                managers[i].Shutdown();
                Object.Destroy(managers[i].gameObject);
            }
            Object.Destroy(ground);
            Object.Destroy(vortexObject);
            if (goalObject != null) Object.Destroy(goalObject);
        }

        yield return null;
    }

    private static (GameObject, GameObject) Spawn(NetworkManager host, NetworkManager client, GameObject prefab, Vector3 position)
    {
        GameObject item = Object.Instantiate(prefab, position, Quaternion.identity);
        item.GetComponent<NetworkObject>().Spawn();
        return (item, FindClientCopy(client, item));
    }

    /// <summary>ホストの物資の、参加者のPCでの写し（まだ届いていなければ null）。</summary>
    private static GameObject FindClientCopy(NetworkManager client, GameObject hostItem)
    {
        if (hostItem == null) return null;
        ulong id = hostItem.GetComponent<NetworkObject>().NetworkObjectId;
        return client.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject copy) ? copy.gameObject : null;
    }

    /// <summary>1つの Unity の中にホストと参加者の物資が重なっているので、押し合わないようにする（本番は別々のPC）。</summary>
    private static void IgnoreEachOther(GameObject a, GameObject b)
    {
        if (a == null || b == null) return;
        foreach (Collider ca in a.GetComponentsInChildren<Collider>())
        {
            foreach (Collider cb in b.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(ca, cb);
            }
        }
    }

    private static NetworkManager CreateManager(string name, GameObject supplyPrefab)
    {
        GameObject root = new GameObject(name);
        UnityTransport transport = root.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", Port);
        NetworkManager manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = supplyPrefab });
        return manager;
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator WaitFor(string label, System.Func<bool> condition, float seconds = 10f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"{seconds}秒以内に条件がそろわなかった：" + label);
    }
}
