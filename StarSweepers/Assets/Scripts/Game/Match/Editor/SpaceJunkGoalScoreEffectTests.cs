using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>ゴールの得点の演出（<see cref="SpaceJunkGoalScoreEffect"/>）の、大きさの測り方と得点の知らせを確かめる。</summary>
public class SpaceJunkGoalScoreEffectTests
{
    private readonly List<GameObject> made = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in made)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
        made.Clear();
    }

    /// <summary>ステージと同じ形のゴール（判定 4×5×5m、色を塗る床 5m×4m を 90° 回して置く）を作る。</summary>
    private SpaceJunkGoal MakePadGoal(Vector3 position, float rotY)
    {
        var root = new GameObject("GoalTest");
        made.Add(root);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotY, 0f));
        var box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4f, 5f, 5f);
        root.AddComponent<NetworkObject>();
        var goal = root.AddComponent<SpaceJunkGoal>();

        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(pad.GetComponent<Collider>());
        pad.transform.SetParent(root.transform, false);
        pad.transform.localPosition = new Vector3(0f, -2.45f, 0f);
        pad.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        pad.transform.localScale = new Vector3(5f, 0.1f, 4f);

        var so = new SerializedObject(goal);
        so.FindProperty("padRenderer").objectReferenceValue = pad.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return goal;
    }

    [Test]
    public void GoalArea_FitsThePadAndStandsOnTheBottomOfTheGoal()
    {
        SpaceJunkGoal goal = MakePadGoal(new Vector3(10f, 2.5f, -3f), 0f);

        Assert.That(SpaceJunkGoalScoreEffect.TryGetGoalArea(goal, 0.1f, out Vector3 basePoint, out Quaternion yaw, out Vector2 size), Is.True);
        Assert.That(Vector2.Distance(size, new Vector2(4.2f, 5.2f)), Is.LessThan(0.01f), $"床（4m×5m）＋すき間 0.1m の両側のはずが {size}");
        Assert.That(Vector3.Distance(basePoint, new Vector3(10f, 0f, -3f)), Is.LessThan(0.01f), $"足もとは判定の底の真ん中のはずが {basePoint}");
        Assert.That(Quaternion.Angle(yaw, Quaternion.identity), Is.LessThan(0.1f));
    }

    [Test]
    public void GoalArea_FollowsTheGoalRotation()
    {
        // ゴールごと 90° 回すと、ゴールから見た幅と奥行きは変わらない（世界では入れ替わる）
        SpaceJunkGoal goal = MakePadGoal(new Vector3(0f, 2.5f, 0f), 90f);

        Assert.That(SpaceJunkGoalScoreEffect.TryGetGoalArea(goal, 0f, out _, out Quaternion yaw, out Vector2 size), Is.True);
        Assert.That(Vector2.Distance(size, new Vector2(4f, 5f)), Is.LessThan(0.01f), $"ゴールから見た大きさは 4m×5m のはずが {size}");
        Assert.That(Mathf.DeltaAngle(yaw.eulerAngles.y, 90f), Is.EqualTo(0f).Within(0.1f));
    }

    [Test]
    public void GoalArea_StandsOnTheFloorAroundWhenItIsHigher()
    {
        // 電車マップ2と同じ：ゴールの判定の底は 0m、まわりの床の上面は 0.25m
        SpaceJunkGoal goal = MakePadGoal(new Vector3(0f, 2.5f, 0f), 0f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        made.Add(floor);
        floor.transform.position = new Vector3(0f, -0.25f, 0f);
        floor.transform.localScale = new Vector3(20f, 1f, 20f);
        Physics.SyncTransforms();

        Assert.That(SpaceJunkGoalScoreEffect.TryGetGoalArea(goal, 0f, out Vector3 basePoint, out _, out _), Is.True);
        Assert.That(basePoint.y, Is.EqualTo(0.25f).Within(0.01f), "まわりの床の上面に合わせるはず");
    }

    [Test]
    public void ShowScored_TellsTheEffect()
    {
        SpaceJunkGoal goal = MakePadGoal(Vector3.zero, 0f);
        SpaceJunkGoal told = null;
        System.Action<SpaceJunkGoal> listener = g => told = g;
        SpaceJunkGoal.Scored += listener;
        try
        {
            goal.ShowScored();
        }
        finally
        {
            SpaceJunkGoal.Scored -= listener;
        }

        Assert.That(told, Is.SameAs(goal), "得点の知らせに、そのゴールが渡るはず");
    }

    [Test]
    public void Play_KeepsAtMostThreeBurstsPerGoal()
    {
        SpaceJunkGoal goal = MakePadGoal(Vector3.zero, 0f);
        var effectObject = new GameObject("EffectTest");
        made.Add(effectObject);
        var effect = effectObject.AddComponent<SpaceJunkGoalScoreEffect>();

        for (int i = 0; i < 5; i++)
        {
            effect.Play(goal, i * 0.1f);
        }

        Assert.That(effect.PlayCount, Is.EqualTo(5), "出した回数は全部数える");
        Assert.That(effect.ActiveCount, Is.EqualTo(3), "1つのゴールで同時に光るのは3つまで（Max Per Goal の初期値）");
    }
}
