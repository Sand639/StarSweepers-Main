using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **引っ張り合い（<see cref="SupplyTug"/>）の自動テスト。**（2026/11/24・小野田さんの依頼）
///
/// ・物理だけ：2つの「体」（ただの箱）で物資を引き合い、片方が離れる向きに動くと、**動かなかった側の光線が切れる**
/// ・物理だけ：どちらも動かなければ、時間切れで両方切れる
/// ・体2つ（通信なし）：A がつかんだ物を B もつかむと引っ張り合いになり、B が下がり続けると **A の光線が切れて、B がそのまま引き寄せる**
///
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SupplyTugTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";

    private static SupplyTug.Settings TestSettings => new SupplyTug.Settings
    {
        basePull = 6f,
        yankGain = 3f,
        swingGain = 0.6f,
        maxSwingTip = 12f,
        swingSide = 0.5f,
        drag = 3f,
        maxSpeed = 10f,
        snapStrain = 1.2f,
        strainRecovery = 1.5f,
        maxSeconds = 3f,
        winDistance = 1.5f,
        maxLength = 14f,
        handHeight = 0f,
    };

    [UnityTest]
    public IEnumerator Physics_StrongerSideWins()
    {
        yield return new EnterPlayMode();
        yield return RunPhysics(true);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Physics_TieTimesOut()
    {
        yield return new EnterPlayMode();
        yield return RunPhysics(false);
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Players_BackingAwayWins()
    {
        yield return new EnterPlayMode();
        yield return RunPlayers();
        yield return new ExitPlayMode();
    }

    /// <summary>物理だけ。<paramref name="challengerYanks"/> が true なら、後から来た側が離れる向きに歩き続ける。</summary>
    private static IEnumerator RunPhysics(bool challengerYanks)
    {
        GameObject holder = new GameObject("Holder");
        GameObject challenger = new GameObject("Challenger");
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            holder.transform.position = new Vector3(-4f, 0f, 0f);
            challenger.transform.position = new Vector3(4f, 0f, 0f);
            box.transform.position = new Vector3(0f, 1f, 0f);
            Rigidbody body = box.AddComponent<Rigidbody>();
            body.useGravity = false;
            HookableObject hookable = box.AddComponent<HookableObject>();
            yield return null;

            int result = -1;
            SupplyTug tug = SupplyTug.Begin(hookable, holder.transform, challenger.transform, TestSettings, r => result = r);
            Assert.That(tug, Is.Not.Null);

            // 画面なしのテストでは Time.deltaTime が当てにならないので、実際の時間で位置を決める（AIの申し送り 2026/11/4 の行）
            float start = Time.realtimeSinceStartup;
            float until = start + 5f;
            Vector3 challengerStart = challenger.transform.position;
            float maxHolderStrain = 0f;
            while (result < 0 && Time.realtimeSinceStartup < until)
            {
                if (challengerYanks)
                {
                    // 物資から離れる向き（+X）に、ゆっくり歩き続ける
                    challenger.transform.position = challengerStart + Vector3.right * 2.5f * (Time.realtimeSinceStartup - start);
                }
                maxHolderStrain = Mathf.Max(maxHolderStrain, tug.StrainOf(0));
                yield return null;
            }

            Debug.Log($"[SupplyTugTests] 物理だけ yank={challengerYanks} 結果={result} 箱={box.transform.position:F2} " +
                      $"かかった秒数={Time.realtimeSinceStartup - start:F2} つかんでいた側のたまり最大={maxHolderStrain:F2} 後から来た側={challenger.transform.position:F2}");
            Assert.That(result, Is.EqualTo(challengerYanks ? SupplyTug.HolderLost : SupplyTug.BothLost),
                challengerYanks ? "下がり続けた側が勝っていない" : "どちらも動かないのに、時間切れにならなかった");
        }
        finally
        {
            Object.Destroy(holder);
            Object.Destroy(challenger);
            Object.Destroy(box);
        }

        yield return null;
    }

    /// <summary>体2つ（通信なし）。A がつかみ、B も同じ物をつかんで、B が下がり続ける。</summary>
    private static IEnumerator RunPlayers()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.That(prefab, Is.Not.Null, "新しい体のプレハブが無い");

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        GameObject a = Object.Instantiate(prefab, new Vector3(-4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        GameObject b = Object.Instantiate(prefab, new Vector3(4f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            // 体の動きはテストが決める
            a.GetComponent<SweeperMover>().enabled = false;
            b.GetComponent<SweeperMover>().enabled = false;
            box.transform.position = new Vector3(0f, 0.6f, 0f);
            box.AddComponent<Rigidbody>();
            HookableObject hookable = box.AddComponent<HookableObject>();
            yield return null;

            SweeperReel reelA = a.GetComponent<SweeperReel>();
            SweeperReel reelB = b.GetComponent<SweeperReel>();
            reelA.Begin(hookable);
            Assert.That(reelA.IsActive, Is.True, "A がつかめなかった");

            // つかんだ直後（Grab Lock Seconds）は、B は引っ張り合いを挑めない（2026/11/27）
            Assert.That(reelB.CanHook(hookable), Is.False, "A がつかんだ直後なのに、B が引っ張り合いを挑めてしまう");
            float lockUntil = Time.realtimeSinceStartup + 5f;
            while (hookable.IsProtected && Time.realtimeSinceStartup < lockUntil)
            {
                yield return null;
            }
            Assert.That(hookable.IsProtected, Is.False, "つかんだ直後のロックが解けない");

            Assert.That(reelB.CanHook(hookable), Is.True, "A がつかんでいる物を、B が引っ張り合いでつかめない");

            reelB.Begin(hookable);
            Assert.That(reelB.IsActive && reelB.IsTugging && reelA.IsTugging, Is.True, "引っ張り合いにならなかった");

            float start = Time.realtimeSinceStartup;
            float until = start + 5f;
            Vector3 bStart = b.transform.position;
            while (reelA.IsActive && Time.realtimeSinceStartup < until)
            {
                b.transform.position = bStart + Vector3.right * 2.5f * (Time.realtimeSinceStartup - start);
                yield return null;
            }

            Debug.Log($"[SupplyTugTests] 体2つ：A={reelA.LastResult} B 続けている={reelB.IsActive} B 引っ張り合い中={reelB.IsTugging}");
            Assert.That(reelA.IsActive, Is.False, "A の光線が切れていない");
            Assert.That(reelA.LastResult, Is.EqualTo("引っ張り合いで切れた"));
            Assert.That(reelB.IsActive, Is.True, "勝った B が物資を離してしまった");
            Assert.That(reelB.IsTugging, Is.False, "勝った B が、いつもの引き寄せに戻っていない");
            Assert.That(hookable.IsHooked, Is.True, "B がつかんでいるのに、つかまれている印が消えた");
            Assert.That(box.GetComponent<Rigidbody>().useGravity, Is.False, "B がつかんでいるのに、重力が戻っている");

            // 負けた A は、B が持っている間は手を出せない（取った直後のロックが解けたあとも。2026/11/27）
            lockUntil = Time.realtimeSinceStartup + 5f;
            while (hookable.IsProtected && Time.realtimeSinceStartup < lockUntil)
            {
                yield return null;
            }
            Assert.That(reelA.CanHook(hookable), Is.False, "引っ張り合いに負けた A が、B の持っている物をまたつかめてしまう");

            reelB.Cancel();
            Assert.That(box.GetComponent<Rigidbody>().useGravity, Is.True, "B が離したのに、重力が戻っていない");
            Assert.That(reelA.CanHook(hookable), Is.True, "B が離したのに、A のロックが解けていない");
        }
        finally
        {
            Object.Destroy(a);
            Object.Destroy(b);
            Object.Destroy(box);
            Object.Destroy(ground);
        }

        yield return null;
    }
}
