using System.Collections.Generic;
using System.Linq;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>タイトル画面の「クイックマッチ」（入れる公開の部屋に人の多い順に入り、無ければ部屋を作る）。</summary>
public class TitleQuickPlay
{
    private const int WaitStepMilliseconds = 100;

    /// <summary>このクイックマッチを持つ画面（await のあとで、画面が消えていないかを確かめる）。</summary>
    private readonly MonoBehaviour owner;
    private readonly TitleRoomList roomList;

    /// <summary>クイックマッチの途中か（二度押しさせない）。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>次の部屋を試すまでの間に出す文（その間に表示が消えてちらつかないため）。それ以外は null。</summary>
    public string Notice { get; private set; }

    public TitleQuickPlay(MonoBehaviour owner, TitleRoomList roomList)
    {
        this.owner = owner;
        this.roomList = roomList;
    }

    /// <summary>クイックマッチを始める。</summary>
    public async void Begin()
    {
        if (IsRunning)
        {
            return;
        }

        InternetConnection internet = TitleConnection.Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        IsRunning = true;
        try
        {
            await RunAsync(internet);
        }
        finally
        {
            IsRunning = false;
            Notice = null;
        }
    }

    /// <summary>すぐ入れる部屋を、人の多い順に並べる（同じ人数なら一覧の順）。</summary>
    public static List<ISessionInfo> OpenSessionsFullestFirst(List<ISessionInfo> sessions)
    {
        List<ISessionInfo> open = new List<ISessionInfo>();
        foreach (ISessionInfo info in sessions)
        {
            if (info.AvailableSlots > 0 && !info.IsLocked && !info.HasPassword)
            {
                open.Add(info);
            }
        }

        // OrderByDescending は同じ人数の並びを変えない
        return open.OrderByDescending(info => TitleConnection.PlayersIn(info)).ToList();
    }

    private async System.Threading.Tasks.Task RunAsync(InternetConnection internet)
    {
        // 一覧を取っている途中なら終わるまで待つ（インターネットの係は1つのことしかできない）
        while (roomList.Searching && owner != null)
        {
            await System.Threading.Tasks.Task.Delay(WaitStepMilliseconds);
        }

        if (owner == null || internet.IsBusy)
        {
            return;
        }

        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        if (owner == null || sessions == null)
        {
            return;
        }

        foreach (ISessionInfo info in OpenSessionsFullestFirst(sessions))
        {
            Debug.Log($"[UI] クイックプレイ：公開のサーバー「{info.Name}」（{TitleConnection.PlayersIn(info)}/{info.MaxPlayers}）に入ります。");
            if (await internet.JoinGameByIdAsync(info.Id, string.Empty, true) || owner == null)
            {
                return;
            }

            // つなぐ準備から失敗した（ほかの部屋でも同じなので、やめる）
            if (internet.State == InternetConnection.Phase.Failed)
            {
                return;
            }

            Notice = "部屋に入れなかったので、ほかの部屋を探しています…";
        }

        string serverName = TitleConnection.DefaultServerName();
        Debug.Log($"[UI] クイックプレイ：入れる公開のサーバーが無いので、「{serverName}」を作ります。");
        internet.HostGame(serverName, SpaceJunkTeams.MaxPlayers, false, string.Empty);
    }
}
