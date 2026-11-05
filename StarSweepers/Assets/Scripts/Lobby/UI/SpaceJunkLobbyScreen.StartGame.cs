using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>SpaceJunkLobbyScreen の「ゲームを始める」（SpaceJunkLobbyScreen.cs から分けた部分）。</summary>
public partial class SpaceJunkLobbyScreen
{
    // ------------------------------------------------------------
    // ゲームを始める
    // ------------------------------------------------------------

    /// <summary>
    /// 「ゲームを始める」のボタンと、その下の一言。始められない理由があれば押せなくして理由を出し、
    /// 始められるなら、誰もいないチームがあることだけ知らせる。
    /// </summary>
    private void RefreshStart(SpaceJunkSession session)
    {
        string problem = StartProblem(session);

        Button start = PartComponent<Button>(SpaceJunkLobbyPartRole.StartButton);
        if (start != null && start.interactable != (problem == null))
        {
            start.interactable = problem == null;
        }

        SetText(SpaceJunkLobbyPartRole.StartLabel, problem == null
            ? $"ゲームを始める（{session.RoundsToWin} 本先取）"
            : "ゲームを始められません");

        string note = problem;
        if (note == null)
        {
            for (int team = 0; team < session.TeamCount; team++)
            {
                if (session.PlayerCountOf(team) == 0)
                {
                    // **無人のチームのゴールは、素材を入れても数えない。** 知らないと混乱するので伝える
                    note = $"{SpaceJunkTeams.TeamName(team)}に誰もいません（このチームのゴールは数えません）";
                    break;
                }
            }
        }

        SetText(SpaceJunkLobbyPartRole.StartMessage, note ?? string.Empty);
    }

    /// <summary>始められない理由。始められるなら null。</summary>
    private static string StartProblem(SpaceJunkSession session)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && !manager.NetworkConfig.EnableSceneManagement)
        {
            return "NetworkManager の Enable Scene Management が OFF です。ONにしないとシーンを切り替えられません。";
        }

        if (!session.HasPlayableMapChoice)
        {
            return "マップを1つ選ぶか、「ランダム」にしてマップに1つ以上チェックを付けてください。";
        }

        // **一番多い原因を先に出す。** ビルドの一覧に入っていないシーンは読み込めない
        if (!session.IsRandomMap)
        {
            if (!SpaceJunkLobbyUI.IsSceneInBuildList(session.FixedMap))
            {
                return $"シーン「{session.FixedMap}」がビルドの一覧に入っていません（File > Build Profiles で入れてください）。";
            }
        }
        else
        {
            foreach (string map in session.SelectedMaps)
            {
                if (!SpaceJunkLobbyUI.IsSceneInBuildList(map))
                {
                    return $"シーン「{map}」がビルドの一覧に入っていません（File > Build Profiles で入れてください）。";
                }
            }
        }

        return null;
    }

    /// <summary>「ゲームを始める」を押したとき。設定端末を閉じてから、全員をマップへ移す（Session 側が行う）。</summary>
    private static void StartMatch(SpaceJunkSession session)
    {
        // ボタンが押せない状態でも（コントローラーの決定 など）念のため確かめる
        if (StartProblem(session) != null)
        {
            return;
        }

        if (SpaceJunkLobbyTerminal.Current != null)
        {
            SpaceJunkLobbyTerminal.Current.Close();
        }

        session.ServerStartMatch();
    }
}
