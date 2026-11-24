using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 鞭の光線（<see cref="SweeperShooter"/> の撃ち方 Whip。2026/11/17）の検証シーンを作るメニュー（エディタ専用）。
/// 説明は Documents/機能ドキュメント/鞭のように曲がる光線.md
/// </summary>
public static class SweeperWhipSetup
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/SweeperPlayer.prefab";
    private const string ScenePath = SceneBuildTools.SceneFolder + "/SweeperWhipTest.unity";

    /// <summary>床・四方の壁・プレイヤー・まわりの物資・柱を置いた検証シーンを作る。</summary>
    [MenuItem("Tools/StarSweepers/鞭の光線の検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[WHIP] 新しい体のプレハブがありません: {PlayerPrefabPath}");
            return;
        }

        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SceneBuildTools.CreateLight();
        SceneBuildTools.CreateGround();
        SceneBuildTools.CreatePlainWalls();

        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        player.name = "SweeperPlayer";
        player.transform.position = Vector3.zero;
        SceneBuildTools.CreateCamera(player);

        // まわりに物資を2重の輪で並べる（近い 4m に 8個・遠い 9m に 12個。消えても 6 秒で戻る）
        SceneBuildTools.CreateSupplyRing(8, 4f);
        for (int i = 0; i < 12; i++)
        {
            float angle = 15f + 30f * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 9f);
            position.y = 0.4f;
            HookableObject hookable = SceneBuildTools.CreateSupply($"FarSupply_{i + 1}", position);
            SceneBuildTools.SetFloat(hookable, "respawnSeconds", 6f);
        }

        // 柱：光線が壁で止まる（柱の向こうの物資には届かない）ことを見る
        Material wall = AssetDatabase.LoadAssetAtPath<Material>(SceneBuildTools.MaterialFolder + "/FishingWall.mat");
        SceneBuildTools.CreateBox("Pillar_East", new Vector3(6.5f, 1f, 0f), new Vector3(1f, 2f, 1f), wall);
        SceneBuildTools.CreateBox("Pillar_West", new Vector3(-6.5f, 1f, 0f), new Vector3(1f, 2f, 1f), wall);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WHIP] 鞭の光線の検証シーンを作りました: {ScenePath}（RT／左クリックで光線。出している間に向きを変えると、しなって物資をつかめます）");
    }
}
