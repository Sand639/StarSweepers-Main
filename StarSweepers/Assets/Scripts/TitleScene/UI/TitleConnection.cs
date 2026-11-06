using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>タイトル画面から部屋を作る・入るときに使う、つなぐ処理のまとめ。</summary>
public static class TitleConnection
{
    /// <summary>NetworkManager に付いている InternetConnection。無ければ null。</summary>
    public static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    /// <summary>つながっているか（ホストとして動いている／参加者としてつながり終わった）。</summary>
    public static bool IsConnected()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && (manager.IsServer || manager.IsConnectedClient);
    }

    /// <summary>名前を入れなかったときのサーバーの名前。</summary>
    public static string DefaultServerName()
    {
        return string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "StarSweepers" : $"{GameSettings.PlayerName} のサーバー";
    }

    /// <summary>公開の部屋に今いる人数。</summary>
    public static int PlayersIn(ISessionInfo info)
    {
        return Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
    }

    /// <summary>プラクティス。このPCの中だけのホストになる（ロビーへは TitleSceneFlow が移す）。</summary>
    public static void StartPractice()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "127.0.0.1");
        }

        manager.StartHost();
    }

    /// <summary>LAN のホストになる（どのネットワークから来た人も受け付ける）。</summary>
    public static void StartLanHost(NetworkManager manager)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "0.0.0.0");
        }

        manager.StartHost();
    }

    /// <summary>LAN の部屋に入る。</summary>
    public static void JoinLanRoom(string address, ushort port)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            return;
        }

        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData(address, port);
        }

        manager.StartClient();
    }

    /// <summary>Steam のフレンド一覧の「ゲームに参加」で頼まれた部屋に入る。</summary>
    public static void JoinFromSteamRequest(string password)
    {
        if (string.IsNullOrEmpty(SteamFriendsService.PendingJoinCode) || IsConnected())
        {
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null || internet.IsBusy)
        {
            return;
        }

        string code = SteamFriendsService.ConsumePendingJoinCode();
        Debug.Log($"[STEAM] Steam から頼まれた部屋（{code}）に入ります。");
        internet.JoinGame(code, password.Trim());
    }

    /// <summary>このPCのIPアドレス（LAN のホストが参加する人に伝える番号）。</summary>
    public static string LocalAddresses()
    {
        List<string> found = new List<string>();

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address))
                    {
                        found.Add(address.Address.ToString());
                    }
                }
            }
        }
        catch (System.Exception error)
        {
            Debug.LogWarning($"[UI] IPアドレスを調べられませんでした：{error.Message}");
        }

        return found.Count > 0 ? string.Join(" ／ ", found) : "（見つかりませんでした）";
    }
}
