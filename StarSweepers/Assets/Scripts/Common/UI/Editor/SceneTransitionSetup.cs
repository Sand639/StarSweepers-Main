using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **トランジション（<see cref="SceneTransition"/>）**のツール。
///
/// | メニュー | すること |
/// | --- | --- |
/// | `Tools > StarSweepers > トランジション > プレハブを作る（無ければ）` | `Assets/Resources/SceneTransition.prefab` を作る。**あれば作り直さない** |
/// | `Tools > StarSweepers > トランジション > 検証シーンを作り直す` | `Assets/Scenes/Test/SceneTransitionTest.unity` を作る（あれば上書き） |
/// </summary>
public static class SceneTransitionSetup
{
    private const string PrefabPath = "Assets/Resources/SceneTransition.prefab";
    private const string ScenePath = "Assets/Scenes/Test/SceneTransitionTest.unity";

    /// <summary>プレハブを作って選ぶ。すでにあれば作り直さない（インスペクターで選んだ種類や時間が消えないように）。</summary>
    [MenuItem("Tools/StarSweepers/トランジション/プレハブを作る（無ければ）")]
    public static void CreatePrefabMenu()
    {
        GameObject prefab = GetOrCreatePrefab();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>検証シーンを新しく作って保存する（カメラ・光・箱・検証用の係）。</summary>
    [MenuItem("Tools/StarSweepers/トランジション/検証シーンを作り直す")]
    public static void CreateTestScene()
    {
        GetOrCreatePrefab();

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Test"))
        {
            AssetDatabase.CreateFolder("Assets/Scenes", "Test");
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 覆われていく様子が分かるように、画面の真ん中に箱を1つ置く
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.rotation = Quaternion.Euler(20f, 35f, 0f);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.35f, 0.5f, 0.7f);
        cameraObject.transform.position = new Vector3(0f, 0f, -4f);

        GameObject testerObject = new GameObject("SceneTransitionTester");
        SceneTransitionTester tester = testerObject.AddComponent<SceneTransitionTester>();
        SerializedObject serialized = new SerializedObject(tester);
        serialized.FindProperty("targetCamera").objectReferenceValue = camera;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TRANSITION] トランジションの検証シーンを作りました：{ScenePath}\n" +
                  "再生して Space（コントローラーは A）を押すと、演出を出してこのシーンを読み直します。");
    }

    /// <summary>プレハブを作る（あればそれを返す）。</summary>
    private static GameObject GetOrCreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            return existing;
        }

        GameObject temporary = new GameObject("SceneTransition", typeof(SceneTransition));
        temporary.layer = LayerMask.NameToLayer("UI");

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, PrefabPath);
        Object.DestroyImmediate(temporary);

        Debug.Log($"[TRANSITION] トランジションのプレハブを作りました：{PrefabPath}");
        return prefab;
    }
}
