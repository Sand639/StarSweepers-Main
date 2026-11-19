using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>赤勝利と青勝利を交互に見せる検証用の入口。</summary>
public sealed class SquareWinnerTransitionTester : MonoBehaviour
{
    private SquareWinnerTransitionUI effect;
    private int nextWinner = 1;
    private double nextPlayAt;

    private void Start()
    {
        effect = gameObject.AddComponent<SquareWinnerTransitionUI>();
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
        else if (!effect.IsPlaying && Time.unscaledTimeAsDouble >= nextPlayAt)
        {
            Play(nextWinner);
        }
    }

    private void Play(int winner)
    {
        nextWinner = 1 - winner;
        effect.Play(winner, () => nextPlayAt = Time.unscaledTimeAsDouble + 1);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 550, 35), "1: RED wins (BLUE → RED)    2: BLUE wins (RED → BLUE)");
    }
}
