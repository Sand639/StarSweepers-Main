using UnityEngine;

/// <summary>
/// **吹き飛ばされた瞬間のエフェクト（ノックバック）。**（2026/11/24・小野田さんの依頼）
///
/// 引っ張られている物がぶつかった・隕石が当たったなどで体が吹き飛ぶ瞬間に、当たった所で星のかけらがはじけ、足元に光の輪が広がる。
/// 出すのは <see cref="MeteorStrikeEvent.ApplyPlayerImpact"/>（吹き飛ばしの共通の入口）。
/// **オンラインでは、吹き飛んだ人のPCから <see cref="SweeperNetSync"/> を通して全員の画面に出す**（体を吹き飛ばすのは本人のPCだけなので）。
///
/// プレハブに付いていなくても、最初に使うときに体へ自分で付く（マテリアルは仮に作るので、ビルドではプレハブに付けて SweeperRing.mat を入れること）。
/// </summary>
public class SweeperHitEffects : MonoBehaviour
{
    [Header("ノックバック（当たった瞬間）")]
    [Tooltip("これより弱い吹き飛ばし（m/秒）では出さない（電気のデブリのように、ほとんど飛ばないとき）")]
    [Min(0f)]
    [SerializeField] private float minSpeed = 2.5f;

    [Tooltip("はじける星のかけらの色")]
    [SerializeField] private Color burstColor = new Color(1f, 0.95f, 0.75f, 1f);

    [Tooltip("足元に広がる光の輪の色")]
    [SerializeField] private Color ringColor = new Color(1f, 0.6f, 0.25f, 1f);

    [Tooltip("当たった所の高さ（足元から m）")]
    [SerializeField] private float hitHeight = 0.6f;

    [Tooltip("エフェクトに使うマテリアル（Art/Materials/SweeperRing.mat）")]
    [SerializeField] private Material material;

    /// <summary>この体のエフェクトの部品（無ければ付ける）。</summary>
    public static SweeperHitEffects For(Component body)
    {
        if (body == null)
        {
            return null;
        }

        SweeperHitEffects effects = body.GetComponent<SweeperHitEffects>();
        return effects != null ? effects : body.gameObject.AddComponent<SweeperHitEffects>();
    }

    /// <summary>
    /// **吹き飛ばされた瞬間に呼ぶ。** このPCに出し、オンラインならほかの人の画面にも出す。
    /// <paramref name="velocity"/> は吹き飛ばす速さ（向きから当たった側を決める）。
    /// </summary>
    public void PlayKnockback(Vector3 velocity)
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (velocity.magnitude < minSpeed)
        {
            return;
        }

        // 飛んでいく向きの逆側（ぶつかった側）
        Vector3 from = flat.sqrMagnitude > 0.0001f ? -flat.normalized : -transform.forward;
        Vector3 point = transform.position + Vector3.up * hitHeight + from * 0.35f;
        ShowKnockback(point, Mathf.Clamp01(velocity.magnitude / 12f));

        SweeperNetSync sync = GetComponent<SweeperNetSync>();
        if (sync != null)
        {
            sync.BroadcastKnockback(point, Mathf.Clamp01(velocity.magnitude / 12f));
        }
    }

    /// <summary>エフェクトをこのPCにだけ出す（オンラインのほかの人の体では、知らせが届いたときに呼ばれる）。</summary>
    public void ShowKnockback(Vector3 point, float strength)
    {
        float size = Mathf.Lerp(0.6f, 1.2f, strength);
        SweeperShards.Spawn(point, size * 0.5f, burstColor, material);
        Vector3 ground = new Vector3(point.x, transform.position.y + 0.03f, point.z);
        SweeperFlash.Spawn(ground, null, ringColor, 0.4f * size, 2.4f * size, 0.35f, material);
    }
}
