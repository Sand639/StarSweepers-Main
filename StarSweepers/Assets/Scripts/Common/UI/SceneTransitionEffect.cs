using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **トランジション（シーンを切り替えるときの演出）の種類。** <see cref="SceneTransition"/> のインスペクターのプルダウンに出る。
///
/// ## 種類を増やすとき（3か所）
///
/// 1. ここに1行足す（`[InspectorName]` はプルダウンに出る名前）
/// 2. <see cref="SceneTransitionEffect"/> を継いだクラスを、新しいファイルに書く（`FadeTransitionEffect.cs` が一番短い手本）
/// 3. <see cref="SceneTransitionEffect.Create"/> の switch に1行足す
///
/// **並びの途中に足さず、一番下に足すこと。** プレハブには番号で保存されるので、途中に足すと選んであった種類がずれる。
/// </summary>
public enum SceneTransitionType
{
    [InspectorName("フェード（だんだん暗くなる）")]
    Fade = 0,

    [InspectorName("ワイプ（横から幕が引かれる）")]
    Wipe = 1,
}

/// <summary>
/// **トランジションの演出1つ分のひな形。** 種類ごとにこれを継いで、見た目を書く。
///
/// 動かすのは <see cref="SceneTransition"/> で、ここでは「どれだけ覆っているか」に合わせて見た目を変えるだけ。
/// 時間の数え方・シーンの読み込みを待つこと・通信で全員に出すことは、継いだクラスでは考えなくてよい。
/// </summary>
public abstract class SceneTransitionEffect
{
    /// <summary>種類に合った演出を作る。</summary>
    public static SceneTransitionEffect Create(SceneTransitionType type)
    {
        switch (type)
        {
            case SceneTransitionType.Fade: return new FadeTransitionEffect();
            case SceneTransitionType.Wipe: return new WipeTransitionEffect();
        }

        Debug.LogWarning($"[TRANSITION] 種類「{type}」の演出が SceneTransitionEffect.Create に書かれていないので、フェードで出します。");
        return new FadeTransitionEffect();
    }

    /// <summary>
    /// 演出に使う部品（画像など）を作る。演出を始めるたびに1回呼ばれる。
    /// <paramref name="root"/> は画面全体を覆う枠で、前の演出の部品は消してから渡される。
    /// </summary>
    public abstract void Build(RectTransform root, Color color);

    /// <summary>
    /// 覆い具合に合わせて見た目を変える。毎フレーム呼ばれる。
    /// <paramref name="amount"/>：0＝何も覆っていない、1＝画面を全部覆った（なめらかに変わるよう、緩急は付けてある）。
    /// <paramref name="revealing"/>：閉じていくとき false（amount が 0→1）、開いていくとき true（amount が 1→0）。
    /// </summary>
    public abstract void Apply(float amount, bool revealing);

    /// <summary>画面全体を覆う一色の画像を作る（継いだクラスで使う）。</summary>
    protected static Image CreateFullScreenImage(RectTransform parent, string name, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true; // 演出の間は、下の画面のボタンを押させない
        return image;
    }
}
