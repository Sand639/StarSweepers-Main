using UnityEngine;
using UnityEngine.UI;

/// <summary>切り替え中の2つのモニターに、走査線と細かい砂嵐を重ねる。</summary>
internal sealed class MonitorScreenNoise : MonoBehaviour
{
    private const int TileWidth = 256;
    private const int TileHeight = 128;
    private const int ScanlineSpacing = 4;
    private const float GrainSize = 1.35f;
    private const float MaxOpacity = 0.65f;
    private const double FramesPerSecond = 24d;

    private RawImage previous;
    private RawImage next;
    private Texture2D tile;
    private float opacity;
    private long lastFrame = -1;

    public void Initialize(RawImage previousScreen, RawImage nextScreen)
    {
        previous = previousScreen;
        next = nextScreen;
        previous.enabled = false;
        next.enabled = false;
    }

    public void SetPullBack(float amount)
    {
        // 前後のシーンが画面いっぱいのときは、モニターのノイズを消す。
        float strength = Mathf.SmoothStep(0f, MaxOpacity, Mathf.InverseLerp(0.08f, 0.65f, amount));
        if (strength == opacity) return;

        opacity = strength;
        Color tint = new Color(1f, 1f, 1f, opacity);
        previous.color = tint;
        next.color = tint;
        previous.enabled = next.enabled = opacity > 0f && tile != null;
    }

    private void LateUpdate()
    {
        if (opacity <= 0f) return;

        if (tile == null)
        {
            tile = CreateTile();
            previous.texture = next.texture = tile;
            previous.enabled = next.enabled = true;
        }

        // 読み込み待ちやポーズ中も、走査線はその場で砂嵐だけが動く。
        long frame = (long)(Time.unscaledTimeAsDouble * FramesPerSecond);
        if (frame == lastFrame) return;

        lastFrame = frame;
        ScrollNoise(previous, frame);
        ScrollNoise(next, frame + 13);
    }

    private static void ScrollNoise(RawImage overlay, long frame)
    {
        Vector2 size = overlay.rectTransform.rect.size / GrainSize;
        float x = (float)(frame * 17 % TileWidth) / TileWidth;
        float y = (float)(frame * (ScanlineSpacing * 3) % TileHeight) / TileHeight;
        overlay.uvRect = new Rect(x, y, size.x / TileWidth, size.y / TileHeight);
    }

    private static Texture2D CreateTile()
    {
        var texture = new Texture2D(TileWidth, TileHeight, TextureFormat.RGBA32, false)
        {
            name = "MonitorNoiseTile",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat,
        };

        var random = new System.Random(241);
        var pixels = new Color32[TileWidth * TileHeight];
        for (int y = 0; y < TileHeight; y++)
        {
            bool scanline = y % ScanlineSpacing == 0;
            for (int x = 0; x < TileWidth; x++)
            {
                byte tone = scanline || random.Next(2) == 0 ? (byte)0 : (byte)255;
                byte alpha = (byte)(random.Next(8, 56) + (scanline ? 65 : 0));
                pixels[y * TileWidth + x] = new Color32(tone, tone, tone, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        if (tile != null) Destroy(tile);
    }
}
