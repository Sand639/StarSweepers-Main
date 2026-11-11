using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// チーム得点バーのプレハブ・検証シーンを作り、ステージに置くメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/TeamScoreBarUI.md
/// </summary>
public static class TeamScoreBarUISetup
{
    private const string PrefabPath = "Assets/Prefabs/UI/TeamScoreBarUI.prefab";
    private const string ScenePath = "Assets/Scenes/Test/TeamScoreBarUITest.unity";
    private const string CirclePath = "Assets/Art/Sprites/TeamScoreBarCircle.png";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string FireShaderName = "StarSweepers/UI/TeamScoreBarFire";
    private const string FireMaterialPath = "Assets/Art/Materials/TeamScoreBarFire.mat";

    // 得点の数字用の、黒い縁取りのついた文字のマテリアル（中身の色はチームの色。TeamScoreBarUI が入れる）
    private const string NumberMaterialPath = "Assets/Art/Materials/TeamScoreNumber.mat";

    // バーと丸の大きさ（1920×1080 の画面での大きさ。CanvasScaler で画面に合わせて拡大・縮小される）
    // 炎がバーの上に立ちのぼるので、画面の上から少し離して置く（近すぎると炎の先が画面の外で切れる）
    private static readonly Vector2 BarSize = new Vector2(760f, 44f);
    private const float BarTop = 48f;
    private const float CircleSize = 96f;
    private const float CircleTop = 34f;

    // 帯の内側の余白（背景の黒い縁）
    private const float SegmentPadding = 3f;

    // 帯の Image を、帯の上へこれだけ高くする（炎の舌が立ちのぼる所）
    private const float FireRise = 44f;

    private static readonly Color BarBackColor = new Color(0.10f, 0.10f, 0.12f, 0.85f);
    private static readonly Color CircleColor = new Color(0.27f, 0.25f, 0.25f, 1f);

    /// <summary>プレハブを作り直してから、それを置いただけの検証シーンを作って保存する。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バーの検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject prefab = BuildPrefab();
        if (prefab == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 背景が真っ黒だと見づらいので、灰色の背景だけを映すカメラを置く
        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.35f, 0.35f, 0.37f);
        cameraObject.tag = "MainCamera";

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "TeamScoreBarUI";

        // 検証シーンでは、ラウンドが無くても動くようにテストモードをオンにする（プレハブ側はオフのまま）
        var so = new SerializedObject(instance.GetComponent<TeamScoreBarUI>());
        so.FindProperty("testMode").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"チーム得点バーのプレハブと検証シーンを作りました: {PrefabPath} / {ScenePath}");
    }

    /// <summary>プレハブだけを作り直す（検証シーンはそのまま）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バーのプレハブを更新")]
    public static void UpdatePrefabOnly()
    {
        if (BuildPrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"チーム得点バーのプレハブを更新しました: {PrefabPath}");
        }
    }

    /// <summary>得点バーを置くステージ。増やすときはここに足してメニューを実行する。</summary>
    private static readonly string[] StageScenePaths =
    {
        "Assets/Scenes/Stage/STAGE_Simple.unity",
        "Assets/Scenes/Stage/STAGE_Train.unity",
        "Assets/Scenes/Stage/STAGE_Meteor.unity",
        "Assets/Scenes/Stage/STAGE_Anchor.unity",
        "Assets/Scenes/Stage/STAGE_Island.unity",
        "Assets/Scenes/Stage/STAGE_Conveyor1.unity",
        "Assets/Scenes/Stage/STAGE_Glass1.unity",
        "Assets/Scenes/Stage/STAGE_Glass2.unity",
    };

    /// <summary>ステージに得点バーのプレハブを置く（置いてあるステージは飛ばす。ほかの物には触らない）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バーをステージに置く")]
    public static void PlaceInStages()
    {
        // 開いているシーンに保存していない変更があれば、先に聞く（ステージを順に開くと閉じてしまうため）
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            prefab = BuildPrefab();
            if (prefab == null) return;
        }

        string previousScene = EditorSceneManager.GetActiveScene().path;
        int placed = 0;

        foreach (string path in StageScenePaths)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                Debug.LogWarning($"[チーム得点バー] ステージが見つからないので飛ばしました: {path}");
                continue;
            }

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<TeamScoreBarUI>(FindObjectsInactive.Include) != null)
            {
                Debug.Log($"[チーム得点バー] すでに置いてあります: {path}");
                continue;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "TeamScoreBarUI";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            placed++;
            Debug.Log($"[チーム得点バー] 置きました: {path}");
        }

        // 元のシーンに戻す
        if (!string.IsNullOrEmpty(previousScene) && !Application.isBatchMode)
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        Debug.Log($"[チーム得点バー] ステージへの配置が終わりました（新しく置いた数: {placed}）");
    }

    /// <summary>部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildPrefab()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError($"TextMeshPro の標準フォントが見つかりません: {FontPath}\n" +
                           "`Window > TextMeshPro > Import TMP Essential Resources` で取り込んでから、もう一度実行してください。");
            return null;
        }

        Sprite circle = EnsureCircleSprite();
        Material fireMaterial = EnsureFireMaterial();
        if (fireMaterial == null) return null;
        Material numberMaterial = EnsureNumberMaterial(font);

        // ---- 一番上：画面全体を覆う Canvas（どのシーンに置いても単体で出るように、Canvas ごとプレハブにする）----
        var root = new GameObject("TeamScoreBarUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<TeamScoreBarUI>();
        root.AddComponent<TeamScoreFlameEffect>();

        // ---- 表示ごと隠すための子 ----
        RectTransform content = CreateRect("Content", root.transform);
        Stretch(content, 0f);

        // ---- バー（画面の上の真ん中）----
        RectTransform bar = CreateRect("Bar", content);
        AnchorTopCenter(bar, BarSize, BarTop);

        RectTransform back = CreateRect("Background", bar);
        Stretch(back, 0f);
        AddImage(back, BarBackColor, null);

        RectTransform segments = CreateRect("Segments", bar);
        Stretch(segments, SegmentPadding);

        var segmentImages = new Image[TeamScoreBarUI.MaxTeams];
        var scoreTexts = new TMP_Text[TeamScoreBarUI.MaxTeams];

        for (int i = 0; i < TeamScoreBarUI.MaxTeams; i++)
        {
            // 帯そのものが炎（シェーダーで描く）。炎の舌が立ちのぼるぶん、上へ FireRise だけ高くする
            RectTransform segment = CreateRect($"Segment{i}", segments);
            Stretch(segment, 0f);
            segment.offsetMax = new Vector2(0f, FireRise);
            segmentImages[i] = AddImage(segment, Color.white, null);
            segmentImages[i].material = fireMaterial;

            // 得点の数字は帯の子にする（帯と一緒に動く）。端から 14 だけ内側に寄せ、
            // 上は炎の舌の所を除いて、帯の高さの真ん中に来るようにする
            RectTransform label = CreateRect("Score", segment);
            Stretch(label, 0f);
            label.offsetMin = new Vector2(14f, 0f);
            label.offsetMax = new Vector2(-14f, -FireRise);
            scoreTexts[i] = AddText(label, font, 30f, "0");
            scoreTexts[i].fontSharedMaterial = numberMaterial;
        }

        // ---- 真ん中の丸と、残り時間 ----
        RectTransform timeCircle = CreateRect("TimeCircle", content);
        AnchorTopCenter(timeCircle, new Vector2(CircleSize, CircleSize), CircleTop);
        AddImage(timeCircle, CircleColor, circle);

        RectTransform timeLabel = CreateRect("TimeText", timeCircle);
        Stretch(timeLabel, 0f);
        TMP_Text timeText = AddText(timeLabel, font, 28f, "2:00");
        timeText.alignment = TextAlignmentOptions.Center;

        // ---- スクリプトに部品を割り当てる ----
        var so = new SerializedObject(ui);
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        SetArray(so.FindProperty("segmentImages"), segmentImages);
        SetArray(so.FindProperty("scoreTexts"), scoreTexts);
        so.FindProperty("timeText").objectReferenceValue = timeText;
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

    /// <summary>親いっぱいに広げる（padding だけ内側に縮める）。</summary>
    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>画面の上の真ん中に、上から top だけ下げて置く。</summary>
    private static void AnchorTopCenter(RectTransform rect, Vector2 size, float top)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(0f, -top);
    }

    private static Image AddImage(RectTransform rect, Color color, Sprite sprite)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.raycastTarget = false; // 表示だけなので、クリックを受けない
        return image;
    }

    private static TMP_Text AddText(RectTransform rect, TMP_FontAsset font, float size, string text)
    {
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Midline;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void SetArray<T>(SerializedProperty property, T[] values) where T : Object
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    /// <summary>帯の炎のマテリアルを用意する（あれば残し、_BarRatio だけ入れ直す）。</summary>
    private static Material EnsureFireMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FireMaterialPath);

        if (material == null)
        {
            Shader shader = Shader.Find(FireShaderName);
            if (shader == null)
            {
                Debug.LogError($"炎のシェーダーが見つかりません: {FireShaderName}（Assets/Art/Shaders/TeamScoreBarFire.shader）");
                return null;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, FireMaterialPath);
        }

        float barHeight = BarSize.y - SegmentPadding * 2f;
        material.SetFloat("_BarRatio", barHeight / (barHeight + FireRise));
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>得点の数字用の、黒い縁取りの文字のマテリアルを用意する（あればそのまま使う）。</summary>
    private static Material EnsureNumberMaterial(TMP_FontAsset font)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(NumberMaterialPath);
        if (existing != null) return existing;

        var material = new Material(font.material);
        material.SetFloat("_OutlineWidth", 0.5f);
        material.SetColor("_OutlineColor", Color.black);
        material.SetFloat("_FaceDilate", 0.25f); // 縁取りのぶん細く見えるので、文字を少し太らせる

        // TMP_SDF-Mobile は OUTLINE_ON を入れないと縁取りを描かない
        material.EnableKeyword("OUTLINE_ON");

        // 縁取りを太くしたぶんの余白の計算をやり直す（しないと、縁取りが途中で切れて細く見える）
        ShaderUtilities.GetShaderPropertyIDs();
        ShaderUtilities.UpdateShaderRatios(material);
        AssetDatabase.CreateAsset(material, NumberMaterialPath);
        return material;
    }

    /// <summary>真ん中の丸に使う白い丸の画像を用意する（無ければ 256×256 で描いて保存する）。</summary>
    private static Sprite EnsureCircleSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
        if (existing != null) return existing;

        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1) * 0.5f;
        float radius = size * 0.5f - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 縁を 1.5 ピクセルぶんぼかして、ギザギザを目立たなくする
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01((radius - distance) / 1.5f + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), CirclePath);
        File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(CirclePath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(CirclePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(CirclePath);
    }
}
