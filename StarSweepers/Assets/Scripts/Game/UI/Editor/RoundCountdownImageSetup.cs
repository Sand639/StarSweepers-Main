using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// カウントダウン（3・2・1・START!）の画像のプレハブを作り直すメニュー（エディタ専用）。
/// 説明：Documents/スクリプトの説明/Game/UI/RoundCountdownImage.md
/// </summary>
public static class RoundCountdownImageSetup
{
    private const string PrefabPath = "Assets/Resources/RoundCountdownImage.prefab";
    private const string SpriteFolder = "Assets/Art/Sprites/CountDown/";

    /// <summary>画像の中心の位置（1920×1080 の画面の真ん中からのずれ）。前の文字と同じ所。</summary>
    private static readonly Vector2 Position = new Vector2(0f, 60f);

    [MenuItem("Tools/StarSweepers/カウントダウンの画像のプレハブを作り直す")]
    public static void BuildPrefab()
    {
        var numbers = new Sprite[3];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = LoadSprite($"UI_CD_{i + 1}.png");
        }

        Sprite start = LoadSprite("UI_CD_Start.png");
        if (start == null || System.Array.IndexOf(numbers, null) >= 0)
        {
            return;
        }

        var root = new GameObject("RoundCountdownImage", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Position;

        var image = root.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.sprite = start;
        image.SetNativeSize();

        var countdown = root.AddComponent<RoundCountdownImage>();
        var so = new SerializedObject(countdown);
        SerializedProperty list = so.FindProperty("numberSprites");
        list.arraySize = numbers.Length;
        for (int i = 0; i < numbers.Length; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = numbers[i];
        }

        so.FindProperty("startSprite").objectReferenceValue = start;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"カウントダウンの画像のプレハブを作りました: {PrefabPath}");
    }

    private static Sprite LoadSprite(string fileName)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + fileName);
        if (sprite == null)
        {
            Debug.LogError($"{SpriteFolder}{fileName} が無いか、取り込み設定が Sprite になっていません。");
        }

        return sprite;
    }
}
