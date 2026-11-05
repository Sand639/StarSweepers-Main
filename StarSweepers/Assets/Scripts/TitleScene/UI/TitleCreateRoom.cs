using Unity.Netcode;
using UnityEngine;

/// <summary>タイトル画面の「部屋を作る」（人数・公開のしかたを選んで部屋を作る）。</summary>
public class TitleCreateRoom
{
    private const int MinPlayers = 2;

    private readonly TitleParts parts;
    private readonly TitleInputFields inputs;

    // LAN（lan）／パブリック（どちらも false）／プライベート（privateRoom だけ true）
    private int maxPlayers = SpaceJunkTeams.MaxPlayers;
    private bool lan;
    private bool privateRoom;

    public TitleCreateRoom(TitleParts parts, TitleInputFields inputs)
    {
        this.parts = parts;
        this.inputs = inputs;
    }

    /// <summary>画面を、今の選び方（人数・公開のしかた）で出し直す。</summary>
    public void Refresh()
    {
        parts.SetText(TitlePartRole.MaxPlayersText, $"{maxPlayers} 人");

        bool publicChecked = !lan && !privateRoom;
        bool privateChecked = !lan && privateRoom;
        parts.SetText(TitlePartRole.PublicCheckText, TitleParts.CheckMark(publicChecked));
        parts.SetText(TitlePartRole.PrivateCheckText, TitleParts.CheckMark(privateChecked));
        parts.SetText(TitlePartRole.LanCheckText, TitleParts.CheckMark(lan));
        parts.SetSelectedLook(TitleButtonAction.SetPublic, publicChecked);
        parts.SetSelectedLook(TitleButtonAction.SetPrivate, privateChecked);
        parts.SetSelectedLook(TitleButtonAction.SetLan, lan);

        // パスワードはインターネットの部屋だけ
        parts.SetActive(TitlePartRole.PasswordRow, !lan);

        RefreshCodeBox();
    }

    public void ChangeMaxPlayers(int delta)
    {
        maxPlayers = Mathf.Clamp(maxPlayers + delta, MinPlayers, SpaceJunkTeams.MaxPlayers);
        Refresh();
    }

    public void SetLan()
    {
        lan = true;
        Refresh();
    }

    /// <summary>パブリック（false）かプライベート（true）を選ぶ。</summary>
    public void SetPrivate(bool value)
    {
        lan = false;
        privateRoom = value;
        Refresh();
    }

    /// <summary>「サーバーを作成」。LAN ならホストになって部屋を知らせ、インターネットなら部屋を作ってもらう。</summary>
    public void Create()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(inputs.ServerName) ? TitleConnection.DefaultServerName() : inputs.ServerName;

        if (lan)
        {
            TitleConnection.StartLanHost(manager);
            if (manager.IsServer)
            {
                LanRoomDiscovery.StartAdvertising(serverName, maxPlayers);
            }
            return;
        }

        InternetConnection internet = TitleConnection.Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        internet.HostGame(serverName, maxPlayers, privateRoom, inputs.CreatePassword.Trim());
    }

    /// <summary>画面の下の枠に、参加する人へ伝えることを出す。</summary>
    private void RefreshCodeBox()
    {
        if (lan)
        {
            parts.SetText(TitlePartRole.CodeBoxTitle, "このPCのIPアドレス");
            parts.SetText(TitlePartRole.CodeBoxBody, $"{TitleConnection.LocalAddresses()}\n参加する人の「部屋を探す」の一覧の一番上に自動で出る");
        }
        else
        {
            parts.SetText(TitlePartRole.CodeBoxTitle, "参加コード");
            parts.SetText(TitlePartRole.CodeBoxBody,
                "サーバーを作成すると発行されます（ロビーの左上に出ます）\n参加する人は「部屋を探す」の「番号入力で検索する」に入れる");
        }
    }
}
