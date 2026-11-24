using UnityEngine;

/// <summary>
/// **電気のデブリの検証シーン（`Scenes/Test/ElectricDebrisTest`）専用。**（2026/11/21）
///
/// 決まった間隔で、置いた人形に「電気のデブリに当たった」ときの電気（<see cref="ElectricSparks.Spawn"/>）を走らせる。
/// 実際にぶつけて痺れるかは、ボット入りの試合で確かめる（`Documents/機能ドキュメント/電気のデブリ.md`）。
/// </summary>
public class ElectricDebrisTestDriver : MonoBehaviour
{
    [Tooltip("電気を走らせる人形")]
    [SerializeField] private Transform victim;

    [Tooltip("電気を走らせる秒数（電気のデブリの痺れる秒数と同じにしておく）")]
    [Min(0.1f)]
    [SerializeField] private float shockSeconds = 3f;

    [Tooltip("電気が消えてから、次に走らせるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float pauseSeconds = 1.5f;

    private float nextShock;

    private void Update()
    {
        if (victim == null || Time.time < nextShock)
        {
            return;
        }

        ElectricSparks.Spawn(victim, shockSeconds);
        nextShock = Time.time + shockSeconds + pauseSeconds;
    }
}
