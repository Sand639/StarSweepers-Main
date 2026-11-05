using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>InternetConnection の「タイトル画面から使う入口（部屋を作る・一覧・番号・版・一覧から入る）」（InternetConnection.cs から分けた部分）。</summary>
public partial class InternetConnection
{
    // ------------------------------------------------------------
    // タイトル画面から使う入口（2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// **部屋の名前・最大人数・非公開かを決めて、部屋を作る。** タイトル画面の「サーバーを作成」から呼ぶ。
    /// 非公開にすると、パブリックサーバーの一覧に出ない（合言葉を知っている人だけが入れる）。
    /// </summary>
    public void HostGame(string roomName, int players, bool privateRoom, string roomPassword = "")
    {
        if (IsBusy)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(roomName))
        {
            sessionName = roomName.Trim();
        }

        maxPlayers = Mathf.Clamp(players, 2, 8);
        isPrivate = privateRoom;
        password = roomPassword ?? string.Empty;
        HostGame();
    }

    /// <summary>
    /// **公開されている部屋の一覧を取る。** タイトル画面の「パブリックサーバーを探す」から呼ぶ。
    /// 失敗したときは null（理由は <see cref="Message"/>）。
    /// </summary>
    public async Task<List<ISessionInfo>> QueryPublicSessionsAsync()
    {
        if (IsBusy)
        {
            return null;
        }

        if (!await PrepareAsync())
        {
            return null;
        }

        Message = "サーバーを探しています…";

        for (int attempt = 1; ; attempt++)
        {
            // 前の問い合わせから間が空いていなければ、空くまで待つ
            // （一覧の取り直しの直後にクイックプレイを押すと、続けて問い合わせることになり 429 で断られるため）
            float wait = lastQueryTime + QueryIntervalSeconds - Time.unscaledTime;
            if (wait > 0f)
            {
                await Task.Delay(Mathf.CeilToInt(wait * 1000f));
            }

            lastQueryTime = Time.unscaledTime;

            try
            {
                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
                State = Phase.Idle;
                Message = string.Empty;

                // 版の違う人の部屋は出さない（入ろうとしても、つないだ直後に相手から切られるため。2026/10/28）
                return results != null && results.Sessions != null
                    ? SameVersionOnly(results.Sessions, LocalVersion())
                    : new List<ISessionInfo>();
            }
            catch (SessionException error) when (error.Error == SessionError.RateLimitExceeded && attempt < QueryMaxAttempts)
            {
                // 頼みすぎ（429）で断られた。少し待ってからもう一度頼む
                Debug.LogWarning($"[NET] サーバーの一覧を頼みすぎて断られたので、{QueryRateLimitWaitSeconds:0}秒待ってもう一度探します（{attempt}回目）。");
                lastQueryTime = Time.unscaledTime + QueryRateLimitWaitSeconds - QueryIntervalSeconds;
            }
            catch (Exception error)
            {
                Fail("サーバーの一覧を取れませんでした", error);
                return null;
            }
        }
    }

    /// <summary>部屋の情報に入れておく、合言葉（部屋番号）の名前。公開の部屋の一覧には合言葉が入ってこないので、ホストがここに書く。</summary>
    private const string RoomCodePropertyKey = "code";

    /// <summary>
    /// **公開の部屋の一覧に、自分の部屋の合言葉（部屋番号）を出す。**（2026/10/22。タイトルの「部屋を探す」で番号を出し、番号で絞り込むため）
    /// 書けなくても部屋はそのまま使える（一覧に番号が出ないだけ）。
    /// </summary>
    private async Task PublishRoomCodeAsync()
    {
        try
        {
            IHostSession host = session.AsHost();
            host.SetProperty(RoomCodePropertyKey, new SessionProperty(session.Code, VisibilityPropertyOptions.Public));
            await host.SavePropertiesAsync();
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[NET] 部屋番号を一覧に出せませんでした（部屋はそのまま使えます）：{error.Message}");
        }
    }

    /// <summary>一覧の部屋の合言葉（部屋番号）。ホストが <see cref="PublishRoomCodeAsync"/> で書いていなければ空。</summary>
    public static string RoomCodeOf(ISessionInfo info)
    {
        return PropertyOf(info, RoomCodePropertyKey);
    }

    /// <summary>一覧の部屋の情報に、ホストが書いた値。書かれていなければ空。</summary>
    private static string PropertyOf(ISessionInfo info, string key)
    {
        return info != null && info.Properties != null &&
               info.Properties.TryGetValue(key, out SessionProperty property) && property != null
            ? property.Value ?? string.Empty
            : string.Empty;
    }

    /// <summary>部屋の情報に入れておく、ゲームの版（<see cref="LocalVersion"/>）の名前。ホストが部屋を作るときに書く。</summary>
    private const string VersionPropertyKey = "ver";

    /// <summary>
    /// **このゲームの版。**（2026/10/28・大槻さん「版の違う人の部屋を出さない」）
    /// Netcode が、つないできた相手を入れてよいか確かめるのに使う番号（通信の設定とネットワークのプレハブの一覧から作る）と同じもの。
    /// これが違う相手の部屋に入ろうとすると、つないだ直後に相手から切られる。
    /// </summary>
    private string LocalVersion()
    {
        return GetComponent<NetworkManager>().NetworkConfig.GetConfig(false).ToString("x16");
    }

    /// <summary>
    /// **版が <paramref name="version"/> と同じ部屋だけ**を、一覧の順のまま残す。
    /// 版の書かれていない部屋（2026/10/28 より前の版のホスト）も、版が違うので外す（テスト：<c>RoomVersionTests</c>）。
    /// </summary>
    public static List<ISessionInfo> SameVersionOnly(IEnumerable<ISessionInfo> sessions, string version)
    {
        List<ISessionInfo> same = new List<ISessionInfo>();
        foreach (ISessionInfo info in sessions)
        {
            if (PropertyOf(info, VersionPropertyKey) == version)
            {
                same.Add(info);
            }
        }
        return same;
    }

    /// <summary>サーバーの一覧を前回頼んだ時刻（Time.unscaledTime の秒）。続けて頼みすぎないために覚えておく。</summary>
    private float lastQueryTime = float.NegativeInfinity;

    /// <summary>サーバーの一覧を頼む間隔の最低秒数（これより短いと、頼みすぎ（429）で断られやすい）。</summary>
    private const float QueryIntervalSeconds = 1.5f;

    /// <summary>頼みすぎ（429）で断られたとき、もう一度頼むまで待つ秒数。</summary>
    private const float QueryRateLimitWaitSeconds = 3f;

    /// <summary>頼みすぎ（429）で断られたとき、最初の1回を含めて何回まで頼むか。</summary>
    private const int QueryMaxAttempts = 3;

    /// <summary>**一覧で選んだ部屋に入る。** タイトル画面のパブリックサーバーの一覧から呼ぶ。</summary>
    public async void JoinGameById(string sessionId, string joinPassword = "")
    {
        await JoinGameByIdAsync(sessionId, joinPassword, false);
    }

    /// <summary>
    /// **一覧の部屋に入り、入れたら true を返す。**
    /// <paramref name="quietFailure"/> を true にすると（クイックプレイ。2026/10/28）、入れなくても失敗の表示を出さず、
    /// 何もしていない状態に戻す（呼んだ側が次の部屋を試すため）。
    /// つなぐ準備の失敗（インターネットにつながっていない など）は、どの部屋でも同じなので、どちらでも失敗の表示を出す。
    /// </summary>
    public async Task<bool> JoinGameByIdAsync(string sessionId, string joinPassword, bool quietFailure)
    {
        LastJoinFailedByPassword = false;

        if (IsBusy || string.IsNullOrEmpty(sessionId))
        {
            return false;
        }

        string passwordProblem = PasswordProblem(joinPassword);
        if (passwordProblem != null)
        {
            FailBeforeStart(passwordProblem);
            return false;
        }

        if (!await PrepareAsync())
        {
            return false;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける
        await LeaveStaleSessionsAsync();

        State = Phase.Joining;
        Message = "部屋に入ろうとしています…";

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, JoinOptions(joinPassword));

            CurrentPassword = string.Empty;
            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "つながりました。";

            Debug.Log($"[NET] 一覧から部屋に入りました。合言葉：{JoinCode}");
            return true;
        }
        catch (Exception error)
        {
            if (quietFailure)
            {
                // 相手のゲームと中身（版）が違う・満員になった・閉じられた など。表示は出さずに、次を試せる状態に戻す
                State = Phase.Idle;
                Message = string.Empty;
                Debug.LogWarning($"[NET] 一覧の部屋に入れませんでした（ほかの部屋を試します）：{error.Message}");
                return false;
            }

            LastJoinFailedByPassword = LooksLikeWrongPassword(error, joinPassword);
            Fail("部屋に入れませんでした。パスワードが違うか、満員か、部屋が閉じられています", error);
            return false;
        }
    }
}
