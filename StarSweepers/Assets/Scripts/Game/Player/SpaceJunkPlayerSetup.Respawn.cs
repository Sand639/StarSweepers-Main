using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>SpaceJunkPlayerSetup の「復活時間」（SpaceJunkPlayerSetup.cs から分けた部分）。</summary>
public partial class SpaceJunkPlayerSetup
{
    // ------------------------------------------------------------
    // 復活時間（ラウンド中に戻ったとき、しばらく動けない）
    // ------------------------------------------------------------

    /// <summary>
    /// **ラウンド中なら、復活時間のあいだ動けなくする。**（2026/9/22・大槻さん）
    /// R キーで戻るのを「逃げ」に使えないようにするため。ロビーでは止めない。
    /// </summary>
    private void StartRespawnLock()
    {
        if (SpaceJunkRound.Current == null || respawnLockSeconds <= 0f)
        {
            return;
        }

        respawnRemaining = respawnLockSeconds;
        if (IsFirstLocalPlayer)
        {
            LocalRespawnRemaining = respawnRemaining;
        }
        SetControlEnabled(false);
    }

    /// <summary>復活待ちの残りを減らし、0 になったら動けるように戻す（ラウンドが止まっていなければ）。</summary>
    private void TickRespawnLock()
    {
        if (respawnRemaining <= 0f)
        {
            return;
        }

        respawnRemaining -= Time.deltaTime;
        if (IsFirstLocalPlayer)
        {
            LocalRespawnRemaining = Mathf.Max(0f, respawnRemaining);
        }

        if (respawnRemaining <= 0f)
        {
            respawnRemaining = 0f;

            // **ラウンドが終わって結果を出している最中なら、戻さない**（ラウンドの係が止めているため）
            SetControlEnabled(SpaceJunkRound.PlayAllowed);
        }
    }

    /// <summary>移動とフックを止める／戻す。</summary>
    private void SetControlEnabled(bool enabledState)
    {
        if (mover != null)
        {
            mover.enabled = enabledState;
        }

        if (hookController != null)
        {
            hookController.enabled = enabledState;
        }
    }
}
