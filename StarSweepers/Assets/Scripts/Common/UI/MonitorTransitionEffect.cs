using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>透明な2つの画面を持つモニターの上を、視点が移動する。</summary>
internal sealed class MonitorTransitionEffect : SceneTransitionEffect
{
    private const string ArtworkPath = "SceneTransitionMonitor";
    private static readonly Vector2 ArtworkSize = new Vector2(1920f, 1080f);
    // 元画像の透明部分。座標の原点は画像の左上。
    private static readonly Rect SourceWindow = new Rect(67f, 61f, 763f, 417f);
    private static readonly Rect DestinationWindow = new Rect(889f, 490f, 975f, 537f);
    private RectTransform root;
    private RectTransform board;
    private RawImage source;
    private RawImage result;
    private RawImage destination;
    private Image blocker;
    private CanvasGroup opacity;
    private RenderTexture sourceFrame;
    private RenderTexture resultFrame;
    private RenderTexture destinationFrame;
    private bool loaded;

    public override void Build(RectTransform parent, Color color)
    {
        root = parent;
        blocker = CreateFullScreenImage(root, "MonitorInputBlocker", Color.clear);
        var composition = new GameObject("MonitorComposition", typeof(RectTransform), typeof(CanvasGroup));
        board = (RectTransform)composition.transform;
        board.SetParent(root, false);
        board.sizeDelta = ArtworkSize;
        opacity = composition.GetComponent<CanvasGroup>();
        opacity.blocksRaycasts = false;

        Texture2D artwork = Resources.Load<Texture2D>(ArtworkPath);
        source = AddScreen("PreviousScene", SourceWindow);
        result = AddScreen("SavedResult", SourceWindow);
        result.enabled = false;
        destination = AddScreen("NextScene", DestinationWindow);
        RawImage frame = AddScreen("MonitorArtwork", new Rect(Vector2.zero, ArtworkSize));
        frame.texture = artwork;
        frame.color = Color.white;
        frame.enabled = artwork != null;
    }

    public void CaptureSource(RenderTexture savedResult)
    {
        sourceFrame = CaptureScreen("MonitorPreviousFrame");
        source.texture = sourceFrame;
        source.color = Color.white;
        source.uvRect = CropUV(sourceFrame, SourceWindow);
        resultFrame = savedResult;
        if (resultFrame != null)
        {
            result.texture = resultFrame;
            result.uvRect = CropUV(resultFrame, SourceWindow);
            result.enabled = true;
        }
    }

    public void CaptureDestination(bool sceneLoaded)
    {
        loaded = sceneLoaded;
        if (!loaded) return;

        // 高さ順で、画面を描いている URP のベースカメラを選ぶ。
        // 開幕カメラが有効なら、そこで止まっている最初の画を使う。
        Camera camera = null;
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        destinationFrame = CreateFrame("MonitorNextFrame", width, height);
        var request = new RenderPipeline.StandardRequest { destination = destinationFrame };
        foreach (Camera candidate in Camera.allCameras)
        {
            if (candidate.TryGetComponent(out UniversalAdditionalCameraData cameraData) &&
                cameraData.renderType == CameraRenderType.Overlay) continue;

            if (candidate.targetTexture == null && candidate.targetDisplay == 0 &&
                (camera == null || candidate.depth > camera.depth) &&
                RenderPipeline.SupportsRenderRequest(candidate, request))
            {
                camera = candidate;
            }
        }

        if (camera == null) return;
        RenderPipeline.SubmitRenderRequest(camera, request);
        destination.texture = destinationFrame;
        destination.color = Color.white;
        destination.uvRect = CropUV(destinationFrame, DestinationWindow);
    }

    public override void Apply(float amount, bool revealing)
    {
        Vector2 viewport = root.rect.size;
        float panelScale = Mathf.Max(viewport.x / ArtworkSize.x, viewport.y / ArtworkSize.y);
        Rect window = revealing && loaded ? DestinationWindow : SourceWindow;
        float fullScale = Mathf.Max(viewport.x / (window.width - 22f), viewport.y / (window.height - 22f));
        float scale = Mathf.Lerp(fullScale, panelScale, amount);
        Vector2 offset = ArtworkCenter(window);
        board.localScale = new Vector3(scale, scale, 1f);
        board.anchoredPosition = -offset * scale * (1f - amount);
        // 画面がいっぱいになった最後の数フレームで、生の画面へつなぐ。
        opacity.alpha = revealing ? Mathf.Clamp01(amount / 0.12f) : 1f;
        blocker.color = new Color(0f, 0f, 0f, opacity.alpha);
        if (resultFrame != null)
        {
            float resultAlpha = revealing && loaded ? 1f : Mathf.InverseLerp(0.3f, 0.75f, amount);
            result.color = new Color(1f, 1f, 1f, resultAlpha);
        }
    }

    public void Release()
    {
        if (sourceFrame != null) Object.Destroy(sourceFrame);
        if (resultFrame != null) Object.Destroy(resultFrame);
        if (destinationFrame != null) Object.Destroy(destinationFrame);
    }

    private RawImage AddScreen(string name, Rect window)
    {
        var screen = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        screen.transform.SetParent(board, false);
        screen.rectTransform.anchoredPosition = ArtworkCenter(window);
        screen.rectTransform.sizeDelta = window.size;
        screen.color = Color.black;
        screen.raycastTarget = false;
        return screen;
    }

    private static Vector2 ArtworkCenter(Rect window)
    {
        return new Vector2(window.center.x - ArtworkSize.x / 2f, ArtworkSize.y / 2f - window.center.y);
    }

    private static RenderTexture CreateFrame(string name, int width, int height)
    {
        return new RenderTexture(Mathf.Max(1, width), Mathf.Max(1, height), 24) { name = name };
    }

    public static RenderTexture CaptureScreen(string name)
    {
        RenderTexture frame = CreateFrame(name, Screen.width, Screen.height);
        try
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(frame);
            return frame;
        }
        catch
        {
            Object.Destroy(frame);
            throw;
        }
    }

    private static Rect CropUV(Texture texture, Rect window)
    {
        float aspect = (float)texture.width / texture.height;
        float windowAspect = window.width / window.height;
        float width = Mathf.Min(1f, windowAspect / aspect);
        float height = Mathf.Min(1f, aspect / windowAspect);
        return new Rect((1f - width) / 2f, (1f - height) / 2f, width, height);
    }
}
