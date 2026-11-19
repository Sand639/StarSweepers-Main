using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>赤勝利と青勝利を交互に見せる検証用の入口。</summary>
public sealed class SquareWinnerTransitionTester : MonoBehaviour
{
    private SquareWinnerTransitionUI effect;
    [SerializeField] private RoundWinnerResultUI result;
    private RoundWinnerResultUI.ResultStyle style;
    private int redWins = 1;
    private int blueWins = 1;
    private int nextWinner = 1;
    private double nextPlayAt;
    private bool started;
    private int lastWinner = 1;

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
        else if (keyboard != null && keyboard.enterKey.wasPressedThisFrame && result.IsFinished && !effect.IsPlaying)
        {
            redWins += lastWinner == 1 ? 1 : 0;
            blueWins += lastWinner == 0 ? 1 : 0;
            Play(nextWinner);
        }
        else if (!started && Time.unscaledTimeAsDouble >= nextPlayAt)
        {
            Play(nextWinner);
        }
    }

    private void Play(int winner)
    {
        started = true;
        lastWinner = winner;
        nextWinner = 1 - winner;
        result.Hide();
        effect.Play(winner, ShowResult);
    }

    private void SetStyle(RoundWinnerResultUI.ResultStyle selected)
    {
        style = selected;
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

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 1050, 35),
            "1: RED wins  2: BLUE wins  |  3: Star  4: Collision  5: Hook throw  |  Enter: Next preview");
        GUI.Label(new Rect(20, 50, 600, 35), $"Mode: {style}    Character art: robot prototype");
    }
}
