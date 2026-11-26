using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// イベント開始の警告バナーのプレハブ・検証シーンを作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/EventBannerUI.md
/// </summary>
public static class EventBannerUISetup
{
    private const string PrefabPath = "Assets/Resources/EventBannerUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/EventBannerUITest.unity";
    private const string BannerPath = "Assets/Art/Sprites/EventUI/UI_EventBanner_base.png";

    // 置き場所（1920×1080 の画面での値）。デザイン案の写真のとおり、画面の真ん中より下に、横いっぱいに出す
    private const float BannerY = -280f;          // 画面の真ん中からの高さ（下がマイナス）
    private const float BannerOverhang = 40f;     // 帯の左右の端（影のついた端）を、画面の外へどれだけ出すか
    private const int FontSize = 96;

    /// <summary>プレハブを作り直してから、それを置いただけの検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/イベントの警告バナーの検証シーンを作る")]
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
        instance.name = "EventBannerUI";
        var so = new SerializedObject(instance.GetComponent<EventBannerUI>());
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"イベントの警告バナーのプレハブと検証シーンを作りました: {PrefabPath} / {ScenePath}");
    }

    /// <summary>プレハブだけを作り直す（検証シーンはそのまま）。</summary>
    [MenuItem("Tools/StarSweepers/イベントの警告バナーのプレハブを更新")]
    public static void UpdatePrefabOnly()
    {
        if (BuildPrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"イベントの警告バナーのプレハブを更新しました: {PrefabPath}");
        }
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        var bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BannerPath);
        if (bannerSprite == null)
        {
            Debug.LogError($"帯の画像が読めません（無いか、Texture Type が Sprite でない）: {BannerPath}");
            return null;
        }

        // ---- 一番上：画面全体を覆う Canvas ----
        var root = new GameObject("EventBannerUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30; // デブリセットのUI（20）より手前、始まりの演出（85）・ポーズ画面（100）より奥

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<EventBannerUI>();

        RectTransform content = CreateRect("Content", root.transform);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        // 帯：横は画面いっぱい（端は画面の外）、高さは画像のまま
        RectTransform bannerRect = CreateRect("Banner", content);
        bannerRect.anchorMin = new Vector2(0f, 0.5f);
        bannerRect.anchorMax = new Vector2(1f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.sizeDelta = new Vector2(BannerOverhang * 2f, bannerSprite.rect.height);
        bannerRect.anchoredPosition = new Vector2(0f, BannerY);
        var banner = bannerRect.gameObject.AddComponent<Image>();
        banner.sprite = bannerSprite;
        banner.raycastTarget = false;

        // 文字：帯の真ん中。帯とは別に動かす（帯が開くときにつぶれないように、帯の子にしない）
        RectTransform labelRect = CreateRect("Label", content);
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(1200f, bannerSprite.rect.height);
        labelRect.anchoredPosition = new Vector2(0f, BannerY);
        var label = labelRect.gameObject.AddComponent<Text>();
        label.text = "緊急連絡！";
        label.fontSize = FontSize;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        var shadow = labelRect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(3f, -3f);

        var so = new SerializedObject(ui);
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        so.FindProperty("banner").objectReferenceValue = banner;
        so.FindProperty("label").objectReferenceValue = label;
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
}
