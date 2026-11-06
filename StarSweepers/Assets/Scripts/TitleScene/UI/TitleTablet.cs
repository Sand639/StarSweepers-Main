using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

/// <summary>タイトル画面の「タブレット」（右から出てくる枠と、右下の操作の案内）。</summary>
public class TitleTablet
{
    private const float SlideSeconds = 0.25f;

    /// <summary>出てくる前に右へずらしておく距離（1920×1080 のときのピクセル）。</summary>
    private const float SlideDistance = 900f;

    private RectTransform sliding;
    private float slideTime;

    /// <summary>タブレットの枠の、ずらす前の位置。</summary>
    private readonly Dictionary<RectTransform, Vector2> home = new Dictionary<RectTransform, Vector2>();

    /// <summary>右下の操作の案内（タブレットの画面ごとにある）。</summary>
    private readonly List<Text> hints = new List<Text>();

    /// <summary>案内に今出しているのがコントローラー用か（null はまだ出していない）。</summary>
    private bool? hintForGamepad;

    /// <summary>タブレットが開いているか。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>root の下の操作の案内を全部集める。</summary>
    public TitleTablet(GameObject root)
    {
        foreach (TitlePart part in root.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            if (part.Role == TitlePartRole.TabletHint && text != null)
            {
                hints.Add(text);
            }
        }
    }

    /// <summary>画面を切り替えたとき。閉じた状態からタブレットの画面を開いたときだけ、右から出す。</summary>
    public void Show(GameObject page, bool tabletPage)
    {
        FinishSlide();

        if (tabletPage && !IsOpen)
        {
            StartSlide(page);
        }

        IsOpen = tabletPage;
    }

    /// <summary>タブレットを閉じた扱いにする（画面が消えるとき）。</summary>
    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>出てくる途中の枠を動かし、操作の案内を出し分ける（毎フレーム呼ぶ）。</summary>
    public void Update()
    {
        UpdateSlide();
        UpdateHints();
    }

    /// <summary>その画面のタブレットの枠を右へずらし、元の位置へ動かし始める。</summary>
    private void StartSlide(GameObject page)
    {
        RectTransform panel = page != null ? PanelOf(page) : null;
        if (panel == null)
        {
            return;
        }

        if (!home.ContainsKey(panel))
        {
            home[panel] = panel.anchoredPosition;
        }

        sliding = panel;
        slideTime = 0f;
        panel.anchoredPosition = home[panel] + new Vector2(SlideDistance, 0f);
    }

    /// <summary>出てくる途中の枠を、少しずつ元の位置へ動かす。</summary>
    private void UpdateSlide()
    {
        if (sliding == null)
        {
            return;
        }

        slideTime += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(slideTime / SlideSeconds);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        sliding.anchoredPosition = home[sliding] + new Vector2(SlideDistance * (1f - eased), 0f);

        if (t >= 1f)
        {
            sliding = null;
        }
    }

    /// <summary>出てくる途中の枠を、すぐ元の位置に置く。</summary>
    private void FinishSlide()
    {
        if (sliding != null)
        {
            sliding.anchoredPosition = home[sliding];
            sliding = null;
        }
    }

    /// <summary>その画面の中のタブレットの枠（印 TabletPanel）。</summary>
    private static RectTransform PanelOf(GameObject page)
    {
        foreach (TitlePart part in page.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == TitlePartRole.TabletPanel)
            {
                return part.transform as RectTransform;
            }
        }

        return null;
    }

    /// <summary>操作の案内を、最後に触った物（コントローラーかキーボード）に合わせて出し分ける。</summary>
    private void UpdateHints()
    {
        bool gamepad = UiPointer.LastInputWasGamepad && Gamepad.current != null;
        if (hintForGamepad == gamepad)
        {
            return;
        }

        hintForGamepad = gamepad;
        bool playStation = Gamepad.current is DualShockGamepad;
        string text = !gamepad ? "<b>Esc</b>　戻る"
            : playStation ? "<b>×</b>　確認　　<b>○</b>　戻る"
            : "<b>A</b>　確認　　<b>B</b>　戻る";

        foreach (Text hint in hints)
        {
            if (hint != null)
            {
                hint.text = text;
            }
        }
    }
}
