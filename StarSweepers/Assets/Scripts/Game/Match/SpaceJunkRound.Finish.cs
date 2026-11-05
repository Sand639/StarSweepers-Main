using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「決着（ホストだけ）」（SpaceJunkRound.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // 決着（**ホストだけが判定する**）
    // ------------------------------------------------------------

    /// <summary>
    /// 得点制で、最大時間に達したときの決着。**得点の高いチームがラウンドを取る。**
    /// 1位が同点なら引き分けで、誰も取らない（全チーム0点も引き分け）。
    /// </summary>
    private void FinishByScore()
    {
        FinishRound(LeadingTeamOrTie(ScoreOf));
    }

    /// <summary>
    /// 最大時間に達したときの決着。**集めた種類が多いチームがラウンドを取る。**
    /// 1位が同数なら引き分けで、誰も取らない。
    /// </summary>
    private void FinishByCount()
    {
        // 1位が同数なら引き分け。**誰も集めていない（全員0種類）ときも引き分け**
        FinishRound(LeadingTeamOrTie(KindCountOf));
    }

    /// <summary>
    /// 遊んでいるチームのうち、<paramref name="valueOf"/>（得点、または集めた種類の数）が一番大きいチーム。
    /// **1位が並んでいる、または1位の値が 0 以下なら -1（引き分け）。**
    /// 得点制と3種類ルールで同じ比べ方をするので、ここにまとめてある。
    /// </summary>
    private int LeadingTeamOrTie(System.Func<int, int> valueOf)
    {
        int bestTeam = -1;
        int bestValue = -1;
        bool tied = false;

        int teams = SpaceJunkSession.Current != null
            ? SpaceJunkSession.Current.TeamCount
            : SpaceJunkTeams.MaxTeams;

        for (int team = 0; team < teams; team++)
        {
            int value = valueOf(team);

            if (value > bestValue)
            {
                bestValue = value;
                bestTeam = team;
                tied = false;
            }
            else if (value == bestValue)
            {
                tied = true;
            }
        }

        return tied || bestValue <= 0 ? -1 : bestTeam;
    }

    /// <summary>ラウンドを終わらせて、試合の係へ結果を渡す。</summary>
    private void FinishRound(int winnerTeam)
    {
        if (phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return;
        }

        roundWinner.Value = winnerTeam;
        phase.Value = SpaceJunkRoundPhase.Result;

        Debug.Log(winnerTeam < 0
            ? "[JUNK] ラウンド終了。引き分け（誰もラウンドを取りません）。"
            : $"[JUNK] ラウンド終了。{SpaceJunkTeams.TeamName(winnerTeam)} がラウンドを取りました。");

        if (SpaceJunkSession.Current != null)
        {
            SpaceJunkSession.Current.ServerReportRoundResult(winnerTeam);
        }
        else
        {
            Debug.LogWarning("[JUNK] 試合の係（SpaceJunkSession）がいないので、次のラウンドへ進めません。");
        }
    }
}
