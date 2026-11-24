using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 電気のデブリ（<see cref="ElectricDebris"/>。2026/11/21）の検証シーンを作るメニュー（エディタ専用）。
/// 回路基板（電気）と装甲板（ふつう）を並べ、人形には決まった間隔で「痺れた」ときの電気を走らせる。
/// 説明は Documents/機能ドキュメント/電気のデブリ.md
/// </summary>
public static class ElectricDebrisSetup
{
    private const string CircuitPrefabPath = "Assets/Prefabs/Item/SpaceJunkCircuit.prefab";
    private const string PlatePrefabPath = "Assets/Prefabs/Item/SpaceJunkPlate.prefab";
    private const string ScenePath = SceneBuildTools.SceneFolder + "/ElectricDebrisTest.unity";

    [MenuItem("Tools/StarSweepers/電気のデブリの検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject circuit = AssetDatabase.LoadAssetAtPath<GameObject>(CircuitPrefabPath);
        GameObject plate = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePrefabPath);
        if (circuit == null || plate == null)
        {
            Debug.LogError($"[ELECTRIC] 素材のプレハブがありません: {CircuitPrefabPath} / {PlatePrefabPath}");
            return;
        }

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SceneBuildTools.CreateLight();
        SceneBuildTools.CreateGround();

        // 手前に回路基板（電気）を3つ、奥に比べる用の装甲板を2つ
        for (int i = 0; i < 3; i++)
        {
            var item = (GameObject)PrefabUtility.InstantiatePrefab(circuit, scene);
            item.name = $"Circuit_{i + 1}";
            item.transform.position = new Vector3(-2f + 2f * i, 0.6f, 0f);
        }
        for (int i = 0; i < 2; i++)
        {
            var item = (GameObject)PrefabUtility.InstantiatePrefab(plate, scene);
            item.name = $"Plate_{i + 1}";
            item.transform.position = new Vector3(-1f + 2f * i, 0.6f, 2.5f);
        }

        // 痺れた人の代わりの人形（体と同じくらいの大きさのカプセル）
        GameObject victim = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        victim.name = "ShockDummy";
        victim.transform.position = new Vector3(4.5f, 1f, 0f);

        var driverObject = new GameObject("ElectricDebrisTestDriver");
        ElectricDebrisTestDriver driver = driverObject.AddComponent<ElectricDebrisTestDriver>();
        SceneBuildTools.SetRef(driver, "victim", victim.transform);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        cameraObject.transform.position = new Vector3(1f, 5f, -7f);
        cameraObject.transform.LookAt(new Vector3(1f, 0.5f, 0.8f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ELECTRIC] 電気のデブリの検証シーンを作りました: {ScenePath}（再生すると、回路基板に電気が走り、右の人形には痺れたときの電気が繰り返し走ります）");
    }
}
