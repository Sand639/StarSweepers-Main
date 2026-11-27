using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>試合の画面の枠（<see cref="ScreenFrameUI"/>）のプレハブが、ゲームの開始時に読み込める形になっているかを確かめる。</summary>
public class ScreenFrameUITests
{
    [Test]
    public void Prefab_IsInResourcesAndWired()
    {
        GameObject prefab = Resources.Load<GameObject>(ScreenFrameUI.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/ScreenFrameUI.prefab が無い（ゲームの開始時に読み込む）");

        var so = new SerializedObject(prefab.GetComponent<ScreenFrameUI>());
        Assert.That(so.FindProperty("testMode").boolValue, Is.False, "プレハブのテストモードがオンになっている");
        Assert.That(so.FindProperty("content").objectReferenceValue, Is.Not.Null, "content が空");

        Image frame = prefab.GetComponentInChildren<Image>(true);
        Assert.That(frame, Is.Not.Null, "枠の画像が無い");
        Assert.That(frame.sprite, Is.Not.Null, "枠の画像が空");
        Assert.That(frame.sprite.border, Is.Not.EqualTo(Vector4.zero), "枠の画像に 9 分割の境目が無い（角が伸びてしまう）");
    }
}
