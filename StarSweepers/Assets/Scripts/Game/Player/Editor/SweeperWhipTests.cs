using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **鞭の光線（撃ち方 Whip）の自動テスト。**（2026/11/17・小野田さんの変更案）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と箱1つだけを置いて確かめる。
/// ・正面の箱には、まっすぐ伸びてつかむ
/// ・真横の箱は、向きを変えなければつかめず、1 秒で光線が戻る
/// ・真横の箱も、出している間に向きを変えて振れば、しなった光線が触れてつかめる
///
/// 体の向きはテストが手で決める（歩く部品 SweeperMover は止めておく）。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperWhipTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator Front_GrabsStraight()
    {
        yield return new EnterPlayMode();
        yield return Run(new Vector3(0f, 0.5f, 5f), false, true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Side_NoSwing_Misses()
    {
        yield return new EnterPlayMode();
        yield return Run(new Vector3(5f, 0.5f, 0f), false, false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Side_Swing_Grabs()
    {
        yield return new EnterPlayMode();
        yield return Run(new Vector3(5f, 0.5f, 0f), true, true);
        yield return new ExitPlayMode();
    }

    /// <summary>中身。再生を始めてから呼ぶ別の関数にしてある（SweeperBendTests と同じ理由）。</summary>
    private static IEnumerator Run(Vector3 boxPosition, bool swing, bool expectGrab)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            box.transform.position = boxPosition;
            box.AddComponent<Rigidbody>();
            box.AddComponent<HookableObject>();
            yield return Wait(0.5f);

            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            shooter.Style = SweeperShooter.ShotStyle.Whip;

            // 押した瞬間と同じ（狙いは正面 +Z のまま。体も正面を向く）
            Invoke(shooter, "BeginWhip");
            Assert.That(shooter.IsWhipping, Is.True, "押しても鞭の光線が出ていない");

            bool grabbed = false;
            float startTime = Time.time;
            float until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until)
            {
                // 光線が伸びきってから、0.3 秒かけて右（+X）へ 90° 振る
                float elapsed = Time.time - startTime;
                if (swing && shooter.IsWhipping && elapsed > 0.4f)
                {
                    float yaw = Mathf.Clamp01((elapsed - 0.4f) / 0.3f) * 90f;
                    player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }

                if (shooter.Phase == SweeperShooter.ShotPhase.Reeling)
                {
                    grabbed = true;
                    break;
                }

                if (shooter.Phase == SweeperShooter.ShotPhase.Idle)
                {
                    break;
                }

                yield return NextFrame();
            }

            float seconds = Time.time - startTime;
            Debug.Log($"[SweeperWhipTests] 箱={boxPosition} 振る={swing} つかんだ={grabbed} 経過={seconds:F2}秒 段階={shooter.Phase}");

            if (expectGrab)
            {
                Assert.That(grabbed, Is.True, $"箱をつかめなかった（段階 {shooter.Phase}・光線の先 {shooter.BeamTip}）");
                Assert.That(seconds, Is.LessThan(1.05f), "出していられる 1 秒を過ぎてからつかんだ");
            }
            else
            {
                Assert.That(grabbed, Is.False, "向きを変えていないのに、真横の箱をつかんだ");
                Assert.That(seconds, Is.InRange(0.9f, 2f), "1 秒で光線が戻っていない");
            }
        }
        finally
        {
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

    /// <summary>ゲームのフレームが1つ進むまで待つ（画面なしの Unity では `yield return null` だけだとフレームが進まないため）。</summary>
    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
    }

    /// <summary>実際の時間で待つ。</summary>
    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
