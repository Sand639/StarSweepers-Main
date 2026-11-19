using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>負けたチームの色を敷き、勝ったチームの四角で覆う演出。</summary>
public sealed class SquareWinnerTransitionUI : MonoBehaviour
{
    [SerializeField, Min(2)] private int columns = 5;
    [SerializeField, Min(0.1f)] private float layerSeconds = 0.65f;
    [Tooltip("最初の色が出始めてから、勝った色が広がり始めるまでの秒数")]
    [SerializeField, Min(0f)] private float winnerStartDelay = 0.18f;
    [SerializeField, Min(0f)] private float winnerHold = 0.45f;

    private RectTransform root;
    private RectTransform[] firstTiles;
    private RectTransform[] winnerTiles;
    private double startedAt;
    private bool playing;
    private Action completed;
    private int rows;
    private Vector2 layoutSize;

    public bool IsPlaying => playing;

    /// <summary>青は0、赤は1。最後まで覆ったあと、画面を消して完了を知らせる。</summary>
    public void Play(int winnerTeam, Action onCompleted = null)
    {
        if (winnerTeam != 0 && winnerTeam != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(winnerTeam));
        }

        EnsureCanvas();
        UpdateLayout();
        Color winner = SpaceJunkTeams.TeamColor(winnerTeam);
        Color other = SpaceJunkTeams.TeamColor(1 - winnerTeam);
        SetColors(firstTiles, other);
        SetColors(winnerTiles, winner);
        completed = onCompleted;
        startedAt = Time.unscaledTimeAsDouble;
        playing = true;
        root.gameObject.SetActive(true);
        Apply(0f);
    }

    private void EnsureCanvas()
    {
        if (root != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("SquareWinnerCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(transform, false);
        root = canvasObject.GetComponent<RectTransform>();
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        columns = Mathf.Max(2, columns);
        rows = Mathf.Max(2, Mathf.CeilToInt(columns * (float)Screen.height / Mathf.Max(1, Screen.width)));
        firstTiles = BuildLayer("FirstColor", rows);
        winnerTiles = BuildLayer("WinnerColor", rows);
        Canvas.ForceUpdateCanvases();
    }

    private RectTransform[] BuildLayer(string layerName, int rows)
    {
        GameObject layer = new GameObject(layerName, typeof(RectTransform));
        RectTransform layerRect = layer.GetComponent<RectTransform>();
        layerRect.SetParent(root, false);
        layerRect.anchorMin = Vector2.zero;
        layerRect.anchorMax = Vector2.one;
        layerRect.offsetMin = Vector2.zero;
        layerRect.offsetMax = Vector2.zero;
        RectTransform[] tiles = new RectTransform[columns * rows];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                GameObject tile = new GameObject("Square", typeof(RectTransform), typeof(Image));
                RectTransform rect = tile.GetComponent<RectTransform>();
                rect.SetParent(layerRect, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = rect.anchorMin;
                tile.GetComponent<Image>().raycastTarget = false;
                tiles[y * columns + x] = rect;
            }
        }
        return tiles;
    }

    private void UpdateLayout()
    {
        Vector2 size = root.rect.size;
        float cell = Mathf.Max(size.x / columns, size.y / rows);
        for (int i = 0; i < firstTiles.Length; i++)
        {
            Vector2 position = new Vector2(
                (i % columns + 0.5f - columns * 0.5f) * cell,
                (i / columns + 0.5f - rows * 0.5f) * cell);
            // 画面の比率にかかわらず正方形。外周は画面外まで覆う。
            firstTiles[i].anchoredPosition = position;
            winnerTiles[i].anchoredPosition = position;
            firstTiles[i].sizeDelta = Vector2.one * (cell + 1f);
            winnerTiles[i].sizeDelta = firstTiles[i].sizeDelta;
        }
        layoutSize = size;
    }

    private static void SetColors(RectTransform[] tiles, Color color)
    {
        foreach (RectTransform tile in tiles)
        {
            tile.GetComponent<Image>().color = color;
            tile.localScale = Vector3.zero;
        }
    }

    private void Update()
    {
        if (!playing)
        {
            return;
        }

        float elapsed = (float)(Time.unscaledTimeAsDouble - startedAt);
        if (layoutSize != root.rect.size)
        {
            UpdateLayout();
        }
        Apply(elapsed);
        if (elapsed >= Mathf.Max(layerSeconds, winnerStartDelay + layerSeconds) + winnerHold)
        {
            playing = false;
            root.gameObject.SetActive(false);
            Action callback = completed;
            completed = null;
            callback?.Invoke();
        }
    }

    private void Apply(float elapsed)
    {
        ApplyLayer(firstTiles, elapsed / layerSeconds, 0);
        ApplyLayer(winnerTiles, (elapsed - winnerStartDelay) / layerSeconds, 43);
    }

    private static void ApplyLayer(RectTransform[] tiles, float progress, int phase)
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            // 毎回同じ順序でばらけさせ、勝敗を比べやすくする。
            float delay = ((i * 37 + 17 + phase) % 101) / 100f * 0.35f;
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - delay) / 0.65f));
            tiles[i].localScale = Vector3.one * amount;
        }
    }

    private void OnDisable()
    {
        playing = false;
        completed = null;
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
    }
}
