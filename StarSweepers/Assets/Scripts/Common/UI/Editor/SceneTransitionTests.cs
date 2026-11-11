using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>トランジション（<see cref="SceneTransition"/>）の設定と、種類ごとの演出を確かめる。</summary>
public class SceneTransitionTests
{
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("TransitionTestRoot", typeof(RectTransform));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void Prefab_IsInResources()
    {
        GameObject prefab = Resources.Load<GameObject>(SceneTransition.PrefabResourcePath);
        Assert.That(prefab, Is.Not.Null, "Resources/SceneTransition.prefab が無い（ゲームの開始時に読み込む）");
        Assert.That(prefab.GetComponent<SceneTransition>(), Is.Not.Null, "SceneTransition が付いていない");
    }

    [Test]
    public void EveryType_HasItsOwnEffect()
    {
        HashSet<Type> seen = new HashSet<Type>();
        foreach (SceneTransitionType type in Enum.GetValues(typeof(SceneTransitionType)))
        {
            SceneTransitionEffect effect = SceneTransitionEffect.Create(type);
            Assert.That(seen.Add(effect.GetType()), Is.True,
                $"種類「{type}」の演出が SceneTransitionEffect.Create に書かれていない（ほかの種類と同じ演出になっている）");
        }
    }

    [Test]
    public void EveryType_CanBuildAndAnimate()
    {
        foreach (SceneTransitionType type in Enum.GetValues(typeof(SceneTransitionType)))
        {
            SceneTransitionEffect effect = SceneTransitionEffect.Create(type);
            effect.Build((RectTransform)root.transform, Color.black);

            foreach (float amount in new[] { 0f, 0.5f, 1f })
            {
                effect.Apply(amount, false);
                effect.Apply(amount, true);
            }

            Assert.That(root.transform.childCount, Is.GreaterThan(0), $"種類「{type}」の演出が、画面に何も作っていない");

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }
        }
    }

    [Test]
    public void Fade_IsClearAtZeroAndSolidAtOne()
    {
        SceneTransitionEffect effect = SceneTransitionEffect.Create(SceneTransitionType.Fade);
        effect.Build((RectTransform)root.transform, Color.black);
        Image image = root.GetComponentInChildren<Image>();

        effect.Apply(0f, false);
        Assert.That(image.color.a, Is.EqualTo(0f).Within(0.001f));

        effect.Apply(1f, true);
        Assert.That(image.color.a, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void Wipe_CoversFromLeftAndLeavesToRight()
    {
        SceneTransitionEffect effect = SceneTransitionEffect.Create(SceneTransitionType.Wipe);
        effect.Build((RectTransform)root.transform, Color.black);
        RectTransform curtain = root.GetComponentInChildren<Image>().rectTransform;

        effect.Apply(0.5f, false);
        Assert.That(curtain.anchorMin.x, Is.EqualTo(0f).Within(0.001f), "閉じるときは左端から伸びる");
        Assert.That(curtain.anchorMax.x, Is.EqualTo(0.5f).Within(0.001f));

        effect.Apply(1f, false);
        Assert.That(curtain.anchorMax.x - curtain.anchorMin.x, Is.EqualTo(1f).Within(0.001f), "覆いきったら画面の幅いっぱい");

        effect.Apply(0.5f, true);
        Assert.That(curtain.anchorMin.x, Is.EqualTo(0.5f).Within(0.001f), "開くときは右へ抜けていく");
        Assert.That(curtain.anchorMax.x, Is.EqualTo(1f).Within(0.001f));
    }
}
