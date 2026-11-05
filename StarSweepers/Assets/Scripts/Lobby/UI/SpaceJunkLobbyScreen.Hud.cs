using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>SpaceJunkLobbyScreen の「ふだんの表示（左上）」（SpaceJunkLobbyScreen.cs から分けた部分）。</summary>
public partial class SpaceJunkLobbyScreen
{
    // ------------------------------------------------------------
    // ふだん（左上）
    // ------------------------------------------------------------

    /// <summary>
    /// 左上の案内を書き直す（全員のPC）。次のマップ・人数・参加者・いまの設定・操作の案内・参加コードの順。
    /// 試合の係（<see cref="SpaceJunkSession"/>）がまだ届いていなければ、その旨だけ出す。
    /// </summary>
    private void RefreshHud(NetworkManager manager, SpaceJunkSession session, SpaceJunkLobbyTerminal terminal)
    {
        SpaceJunkLobbyUI lobby = SpaceJunkLobbyUI.Current;

        if (session == null)
        {
            SetText(SpaceJunkLobbyPartRole.HudRoster, "試合の係を待っています…");
            return;
        }

        // 次に遊ぶマップ
        if (session.IsRandomMap)
        {
            ShowPicture(SpaceJunkLobbyPartRole.HudMapImage, SpaceJunkLobbyPartRole.HudMapPlaceholder, null, "ランダム");
            SetText(SpaceJunkLobbyPartRole.HudMapName, $"ランダム（{session.SelectedMapCount} 個から）");
        }
        else
        {
            SpaceJunkMapList.Entry entry = lobby != null ? lobby.FindEntry(session.FixedMap) : null;
            ShowPicture(SpaceJunkLobbyPartRole.HudMapImage, SpaceJunkLobbyPartRole.HudMapPlaceholder,
                entry != null ? entry.thumbnail : null, "画像 未設定");
            SetText(SpaceJunkLobbyPartRole.HudMapName, lobby != null ? lobby.LabelOf(session.FixedMap) : session.FixedMap);
        }

        IReadOnlyList<SpaceJunkPlayerSlot> slots = session.Slots;
        SetText(SpaceJunkLobbyPartRole.HudPlayerCount, $"{slots.Count}/{SpaceJunkTeams.MaxPlayers}");

        // 参加者（チームの色つき）
        StringBuilder roster = new StringBuilder();
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            string hex = ColorUtility.ToHtmlStringRGB(SpaceJunkTeams.TeamColor(slot.Team));
            string mark = IsLocalPlayer(slot.ClientId) ? "▶ " : "　 ";
            roster.Append($"<color=#{hex}>{mark}{session.NameOf(slot.ClientId)}（{SpaceJunkTeams.TeamName(slot.Team)}）</color>\n");
        }
        SetText(SpaceJunkLobbyPartRole.HudRoster, roster.ToString().TrimEnd('\n'));

        SetText(SpaceJunkLobbyPartRole.HudSummary,
            $"{session.TeamCount} チーム ／ {session.RoundsToWin} 本先取 ／ 1ラウンド {Mathf.RoundToInt(session.RoundSeconds)} 秒");

        // 案内
        string hint;
        if (!manager.IsServer)
        {
            hint = "ホストが始めるのを待っています…";
        }
        else if (terminal == null)
        {
            hint = "設定端末がこのシーンにありません。";
        }
        else
        {
            hint = terminal.IsHostNearby
                ? $"［{terminal.InteractKeyName}］でゲーム設定を開く"
                : "設定端末に近づくと、ゲーム設定を開けます";
        }

        // **落ちて戻れなくなったときの案内。** 知らないと詰まるので常に出す
        if (!string.IsNullOrEmpty(SpaceJunkPlayerSetup.LocalResetKeyName))
        {
            hint += $"\n落ちたら［{SpaceJunkPlayerSetup.LocalResetKeyName}］で最初の位置に戻れます";
        }

        SetText(SpaceJunkLobbyPartRole.HudHint, hint);

        SetText(SpaceJunkLobbyPartRole.HudJoinCode, JoinInfo(manager));
    }

    /// <summary>
    /// **ほかの人を呼ぶための情報**（2026/10/6・大槻さん「参加コードをロビーの左上に出して」）。
    /// ・インターネット：参加コード（全員に出す）。ホストには、パスワードを付けていればパスワードも出す
    /// ・LAN：ホストにはこのPCのIPアドレス（参加する人は「サーバーを探す → LAN」で入れる）
    /// </summary>
    private static string JoinInfo(NetworkManager manager)
    {
        InternetConnection internet = Internet();

        if (internet != null && internet.State == InternetConnection.Phase.Connected && !string.IsNullOrEmpty(internet.JoinCode))
        {
            string info = $"参加コード：{internet.JoinCode}";
            if (manager.IsServer && internet.HasPassword)
            {
                info += $"\nパスワード：{internet.CurrentPassword}";
            }
            return info;
        }

        return manager.IsServer ? $"LAN：{TitleConnection.LocalAddresses()}" : "LAN でつないでいます";
    }
}
