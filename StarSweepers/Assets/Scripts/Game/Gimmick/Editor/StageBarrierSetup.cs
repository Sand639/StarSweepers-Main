using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// バリア（<see cref="StageBarrier"/>）の検証シーンを作り、全ステージとロビーに入れるメニュー（エディタ専用）。
/// 説明は Documents/スクリプトの説明/Game/Gimmick/StageBarrier.md
/// </summary>
public static class StageBarrierSetup
{
    private const string ShaderName = "StarSweepers/BarrierRipple";
    private const string MaterialPath = "Assets/Art/Materials/BarrierRipple.mat";
    private const string PrefabPath = "Assets/Prefabs/Gimmick/StageBarrier.prefab";
    private const string ScenePath = "Assets/Scenes/Test/StageBarrierTest.unity";

    /// <summary>全マップに置いてある進行役のプレハブ（この子に入れると全ステージに入る）。</summary>
    private const string RoundPrefabPath = "Assets/Prefabs/System/SpaceJunkRound.prefab";

    /// <summary>床・バリア・玉を投げる係を置いた検証シーンを作る（マテリアルとプレハブが無ければ作る）。</summary>
    [MenuItem("Tools/StarSweepers/ステージを囲むバリアの検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject prefab = EnsurePrefab();
        if (prefab == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.13f, 0.18f);
        cameraObject.transform.position = new Vector3(0f, 9f, -16f);
        cameraObject.transform.LookAt(new Vector3(0f, 1f, 0f));

        // 16m×12m の床（上面が高さ 0）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(16f, 1f, 12f);

        var barrier = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        barrier.name = "StageBarrier";

        var launcher = new GameObject("TestLauncher");
        launcher.transform.position = new Vector3(0f, 1f, 0f);
        launcher.AddComponent<StageBarrierTestLauncher>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BARRIER] バリアの検証シーンを作りました: {ScenePath}（再生すると、玉が壁に当たるたびに波紋が出ます）");
    }

    /// <summary>進行役のプレハブの子とロビーのシーンにバリアを入れる（入っていれば何もしない）。</summary>
    [MenuItem("Tools/StarSweepers/ステージを囲むバリアを全ステージとロビーに入れる")]
    public static void InstallEverywhere()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        GameObject prefab = EnsurePrefab();
        if (prefab == null) return;

        // ステージ：進行役のプレハブの子に入れる
        GameObject round = PrefabUtility.LoadPrefabContents(RoundPrefabPath);
        if (round.GetComponentInChildren<StageBarrier>(true) == null)
        {
            var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, round.transform);
            child.name = "StageBarrier";
            PrefabUtility.SaveAsPrefabAsset(round, RoundPrefabPath);
            Debug.Log($"[BARRIER] 進行役のプレハブに入れました（全ステージに入ります）: {RoundPrefabPath}");
        }
        else
        {
            Debug.Log($"[BARRIER] 進行役のプレハブには、すでに入っています: {RoundPrefabPath}");
        }
        PrefabUtility.UnloadPrefabContents(round);

        // ロビー：シーンに置く
        var lobby = EditorSceneManager.OpenScene(SpaceJunkSetup.LobbyScenePath, OpenSceneMode.Single);
        if (Object.FindFirstObjectByType<StageBarrier>(FindObjectsInactive.Include) == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lobby);
            instance.name = "StageBarrier";
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
            Debug.Log($"[BARRIER] ロビーに置きました: {SpaceJunkSetup.LobbyScenePath}");
        }
        else
        {
            Debug.Log($"[BARRIER] ロビーには、すでに置いてあります: {SpaceJunkSetup.LobbyScenePath}");
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>バリアのプレハブを読み込む。無ければ作る（あれば作り直さない）。</summary>
    private static GameObject EnsurePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            return prefab;
        }

        Material material = EnsureMaterial();
        if (material == null) return null;

        var root = new GameObject("StageBarrier");
        var barrier = root.AddComponent<StageBarrier>();
        var so = new SerializedObject(barrier);
        so.FindProperty("rippleMaterial").objectReferenceValue = material;
        so.ApplyModifiedPropertiesWithoutUndo();

        prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[BARRIER] バリアのプレハブを作りました: {PrefabPath}");
        return prefab;
    }

    /// <summary>波紋のマテリアルを読み込む。無ければシェーダーから作る。</summary>
    private static Material EnsureMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[BARRIER] シェーダー {ShaderName}（Assets/Art/Shaders/BarrierRipple.shader）が見つかりません。");
            return null;
        }

        material = new Material(shader) { name = "BarrierRipple" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }
}
