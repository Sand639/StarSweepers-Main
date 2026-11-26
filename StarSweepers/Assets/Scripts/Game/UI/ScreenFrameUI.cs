using UnityEngine;

/// <summary>
/// 試合中、画面のふちに枠の画像（UI_MainFrame_02）を重ねる。
/// 説明：Documents/スクリプトの説明/Game/UI/ScreenFrameUI.md
/// </summary>
public sealed class ScreenFrameUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/ScreenFrameUI.prefab）。</summary>
    public const string PrefabResourcePath = "ScreenFrameUI";

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Header("テスト用")]
    [Tooltip("オンなら、試合でなくても出す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    /// <summary>いま動いている係（ゲーム中は1つだけ）。</summary>
    public static ScreenFrameUI Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        // シーンに置かれていれば、それを使う（検証シーン）
        if (Instance != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[画面の枠] Resources/{PrefabResourcePath}.prefab が無いので、試合の画面に枠は出ません。");
            return;
        }

        GameObject created = Instantiate(prefab);
        created.name = "ScreenFrameUI";
        DontDestroyOnLoad(created);
    }

    private void Awake()
    {
        // 2つ目は使わない（ステージに置かれていた場合など）
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SetVisible(testMode);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // 試合（ラウンドがある間）だけ出す。タイトル・ロビーでは出さない
        SetVisible(testMode || SpaceJunkRound.Current != null);
    }

    private void SetVisible(bool visible)
    {
        if (content != null && content.activeSelf != visible)
        {
            content.SetActive(visible);
        }
    }
}
