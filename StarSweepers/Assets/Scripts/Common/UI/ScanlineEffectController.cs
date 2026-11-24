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
