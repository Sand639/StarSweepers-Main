using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 高単価デブリのルーレットUIのプレハブ・検証シーンを作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/HighValueRouletteUI.md
/// </summary>
public static class HighValueRouletteUISetup
{
    private const string PrefabPath = "Assets/Resources/HighValueRouletteUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/HighValueRouletteUITest.unity";
    private const string ScoreBarPrefabPath = "Assets/Prefabs/UI/TeamScoreBarFrameUI.prefab"; // ステージで使っている得点バー

    // フレームの画像（デザイナーさんの素材。半円の上の辺の真ん中が、物資が回る中心）
    private const string FramePath = "Assets/Art/Sprites/EventUI/HighValueDebris/UI_roulette.png";

    // 物資の画像は、デブリセットのUIと同じ画像を使う（1回差し替えれば両方に効く）
    private static string ItemPath(int kind) => $"Assets/Art/Sprites/DebrisSetItem{kind + 1}.png";

    // 大きさ（1920×1080 の画面での値）。得点バー（フレーム版）は、横長の枠が画面の上から 110〜217、
    // 時間の板は 176〜230 にある（Assets/Prefabs/UI/TeamScoreBarFrameUI.prefab）
    private const float ClipTop = 110f;                                  // ここより上は切り抜く（得点バーの横長の枠の上の端）
    private static readonly Vector2 ClipSize = new Vector2(800f, 420f);
    private const float WheelTop = 90f;                                  // 半円の上の端を、切り抜きの上の端からどれだけ下に置くか（上の端は時間の板の裏に隠れ、左右の物資は得点バーの下に出る）
    private const float HubFromTop = 8f;                                 // 画像の上の端から、半円の上の辺（回る中心）までの距離
    private const float RailRadius = 92f;                                // 真下の物資が、真ん中の三角の中に収まる
    private const float ItemSize = 64f;
    private const float ItemAngle = 64f;                                 // となりの物資が、三角の左右の扇の真ん中に来る

    /// <summary>プレハブを作り直してから、得点バーと一緒に置いた検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/高単価デブリのルーレットUIの検証シーンを作る")]
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

        // ルーレットは得点バーの時間の板の下から出るので、得点バーも置いて並びを見られるようにする
        var scoreBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScoreBarPrefabPath);
        if (scoreBarPrefab != null)
        {
            var bar = (GameObject)PrefabUtility.InstantiatePrefab(scoreBarPrefab);
            bar.name = "TeamScoreBarUI";
            SetTestMode(bar.GetComponent<TeamScoreBarUI>());
        }
        else
        {
            Debug.LogWarning($"{ScoreBarPrefabPath} が無いので、検証シーンには得点バーを置きません。");
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "HighValueRouletteUI";
        SetTestMode(instance.GetComponent<HighValueRouletteUI>());

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"高単価デブリのルーレットUIのプレハブと検証シーンを作りました: {PrefabPath} / {ScenePath}");
    }

    /// <summary>プレハブだけを作り直す（検証シーンはそのまま）。</summary>
    [MenuItem("Tools/StarSweepers/高単価デブリのルーレットUIのプレハブを更新")]
    public static void UpdatePrefabOnly()
    {
        if (BuildPrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"高単価デブリのルーレットUIのプレハブを更新しました: {PrefabPath}");
        }
    }

    /// <summary>検証シーンでは、ラウンドが無くても動くようにテストモードをオンにする（プレハブ側はオフのまま）。</summary>
    private static void SetTestMode(Object component)
    {
        if (component == null) return;
        var so = new SerializedObject(component);
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        var itemSprites = new Sprite[HighValueRouletteUI.ItemKinds];
        for (int kind = 0; kind < itemSprites.Length; kind++)
        {
            itemSprites[kind] = AssetDatabase.LoadAssetAtPath<Sprite>(ItemPath(kind));
            if (itemSprites[kind] == null)
            {
                Debug.LogError($"{ItemPath(kind)} が無い。先に Tools > StarSweepers > デブリセットUIのプレハブを更新 で物資の画像を作ってください。");
                return null;
            }
        }

        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        if (frameSprite == null)
        {
            Debug.LogError($"ルーレットのフレームの画像が読めません（無いか、Texture Type が Sprite でない）: {FramePath}");
            return null;
        }

        // ---- 一番上：画面全体を覆う Canvas ----
        var root = new GameObject("HighValueRouletteUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9; // 得点バー（10）のすぐ奥。バーと時間の丸の裏から出てくるように見せる

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<HighValueRouletteUI>();

        RectTransform content = CreateRect("Content", root.transform);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        // 得点バーの上の端より上は切り抜く（画面の上の端に、フレームの上半分がはみ出して見えないように）
        RectTransform clip = CreateRect("Clip", content);
        clip.anchorMin = new Vector2(0.5f, 1f);
        clip.anchorMax = new Vector2(0.5f, 1f);
        clip.pivot = new Vector2(0.5f, 1f);
        clip.sizeDelta = ClipSize;
        clip.anchoredPosition = new Vector2(0f, -ClipTop);
        clip.gameObject.AddComponent<RectMask2D>();

        // フレームの画像の大きさのまま、上の端を時間の板の裏に置く
        RectTransform wheel = CreateRect("Wheel", clip);
        wheel.anchorMin = new Vector2(0.5f, 1f);
        wheel.anchorMax = new Vector2(0.5f, 1f);
        wheel.pivot = new Vector2(0.5f, 1f);
        wheel.sizeDelta = new Vector2(frameSprite.rect.width, frameSprite.rect.height);
        wheel.anchoredPosition = new Vector2(0f, -WheelTop);

        RectTransform frameRect = CreateRect("Frame", wheel);
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        Image frame = AddImage(frameRect, frameSprite);
        frame.preserveAspect = true;

        // 物資は半円の中だけに見せる（上の辺より上へ回った物資は切り抜く）
        RectTransform window = CreateRect("Window", wheel);
        window.anchorMin = Vector2.zero;
        window.anchorMax = Vector2.one;
        window.offsetMin = Vector2.zero;
        window.offsetMax = new Vector2(0f, -HubFromTop);
        window.gameObject.AddComponent<RectMask2D>();

        // 物資の位置は、Window の上の辺の真ん中（回る中心）から測る
        var items = new Image[HighValueRouletteUI.RailItems];
        int pad = items.Length / 2;
        for (int s = 0; s < items.Length; s++)
        {
            RectTransform item = CreateRect($"Item{s}", window);
            item.anchorMin = new Vector2(0.5f, 1f);
            item.anchorMax = new Vector2(0.5f, 1f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(ItemSize, ItemSize);

            // プレハブを開いたときの見本：真ん中が真下、左右に ItemAngle 度ずつ（ゲーム中はスクリプトが動かす）
            item.anchoredPosition = HighValueRouletteUI.RailPoint((pad - s) * ItemAngle, RailRadius);
            items[s] = AddImage(item, itemSprites[s % itemSprites.Length]);
            items[s].preserveAspect = true;
        }

        var so = new SerializedObject(ui);
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        so.FindProperty("wheel").objectReferenceValue = wheel;
        so.FindProperty("frame").objectReferenceValue = frame;
        so.FindProperty("frameSprite").objectReferenceValue = frameSprite;
        SetArray(so.FindProperty("itemSprites"), itemSprites);
        SetArray(so.FindProperty("railItems"), items);
        so.FindProperty("railRadius").floatValue = RailRadius;
        so.FindProperty("itemAngle").floatValue = ItemAngle;
        so.FindProperty("testMode").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------------------------------------------------------------- 部品づくりの手伝い

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image AddImage(RectTransform rect, Sprite sprite)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false; // 表示だけなので、クリックを受けない
        return image;
    }

    private static void SetArray<T>(SerializedProperty property, T[] values) where T : Object
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
