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

    /// <summary>
    /// 爆弾を鞭でつかむと、導火線に火がつく（2026/11/18。新しい光線では火がつかず、爆弾が爆発しなかった）。
    /// </summary>
    [UnityTest]
    public IEnumerator Bomb_GrabLightsFuse()
    {
        yield return new EnterPlayMode();
        yield return Run(new Vector3(0f, 0.5f, 5f), false, true, true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Setting_ChangesStyle()
    {
        yield return new EnterPlayMode();
        yield return RunSetting();
        yield return new ExitPlayMode();
    }

    /// <summary>
    /// プレイヤー挙動の「固定の組」の決まり（2026/11/18）：狙うを変えると発射も、発射を変えると狙うも同じパターンになる。
    /// 引っ張る・投げるは A しか無いので、B にはならない。食い違った値は、前の段階（狙う）に合わせて直る。
    /// </summary>
    [Test]
    public void Behavior_FixedPairs()
    {
        PlayerBehaviorPattern[] values = new PlayerBehaviorPattern[PlayerBehavior.StepCount];

        PlayerBehavior.Assign(values, PlayerBehaviorStep.Aim, PlayerBehaviorPattern.B);
        Assert.That(values[(int)PlayerBehaviorStep.Fire], Is.EqualTo(PlayerBehaviorPattern.B), "狙うを B にしたのに、発射が B にならない");

        PlayerBehavior.Assign(values, PlayerBehaviorStep.Fire, PlayerBehaviorPattern.A);
        Assert.That(values[(int)PlayerBehaviorStep.Aim], Is.EqualTo(PlayerBehaviorPattern.A), "発射を A にしたのに、狙うが A にならない");

        PlayerBehavior.Assign(values, PlayerBehaviorStep.Pull, PlayerBehaviorPattern.B);
        Assert.That(values[(int)PlayerBehaviorStep.Pull], Is.EqualTo(PlayerBehaviorPattern.A), "引っ張るに無いパターン（B）が入った");
        Assert.That(values[(int)PlayerBehaviorStep.Throw], Is.EqualTo(PlayerBehaviorPattern.A));

        values[(int)PlayerBehaviorStep.Aim] = PlayerBehaviorPattern.B;
        values[(int)PlayerBehaviorStep.Fire] = PlayerBehaviorPattern.A;
        values[(int)PlayerBehaviorStep.Throw] = PlayerBehaviorPattern.B;
        PlayerBehavior.Normalize(values);
        Assert.That(values[(int)PlayerBehaviorStep.Fire], Is.EqualTo(PlayerBehaviorPattern.B), "食い違った組が、狙うに合わせて直っていない");
        Assert.That(values[(int)PlayerBehaviorStep.Throw], Is.EqualTo(PlayerBehaviorPattern.A), "投げるに無いパターンが A に直っていない");
    }

    /// <summary>
    /// プレイヤー挙動「狙う・発射」（通信なしなので、このPCに保存した値）が体に入ること、変えると次に押したときから変わること、
    /// 合わせるのをやめるとインスペクターの撃ち方に戻ることを確かめる。値は PC に保存されるので、終わったら元に戻す。
    /// </summary>
    private static IEnumerator RunSetting()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い（メニューで作る）");

        PlayerBehaviorPattern[] before = PlayerBehavior.CopySaved();
        GameObject player = null;
        try
        {
            SaveShot(PlayerBehaviorPattern.B);
            player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            SweeperShooter shooter = player.GetComponent<SweeperShooter>();
            Assert.That(shooter.Style, Is.EqualTo(SweeperShooter.ShotStyle.Aim), "挙動が B（溜め）なのに、体が溜めになっていない");

            // 押した瞬間と同じ合わせ方（挙動が変わっていたら合わせる）
            SaveShot(PlayerBehaviorPattern.A);
            MethodInfo apply = typeof(SweeperShooter).GetMethod("ApplySetting", BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Invoke(shooter, new object[] { false });
            Assert.That(shooter.Style, Is.EqualTo(SweeperShooter.ShotStyle.Whip), "挙動を A（鞭）にしたのに、鞭になっていない");

            // 合わせるのをやめると、プレハブの撃ち方（Whip）に戻り、挙動を変えても変わらない
            SaveShot(PlayerBehaviorPattern.B);
            shooter.FollowSettings = false;
            Assert.That(shooter.Style, Is.EqualTo(SweeperShooter.ShotStyle.Whip), "合わせるのをやめたのに、プレハブの撃ち方に戻っていない");
            yield return NextFrame();
            Assert.That(shooter.Style, Is.EqualTo(SweeperShooter.ShotStyle.Whip));
        }
        finally
        {
            PlayerBehavior.Save(before);
            if (player != null)
            {
                Object.Destroy(player);
            }
        }

        yield return null;
    }

    /// <summary>このPCに保存したプレイヤー挙動の「狙う」（発射も固定の組で同じになる）を変える。</summary>
    private static void SaveShot(PlayerBehaviorPattern pattern)
    {
        PlayerBehaviorPattern[] values = PlayerBehavior.CopySaved();
        PlayerBehavior.Assign(values, PlayerBehaviorStep.Aim, pattern);
        PlayerBehavior.Save(values);
    }

    /// <summary>中身。再生を始めてから呼ぶ別の関数にしてある（SweeperBendTests と同じ理由）。</summary>
    private static IEnumerator Run(Vector3 boxPosition, bool swing, bool expectGrab, bool bomb = false)
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
            ExplosiveObject explosive = null;
            if (bomb)
            {
                explosive = box.AddComponent<ExplosiveObject>();
                // 点滅させる見た目は空にしておく（テストの箱には要らない）
                typeof(ExplosiveObject)
                    .GetField("blinkRenderers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(explosive, new Renderer[0]);
            }
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
                if (explosive != null)
                {
                    Assert.That(explosive.FuseLit, Is.True, "爆弾をつかんだのに、導火線に火がついていない");
                }
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
