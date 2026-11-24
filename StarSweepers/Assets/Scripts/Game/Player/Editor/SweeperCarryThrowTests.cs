using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **手前で押すと両手で持って前へ投げる・押さなければ後ろへ引っ張り切る、の自動テスト。**（2026/11/24・小野田さんの指定）
///
/// ・近い物資（2m 先）をつかんですぐ押すと、両手で持ってから、**向いている方向（前）**へ投げる
/// ・押さずに手前まで来たら、**後ろ（体の向こう側）**へ飛ぶ（前は前へはじいていた）
///
/// 通信なしで、体のプレハブと箱1つだけを置き、巻き取り（<see cref="SweeperReel.Begin"/>）を直接呼ぶ。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperCarryThrowTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator NearPress_CarriesThenThrowsForward()
    {
        yield return new EnterPlayMode();
        yield return Run(2f, true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NoPress_PullsThroughToBack()
    {
        yield return new EnterPlayMode();
        yield return Run(6f, false);
        yield return new ExitPlayMode();
    }

    private static IEnumerator Run(float boxDistance, bool pressNear)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い");

        // いつもの操作（パターンA）で確かめる。通信なしなので、このPCに保存した値を使う
        PlayerBehaviorPattern[] before = PlayerBehavior.CopySaved();
        PlayerBehaviorPattern[] normal = PlayerBehavior.CopySaved();
        PlayerBehavior.Assign(normal, PlayerBehaviorStep.Pull, PlayerBehaviorPattern.A);
        PlayerBehavior.Save(normal);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            box.transform.position = new Vector3(0f, 0.5f, boxDistance);
            Rigidbody body = box.AddComponent<Rigidbody>();
            body.linearDamping = 0.4f;
            HookableObject hookable = box.AddComponent<HookableObject>();
            yield return Wait(0.3f);

            SweeperReel reel = player.GetComponent<SweeperReel>();
            reel.Begin(hookable);
            Assert.That(reel.IsActive, Is.True, "巻き取りが始まっていない");

            bool sawCarry = false;
            if (pressNear)
            {
                yield return Wait(0.05f);
                reel.Press();
                sawCarry = reel.IsCarrying;
                Assert.That(sawCarry, Is.True, "近くで押したのに、両手で持っていない");
            }

            // 投げ終わって、落ち着くまで待つ
            float until = Time.realtimeSinceStartup + 6f;
            while (reel.IsActive && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            Assert.That(reel.IsActive, Is.False, "いつまでも投げない");
            yield return Wait(2.5f);

            Vector3 end = box.transform.position;
            Debug.Log($"[SweeperCarryThrowTests] {boxDistance}m 先 押した={pressNear} 結果={reel.LastResult} 止まった所={end:F2}");

            if (pressNear)
            {
                Assert.That(reel.LastResult, Is.EqualTo("前"));
                Assert.That(end.z, Is.GreaterThan(boxDistance + 2f), "向いている方向（前）へ飛んでいない");
            }
            else
            {
                Assert.That(reel.LastResult, Is.EqualTo("後ろ"));
                Assert.That(end.z, Is.LessThan(-2f), "押さなかったのに、後ろへ引っ張り切っていない");
            }
        }
        finally
        {
            PlayerBehavior.Save(before);
            Object.Destroy(player);
            Object.Destroy(box);
            Object.Destroy(ground);
        }

        yield return null;
    }

    /// <summary>実際の時間で待つ（ゲームのフレームが進むように1フレームずつ待つ）。</summary>
    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
