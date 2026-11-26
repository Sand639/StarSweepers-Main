using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

/// <summary>タイトル画面の「タブレット」（右から出てくる枠と、右下の操作の案内）。</summary>
public class TitleTablet
{
    private const float SlideSeconds = 0.25f;
    private const float NotebookOpenSeconds = 0.55f;
    private const float NotebookCloseSeconds = 0.42f;

    /// <summary>出てくる前に右へずらしておく距離（1920×1080 のときのピクセル）。</summary>
    private const float SlideDistance = 900f;

    private readonly StarSweepersTitlePresentation presentation;
    private RectTransform currentPanel;
    private RectTransform sliding;
    private float progress;
    private Vector2 restingPosition;
    private Quaternion restingRotation;
    private Action afterRetraction;

    /// <summary>右下の操作の案内（タブレットの画面ごとにある）。</summary>
    private readonly List<Text> hints = new List<Text>();

    /// <summary>案内に今出しているのがコントローラー用か（null はまだ出していない）。</summary>
    private bool? hintForGamepad;

    /// <summary>タブレットが開いているか。</summary>
    public bool IsOpen { get; private set; }
    public bool IsClosing => afterRetraction != null;

    /// <summary>root の下の操作の案内を全部集める。</summary>
    public TitleTablet(GameObject root)
    {
        presentation = root.GetComponent<StarSweepersTitlePresentation>();
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
        bool opening = tabletPage && !IsOpen;
        currentPanel = tabletPage && page != null ? PanelOf(page) : null;
        if (presentation != null)
        {
            presentation.ShowPage(tabletPage, opening);
        }

        if (opening && currentPanel != null)
        {
            StartSlide(0f);
        }

        IsOpen = tabletPage;
    }

    public bool TryRetract(Action onClosed)
    {
        if (presentation == null || currentPanel == null || IsClosing)
        {
            return false;
        }

        if (sliding == null)
        {
            StartSlide(1f);
        }

        afterRetraction = onClosed;
        presentation.PlayRetractionSwing();
        return true;
    }

    /// <summary>タブレットを閉じた扱いにする（画面が消えるとき）。</summary>
    public void Close()
    {
        FinishSlide();
        afterRetraction = null;
        currentPanel = null;
        IsOpen = false;
    }

    /// <summary>出てくる途中の枠を動かし、操作の案内を出し分ける（毎フレーム呼ぶ）。</summary>
    public void Update()
    {
        UpdateSlide();
        UpdateHints();
    }

    /// <summary>その画面のタブレットの枠を右へずらし、元の位置へ動かし始める。</summary>
    private void StartSlide(float startingProgress)
    {
        sliding = currentPanel;
        restingPosition = sliding.anchoredPosition;
        restingRotation = sliding.localRotation;
        progress = startingProgress;
        PlaceSlide();
    }

    /// <summary>出てくる途中の枠を、少しずつ元の位置へ動かす。</summary>
    private void UpdateSlide()
    {
        if (sliding == null)
        {
            return;
        }

        float target = IsClosing ? 0f : 1f;
        float seconds = IsClosing ? NotebookCloseSeconds : presentation != null ? NotebookOpenSeconds : SlideSeconds;
        progress = Mathf.MoveTowards(progress, target, Time.unscaledDeltaTime / seconds);
        if (progress == target)
        {
            Action callback = afterRetraction;
            afterRetraction = null;
            FinishSlide();
            callback?.Invoke();
            return;
        }

        PlaceSlide();
    }

    private void PlaceSlide()
    {
        float remaining = (1f - progress) * (1f - progress) * (1f - progress);
        if (presentation == null)
        {
            sliding.anchoredPosition = restingPosition + Vector2.right * (SlideDistance * remaining);
            return;
        }

        float height = -160f * remaining + 36f * Mathf.Sin(progress * Mathf.PI);
        sliding.anchoredPosition = restingPosition + new Vector2(1480f * remaining, height);
        sliding.localRotation = restingRotation * Quaternion.AngleAxis(-19f * remaining, Vector3.forward);
    }

    /// <summary>出てくる途中の枠を、すぐ元の位置に置く。</summary>
    private void FinishSlide()
    {
        if (sliding != null)
        {
            sliding.anchoredPosition = restingPosition;
            sliding.localRotation = restingRotation;
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
