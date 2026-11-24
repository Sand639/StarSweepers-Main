using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **新しい体の「光線の操作：距離で変わる」の自動テスト。**（2026/11/17）
///
/// 通信なしで、新しい体のプレハブ（`SweeperPlayer.prefab`）と箱だけを置き、光線が当たった直後に押して確かめる。
/// 距離の 100% は、ビームの長さの上限（Max Reach。12m）。溜めた長さには関係ない。
/// ・近く（20% より手前）で押すと、**前へ投げる**（いつもの前へ投げる速さ × 強さ）
/// ・投げとクリティカルの間（20〜29%）で押すと、強さ 0.5 で引っ張る
/// ・クリティカルより遠くで押すと、**引っ張る**＝いつもの後ろへの投げの速さ × 強さ。強さが 1 より小さいので、体の後ろまでは届かず手前に落ちる
/// ・29〜32% で押すと、今までどおりのクリティカルプル（体の後ろまで飛ぶ）
///
/// 作り方は <c>SweeperHeavyBotTests</c> と同じ。設定はテストの間だけ変え、終わったら元に戻す。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperDistanceModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator NearPress_ThrowsForward()
    {
        yield return new EnterPlayMode();
        yield return Run(1.6f, 1f, true, false);
        yield return new ExitPlayMode();
    }

    /// <summary>投げとクリティカルの間（20〜29%）の引っ張りは、どこで押しても強さ 0.5。3m ÷ 12m＝25%。</summary>
    [UnityTest]
    public IEnumerator MiddlePress_PullsAtHalfStrength()
    {
        yield return new EnterPlayMode();
        yield return Run(3f, 1f, false, false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FarPress_PullsWithStrength()
    {
        yield return new EnterPlayMode();
        yield return Run(9f, 1f, false, false);
        yield return new ExitPlayMode();
    }

    /// <summary>100% はビームの長さの上限（12m）で、溜めた長さには関係ない：溜めずに撃っても、1.6m 先は 13% なので前へ投げる。</summary>
    [UnityTest]
    public IEnumerator ShortShot_StillUsesMaxReach()
    {
        yield return new EnterPlayMode();
        yield return Run(1.6f, 0f, true, false);
        yield return new ExitPlayMode();
    }

    /// <summary>押さなければ、一定の速さ（6m/秒）で寄ってきて、手前に着いたら前へ投げる。9m 先なら (9 − 1.4) ÷ 6 ≒ 1.3 秒。</summary>
    [UnityTest]
    public IEnumerator NoPress_ComesAtConstantSpeedThenForward()
    {
        yield return new EnterPlayMode();
        yield return RunNoPress();
        yield return new ExitPlayMode();
    }

    private static IEnumerator RunNoPress()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        // 距離で変わる操作は、プレイヤー挙動「引っ張る・投げる」のパターン B（2026/11/18。前はポーズ画面の設定）。通信なしなので、このPCに保存した値を使う
        PlayerBehaviorPattern[] before = PlayerBehavior.CopySaved();
        PlayerBehaviorPattern[] distance = PlayerBehavior.CopySaved();
        PlayerBehavior.Assign(distance, PlayerBehaviorStep.Pull, PlayerBehaviorPattern.B);
        PlayerBehavior.Save(distance);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;
            box.transform.position = new Vector3(0f, 0.5f, 9f);
            box.AddComponent<Rigidbody>().linearDamping = 0.4f;
            box.AddComponent<HookableObject>();
            yield return Wait(0.5f);

            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            SweeperReel reel = player.GetComponent<SweeperReel>();
            Invoke(shooter, "BeginCharge");
            SetField(shooter, "charge", 1f);
            yield return NextFrame();
            Invoke(shooter, "Fire");

            float deadline = Time.realtimeSinceStartup + 5f;
            while (!reel.IsActive && Time.realtimeSinceStartup < deadline)
            {
                yield return NextFrame();
            }
            Assert.That(reel.IsActive, Is.True, "光線が箱に当たらなかった");

            float started = Time.time;
            deadline = Time.realtimeSinceStartup + 6f;
            while (reel.IsActive && Time.realtimeSinceStartup < deadline)
            {
                yield return NextFrame();
            }
            float took = Time.time - started;
            Debug.Log($"[SweeperDistanceModeTests] 押さなかった：結果={reel.LastResult} 着くまで={took:F2}秒");

            Assert.That(reel.LastResult, Is.EqualTo("前"));
            Assert.That(took, Is.InRange(1.0f, 1.7f), "一定の速さ（6m/秒）で寄ってきていない");
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

    [UnityTest]
    public IEnumerator CriticalPress_KeepsNormalCritical()
    {
        yield return new EnterPlayMode();
        yield return Run(9f, 1f, false, true);
        yield return new ExitPlayMode();
    }

    private static IEnumerator Run(float boxDistance, float charge, bool expectForward, bool pressInCritical)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い");

        // 距離で変わる操作は、プレイヤー挙動「引っ張る・投げる」のパターン B（2026/11/18。前はポーズ画面の設定）。通信なしなので、このPCに保存した値を使う
        PlayerBehaviorPattern[] before = PlayerBehavior.CopySaved();
        PlayerBehaviorPattern[] distance = PlayerBehavior.CopySaved();
        PlayerBehavior.Assign(distance, PlayerBehaviorStep.Pull, PlayerBehaviorPattern.B);
        PlayerBehavior.Save(distance);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        GameObject player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            player.transform.rotation = Quaternion.identity;

            // 正面（+Z）に箱を置く。空気の抵抗は宇宙ごみの物資と同じ
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

            // 当たったらすぐ押す（箱はまだ当たった位置にいる）
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!reel.IsActive && Time.realtimeSinceStartup < deadline)
            {
                yield return NextFrame();
            }
            Assert.That(reel.IsActive, Is.True, "光線が箱に当たらなかった");
            if (pressInCritical)
            {
                deadline = Time.realtimeSinceStartup + 5f;
                while (!reel.InCriticalWindow && Time.realtimeSinceStartup < deadline)
                {
                    yield return NextFrame();
                }
                Assert.That(reel.InCriticalWindow, Is.True, "クリティカルの距離に来なかった");
                // クリティカルは距離で取る：ビームの長さの上限 12m の 29〜32%（3.48〜3.84m）
                float pressedAt = Mathf.Abs(box.transform.position.z - player.transform.position.z);
                Debug.Log($"[SweeperDistanceModeTests] クリティカルの距離に来た：{pressedAt:F2}m");
                Assert.That(pressedAt, Is.InRange(3.3f, 4.0f), "クリティカルになった距離が、29〜32% ではない");
            }
            reel.Press();
            Assert.That(reel.IsActive, Is.False, "押しても飛ばなかった");

            // 飛んだ箱が落ち着くまで待つ
            float maxHeight = box.transform.position.y;
            float settle = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < settle)
            {
                maxHeight = Mathf.Max(maxHeight, box.transform.position.y);
                yield return NextFrame();
            }

            Vector3 end = box.transform.position;
            Debug.Log($"[SweeperDistanceModeTests] {boxDistance}m 先で押した：結果={reel.LastResult} 止まった所={end:F2} いちばん高い所={maxHeight:F2}m");

            if (pressInCritical)
            {
                // クリティカルの距離では、今までどおりのクリティカルプル（強さを掛けない）＝体の後ろまで飛ぶ
                Assert.That(reel.LastResult, Is.EqualTo("クリティカル"));
                Assert.That(end.z, Is.LessThan(0f), "クリティカルなのに、体の後ろまで飛んでいない");
            }
            else if (expectForward)
            {
                // 1.6m ÷ 12m ≒ 0.13 → 投げる強さ 1 − 0.13 ÷ 0.2 ≒ 0.33
                StringAssert.StartsWith("前", reel.LastResult);
                Assert.That(end.z, Is.GreaterThan(start.z + 0.3f), "前へ飛んでいない");
            }
            else if (boxDistance < 3.4f)
            {
                // 投げとクリティカルの間：いつも強さ 0.5
                Assert.That(reel.LastResult, Is.EqualTo("後ろ（強さ 0.50）"));
            }
            else
            {
                // 9m ÷ 12m＝0.75 → クリティカルより遠い引っ張り：0.2 ＋ 0.8 × (1 − 0.75) ÷ (1 − 0.32) ≒ 0.49
                StringAssert.StartsWith("後ろ", reel.LastResult);
                Assert.That(end.z, Is.LessThan(start.z - 0.5f), "手前へ引っ張られていない");
                Assert.That(end.z, Is.GreaterThan(0f), "強さを掛けたのに、いつもどおり体の後ろまで飛んだ");
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
