using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>負けたチームの色を敷き、勝ったチームの四角で覆う演出。</summary>
public sealed class SquareWinnerTransitionUI : MonoBehaviour
{
    [SerializeField, Min(2)] private int columns = 10;
    [SerializeField, Min(0.1f)] private float layerSeconds = 0.85f;
    [SerializeField, Min(0f)] private float firstColorHold = 0.3f;
    [SerializeField, Min(0f)] private float winnerHold = 1f;

    private RectTransform root;
    private RectTransform[] firstTiles;
    private RectTransform[] winnerTiles;
    private double startedAt;
    private bool playing;
    private Action completed;

    public bool IsPlaying => playing;

    /// <summary>青は0、赤は1。最後まで覆ったあと、画面を消して完了を知らせる。</summary>
    public void Play(int winnerTeam, Action onCompleted = null)
    {
        if (winnerTeam != 0 && winnerTeam != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(winnerTeam));
        }

        EnsureCanvas();
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
        int rows = Mathf.Max(2, Mathf.RoundToInt(columns * (float)Screen.height / Mathf.Max(1, Screen.width)));
        firstTiles = BuildLayer("FirstColor", rows);
        winnerTiles = BuildLayer("WinnerColor", rows);
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
                rect.anchorMin = new Vector2((float)x / columns, (float)y / rows);
                rect.anchorMax = new Vector2((float)(x + 1) / columns, (float)(y + 1) / rows);
                // 境界の隙間を防ぐため、覆いきったときだけ隣と少し重ねる。
                rect.offsetMin = -Vector2.one;
                rect.offsetMax = Vector2.one;
                tile.GetComponent<Image>().raycastTarget = false;
                tiles[y * columns + x] = rect;
            }
        }
        return tiles;
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
        Apply(elapsed);
        if (elapsed >= layerSeconds * 2f + firstColorHold + winnerHold)
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
        ApplyLayer(firstTiles, elapsed / layerSeconds);
        ApplyLayer(winnerTiles, (elapsed - layerSeconds - firstColorHold) / layerSeconds);
    }

    private static void ApplyLayer(RectTransform[] tiles, float progress)
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            // 毎回同じ順序でばらけさせ、勝敗を比べやすくする。
            float delay = ((i * 37 + 17) % 101) / 100f * 0.55f;
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - delay) / 0.45f));
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
