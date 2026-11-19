using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>赤勝利と青勝利を交互に見せる検証用の入口。</summary>
public sealed class SquareWinnerTransitionTester : MonoBehaviour
{
    private SquareWinnerTransitionUI effect;
    [SerializeField] private RoundWinnerResultUI result;
    [SerializeField, Min(0f)] private float resultHoldSeconds = 0.7f;
    private RoundWinnerResultUI.ResultStyle style;
    private int redWins = 1;
    private int blueWins = 1;
    private int nextWinner = 1;
    private double nextPlayAt;
    private bool started;
    private int lastWinner = 1;
    private double resultFinishedAt = -1;
    private Coroutine stripeRoutine;
    private GameObject stripeCanvas;

    private void Start()
    {
        effect = gameObject.AddComponent<SquareWinnerTransitionUI>();
        if (result == null)
        {
            result = gameObject.AddComponent<RoundWinnerResultUI>();
        }
        nextPlayAt = Time.unscaledTimeAsDouble + 1;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.digit1Key.wasPressedThisFrame)
        {
            Play(1);
        }
        else if (keyboard != null && keyboard.digit2Key.wasPressedThisFrame)
        {
            Play(0);
        }
        else if (keyboard != null && keyboard.digit3Key.wasPressedThisFrame)
        {
            SetStyle(RoundWinnerResultUI.ResultStyle.StarAward);
        }
        else if (keyboard != null && keyboard.digit4Key.wasPressedThisFrame)
        {
            SetStyle(RoundWinnerResultUI.ResultStyle.Collision);
        }
        else if (keyboard != null && keyboard.digit5Key.wasPressedThisFrame)
        {
            SetStyle(RoundWinnerResultUI.ResultStyle.HookThrow);
        }
        else if (result.IsFinished && !effect.IsPlaying && stripeRoutine == null)
        {
            if (resultFinishedAt < 0)
            {
                resultFinishedAt = Time.unscaledTimeAsDouble;
            }
            if (Time.unscaledTimeAsDouble >= resultFinishedAt + resultHoldSeconds)
            {
                stripeRoutine = StartCoroutine(PreviewStripes());
            }
        }
        else if (!started && Time.unscaledTimeAsDouble >= nextPlayAt)
        {
            Play(nextWinner);
        }
    }

    private void Play(int winner)
    {
        StopStripePreview();
        started = true;
        resultFinishedAt = -1;
        lastWinner = winner;
        nextWinner = 1 - winner;
        result.Hide();
        effect.Play(winner, ShowResult);
    }

    private void SetStyle(RoundWinnerResultUI.ResultStyle selected)
    {
        StopStripePreview();
        style = selected;
        resultFinishedAt = -1;
        if (!effect.IsPlaying && started)
        {
            ShowResult();
        }
    }

    private void ShowResult()
    {
        result.Show(lastWinner, redWins + (lastWinner == 1 ? 1 : 0),
            blueWins + (lastWinner == 0 ? 1 : 0), 3, style);
    }

    private IEnumerator PreviewStripes()
    {
        stripeCanvas = new GameObject("StripePreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        stripeCanvas.transform.SetParent(transform, false);
        Canvas canvas = stripeCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1;
        CanvasScaler scaler = stripeCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        var stripes = new RoundedStripesTransitionEffect();
        stripes.Build(stripeCanvas.GetComponent<RectTransform>(), Color.white);
        yield return AnimateStripes(stripes, false);
        result.Hide();
        yield return new WaitForSecondsRealtime(0.2f);
        yield return AnimateStripes(stripes, true);
        Destroy(stripeCanvas);
        stripeCanvas = null;
        stripeRoutine = null;
    }

    private static IEnumerator AnimateStripes(SceneTransitionEffect stripes, bool revealing)
    {
        double startedAt = Time.unscaledTimeAsDouble;
        float progress;
        do
        {
            progress = Mathf.Clamp01((float)(Time.unscaledTimeAsDouble - startedAt) / 0.5f);
            stripes.Apply(Mathf.SmoothStep(0, 1, revealing ? 1 - progress : progress), revealing);
            yield return null;
        } while (progress < 1);
    }

    private void StopStripePreview()
    {
        if (stripeRoutine != null)
        {
            StopCoroutine(stripeRoutine);
            stripeRoutine = null;
        }
        if (stripeCanvas != null)
        {
            stripeCanvas.SetActive(false);
            Destroy(stripeCanvas);
            stripeCanvas = null;
        }
    }

    private void OnDisable()
    {
        StopStripePreview();
    }
}
