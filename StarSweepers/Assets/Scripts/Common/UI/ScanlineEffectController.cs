using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Controls the screen-wide scanline overlay.</summary>
[DisallowMultipleComponent]
public sealed class ScanlineEffectController : MonoBehaviour
{
    private static ScanlineEffectController instance;

    [SerializeField] private Image overlayImage;
    [SerializeField] private bool scanlineActive = true;
    [SerializeField] private bool preserveAcrossScenes = true;

    [Tooltip("オンにすると、ゲーム画面（試合の進行役 SpaceJunkRound がいるシーン）でだけ横縞を出す。タイトルやロビーでは出さない。自動で作ったときはオンになる")]
    [SerializeField] private bool onlyInGameScenes = false;

    public static ScanlineEffectController Instance => instance;
    public bool IsScanlineActive => scanlineActive;

    private const string PrefabResourcePath = "ScanlineOverlay";

    /// <summary>表に出ているシーンがゲーム画面かどうか（シーンが替わるたびに調べ直す）。</summary>
    private bool isInGameScene;

    // ------------------------------------------------------------
    // 自動で作る（どのシーンから再生しても用意しておき、ゲーム画面でだけ見せる）
    // ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 「再生するたびに作り直さない」設定（ドメインの再読み込みなし）でも、前の再生の値を残さない
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        // シーンに置かれていた場合（検証シーンなど）は、そちらを使う
        if (instance != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ScanlineEffectController] Resources/{PrefabResourcePath} が見つからないため、走査線を表示しません。");
            return;
        }

        GameObject created = Instantiate(prefab);
        created.name = PrefabResourcePath;

        // 自動で作った物は「ゲーム画面だけ」。同じフレームの中で切り替えるので、タイトルなどで一瞬映ることはない
        ScanlineEffectController controller = created.GetComponent<ScanlineEffectController>();
        if (controller != null)
            controller.SetOnlyInGameScenes(true);
    }

    /// <summary>ゲーム画面（SpaceJunkRound がいるシーン）でだけ出すかどうかを切り替える。</summary>
    public void SetOnlyInGameScenes(bool onlyInGame)
    {
        onlyInGameScenes = onlyInGame;
        RefreshGameScene();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (overlayImage == null)
            overlayImage = GetComponentInChildren<Image>(true);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        EnforceClickThrough();
        RefreshGameScene();

        if (preserveAcrossScenes)
            DontDestroyOnLoad(gameObject);
    }

    private void LateUpdate()
    {
        // Keep the full-screen graphic from intercepting UI input if changed at runtime.
        EnforceClickThrough();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (instance == this)
            instance = null;
    }

    private void OnValidate()
    {
        if (overlayImage == null)
            overlayImage = GetComponentInChildren<Image>(true);

        EnforceClickThrough();
        ApplyActiveState();
    }

    /// <summary>Turns the scanline overlay on or off.</summary>
    public void SetScanlineActive(bool isActive)
    {
        scanlineActive = isActive;
        ApplyActiveState();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshGameScene();
    }

    private void OnActiveSceneChanged(Scene before, Scene after)
    {
        RefreshGameScene();
    }

    /// <summary>
    /// 表に出ているシーンに試合の進行役（SpaceJunkRound）がいれば、ゲーム画面とみなす。
    /// 星空（SpaceJunkSkybox）と同じ見分け方。読み込みと表の切り替えのどちらが先に来ても間に合うよう、両方で呼ぶ。
    /// </summary>
    private void RefreshGameScene()
    {
        isInGameScene = false;
        if (onlyInGameScenes)
        {
            // シーンの入れ替わりの途中は前のマップの進行役も残っているので、表のシーンの進行役だけを見る
            Scene active = SceneManager.GetActiveScene();
            foreach (SpaceJunkRound round in FindObjectsByType<SpaceJunkRound>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (round.gameObject.scene == active)
                {
                    isInGameScene = true;
                    break;
                }
            }
        }

        ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        if (overlayImage != null)
            overlayImage.enabled = scanlineActive && (!onlyInGameScenes || isInGameScene);
    }

    private void EnforceClickThrough()
    {
        if (overlayImage != null)
            overlayImage.raycastTarget = false;
    }
}
