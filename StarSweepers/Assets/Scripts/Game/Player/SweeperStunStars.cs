using UnityEngine;

/// <summary>
/// **スタン中に頭のまわりを回る星（★）。**（2026/11/24・小野田さんの依頼）
///
/// <see cref="PlayerStun"/> がスタンした瞬間に体の子として作り、スタンが解けたら隠す。
/// 星はリングと同じシェーダー（`Art/Shaders/SweeperRing.shader` の星を塗りつぶしたもの）の板で、いつもカメラの方を向く。
/// デザイナーさんの素材が来たら、PlayerStun の Stun Marker（頭の上の印）に差し替え、Use Stars を OFF にすればよい。
/// </summary>
public class SweeperStunStars : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
    private static readonly int RotationId = Shader.PropertyToID("_Rotation");
    private static readonly int StarInnerId = Shader.PropertyToID("_StarInner");

    private Transform[] stars = new Transform[0];
    private MeshRenderer[] renderers = new MeshRenderer[0];
    private MaterialPropertyBlock block;
    private Color color;
    private float height;
    private float radius;
    private float size;
    private float orbitSpeed;
    private float angle;

    /// <summary>体（<paramref name="owner"/>）の子として星を作る。最初は隠しておく。</summary>
    public static SweeperStunStars Create(Transform owner, int count, float height, float radius, float size,
        float orbitSpeed, Color color, Material material)
    {
        GameObject root = new GameObject("StunStars");
        root.transform.SetParent(owner, false);

        SweeperStunStars made = root.AddComponent<SweeperStunStars>();
        made.color = color;
        made.height = height;
        made.radius = radius;
        made.size = size;
        made.orbitSpeed = orbitSpeed;
        made.block = new MaterialPropertyBlock();

        Material useMaterial = material != null ? material : SweeperGroundMesh.FallbackMaterial("StarSweepers/SweeperRing");
        count = Mathf.Max(1, count);
        made.stars = new Transform[count];
        made.renderers = new MeshRenderer[count];
        for (int i = 0; i < count; i++)
        {
            GameObject star = new GameObject("Star");
            star.transform.SetParent(root.transform, false);
            star.AddComponent<MeshFilter>().sharedMesh = SweeperGroundMesh.UnitQuad;
            MeshRenderer starRenderer = star.AddComponent<MeshRenderer>();
            starRenderer.sharedMaterial = useMaterial;
            starRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            starRenderer.receiveShadows = false;
            made.stars[i] = star.transform;
            made.renderers[i] = starRenderer;
        }

        root.SetActive(false);
        return made;
    }

    /// <summary>見せる／隠す。</summary>
    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    private void LateUpdate()
    {
        angle += orbitSpeed * Time.deltaTime;

        Camera view = Camera.main;
        Vector3 toCamera = view != null ? -view.transform.forward : Vector3.up;
        Vector3 center = transform.parent != null ? transform.parent.position + Vector3.up * height : transform.position;
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 10f);

        for (int i = 0; i < stars.Length; i++)
        {
            float a = (angle + i * 360f / stars.Length) * Mathf.Deg2Rad;
            stars[i].position = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * (0.05f * Mathf.Sin(a * 2f));
            // 板の表（上向き）をカメラへ向ける
            stars[i].rotation = Quaternion.FromToRotation(Vector3.up, toCamera);
            stars[i].localScale = Vector3.one * size * pulse;

            renderers[i].GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            block.SetFloat(ShapeId, 3f);
            block.SetFloat(ThicknessId, 0.3f);
            block.SetFloat(FillAlphaId, 1f);
            block.SetFloat(RotationId, angle * 0.5f);
            block.SetFloat(StarInnerId, 0.45f);
            renderers[i].SetPropertyBlock(block);
        }
    }
}
