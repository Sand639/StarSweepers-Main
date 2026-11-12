using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// デブリセットのUIの仮の画像・プレハブ・検証シーンを作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/DebrisSetUI.md
/// </summary>
public static class DebrisSetUISetup
{
    private const string PrefabPath = "Assets/Resources/DebrisSetUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/DebrisSetUITest.unity";

    // 仮の画像。**同じ名前の本素材で上書きすれば、そのまま差し替わる**（無いときだけ描いて作る）
    private const string FramePath = "Assets/Art/Sprites/DebrisSetFrame.png";
    private const string CheckPath = "Assets/Art/Sprites/DebrisSetCheck.png";
    private static string ItemPath(int kind) => $"Assets/Art/Sprites/DebrisSetItem{kind + 1}.png";

    // 大きさ（1920×1080 の画面での値。CanvasScaler で画面に合わせて拡大・縮小される）
    private const float SlotSize = 58f;
    private const float SlotSpacing = 9f;
    private const int PaddingX = 12;
    private const int PaddingY = 10;
    private const float CheckSize = 30f;
    private static readonly Vector2 CheckOffset = new Vector2(-6f, 8f);

    // プレハブを開いたときに見本として出すマスの数（ゲーム中はお題の数に合わせる。お題の初期値の3個に合わせた）
    private const int PreviewSlots = 3;

    // 枠の置き場所（左上の枠は画面の左上から、右上の枠は画面の右上から）
    private const float PanelSide = 90f;
    private const float PanelTop = 190f;
    private const float PanelRowGap = 100f;

    // 仮のフレームの画像の大きさと、9スライスの幅
    private const int FrameWidth = 160;
    private const int FrameHeight = 80;
    private const int FrameRadius = 22;
    private const int FrameBorder = 26;

    /// <summary>プレハブを作り直してから、それを置いただけの検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/デブリセットUIの検証シーンを作る")]
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
        instance.name = "DebrisSetUI";

        // 検証シーンでは、ラウンドが無くても動くようにテストモードをオンにする（プレハブ側はオフのまま）
        var so = new SerializedObject(instance.GetComponent<DebrisSetUI>());
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"デブリセットUIのプレハブと検証シーンを作りました: {PrefabPath} / {ScenePath}");
    }

    /// <summary>プレハブだけを作り直す（検証シーンはそのまま）。</summary>
    [MenuItem("Tools/StarSweepers/デブリセットUIのプレハブを更新")]
    public static void UpdatePrefabOnly()
    {
        if (BuildPrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"デブリセットUIのプレハブを更新しました: {PrefabPath}");
        }
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        Sprite frameSprite = EnsureSprite(FramePath, FrameWidth, FrameHeight, DrawFrame, FrameBorder);
        Sprite checkSprite = EnsureSprite(CheckPath, 128, 128, DrawCheck, 0);
        var itemSprites = new Sprite[DebrisSetUI.ItemKinds];
        for (int kind = 0; kind < itemSprites.Length; kind++)
        {
            int k = kind;
            itemSprites[kind] = EnsureSprite(ItemPath(kind), 128, 128, (x, y) => DrawItem(k, x, y), 0);
        }

        // ---- 一番上：画面全体を覆う Canvas ----
        var root = new GameObject("DebrisSetUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // 得点バー（10）より手前、始まりの演出（85）・ポーズ画面（100）より奥

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<DebrisSetUI>();

        RectTransform content = CreateRect("Content", root.transform);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;

        var so = new SerializedObject(ui);
        SerializedProperty panels = so.FindProperty("panels");
        panels.arraySize = DebrisSetUI.MaxTeams;

        for (int p = 0; p < DebrisSetUI.MaxTeams; p++)
        {
            BuildPanel(p, content, frameSprite, checkSprite, itemSprites, panels.GetArrayElementAtIndex(p));
        }

        so.FindProperty("content").objectReferenceValue = content.gameObject;
        so.FindProperty("frameSprite").objectReferenceValue = frameSprite;
        so.FindProperty("checkSprite").objectReferenceValue = checkSprite;
        SetArray(so.FindProperty("itemSprites"), itemSprites);
        so.FindProperty("testMode").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>1チームぶんの枠（フレーム＋横並びのマス）を作り、スクリプトの欄に割り当てる。</summary>
    private static void BuildPanel(int index, Transform parent, Sprite frameSprite, Sprite checkSprite, Sprite[] itemSprites,
        SerializedProperty property)
    {
        bool right = index % 2 == 1;
        float top = PanelTop + PanelRowGap * (index / 2);

        RectTransform panel = CreateRect($"Panel{index}", parent);
        Vector2 corner = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        panel.anchorMin = corner;
        panel.anchorMax = corner;
        panel.pivot = corner; // 物資の数が変わると、右の枠は左へ、左の枠は右へ伸びる
        panel.anchoredPosition = new Vector2(right ? -PanelSide : PanelSide, -top);
        panel.sizeDelta = new Vector2(PaddingX * 2 + SlotSize * PreviewSlots + SlotSpacing * (PreviewSlots - 1), PaddingY * 2 + SlotSize);

        Image frame = AddImage(panel, frameSprite);
        frame.type = Image.Type.Sliced;

        var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(PaddingX, PaddingX, PaddingY, PaddingY);
        layout.spacing = SlotSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        property.FindPropertyRelative("root").objectReferenceValue = panel.gameObject;
        property.FindPropertyRelative("frame").objectReferenceValue = frame;
        SerializedProperty slots = property.FindPropertyRelative("slots");
        slots.arraySize = DebrisSetUI.MaxSlots;

        for (int n = 0; n < DebrisSetUI.MaxSlots; n++)
        {
            RectTransform slot = CreateRect($"Slot{n}", panel);
            slot.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.gameObject.SetActive(n < PreviewSlots);

            // 流れる物資を、マスの形で切り抜く
            RectTransform reel = CreateRect("Reel", slot);
            Stretch(reel);
            reel.gameObject.AddComponent<RectMask2D>();

            RectTransform item = CreateRect("Item", reel);
            Stretch(item);
            Image itemImage = AddImage(item, itemSprites[n % itemSprites.Length]);
            itemImage.preserveAspect = true;

            RectTransform next = CreateRect("NextItem", reel);
            Stretch(next);
            Image nextImage = AddImage(next, null);
            nextImage.preserveAspect = true;
            next.gameObject.SetActive(false);

            // チェックは切り抜かない（マスの右下から少しはみ出す）
            RectTransform check = CreateRect("Check", slot);
            check.anchorMin = new Vector2(1f, 0f);
            check.anchorMax = new Vector2(1f, 0f);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.sizeDelta = new Vector2(CheckSize, CheckSize);
            check.anchoredPosition = CheckOffset;
            Image checkImage = AddImage(check, checkSprite);
            checkImage.preserveAspect = true;
            check.gameObject.SetActive(false);

            SerializedProperty slotProperty = slots.GetArrayElementAtIndex(n);
            slotProperty.FindPropertyRelative("root").objectReferenceValue = slot;
            slotProperty.FindPropertyRelative("item").objectReferenceValue = itemImage;
            slotProperty.FindPropertyRelative("nextItem").objectReferenceValue = nextImage;
            slotProperty.FindPropertyRelative("check").objectReferenceValue = checkImage;
        }
    }

    // ---------------------------------------------------------------- 部品づくりの手伝い

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    /// <summary>画像が無ければ、draw で1ピクセルずつ描いて保存する。**もうあれば（本素材も）そのまま使う。**</summary>
    private static Sprite EnsureSprite(string path, int width, int height, Func<int, int, Color> draw, int border)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                texture.SetPixel(x, y, draw(x, y));
            }
        }

        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>仮のフレーム：黄緑の角丸の四角に黒い縁（設計図の SVG と同じ色）。</summary>
    private static Color DrawFrame(int x, int y)
    {
        var p = new Vector2(x + 0.5f, y + 0.5f);
        var half = new Vector2(FrameWidth * 0.5f - 1f, FrameHeight * 0.5f - 1f);
        Vector2 q = Abs(p - new Vector2(FrameWidth * 0.5f, FrameHeight * 0.5f)) - half + Vector2.one * FrameRadius;
        float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - FrameRadius;
        return Outlined(distance, 3f, new Color(0.35f, 0.95f, 0.30f), new Color(0.05f, 0.05f, 0.05f));
    }

    /// <summary>仮の物資：素材の色の丸に、物資の番号のぶんだけ点（サイコロの目）。</summary>
    private static Color DrawItem(int kind, int x, int y)
    {
        var p = new Vector2(x + 0.5f, y + 0.5f);
        var center = new Vector2(64f, 64f);
        Color fill = SpaceJunkMaterials.Color(SpaceJunkMaterials.FromIndex(kind));
        Color color = Outlined(Vector2.Distance(p, center) - 60f, 5f, fill, new Color(0.08f, 0.08f, 0.08f));

        // 明るい色の物資には黒い点、暗い色の物資には白い点
        float brightness = fill.r * 0.3f + fill.g * 0.59f + fill.b * 0.11f;
        Color pip = brightness > 0.5f ? new Color(0.08f, 0.08f, 0.08f) : Color.white;

        foreach (Vector2 offset in PipOffsets(kind + 1))
        {
            float coverage = Mathf.Clamp01(0.5f - (Vector2.Distance(p, center + offset * 26f) - 10f));
            color = new Color(
                Mathf.Lerp(color.r, pip.r, coverage),
                Mathf.Lerp(color.g, pip.g, coverage),
                Mathf.Lerp(color.b, pip.b, coverage),
                color.a);
        }

        return color;
    }

    /// <summary>仮のチェックマーク：緑のチェックに濃い縁。</summary>
    private static Color DrawCheck(int x, int y)
    {
        var p = new Vector2(x + 0.5f, y + 0.5f);
        var a = new Vector2(24f, 66f);
        var b = new Vector2(52f, 34f);
        var c = new Vector2(106f, 98f);
        float distance = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)) - 16f;
        return Outlined(distance, 6f, new Color(0.30f, 0.95f, 0.40f), new Color(0.03f, 0.22f, 0.08f));
    }

    /// <summary>形の外側からの距離（内側は負）から、縁取りした色を出す（縁は1ピクセルぶんぼかす）。</summary>
    private static Color Outlined(float distance, float outline, Color fill, Color edge)
    {
        float outer = Mathf.Clamp01(0.5f - distance);
        float inner = Mathf.Clamp01(0.5f - (distance + outline));
        Color color = Color.Lerp(edge, fill, inner);
        color.a = outer;
        return color;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    private static Vector2 Abs(Vector2 v) => new Vector2(Mathf.Abs(v.x), Mathf.Abs(v.y));

    /// <summary>サイコロの目の位置（1〜5）。</summary>
    private static Vector2[] PipOffsets(int count)
    {
        var center = Vector2.zero;
        var ul = new Vector2(-1f, 1f);
        var ur = new Vector2(1f, 1f);
        var dl = new Vector2(-1f, -1f);
        var dr = new Vector2(1f, -1f);

        switch (count)
        {
            case 1: return new[] { center };
            case 2: return new[] { ur, dl };
            case 3: return new[] { ur, center, dl };
            case 4: return new[] { ul, ur, dl, dr };
            default: return new[] { ul, ur, center, dl, dr };
        }
    }
}
