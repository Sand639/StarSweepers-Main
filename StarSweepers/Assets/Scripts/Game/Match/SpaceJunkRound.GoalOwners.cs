using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「ゴールの持ち主と、操作の止め方」（SpaceJunkRound.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // ゴールの持ち主と、操作の止め方
    // ------------------------------------------------------------

    /// <summary>
    /// **そのゴールが、どのチームのものか。**
    ///
    /// 全員が同じ計算（チーム数 → 割り当て）をするので、通信で送る必要はない。
    /// ゴールの側からも呼べるようにしてある（**生まれる順番がどちらでも取りこぼさないため**）。
    /// </summary>
    public int OwnerTeamOfGoal(int goalIndex)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

        // **このマップにあるゴールも見て決める**（東西だけのマップなら、2チームは東西を使う。2026/9/30）
        int[] owners = SpaceJunkTeams.GoalOwners(teamCount, GoalMaskOfThisMap());

        return owners[Mathf.Clamp(goalIndex, 0, owners.Length - 1)];
    }

    /// <summary>
    /// **このマップにあるゴールの番号**（北=1 / 東=2 / 南=4 / 西=8 を足したもの）。
    /// シーンの切り替え途中に前のマップのゴールが残っていることがあるので、**このシーンのゴールだけ**数える。
    /// </summary>
    private int GoalMaskOfThisMap()
    {
        int mask = 0;

        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null && goal.gameObject.scene == gameObject.scene &&
                goal.GoalIndex >= 0 && goal.GoalIndex < SpaceJunkTeams.GoalCount)
            {
                mask |= 1 << goal.GoalIndex;
            }
        }

        return mask;
    }

    /// <summary>シーンにあるゴールへ、どのチームのものかをまとめて配る。</summary>
    private void ApplyGoalOwners()
    {
        foreach (SpaceJunkGoal goal in SpaceJunkGoal.All)
        {
            if (goal != null)
            {
                goal.ApplyOwnerTeam(OwnerTeamOfGoal(goal.GoalIndex));
            }
        }
    }

    /// <summary>
    /// **このPCで操作しているプレイヤーの、移動とフックを止める／戻す。**
    ///
    /// 釣りでは <c>FishingMatch.PlayAllowed</c> がこの役目をしているが、
    /// 宇宙ごみのシーンには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）。
    /// **釣り側のファイルを一切変えずに済ませるために、ここで直接止めている。**
    /// </summary>
    private void SetLocalPlayerControlEnabled(bool enabledState)
    {
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsOwner)
            {
                continue;
            }

            FishingPlayerController move = player.GetComponent<FishingPlayerController>();
            if (move != null)
            {
                move.enabled = enabledState;
            }

            HookController hook = player.GetComponent<HookController>();
            if (hook != null)
            {
                hook.enabled = enabledState;
            }

            // **オートエイムも一緒に戻す。** ロビーの設定端末は、開いている間これを止める。
            // ホストは設定を開いたまま「ゲーム開始」を押すので、ここで戻さないと
            // **ホストだけマップでオートエイムが効かない**ままになる
            HookAimAssist aimAssist = player.GetComponent<HookAimAssist>();
            if (aimAssist != null)
            {
                aimAssist.enabled = enabledState;
            }
        }
    }
}
