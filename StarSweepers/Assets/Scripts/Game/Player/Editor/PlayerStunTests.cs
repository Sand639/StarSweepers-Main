using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// **スタンの赤い点滅（<see cref="PlayerStun"/>）が、`_Color` の無いマテリアルでもエラーを出さずに色を変えるかの自動テスト。**（2026/11/10）
///
/// 隕石ステージで「Material 'M_Character (Instance)' … doesn't have a color property '_Color'」が毎フレーム出て
/// Unity が固まったように見えた不具合の再発防止。キャラクターの体（`M_Character.mat`・Shader Graph）は色の項目が
/// `_SuitColor` だけなので、`material.color` では触れない。
///
/// ・キャラクターの体のマテリアル → `_SuitColor` が赤になり、戻すと元の色。エラーは出ない
/// ・URP の Lit（前の印 FrontMark など）→ 今までどおり `material.color`（= `_BaseColor`）で点滅する
///
/// `renderer.material` は再生中にしか安全に使えないので、再生モードで動かす。
/// `Window > General > Test Runner` の EditMode から実行できる。
/// </summary>
public class PlayerStunTests
{
    private const string CharacterMaterialPath = "Assets/Art/Materials/M_Character.mat";

    [UnityTest]
    public IEnumerator Stun_OnCharacterMaterialWithoutColorProperty_TintsSuitColorWithoutErrors()
    {
        yield return new EnterPlayMode();
        yield return RunCharacterMaterial();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Stun_OnUrpLitMaterial_StillTintsMainColor()
    {
        yield return new EnterPlayMode();
        yield return RunUrpLitMaterial();
        yield return new ExitPlayMode();
    }

    /// <summary>キャラクターの体のマテリアル（`_SuitColor` だけ）で、点滅と戻しがエラーなしで通るか。</summary>
    private static IEnumerator RunCharacterMaterial()
    {
        Material source = AssetDatabase.LoadAssetAtPath<Material>(CharacterMaterialPath);
        Assert.That(source, Is.Not.Null, CharacterMaterialPath + " が見つからない");
        Assert.That(source.HasProperty("_Color"), Is.False, "前提：_Color が無いマテリアルで確かめる");
        Assert.That(source.HasProperty("_SuitColor"), Is.True, "前提：_SuitColor があるマテリアルで確かめる");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        MeshRenderer renderer = body.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = source;
        Color original = source.GetColor("_SuitColor");

        PlayerStun stun = body.AddComponent<PlayerStun>();
        SetBodyRenderers(stun, renderer);
        Color stunColor = (Color)GetField(stun, "stunColor");

        stun.Stun(1f);
        Invoke(stun, "Blink");
        Color blinking = renderer.material.GetColor("_SuitColor");
        Assert.That(blinking == stunColor || blinking == original, Is.True,
            $"点滅中は赤か元の色のはず。実際：{blinking}");

        Invoke(stun, "RestoreColors");
        Assert.That(renderer.material.GetColor("_SuitColor"), Is.EqualTo(original), "戻したら元の色のはず");

        Object.Destroy(body);
        yield return null;
    }

    /// <summary>URP の Lit（`_BaseColor` が主の色）で、今までどおり `material.color` が点滅するか。</summary>
    private static IEnumerator RunUrpLitMaterial()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Assert.That(lit, Is.Not.Null, "URP の Lit が見つからない");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        MeshRenderer renderer = body.GetComponent<MeshRenderer>();
        Material material = new Material(lit) { color = Color.blue };
        renderer.sharedMaterial = material;

        PlayerStun stun = body.AddComponent<PlayerStun>();
        SetBodyRenderers(stun, renderer);
        Color stunColor = (Color)GetField(stun, "stunColor");

        stun.Stun(1f);
        Invoke(stun, "Blink");
        Color blinking = renderer.material.color;
        Assert.That(blinking == stunColor || blinking == Color.blue, Is.True,
            $"点滅中は赤か元の色（青）のはず。実際：{blinking}");

        Invoke(stun, "RestoreColors");
        Assert.That(renderer.material.color, Is.EqualTo(Color.blue), "戻したら青のはず");

        Object.Destroy(body);
        Object.Destroy(material);
        yield return null;
    }

    /// <summary>インスペクターでしか入れられない Body Renderers を、テストから入れる。</summary>
    private static void SetBodyRenderers(PlayerStun stun, params Renderer[] renderers)
    {
        typeof(PlayerStun)
            .GetField("bodyRenderers", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(stun, renderers);
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Invoke(object target, string methodName)
    {
        target.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }
}
