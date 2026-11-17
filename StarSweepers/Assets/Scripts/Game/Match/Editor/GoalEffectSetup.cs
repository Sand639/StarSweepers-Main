using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ゴールの渦（<see cref="GoalEffect"/>）の検証シーンを作り、全ステージとロビーに入れるメニュー（エディタ専用）。
/// 説明は Documents/スクリプトの説明/Game/Effect/GoalEffect.md
/// </summary>
public static class GoalEffectSetup
{
    private const string ShaderName = "StarSweepers/GoalVortex";
    private const string MaterialPath = "Assets/Art/Materials/GoalVortex.mat";
    private const string PrefabPath = "Assets/Prefabs/Effect/GoalEffect.prefab";
    private const string ScenePath = "Assets/Scenes/Test/GoalEffectTest.unity";

    /// <summary>得点が入ったときの光る演出（検証シーンにも置く）。</summary>
    private const string ScoreEffectPrefabPath = "Assets/Prefabs/Effect/SpaceJunkGoalScoreEffect.prefab";

    /// <summary>Hans さんの輸送機のモデル（ゴールの見た目に使っているマップがある。使うだけで変えない）。</summary>
    private const string TransporterModelPath = "Assets/Art/Models/SM_Transporter.fbx";

    /// <summary>全マップに置いてある進行役のプレハブ（この子に入れると全ステージに入る）。</summary>
    private const string RoundPrefabPath = "Assets/Prefabs/System/SpaceJunkRound.prefab";

    /// <summary>ロビー（練習用ゴールがある）。</summary>
    private const string LobbyScenePath = "Assets/Scenes/MainLoop/Lobby.unity";

    /// <summary>床のゴールと輸送機のゴールを並べ、上から物資を落として渦に吸い込ませる検証シーンを作る。</summary>
    [MenuItem("Tools/StarSweepers/ゴールの渦の検証シーンを作る")]
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
        camera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
        cameraObject.transform.position = new Vector3(0f, 9f, -11f);
        cameraObject.transform.LookAt(new Vector3(0f, 0.5f, 0f));

        // 24m×14m の床（上面が高さ 0）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(24f, 1f, 14f);

        // 左：ステージと同じ形の、色を塗る床のゴール（5m×4m）
        SpaceJunkGoal padGoal = CreateGoal("GoalArea_Pad", new Vector3(-5f, 2.5f, 0f), new Vector3(4f, 5f, 5f), 0);
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Pad";
        pad.transform.SetParent(padGoal.transform, false);
        pad.transform.SetPositionAndRotation(new Vector3(-5f, 0.05f, 0f), Quaternion.Euler(0f, 90f, 0f));
        pad.transform.localScale = new Vector3(5f, 0.1f, 4f);
        SetGoalVisual(padGoal, pad.GetComponent<Renderer>(), null);

        // 右：Hans さんの輸送機をゴールの見た目にしたゴール（ガラスマップ・電車マップ2と同じ形）
        SpaceJunkGoal transporterGoal = CreateGoal("GoalArea_Transporter", new Vector3(5f, 2.5f, 0f), new Vector3(3f, 5f, 3f), 1);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(TransporterModelPath);
        if (model != null)
        {
            var transporter = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            transporter.name = "SM_Transporter";
            transporter.transform.SetParent(transporterGoal.transform, false);
            transporter.transform.SetPositionAndRotation(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 270f, 0f));
            foreach (MeshFilter filter in transporter.GetComponentsInChildren<MeshFilter>())
            {
                // 荷台に物資が乗るように、本体に当たり判定を付ける（手すりは SpaceJunkGoalFence が付ける）
                if (!filter.name.StartsWith("Fence"))
                {
                    filter.gameObject.AddComponent<MeshCollider>();
                }
            }
            SetGoalVisual(transporterGoal, transporter.GetComponentInChildren<Renderer>(), transporter.transform);
        }
        else
        {
            Debug.LogWarning($"[VORTEX] 輸送機のモデルが見つかりません: {TransporterModelPath}（床のゴールだけで作ります）");
        }

        var vortex = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        vortex.name = "GoalEffect";

        var scoreEffect = AssetDatabase.LoadAssetAtPath<GameObject>(ScoreEffectPrefabPath);
        if (scoreEffect != null)
        {
            PrefabUtility.InstantiatePrefab(scoreEffect, scene);
        }

        var driverObject = new GameObject("TestDriver");
        var driver = driverObject.AddComponent<GoalEffectTestDriver>();
        var so = new SerializedObject(driver);
        SerializedProperty goals = so.FindProperty("goals");
        goals.arraySize = 2;
        goals.GetArrayElementAtIndex(0).objectReferenceValue = padGoal;
        goals.GetArrayElementAtIndex(1).objectReferenceValue = transporterGoal;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[VORTEX] ゴールの渦の検証シーンを作りました: {ScenePath}（再生すると、左右のゴールに物資が落ちて渦に吸い込まれます）");
    }

    /// <summary>判定の箱（トリガー）・通信の部品・ゴールの部品が付いた、ゴールの入れ物を作る。</summary>
    private static SpaceJunkGoal CreateGoal(string goalName, Vector3 position, Vector3 size, int index)
    {
        var root = new GameObject(goalName);
        root.transform.position = position;
        var box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        root.AddComponent<NetworkObject>();
        var goal = root.AddComponent<SpaceJunkGoal>();

        var so = new SerializedObject(goal);
        so.FindProperty("goalIndex").intValue = index;
        so.ApplyModifiedPropertiesWithoutUndo();
        return goal;
    }

    /// <summary>ゴールの色を塗る床（Pad Renderer）と、始まりの演出で飛ばす見た目（Intro Visual）を入れる。</summary>
    private static void SetGoalVisual(SpaceJunkGoal goal, Renderer padRenderer, Transform introVisual)
    {
        var so = new SerializedObject(goal);
        so.FindProperty("padRenderer").objectReferenceValue = padRenderer;
        so.FindProperty("introVisual").objectReferenceValue = introVisual;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>進行役のプレハブの子と、ロビーのシーンに渦を入れる（入っていれば何もしない）。</summary>
    [MenuItem("Tools/StarSweepers/ゴールの渦を全ステージとロビーに入れる")]
    public static void InstallEverywhere()
    {
        GameObject prefab = EnsurePrefab();
        if (prefab == null) return;

        GameObject round = PrefabUtility.LoadPrefabContents(RoundPrefabPath);
        if (round.GetComponentInChildren<GoalEffect>(true) == null)
        {
            var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, round.transform);
            child.name = "GoalEffect";
            PrefabUtility.SaveAsPrefabAsset(round, RoundPrefabPath);
            Debug.Log($"[VORTEX] 進行役のプレハブに入れました（全ステージに入ります）: {RoundPrefabPath}");
        }
        else
        {
            Debug.Log($"[VORTEX] 進行役のプレハブには、すでに入っています: {RoundPrefabPath}");
        }
        PrefabUtility.UnloadPrefabContents(round);

        var lobby = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
        if (Object.FindObjectsByType<GoalEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lobby);
            instance.name = "GoalEffect";
            EditorSceneManager.SaveScene(lobby);
            Debug.Log($"[VORTEX] ロビーに入れました: {LobbyScenePath}");
        }
        else
        {
            Debug.Log($"[VORTEX] ロビーには、すでに入っています: {LobbyScenePath}");
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>渦のプレハブを読み込む。無ければ作る（あれば作り直さない）。</summary>
    private static GameObject EnsurePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            return prefab;
        }

        Material material = EnsureMaterial();
        if (material == null) return null;

        var root = new GameObject("GoalEffect");
        var vortex = root.AddComponent<GoalEffect>();
        var so = new SerializedObject(vortex);
        so.FindProperty("vortexMaterial").objectReferenceValue = material;
        so.ApplyModifiedPropertiesWithoutUndo();

        prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"[VORTEX] 渦のプレハブを作りました: {PrefabPath}");
        return prefab;
    }

    /// <summary>渦のマテリアルを読み込む。無ければシェーダーから作る（あれば作り直さないので、調整した値は残る）。</summary>
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
            Debug.LogError($"[VORTEX] シェーダー {ShaderName}（Assets/Art/Shaders/GoalVortex.shader）が見つかりません。");
            return null;
        }

        material = new Material(shader) { name = "GoalVortex" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        Debug.Log($"[VORTEX] 渦のマテリアルを作りました: {MaterialPath}");
        return material;
    }
}
