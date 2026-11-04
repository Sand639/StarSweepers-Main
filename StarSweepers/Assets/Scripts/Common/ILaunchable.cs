using UnityEngine;

/// <summary>
/// **外から勢いを与えて、吹っ飛ばせるもの**に付ける目印。
///
/// 爆弾（<c>Bomb</c>）は、当たった相手がこれを持っていれば <see cref="Launch"/> を呼ぶ。
/// **相手がロボットか、別のキャラクターかを知らなくて済む**ようにするためのもの。
///
/// 今はロボットの体（<c>RobotBody</c>）だけが持っている。
/// 本番用のプレイヤーを作ったら、そこにもこれを付ければ爆弾で飛ぶようになる。
/// </summary>
public interface ILaunchable
{
    /// <summary>
    /// 勢いを与える。<paramref name="velocity"/> は**1秒あたりのメートル**。
    /// </summary>
    void Launch(Vector3 velocity);
}
