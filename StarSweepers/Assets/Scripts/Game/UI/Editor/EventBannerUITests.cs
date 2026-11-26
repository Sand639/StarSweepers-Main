using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>イベント開始の警告バナー（<see cref="EventBannerUI"/>）のプレハブが、ゲームの開始時に読み込める形になっているかを確かめる。</summary>
public class EventBannerUITests
{
    [Test]
    public void Prefab_IsInResourcesAndWired()
    {
        GameObject prefab = Resources.Load<GameObject>(EventBannerUI.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/EventBannerUI.prefab が無い（ゲームの開始時に読み込む）");

        var so = new SerializedObject(prefab.GetComponent<EventBannerUI>());
        Assert.That(so.FindProperty("testMode").boolValue, Is.False, "プレハブのテストモードがオンになっている");

        foreach (string part in new[] { "content", "banner", "label" })
        {
            Assert.That(so.FindProperty(part).objectReferenceValue, Is.Not.Null, $"{part} が空");
        }

        var banner = (UnityEngine.UI.Image)so.FindProperty("banner").objectReferenceValue;
        Assert.That(banner.sprite, Is.Not.Null, "帯の画像が空");
    }
}
