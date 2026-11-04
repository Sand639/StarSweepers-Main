using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **ポーズ画面をシーンに置くツール。**
///
/// Unityのメニュー `Tools > StarSweepers > アーカイブ > 共通の部品 > ポーズ画面を置く` から実行できる。
///
/// 置かれるのは**空のゲームオブジェクトに部品が1つ付いただけ**のもの。
/// 画面そのものは、再生したときにコードで組み立てられる（日本語フォントのため）。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class PauseMenuSetup
{
    private const string PrefabPath = "Assets/Prefabs/Common/PauseMenu.prefab";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    [MenuItem("Tools/StarSweepers/アーカイブ/共通の部品/ポーズ画面を置く")]
    public static void CreatePauseMenu()
    {
        if (Object.FindFirstObjectByType<PauseMenu>() != null)
        {
            Debug.LogWarning("このシーンには、すでにポーズ画面が置かれています。");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject placed;

        if (prefab != null)
        {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }
        else
        {
            placed = CreatePrefabAsset();
            placed = (GameObject)PrefabUtility.InstantiatePrefab(placed);
        }

        Undo.RegisterCreatedObjectUndo(placed, "ポーズ画面を置く");
        Selection.activeGameObject = placed;
        EditorSceneManager.MarkSceneDirty(placed.scene);

        Debug.Log("ポーズ画面を置きました。**再生して Escape を押すと開きます。**\n" +
                  "画面は再生したときに作られるので、止まっている間は何も見えません。", placed);
    }

    /// <summary>プレハブを作る。**入力の設定も繋いでおく**（UIのクリックに使う）。</summary>
    private static GameObject CreatePrefabAsset()
    {
        GameObject root = new GameObject("PauseMenu");

        PauseMenu menu = root.AddComponent<PauseMenu>();

        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

        if (inputActions != null)
        {
            SerializedObject serialized = new SerializedObject(menu);
            serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning($"{InputActionsPath} が見つかりません。入力の設定は手で入れてください。");
        }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"{PrefabPath} を作りました。");

        return saved;
    }
}
