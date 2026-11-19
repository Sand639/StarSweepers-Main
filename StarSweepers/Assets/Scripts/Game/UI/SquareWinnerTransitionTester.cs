using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>赤勝利と青勝利を交互に見せる検証用の入口。</summary>
public sealed class SquareWinnerTransitionTester : MonoBehaviour
{
    private SquareWinnerTransitionUI effect;
    [SerializeField] private RoundWinnerResultUI result;
    [SerializeField] private string nextStage = "STAGE_Simple";
    [SerializeField, Min(0f)] private float resultHoldSeconds = 0.7f;
    private RoundWinnerResultUI.ResultStyle style;
    private int redWins = 1;
    private int blueWins = 1;
    private int nextWinner = 1;
    private double nextPlayAt;
    private bool started;
    private int lastWinner = 1;
    private double resultFinishedAt = -1;
    private bool changingStage;

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
        if (changingStage)
        {
            return;
        }
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
        else if (result.IsFinished && !effect.IsPlaying)
        {
            if (resultFinishedAt < 0)
            {
                resultFinishedAt = Time.unscaledTimeAsDouble;
            }
            if (Time.unscaledTimeAsDouble >= resultFinishedAt + resultHoldSeconds)
            {
                if (!SpaceJunkLobbyUI.IsSceneInBuildList(nextStage))
                {
                    Debug.LogError($"[RESULT] 次のステージがビルド一覧にありません: {nextStage}");
                    changingStage = true;
                    return;
                }
                changingStage = SceneTransition.Play(LoadNextStage, SceneTransitionType.RoundedStripes);
            }
        }
        else if (!started && Time.unscaledTimeAsDouble >= nextPlayAt)
        {
            Play(nextWinner);
        }
    }

    private void Play(int winner)
    {
        started = true;
        resultFinishedAt = -1;
        lastWinner = winner;
        nextWinner = 1 - winner;
        result.Hide();
        effect.Play(winner, ShowResult);
    }

    private void SetStyle(RoundWinnerResultUI.ResultStyle selected)
    {
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

    private void LoadNextStage()
    {
        result.Hide();
        SceneManager.LoadSceneAsync(nextStage);
    }

}
