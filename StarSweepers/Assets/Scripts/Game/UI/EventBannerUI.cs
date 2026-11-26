using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドイベントが始まった瞬間に、画面を横切る赤い帯と「緊急連絡！」の文字を出して知らせる
/// （帯が上下に開く → 文字が右から滑り込む → 帯が点滅しながら少し残る → 帯が閉じて消える）。
/// 説明：Documents/スクリプトの説明/Game/UI/EventBannerUI.md
/// </summary>
public sealed class EventBannerUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/EventBannerUI.prefab）。</summary>
    public const string PrefabResourcePath = "EventBannerUI";

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Tooltip("帯（UI_EventBanner_base）。上下に開いて出てくる")]
    [SerializeField] private Image banner;

    [Tooltip("帯の上に出す文字")]
    [SerializeField] private Text label;

    [Header("文字")]
    [Tooltip("帯の上に出す文字")]
    [SerializeField] private string message = "緊急連絡！";

    [Tooltip("文字のフォント。空ならパソコンに入っている日本語のフォントを使う（デザイナーさんの指定のフォントが来たら入れる）")]
    [SerializeField] private Font font;

    [Header("演出（イベントが始まってからの秒数で決まるので、全員の画面で同じ時に出る）")]
    [Tooltip("帯が上下に開く秒数")]
    [SerializeField, Min(0f)] private float openSeconds = 0.2f;

    [Tooltip("開ききってから閉じ始めるまでの秒数")]
    [SerializeField, Min(0f)] private float holdSeconds = 1.8f;

    [Tooltip("帯が閉じる秒数")]
    [SerializeField, Min(0f)] private float closeSeconds = 0.25f;

    [Tooltip("文字が滑り込んでくる秒数（帯が開き始めると同時に始まる）")]
    [SerializeField, Min(0f)] private float textSlideSeconds = 0.3f;

    [Tooltip("文字が滑り込んでくる距離（右から）")]
    [SerializeField] private float textSlideDistance = 320f;

    [Tooltip("帯の点滅の速さ（1秒に何回）。0 なら点滅しない")]
    [SerializeField, Min(0f)] private float blinkPerSecond = 2.5f;

    [Tooltip("点滅でいちばん薄くなったときの帯の濃さ（1＝薄くならない）")]
    [SerializeField, Range(0f, 1f)] private float blinkMinAlpha = 0.6f;

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くても一定の間隔でくり返し出す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    [Tooltip("テストで出す間隔（秒）")]
    [SerializeField, Min(1f)] private float testInterval = 4f;

    /// <summary>いま動いている係（ゲーム中は1つだけ）。</summary>
    public static EventBannerUI Instance { get; private set; }

    private RectTransform bannerRect;
    private Vector2 labelPosition;
    private float bannerAlpha = 1f;
    private float testStart = float.NaN;

    /// <summary>出てから消えるまでの秒数。</summary>
    private float TotalSeconds => openSeconds + holdSeconds + closeSeconds;

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
            Debug.LogWarning($"[イベントの警告バナー] Resources/{PrefabResourcePath}.prefab が無いので、イベントの始まりに帯は出ません。");
            return;
        }

        GameObject created = Instantiate(prefab);
        created.name = "EventBannerUI";
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

        if (banner != null)
        {
            bannerRect = banner.rectTransform;
            bannerAlpha = banner.color.a;
        }

        if (label != null)
        {
            labelPosition = label.rectTransform.anchoredPosition;
            label.text = message;
            label.font = font != null ? font : UiFont.Find(label.fontSize);
        }

        SetVisible(false);
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
        float since = ReadSecondsSinceStart(Time.unscaledTime);
        bool visible = since >= 0f && since < TotalSeconds;
        SetVisible(visible);

        if (visible)
        {
            Draw(since);
        }
    }

    /// <summary>イベントが始まってからの秒数。イベントが起きていなければ -1。</summary>
    private float ReadSecondsSinceStart(float now)
    {
        SpaceJunkRound round = SpaceJunkRound.Current;
        if (round != null)
        {
            return round.SecondsSinceEventStarted;
        }

        if (!testMode)
        {
            return -1f;
        }

        if (float.IsNaN(testStart))
        {
            testStart = now;
        }

        return (now - testStart) % testInterval;
    }

    /// <summary>始まってから since 秒の見た目にする。</summary>
    private void Draw(float since)
    {
        float closing = Clamp01Div(since - openSeconds - holdSeconds, closeSeconds);
        float open = EaseOutCubic(Clamp01Div(since, openSeconds)) * (1f - EaseInCubic(closing));

        if (bannerRect != null)
        {
            bannerRect.localScale = new Vector3(1f, open, 1f);

            // 開ききっている間だけ、警報のように点滅させる（開いた瞬間がいちばん濃い）
            float blink = blinkPerSecond > 0f && since > openSeconds
                ? Mathf.Lerp(blinkMinAlpha, 1f, 0.5f + 0.5f * Mathf.Cos((since - openSeconds) * blinkPerSecond * Mathf.PI * 2f))
                : 1f;
            Color color = banner.color;
            color.a = bannerAlpha * blink;
            banner.color = color;
        }

        if (label != null)
        {
            float slide = EaseOutCubic(Clamp01Div(since, textSlideSeconds));
            label.rectTransform.anchoredPosition = labelPosition + new Vector2((1f - slide) * textSlideDistance, 0f);

            Color color = label.color;
            color.a = slide * (1f - closing);
            label.color = color;
        }
    }

    private void SetVisible(bool visible)
    {
        visible &= !RoundResultPlayback.IsPresenting;
        if (content != null && content.activeSelf != visible)
        {
            content.SetActive(visible);
        }
    }

    private static float Clamp01Div(float value, float duration)
    {
        return duration <= 0f ? (value >= 0f ? 1f : 0f) : Mathf.Clamp01(value / duration);
    }

    private static float EaseOutCubic(float t)
    {
        float rest = 1f - Mathf.Clamp01(t);
        return 1f - rest * rest * rest;
    }

    private static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    [ContextMenu("テスト：今すぐもう一度出す")]
    private void TestReplay()
    {
        testStart = Time.unscaledTime;
    }
}
