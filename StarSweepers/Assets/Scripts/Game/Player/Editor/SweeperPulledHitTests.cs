using NUnit.Framework;
using UnityEngine;

/// <summary>
/// **引っ張った物がぶつかった人の吹き飛ばしの自動テスト。**（2026/11/21）
///
/// 吹き飛ばす向きの計算（<see cref="SweeperPulledHit.KnockbackVelocity"/>）だけを確かめる。
/// 実際にぶつけて動けなくなるかは、遊んで確かめる（`Documents/機能ドキュメント/引っ張った物がぶつかった人のスタン.md`）。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class SweeperPulledHitTests
{
    [Test]
    public void PushesAwayFromItem_WithLift()
    {
        Vector3 v = SweeperPulledHit.KnockbackVelocity(
            new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 0f), Vector3.forward, 9f, 4f);

        Assert.That(v.x, Is.EqualTo(9f).Within(0.001f), "物から人への向き（＋X）へ飛ぶ");
        Assert.That(v.z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(v.y, Is.EqualTo(4f).Within(0.001f), "上向きの速さはそのまま");
    }

    [Test]
    public void ExactlyOverlapping_PushesBehindPlayer()
    {
        Vector3 v = SweeperPulledHit.KnockbackVelocity(
            Vector3.zero, Vector3.zero, Vector3.forward, 9f, 0f);

        Assert.That(v.z, Is.EqualTo(-9f).Within(0.001f), "向きが決まらないときは、その人の後ろへ飛ぶ");
    }

    /// <summary>ぶつかった速さで強さが変わる（2026/11/24）：いちばん遅い所で 0、いちばん速い所で 1、その間はまっすぐ増える。</summary>
    [Test]
    public void StrengthGrowsWithSpeed()
    {
        Assert.That(SweeperPulledHit.StrengthForSpeed(1f, 1.5f, 9f), Is.EqualTo(0f), "遅すぎる物は強さ 0");
        Assert.That(SweeperPulledHit.StrengthForSpeed(5.25f, 1.5f, 9f), Is.EqualTo(0.5f).Within(0.001f), "真ん中の速さで 0.5");
        Assert.That(SweeperPulledHit.StrengthForSpeed(20f, 1.5f, 9f), Is.EqualTo(1f), "速すぎても 1 まで");
    }
}
