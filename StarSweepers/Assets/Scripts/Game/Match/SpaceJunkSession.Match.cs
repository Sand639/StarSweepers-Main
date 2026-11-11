using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「ラウンドの進行（ホストだけ）」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    // ------------------------------------------------------------
    // ラウンドの進行（**ホストだけが動かす**）
    // ------------------------------------------------------------

    /// <summary>
    /// 試合を始める。勝ち数を 0 に戻し、1ラウンド目のマップへ全員で移動する。
    /// **ロビーの「ゲーム開始」から呼ばれる。**
    /// </summary>
    public void ServerStartMatch()
    {
        if (!IsServer)
        {
            return;
        }

        CancelLobbyStartCountdown();
        playersInTeamZones.Clear();

        if (!HasPlayableMapChoice)
        {
            Debug.LogError("[JUNK] マップが選ばれていない（ランダムなのにチェックの付いたマップが無い）ので、試合を始められません。");
            return;
        }

        for (int i = 0; i < roundWins.Count; i++)
        {
            roundWins[i] = 0;
        }

        currentRound.Value = 1;
        lastRoundWinner.Value = -1;
        matchWinner.Value = -1;
        state.Value = SpaceJunkMatchState.Playing;

        // 全員の画面をトランジションで覆ってから、マップへ移る
        SceneTransition.PlayForEveryone(LoadNextMap);
    }

    /// <summary>
    /// **ラウンドの決着を受け取る。** SpaceJunkRound から呼ばれる。
    ///
    /// 引き分け（<paramref name="winnerTeam"/> が -1）のときは
    /// **誰もラウンドを取らず、次のラウンドへ行く。**
    /// </summary>
    public void ServerReportRoundResult(int winnerTeam)
    {
        if (!IsServer)
        {
            return;
        }

        // **黙って捨てない。** ここで捨てると、結果の表示が出たまま次へ進まず、
        // 何が起きているのか画面からは分からなくなる
        if (state.Value != SpaceJunkMatchState.Playing)
        {
            Debug.LogError(
                $"[JUNK] 試合が始まっていない状態（{state.Value}）で、ラウンドの結果を受け取りました。\n" +
                "次のラウンドへ進めません。**ロビーの「ゲーム開始」から始めてください。**\n" +
                "係がマップへ移る途中で消えている可能性もあります" +
                "（SpaceJunkSessionSpawner の Spawn は destroyWithScene を false にすること）。");
            return;
        }

        lastRoundWinner.Value = winnerTeam;

        if (winnerTeam >= 0 && winnerTeam < roundWins.Count)
        {
            roundWins[winnerTeam] = roundWins[winnerTeam] + 1;

            Debug.Log($"[JUNK] ラウンド {currentRound.Value} は {SpaceJunkTeams.TeamName(winnerTeam)} が取りました" +
                      $"（{roundWins[winnerTeam]} / {RoundsToWin} 本）。");

            if (roundWins[winnerTeam] >= RoundsToWin)
            {
                matchWinner.Value = winnerTeam;
                state.Value = SpaceJunkMatchState.MatchOver;
                StartCoroutine(ReturnToLobbyAfterDelay());
                return;
            }
        }
        else
        {
            Debug.Log($"[JUNK] ラウンド {currentRound.Value} は引き分けでした。誰もラウンドを取らずに次へ進みます。");
        }

        StartCoroutine(NextRoundAfterDelay());
    }

    /// <summary>ラウンドの結果を見せる時間だけ待ってから、次のラウンドのマップへ移る。</summary>
    private IEnumerator NextRoundAfterDelay()
    {
        // ⚠ **WaitForSeconds ではなく WaitForSecondsRealtime を使うこと。**
        //
        // `WaitForSeconds` は `Time.timeScale` の影響を受けるので、時間が止まると
        // **ここで永久に待ち続け、次のラウンドへ進まなくなる。**
        // 残り時間のほうは同期された時計で数えていて止まらないため、
        // 「時間だけ過ぎて試合が進まない」という分かりにくい形で出る。
        //
        // ※ 2026/9/20 から `GamePause` が**つながっている間は時間を止めない**ように
        //    なったので、いまは止まらないはず。**それでも realtime のままにしておく。**
        //    ラウンドの進行を、ポーズの作りに依存させたくないため
        yield return new WaitForSecondsRealtime(roundResultSeconds);

        if (!IsServer || state.Value != SpaceJunkMatchState.Playing)
        {
            yield break;
        }

        currentRound.Value = currentRound.Value + 1;
        LoadNextMap();
    }

    /// <summary>試合の結果を見せる時間だけ待ってから、全員をロビーへ戻す。</summary>
    private IEnumerator ReturnToLobbyAfterDelay()
    {
        // 上と同じ理由で、止まらない時計で数える
        yield return new WaitForSecondsRealtime(matchResultSeconds);

        if (!IsServer)
        {
            yield break;
        }

        state.Value = SpaceJunkMatchState.Lobby;
        currentRound.Value = 0;

        LoadScene(lobbySceneName);
    }

    /// <summary>
    /// **次のマップをランダムに選んで、全員で移動する。**
    /// 選ばれているマップが2つ以上あるときは、**直前と同じマップは選ばない。**
    /// </summary>
    private void LoadNextMap()
    {
        List<string> candidates = new List<string>();
        string previous = currentMap.Value.ToString();

        // **1つに決めてあれば、毎ラウンドそのマップ**（2026/10/6）
        if (!IsRandomMap)
        {
            candidates.Add(FixedMap);
        }
        else
        {
            // ランダム：チェックの付いたマップから。2つ以上あれば、直前と同じマップは選ばない
            foreach (FixedString64Bytes map in selectedMaps)
            {
                string name = map.ToString();

                if (selectedMaps.Count > 1 && name == previous)
                {
                    continue;
                }

                candidates.Add(name);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("[JUNK] 移動できるマップがありません。");
            return;
        }

        string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        currentMap.Value = new FixedString64Bytes(chosen);

        // **読み込めなかったら、試合を打ち切ってロビーへ戻す。**
        // ここで黙って止まると、結果の表示が出たまま永久に動かなくなり、
        // 遊んでいる人には何が起きたのか分からない
        if (!LoadScene(chosen))
        {
            Debug.LogError($"[JUNK] マップ「{chosen}」へ移れませんでした。試合を打ち切ってロビーへ戻ります。");
            AbortToLobby();
        }
    }

    /// <summary>ロビーのシーンの名前。</summary>
    public string LobbySceneName => lobbySceneName;

    /// <summary>
    /// **ポーズ画面の「ロビーに戻る」をホストが押したとき。** 試合を打ち切って、全員をロビーへ戻す。
    /// ホストでなければ何もせず false（<see cref="SpaceJunkLeaveMatch"/> から呼ばれる）。
    /// </summary>
    public bool ServerReturnToLobby()
    {
        if (!IsServer)
        {
            return false;
        }

        StopAllCoroutines();
        AbortToLobby();
        return true;
    }

    /// <summary>試合を打ち切って、全員をロビーへ戻す。</summary>
    private void AbortToLobby()
    {
        state.Value = SpaceJunkMatchState.Lobby;
        currentRound.Value = 0;
        matchWinner.Value = -1;

        LoadScene(lobbySceneName);
    }

    /// <summary>全員のシーンをまとめて切り替える。切り替えを始められたら true。</summary>
    private bool LoadScene(string sceneName)
    {
        if (NetworkManager == null || NetworkManager.SceneManager == null)
        {
            Debug.LogError("[JUNK] SceneManager がまだ使えません。");
            return false;
        }

        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(
            sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError(
                $"[JUNK] シーン「{sceneName}」を読み込めませんでした（{status}）。\n" +
                "よくある原因：\n" +
                "・**File > Build Profiles のシーン一覧に、そのシーンが入っていない**\n" +
                "・NetworkManager の Enable Scene Management が OFF\n" +
                "・別のシーン切り替えがまだ終わっていない（SceneEventInProgress）");

            return false;
        }

        return true;
    }
}
