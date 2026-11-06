using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// **インターネット越しに、離れた人とつなぐ。**
///
/// 家のPCはルーターの内側にいるため、外から直接つなぐことができない。
/// そこで **Unityの中継サーバー（Relay）を1つ挟んで、両方から外向きにつなぐ。**
///
/// つなぎ方は**合言葉（6文字くらいの英数字）**を使う。
/// ホストが合言葉を作り、それを相手に伝えると、相手はそれを入れて入ってこられる。
///
/// ## LANとの違いは「つなぐ前の準備」だけ
///
/// ゲームの中身（位置・得点・物理の同期）は**LANのときとまったく同じもの**が動く。
/// 変わるのは、どの道を通ってつながるかだけ。
///
/// | | LAN | インターネット |
/// | --- | --- | --- |
/// | つなぎ方 | 相手のIPを直接指定 | 合言葉で中継サーバー経由 |
/// | ゲームの中身 | 同じ | 同じ |
/// | 遅れ | ほぼ無し | 数十ミリ秒 |
///
/// ## 使う前に必要な準備（人の作業）
///
/// **Unity Cloud にこのプロジェクトを登録しておく必要がある。**
/// 済んでいない場合は、画面にその旨が出るようにしてある。
/// 手順は `Documents/機能ドキュメント/インターネットでの複数人プレイ.md` を読むこと。
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Common/Network/InternetConnection.md。
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public partial class InternetConnection : MonoBehaviour
{
    /// <summary>いま何をしている最中か。画面表示に使う。</summary>
    public enum Phase
    {
        /// <summary>まだ何もしていない</summary>
        Idle,

        /// <summary>つなぐ準備をしている（初回だけ数秒かかる）</summary>
        Preparing,

        /// <summary>部屋を作っている（ホスト側）</summary>
        Creating,

        /// <summary>部屋に入ろうとしている（参加側）</summary>
        Joining,

        /// <summary>つながっている</summary>
        Connected,

        /// <summary>失敗した。理由は Message に入る</summary>
        Failed,
    }

    [Header("部屋の設定")]
    [Tooltip("同時に遊べる人数。ホストを含む")]
    [Range(2, 8)]
    [SerializeField] private int maxPlayers = 4;

    [Tooltip("部屋の名前。一覧に出すわけではないので、分かりやすければよい")]
    [SerializeField] private string sessionName = "StarSweepers";

    [Tooltip("ONにすると、部屋の一覧に出さない（合言葉を知っている人だけが入れる）")]
    [SerializeField] private bool isPrivate = true;

    [Header("中継サーバー")]
    [Tooltip("使う地域。空欄にすると、一番速い地域を自動で選ぶ（ふつうは空欄でよい）。" +
             "例：asia-northeast1（東京）／asia-southeast1（シンガポール）")]
    [SerializeField] private string region = string.Empty;

    /// <summary>いまの状態。</summary>
    public Phase State { get; private set; } = Phase.Idle;

    /// <summary>ホストが作った合言葉。相手に伝える。</summary>
    public string JoinCode { get; private set; } = string.Empty;

    /// <summary>画面に出す説明。失敗したときは理由が入る。</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// **ホストが部屋に付けたパスワード。** 空ならパスワードなし（2026/10/6・大槻さん）。
    /// ホストのPCでだけ分かる（中継サーバーからは読めないため、作ったときに控えておく）。
    /// </summary>
    public string CurrentPassword { get; private set; } = string.Empty;

    /// <summary>いまの部屋にパスワードが付いているか（ホストのPCで使う）。</summary>
    public bool HasPassword => !string.IsNullOrEmpty(CurrentPassword);

    /// <summary>
    /// **直前に部屋に入れなかった理由が、パスワード違いらしいか。**（2026/10/19。タイトルの「パスワードが間違えています」に使う）
    /// 中継サーバーに「パスワード違い」専用の知らせは無いので、エラーの中身（password の文字・断られた・許されていない）で見分ける。
    /// </summary>
    public bool LastJoinFailedByPassword { get; private set; }

    /// <summary>いまの部屋の名前・人数・最大人数（Steam のフレンドに見せるのに使う。部屋に入っていなければ空・0）。</summary>
    public string RoomName => session != null ? session.Name : string.Empty;
    public int RoomPlayers => session != null ? session.PlayerCount : 0;
    public int RoomMaxPlayers => session != null ? session.MaxPlayers : 0;

    /// <summary>
    /// **パスワードの文字数**（遊ぶ人が入れる文字数。2026/10/6・大槻さんの決まり：1〜32文字）。
    /// 中継サーバーは 8〜64 文字しか受け付けないので、8文字に足りないときは<see cref="ToSessionPassword"/> で後ろに a を足して渡す。
    /// </summary>
    public const int PasswordMinLength = 1;
    public const int PasswordMaxLength = 32;

    /// <summary>入力欄に打てる文字数。32文字を超えたことを知らせられるよう、少し多めに打てるようにしておく。</summary>
    public const int PasswordInputLimit = 64;

    /// <summary>中継サーバーが受け付ける、いちばん短いパスワード。</summary>
    private const int SessionPasswordMinLength = 8;

    /// <summary>32文字を超えたときに出す文。</summary>
    public const string PasswordTooLongMessage = "パスワードは32文字までです";

    /// <summary>次に作る部屋のパスワード（空ならなし）。</summary>
    private string password = string.Empty;

    /// <summary>
    /// パスワードとして使えない理由。使えるなら null。**空は「パスワードなし」なので使える。**
    /// </summary>
    public static string PasswordProblem(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length > PasswordMaxLength ? PasswordTooLongMessage : null;
    }

    /// <summary>
    /// **中継サーバーに渡すパスワードにする。** 8文字に足りなければ、足りない数だけ後ろに「a」を足す
    /// （例：「B」→「Baaaaaaa」）。作る側も入る側も同じ足し方をするので、遊ぶ人は「B」と入れるだけでよい。空なら null（パスワードなし）。
    /// </summary>
    private static string ToSessionPassword(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length < SessionPasswordMinLength
            ? value + new string('a', SessionPasswordMinLength - value.Length)
            : value;
    }

    /// <summary>入る側のパスワードの設定（空なら付けない）。</summary>
    private static JoinSessionOptions JoinOptions(string joinPassword)
    {
        return string.IsNullOrEmpty(joinPassword) ? null : new JoinSessionOptions { Password = ToSessionPassword(joinPassword) };
    }

    /// <summary>
    /// **部屋のパスワードを変える**（ホストだけ。ロビーのゲーム設定から呼ぶ）。
    /// うまくいけば null、だめなら理由を返す。パスワードなしで作った部屋には付けられない（付け外しは部屋を作るときに決める）。
    /// </summary>
    public async Task<string> ChangePasswordAsync(string newPassword)
    {
        if (session == null || !session.IsHost || State != Phase.Connected)
        {
            return "インターネットの部屋のホストのときだけ変えられます。";
        }

        if (!HasPassword)
        {
            return "パスワードなしで作った部屋なので、パスワードは付けられません。";
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return "新しいパスワードを入れてください。";
        }

        string problem = PasswordProblem(newPassword);
        if (problem != null)
        {
            return problem;
        }

        try
        {
            IHostSession host = session.AsHost();
            host.Password = ToSessionPassword(newPassword);
            await host.SavePropertiesAsync();
            CurrentPassword = newPassword;
            Debug.Log("[NET] 部屋のパスワードを変えました。");
            return null;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[NET] パスワードを変えられませんでした：{error.Message}");
            return "パスワードを変えられませんでした（通信を確かめてください）。";
        }
    }

    /// <summary>いま作業中か。ボタンを押せなくするのに使う。</summary>
    public bool IsBusy =>
        State == Phase.Preparing || State == Phase.Creating || State == Phase.Joining;

    /// <summary>
    /// いま入っている部屋（Unity の Session）。入っていなければ null。
    /// 部屋の名前・人数・合言葉はここから読む。抜けるとき（<see cref="LeaveGameAsync"/>）に null に戻す。
    /// </summary>
    private ISession session;

    /// <summary>
    /// 「このPCで何番目に起動したか」を押さえておくための鍵。
    /// 掴んだままにしておくと、他のインスタンスは同じ番号を取れない。
    /// **プロセスが終われば自動で解放される。**
    /// </summary>
    private static FileStream instanceLock;

    /// <summary>自動で決まったプロファイル名。1番目は null（初期のまま）。</summary>
    private static string autoProfile;

    /// <summary>
    /// プロファイルを決める処理（<see cref="ResolveInstanceSlot"/>）を、もう済ませたか。
    /// static なので、シーンを行き来して InternetConnection が作り直されても**一度しか決めない**。
    /// </summary>
    private static bool profileResolved;

    /// <summary>
    /// 同じPCで同時に起動できる数の上限。
    /// 起動のたびに `instance0.lock` 〜 `instance7.lock` を順に掴んでいき、全部ふさがっていたらあきらめる。
    /// </summary>
    private const int MaxInstancesPerPc = 8;

    private void Awake()
    {
        // 起動した順に番号を取る。**つなぐ前に決めておく**のが大事
        ResolveInstanceSlot();
    }

    // ------------------------------------------------------------
    // 外から呼ぶ入口
    // ------------------------------------------------------------

    /// <summary>ホストになって部屋を作る。できたら合言葉が <see cref="JoinCode"/> に入る。</summary>
    public async void HostGame()
    {
        if (IsBusy)
        {
            return;
        }

        // パスワードは 32 文字まで（空ならなし）。だめなら作る前に止める
        string passwordProblem = PasswordProblem(password);
        if (passwordProblem != null)
        {
            FailBeforeStart(passwordProblem);
            return;
        }

        if (!await PrepareAsync())
        {
            return;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける（残っていると「すでに参加しています」で入れない）
        await LeaveStaleSessionsAsync();

        State = Phase.Creating;
        Message = "部屋を作っています…";

        try
        {
            // 地域を空欄にすると、Unityが一番速い地域を測って選んでくれる
            SessionOptions options = new SessionOptions
            {
                Name = sessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = isPrivate,
                Password = ToSessionPassword(password),
                // 公開の部屋の一覧で、版の同じ人にだけ見せるため（2026/10/28。SameVersionOnly）
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { VersionPropertyKey, new SessionProperty(LocalVersion(), VisibilityPropertyOptions.Public) },
                },
            }.WithRelayNetwork(string.IsNullOrWhiteSpace(region) ? null : region.Trim());

            // 部屋ができると、通信の開始（ホストとしての待ち受け）まで自動で行われる
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            CurrentPassword = password;
            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "部屋ができました。合言葉を相手に伝えてください。";

            Debug.Log($"[NET] 部屋を作りました。合言葉：{JoinCode}（版：{LocalVersion()}）");

            if (!isPrivate)
            {
                await PublishRoomCodeAsync();
            }
        }
        catch (Exception error)
        {
            Fail("部屋を作れませんでした", error);
        }
    }

    /// <summary>合言葉を使って、他の人の部屋に入る。パスワードの付いた部屋なら <paramref name="joinPassword"/> も要る。</summary>
    public async void JoinGame(string code, string joinPassword = "")
    {
        LastJoinFailedByPassword = false;

        if (IsBusy)
        {
            return;
        }

        string passwordProblem = PasswordProblem(joinPassword);
        if (passwordProblem != null)
        {
            FailBeforeStart(passwordProblem);
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            FailBeforeStart("合言葉が入力されていません。");
            return;
        }

        if (!await PrepareAsync())
        {
            return;
        }

        // 前の部屋から抜け損ねていたら、先に抜ける（残っていると「すでに参加しています」で入れない）
        await LeaveStaleSessionsAsync();

        State = Phase.Joining;
        Message = "部屋に入ろうとしています…";

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim(), JoinOptions(joinPassword));

            CurrentPassword = string.Empty;
            JoinCode = session.Code;
            State = Phase.Connected;
            Message = "つながりました。";

            Debug.Log($"[NET] 部屋に入りました。合言葉：{JoinCode}");
        }
        catch (Exception error)
        {
            LastJoinFailedByPassword = LooksLikeWrongPassword(error, joinPassword);
            Fail("部屋に入れませんでした。合言葉かパスワードが違うか、部屋が閉じられています", error);
        }
    }

    /// <summary>失敗の表示を消して、何もしていない状態に戻す（タイトル画面で「戻る」を押したとき）。</summary>
    public void ClearFailure()
    {
        if (State == Phase.Failed)
        {
            State = Phase.Idle;
            Message = string.Empty;
        }
    }

    /// <summary>
    /// 部屋に入れなかったエラーが、パスワード違いらしいか（<see cref="LastJoinFailedByPassword"/>）。
    ///
    /// 中継サーバーは「パスワードが違う」とはっきり教えてくれないので、
    /// **エラーの種類と文面から推測する。** 次のどれかなら「パスワード違い」とみなす。
    ///
    /// 1. 中継サーバーが「断られた（Forbidden）」「許されていない（NotAuthorized）」と返した
    /// 2. エラーの文面に password の文字が入っている
    /// 3. パスワードを入れて入ろうとしたのに、満員でも・部屋が無いのでもない理由で断られた
    /// </summary>
    private static bool LooksLikeWrongPassword(Exception error, string joinPassword)
    {
        if (error is SessionException sessionError &&
            (sessionError.Error == SessionError.Forbidden || sessionError.Error == SessionError.NotAuthorized))
        {
            return true;
        }

        string text = error.ToString();
        if (text.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // パスワードを入れて入ろうとして、満員・見つからない以外で断られたときも、パスワード違いとみなす
        bool triedWithPassword = !string.IsNullOrEmpty(joinPassword);
        bool roomIsFull = text.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0;
        bool roomNotFound = error is SessionException notFound && notFound.Error == SessionError.SessionNotFound;

        return triedWithPassword && !roomIsFull && !roomNotFound;
    }

    /// <summary>
    /// **始める前の確認で引っかかったとき**の後始末（パスワードが長すぎる・合言葉が空 など）。
    /// 画面に理由を出して、何もしていない状態（ボタンを押せる状態）に戻す。
    /// 通信のエラーで失敗したときは、ログも残す <see cref="Fail"/> のほうを使う。
    /// </summary>
    private void FailBeforeStart(string message)
    {
        State = Phase.Failed;
        Message = message;
    }

    /// <summary>失敗したときの後始末。理由を画面とログの両方に残す。</summary>
    private void Fail(string reason, Exception error)
    {
        State = Phase.Failed;
        Message = $"{reason}\n（{error.GetType().Name}）";

        Debug.LogError($"[NET] {reason}：{error}");
    }
}
