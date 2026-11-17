using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **新しい体の「光線の操作：距離で変わる」の自動テスト。**（2026/11/17）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と箱だけを置いて確かめる。
/// ・近く（届く長さの 32% より手前）の箱は、当たったらすぐ頭を越えて後ろへ投げる
/// ・遠くの箱は、投げずに決まった距離だけ引き寄せて離す（9m 先なら約 2.5m）
///
/// 作り方は <c>SweeperHeavyBotTests</c> と同じ。設定はテストの間だけ変え、終わったら元に戻す。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperDistanceModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator NearSupply_IsThrownBack()
    {
        yield return new EnterPlayMode();
        yield return Run(2f, 0f, true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FarSupply_IsPulledAndReleased()
    {
        yield return new EnterPlayMode();
        yield return Run(9f, 1f, false);
        yield return new ExitPlayMode();
    }

    private static IEnumerator Run(float boxDistance, float charge, bool expectThrow)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い");

        BeamOperationType before = GameSettings.BeamOperation;
        GameSettings.BeamOperation = BeamOperationType.DistanceStrength;

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            // 正面（+Z）に箱を置く
            box.transform.position = new Vector3(0f, 0.5f, boxDistance);
            Rigidbody body = box.AddComponent<Rigidbody>();
            body.linearDamping = 0.4f;
            box.AddComponent<HookableObject>();
            yield return Wait(0.5f);

            Vector3 start = box.transform.position;
            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            SweeperReel reel = player.GetComponent<SweeperReel>();

            Invoke(shooter, "BeginCharge");
            SetField(shooter, "charge", charge);
            yield return NextFrame();
            Invoke(shooter, "Fire");

            float maxHeight = box.transform.position.y;
            float until = Time.realtimeSinceStartup + 6f;
            bool hit = false;
            while (Time.realtimeSinceStartup < until)
            {
                hit |= reel.IsActive;
                maxHeight = Mathf.Max(maxHeight, box.transform.position.y);
                if (hit && !reel.IsActive)
                {
                    break;
                }
                yield return NextFrame();
            }

            Assert.That(hit, Is.True, "光線が箱に当たらなかった");
            Assert.That(reel.IsActive, Is.False, "いつまでも終わらない");

            // 飛んだ・引いた箱が落ち着くまで待つ
            float settle = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < settle)
            {
                maxHeight = Mathf.Max(maxHeight, box.transform.position.y);
                yield return NextFrame();
            }

            Vector3 end = box.transform.position;
            Debug.Log($"[SweeperDistanceModeTests] {boxDistance}m 先の箱：結果={reel.LastResult} 止まった所={end:F2} いちばん高い所={maxHeight:F2}m");

            if (expectThrow)
            {
                Assert.That(reel.LastResult, Is.EqualTo("近くで投げた"));
                Assert.That(maxHeight, Is.GreaterThan(2f), "頭を越える高さまで上がっていない");
                Assert.That(end.z, Is.LessThan(-1.5f), "体の後ろへ飛んでいない");
            }
            else
            {
                float moved = start.z - end.z;
                Assert.That(reel.LastResult, Is.EqualTo("引っ張った"));
                Assert.That(maxHeight, Is.LessThan(1.2f), "引っ張るはずが、投げられた（浮いた）");
                // 9m ÷ 12m＝0.75 → 強さ (1−0.75)÷(1−0.43)≒0.44 → 0.5＋4.5×0.44≒2.5m
                Assert.That(moved, Is.InRange(1.6f, 3.4f), "引き寄せた距離が約 2.5m になっていない");
            }
        }
        finally
        {
            GameSettings.BeamOperation = before;
            Object.Destroy(player);
            Object.Destroy(box);
            Object.Destroy(ground);
        }

        yield return null;
    }

    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, method + " が見つからない");
        info.Invoke(target, null);
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, field + " が見つからない");
        info.SetValue(target, value);
    }

    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
