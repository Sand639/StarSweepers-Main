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

    // ---- フレーム版（新しいデザイン案）----
    private const string FramePrefabPath = "Assets/Prefabs/UI/TeamScoreBarFrameUI.prefab";
    private const string FrameSpritePath = "Assets/Art/Textures/UI/NewGameUIFrame.png";
    private const string FrameFireMaterialPath = "Assets/Art/Materials/TeamScoreBarFireFrame.mat";

    // デザイン案のデジタル数字風の文字（DSEG。SIL OFL 1.1）。ROUND は 14セグ（英字が出る）、時間は 7セグ
    private const string RoundFontSourcePath = "Assets/TextMesh Pro/Fonts/DSEG14Classic-Bold.ttf";
    private const string RoundFontAssetPath = "Assets/TextMesh Pro/Fonts/DSEG14ClassicBoldSDF.asset";
    private const string TimeFontSourcePath = "Assets/TextMesh Pro/Fonts/DSEG7Classic-Bold.ttf";
    private const string TimeFontAssetPath = "Assets/TextMesh Pro/Fonts/DSEG7ClassicBoldSDF.asset";
    private const string DsegCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ :.-";

    // フレームの画像の大きさ（ピクセル）と、画面での幅
    private const float FrameImageWidth = 2170f;
    private const float FrameImageHeight = 725f;
    private const float FrameWidth = 900f;
    private const float FrameTop = 8f;

    // 画像の中の場所（ピクセル。左上が 0）：得点の枠の内側・ラウンドの板・時間の板
    private static readonly Rect FillPixels = Rect.MinMaxRect(126f, 263f, 2043f, 487f);
    private static readonly Rect RoundPixels = Rect.MinMaxRect(758f, 190f, 1412f, 276f);
    private static readonly Rect TimePixels = Rect.MinMaxRect(848f, 406f, 1322f, 502f);

    // フレーム版の炎で、得点の枠の下から何割を帯にするか（残りは炎の舌が立ちのぼる所）
    private const float FrameFireBarRatio = 0.5f;

    // デザイン案の色（0＝青チーム→青緑、1＝赤チーム→オレンジ）と文字の色
    private static readonly Color DesignTeal = new Color(0.58f, 0.73f, 0.67f);
    private static readonly Color DesignOrange = new Color(0.78f, 0.39f, 0.24f);
    private static readonly Color PlateTextColor = new Color(0.86f, 0.92f, 0.92f);

    /// <summary>フレーム版のモードA（炎）とモードB（炎なし）を並べた検証シーンを作る（ステージ用のプレハブには触らない）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バーの検証シーンを作る")]
    public static void CreateTestScene()
    {
        GameObject framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FramePrefabPath);
        if (framePrefab == null) framePrefab = BuildFramePrefab();
        if (framePrefab == null) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 灰色の背景だけを映すカメラ
        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.35f, 0.35f, 0.37f);
        cameraObject.tag = "MainCamera";

        PlaceTestInstance(framePrefab, "TeamScoreBarFrame_A_SolidAndFire", true, true, 0f);
        PlaceTestInstance(framePrefab, "TeamScoreBarFrame_B_SolidOnly", true, false, -380f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"チーム得点バーの検証シーンを作りました（フレーム版 A＝色＋炎・B＝色だけ）: {ScenePath}");
    }

    /// <summary>フレーム版のプレハブを作り直す（手で直した大きさは消える）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バー（フレーム版）のプレハブを作り直す")]
    public static void RebuildFramePrefab()
    {
        if (BuildFramePrefab() != null)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"チーム得点バー（フレーム版）のプレハブを作り直しました: {FramePrefabPath}");
        }
    }

    /// <summary>検証シーンにフレーム版を1つ置き、テストモードと色の帯・炎のオン・オフと高さを決める。</summary>
    private static void PlaceTestInstance(GameObject prefab, string name, bool solid, bool fire, float offsetY)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;

        var ui = new SerializedObject(instance.GetComponent<TeamScoreBarUI>());
        ui.FindProperty("testMode").boolValue = true;
        ui.ApplyModifiedPropertiesWithoutUndo();

        var effect = new SerializedObject(instance.GetComponent<TeamScoreFlameEffect>());
        effect.FindProperty("showSolid").boolValue = solid;
        effect.FindProperty("showFire").boolValue = fire;
        effect.ApplyModifiedPropertiesWithoutUndo();

        if (offsetY != 0f)
        {
            var frame = (RectTransform)instance.transform.Find("Content/Frame");
            frame.anchoredPosition += new Vector2(0f, offsetY);
        }
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

    /// <summary>フレーム版（新しいデザイン案）の部品を組み立てて、プレハブとして保存する。失敗したら null。</summary>
    private static GameObject BuildFramePrefab()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Sprite frameSprite = EnsureFrameSprite();
        Material fireMaterial = EnsureFrameFireMaterial();
        if (font == null || frameSprite == null || fireMaterial == null)
        {
            Debug.LogError("フレーム版を作れません（フォント・フレームの画像・炎のシェーダーのどれかが見つからない）");
            return null;
        }

        Material numberMaterial = EnsureNumberMaterial(font);

        var root = new GameObject("TeamScoreBarFrameUI", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var ui = root.AddComponent<TeamScoreBarUI>();
        var effect = root.AddComponent<TeamScoreFlameEffect>();

        RectTransform content = CreateRect("Content", root.transform);
        Stretch(content, 0f);

        // フレーム全体（画像の縦横比のまま、画面の上の真ん中）
        RectTransform frame = CreateRect("Frame", content);
        AnchorTopCenter(frame, new Vector2(FrameWidth, FrameWidth * FrameImageHeight / FrameImageWidth), FrameTop);

        // 色の帯を入れる所（得点の枠の内側）。RectMask2D で四角の外を切る。大きさは手で変えてよい
        RectTransform fillArea = CreateRect("FillArea", frame);
        AnchorToFramePixels(fillArea, FillPixels);
        fillArea.gameObject.AddComponent<RectMask2D>();

        RectTransform segments = CreateRect("Segments", fillArea);
        Stretch(segments, 0f);

        var segmentImages = new Image[TeamScoreBarUI.MaxTeams];
        var scoreTexts = new TMP_Text[TeamScoreBarUI.MaxTeams];

        for (int i = 0; i < TeamScoreBarUI.MaxTeams; i++)
        {
            RectTransform segment = CreateRect($"Segment{i}", segments);
            Stretch(segment, 0f);
            segmentImages[i] = AddImage(segment, Color.white, null);

            RectTransform label = CreateRect("Score", segment);
            Stretch(label, 0f);
            label.offsetMin = new Vector2(24f, 0f);
            label.offsetMax = new Vector2(-24f, 0f);
            scoreTexts[i] = AddText(label, font, 30f, "0");
            scoreTexts[i].fontSharedMaterial = numberMaterial;
        }

        // フレームの画像は帯より後（手前）に描く。帯のはみ出しはフレームの縁と板が隠す
        RectTransform frameImage = CreateRect("FrameImage", frame);
        Stretch(frameImage, 0f);
        Image frameGraphic = AddImage(frameImage, Color.white, frameSprite);
        frameGraphic.preserveAspect = true;

        TMP_FontAsset roundFont = EnsureDsegFont(RoundFontSourcePath, RoundFontAssetPath);
        TMP_FontAsset timeFont = EnsureDsegFont(TimeFontSourcePath, TimeFontAssetPath);
        TMP_Text roundText = AddPlateText(CreateRect("RoundText", frame), roundFont != null ? roundFont : font, RoundPixels, "ROUND 01");
        TMP_Text timeText = AddPlateText(CreateRect("TimeText", frame), timeFont != null ? timeFont : font, TimePixels, "120");
        if (roundFont != null) SetPlateFont(roundText.transform, roundFont);
        if (timeFont != null) SetPlateFont(timeText.transform, timeFont);

        var so = new SerializedObject(ui);
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        SetArray(so.FindProperty("segmentImages"), segmentImages);
        SetArray(so.FindProperty("scoreTexts"), scoreTexts);
        so.FindProperty("timeText").objectReferenceValue = timeText;
        so.FindProperty("roundText").objectReferenceValue = roundText;
        so.FindProperty("occupiedArea").objectReferenceValue = frame;
        so.FindProperty("timeStyle").enumValueIndex = (int)TeamScoreBarUI.TimeStyle.Seconds;
        so.FindProperty("showScoreNumbers").boolValue = false;
        so.FindProperty("useCustomColors").boolValue = true;
        SerializedProperty colors = so.FindProperty("customTeamColors");
        colors.GetArrayElementAtIndex(0).colorValue = DesignTeal;
        colors.GetArrayElementAtIndex(1).colorValue = DesignOrange;
        so.FindProperty("timeColor").colorValue = PlateTextColor;
        so.FindProperty("testMode").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();

        AddFireLayer(root, fireMaterial);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FramePrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>今あるフレーム版のプレハブに、炎だけの層（FireArea）を足す（ほかは触らない）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バー（フレーム版）に炎の層を足す")]
    public static void AddFireLayerToFramePrefab()
    {
        // マテリアルがあれば調整した値ごとそのまま使う（無いときだけ作る）
        Material fireMaterial = AssetDatabase.LoadAssetAtPath<Material>(FrameFireMaterialPath);
        if (fireMaterial == null) fireMaterial = EnsureFrameFireMaterial();
        if (fireMaterial == null || AssetDatabase.LoadAssetAtPath<GameObject>(FramePrefabPath) == null)
        {
            Debug.LogError("炎のマテリアルかフレーム版のプレハブが見つかりません");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(FramePrefabPath);
        bool added = AddFireLayer(root, fireMaterial);
        PrefabUtility.SaveAsPrefabAsset(root, FramePrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log(added
            ? "チーム得点バー（フレーム版）に炎の層（Frame/FireArea）を足しました"
            : "チーム得点バー（フレーム版）には、もう炎の層があります");
    }

    /// <summary>
    /// Frame の下に、FillArea と同じ四角の FireArea（マスク無し）と帯ごとの炎の Image を足し、色の帯からは炎を外す。
    /// すでに FireArea があれば何もしない。
    /// </summary>
    private static bool AddFireLayer(GameObject root, Material fireMaterial)
    {
        Transform frame = root.transform.Find("Content/Frame");
        var fillArea = frame != null ? frame.Find("FillArea") as RectTransform : null;
        if (fillArea == null || frame.Find("FireArea") != null) return false;

        // 炎の層は FillArea のすぐ後（フレームの画像より奥）。四角は FillArea と同じで始め、手で広げてよい（はみ出してもよい）
        RectTransform fireArea = CreateRect("FireArea", frame);
        fireArea.anchorMin = fillArea.anchorMin;
        fireArea.anchorMax = fillArea.anchorMax;
        fireArea.pivot = fillArea.pivot;
        fireArea.offsetMin = fillArea.offsetMin;
        fireArea.offsetMax = fillArea.offsetMax;
        fireArea.SetSiblingIndex(fillArea.GetSiblingIndex() + 1);

        var fireImages = new Image[TeamScoreBarUI.MaxTeams];
        for (int i = 0; i < TeamScoreBarUI.MaxTeams; i++)
        {
            RectTransform fire = CreateRect($"Fire{i}", fireArea);
            Stretch(fire, 0f);
            fireImages[i] = AddImage(fire, Color.white, null);
            fireImages[i].material = fireMaterial;

            // 色の帯は、ただの色に戻す（炎は FireArea が描く）
            var segment = fillArea.Find($"Segments/Segment{i}")?.GetComponent<Image>();
            if (segment != null) segment.material = null;
        }

        var effect = new SerializedObject(root.GetComponent<TeamScoreFlameEffect>());
        SetArray(effect.FindProperty("fireImages"), fireImages);
        effect.FindProperty("fireMaterial").objectReferenceValue = fireMaterial;
        effect.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>フレームの画像のピクセルの範囲に合わせて、アンカーで置く（フレームを拡大・縮小してもずれない）。</summary>
    private static void AnchorToFramePixels(RectTransform rect, Rect pixels)
    {
        rect.anchorMin = new Vector2(pixels.xMin / FrameImageWidth, 1f - pixels.yMax / FrameImageHeight);
        rect.anchorMax = new Vector2(pixels.xMax / FrameImageWidth, 1f - pixels.yMin / FrameImageHeight);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>フレームの板の上に、板の大きさに合わせて縮む文字を置く。</summary>
    private static TMP_Text AddPlateText(RectTransform rect, TMP_FontAsset font, Rect pixels, string text)
    {
        AnchorToFramePixels(rect, pixels);
        TMP_Text tmp = AddText(rect, font, 40f, text);
        tmp.color = PlateTextColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.characterSpacing = 12f;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 8f;
        tmp.fontSizeMax = 60f;
        tmp.margin = new Vector4(6f, 2f, 6f, 2f);
        return tmp;
    }

    /// <summary>フレームの画像を UI で使える Sprite の設定にする（すでに Sprite ならそのまま）。</summary>
    private static Sprite EnsureFrameSprite()
    {
        var importer = AssetImporter.GetAtPath(FrameSpritePath) as TextureImporter;
        if (importer == null) return null;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096; // 画像の幅が 2170 なので、2048 だと縮んでぼやける
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(FrameSpritePath);
    }

    /// <summary>今あるフレーム版のプレハブの、ROUND と時間の文字だけをデジタル数字のフォントにする（ほかは触らない）。</summary>
    [MenuItem("Tools/StarSweepers/チーム得点バー（フレーム版）にデジタル数字のフォントを付ける")]
    public static void ApplyDsegFontsToFramePrefab()
    {
        TMP_FontAsset roundFont = EnsureDsegFont(RoundFontSourcePath, RoundFontAssetPath);
        TMP_FontAsset timeFont = EnsureDsegFont(TimeFontSourcePath, TimeFontAssetPath);
        if (roundFont == null || timeFont == null || AssetDatabase.LoadAssetAtPath<GameObject>(FramePrefabPath) == null)
        {
            Debug.LogError("フォントかフレーム版のプレハブが見つかりません");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(FramePrefabPath);
        SetPlateFont(root.transform.Find("Content/Frame/RoundText"), roundFont);
        SetPlateFont(root.transform.Find("Content/Frame/TimeText"), timeFont);
        PrefabUtility.SaveAsPrefabAsset(root, FramePrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log("チーム得点バー（フレーム版）の ROUND と時間の文字を DSEG にしました");
    }

    /// <summary>板の文字のフォントを差し替える（DSEG は字の間が広いので、字間を 0 にする）。</summary>
    private static void SetPlateFont(Transform target, TMP_FontAsset font)
    {
        TMP_Text text = target != null ? target.GetComponent<TMP_Text>() : null;
        if (text == null) return;

        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontStyle = FontStyles.Normal;
        text.characterSpacing = 0f;
        text.wordSpacing = 60f; // DSEG の空白は細いので、「ROUND」と「01」の間を空ける
    }

    /// <summary>DSEG の ttf から TextMeshPro のフォントを作る（あればそのまま使う）。使う文字だけを焼き込んだ静的なフォント。</summary>
    private static TMP_FontAsset EnsureDsegFont(string sourcePath, string assetPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;

        var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (source == null) return null;

        // 90pt の字を40字ほど焼くので 1024×1024（512 だと半分ほどしか入らず、残りが別のフォントで出る）
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(source, 90, 9,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, false);
        fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);

        AssetDatabase.CreateAsset(fontAsset, assetPath);
        fontAsset.material.name = fontAsset.name + " Material";
        fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);

        // 使う文字を焼き込んでから静的にする（ビルドで元の ttf を読みに行かないように）
        if (!fontAsset.TryAddCharacters(DsegCharacters, out string missing))
        {
            Debug.LogWarning($"{fontAsset.name} に入らなかった文字: {missing}");
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    /// <summary>フレーム版の炎の色合い（デザイン案の色が淡いので、白くしすぎない）。</summary>
    public static void ApplyFrameFireLook(Material material)
    {
        material.SetFloat("_InnerWhiten", 0.15f);
        material.SetFloat("_CoreWhiten", 0.4f);
        material.SetFloat("_Depth", 0.25f);
        material.SetFloat("_RimDarken", 0.6f);
        material.SetFloat("_BoostBright", 0.08f);
    }

    /// <summary>フレーム版の炎のマテリアルを用意する（あれば残し、_BarRatio だけ入れ直す）。</summary>
    private static Material EnsureFrameFireMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FrameFireMaterialPath);

        if (material == null)
        {
            Shader shader = Shader.Find(FireShaderName);
            if (shader == null) return null;

            material = new Material(shader);
            ApplyFrameFireLook(material);
            AssetDatabase.CreateAsset(material, FrameFireMaterialPath);
        }

        material.SetFloat("_BarRatio", FrameFireBarRatio);
        EditorUtility.SetDirty(material);
        return material;
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
