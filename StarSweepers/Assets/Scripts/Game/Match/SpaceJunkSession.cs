using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>試合がいまどの段階か。</summary>
public enum SpaceJunkMatchState
{
    /// <summary>ロビーで待っている（ホストが設定中）</summary>
    Lobby,

    /// <summary>ラウンドを戦っている最中</summary>
    Playing,

    /// <summary>何本先取に届いて、試合そのものが終わった</summary>
    MatchOver
}

/// <summary>
/// 参加者1人ぶんの席。**誰が・どのチームか**を持つ。名前はプレイヤー自身が持つ。
/// <see cref="NetworkList{T}"/> に入れるため、構造体にして比較できるようにしてある。
/// </summary>
public struct SpaceJunkPlayerSlot : INetworkSerializable, IEquatable<SpaceJunkPlayerSlot>
{
    /// <summary>
    /// その人の番号。**1台に1人なら接続番号（ClientId）そのもの。**
    /// 1台で複数人のときの2人目以降は、接続番号に「何人目か」を足した人ごとの番号（<see cref="LocalMultiplayer.MakeKey"/>）。
    /// 接続番号に戻すときは <see cref="LocalMultiplayer.ClientOf"/>。
    /// </summary>
    public ulong ClientId;

    /// <summary>所属チーム（0から）。</summary>
    public int Team;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Team);
    }

    public bool Equals(SpaceJunkPlayerSlot other)
    {
        return ClientId == other.ClientId && Team == other.Team;
    }
}

/// <summary>
/// **宇宙ごみ集めの「試合そのもの」を覚えておく係。**
///
/// ## なぜこれが要るのか
///
/// この遊びは**ラウンドごとにマップ（シーン）が切り替わる。**
/// シーンを切り替えると、そのシーンに置いてあった物は全部消えてしまうので、
/// **ラウンドをまたいで覚えておきたいもの**を、ここに集めてある。
///
///   ・ホストが決めた設定（何本先取・1ラウンドの最大時間・使うマップ）
///   ・誰がどのチームか
///   ・チームごとの**ラウンドの勝ち数**
///
/// ## どうやってシーンをまたぐのか
///
/// **プレハブから生まれた通信オブジェクトは、シーンを切り替えても生き続ける**
/// （Netcode が「どのシーンにも属さない場所」へ移してくれる）。
/// 釣りのプレイヤーがロビーから会場へそのまま付いてくるのと同じ仕組み。
///
/// そのため、これは**シーンに置くのではなく、ホストがプレハブから1つだけ出す。**
/// 出す役は SpaceJunkSessionSpawner。
///
/// ## 書き換えられるのはホストだけ
///
/// 設定もチーム分けも勝ち数も、**すべてホストが決めて全員へ配る**
/// （ネットワークの制作工程.md の「動きは本人、勝敗と点数はホスト」の形）。
/// 参加者のPCからは読むだけ。
///
/// ## 流れ（何がどの順で起きるか）
///
/// 1. **部屋ができる** … ホストがこれをプレハブから出す（<see cref="OnNetworkSpawn"/>）。
///    つながっている人の席を作り、前回のマップの選び方を読み込む。参加者は自分の名前をホストへ送る
/// 2. **ロビー** … 人が入る・抜けるたびに席を直す。ホストは設定端末からチーム数・何本先取・時間・マップを変える
///    （`ServerSet…` のメソッド）。裏では1秒ごとに「席とつながっている人」の答え合わせをする
/// 3. **試合開始** … ロビーの「ゲームを始める」→ <see cref="ServerStartMatch"/>。勝ち数を 0 に戻してマップへ移る
/// 4. **ラウンド** … マップ側の <see cref="SpaceJunkRound"/> が決着を付けて <see cref="ServerReportRoundResult"/> を呼ぶ。
///    何本先取に届かなければ、結果を見せてから次のマップへ（<see cref="NextRoundAfterDelay"/>）
/// 5. **試合終了** … 届いたら <see cref="SpaceJunkMatchState.MatchOver"/> にして、結果を見せてからロビーへ戻る
///    （<see cref="ReturnToLobbyAfterDelay"/>）。ホストがポーズ画面から打ち切ったときは <see cref="ServerReturnToLobby"/>
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Game/Match/SpaceJunkSession.md。
/// </summary>
public partial class SpaceJunkSession : NetworkBehaviour
{
    /// <summary>いま動いている試合の係。どこからでも参照できるように持っている。</summary>
    public static SpaceJunkSession Current { get; private set; }

    /// <summary>チームが確定した瞬間に通知する（ホスト側）。引数はプレイヤーキーと新しいチーム番号。</summary>
    public event Action<ulong, int> OnPlayerTeamChanged;

    // ロビーでエリアに入っているプレイヤーキーとチーム番号。試合開始判定はホストだけで行う。
    private readonly Dictionary<ulong, int> playersInTeamZones = new Dictionary<ulong, int>();
    private double lobbyCountdownEndsAt;
    private bool lobbyCountdownActive;
    private bool lobbyCountdownWasHostTriggered;

    [Header("戻り先")]
    [Tooltip("試合が終わったあとに全員が戻るシーンの名前。**ビルドの一覧に入っていること**")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Header("待ち時間")]
    [Tooltip("ラウンドの結果を見せてから、次のマップへ移るまでの秒数")]
    [SerializeField] private float roundResultSeconds = 5f;

    [Tooltip("試合の結果を見せてから、ロビーへ戻るまでの秒数")]
    [SerializeField] private float matchResultSeconds = 8f;

    [Header("ラウンドの勝ち方")]
    [Tooltip("Score＝時間いっぱい得点を競う（いまのルール）。\n" +
             "ThreeKinds＝素材3種類をそろえたチームがその場で勝つ（前のルール。**コードだけ残してある**）。\n" +
             "ロビーには出していない。戻したいときはここを切り替える")]
    [SerializeField] private SpaceJunkWinRule winRule = SpaceJunkWinRule.Score;

    /// <summary>ラウンドの勝ち方。**ホストの値が使われる**（<see cref="SpaceJunkRound"/> が開始時に全員へ配る）。</summary>
    public SpaceJunkWinRule WinRule => winRule;

    [Header("イベント（得点制のときだけ起きる）")]
    [Tooltip("ラウンドの途中で起きるイベントの設定。何秒後に始めるか・候補の一覧・お題セットの個数とボーナス")]
    [SerializeField] private SpaceJunkEventSettings events = new SpaceJunkEventSettings();

    /// <summary>イベントの設定。**ホストの値が使われる**（<see cref="SpaceJunkRound"/> がホストで読む）。</summary>
    public SpaceJunkEventSettings Events => events;

    // ------------------------------------------------------------
    // ホストが決めて全員へ配るもの
    // ------------------------------------------------------------

    /// <summary>いくつのチームに分かれるか。**ホストが決める。**</summary>
    private readonly NetworkVariable<int> teamCount = new NetworkVariable<int>(2);

    /// <summary>何本先取か。**ホストがゲーム開始前に決める。**</summary>
    private readonly NetworkVariable<int> roundsToWin = new NetworkVariable<int>(2);

    /// <summary>1ラウンドの最大時間（秒）。**ホストがゲーム開始前に決める。**</summary>
    private readonly NetworkVariable<float> roundSeconds = new NetworkVariable<float>(60f);

    /// <summary>いまの段階。</summary>
    private readonly NetworkVariable<SpaceJunkMatchState> state =
        new NetworkVariable<SpaceJunkMatchState>(SpaceJunkMatchState.Lobby);

    /// <summary>いま何ラウンド目か（1から）。</summary>
    private readonly NetworkVariable<int> currentRound = new NetworkVariable<int>(0);

    /// <summary>直前のラウンドを取ったチーム。-1 なら引き分けで、誰も取っていない。</summary>
    private readonly NetworkVariable<int> lastRoundWinner = new NetworkVariable<int>(-1);

    /// <summary>試合に勝ったチーム。まだ決まっていなければ -1。</summary>
    private readonly NetworkVariable<int> matchWinner = new NetworkVariable<int>(-1);

    /// <summary>いま遊んでいるマップのシーン名。次のラウンドで同じマップを避けるために持つ。</summary>
    private readonly NetworkVariable<FixedString64Bytes> currentMap =
        new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>誰がどのチームか。</summary>
    private readonly NetworkList<SpaceJunkPlayerSlot> slots = new NetworkList<SpaceJunkPlayerSlot>();

    /// <summary>ホストが選んだ、この試合で使うマップ（「ランダム」のときの候補。ロビーのチェック）。</summary>
    private readonly NetworkList<FixedString64Bytes> selectedMaps = new NetworkList<FixedString64Bytes>();

    /// <summary>
    /// **ホストが「このマップで遊ぶ」と1つ選んだマップ。** 空なら「ランダム」（チェックの付いたマップから選ぶ）。
    /// 1つ選んだときは、**全部のラウンドをそのマップで遊ぶ**（2026/10/6・大槻さん。めっちゃカメレオンのマップ選び）。
    /// </summary>
    private readonly NetworkVariable<FixedString64Bytes> fixedMap = new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>チームごとのラウンドの勝ち数。長さは常に <see cref="SpaceJunkTeams.MaxTeams"/>。</summary>
    private readonly NetworkList<int> roundWins = new NetworkList<int>();

    // ------------------------------------------------------------
    // 読むためのもの（参加者のPCからも使える）
    // ------------------------------------------------------------

    /// <summary>いくつのチームに分かれるか。</summary>
    public int TeamCount => Mathf.Clamp(teamCount.Value, 1, SpaceJunkTeams.MaxTeams);

    /// <summary>何本先取か。</summary>
    public int RoundsToWin => Mathf.Max(1, roundsToWin.Value);

    /// <summary>
    /// 1ラウンドの最大時間（秒）。
    /// 5秒は念のための下限（ホストが入れられる下限は <see cref="MinRoundSeconds"/>。壊れた値が届いても一瞬で終わるラウンドにならないように）。
    /// </summary>
    public float RoundSeconds => Mathf.Max(5f, roundSeconds.Value);

    /// <summary>いまの段階。</summary>
    public SpaceJunkMatchState State => state.Value;

    /// <summary>いま何ラウンド目か（1から。まだ始まっていなければ 0）。</summary>
    public int CurrentRound => currentRound.Value;

    /// <summary>直前のラウンドを取ったチーム。-1 なら引き分け。</summary>
    public int LastRoundWinner => lastRoundWinner.Value;

    /// <summary>試合に勝ったチーム。まだなら -1。</summary>
    public int MatchWinner => matchWinner.Value;

    /// <summary>いま遊んでいるマップのシーン名。</summary>
    public string CurrentMap => currentMap.Value.ToString();

    /// <summary>4つのゴールの持ち主。並び順は北・東・南・西。</summary>
    public int[] GoalOwners => SpaceJunkTeams.GoalOwners(TeamCount);

    /// <summary>そのチームが取ったラウンド数。</summary>
    public int RoundWinsOf(int team)
    {
        if (team < 0 || team >= roundWins.Count)
        {
            return 0;
        }

        return roundWins[team];
    }

    /// <summary>
    /// **そのチームに誰か入っているか。**
    ///
    /// 誰もいないチームのゴールに素材が入っても数えないようにするために使う。
    /// 入れてしまうと、**投げ間違いだけで「誰もいないチーム」がラウンドを取る**ことがある。
    /// </summary>
    public bool HasPlayers(int team)
    {
        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team == team)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>そのチームの人数。ロビーの表示にも使う。</summary>
    public int PlayerCountOf(int team)
    {
        int count = 0;

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>その人の所属チーム。席が無ければ 0。</summary>
    public int TeamOf(ulong clientId)
    {
        int index = TeamSlotIndex(clientId);
        return index >= 0 ? slots[index].Team : 0;
    }

    /// <summary>
    /// <see cref="Slots"/> と <see cref="SelectedMaps"/> が返す入れ物。**毎回作らず使い回す。**
    ///
    /// ロビーの画面は `OnGUI` で描いていて、**1フレームに何度も呼ばれる。**
    /// そのたびに新しいリストを作ると、捨てるゴミが積み上がって動きが重くなる。
    ///
    /// ⚠ **使い回しているので、1つの `foreach` を回している途中で
    /// もう一度同じものを取りに行かないこと**（中身が入れ替わる）。
    /// 取り出したら、最後まで使い切ってから次を取ること。
    /// </summary>
    private readonly List<SpaceJunkPlayerSlot> slotsView = new List<SpaceJunkPlayerSlot>();

    private readonly List<string> mapsView = new List<string>();

    /// <summary>いま席にいる人を、入った順に返す。ロビーの一覧に使う。</summary>
    public IReadOnlyList<SpaceJunkPlayerSlot> Slots
    {
        get
        {
            slotsView.Clear();
            foreach (SpaceJunkPlayerSlot slot in slots)
            {
                slotsView.Add(slot);
            }
            return slotsView;
        }
    }

    /// <summary>ホストが選んでいるマップ。</summary>
    public IReadOnlyList<string> SelectedMaps
    {
        get
        {
            mapsView.Clear();
            foreach (FixedString64Bytes map in selectedMaps)
            {
                mapsView.Add(map.ToString());
            }
            return mapsView;
        }
    }

    /// <summary>ホストが選んでいるマップの数。**中身が要らないときはこちらを使う**（入れ物を作らない）。</summary>
    public int SelectedMapCount => selectedMaps.Count;

    /// <summary>「このマップで遊ぶ」と選ばれたマップのシーン名。空なら「ランダム」。</summary>
    public string FixedMap => fixedMap.Value.ToString();

    /// <summary>マップを「ランダム」で選ぶか（1つに決めていないか）。</summary>
    public bool IsRandomMap => fixedMap.Value.Length == 0;

    /// <summary>
    /// 試合を始められるマップの選び方になっているか。
    /// 1つに決めている、または「ランダム」でチェックの付いたマップが1つ以上ある。
    /// </summary>
    public bool HasPlayableMapChoice => !IsRandomMap || selectedMaps.Count > 0;

    /// <summary>そのマップが選ばれているか。</summary>
    public bool IsMapSelected(string sceneName)
    {
        FixedString64Bytes value = new FixedString64Bytes(sceneName ?? string.Empty);

        foreach (FixedString64Bytes map in selectedMaps)
        {
            if (map.Equals(value))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------
    // 出入り
    // ------------------------------------------------------------

    /// <summary>
    /// 全員のPCで、この係が現れたとき。
    /// **ホストは席と勝ち数の枠を作って、人の出入りの知らせを受け取り始める。**
    /// </summary>
    public override void OnNetworkSpawn()
    {
        Current = this;

        if (IsServer)
        {
            // ラウンドの勝ち数の枠を、チームの最大数ぶん用意する
            roundWins.Clear();
            for (int i = 0; i < SpaceJunkTeams.MaxTeams; i++)
            {
                roundWins.Add(0);
            }

            // いまつながっている人の席を作る（ホスト自身を含む）
            slots.Clear();
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                AddSlot(clientId);
            }

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // 前回ホストをしたときのマップの選び方（チェックと「このマップで遊ぶ」）を読み込む
            LoadSavedMapChoice();
        }
    }

    // ------------------------------------------------------------
    // マップの選び方を覚えておく（2026/10/6・大槻さん「チェックしたかは前回の設定を読み込んでほしい」）
    // ------------------------------------------------------------

    /// <summary>チェックしたマップ（シーン名を改行でつないだもの）を保存する鍵。</summary>
    private const string SavedMapsKey = "StarSweepers.SpaceJunk.SelectedMaps";

    /// <summary>「このマップで遊ぶ」で選んだマップ（空ならランダム）を保存する鍵。</summary>
    private const string SavedFixedMapKey = "StarSweepers.SpaceJunk.FixedMap";

    /// <summary>
    /// 前回のマップの選び方を読み込む（ホストだけ）。**ビルドの一覧に無くなったマップは読み込まない**
    /// （名前を変えた・消したマップがチェックの数に入ってしまわないように）。
    /// </summary>
    private void LoadSavedMapChoice()
    {
        selectedMaps.Clear();

        string saved = PlayerPrefs.GetString(SavedMapsKey, string.Empty);
        foreach (string name in saved.Split('\n'))
        {
            if (!string.IsNullOrEmpty(name) && SpaceJunkLobbyUI.IsSceneInBuildList(name) && !IsMapSelected(name))
            {
                selectedMaps.Add(new FixedString64Bytes(name));
            }
        }

        string fixedName = PlayerPrefs.GetString(SavedFixedMapKey, string.Empty);
        fixedMap.Value = SpaceJunkLobbyUI.IsSceneInBuildList(fixedName)
            ? new FixedString64Bytes(fixedName)
            : default;

        if (selectedMaps.Count > 0 || !IsRandomMap)
        {
            Debug.Log($"[JUNK] 前回のマップの選び方を読み込みました（チェック {selectedMaps.Count} 個／{(IsRandomMap ? "ランダム" : FixedMap)}）。");
        }
    }

    /// <summary>いまのマップの選び方を保存する（ホストだけ）。</summary>
    private void SaveMapChoice()
    {
        PlayerPrefs.SetString(SavedMapsKey, string.Join("\n", SelectedMaps));
        PlayerPrefs.SetString(SavedFixedMapKey, FixedMap);
        PlayerPrefs.Save();
    }

    /// <summary>係が消えるとき（部屋を閉じた・切断した）。ホストは人の出入りの知らせを受け取るのをやめる。</summary>
    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>誰かがつながってきたとき（ホストだけ）。定員を超えていなければ席を作る。</summary>
    private void OnClientConnected(ulong clientId)
    {
        // **定員（6人）を超えてつながってきた人は、断って切る。**（2026/10/14・大槻さん「定員を6人に」）
        // インターネットの部屋は中継サーバーが定員で止めてくれるが、LAN の部屋はつなぐ時点で止める仕組みが無く、
        // 7人目もつながったまま「席が無い（体が出ない）」状態になっていた
        if (IsServer && clientId != NetworkManager.ServerClientId && !HasSlot(clientId) &&
            slots.Count >= SpaceJunkTeams.MaxPlayers)
        {
            Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）なので、接続番号 {clientId} を断りました。");
            NetworkManager.DisconnectClient(clientId, $"満員です（最大{SpaceJunkTeams.MaxPlayers}人）");
            return;
        }

        AddSlot(clientId);
    }

    /// <summary>その接続番号の席が、もうあるか。</summary>
    private bool HasSlot(ulong clientId)
    {
        return TeamSlotIndex(clientId) >= 0;
    }

    // ------------------------------------------------------------
    // 名前（2026/10/6）
    // ------------------------------------------------------------

    /// <summary>
    /// その人の表示名。名前の番号と同期はプレイヤー自身が持つ。
    /// 人ごとの番号で探すので、同じPCの2人目以降もそれぞれの名前になる。
    /// </summary>
    public string NameOf(ulong clientId)
    {
        // デバッグ用のボットは、従来どおり番号で見分ける。
        if (IsBotKey(clientId))
        {
            return $"BOT {LocalMultiplayer.SeatOf(clientId) - BotSeatStart + 1}";
        }

        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player != null && player.IsSpawned && player.NetworkManager == NetworkManager
                && player.PlayerKey == clientId)
            {
                return player.DisplayName;
            }
        }

        return $"参加者 {clientId}";
    }

    /// <summary>ホストが席と接続中の人を定期的に比べる。名前はPlayerのNetworkVariableが配る。</summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer || NetworkManager == null)
        {
            return;
        }

        UpdateLobbyStartCountdown();

        // ホストのPC：席の一覧とつながっている人を、一定の間隔で比べる
        slotCheckTimer += Time.unscaledDeltaTime;
        if (slotCheckTimer < SlotCheckInterval)
        {
            return;
        }
        slotCheckTimer = 0f;

        ServerCheckSlots();
    }

    /// <summary>その人（人ごとの番号）の席を消す。</summary>
    private void RemoveSlot(ulong key)
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i].ClientId == key)
            {
                slots.RemoveAt(i);
            }
        }
    }

    /// <summary>その人の席の番号（無ければ -1）。</summary>
    private int TeamSlotIndex(ulong clientId)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].ClientId == clientId)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 誰かが抜けたとき（ホストだけ）。**そのPCの席を全部消す**
    /// （1台で複数人なら、2人目以降の席も一緒に。人ごとの番号から接続番号に戻して比べる）。
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        bool removedFromZone = false;
        foreach (ulong key in new List<ulong>(playersInTeamZones.Keys))
        {
            if (LocalMultiplayer.ClientOf(key) == clientId)
            {
                playersInTeamZones.Remove(key);
                removedFromZone = true;
            }
        }

        if (removedFromZone)
        {
            CancelLobbyStartCountdown();
        }

        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (LocalMultiplayer.ClientOf(slots[i].ClientId) == clientId)
            {
                slots.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 席を作る。**入ってきた人は、いちばん人数の少ないチームに入れておく。**
    /// ホストがあとから自由に動かせるので、これは仮の置き場でしかない。
    /// </summary>
    private void AddSlot(ulong clientId)
    {
        if (HasSlot(clientId))
        {
            return;
        }

        if (slots.Count >= SpaceJunkTeams.MaxPlayers)
        {
            Debug.LogWarning($"[JUNK] 定員（{SpaceJunkTeams.MaxPlayers}人）を超えたので席を作りませんでした（{clientId}）。");
            return;
        }

        slots.Add(new SpaceJunkPlayerSlot { ClientId = clientId, Team = SmallestTeam() });
    }

    /// <summary>いま一番人数の少ないチームの番号。</summary>
    private int SmallestTeam()
    {
        int teams = TeamCount;
        int[] counts = new int[teams];

        foreach (SpaceJunkPlayerSlot slot in slots)
        {
            if (slot.Team >= 0 && slot.Team < teams)
            {
                counts[slot.Team]++;
            }
        }

        int best = 0;
        for (int i = 1; i < teams; i++)
        {
            if (counts[i] < counts[best])
            {
                best = i;
            }
        }

        return best;
    }

}
