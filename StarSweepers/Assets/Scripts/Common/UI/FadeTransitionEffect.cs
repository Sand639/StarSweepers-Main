using UnityEngine;
using UnityEngine.UI;

/// <summary>**フェード。** 画面全体がだんだん暗くなり、次のシーンでだんだん明るくなる（<see cref="SceneTransitionType.Fade"/>）。</summary>
public class FadeTransitionEffect : SceneTransitionEffect
{
    private Image image;
    private Color color;

    public override void Build(RectTransform root, Color color)
    {
        this.color = color;
        image = CreateFullScreenImage(root, "Fade", color);
    }

    public override void Apply(float amount, bool revealing)
    {
        Color current = color;
        current.a = color.a * amount;
        image.color = current;
    }
}
