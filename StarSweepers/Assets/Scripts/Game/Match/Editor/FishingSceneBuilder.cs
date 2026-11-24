using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **検証用のシーンやプレハブを、エディタの道具で組み立てるときの共通の部品置き場。**
///
/// 床・明かり・壁・カメラ・物資の作り方と、スクリプトの値をインスペクターと同じ形で書き込む関数
/// （<see cref="SetRef"/> など）をまとめてある。道具側は「どの部品を、どう並べるか」だけを書く。
///
/// もとは釣りの検証シーンを作る道具（FishingSceneBuilder）だった。2026/11/24 に古い体と釣りを片付けたとき、
/// ほかの道具が使っている共通の部品だけを残して、名前を変えた。
///
/// ## `SetRef` などで渡すフィールド名
///
/// `SetRef(follow, "target", player.transform)` のように、つなぎ先のスクリプトのフィールド名を**文字列で**書いている。
/// **つなぎ先のフィールドを改名すると、ここは静かに壊れる**（コンパイルエラーにならない）。
/// `SetRef` / `SetObjectArray` は Console に警告を出すが、`SetInt` / `SetFloat` / `SetString` は**何も出さずに飛ばす**ので特に気づきにくい。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
internal static class SceneBuildTools
{
    public const string MaterialFolder = "Assets/Art/Materials";
    public const string PrefabFolder = "Assets/Prefabs";

    // ---- プレハブの置き場所 ----
    // **プレハブを別のフォルダへ移したら、ここも直すこと。**
    /// <summary>プレイヤーの体。</summary>
    public const string PlayerPrefabFolder = PrefabFolder + "/Player";

    /// <summary>物資・爆発物と、それを出すスポナー。</summary>
    public const string ItemPrefabFolder = PrefabFolder + "/Item";

    /// <summary>見た目の演出（爆発など）。</summary>
    public const string EffectPrefabFolder = PrefabFolder + "/Effect";

    /// <summary>プレイヤーの体のプレハブ（オンラインもオフラインもこれ1つ。2026/11/24 にまとめた）。</summary>
    public const string PlayerPrefabPath = PlayerPrefabFolder + "/SweeperPlayer.prefab";

    // ---- シーンの置き場所 ----
    /// <summary>機能ごとの検証シーン。</summary>
    public const string SceneFolder = "Assets/Scenes/Test";

    /// <summary>遊ぶためのマップ（ステージ）の置き場所。</summary>
    public const string MapSceneFolder = "Assets/Scenes/Stage";

    /// <summary>
    /// 通信で出せるプレハブの一覧（Netcode が最初に作るファイル）。ここに入っていないプレハブは、ホストが出しても他の人に出ない。
    /// </summary>
    private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

    // ---- 検証シーンの床の大きさ（単位はすべてメートル。中心は原点）----
    public const float ArenaHalf = 20f;       // 床の半分の大きさ（40m四方）
    public const float WallLine = 19f;        // 壁を置く位置（中心から壁の厚みの真ん中までの距離）
    public const float WallHeight = 1.5f;     // 壁の高さ
    public const float WallThickness = 1f;    // 壁の厚み

    // ------------------------------------------------------------
    // 明かりと床
    // ------------------------------------------------------------

    /// <summary>
    /// 太陽の光を1つ置く。空のシーンから作るので、これが無いと真っ暗になる。
    /// 真上からではなく斜め上から照らす（影が横に伸びるので、物の高さが分かりやすい）。
    /// </summary>
    public static void CreateLight()
    {
        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    /// <summary>40m四方の床を、中心を原点にして置く。</summary>
    public static void CreateGround()
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/TestGround.mat", new Color(0.72f, 0.72f, 0.72f));

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        // Unity の Plane は大きさ1倍で 10m 四方。「半分の大きさ × 2 ÷ 10」＝ ArenaHalf × 0.2 倍で、ちょうど 40m 四方になる
        ground.transform.localScale = new Vector3(ArenaHalf * 0.2f, 1f, ArenaHalf * 0.2f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>四方に、まっすぐな壁を置く。</summary>
    public static void CreatePlainWalls()
    {
        Material material = GetOrCreateMaterial(MaterialFolder + "/FishingWall.mat", new Color(0.5f, 0.55f, 0.62f));

        // 床の端から端まで（40m）。四隅は南北の壁と東西の壁が重なってすき間が無くなる
        float length = ArenaHalf * 2f;

        // 箱は中心で位置を決めるので、高さの半分だけ持ち上げると足元が床（高さ0）に来る
        CreateBox("Wall_North", new Vector3(0f, WallHeight * 0.5f, WallLine),
            new Vector3(length, WallHeight, WallThickness), material);
        CreateBox("Wall_South", new Vector3(0f, WallHeight * 0.5f, -WallLine),
            new Vector3(length, WallHeight, WallThickness), material);
        CreateBox("Wall_East", new Vector3(WallLine, WallHeight * 0.5f, 0f),
            new Vector3(WallThickness, WallHeight, length), material);
        CreateBox("Wall_West", new Vector3(-WallLine, WallHeight * 0.5f, 0f),
            new Vector3(WallThickness, WallHeight, length), material);
    }

    // ------------------------------------------------------------
    // カメラ
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーを追いかける見下ろしカメラを置く。
    ///
    /// 置く位置 (0, 16, -9) は、原点に立つプレイヤーから `TopDownCameraFollow` の初期の Offset だけ離れた所
    /// （高さ 16m・後ろへ 9m）。こうしておくと、再生した瞬間にカメラが飛ばない。
    /// </summary>
    public static Camera CreateCamera(GameObject player)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 16f, -9f);
        cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500f;
        cameraObject.AddComponent<AudioListener>();

        TopDownCameraFollow follow = cameraObject.AddComponent<TopDownCameraFollow>();
        SetRef(follow, "target", player.transform);

        return camera;
    }

    // ------------------------------------------------------------
    // 物資
    // ------------------------------------------------------------

    /// <summary>
    /// プレイヤーの周りに、円形に物資を並べる。
    /// <paramref name="count"/> 個を、中心から <paramref name="radius"/> m の円の上に等間隔で置く（北から時計回り）。
    /// </summary>
    public static void CreateSupplyRing(int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, radius);
            // 物資は 0.8m の箱なので、高さの半分（0.4m）持ち上げると床にちょうど乗る
            position.y = 0.4f;

            // 並べて置く物資は、消えても元の場所に戻ってくる（物資が尽きて遊べなくならないように）
            HookableObject hookable = CreateSupply($"Supply_{i + 1}", position);
            SetFloat(hookable, "respawnSeconds", 6f);
        }
    }

    /// <summary>物資を1つ作る。**Respawn Seconds は 0（消えたら戻ってこない）。**</summary>
    public static HookableObject CreateSupply(string name, Vector3 position)
    {
        Material material = GetOrCreateMaterial(
            MaterialFolder + "/FishingSupply.mat", new Color(0.85f, 0.6f, 0.3f));

        GameObject supply = CreateBox(name, position, new Vector3(0.8f, 0.8f, 0.8f), material);

        // 重さ 1kg。Damping は、投げたあと床を滑り続けたり回り続けたりしないための「空気の抵抗」
        Rigidbody body = supply.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.linearDamping = 0.4f;
        body.angularDamping = 0.6f;

        HookableObject hookable = supply.AddComponent<HookableObject>();
        SetFloat(hookable, "respawnSeconds", 0f);
        return hookable;
    }

    // ------------------------------------------------------------
    // 小さな部品
    // ------------------------------------------------------------

    /// <summary>
    /// 箱を1つ置く。<paramref name="position"/> は**箱の中心**のワールド位置、<paramref name="size"/> は縦横高さ（メートル）。
    /// 床の上に乗せたいときは、高さの半分だけ持ち上げた位置を渡す。当たり判定（BoxCollider）付き。
    /// </summary>
    public static GameObject CreateBox(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    /// <summary>
    /// <see cref="CreateBox"/> と同じだが、<paramref name="parent"/> の子として置く。
    /// 位置は**親から見た位置**（ローカル座標）なので、親を回すと箱も一緒に回る。
    /// </summary>
    public static GameObject AddBox(Transform parent, string name,
        Vector3 localPosition, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    // ---- スクリプトの値を、インスペクターで入れるのと同じ形で書き込む ----
    // private の [SerializeField] にも書き込めるよう、SerializedObject を通している。
    // ⚠ 引数 field は、つなぎ先のフィールド名そのもの（文字列）。
    //   **つなぎ先のフィールドを改名すると、ここは静かに壊れる**（コンパイルエラーにならない）。
    //   見つからないとき、SetRef / SetObjectArray は Console に警告を出すが、
    //   SetInt / SetFloat / SetString は**何も言わずに飛ばす**。

    /// <summary>参照（ほかの部品・オブジェクト）を1つ入れる。</summary>
    public static void SetRef(Object owner, string field, Object value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>参照の配列を、<paramref name="values"/> の中身で丸ごと置き換える（長さも合わせる）。</summary>
    public static void SetObjectArray(Object owner, string field, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"{owner.GetType().Name} に {field} が見つかりません。");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>整数を入れる。LayerMask（どのレイヤーを対象にするか）もこれで入れられる。</summary>
    public static void SetInt(Object owner, string field, int value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetFloat(Object owner, string field, float value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void SetString(Object owner, string field, string value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            return;
        }
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// <paramref name="path"/> の素材を読み込む。**無ければ <paramref name="color"/> の色で作って保存する。**
    ///
    /// ・すでにある素材は**そのまま使い、色は変えない**（Unity 上で色を変えていても、上書きしない）。
    /// ・URP のシェーダーが無ければ、標準のシェーダーで作る。
    /// ・素材のフォルダが無いときは保存せず、シーンの中だけの素材になる（ファイルとしては残らない）。
    /// </summary>
    public static Material GetOrCreateMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };

        if (AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateAsset(material, path);
        }

        return material;
    }

    /// <summary>フォルダが無ければ作る。**途中のフォルダもまとめて作る。**</summary>
    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int lastSlash = path.LastIndexOf('/');
        string parent = path.Substring(0, lastSlash);
        string folderName = path.Substring(lastSlash + 1);

        // 親が無ければ先に作る（Unity は1段ずつしか作れない）
        EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }

    // ------------------------------------------------------------
    // 通信への登録
    // ------------------------------------------------------------

    /// <summary>
    /// **全員の画面に出せるように、プレハブを通信の一覧へ登録する。**
    /// 登録しないと、ホストが出しても他の人の画面には現れない。
    /// </summary>
    public static void RegisterNetworkPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);

        if (list == null)
        {
            Debug.LogWarning($"[JUNK] {NetworkPrefabsListPath} が見つかりません。" +
                             $"{prefab.name} を NetworkManager の Network Prefabs に手で登録してください。");
            return;
        }

        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }
    }
}
