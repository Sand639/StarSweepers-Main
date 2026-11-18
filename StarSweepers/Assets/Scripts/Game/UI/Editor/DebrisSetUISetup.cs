using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// デブリセットのUIのプレハブ・検証シーン・物資の仮の画像を作るメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/DebrisSetUI.md
/// </summary>
public static class DebrisSetUISetup
{
    private const string PrefabPath = "Assets/Resources/DebrisSetUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/DebrisSetUITest.unity";
    private const string EventSceneName = "EventUI_SetDebris";
    private const string EventScenePath = "Assets/Scenes/Test/EventUI_SetDebris.unity";

    // デザイナーさんの素材（イベント「セット」のUI）。重なり順は 奥から 枠(04) → カード(02) → 白いカード(01) → チェック(00)
    private const string SpriteFolder = "Assets/Art/Sprites/EventUI/SetDebris/";
    private const string CheckPath = SpriteFolder + "UI_SET_00.png";
    private const string CoverPath = SpriteFolder + "UI_SET_01.png";
    private const string CardPath = SpriteFolder + "UI_SET_02.png";
    private const string ArmPath = SpriteFolder + "UI_SET_03_01.png";
    private const string BracketPath = SpriteFolder + "UI_SET_03_02.png";
    private const string PanelPath = SpriteFolder + "UI_SET_04_01.png";

    // 物資の仮の画像。**同じ名前の本素材で上書きすれば、そのまま差し替わる**（無いときだけ描いて作る）
    private static string ItemPath(int kind) => $"Assets/Art/Sprites/DebrisSetItem{kind + 1}.png";

    // カードの上に載せる物資の画像の大きさ
    private const float ItemSize = 72f;

    // 1チームの表示の大きさ（画像の元の大きさに対する倍率。1920×1080 の画面での値）と置き場所
    private const float PanelScale = 0.75f;
    private const float PanelSide = 10f;
    private const float PanelTop = 150f;
    private const float PanelRowGap = 160f;

    // プレハブを開いたときに見本として出すカードの数（ゲーム中はお題の数に合わせる）
    private const int PreviewSlots = 3;

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

        PlaceTestInstance(prefab);

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

    /// <summary>
    /// EventUI_SetDebris シーン（大槻さんが用意した検証用のシーン）に、テストモードのデブリセットUIを置いて保存する。
    /// もう置いてあれば何もしない。
    /// </summary>
    [MenuItem("Tools/StarSweepers/デブリセットUIをEventUI_SetDebrisシーンに置く")]
    public static void PlaceInEventScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"{PrefabPath} がありません。先に「デブリセットUIのプレハブを更新」を実行してください。");
            return;
        }

        var scene = EditorSceneManager.OpenScene(EventScenePath, OpenSceneMode.Single);
        if (Object.FindAnyObjectByType<DebrisSetUI>(FindObjectsInactive.Include) != null)
        {
            Debug.Log($"{EventSceneName} には、もうデブリセットUIが置いてあります。");
            return;
        }

        PlaceTestInstance(prefab);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"{EventSceneName} にデブリセットUI（テストモード）を置きました: {EventScenePath}");
    }

    /// <summary>今のシーンにプレハブを置き、ラウンドが無くても動くようにテストモードをオンにする（プレハブ側はオフのまま）。</summary>
    private static void PlaceTestInstance(GameObject prefab)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "DebrisSetUI";

        var so = new SerializedObject(instance.GetComponent<DebrisSetUI>());
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        Sprite arm = LoadSprite(ArmPath);
        Sprite bracket = LoadSprite(BracketPath);
        Sprite panelSprite = LoadSprite(PanelPath);
        Sprite card = LoadSprite(CardPath);
        Sprite cover = LoadSprite(CoverPath);
        Sprite check = LoadSprite(CheckPath);
        if (arm == null || bracket == null || panelSprite == null || card == null || cover == null || check == null)
        {
            Debug.LogError($"デブリセットUIの素材が {SpriteFolder} に足りません（UI_SET_00〜04_01）。");
            return null;
        }

        var itemSprites = new Sprite[DebrisSetUI.ItemKinds];
        for (int kind = 0; kind < itemSprites.Length; kind++)
        {
            int k = kind;
            itemSprites[kind] = EnsureSprite(ItemPath(kind), 128, 128, (x, y) => DrawItem(k, x, y));
        }

        var sprites = new PanelSprites
        {
            arm = arm, bracket = bracket, panel = panelSprite, card = card, cover = cover, check = check, item = itemSprites,
        };

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
        Stretch(content);

        var so = new SerializedObject(ui);
        SerializedProperty panels = so.FindProperty("panels");
        panels.arraySize = DebrisSetUI.MaxTeams;

        for (int p = 0; p < DebrisSetUI.MaxTeams; p++)
        {
            BuildPanel(p, content, sprites, panels.GetArrayElementAtIndex(p));
        }

        so.FindProperty("content").objectReferenceValue = content.gameObject;
        SetArray(so.FindProperty("itemSprites"), itemSprites);
        so.FindProperty("cardSprite").objectReferenceValue = card;
        so.FindProperty("plainCardSprite").objectReferenceValue = cover;
        so.FindProperty("testMode").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 開いたときに形が分かるよう、開ききった見本の並びにしておく
        ui.LayoutPreview();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private sealed class PanelSprites
    {
        public Sprite arm, bracket, panel, card, cover, check;
        public Sprite[] item;
    }

    /// <summary>1チームぶん（腕・枠・金具・文字・カード）を作り、スクリプトの欄に割り当てる。</summary>
    private static void BuildPanel(int index, Transform parent, PanelSprites sprites, SerializedProperty property)
    {
        bool right = index % 2 == 1;
        float top = PanelTop + PanelRowGap * (index / 2);

        // 画面の角に付ける。中の物の位置は DebrisSetUI が毎フレーム決める
        RectTransform root = CreateRect($"Panel{index}", parent);
        Vector2 corner = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        root.anchorMin = corner;
        root.anchorMax = corner;
        root.pivot = corner;
        root.sizeDelta = Vector2.zero;
        root.anchoredPosition = new Vector2(right ? -PanelSide : PanelSide, -top);
        root.localScale = new Vector3(PanelScale, PanelScale, 1f);

        // 重なり順：枠 → 文字 → カード → 金具 → 腕（あとに作った物ほど手前）
        Image panel = AddImage(CreateRect("Frame", root), sprites.panel);
        panel.SetNativeSize();
        panel.type = Image.Type.Filled;
        panel.fillMethod = Image.FillMethod.Horizontal;

        RectTransform titleRect = CreateRect("Title", root);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(320f, 34f);
        var title = titleRect.gameObject.AddComponent<Text>();
        title.text = "特注依頼...";
        title.fontSize = 24;
        title.fontStyle = FontStyle.Bold;
        title.color = Color.white;
        title.alignment = TextAnchor.UpperLeft;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        title.raycastTarget = false;

        property.FindPropertyRelative("root").objectReferenceValue = root.gameObject;
        property.FindPropertyRelative("panel").objectReferenceValue = panel;
        property.FindPropertyRelative("title").objectReferenceValue = title;
        SerializedProperty slots = property.FindPropertyRelative("slots");
        slots.arraySize = DebrisSetUI.MaxSlots;

        for (int n = 0; n < DebrisSetUI.MaxSlots; n++)
        {
            RectTransform slot = CreateRect($"Slot{n}", root);
            slot.sizeDelta = Vector2.zero;
            slot.gameObject.SetActive(n < PreviewSlots);

            SerializedProperty slotProperty = slots.GetArrayElementAtIndex(n);
            slotProperty.FindPropertyRelative("root").objectReferenceValue = slot;
            BuildCard("Card", slot, sprites, sprites.item[n % sprites.item.Length], slotProperty.FindPropertyRelative("card"));
            BuildCard("NextCard", slot, sprites, null, slotProperty.FindPropertyRelative("nextCard")).gameObject.SetActive(false);
        }

        Image bracket = AddImage(CreateRect("Bracket", root), sprites.bracket);
        bracket.SetNativeSize();

        Image arm = AddImage(CreateRect("Arm", root), sprites.arm);
        arm.SetNativeSize();

        property.FindPropertyRelative("arm").objectReferenceValue = arm;
        property.FindPropertyRelative("bracket").objectReferenceValue = bracket;
    }

    /// <summary>カード1枚（カード → 物資 → 白いカード → チェック）を作り、欄に割り当てる。</summary>
    private static RectTransform BuildCard(string name, Transform parent, PanelSprites sprites, Sprite item, SerializedProperty property)
    {
        RectTransform root = CreateRect(name, parent);
        root.sizeDelta = new Vector2(sprites.card.rect.width, sprites.card.rect.height);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Image background = AddImage(CreateRect("Base", root), sprites.card);
        background.SetNativeSize();

        RectTransform itemRect = CreateRect("Item", root);
        itemRect.sizeDelta = new Vector2(ItemSize, ItemSize);
        Image itemImage = AddImage(itemRect, item);
        itemImage.preserveAspect = true;

        Image cover = AddImage(CreateRect("Cover", root), sprites.cover);
        cover.SetNativeSize();
        cover.gameObject.SetActive(false);

        Image check = AddImage(CreateRect("Check", root), sprites.check);
        check.SetNativeSize();
        check.gameObject.SetActive(false);

        property.FindPropertyRelative("root").objectReferenceValue = root;
        property.FindPropertyRelative("group").objectReferenceValue = group;
        property.FindPropertyRelative("background").objectReferenceValue = background;
        property.FindPropertyRelative("item").objectReferenceValue = itemImage;
        property.FindPropertyRelative("cover").objectReferenceValue = cover;
        property.FindPropertyRelative("check").objectReferenceValue = check;
        return root;
    }

    // ---------------------------------------------------------------- 部品づくりの手伝い

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError($"画像が読めません（無いか、Texture Type が Sprite でない）: {path}");
        }

        return sprite;
    }

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

    // ---------------------------------------------------------------- 物資の仮の画像

    /// <summary>画像が無ければ、draw で1ピクセルずつ描いて保存する。**もうあれば（本素材も）そのまま使う。**</summary>
    private static Sprite EnsureSprite(string path, int width, int height, Func<int, int, Color> draw)
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
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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

    /// <summary>形の外側からの距離（内側は負）から、縁取りした色を出す（縁は1ピクセルぶんぼかす）。</summary>
    private static Color Outlined(float distance, float outline, Color fill, Color edge)
    {
        float outer = Mathf.Clamp01(0.5f - distance);
        float inner = Mathf.Clamp01(0.5f - (distance + outline));
        Color color = Color.Lerp(edge, fill, inner);
        color.a = outer;
        return color;
    }

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
