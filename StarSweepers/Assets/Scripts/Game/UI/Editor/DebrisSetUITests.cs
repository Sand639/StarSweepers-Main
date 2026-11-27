using NUnit.Framework;
using UnityEngine;

/// <summary>デブリセットのUI（<see cref="DebrisSetUI"/>）のスロットの止まり方と、チェックの付け方を確かめる。</summary>
public class DebrisSetUITests
{
    [Test]
    public void LastLane_StopsExactlyAtSpinSeconds()
    {
        for (int lanes = 1; lanes <= DebrisSetUI.MaxSlots; lanes++)
        {
            Assert.That(DebrisSetUI.LaneStopTime(lanes - 1, lanes, 1f, 0.5f), Is.EqualTo(1f),
                $"{lanes} 個のとき、いちばん右が 1 秒ちょうどに止まらない");
        }

        Assert.That(DebrisSetUI.LaneStopTime(4, 5, 2.5f, 0.3f), Is.EqualTo(2.5f), "秒数を変えたときに合わない");
    }

    [Test]
    public void Lanes_StopFromLeftToRight()
    {
        const int lanes = 6;
        float previous = -1f;
        for (int lane = 0; lane < lanes; lane++)
        {
            float stop = DebrisSetUI.LaneStopTime(lane, lanes, 1f, 0.5f);
            Assert.That(stop, Is.GreaterThan(previous), $"{lane} 番目が、左のマスより先に止まる");
            previous = stop;
        }

        Assert.That(DebrisSetUI.LaneStopTime(0, lanes, 1f, 0.5f), Is.EqualTo(0.5f).Within(1e-6f), "いちばん左の止まる時刻が割合どおりでない");
    }

    [Test]
    public void Reel_SlowsDownAndLandsOnTheLastItem()
    {
        const int count = 8;
        Assert.That(DebrisSetUI.ReelPosition(0f, 1f, count), Is.EqualTo(0f));
        Assert.That(DebrisSetUI.ReelPosition(1f, 1f, count), Is.EqualTo((float)count), "止まる時刻に、最後の物資（お題）で止まらない");
        Assert.That(DebrisSetUI.ReelPosition(5f, 1f, count), Is.EqualTo((float)count), "止まったあとに動く");

        // だんだん遅くなる：同じ時間で進む量が減っていく
        float step = 0.1f;
        float lastMove = float.MaxValue;
        for (float t = 0f; t < 1f - step / 2f; t += step)
        {
            float move = DebrisSetUI.ReelPosition(t + step, 1f, count) - DebrisSetUI.ReelPosition(t, 1f, count);
            Assert.That(move, Is.LessThan(lastMove), $"{t:0.0} 秒のあたりで遅くなっていない");
            lastMove = move;
        }
    }

    [Test]
    public void Unordered_ChecksSameKindFromTheLeft()
    {
        // お題（3個）：装甲板・燃料タンク・装甲板。装甲板を1個、燃料タンクを1個入れた
        int[] kinds = { 0, 2, 0, -1, -1, -1, -1, -1 };
        int[] progress = { 1, 0, 1, 0, 0 };
        var result = new bool[DebrisSetUI.MaxSlots];

        DebrisSetUI.MarkCollected(kinds, 3, false, 0, progress, result);

        Assert.That(result, Is.EqualTo(new[] { true, true, false, false, false, false, false, false }));
    }

    [Test]
    public void Ordered_ChecksFromTheLeftByStep()
    {
        // お題（3個）の左から2個まで入れた
        int[] kinds = { 3, 4, 3, -1, -1, -1, -1, -1 };
        int[] progress = { 0, 0, 0, 1, 1 };
        var result = new bool[DebrisSetUI.MaxSlots];

        DebrisSetUI.MarkCollected(kinds, 3, true, 2, progress, result);

        Assert.That(result, Is.EqualTo(new[] { true, true, false, false, false, false, false, false }));
    }

    [Test]
    public void Side_FollowsWhereTheGoalsAreSeen()
    {
        // 青チーム（0）のゴールが右、赤チーム（1）のゴールが左に見える → 入れ替える（左＝赤）
        Assert.That(DebrisSetUI.DecideSwap(false, 0.8f, 0.2f), Is.True);
        // 青チームのゴールが左 → 入れ替えない（左＝青）
        Assert.That(DebrisSetUI.DecideSwap(true, 0.2f, 0.8f), Is.False);
        // 2つのゴールが同じくらいの横位置 → 今のまま（ちらつかないように）
        Assert.That(DebrisSetUI.DecideSwap(true, 0.52f, 0.48f), Is.True);
        Assert.That(DebrisSetUI.DecideSwap(false, 0.52f, 0.48f), Is.False);
    }

    [Test]
    public void TitleDots_GrowThenShrinkAndLoop()
    {
        // 0.2 秒ごとに 1→6 と増え、5→2 と減って、また 1 から
        int[] expected = { 1, 2, 3, 4, 5, 6, 5, 4, 3, 2, 1, 2 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(DebrisSetUI.TitleDots(i * 0.2f + 0.1f, 0.2f, 6), Is.EqualTo(expected[i]), $"{i} 段目の点の数が違う");
        }

        Assert.That(DebrisSetUI.TitleDots(float.PositiveInfinity, 0.2f, 6), Is.InRange(1, 6), "とても大きな秒数で範囲を外れる");
    }

    [Test]
    public void ArmSwing_TiltsUprightSwingsBackAndSettles()
    {
        // 25 度傾いて入り、0.45 でまっすぐ、反対側へ 20 度まで揺れて、最後はまっすぐに止まる
        Assert.That(DebrisSetUI.ArmSwing(0f, 0.45f, 25f, 20f), Is.EqualTo(25f).Within(1e-3f), "入り始めの傾きが違う");
        Assert.That(DebrisSetUI.ArmSwing(0.45f, 0.45f, 25f, 20f), Is.EqualTo(0f).Within(1e-3f), "起き上がる時刻にまっすぐでない");
        Assert.That(DebrisSetUI.ArmSwing(1f, 0.45f, 25f, 20f), Is.EqualTo(0f).Within(1e-3f), "最後にまっすぐに止まっていない");

        float lowest = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            lowest = Mathf.Min(lowest, DebrisSetUI.ArmSwing(i / 1000f, 0.45f, 25f, 20f));
        }

        Assert.That(lowest, Is.EqualTo(-20f).Within(0.1f), "反対側への揺れの大きさが違う");
    }

    [Test]
    public void Prefab_IsInResourcesAndWired()
    {
        GameObject prefab = Resources.Load<GameObject>(DebrisSetUI.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/DebrisSetUI.prefab が無い（ゲームの開始時に読み込む）");

        var so = new UnityEditor.SerializedObject(prefab.GetComponent<DebrisSetUI>());
        Assert.That(so.FindProperty("testMode").boolValue, Is.False, "プレハブのテストモードがオンになっている");

        UnityEditor.SerializedProperty items = so.FindProperty("itemSprites");
        Assert.That(items.arraySize, Is.EqualTo(DebrisSetUI.ItemKinds), "物資の画像の数が種類の数と違う");
        for (int i = 0; i < items.arraySize; i++)
        {
            Assert.That(items.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"物資{i + 1}の画像が空");
        }

        UnityEditor.SerializedProperty panels = so.FindProperty("panels");
        Assert.That(panels.arraySize, Is.EqualTo(DebrisSetUI.MaxTeams));
        for (int p = 0; p < panels.arraySize; p++)
        {
            UnityEditor.SerializedProperty panel = panels.GetArrayElementAtIndex(p);
            foreach (string part in new[] { "root", "body", "arm", "rod", "panel", "bracket", "title" })
            {
                Assert.That(panel.FindPropertyRelative(part).objectReferenceValue, Is.Not.Null, $"枠{p}の {part} が空");
            }

            UnityEditor.SerializedProperty slots = panel.FindPropertyRelative("slots");
            Assert.That(slots.arraySize, Is.EqualTo(DebrisSetUI.MaxSlots), $"枠{p}のマスの数が足りない");
            for (int n = 0; n < slots.arraySize; n++)
            {
                UnityEditor.SerializedProperty slot = slots.GetArrayElementAtIndex(n);
                Assert.That(slot.FindPropertyRelative("root").objectReferenceValue, Is.Not.Null, $"枠{p}のマス{n}の root が空");
                foreach (string card in new[] { "card", "nextCard" })
                {
                    foreach (string part in new[] { "root", "group", "background", "item", "cover", "check" })
                    {
                        Assert.That(slot.FindPropertyRelative(card).FindPropertyRelative(part).objectReferenceValue, Is.Not.Null,
                            $"枠{p}のマス{n}の {card}.{part} が空");
                    }
                }
            }
        }
    }
}
