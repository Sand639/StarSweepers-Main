using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 高単価デブリのルーレットUIの仮の画像・プレハブ・検証シーンを作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/HighValueRouletteUI.md
/// </summary>
public static class HighValueRouletteUISetup
{
    private const string PrefabPath = "Assets/Resources/HighValueRouletteUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/HighValueRouletteUITest.unity";
    private const string ScoreBarPrefabPath = "Assets/Prefabs/UI/TeamScoreBarUI.prefab";

    // 仮のフレームの画像。**同じ名前の本素材で上書きすれば、そのまま差し替わる**（無いときだけ描いて作る）
    private const string FramePath = "Assets/Art/Sprites/HighValueRouletteFrame.png";

    // 物資の画像は、デブリセットのUIと同じ画像を使う（1回差し替えれば両方に効く）
    private static string ItemPath(int kind) => $"Assets/Art/Sprites/DebrisSetItem{kind + 1}.png";

    // 大きさ（1920×1080 の画面での値）。得点バーは画面の上から 48〜92、時間の丸は 34〜130
    private const float ClipTop = 48f;                                   // ここより上は切り抜く（得点バーの上の端）
    private static readonly Vector2 ClipSize = new Vector2(800f, 420f);
    private const float WheelRadius = 260f;
    private const float WheelCenterAboveClip = 88f;                      // フレームの中心は画面の上の端より 40 上
    private const float RailRadius = 200f;
    private const float ItemSize = 64f;
    private const float ItemAngle = 45f;

    // 仮のフレームの画像の大きさ
    private const int FrameTextureSize = 512;

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

        // ルーレットは時間の丸の下から出るので、得点バーも置いて並びを見られるようにする
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

        Sprite frameSprite = EnsureFrameSprite();

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

        RectTransform wheel = CreateRect("Wheel", clip);
        wheel.anchorMin = new Vector2(0.5f, 1f);
        wheel.anchorMax = new Vector2(0.5f, 1f);
        wheel.pivot = new Vector2(0.5f, 0.5f);
        wheel.sizeDelta = new Vector2(WheelRadius * 2f, WheelRadius * 2f);
        wheel.anchoredPosition = new Vector2(0f, WheelCenterAboveClip);

        RectTransform frameRect = CreateRect("Frame", wheel);
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        Image frame = AddImage(frameRect, frameSprite);
        frame.preserveAspect = true;

        var items = new Image[HighValueRouletteUI.RailItems];
        int pad = items.Length / 2;
        for (int s = 0; s < items.Length; s++)
        {
            RectTransform item = CreateRect($"Item{s}", wheel);
            item.anchorMin = new Vector2(0.5f, 0.5f);
            item.anchorMax = new Vector2(0.5f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(ItemSize, ItemSize);

            // プレハブを開いたときの見本：真ん中が真下、左右に 45 度ずつ（ゲーム中はスクリプトが動かす）
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

    // ---------------------------------------------------------------- 仮の画像

    /// <summary>
    /// 仮のフレーム：薄い灰色の円盤に、濃い灰色の縁と、物資が乗るレールの線。
    /// **画像がもうあれば（本素材も）そのまま使う。**
    /// </summary>
    private static Sprite EnsureFrameSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        if (existing != null) return existing;

        const int size = FrameTextureSize;
        float scale = size * 0.5f / WheelRadius;
        float outer = size * 0.5f - 1f;
        float rail = RailRadius * scale;
        var center = new Vector2(size * 0.5f, size * 0.5f);
        var fill = new Color(0.85f, 0.85f, 0.86f, 0.92f);
        var edge = new Color(0.45f, 0.45f, 0.48f, 1f);
        var railColor = new Color(0.66f, 0.66f, 0.69f, 0.92f);

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                // レールの線（太さ 4 ピクセル。縁は1ピクセルぶんぼかす）
                float onRail = Mathf.Clamp01(2.5f - Mathf.Abs(distance - rail));
                Color color = Color.Lerp(fill, railColor, onRail);

                // 外側の縁（太さ 6 ピクセル）と、円の外の透明
                color = Color.Lerp(edge, color, Mathf.Clamp01(outer - 6f - distance + 0.5f));
                color.a *= Mathf.Clamp01(outer - distance + 0.5f);
                texture.SetPixel(x, y, color);
            }
        }

        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), FramePath), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(FramePath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(FramePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
    }
}
