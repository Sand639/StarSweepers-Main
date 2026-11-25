using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **光線でつかんでいる間は重力なし（ただし持つ高さより上では落ちる）、の自動テスト。**（2026/11/24・小野田さんの指示）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と、空中に浮かせた箱1つだけを置いて確かめる。
/// ・つかんでいる間は箱の重力が切れていて、つかんだ高さのまま寄ってくる
/// ・高すぎる所でつかんだ箱は、持つ高さの上限（Max Hold Above）まで落ちて、そこで止まる（床までは落ちない）
/// ・つかんでいる間に上へはね上げられても、持つ高さまで落ちてくる（そのまま浮いていかない）
/// ・離したら重力が元に戻る
/// ・No Gravity While Held をオフにすると、前のように重力のまま
///
/// 光線を撃つところは飛ばして、巻き取り（<see cref="SweeperReel.Begin"/>）を直接呼ぶ。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperReelGravityTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    /// <summary>ふつうに置く高さ（m）。床から離しておき、重力があれば落ちるのが分かるようにする。持つ高さの上限（1.5m）より低い。</summary>
    private const float BoxHeight = 1.2f;

    [UnityTest]
    public IEnumerator Held_NoGravity_ThenRestored()
    {
        yield return new EnterPlayMode();
        yield return Run(true, BoxHeight, BoxHeight, false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SettingOff_KeepsGravity()
    {
        yield return new EnterPlayMode();
        yield return Run(false, BoxHeight, -1f, false);
        yield return new ExitPlayMode();
    }

    /// <summary>3m の高さでつかむと、持つ高さの上限（足元から Max Hold Above）まで落ちて止まる。</summary>
    [UnityTest]
    public IEnumerator HighGrab_FallsToHoldLimit()
    {
        yield return new EnterPlayMode();
        yield return Run(true, 3f, float.NaN, false);
        yield return new ExitPlayMode();
    }

    /// <summary>つかんでいる間に上へはね上げられても、持つ高さまで落ちてくる。</summary>
    [UnityTest]
    public IEnumerator KnockedUp_ComesBackDown()
    {
        yield return new EnterPlayMode();
        yield return Run(true, BoxHeight, BoxHeight, true);
        yield return new ExitPlayMode();
    }

    /// <summary>
    /// 中身。再生を始めてから呼ぶ別の関数にしてある（SweeperWhipTests と同じ理由）。
    /// <paramref name="expectedHeight"/>：つかんでいる間の高さ。負なら「落ちていく」、NaN なら持つ高さの上限。
    /// </summary>
    private static IEnumerator Run(bool noGravityWhileHeld, float startHeight, float expectedHeight, bool knockUp)
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
            SetField(reel, "noGravityWhileHeld", noGravityWhileHeld);
            float holdLimit = player.transform.position.y + (float)GetField(reel, "maxHoldAbove");
            if (float.IsNaN(expectedHeight))
            {
                expectedHeight = holdLimit;
            }

            box.transform.position = new Vector3(0f, startHeight, 8f);
            Rigidbody body = box.AddComponent<Rigidbody>();
            HookableObject hookable = box.AddComponent<HookableObject>();
            Assert.That(body.useGravity, Is.True);

            // 光線が当たった瞬間と同じ
            reel.Begin(hookable);
            Assert.That(reel.IsActive, Is.True, "巻き取りが始まっていない");
            Assert.That(body.useGravity, Is.EqualTo(!noGravityWhileHeld), "つかんだ瞬間の重力の設定がちがう");

            if (knockUp)
            {
                // 人や物にぶつかって、上へはね上げられた
                yield return Wait(0.1f);
                body.linearVelocity = new Vector3(body.linearVelocity.x, 6f, body.linearVelocity.z);
            }

            // 引き寄せが終わる前の少しの間だけ見る（はね上げたときは落ちてくるまで少し長く）
            float startZ = box.transform.position.z;
            float peak = box.transform.position.y;
            float until = Time.realtimeSinceStartup + (knockUp ? 1.6f : 0.9f);
            while (Time.realtimeSinceStartup < until)
            {
                peak = Mathf.Max(peak, box.transform.position.y);
                yield return null;
            }
            Assert.That(reel.IsActive, Is.True, "見ている間に巻き取りが終わってしまった");

            float y = box.transform.position.y;
            float z = box.transform.position.z;
            Debug.Log($"[SweeperReelGravityTests] 重力なし={noGravityWhileHeld} 高さ {startHeight:F2}→{y:F2}（いちばん高い所 {peak:F2}・上限 {holdLimit:F2}） 奥行き {startZ:F2}→{z:F2}");

            Assert.That(z, Is.LessThan(startZ - 0.5f), "箱がプレイヤーのほうへ寄ってきていない");
            if (noGravityWhileHeld)
            {
                Assert.That(body.useGravity, Is.False, "つかんでいる間に重力が戻っている");
                Assert.That(y, Is.EqualTo(expectedHeight).Within(0.12f), "持つ高さに落ち着いていない");
                if (knockUp)
                {
                    Assert.That(peak, Is.GreaterThan(expectedHeight + 0.5f), "はね上げたのに上がらなかった（テストの作りがおかしい）");
                }
            }
            else
            {
                Assert.That(body.useGravity, Is.True, "設定をオフにしたのに重力が切れている");
                Assert.That(y, Is.LessThan(startHeight - 0.3f), "重力のままなのに、箱が落ちていない");
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

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    /// <summary>実際の時間で待つ（ゲームのフレームが進むように1フレームずつ待つ）。</summary>
    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
