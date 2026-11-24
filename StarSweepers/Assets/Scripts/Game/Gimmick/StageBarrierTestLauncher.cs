using UnityEngine;

/// <summary>
/// バリアの検証シーン専用。決まった間隔で玉をまわりの壁へ投げる。
/// 説明は Documents/スクリプトの説明/Game/Gimmick/StageBarrier.md
/// </summary>
public class StageBarrierTestLauncher : MonoBehaviour
{
    [Tooltip("何秒ごとに玉を投げるか")]
    [SerializeField] private float interval = 0.7f;

    [Tooltip("投げる速さ（m/s）")]
    [SerializeField] private float speed = 14f;

    [Tooltip("玉が消えるまでの秒数")]
    [SerializeField] private float ballLifetime = 4f;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < interval)
        {
            return;
        }

        timer = 0f;
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "TestBall";
        ball.transform.position = transform.position;
        ball.transform.localScale = Vector3.one * 0.6f;
        Rigidbody body = ball.AddComponent<Rigidbody>();
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // 水平のどこか（少し上向き）へ投げる
        float angle = Random.Range(0f, Mathf.PI * 2f);
        body.linearVelocity = new Vector3(Mathf.Cos(angle), 0.25f, Mathf.Sin(angle)) * speed;
        Destroy(ball, ballLifetime);
    }
}
