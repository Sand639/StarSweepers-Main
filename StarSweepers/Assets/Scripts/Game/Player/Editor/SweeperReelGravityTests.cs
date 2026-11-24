using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **光線でつかんでいる間は重力なし、の自動テスト。**（2026/11/24・小野田さんの指示）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と、空中に浮かせた箱1つだけを置いて確かめる。
/// ・つかんでいる間は箱の重力が切れていて、落ちずに（高さを保ったまま）寄ってくる
/// ・離したら重力が元に戻る
/// ・No Gravity While Held をオフにすると、前のように重力のまま
///
/// 光線を撃つところは飛ばして、巻き取り（<see cref="SweeperReel.Begin"/>）を直接呼ぶ。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperReelGravityTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    /// <summary>箱を置く高さ（m）。床から離しておき、重力があれば落ちるのが分かるようにする。</summary>
    private const float BoxHeight = 3f;

    [UnityTest]
    public IEnumerator Held_NoGravity_ThenRestored()
    {
        yield return new EnterPlayMode();
        yield return Run(true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SettingOff_KeepsGravity()
    {
        yield return new EnterPlayMode();
        yield return Run(false);
        yield return new ExitPlayMode();
    }

    /// <summary>中身。再生を始めてから呼ぶ別の関数にしてある（SweeperWhipTests と同じ理由）。</summary>
    private static IEnumerator Run(bool noGravityWhileHeld)
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

            SweeperReel reel = player.GetComponent<SweeperReel>();
            typeof(SweeperReel)
                .GetField("noGravityWhileHeld", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(reel, noGravityWhileHeld);

            box.transform.position = new Vector3(0f, BoxHeight, 6f);
            Rigidbody body = box.AddComponent<Rigidbody>();
            HookableObject hookable = box.AddComponent<HookableObject>();
            Assert.That(body.useGravity, Is.True);

            // 光線が当たった瞬間と同じ
            reel.Begin(hookable);
            Assert.That(reel.IsActive, Is.True, "巻き取りが始まっていない");
            Assert.That(body.useGravity, Is.EqualTo(!noGravityWhileHeld), "つかんだ瞬間の重力の設定がちがう");

            // 引き寄せ（Reel Seconds 1.5 秒）が終わる前の 0.5 秒だけ見る
            float startZ = box.transform.position.z;
            yield return Wait(0.5f);
            Assert.That(reel.IsActive, Is.True, "0.5 秒で巻き取りが終わってしまった");

            float y = box.transform.position.y;
            float z = box.transform.position.z;
            Debug.Log($"[SweeperReelGravityTests] 重力なし={noGravityWhileHeld} 高さ {BoxHeight:F2}→{y:F2} 奥行き {startZ:F2}→{z:F2}");

            Assert.That(z, Is.LessThan(startZ - 0.5f), "箱がプレイヤーのほうへ寄ってきていない");
            if (noGravityWhileHeld)
            {
                Assert.That(body.useGravity, Is.False, "つかんでいる間に重力が戻っている");
                Assert.That(y, Is.EqualTo(BoxHeight).Within(0.05f), "重力を切っているのに、箱が落ちた");
            }
            else
            {
                Assert.That(body.useGravity, Is.True, "設定をオフにしたのに重力が切れている");
                Assert.That(y, Is.LessThan(BoxHeight - 0.3f), "重力のままなのに、箱が落ちていない");
            }

            // 離したら、重力は元どおり
            reel.Cancel();
            Assert.That(reel.IsActive, Is.False);
            Assert.That(body.useGravity, Is.True, "離したのに重力が戻っていない");
        }
        finally
        {
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
