using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>SpaceJunkLobbyScreen の「ゲーム設定（ルール・プレイヤー・マップの一覧）」（SpaceJunkLobbyScreen.cs から分けた部分）。</summary>
public partial class SpaceJunkLobbyScreen
{
    // ------------------------------------------------------------
    // ゲーム設定
    // ------------------------------------------------------------

    /// <summary>± のボタン：その行の値を <paramref name="amount"/> だけ動かす（範囲に収めるのは Session 側）。</summary>
    private static void Step(SpaceJunkSession session, SpaceJunkLobbyValueKind kind, int amount)
    {
        switch (kind)
        {
            case SpaceJunkLobbyValueKind.TeamCount:
                session.ServerSetTeamCount(Mathf.Clamp(session.TeamCount + amount, 1, SpaceJunkTeams.MaxTeams));
                break;
            case SpaceJunkLobbyValueKind.RoundsToWin:
                session.ServerSetRoundsToWin(session.RoundsToWin + amount);
                break;
            case SpaceJunkLobbyValueKind.RoundSeconds:
                session.ServerSetRoundSeconds(Mathf.RoundToInt(session.RoundSeconds) + amount);
                break;
        }
    }

    /// <summary>
    /// ゲーム設定の画面を、いまの設定に合わせて書き直す（開いている間、毎フレーム）。
    /// 数字の行 → 参加者の行 → マップの一覧 → 「始める」ボタン → パスワード欄 → ルールの説明、の順。
    /// </summary>
    private void RefreshSettings(SpaceJunkSession session)
    {
        foreach (SpaceJunkLobbyValueRow row in valueRows)
        {
            // 入力欄があれば、打ち込み中でないときだけ、いまの値を入れ直す
            if (row.Input != null)
            {
                string current = ValueOf(session, row.Kind).ToString();
                if (!row.Input.isFocused && row.Input.text != current)
                {
                    row.Input.SetTextWithoutNotify(current);
                }
                continue;
            }

            if (row.ValueText == null)
            {
                continue;
            }

            switch (row.Kind)
            {
                case SpaceJunkLobbyValueKind.TeamCount:
                    SetText(row.ValueText, $"{session.TeamCount} チーム");
                    break;
                case SpaceJunkLobbyValueKind.RoundsToWin:
                    SetText(row.ValueText, $"{session.RoundsToWin} 本先取");
                    break;
                case SpaceJunkLobbyValueKind.RoundSeconds:
                    SetText(row.ValueText, $"{Mathf.RoundToInt(session.RoundSeconds)}");
                    break;
            }
        }

        RefreshPlayerRows(session);
        RefreshMapRows(session);
        RefreshStart(session);
        RefreshPasswordSection();

        SetText(SpaceJunkLobbyPartRole.RuleText, session.WinRule == SpaceJunkWinRule.Score
            ? $"得点制：時間いっぱい宇宙ごみを集め、得点の高いチームがラウンドを取る（1個 {SpaceJunkRound.PointPerItem} 点）。" +
              $"{session.Events.startSeconds:0} 秒たつとイベントが起きる。{session.RoundsToWin} 本先に取ったチームの勝ち"
            : $"3種類そろえたチームが出たら、その時点でラウンドを取る。{session.RoundsToWin} 本先に取ったチームの勝ち");
    }

    /// <summary>チーム分けの行。参加者やチームが変わったときだけ作り直す。</summary>
    private void RefreshPlayerRows(SpaceJunkSession session)
    {
        GameObject content = PartObject(SpaceJunkLobbyPartRole.PlayerListContent);
        if (content == null || playerRowTemplate == null)
        {
            return;
        }

        StringBuilder signature = new StringBuilder();
        signature.Append(session.TeamCount).Append('|');
        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            signature.Append(slot.ClientId).Append(':').Append(slot.Team).Append(':').Append(session.NameOf(slot.ClientId)).Append(';');
        }

        if (signature.ToString() == playerRowsSignature)
        {
            return;
        }

        playerRowsSignature = signature.ToString();

        foreach (GameObject row in playerRows)
        {
            Destroy(row);
        }
        playerRows.Clear();

        int teamCount = session.TeamCount;

        foreach (SpaceJunkPlayerSlot slot in session.Slots)
        {
            GameObject row = Instantiate(playerRowTemplate, content.transform);
            row.name = $"Player {slot.ClientId}";
            row.SetActive(true);
            playerRows.Add(row);

            Color color = SpaceJunkTeams.TeamColor(slot.Team);
            ulong clientId = slot.ClientId;
            int team = slot.Team;

            foreach (SpaceJunkLobbyPart part in row.GetComponentsInChildren<SpaceJunkLobbyPart>(true))
            {
                Text text = part.GetComponent<Text>();
                if (text == null)
                {
                    continue;
                }

                if (part.Role == SpaceJunkLobbyPartRole.PlayerRowName)
                {
                    text.text = (IsLocalPlayer(clientId) ? "▶ " : string.Empty) + session.NameOf(clientId);
                    text.color = color;
                }
                else if (part.Role == SpaceJunkLobbyPartRole.PlayerRowTeam)
                {
                    text.text = SpaceJunkTeams.TeamName(team);
                    text.color = color;
                }
            }

            // ＜ ＞ のボタン：その人のチームを1つ前・次に動かす
            foreach (SpaceJunkLobbyButton button in row.GetComponentsInChildren<SpaceJunkLobbyButton>(true))
            {
                int delta = button.Action == SpaceJunkLobbyAction.TeamPrevious ? -1
                    : button.Action == SpaceJunkLobbyAction.TeamNext ? 1 : 0;
                if (delta == 0)
                {
                    continue;
                }

                // 見本から複製したボタンには見本のつなぎが残っているので、いったん外してから付け直す
                button.Button.onClick.RemoveAllListeners();
                button.Button.onClick.AddListener(() =>
                {
                    SpaceJunkSession current = SpaceJunkSession.Current;
                    if (current != null && current.IsServer)
                    {
                        // 端で折り返す（最初のチームの前は最後のチーム）
                        current.ServerSetTeamOf(clientId, (team + delta + teamCount) % teamCount);
                    }
                });
            }
        }
    }

    /// <summary>マップの一覧。候補が変わったときだけ作り直し、選択とチェックの見た目は毎回合わせる。</summary>
    private void RefreshMapRows(SpaceJunkSession session)
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;
        GameObject content = PartObject(SpaceJunkLobbyPartRole.MapListContent);

        if (lobby == null || content == null || mapRowTemplate == null)
        {
            SetText(SpaceJunkLobbyPartRole.MapHiddenNote, "マップの候補を読めません（NetworkManager に SpaceJunkLobbyUI がありません）。");
            return;
        }

        List<KeyValuePair<string, string>> maps = lobby.GetAvailableMaps(session.TeamCount, out int hidden);
        lobby.PruneUnplayableSelection(session, maps);

        StringBuilder signature = new StringBuilder();
        foreach (KeyValuePair<string, string> map in maps)
        {
            signature.Append(map.Key).Append('=').Append(map.Value).Append(';');
        }

        if (signature.ToString() != mapRowsSignature)
        {
            mapRowsSignature = signature.ToString();
            RebuildMapRows(content.transform, maps);
        }

        // 選ばれている行・チェックの見た目
        foreach (MapRow row in mapRows)
        {
            bool selected = !session.IsRandomMap && session.FixedMap == row.SceneName;
            bool check = session.IsMapSelected(row.SceneName);

            SetActive(row.Highlight, selected);
            SetActive(row.CheckOn, check);
            SetActive(row.CheckOff, !check);
        }

        SetActive(PartObject(SpaceJunkLobbyPartRole.MapRandomHighlight), session.IsRandomMap);
        SetText(SpaceJunkLobbyPartRole.MapRandomLabel, $"ランダム（チェック {session.SelectedMapCount} 個から）");

        string note = maps.Count == 0
            ? "ロビーに出すマップがありません。`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く` で足してください"
            : hidden > 0 ? $"{session.TeamCount} チームでは遊べないマップ（ゴールが足りない）を {hidden} 個隠しています" : string.Empty;
        SetText(SpaceJunkLobbyPartRole.MapHiddenNote, note);

        // 大きい画像：選んだマップ。ランダムなら「ランダム」
        if (session.IsRandomMap)
        {
            ShowPicture(SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder, null, "ランダム");
            SetText(SpaceJunkLobbyPartRole.MapPreviewName, "ランダム（チェックの付いたマップから、ラウンドごとに選ぶ）");
        }
        else
        {
            SpaceJunkMapList.Entry entry = lobby.FindEntry(session.FixedMap);
            ShowPicture(SpaceJunkLobbyPartRole.MapPreviewImage, SpaceJunkLobbyPartRole.MapPreviewPlaceholder,
                entry != null ? entry.thumbnail : null, "画像 未設定");
            SetText(SpaceJunkLobbyPartRole.MapPreviewName, lobby.LabelOf(session.FixedMap));
        }
    }

    /// <summary>
    /// マップの行を全部消して、候補（シーン名 → 表示名）の数だけ見本から作り直す。
    /// 行の中の部品（名前・選択の目印・チェック）を <see cref="MapRow"/> に控え、ボタンをホストへの指示につなぐ。
    /// </summary>
    private void RebuildMapRows(Transform content, List<KeyValuePair<string, string>> maps)
    {
        foreach (MapRow row in mapRows)
        {
            Destroy(row.Root);
        }
        mapRows.Clear();

        foreach (KeyValuePair<string, string> map in maps)
        {
            GameObject root = Instantiate(mapRowTemplate, content);
            root.name = $"Map {map.Key}";
            root.SetActive(true);

            MapRow row = new MapRow { SceneName = map.Key, Root = root };
            string sceneName = map.Key;

            foreach (SpaceJunkLobbyPart part in root.GetComponentsInChildren<SpaceJunkLobbyPart>(true))
            {
                switch (part.Role)
                {
                    case SpaceJunkLobbyPartRole.MapRowName:
                        Text text = part.GetComponent<Text>();
                        if (text != null)
                        {
                            text.text = map.Value;
                        }
                        break;
                    case SpaceJunkLobbyPartRole.MapRowHighlight:
                        row.Highlight = part.gameObject;
                        break;
                    case SpaceJunkLobbyPartRole.MapRowCheckOn:
                        row.CheckOn = part.gameObject;
                        break;
                    case SpaceJunkLobbyPartRole.MapRowCheckOff:
                        row.CheckOff = part.gameObject;
                        break;
                }
            }

            foreach (SpaceJunkLobbyButton button in root.GetComponentsInChildren<SpaceJunkLobbyButton>(true))
            {
                button.Button.onClick.RemoveAllListeners();

                if (button.Action == SpaceJunkLobbyAction.SelectMap)
                {
                    button.Button.onClick.AddListener(() =>
                    {
                        SpaceJunkSession current = SpaceJunkSession.Current;
                        if (current != null && current.IsServer)
                        {
                            current.ServerSetFixedMap(sceneName);
                        }
                    });
                }
                else if (button.Action == SpaceJunkLobbyAction.ToggleMapCheck)
                {
                    button.Button.onClick.AddListener(() =>
                    {
                        SpaceJunkSession current = SpaceJunkSession.Current;
                        if (current != null && current.IsServer)
                        {
                            current.ServerToggleMap(sceneName);
                        }
                    });
                }
            }

            mapRows.Add(row);
        }
    }
}
