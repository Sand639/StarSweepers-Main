using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>高単価デブリのルーレットUI（<see cref="HighValueRouletteUI"/>）の止まり方と、プレハブの割り当てを確かめる。</summary>
public class HighValueRouletteUITests
{
    [Test]
    public void ChosenItem_StopsAtTheBottomExactlyAtSpinSeconds()
    {
        const float spinSeconds = 2f;
        int count = HighValueRouletteUI.ReelCount(14f, spinSeconds);

        float position = DebrisSetUI.ReelPosition(spinSeconds, spinSeconds, count);
        float angle = HighValueRouletteUI.RailAngle(position, count, 45f);
        Vector2 point = HighValueRouletteUI.RailPoint(angle, 200f);

        Assert.That(angle, Is.EqualTo(0f), "2秒ちょうどに、決まる物資が真下に来ない");
        Assert.That(point.x, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(point.y, Is.EqualTo(-200f).Within(1e-4f), "真下はフレームの中心から半径ぶん下");

        float before = HighValueRouletteUI.RailAngle(DebrisSetUI.ReelPosition(spinSeconds - 0.1f, spinSeconds, count), count, 45f);
        Assert.That(before, Is.LessThan(0f), "止まる少し前は、決まる物資が真下より左にあるはず");
    }

    [Test]
    public void Items_FlowFromLeftToRightAndSlowDown()
    {
        const float spinSeconds = 2f;
        int count = HighValueRouletteUI.ReelCount(14f, spinSeconds);
        float lastAngle = float.MinValue;
        float lastMove = float.MaxValue;

        for (float t = 0f; t <= spinSeconds + 1e-4f; t += 0.25f)
        {
            float angle = HighValueRouletteUI.RailAngle(DebrisSetUI.ReelPosition(t, spinSeconds, count), count, 45f);
            Assert.That(angle, Is.GreaterThan(lastAngle), $"{t:0.00} 秒で、物資が右へ進んでいない");

            if (lastAngle > float.MinValue)
            {
                float move = angle - lastAngle;
                Assert.That(move, Is.LessThan(lastMove), $"{t:0.00} 秒のあたりで遅くなっていない");
                lastMove = move;
            }

            lastAngle = angle;
        }

        // 角度がプラスなら右、マイナスなら左
        Assert.That(HighValueRouletteUI.RailPoint(45f, 200f).x, Is.GreaterThan(0f));
        Assert.That(HighValueRouletteUI.RailPoint(-45f, 200f).x, Is.LessThan(0f));
    }

    [Test]
    public void Reel_HasTheChosenKindAndNeighborsDiffer()
    {
        var reel = new int[20];
        HighValueRouletteUI.FillReel(reel, 15, 2, avoid => (avoid + 1) % HighValueRouletteUI.ItemKinds);

        Assert.That(reel[15], Is.EqualTo(2), "決まる場所に、選ばれた物資が入っていない");
        for (int i = 1; i < reel.Length; i++)
        {
            Assert.That(reel[i], Is.Not.EqualTo(reel[i - 1]), $"{i - 1} 番目と {i} 番目が同じ物資");
        }
    }

    [Test]
    public void Prefab_IsInResourcesAndWired()
    {
        GameObject prefab = Resources.Load<GameObject>(HighValueRouletteUI.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/HighValueRouletteUI.prefab が無い（ゲームの開始時に読み込む）");

        var so = new SerializedObject(prefab.GetComponent<HighValueRouletteUI>());
        Assert.That(so.FindProperty("testMode").boolValue, Is.False, "プレハブのテストモードがオンになっている");
        Assert.That(so.FindProperty("spinSeconds").floatValue, Is.EqualTo(2f), "物資が決まるまでの秒数が2秒でない");

        foreach (string part in new[] { "content", "wheel", "frame", "frameSprite" })
        {
            Assert.That(so.FindProperty(part).objectReferenceValue, Is.Not.Null, $"{part} が空");
        }

        SerializedProperty rail = so.FindProperty("railItems");
        Assert.That(rail.arraySize, Is.EqualTo(HighValueRouletteUI.RailItems));
        for (int i = 0; i < rail.arraySize; i++)
        {
            Assert.That(rail.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"レールの物資{i}が空");
        }

        // 物資の画像は、デブリセットのUIと同じ画像（1回差し替えれば両方に効く）
        var debrisSet = new SerializedObject(Resources.Load<GameObject>(DebrisSetUI.PrefabResourcePath).GetComponent<DebrisSetUI>());
        SerializedProperty items = so.FindProperty("itemSprites");
        SerializedProperty debrisItems = debrisSet.FindProperty("itemSprites");
        Assert.That(items.arraySize, Is.EqualTo(HighValueRouletteUI.ItemKinds), "物資の画像の数が種類の数と違う");
        for (int i = 0; i < items.arraySize; i++)
        {
            Assert.That(items.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"物資{i + 1}の画像が空");
            Assert.That(items.GetArrayElementAtIndex(i).objectReferenceValue,
                Is.SameAs(debrisItems.GetArrayElementAtIndex(i).objectReferenceValue), $"物資{i + 1}の画像が、デブリセットのUIと違う");
        }
    }
}
