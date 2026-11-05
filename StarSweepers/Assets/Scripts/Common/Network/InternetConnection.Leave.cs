using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>InternetConnection の「部屋を出る」（InternetConnection.cs から分けた部分）。</summary>
public partial class InternetConnection
{
    /// <summary>部屋から出る。</summary>
    public async void LeaveGame()
    {
        await LeaveGameAsync();
    }

    /// <summary>
    /// **部屋から出て、抜け終わるまで待つ。** 抜けたあとで通信も止める。
    /// 先に通信を止めると抜ける処理が失敗し、中継サーバーに「まだ参加している」記録が残って、
    /// 次に同じ合言葉で入れなくなる（SessionConflict。2026/10/6）。
    /// </summary>
    public async Task LeaveGameAsync()
    {
        try
        {
            if (session != null)
            {
                await session.LeaveAsync();
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[NET] 退出でエラー：{error.Message}");
        }
        finally
        {
            session = null;
            JoinCode = string.Empty;
            CurrentPassword = string.Empty;
            State = Phase.Idle;
            Message = string.Empty;

            // 念のため、通信も止めておく
            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
    }

    /// <summary>
    /// **抜け損ねて残っている部屋があれば、抜ける。** 部屋を作る・入る直前に呼ぶ。
    ///
    /// 前の部屋から正しく抜けられなかったとき（通信を先に止めた、ゲームが固まった など）、
    /// 中継サーバーには「まだ参加している」記録が残り、同じ合言葉で入ろうとすると
    /// SessionConflict（すでに参加しています）で断られる。ここで先に抜けておく。
    /// </summary>
    private async Task LeaveStaleSessionsAsync()
    {
        if (MultiplayerService.Instance == null || MultiplayerService.Instance.Sessions == null)
        {
            return;
        }

        // 抜けると一覧から消えるので、先に写しておく
        List<ISession> stale = new List<ISession>(MultiplayerService.Instance.Sessions.Values);

        foreach (ISession leftover in stale)
        {
            try
            {
                Debug.Log($"[NET] 抜け損ねていた部屋から抜けます（合言葉：{leftover.Code}）。");
                await leftover.LeaveAsync();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[NET] 残っていた部屋から抜けられませんでした：{error.Message}");
            }
        }

        session = null;
    }
}
