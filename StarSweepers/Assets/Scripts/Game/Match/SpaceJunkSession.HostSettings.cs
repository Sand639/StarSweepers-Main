using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「ホストが設定を変える（チーム・ルール・マップ）」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    // ------------------------------------------------------------
    // ホストが設定を変える（**ホストのPCからだけ呼ぶこと**）
    // ------------------------------------------------------------

    /// <summary>チーム数を変える。**減らしたときは、はみ出した人を拾い直す。**</summary>
    public void ServerSetTeamCount(int value)
    {
        if (!IsServer)
        {
            return;
        }

        teamCount.Value = Mathf.Clamp(value, 1, SpaceJunkTeams.MaxTeams);

        for (int i = 0; i < slots.Count; i++)
        {
            if (!SpaceJunkTeams.IsValidTeam(slots[i].Team, teamCount.Value))
            {
                slots[i] = new SpaceJunkPlayerSlot { ClientId = slots[i].ClientId, Team = SmallestTeam() };
            }
        }
    }

    /// <summary>その人を、指定したチームへ移す。</summary>
    public void ServerSetTeamOf(ulong clientId, int team)
    {
        if (!IsServer || !SpaceJunkTeams.IsValidTeam(team, TeamCount))
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].ClientId == clientId)
            {
                if (slots[i].Team == team)
                {
                    return;
                }

                // 名前は席ではなく Player の NetworkVariable が持つ（3段ランダム名）ので、席には入れない
                slots[i] = new SpaceJunkPlayerSlot { ClientId = clientId, Team = team };
                OnPlayerTeamChanged?.Invoke(clientId, team);
                return;
            }
        }
    }

    /// <summary>いまのチーム数で、全員をランダムに割り振り直す。</summary>
    public void ServerRandomAssign()
    {
        if (!IsServer)
        {
            return;
        }

        int[] assigned = SpaceJunkTeams.RandomAssign(slots.Count, TeamCount);

        for (int i = 0; i < slots.Count; i++)
        {
            slots[i] = new SpaceJunkPlayerSlot { ClientId = slots[i].ClientId, Team = assigned[i] };
        }
    }

    /// <summary>何本先取として、ホストが入れられる一番大きい数。</summary>
    private const int MaxRoundsToWin = 5;

    /// <summary>何本先取かを変える。範囲の外なら端にそろえる。</summary>
    public void ServerSetRoundsToWin(int value)
    {
        if (IsServer)
        {
            roundsToWin.Value = Mathf.Clamp(value, 1, MaxRoundsToWin);
        }
    }

    /// <summary>1ラウンドの最大時間として、ホストが入れられる一番短い秒数。</summary>
    public const int MinRoundSeconds = 10;

    /// <summary>1ラウンドの最大時間として、ホストが入れられる一番長い秒数。</summary>
    public const int MaxRoundSeconds = 300;

    /// <summary>1ラウンドの最大時間を変える。範囲の外なら端にそろえる。</summary>
    public void ServerSetRoundSeconds(float value)
    {
        if (IsServer)
        {
            roundSeconds.Value = Mathf.Clamp(value, MinRoundSeconds, MaxRoundSeconds);
        }
    }

    /// <summary>
    /// 使うマップを、選ぶ／選ばないで切り替える。
    /// <paramref name="remember"/> が true なら、次にホストをしたときのために保存する
    /// （チーム数に合わないマップを自動で外すときは false。チーム数を戻したら、またチェックが付いた状態で始められるように）。
    /// </summary>
    public void ServerToggleMap(string sceneName, bool remember = true)
    {
        if (!IsServer || string.IsNullOrEmpty(sceneName))
        {
            return;
        }

        FixedString64Bytes value = new FixedString64Bytes(sceneName);
        bool removed = false;

        for (int i = 0; i < selectedMaps.Count; i++)
        {
            if (selectedMaps[i].Equals(value))
            {
                selectedMaps.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (!removed)
        {
            selectedMaps.Add(value);
        }

        if (remember)
        {
            SaveMapChoice();
        }
    }

    /// <summary>
    /// 「このマップで遊ぶ」を決める。**空を渡すと「ランダム」**（チェックの付いたマップから、ラウンドごとに選ぶ）。
    /// <paramref name="remember"/> は <see cref="ServerToggleMap"/> と同じ。
    /// </summary>
    public void ServerSetFixedMap(string sceneName, bool remember = true)
    {
        if (!IsServer)
        {
            return;
        }

        fixedMap.Value = new FixedString64Bytes(sceneName ?? string.Empty);

        if (remember)
        {
            SaveMapChoice();
        }
    }
}
