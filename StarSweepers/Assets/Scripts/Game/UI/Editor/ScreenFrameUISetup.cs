using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 試合の画面の枠のプレハブ・検証シーンを作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/ScreenFrameUI.md
/// </summary>
public static class ScreenFrameUISetup
{
    private const string PrefabPath = "Assets/Resources/ScreenFrameUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/ScreenFrameUITest.unity";
    private const string FramePath = "Assets/Art/Sprites/UI_MainFrame_02.png";

    // 画像は 1925×1083 で、枠はその左上の 1920×1080 に描いてある（右に 5、下に 3 の透明な余白）。
    // 余白のぶんだけ画面の外へはみ出させて、枠のふちを画面のふちにぴったり合わせる
    private const float RightMargin = 5f;
    private const float BottomMargin = 3f;

    /// <summary>プレハブを作り直してから、それを置いただけの検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/画面の枠の検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject prefab = BuildPrefab();
        if (prefab == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
        cameraObject.tag = "MainCamera";

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "ScreenFrameUI";
        var so = new SerializedObject(instance.GetComponent<ScreenFrameUI>());
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"画面の枠のプレハブと検証シーンを作りました: {PrefabPath} / {ScenePath}");
    }

    /// <summary>プレハブだけを作り直す（検証シーンはそのまま）。</summary>
    [MenuItem("Tools/StarSweepers/画面の枠のプレハブを更新")]
    public static void UpdatePrefabOnly()
    {
        if (BuildPrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"画面の枠のプレハブを更新しました: {PrefabPath}");
        }
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        if (frameSprite == null)
        {
            Debug.LogError($"枠の画像が読めません（無いか、Texture Type が Sprite でない）: {FramePath}");
            return null;
        }

        // ---- 一番上：画面全体を覆う Canvas ----
        var root = new GameObject("ScreenFrameUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40; // 試合のUI（得点バー 10・デブリセット 20・警告の帯 30）より手前、始まりの演出（85）・ポーズ画面（100）より奥

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<ScreenFrameUI>();

        RectTransform content = CreateRect("Content", root.transform);
        Stretch(content, Vector2.zero, Vector2.zero);

        // 枠：画面いっぱい。角は引き伸ばさない（画像に 9 分割の境目を付けてある）ので、16:9 でない画面でも角の形は変わらない
        RectTransform frameRect = CreateRect("Frame", content);
        Stretch(frameRect, new Vector2(0f, -BottomMargin), new Vector2(RightMargin, 0f));
        var frame = frameRect.gameObject.AddComponent<Image>();
        frame.sprite = frameSprite;
        frame.type = frameSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        frame.raycastTarget = false;

        var so = new SerializedObject(ui);
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        so.FindProperty("testMode").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
