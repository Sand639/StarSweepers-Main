using UnityEngine;

/// <summary>
/// **電気のデブリ（電気系の部品）の目印と、その固有の能力。**（2026/11/21・小野田さんの依頼）
///
/// | 何が起きるか | 中身 |
/// | --- | --- |
/// | いつも | 物のまわりに電気がバチバチ走る（<see cref="ElectricSparks"/>） |
/// | **引っ張られている最中に人へぶつかる** | ぶつかった人は**痺れて長く動けない**（ふつうのデブリより長い）。**吹き飛ばしはほぼ無し**。痺れている間、その人にも電気が走る |
///
/// ぶつかったかどうかの判定は、ふつうのデブリと同じく <see cref="SweeperPulledHit"/> が行い、
/// ぶつかった物にこの部品が付いていれば、ここの値（痺れる秒数・吹き飛ばしの強さ）を使う。
///
/// **どの素材が「電気」かは <see cref="ElectricKinds"/> で決める**（いまは回路基板だけ）。
/// 素材の目印（<see cref="SpaceJunkMaterial"/>）が起動時に <see cref="AttachIfElectric"/> を呼んで自分で付けるので、
/// プレハブに付けなくても効く。値を変えたいときだけ、プレハブ（例：`SpaceJunkCircuit`）にこの部品を付ける。
/// 詳しくは `Documents/機能ドキュメント/電気のデブリ.md`。
/// </summary>
public class ElectricDebris : MonoBehaviour
{
    /// <summary>電気のデブリとして扱う素材の種類。増やしたいときはここに足す（例：アンテナ）。</summary>
    private static readonly SpaceJunkMaterialKind[] ElectricKinds = { SpaceJunkMaterialKind.Circuit };

    [Header("ぶつかった人への効果")]
    [Tooltip("ぶつかった人が痺れて動けなくなる秒数。ふつうのデブリ（SweeperPulledHit の Stun Seconds）より長くする")]
    [Min(0f)]
    [SerializeField] private float stunSeconds = 3f;

    [Tooltip("横方向に押す速さ（m/秒）。ほぼ吹き飛ばさないので小さく。0 でその場に止まる")]
    [Min(0f)]
    [SerializeField] private float knockbackSpeed = 1f;

    [Tooltip("上向きに飛ばす速さ（m/秒）。0 で浮かない")]
    [Min(0f)]
    [SerializeField] private float knockbackLift;

    [Header("見た目")]
    [Tooltip("ON：物のまわりにいつも電気を走らせる")]
    [SerializeField] private bool showSparks = true;

    [Tooltip("ON：痺れている人のまわりにも電気を走らせる")]
    [SerializeField] private bool shockVictim = true;

    private HookableObject item;
    private ElectricSparks sparks;

    /// <summary>ぶつかった人が痺れて動けなくなる秒数。</summary>
    public float StunSeconds => stunSeconds;

    /// <summary>横方向に押す速さ（m/秒）。</summary>
    public float KnockbackSpeed => knockbackSpeed;

    /// <summary>上向きに飛ばす速さ（m/秒）。</summary>
    public float KnockbackLift => knockbackLift;

    /// <summary>この種類の素材は電気のデブリか。</summary>
    public static bool IsElectric(SpaceJunkMaterialKind kind)
    {
        foreach (SpaceJunkMaterialKind electric in ElectricKinds)
        {
            if (electric == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>電気の種類の素材なら、この部品を付ける（もう付いていれば何もしない）。<see cref="SpaceJunkMaterial"/> の起動時に呼ばれる。</summary>
    public static void AttachIfElectric(SpaceJunkMaterial material)
    {
        if (material == null || !IsElectric(material.Kind) || material.GetComponent<ElectricDebris>() != null)
        {
            return;
        }

        material.gameObject.AddComponent<ElectricDebris>();
    }

    private void Awake()
    {
        item = GetComponent<HookableObject>();
    }

    private void Start()
    {
        if (showSparks)
        {
            sparks = gameObject.AddComponent<ElectricSparks>();
        }
    }

    private void Update()
    {
        if (sparks == null)
        {
            return;
        }

        // ゴールに吸い込まれている間・消えている間は電気を止める（見た目が消えても線だけ残らないように）
        bool hidden = item != null && (item.IsVanished || item.IsAbsorbing);
        if (sparks.enabled == hidden)
        {
            sparks.enabled = !hidden;
        }
    }

    /// <summary>ぶつかった人（<paramref name="victim"/>）に、痺れている秒数だけ電気を走らせる。</summary>
    public void PlayShock(Transform victim, float seconds)
    {
        if (shockVictim)
        {
            ElectricSparks.Spawn(victim, seconds);
        }
    }
}
