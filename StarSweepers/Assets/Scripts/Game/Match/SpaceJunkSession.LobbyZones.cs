using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「ロビーのチームエリアと開始のカウントダウン」（SpaceJunkSession.cs から分けた部分）。</summary>
public partial class SpaceJunkSession
{
    /// <summary>ローカルプレイヤーがチームエリアに入った／出たことをホストへ伝える。</summary>
    public void RequestLobbyZonePresence(ulong playerKey, int zoneTeam, bool inside)
    {
        if (!IsSpawned || State != SpaceJunkMatchState.Lobby)
        {
            return;
        }

        if (IsServer)
        {
            ServerSetLobbyZonePresence(playerKey, zoneTeam, inside);
        }
        else
        {
            LobbyZonePresenceServerRpc(playerKey, zoneTeam, inside);
        }
    }

    /// <summary>1秒間エリアにいたプレイヤーのチーム変更をホストへ依頼する。</summary>
    public void RequestLobbyZoneTeam(ulong playerKey, int team)
    {
        if (!IsSpawned || State != SpaceJunkMatchState.Lobby)
        {
            return;
        }

        if (IsServer)
        {
            ServerApplyLobbyZoneTeam(playerKey, team, NetworkManager.ServerClientId);
        }
        else
        {
            LobbyZoneTeamServerRpc(playerKey, team);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void LobbyZonePresenceServerRpc(ulong playerKey, int zoneTeam, bool inside, ServerRpcParams rpcParams = default)
    {
        if (LocalMultiplayer.ClientOf(playerKey) != rpcParams.Receive.SenderClientId)
        {
            return;
        }

        ServerSetLobbyZonePresence(playerKey, zoneTeam, inside);
    }

    [ServerRpc(RequireOwnership = false)]
    private void LobbyZoneTeamServerRpc(ulong playerKey, int team, ServerRpcParams rpcParams = default)
    {
        ServerApplyLobbyZoneTeam(playerKey, team, rpcParams.Receive.SenderClientId);
    }

    private void ServerSetLobbyZonePresence(ulong playerKey, int zoneTeam, bool inside)
    {
        if (!IsServer || State != SpaceJunkMatchState.Lobby || !HasSlot(playerKey) ||
            !SpaceJunkTeams.IsValidTeam(zoneTeam, TeamCount))
        {
            return;
        }

        if (inside)
        {
            playersInTeamZones[playerKey] = zoneTeam;
            if (playerKey == NetworkManager.ServerClientId && !lobbyCountdownActive)
            {
                BeginLobbyStartCountdown(10f, true);
            }

            if (AllPlayersInTeamZones())
            {
                if (!lobbyCountdownActive || lobbyCountdownWasHostTriggered)
                {
                    BeginLobbyStartCountdown(3f, false);
                }
            }
        }
        else if (playersInTeamZones.TryGetValue(playerKey, out int currentZone) && currentZone == zoneTeam)
        {
            playersInTeamZones.Remove(playerKey);
            CancelLobbyStartCountdown();
        }
    }

    private void ServerApplyLobbyZoneTeam(ulong playerKey, int team, ulong senderClientId)
    {
        if (!IsServer || State != SpaceJunkMatchState.Lobby || !HasSlot(playerKey) ||
            LocalMultiplayer.ClientOf(playerKey) != senderClientId ||
            !playersInTeamZones.TryGetValue(playerKey, out int currentTeam) || currentTeam != team)
        {
            return;
        }

        ServerSetTeamOf(playerKey, team);
    }

    private bool AllPlayersInTeamZones()
    {
        if (slots.Count == 0)
        {
            return false;
        }

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (!playersInTeamZones.ContainsKey(slot.ClientId))
            {
                return false;
            }
        }

        return true;
    }

    private void BeginLobbyStartCountdown(float seconds, bool hostTriggered)
    {
        lobbyCountdownActive = true;
        lobbyCountdownWasHostTriggered = hostTriggered;
        lobbyCountdownEndsAt = Time.unscaledTimeAsDouble + seconds;
    }

    private void CancelLobbyStartCountdown()
    {
        lobbyCountdownActive = false;
        lobbyCountdownWasHostTriggered = false;
        lobbyCountdownEndsAt = 0d;
    }

    private void UpdateLobbyStartCountdown()
    {
        if (!lobbyCountdownActive || State != SpaceJunkMatchState.Lobby || Time.unscaledTimeAsDouble < lobbyCountdownEndsAt)
        {
            return;
        }

        CancelLobbyStartCountdown();
        ServerStartMatch();
    }
}
