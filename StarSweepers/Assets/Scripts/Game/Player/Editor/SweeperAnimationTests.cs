using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **プレイヤーの動きのアニメーション（狙い歩き・撃つ・引っ張る・投げる・落ちる）が切り替わるかの自動テスト。**（2026/11/25・小野田さんの依頼）
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
    public IEnumerator PressBack_PullPeaksAtLaunch()
    {
        yield return new EnterPlayMode();
        yield return RunPullBack();
        yield return new ExitPlayMode();
    }

    [Test]
    public void Muzzle_IsOnLeftHandGun()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        SweeperShooter shooter = prefab.GetComponent<SweeperShooter>();
        Transform muzzle = new SerializedObject(shooter).FindProperty("muzzle").objectReferenceValue as Transform;
        Assert.That(muzzle, Is.Not.Null, "SweeperShooter の Muzzle が空");
        Assert.That(muzzle.parent.name, Is.EqualTo("Gun"), "Muzzle は銃の子のはず");
        Assert.That(muzzle.parent.parent.name, Is.EqualTo("LCT_Tool"), "銃は左手の道具の位置（LCT_Tool）に付いているはず");
        Assert.That(muzzle.parent.parent.parent.name, Is.EqualTo("JNT_Hand_L"), "銃は左手に付いているはず");
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

            // 巻き取っている間は構えたまま（2026/11/26：前は Pull をくり返していた）
            anim.PlayPull();
            yield return Wait(0.3f);
            Assert.That(State(animator), Is.EqualTo("AimWalk"), "巻き取っている間は AimWalk のはず");

            // 引っ張った：Pull を途中から1回流し、終わったら Idle に戻る
            anim.PlayIdle();
            anim.PlayPullRelease(0f);
            yield return WaitState(animator, "Pull", 0.5f);
            yield return Wait(0.4f);
            Assert.That(State(animator), Is.EqualTo("Pull"), "Pull がすぐ切れている");
            yield return WaitState(animator, "Idle", 2.5f);
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

    /// <summary>離れた物資を巻き取っている途中で押す → 後ろへ飛ぶ瞬間に、Pull の「いちばん引く所」の近くまで来ているか。</summary>
    private static IEnumerator RunPullBack()
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
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            box.transform.position = new Vector3(0f, 0.5f, 6f);
            box.transform.localScale = Vector3.one * 0.5f;
            box.AddComponent<Rigidbody>();
            HookableObject hookable = box.AddComponent<HookableObject>();
            yield return Wait(0.3f);

            SweeperReel reel = player.GetComponent<SweeperReel>();
            SweeperAnimation anim = player.GetComponent<SweeperAnimation>();
            reel.Begin(hookable);
            anim.PlayPull();
            yield return Wait(0.2f);
            Assert.That(State(animator), Is.EqualTo("AimWalk"), "巻き取っている間は AimWalk のはず");

            reel.Press();
            Assert.That(reel.IsActive, Is.False, "押したのに、後ろへ飛ばしていない");
            anim.PlayIdle();
            yield return WaitState(animator, "Pull", 0.3f);

            // 押してから 0.1 秒（Pull Lead Seconds）ほどで、いちばん引く所（0.95 秒）に来る
            AnimatorStateInfo info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            float seconds = info.normalizedTime * info.length;
            Debug.Log($"[SweeperAnimationTests] 引っ張った結果={reel.LastResult} 押した直後の Pull の位置={seconds:F2} 秒");
            Assert.That(seconds, Is.InRange(0.8f, 1.0f), "Pull が「いちばん引く所」の少し前から流れていない");

            // すぐ Idle に戻されず、Pull が続いている（2026/11/27：つなぎの途中で Idle に戻されて見えなかった不具合）
            yield return Wait(0.4f);
            Assert.That(State(animator), Is.EqualTo("Pull"), "引っ張った直後に Pull が切れて、見えていない");

            // 振り切ったら Idle に戻る
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

    private static readonly string[] Names = { "Idle", "Walk", "AimWalk", "Shoot", "Pull", "Throw", "Drop", "DropLoot" };

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
