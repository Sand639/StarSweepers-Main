using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>ベルトコンベアの角（<see cref="SpaceJunkConveyorCorner"/>）の、流れる向きと範囲を確かめる。</summary>
public class SpaceJunkConveyorCornerTests
{
    private GameObject cornerObject;

    [TearDown]
    public void TearDown()
    {
        if (cornerObject != null) Object.DestroyImmediate(cornerObject);
    }

    private SpaceJunkConveyorCorner Make(bool turnRight, float speed = 3f, float rotY = 0f)
    {
        cornerObject = new GameObject("CornerTest");
        cornerObject.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        SpaceJunkConveyorCorner corner = cornerObject.AddComponent<SpaceJunkConveyorCorner>();
        SerializedObject so = new SerializedObject(corner);
        so.FindProperty("turnRight").boolValue = turnRight;
        so.FindProperty("speed").floatValue = speed;
        so.FindProperty("radius").floatValue = 2.75f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return corner;
    }

    private static void AssertFlow(SpaceJunkConveyorCorner corner, Vector3 at, Vector3 expected, string message)
    {
        Assert.That(corner.TryGetFlowVelocity(at, out Vector3 velocity), Is.True, message + "（範囲の中のはず）");
        Assert.That(Vector3.Distance(velocity, expected), Is.LessThan(0.01f), $"{message}：{velocity} のはずが {expected}");
    }

    [Test]
    public void RightTurn_EntersMovingWestAndLeavesMovingNorth()
    {
        SpaceJunkConveyorCorner corner = Make(true);
        AssertFlow(corner, new Vector3(0f, 0.1f, -1.5f), new Vector3(-3f, 0f, 0f), "入口（中心の南）では西へ");
        AssertFlow(corner, new Vector3(-1.5f, 0.1f, 0f), new Vector3(0f, 0f, 3f), "出口（中心の西）では北へ");
        float d = 1.5f / Mathf.Sqrt(2f);
        AssertFlow(corner, new Vector3(-d, 0.1f, -d), new Vector3(-3f, 0f, 3f) / Mathf.Sqrt(2f), "真ん中では北西へ");
    }

    [Test]
    public void LeftTurn_EntersMovingWestAndLeavesMovingSouth()
    {
        SpaceJunkConveyorCorner corner = Make(false);
        AssertFlow(corner, new Vector3(0f, 0.1f, 1.5f), new Vector3(-3f, 0f, 0f), "入口（中心の北）では西へ");
        AssertFlow(corner, new Vector3(-1.5f, 0.1f, 0f), new Vector3(0f, 0f, -3f), "出口（中心の西）では南へ");
    }

    [Test]
    public void RotatedRightTurn_FollowsTheRotation()
    {
        // ロビーの北の角と同じ（向き 90）：南から北へ入って、東へ出る
        SpaceJunkConveyorCorner corner = Make(true, 3f, 90f);
        AssertFlow(corner, new Vector3(-1.5f, 0.1f, 0f), new Vector3(0f, 0f, 3f), "入口（中心の西）では北へ");
        AssertFlow(corner, new Vector3(0f, 0.1f, 1.5f), new Vector3(3f, 0f, 0f), "出口（中心の北）では東へ");
    }

    [Test]
    public void NegativeSpeed_FlowsBackward()
    {
        SpaceJunkConveyorCorner corner = Make(true, -3f);
        AssertFlow(corner, new Vector3(0f, 0.1f, -1.5f), new Vector3(3f, 0f, 0f), "逆向きなら入口で東へ");
    }

    [Test]
    public void OutsideTheQuarter_IsNotCarried()
    {
        SpaceJunkConveyorCorner corner = Make(true);
        Assert.That(corner.TryGetFlowVelocity(new Vector3(1f, 0.1f, -1f), out _), Is.False, "中心の東側は範囲の外");
        Assert.That(corner.TryGetFlowVelocity(new Vector3(-1f, 0.1f, 1f), out _), Is.False, "中心の北側は範囲の外");
        Assert.That(corner.TryGetFlowVelocity(new Vector3(-2.2f, 0.1f, -2.2f), out _), Is.False, "外側の半径より外");
        Assert.That(corner.TryGetFlowVelocity(new Vector3(-1f, 2f, -1f), out _), Is.False, "高すぎる所");
    }
}
