using Unity.Netcode;

public partial class SpaceJunkSession
{
    private readonly NetworkList<SpaceJunkPlayerSlot> testReadyPlayers = new NetworkList<SpaceJunkPlayerSlot>();
    private readonly NetworkVariable<double> testBoardingTime = new NetworkVariable<double>(0);

    public double TestBoardingTime => testBoardingTime.Value;

    public bool IsTestReady(ulong key)
    {
        return testReadyPlayers.Contains(new SpaceJunkPlayerSlot { ClientId = key, Team = TeamOf(key) });
    }

    public void RequestTestReady(ulong playerKey)
    {
        if (IsSpawned && LobbyTestDeparture.Current != null)
        {
            SetTestReadyRpc(playerKey);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetTestReadyRpc(ulong playerKey, RpcParams rpcParams = default)
    {
        LobbyTestDeparture departure = LobbyTestDeparture.Current;
        if (departure == null || State != SpaceJunkMatchState.Lobby || TestBoardingTime > 0 ||
            LocalMultiplayer.ClientOf(playerKey) != rpcParams.Receive.SenderClientId ||
            !playersInTeamZones.TryGetValue(playerKey, out int team) || team != TeamOf(playerKey))
        {
            return;
        }

        int index = TeamSlotIndex(playerKey);
        if (index >= 0 && !testReadyPlayers.Contains(slots[index]))
        {
            testReadyPlayers.Add(slots[index]);
        }
    }

    public bool TryGetTestBoardingSeat(ulong key, out int team, out int seat)
    {
        for (int i = 0; i < testReadyPlayers.Count; i++)
        {
            SpaceJunkPlayerSlot player = testReadyPlayers[i];
            if (player.ClientId != key)
            {
                continue;
            }

            team = player.Team;
            seat = 0;
            for (int j = 0; j < i; j++)
            {
                if (testReadyPlayers[j].Team == team)
                {
                    seat++;
                }
            }
            return true;
        }

        team = 0;
        seat = 0;
        return false;
    }

    private void UpdateLobbyTestDeparture(LobbyTestDeparture departure)
    {
        if (State != SpaceJunkMatchState.Lobby)
        {
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        if (TestBoardingTime > 0)
        {
            if (now >= TestBoardingTime + departure.Duration)
            {
                ServerStartMatch();
            }
            return;
        }

        for (int i = testReadyPlayers.Count - 1; i >= 0; i--)
        {
            SpaceJunkPlayerSlot ready = testReadyPlayers[i];
            int index = TeamSlotIndex(ready.ClientId);
            if (index < 0 || !ready.Equals(slots[index]))
            {
                testReadyPlayers.RemoveAt(i);
            }
        }

        if (testReadyPlayers.Count == 0)
        {
            return;
        }

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (FindPlayerByKey(slot.ClientId) == null ||
                (!IsBotKey(slot.ClientId) && !testReadyPlayers.Contains(slot)))
            {
                return;
            }
        }

        // テストシーンを直接再生した場合も行き先を用意する。
        if (!HasPlayableMapChoice && SpaceJunkLobbyUI.IsSceneInBuildList(departure.DefaultStage))
        {
            ServerSetFixedMap(departure.DefaultStage, remember: false);
        }
        if (!HasPlayableMapChoice)
        {
            return;
        }

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (IsBotKey(slot.ClientId))
            {
                testReadyPlayers.Add(slot);
            }
        }

        SpaceJunkLobbyTerminal.Current?.Close();
        testBoardingTime.Value = now + 0.35;
    }
}
