using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ゴールの輸送機の手すり（<see cref="SpaceJunkGoalFence"/>）の検証シーンを作るメニュー（エディタ専用）。
/// 説明は Documents/スクリプトの説明/Game/Match/SpaceJunkGoalFence.md
/// </summary>
public static class SpaceJunkGoalFenceSetup
{
    private const string ModelPath = "Assets/Art/Models/SM_Transporter.fbx";
    private const string ScenePath = "Assets/Scenes/Test/GoalFenceTest.unity";

    /// <summary>床のつき方が違う輸送機を3つ並べた検証シーンを作る。</summary>
    [MenuItem("Tools/StarSweepers/ゴールの手すりの検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[GOAL FENCE] 輸送機のモデルが見つかりません: {ModelPath}");
            return;
        }

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

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
        cameraObject.transform.position = new Vector3(0f, 12f, -14f);
        cameraObject.transform.LookAt(new Vector3(0f, 0f, 1f));

        // 左：前だけ床（ガラスマップのように、床の外側に付いた輸送機）→ 前の手すりだけしまう
        PlaceTransporter(model, "Transporter_FrontOnly", new Vector3(-9f, 0f, 0f));
        PlaceFloor("Floor_FrontOnly", new Vector3(-9f, 0f, 3f));

        // 真ん中：前・左・右に床（端の列のマスに置いた輸送機）→ 3枚しまう
        PlaceTransporter(model, "Transporter_ThreeSides", new Vector3(0f, 0f, 0f));
        PlaceFloor("Floor_ThreeSides_Front", new Vector3(0f, 0f, 3f));
        PlaceFloor("Floor_ThreeSides_Left", new Vector3(-3f, 0f, 0f));
        PlaceFloor("Floor_ThreeSides_Right", new Vector3(3f, 0f, 0f));

        // 右：床なし → 4枚とも残る
        PlaceTransporter(model, "Transporter_NoFloor", new Vector3(9f, 0f, 0f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GOAL FENCE] ゴールの手すりの検証シーンを作りました: {ScenePath}" +
                  "（再生すると、左は前の1枚・真ん中は3枚の手すりが床下にしまわれ、右は4枚とも残ります）");
    }

    /// <summary>輸送機を、マップと同じ高さ（床の上面 0 から 0.25 下げる）・同じ当たり判定で置く。前（手すりの無かった側）が +Z。</summary>
    private static void PlaceTransporter(GameObject model, string name, Vector3 position)
    {
        var transporter = (GameObject)PrefabUtility.InstantiatePrefab(model);
        transporter.name = name;
        transporter.transform.position = position + Vector3.down * 0.25f;

        var box = transporter.AddComponent<BoxCollider>();
        box.size = new Vector3(3f, 0.29f, 3f);
        box.center = new Vector3(0f, 0.145f, 0f);
    }

    /// <summary>3m 四方の床（上面が高さ 0）を置く。</summary>
    private static void PlaceFloor(string name, Vector3 position)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = name;
        floor.transform.position = position + Vector3.down * 0.5f;
        floor.transform.localScale = new Vector3(3f, 1f, 3f);
    }
}
