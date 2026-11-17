using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 計算で作る星空の背景（シェーダー StarSweepers/ProceduralStarrySky）の検証シーンを作るメニュー（エディタ専用）。
/// 説明は Documents/機能ドキュメント/計算で作る星空の背景.md
/// マテリアルは試合のマップの空と同じもの（<see cref="SpaceJunkSkybox"/> が名前で読み込む）。検証シーンで数値を変えると、試合の空も変わる。
/// </summary>
public static class ProceduralStarrySkySetup
{
    private const string ShaderName = "StarSweepers/ProceduralStarrySky";
    public const string MaterialPath = "Assets/Resources/SpaceJunkSkybox.mat";
    public const string ScenePath = "Assets/Scenes/Test/ProceduralStarrySkyTest.unity";

    /// <summary>空をこの星空にして、大きさの見本の床を置いた検証シーンを作る（マテリアルが無ければ作る）。</summary>
    [MenuItem("Tools/StarSweepers/計算で作る星空の検証シーンを作る")]
    public static void CreateTestScene()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Material material = EnsureMaterial();
        if (material == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = material;
        // 環境光は空から作らず、暗い一色にする（星空から作ると真っ暗になり、床が見えないため）
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.33f);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.Skybox;
        cameraObject.transform.position = new Vector3(0f, 6f, -14f);
        cameraObject.transform.rotation = Quaternion.Euler(10f, 0f, 0f);

        // 大きさの見本：試合のステージくらいの床（12m×8m、上面が高さ 0）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(12f, 1f, 8f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SKY] 計算で作る星空の検証シーンを作りました: {ScenePath}（星の色や数は {MaterialPath} の Inspector で変えられます）");
    }

    /// <summary>星空のマテリアルを読み込む。無ければシェーダーから作る（あれば作り直さない）。</summary>
    public static Material EnsureMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[SKY] シェーダー {ShaderName}（Assets/Art/Shaders/ProceduralStarrySky.shader）が見つかりません。");
            return null;
        }

        material = new Material(shader) { name = "SpaceJunkSkybox" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }
}
