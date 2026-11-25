using UnityEngine;
using UnityEngine.UI;

/// <summary>Controls the screen-wide scanline overlay.</summary>
[DisallowMultipleComponent]
public sealed class ScanlineEffectController : MonoBehaviour
{
    private static ScanlineEffectController instance;

    [SerializeField] private Image overlayImage;
    [SerializeField] private bool scanlineActive = true;
    [SerializeField] private bool preserveAcrossScenes = true;

    public static ScanlineEffectController Instance => instance;
    public bool IsScanlineActive => scanlineActive;

    private const string PrefabResourcePath = "ScanlineOverlay";

    // ------------------------------------------------------------
    // 自動で作る（どのシーンから再生しても走査線が出るようにする）
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

        EnforceClickThrough();
        ApplyActiveState();

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

    private void ApplyActiveState()
    {
        if (overlayImage != null)
            overlayImage.enabled = scanlineActive;
    }

    private void EnforceClickThrough()
    {
        if (overlayImage != null)
            overlayImage.raycastTarget = false;
    }
}
