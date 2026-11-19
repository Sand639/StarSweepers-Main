using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>実際の試合の結果表示。色帯とシーン読み込みはSessionが全員へ指示する。</summary>
public sealed class RoundResultPlayback : MonoBehaviour
{
    public static bool IsPresenting { get; private set; }
    private SquareWinnerTransitionUI squares;
    private RoundWinnerResultUI result;
    private Action finished;
    private double finishedAt = -1;

    public void Play(int winner, int redWins, int blueWins, int winsNeeded,
        RoundWinnerResultUI.ResultStyle style, Action onFinished)
    {
        if (squares == null) squares = gameObject.AddComponent<SquareWinnerTransitionUI>();
        if (result == null) result = gameObject.AddComponent<RoundWinnerResultUI>();
        IsPresenting = true;
        squares.enabled = true;
        finished = onFinished;
        finishedAt = -1;
        result.Hide();
        squares.Play(winner, () => result.Show(winner, redWins, blueWins, winsNeeded, style));
    }

    private void Update()
    {
        if (finished == null || result == null || !result.IsFinished)
        {
            return;
        }
        if (finishedAt < 0) finishedAt = Time.unscaledTimeAsDouble;
        if (Time.unscaledTimeAsDouble < finishedAt + 0.7) return;
        Action callback = finished;
        finished = null;
        callback();
    }

    public void HideResult()
    {
        finished = null;
        IsPresenting = false;
        if (squares != null) squares.enabled = false;
        if (result != null) result.Hide();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        HideResult();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) HideResult();
    }
}
