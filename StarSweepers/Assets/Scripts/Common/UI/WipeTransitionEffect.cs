using UnityEngine;

/// <summary>
/// **ワイプ。** 左から幕が伸びて画面を覆い、次のシーンでは同じ向きのまま右へ抜けていく（<see cref="SceneTransitionType.Wipe"/>）。
/// </summary>
public class WipeTransitionEffect : SceneTransitionEffect
{
    private RectTransform curtain;

    public override void Build(RectTransform root, Color color)
    {
        curtain = CreateFullScreenImage(root, "Wipe", color).rectTransform;
    }

    public override void Apply(float amount, bool revealing)
    {
        // 幕の左端と右端を、画面の幅に対する割合で決める（画面の大きさが変わっても同じに見える）
        float left = revealing ? 1f - amount : 0f;
        float right = revealing ? 1f : amount;

        curtain.anchorMin = new Vector2(left, 0f);
        curtain.anchorMax = new Vector2(right, 1f);
    }
}
