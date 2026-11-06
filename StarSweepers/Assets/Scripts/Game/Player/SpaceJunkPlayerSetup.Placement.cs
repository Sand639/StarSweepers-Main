using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>SpaceJunkPlayerSetup の「シーンが切り替わったときの置き直し」（SpaceJunkPlayerSetup.cs から分けた部分）。</summary>
public partial class SpaceJunkPlayerSetup
{
    // ------------------------------------------------------------
    // シーンが切り替わったときの置き直し（2026/10/6 に作り直した）
    // ------------------------------------------------------------
    //
    // 前は「シーンを読み込んでから2フレーム待って置き直す」決め打ちだった。
    // 通信が遅いと、2フレームの時点ではまだラウンドの係や自分のチーム・ゴールの持ち主が届いておらず、
    // **ゴールが見つからないとき用の場所（中心のまわり）に置かれ、床の無いマップでは落ちていた。**
    //
    // 今は、**届くまでその場で止めて待つ**（最大 MaxPlacementWaitSeconds 秒）。
    // 届かなければ**床がある所**を探して置き、あとからゴールが届いたら置き直す。

    /// <summary>置き直しを待っているか（待っている間は、動けず落ちない）。</summary>
    private bool placementPending;

    /// <summary>待ち始めてからの秒数とフレーム数。</summary>
    private float placementWaitTime;
    private int placementFramesWaited;

    /// <summary>ゴールが見つからず、とりあえず床のある所に置いたか（あとでゴールが届いたら置き直す）。</summary>
    private bool placedByFallback;
    private float fallbackRetryTimer;

    /// <summary>最低でも待つフレーム数（読み込んだ直後は、前のシーンの物が残っているため）。</summary>
    private const int MinPlacementFrames = 2;

    /// <summary>マップで、自分のゴールが届くのを待つ最大の秒数。</summary>
    private const float MaxPlacementWaitSeconds = 4f;

    /// <summary>とりあえず置いたあと、ゴールが届いたら置き直す期間（秒）。</summary>
    private const float FallbackRetrySeconds = 6f;

    /// <summary>置き直しを待っているか（待っている間は動けない）。</summary>
    public bool IsWaitingForPlacement => placementPending;

    /// <summary>
    /// **いま、このプレイヤーは動けるべきか。** 答え合わせ（<see cref="SpaceJunkLocalStateCheck"/>）が、
    /// 実際の状態と比べるのに使う。
    /// </summary>
    public bool ControlShouldBeEnabled
    {
        get
        {
            if (placementPending || respawnRemaining > 0f)
            {
                return false;
            }

            if (SpaceJunkRound.Current != null)
            {
                return SpaceJunkRound.Current.IsPlaying; // 結果発表中は止まる
            }

            // ロビー：ホストが設定端末を開いている間は止まる
            SpaceJunkLobbyTerminal terminal = FindFirstObjectByType<SpaceJunkLobbyTerminal>();
            return terminal == null || !terminal.IsOpen;
        }
    }

    /// <summary>置き直しを待ち始める。待っている間は動けない（落ちない）。</summary>
    private void BeginPlacement()
    {
        placementPending = true;
        placementWaitTime = 0f;
        placementFramesWaited = 0;
        SetControlEnabled(false);
    }

    /// <summary>
    /// **置き直しの準備ができたら置く。**
    /// ・ロビー … 少し待ってから、ロビー用の場所へ
    /// ・マップ … ラウンドの係と、**自分のチームのゴール**が届くまで待ってから、ゴールの近くへ。
    ///   待ちきれなければ、床のある所へ置き、あとでゴールが届いたら置き直す
    /// </summary>
    private void TickPlacement()
    {
        if (placementPending)
        {
            placementFramesWaited++;
            placementWaitTime += Time.unscaledDeltaTime;

            // ラウンドの係が操作を戻しても、置き終わるまでは止めておく（落ちないように）
            SetControlEnabled(false);

            if (placementFramesWaited < MinPlacementFrames)
            {
                return;
            }

            if (!IsInLobbyScene())
            {
                bool ready = SpaceJunkRound.Current != null &&
                             SpaceJunkRound.Current.gameObject.scene ==
                             UnityEngine.SceneManagement.SceneManager.GetActiveScene() &&
                             TryFindOwnGoal(out _);

                if (!ready && placementWaitTime < MaxPlacementWaitSeconds)
                {
                    return;
                }

                if (!ready)
                {
                    Debug.LogWarning($"[NET][答え合わせ] 自分のゴールが {MaxPlacementWaitSeconds:0} 秒待っても届かないので、" +
                                     "とりあえず床のある所に置きます（届いたら置き直します）。");
                }
            }

            placementPending = false;
            MoveToSpawnPoint();
            FollowWithCamera();
            SetControlEnabled(ControlShouldBeEnabled);
            return;
        }

        // とりあえず置いたあとに、自分のゴールが届いたら置き直す
        if (placedByFallback && SpaceJunkRound.Current != null)
        {
            fallbackRetryTimer += Time.unscaledDeltaTime;
            if (fallbackRetryTimer > FallbackRetrySeconds)
            {
                placedByFallback = false;
                return;
            }

            if (TryFindOwnGoal(out _))
            {
                Debug.LogWarning("[NET][答え合わせ] 自分のゴールが遅れて届いたので、ゴールの近くへ置き直しました。");
                MoveToSpawnPoint();
                FollowWithCamera();
            }
        }
    }

    /// <summary>いまのシーンがロビーか。</summary>
    private static bool IsInLobbyScene()
    {
        string lobby = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.LobbySceneName : "SpaceJunkLobby";
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == lobby;
    }

    /// <summary>
    /// **このシーンにある、自分のチームのゴール**を探す。自分のチームがまだ届いていなければ false。
    /// （シーンの入れ替わりの途中に残っている「前のマップのゴール」はつかまない）
    /// 自分のゴールが複数あるとき（1チーム）は番号のいちばん小さいもの（どのPCでも同じものを選ぶため）。
    /// </summary>
    private bool TryFindOwnGoal(out SpaceJunkGoal found)
    {
        found = null;

        if (SpaceJunkSession.Current == null || netPlayer == null)
        {
            return false;
        }

        found = SpaceJunkGoal.FirstOwnedBy(MyTeam(), UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return found != null;
    }
}
