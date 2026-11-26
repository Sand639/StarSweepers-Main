using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ラウンドイベントが始まる少し前に、画面を横切る赤い帯と「緊急連絡！」を出し、2回点滅させてから、
/// 「緊急連絡！」を右へ送り出し、イベントの見出しと説明（「特別調達依頼」など）を左から入れて、帯ごと消す。
/// 説明：Documents/スクリプトの説明/Game/UI/EventBannerUI.md
/// </summary>
public sealed class EventBannerUI : MonoBehaviour
{
    /// <summary>ゲームの開始時に読み込むプレハブ（Assets/Resources/EventBannerUI.prefab）。</summary>
    public const string PrefabResourcePath = "EventBannerUI";

    /// <summary>イベントの種類ごとの、見出しと説明の文字。</summary>
    [Serializable]
    public sealed class EventMessage
    {
        [Tooltip("イベントの種類")]
        public SpaceJunkEventKind kind;

        [Tooltip("大きく出す見出し")]
        public string title;

        [Tooltip("見出しの下に小さく出す説明（空なら出さない）")]
        public string subtitle;

        public EventMessage()
        {
        }

        public EventMessage(SpaceJunkEventKind kind, string title, string subtitle)
        {
            this.kind = kind;
            this.title = title;
            this.subtitle = subtitle;
        }
    }

    [Header("部品（プレハブで割り当て済み）")]
    [Tooltip("表示ごと隠すときにオン・オフする子オブジェクト")]
    [SerializeField] private GameObject content;

    [Tooltip("帯（UI_EventBanner_base）")]
    [SerializeField] private Image banner;

    [Tooltip("最初に出す文字（「緊急連絡！」）")]
    [SerializeField] private Text label;

    [Tooltip("イベントの見出しの文字")]
    [SerializeField] private Text eventTitle;

    [Tooltip("イベントの説明の文字")]
    [SerializeField] private Text eventSubtitle;

    [Header("文字")]
    [Tooltip("最初に出す文字")]
    [SerializeField] private string message = "緊急連絡！";

    [Tooltip("イベントごとの見出しと説明。種類を選んで書く（一覧に無いイベントのときは「緊急連絡！」のまま消える）")]
    [SerializeField] private EventMessage[] eventMessages =
    {
        new EventMessage(SpaceJunkEventKind.DebrisSet, "特別調達依頼", "指定された資材を集めて、ボーナススコアを獲得しよう！"),
        new EventMessage(SpaceJunkEventKind.HighValueDebris, "市場価格高騰！", "特定の資材が高値で取引中！今のうちに回収しよう！"),
        new EventMessage(SpaceJunkEventKind.HeavyDebris, "高重量デブリ発生！", ""),
    };

    [Tooltip("文字のフォント。空ならパソコンに入っている日本語のフォントを使う（デザイナーさんの指定のフォントが来たら入れる）")]
    [SerializeField] private Font font;

    [Header("演出（秒。出始めを 0 として数える。イベントの始まる時刻から決まるので、全員の画面で同じ時に出る）")]
    [Tooltip("イベントが始まる何秒前に出し始めるか")]
    [SerializeField, Min(0f)] private float leadSeconds = 5f;

    [Tooltip("1回目の点滅（帯と文字が消える）が始まる時刻")]
    [SerializeField, Min(0f)] private float firstBlinkAt = 0.3f;

    [Tooltip("点滅で消えている秒数")]
    [SerializeField, Min(0f)] private float blinkOffSeconds = 0.3f;

    [Tooltip("点滅の間隔（1回目の点滅の始まりから、2回目の点滅の始まりまで）")]
    [SerializeField, Min(0.01f)] private float blinkInterval = 0.6f;

    [Tooltip("点滅の回数")]
    [SerializeField, Min(0)] private int blinkCount = 2;

    [Tooltip("「緊急連絡！」が右へ出ていき、イベントの文字が左から入ってくる時刻")]
    [SerializeField, Min(0f)] private float switchAt = 1.5f;

    [Tooltip("入れ替わりにかかる秒数")]
    [SerializeField, Min(0f)] private float switchSeconds = 1f;

    [Tooltip("入れ替わりで文字が動く距離")]
    [SerializeField] private float switchDistance = 1200f;

    [Tooltip("帯と文字が消え始める時刻")]
    [SerializeField, Min(0f)] private float fadeAt = 4f;

    [Tooltip("消えるまでの秒数")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.5f;

    [Header("テスト用（ラウンドが無いときだけ使う）")]
    [Tooltip("オンなら、ラウンドが無くても一定の間隔でくり返し出す。検証シーン用。本番（Resources のプレハブ）ではオフ")]
    [SerializeField] private bool testMode;

    [Tooltip("テストで出す間隔（秒）。回ごとに、イベントごとの文字を順に出す")]
    [SerializeField, Min(1f)] private float testInterval = 6f;

    /// <summary>いま動いている係（ゲーム中は1つだけ）。</summary>
    public static EventBannerUI Instance { get; private set; }

    private float bannerAlpha = 1f;
    private Vector2 labelPosition;
    private Vector2 titlePosition;
    private Vector2 subtitlePosition;
    private float testStart = float.NaN;

    /// <summary>出てから消えるまでの秒数。</summary>
    private float TotalSeconds => fadeAt + fadeSeconds;

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
            bannerAlpha = banner.color.a;
        }

        labelPosition = PrepareText(label);
        titlePosition = PrepareText(eventTitle);
        subtitlePosition = PrepareText(eventSubtitle);

        if (label != null)
        {
            label.text = message;
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
        float shown = ReadSecondsShown(Time.unscaledTime, out SpaceJunkEventKind kind);
        bool visible = shown >= 0f && shown < TotalSeconds;
        SetVisible(visible);

        if (visible)
        {
            Draw(shown, kind);
        }
    }

    /// <summary>出始めてからの秒数（イベントの始まる Lead Seconds 秒前が 0）と、起きるイベントの種類。出す時でなければ -1。</summary>
    private float ReadSecondsShown(float now, out SpaceJunkEventKind kind)
    {
        SpaceJunkRound round = SpaceJunkRound.Current;
        if (round != null)
        {
            kind = round.ActiveEvent != SpaceJunkEventKind.None ? round.ActiveEvent : round.UpcomingEvent;

            // 始まる前（0 は「時刻になったが、ホストがまだ始めていない」一瞬）
            float until = round.SecondsUntilEvent;
            if (round.Phase == SpaceJunkRoundPhase.Playing && until >= 0f && until <= leadSeconds)
            {
                return leadSeconds - until;
            }

            // 始まったあと（演出が残っている間だけ）
            float since = round.SecondsSinceEventStarted;
            return since >= 0f ? leadSeconds + since : -1f;
        }

        kind = SpaceJunkEventKind.None;
        if (!testMode)
        {
            return -1f;
        }

        if (float.IsNaN(testStart))
        {
            testStart = now;
        }

        // 回ごとに、イベントごとの文字を順に見せる
        float elapsed = now - testStart;
        if (eventMessages != null && eventMessages.Length > 0)
        {
            kind = eventMessages[Mathf.FloorToInt(elapsed / testInterval) % eventMessages.Length].kind;
        }

        return elapsed % testInterval;
    }

    /// <summary>出始めてから shown 秒の見た目にする。kind は起きるイベントの種類。</summary>
    private void Draw(float shown, SpaceJunkEventKind kind)
    {
        // 点滅で消えている間と、最後に消えていくぶんの濃さ
        float alpha = IsBlinkOff(shown) ? 0f : 1f - Clamp01Div(shown - fadeAt, fadeSeconds);

        if (banner != null)
        {
            Color color = banner.color;
            color.a = bannerAlpha * alpha;
            banner.color = color;
        }

        // 入れ替わり：「緊急連絡！」は右へ出ていき、イベントの文字は左から入ってくる（イベントの文字が無ければ入れ替えない）
        bool hasEvent = TryGetEventText(kind, out EventMessage entry);
        float swap = hasEvent ? EaseInOutCubic(Clamp01Div(shown - switchAt, switchSeconds)) : 0f;

        DrawText(label, message, labelPosition + new Vector2(swap * switchDistance, 0f), alpha, swap < 1f);

        Vector2 enter = new Vector2(-(1f - swap) * switchDistance, 0f);
        bool showEvent = hasEvent && swap > 0f;
        DrawText(eventTitle, hasEvent ? entry.title : null, titlePosition + enter, alpha, showEvent);
        DrawText(eventSubtitle, hasEvent ? entry.subtitle : null, subtitlePosition + enter, alpha,
                 showEvent && !string.IsNullOrEmpty(entry.subtitle));
    }

    /// <summary>点滅で消えている時刻か。</summary>
    private bool IsBlinkOff(float shown)
    {
        for (int n = 0; n < blinkCount; n++)
        {
            float start = firstBlinkAt + blinkInterval * n;
            if (shown >= start && shown < start + blinkOffSeconds)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>文字を1つ、決めた位置・濃さで出す（visible が false なら隠す）。</summary>
    private static void DrawText(Text text, string value, Vector2 position, float alpha, bool visible)
    {
        if (text == null)
        {
            return;
        }

        if (text.gameObject.activeSelf != visible)
        {
            text.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        if (text.text != value)
        {
            text.text = value;
        }

        text.rectTransform.anchoredPosition = position;
        Color color = text.color;
        color.a = alpha;
        text.color = color;
    }

    /// <summary>そのイベントの見出しと説明を探す。一覧に無い・見出しが空なら false。</summary>
    private bool TryGetEventText(SpaceJunkEventKind kind, out EventMessage found)
    {
        found = null;
        if (eventMessages == null || kind == SpaceJunkEventKind.None)
        {
            return false;
        }

        foreach (EventMessage entry in eventMessages)
        {
            if (entry != null && entry.kind == kind && !string.IsNullOrEmpty(entry.title))
            {
                found = entry;
                return true;
            }
        }

        return false;
    }

    /// <summary>フォントを入れ、元の位置を返す。</summary>
    private Vector2 PrepareText(Text text)
    {
        if (text == null)
        {
            return Vector2.zero;
        }

        text.font = font != null ? font : UiFont.Find(text.fontSize);
        return text.rectTransform.anchoredPosition;
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

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    [ContextMenu("テスト：今すぐもう一度出す")]
    private void TestReplay()
    {
        testStart = Time.unscaledTime;
    }
}
