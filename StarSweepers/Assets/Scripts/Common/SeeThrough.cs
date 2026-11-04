using UnityEngine;

/// <summary>
/// **「透けて見えるので、カメラはこれを壁として扱わない」という印。**
///
/// ガラスのように向こう側が見える物に付ける。
/// 当たり判定はそのまま残るので、**通り抜けられるわけではない。**
///
/// ## なぜ必要か
///
/// 三人称のカメラは、体との間に物があるとカメラを手前へ寄せる
/// （<see cref="CameraObstacleAvoid"/>）。
/// けれどガラス越しは**ちゃんと見えている**のに、
/// カメラだけが「壁がある」と判断して、いきなり体へ寄ってしまっていた
/// （`リスクリスト.md` 2026/9/7 登録）。
///
/// この印が付いた物は、カメラが無視する。
///
/// ## 使い方
///
/// **ガラスの板に付けるだけ。** つなぐものは無い。
/// `Tools > StarSweepers > アーカイブ > ロボットステージ > ガラスの板を置く` で置いた板には、最初から付いている。
///
/// 子どもの物にも効く（当たった物の親をたどって探すため）。
/// </summary>
[DisallowMultipleComponent]
public class SeeThrough : MonoBehaviour
{
    /// <summary>ガラスのシェーダーの名前。**この材質の物は、印が無くても透けている**と見なす。</summary>
    private const string GlassShaderName = "StarSweepers/Glass";

    /// <summary>
    /// **当たった物が「透けて見える物」かどうか。**
    ///
    /// 印（このスクリプト）が付いていれば、それが答え。
    /// 付いていなくても、**ガラスのシェーダーを使っていれば透けている**と見なす。
    /// すでにシーンに置いてあるガラスに、あとから印を付けて回らなくてよいようにするため。
    /// </summary>
    public static bool Is(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        if (collider.GetComponentInParent<SeeThrough>() != null)
        {
            return true;
        }

        Renderer renderer = collider.GetComponent<Renderer>();

        if (renderer == null)
        {
            return false;
        }

        Material material = renderer.sharedMaterial;

        return material != null && material.shader != null && material.shader.name == GlassShaderName;
    }
}
