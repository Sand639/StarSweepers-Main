using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **プレイヤーの動きのアニメーション（狙い歩き・撃つ・引っ張る・投げる・落ちる・スタン）が切り替わるかの自動テスト。**（2026/11/25・小野田さんの依頼。2026/11/26 にスタンを追加）
///
/// 通信なしで体のプレハブを置き、Animator がいまどの状態にいるかを見る。見た目（動きが自然か）は人が確かめる。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperAnimationTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    [UnityTest]
    public IEnumerator ShotCalls_SwitchAimShootPull()
    {
        yield return new EnterPlayMode();
        yield return RunShotCalls();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NearPress_PlaysThrow()
    {
        yield return new EnterPlayMode();
        yield return RunThrow();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Stun_PlaysFallThenRise()
    {
        yield return new EnterPlayMode();
        yield return RunStun();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FallingOffGround_PlaysDropThenLoop()
    {
        yield return new EnterPlayMode();
        yield return RunFall();
        yield return new ExitPlayMode();
    }

    private static IEnumerator RunShotCalls()
    {
        GameObject ground = MakeGround();
        GameObject player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath), Vector3.zero, Quaternion.identity);
        try
        {
            Animator animator = player.GetComponentInChildren<Animator>();
            SweeperAnimation anim = player.GetComponent<SweeperAnimation>();
            yield return Wait(0.3f);
            Assert.That(State(animator), Is.EqualTo("Idle"), "初めは Idle のはず");

            anim.PlayAim();
            yield return WaitState(animator, "AimWalk", 0.5f);

            anim.PlayShoot();
            yield return WaitState(animator, "Shoot", 0.5f);
            // Shoot（1 秒）が終わったら AimWalk に戻る
            yield return WaitState(animator, "AimWalk", 2f);

            anim.PlayPull();
            yield return WaitState(animator, "Pull", 0.5f);
            yield return Wait(2.5f);
            Assert.That(State(animator), Is.EqualTo("Pull"), "Pull はくり返し続くはず");

            anim.PlayIdle();
            yield return WaitState(animator, "Idle", 0.5f);
        }
        finally
        {
            Object.Destroy(player);
            Object.Destroy(ground);
        }
    }

    private static IEnumerator RunThrow()
    {
        PlayerBehaviorPattern[] before = PlayerBehavior.CopySaved();
        PlayerBehaviorPattern[] normal = PlayerBehavior.CopySaved();
        PlayerBehavior.Assign(normal, PlayerBehaviorStep.Pull, PlayerBehaviorPattern.A);
        PlayerBehavior.Save(normal);

        GameObject ground = MakeGround();
        GameObject player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath), Vector3.zero, Quaternion.identity);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            player.GetComponent<SweeperMover>().enabled = false;
            Animator animator = player.GetComponentInChildren<Animator>();
            // テストの場面にはカメラが無く、体が「見えていない」扱いで骨が動かないので、常に動かす
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            box.transform.position = new Vector3(0f, 0.5f, 2f);
            box.transform.localScale = Vector3.one * 0.5f;
            box.AddComponent<Rigidbody>();
            HookableObject hookable = box.AddComponent<HookableObject>();
            yield return Wait(0.3f);

            SweeperReel reel = player.GetComponent<SweeperReel>();
            reel.Begin(hookable);
            yield return Wait(0.05f);
            reel.Press();
            Assert.That(reel.IsCarrying, Is.True, "近くで押したのに、両手で持っていない");
            yield return WaitState(animator, "Throw", 0.5f);

            // 持ち上げている間に物資がどこまで上がったか（両手に合わせて動いているか）
            float highest = box.transform.position.y;
            float until = Time.realtimeSinceStartup + 3f;
            while (reel.IsActive && Time.realtimeSinceStartup < until)
            {
                highest = Mathf.Max(highest, box.transform.position.y);
                yield return null;
            }
            Debug.Log($"[SweeperAnimationTests] 投げた結果={reel.LastResult} 持っている間のいちばん高い所={highest:F2}m");
            Assert.That(reel.LastResult, Is.EqualTo("前"));
            // 両手に合わせて、胸の高さ（Carry Height 0.8m）くらいまで持ち上がる（両手の真ん中で約 0.8m）
            Assert.That(highest, Is.GreaterThan(0.7f), "投げる前に、物資が両手と一緒に持ち上がっていない");
            // 投げたあとも、振り切るまで Throw のまま
            Assert.That(State(animator), Is.EqualTo("Throw"), "投げた直後に Throw が切れている");
            yield return WaitState(animator, "Idle", 2f);
        }
        finally
        {
            PlayerBehavior.Save(before);
            Object.Destroy(player);
            Object.Destroy(box);
            Object.Destroy(ground);
        }
    }

    private static IEnumerator RunStun()
    {
        GameObject ground = MakeGround();
        GameObject player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath), Vector3.zero, Quaternion.identity);
        try
        {
            Animator animator = player.GetComponentInChildren<Animator>();
            yield return Wait(0.3f);
            Assert.That(State(animator), Is.EqualTo("Idle"));

            // 短いスタン（1 秒）と長いスタン（3 秒）。どちらも、倒れる（Fall。頭の構えの部分は飛ばす）→ 起き上がる（Rise）で、
            // スタンが終わる瞬間には起き上がり終わっている。撃つ動き（Shoot）は出ない
            foreach (float seconds in new[] { 1f, 3f })
            {
                PlayerStun stun = player.GetComponent<PlayerStun>();
                stun.Stun(seconds, false);
                yield return WaitState(animator, "Fall", 0.3f);
                yield return null;
                AnimatorStateInfo fall = Info(animator);
                // AN_Fall（2.1 秒）の 0.9 秒目から流すので、正規化時間はすぐ 0.4 を超えている
                Assert.That(fall.normalizedTime, Is.GreaterThan(0.4f), "倒れるアニメーションの頭（撃つように見える構え）を飛ばしていない");

                bool sawShoot = false;
                float until = Time.realtimeSinceStartup + seconds + 1f;
                while (State(animator) != "Rise" && Time.realtimeSinceStartup < until)
                {
                    sawShoot |= State(animator) == "Shoot";
                    yield return null;
                }
                Assert.That(State(animator), Is.EqualTo("Rise"), "起き上がらない");
                Assert.That(stun.IsStunned, Is.True, "スタンが終わってから起き上がり始めている");
                float riseStartRemaining = stun.Remaining;

                while (stun.IsStunned && Time.realtimeSinceStartup < until)
                {
                    sawShoot |= State(animator) == "Shoot";
                    yield return null;
                }
                AnimatorStateInfo end = Info(animator);
                string endState = State(animator);
                Debug.Log($"[SweeperAnimationTests] スタン {seconds} 秒：起き上がり始め＝残り {riseStartRemaining:F2} 秒、スタンが終わった瞬間＝{endState} {end.normalizedTime:F2}");
                Assert.That(sawShoot, Is.False, "スタン中に撃つ動きが出た");
                Assert.That(endState == "Idle" || (endState == "Rise" && end.normalizedTime > 0.85f), Is.True,
                    $"スタンが終わった瞬間に起き上がり終わっていない（{endState} {end.normalizedTime:F2}）");
                yield return WaitState(animator, "Idle", 1f);
            }
        }
        finally
        {
            Object.Destroy(player);
            Object.Destroy(ground);
        }
    }

    private static IEnumerator RunFall()
    {
        GameObject ground = MakeGround();
        GameObject player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath), new Vector3(0f, 0.1f, 0f), Quaternion.identity);
        try
        {
            Animator animator = player.GetComponentInChildren<Animator>();
            yield return Wait(0.5f);
            Assert.That(State(animator), Is.EqualTo("Idle"));

            // 足場を消して落とす
            Object.Destroy(ground);
            ground = null;
            yield return WaitState(animator, "Drop", 1.5f);
            yield return WaitState(animator, "DropLoot", 1.5f);
            Debug.Log($"[SweeperAnimationTests] 落ちている：高さ={player.transform.position.y:F2}");

            // 下に足場を置き直して着かせると、Idle に戻る
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = new Vector3(0f, -2.9f, 0f);
            controller.enabled = true;
            ground = MakeGround();
            ground.transform.position = new Vector3(0f, -3f, 0f);
            yield return WaitState(animator, "Idle", 2f);
        }
        finally
        {
            Object.Destroy(player);
            if (ground != null) Object.Destroy(ground);
        }
    }

    private static GameObject MakeGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        return ground;
    }

    private static readonly string[] Names = { "Idle", "Walk", "AimWalk", "Shoot", "Pull", "Throw", "Drop", "DropLoot", "Fall", "Rise" };

    /// <summary>Animator がいまいる状態（移り変わり中なら行き先）。</summary>
    private static AnimatorStateInfo Info(Animator animator)
    {
        return animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
    }

    /// <summary>Animator がいまいる状態の名前（移り変わり中なら行き先）。</summary>
    private static string State(Animator animator)
    {
        AnimatorStateInfo info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        foreach (string name in Names)
        {
            if (info.IsName(name)) return name;
        }
        return "?";
    }

    private static IEnumerator WaitState(Animator animator, string name, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (State(animator) != name && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        Assert.That(State(animator), Is.EqualTo(name), $"{seconds} 秒たっても {name} にならない");
    }

    /// <summary>実際の時間で待つ（ゲームのフレームが進むように1フレームずつ待つ）。</summary>
    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
}
