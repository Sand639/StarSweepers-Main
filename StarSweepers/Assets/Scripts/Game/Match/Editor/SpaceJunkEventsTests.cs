using NUnit.Framework;

/// <summary>ラウンドイベントの設定の初期値と、始まったときに出す説明（<see cref="SpaceJunkEvents"/>）を確かめる。</summary>
public class SpaceJunkEventsTests
{
    [Test]
    public void DebrisSet_DefaultIsThreeItemsAndNeedsAllThree()
    {
        var settings = new SpaceJunkEventSettings();

        Assert.That(settings.debrisSetMinSize, Is.EqualTo(3), "お題の個数（いちばん少ない数）の初期値が3でない");
        Assert.That(settings.debrisSetMaxSize, Is.EqualTo(3), "お題の個数（いちばん多い数）の初期値が3でない");
        Assert.That(settings.debrisSetGoal, Is.EqualTo(3), "そろえるのに要る数の初期値が3でない");
    }

    [Test]
    public void DebrisSet_DescriptionSaysAllWhenGoalIsWholeSet()
    {
        var settings = new SpaceJunkEventSettings { debrisSetMinSize = 3, debrisSetMaxSize = 3, debrisSetGoal = 3, debrisSetBonus = 10 };

        Assert.That(SpaceJunkEvents.DefaultDescription(SpaceJunkEventKind.DebrisSet, settings),
            Is.EqualTo("お題のデブリを全部入れると 10 点！（順番は自由）"));

        settings.debrisSetOrdered = true;
        Assert.That(SpaceJunkEvents.DefaultDescription(SpaceJunkEventKind.DebrisSet, settings),
            Is.EqualTo("お題のデブリを先頭から順番に全部入れると 10 点！"));
    }

    [Test]
    public void DebrisSet_DescriptionSaysCountWhenGoalIsPartOfSet()
    {
        // 前の設定（4〜6個のうち3個）に戻したときは、今までどおり「どれでも 3 個」
        var settings = new SpaceJunkEventSettings { debrisSetMinSize = 4, debrisSetMaxSize = 6, debrisSetGoal = 3, debrisSetBonus = 10 };

        Assert.That(SpaceJunkEvents.DefaultDescription(SpaceJunkEventKind.DebrisSet, settings),
            Is.EqualTo("お題のデブリをどれでも 3 個入れると 10 点！（順番は自由）"));
    }
}
