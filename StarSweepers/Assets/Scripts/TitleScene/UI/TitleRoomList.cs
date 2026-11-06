using System.Collections.Generic;
using System.Text;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

/// <summary>タイトル画面の「部屋を探す」（LAN・公開・フレンドの部屋の一覧と、部屋番号での検索）。</summary>
public class TitleRoomList
{
    private const string SearchPublicKey = "StarSweepers.SearchPublic";
    private const string SearchFriendsKey = "StarSweepers.SearchFriends";
    private const string SearchNoPasswordKey = "StarSweepers.SearchNoPassword";

    /// <summary>部屋番号の文字数（全部入れると、一覧に無いプライベートの部屋も出す）。</summary>
    private const int RoomCodeLength = 6;

    /// <summary>公開の部屋の一覧を取り直す間隔（短いと頼みすぎで断られる）。</summary>
    private const float PublicRefreshSeconds = 5f;

    /// <summary>公開の部屋の一覧を取れなかったときに、次に取り直すまでの秒数。</summary>
    private const float PublicRetrySeconds = 15f;

    private const float FriendRefreshSeconds = 10f;

    /// <summary>Steam にフレンドの様子を頼んでから、読むまで待つミリ秒。</summary>
    private const int FriendReplyWaitMilliseconds = 1000;

    /// <summary>一覧の1行ぶん（LAN・公開・フレンド・番号で入る部屋のどれか）。</summary>
    private struct Entry
    {
        public string Name;
        public string Players;
        public bool Full;
        public string Code;
        public bool Locked;
        public UnityEngine.Events.UnityAction Join;

        /// <summary>どの部屋かを見分ける文字（並べ直すかの判定に使う）。</summary>
        public string Key;
    }

    /// <summary>この一覧を持つ画面（await のあとで、画面が消えていないかを確かめる）。</summary>
    private readonly MonoBehaviour owner;
    private readonly TitleParts parts;
    private readonly TitleInputFields inputs;
    private readonly TitleJoinDialog joinDialog;

    private readonly GameObject cardTemplate;
    private readonly List<GameObject> cards = new List<GameObject>();

    /// <summary>前に並べた一覧の中身を1本の文字にしたもの。変わったときだけカードを作り直す（null で必ず作り直す）。</summary>
    private string signature = string.Empty;

    // 検索の設定（■パブリック ■フレンド ■パスワード不要）。最初に画面を開いたときに読む
    private bool searchPublic = true;
    private bool searchFriends = true;
    private bool searchNoPassword = true;
    private bool searchSettingsLoaded;

    private readonly List<ISessionInfo> publicSessions = new List<ISessionInfo>();

    /// <summary>公開の部屋の一覧を取れなかった理由（一覧の下に出す）。</summary>
    private string publicProblem = string.Empty;

    private float nextPublicQueryTime;

    private readonly List<SteamFriendRoom> friendRooms = new List<SteamFriendRoom>();
    private bool searchingFriends;

    /// <summary>フレンドの部屋を探せない理由（一覧の下に出す）。</summary>
    private string friendStatus = string.Empty;

    private float nextFriendQueryTime;
    private bool lanListening;

    /// <summary>一覧用に公開の部屋を取っている途中か（クイックマッチはこれが終わるのを待つ）。</summary>
    public bool Searching { get; private set; }

    public TitleRoomList(MonoBehaviour owner, TitleParts parts, TitleInputFields inputs, TitleJoinDialog joinDialog)
    {
        this.owner = owner;
        this.parts = parts;
        this.inputs = inputs;
        this.joinDialog = joinDialog;

        // カードの見本は、複製して1枚ずつ作るので隠しておく
        cardTemplate = parts.Object(TitlePartRole.JoinableCardTemplate);
        if (cardTemplate != null)
        {
            cardTemplate.SetActive(false);
        }
    }

    /// <summary>カードの見本（この中のボタンは、カードを作るときにつなぐ）。</summary>
    public GameObject CardTemplate => cardTemplate;

    /// <summary>一覧の一番上のカード（無ければ null）。</summary>
    public GameObject FirstCard => cards.Count > 0 ? cards[0] : null;

    /// <summary>一覧のスクロール（右スティックで動かす先）。</summary>
    public ScrollRect ScrollRect()
    {
        GameObject content = parts.Object(TitlePartRole.JoinableListContent);
        return content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
    }

    /// <summary>画面を開いたとき。検索の設定を出し、一覧をすぐ取り直す。</summary>
    public void Open(bool quickPlaying)
    {
        if (!searchSettingsLoaded)
        {
            searchSettingsLoaded = true;
            searchPublic = PlayerPrefs.GetInt(SearchPublicKey, 1) != 0;
            searchFriends = PlayerPrefs.GetInt(SearchFriendsKey, 1) != 0;
            searchNoPassword = PlayerPrefs.GetInt(SearchNoPasswordKey, 1) != 0;
        }

        RefreshChecks();

        nextFriendQueryTime = 0f;
        signature = null;
        Update(quickPlaying);
    }

    /// <summary>LAN の部屋の知らせを聞き始める・やめる。</summary>
    public void SetListening(bool listen)
    {
        if (listen == lanListening)
        {
            return;
        }

        lanListening = listen;
        if (listen)
        {
            LanRoomDiscovery.StartListening();
        }
        else
        {
            LanRoomDiscovery.StopListening();
        }
    }

    /// <summary>■パブリック を切り替えて覚え、一覧を並べ直す。</summary>
    public void ToggleSearchPublic()
    {
        searchPublic = !searchPublic;
        SaveCheck(SearchPublicKey, searchPublic);
    }

    /// <summary>■フレンド を切り替えて覚え、フレンドの部屋をすぐ探し直す。</summary>
    public void ToggleSearchFriends()
    {
        searchFriends = !searchFriends;
        SaveCheck(SearchFriendsKey, searchFriends);
        nextFriendQueryTime = 0f;
    }

    /// <summary>■パスワード不要 を切り替えて覚え、一覧を並べ直す。</summary>
    public void ToggleSearchNoPassword()
    {
        searchNoPassword = !searchNoPassword;
        SaveCheck(SearchNoPasswordKey, searchNoPassword);
    }

    /// <summary>一覧の ⟳。公開とフレンドの部屋をすぐ取り直す。</summary>
    public void RefreshNow()
    {
        nextPublicQueryTime = 0f;
        nextFriendQueryTime = 0f;
        signature = null;
    }

    /// <summary>一覧を間隔をあけて取り直し、中身が変わったときだけ並べ直す（画面を開いている間、毎フレーム呼ぶ）。</summary>
    public void Update(bool quickPlaying)
    {
        float now = Time.unscaledTime;

        // クイックマッチの途中は、インターネットの係を取り合わないように取らない
        if (searchPublic && !Searching && !quickPlaying && now >= nextPublicQueryTime)
        {
            nextPublicQueryTime = now + PublicRefreshSeconds;
            QueryPublic();
        }

        if (searchFriends && !searchingFriends && now >= nextFriendQueryTime)
        {
            nextFriendQueryTime = now + FriendRefreshSeconds;
            SearchFriends();
        }

        List<Entry> entries = CollectEntries();
        string newSignature = SignatureOf(entries);
        if (newSignature == signature)
        {
            return;
        }

        signature = newSignature;
        RebuildCards(entries);
    }

    private void SaveCheck(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshChecks();
        signature = null;
    }

    private void RefreshChecks()
    {
        parts.SetText(TitlePartRole.SearchPublicCheckText, TitleParts.CheckMark(searchPublic));
        parts.SetText(TitlePartRole.SearchFriendsCheckText, TitleParts.CheckMark(searchFriends));
        parts.SetText(TitlePartRole.SearchNoPasswordCheckText, TitleParts.CheckMark(searchNoPassword));
        parts.SetSelectedLook(TitleButtonAction.ToggleSearchPublic, searchPublic);
        parts.SetSelectedLook(TitleButtonAction.ToggleSearchFriends, searchFriends);
        parts.SetSelectedLook(TitleButtonAction.ToggleSearchNoPassword, searchNoPassword);
    }

    /// <summary>検索欄の文字（前後の空白と「#」を除き、大文字にそろえる）。</summary>
    private string RoomCodeSearch()
    {
        return (inputs.RoomCode ?? string.Empty).Trim().TrimStart('#').Trim().ToUpperInvariant();
    }

    /// <summary>一覧に出るもの全部を1本の文字にする（前と同じなら並べ直さない）。</summary>
    private string SignatureOf(List<Entry> entries)
    {
        StringBuilder text = new StringBuilder();
        text.Append(searchPublic).Append(searchFriends).Append(searchNoPassword).Append('|').Append(RoomCodeSearch())
            .Append('|').Append(publicProblem).Append('|').Append(friendStatus).Append('|');
        foreach (Entry entry in entries)
        {
            text.Append(entry.Key).Append(',').Append(entry.Name).Append(',').Append(entry.Code).Append(',')
                .Append(entry.Players).Append(entry.Full).Append(entry.Locked).Append(';');
        }

        return text.ToString();
    }

    /// <summary>一覧に並べる行を集める（LAN → 公開 → フレンドの順。検索欄があれば番号で絞る）。</summary>
    private List<Entry> CollectEntries()
    {
        List<Entry> all = new List<Entry>();
        AddLanEntries(all);

        if (searchPublic)
        {
            AddPublicEntries(all);
        }

        if (searchFriends)
        {
            AddFriendEntries(all);
        }

        // 番号がその文字で始まる部屋 → 途中に入っている部屋の順
        string search = RoomCodeSearch();
        List<Entry> startsWith = new List<Entry>();
        List<Entry> contains = new List<Entry>();
        bool exactFound = false;
        foreach (Entry entry in all)
        {
            if (searchNoPassword && entry.Locked)
            {
                continue;
            }

            if (search.Length == 0)
            {
                startsWith.Add(entry);
                continue;
            }

            string code = (entry.Code ?? string.Empty).ToUpperInvariant();
            exactFound |= code == search;
            if (code.StartsWith(search, System.StringComparison.Ordinal))
            {
                startsWith.Add(entry);
            }
            else if (code.Length > 0 && code.Contains(search))
            {
                contains.Add(entry);
            }
        }

        startsWith.AddRange(contains);

        // プライベートの部屋は一覧に出ないので、番号を全部入れたときだけ「入ってみる」行を出す
        if (search.Length == RoomCodeLength && !exactFound)
        {
            startsWith.Add(PrivateCodeEntry(search));
        }

        return startsWith;
    }

    /// <summary>一覧に無い部屋番号に「入ってみる」行（パスワードで断られたら聞き直す）。</summary>
    private Entry PrivateCodeEntry(string code)
    {
        string roomName = $"部屋番号 #{code}";
        return new Entry
        {
            Key = $"code:{code}",
            Name = "プライベートの部屋（この番号で入ってみる）",
            Code = code,
            Players = "?",
            Full = false,
            Locked = false,
            Join = () => joinDialog.Open(roomName, false, password =>
            {
                InternetConnection internet = TitleConnection.Internet();
                if (internet != null)
                {
                    internet.JoinGame(code, password);
                }
            }, true),
        };
    }

    /// <summary>LAN の部屋の行（押すと確認なしでつなぐ）。</summary>
    private void AddLanEntries(List<Entry> entries)
    {
        foreach (LanRoomDiscovery.Room room in LanRoomDiscovery.Rooms)
        {
            string address = room.Address;
            ushort port = room.Port;
            entries.Add(new Entry
            {
                Key = $"lan:{room.Address}:{room.Port}",
                Name = $"【LAN】{room.Name}",
                Players = $"{room.Players}/{room.MaxPlayers}",
                Full = room.Players >= room.MaxPlayers,
                Join = () => TitleConnection.JoinLanRoom(address, port),
            });
        }
    }

    /// <summary>公開の部屋の行（押すと参加の確認の窓を出す）。</summary>
    private void AddPublicEntries(List<Entry> entries)
    {
        foreach (ISessionInfo info in publicSessions)
        {
            string sessionId = info.Id;
            string roomName = string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name;
            bool hasPassword = info.HasPassword;
            entries.Add(new Entry
            {
                Key = $"pub:{info.Id}",
                Name = roomName,
                Code = InternetConnection.RoomCodeOf(info),
                Locked = hasPassword,
                Players = $"{TitleConnection.PlayersIn(info)}/{info.MaxPlayers}",
                Full = info.AvailableSlots <= 0 || info.IsLocked,
                Join = () => joinDialog.Open(roomName, hasPassword, password =>
                {
                    InternetConnection internet = TitleConnection.Internet();
                    if (internet != null)
                    {
                        internet.JoinGameById(sessionId, password);
                    }
                }),
            });
        }
    }

    /// <summary>Steam のフレンドの部屋の行（満員かは分からないので、いつも押せる）。</summary>
    private void AddFriendEntries(List<Entry> entries)
    {
        foreach (SteamFriendRoom room in friendRooms)
        {
            string code = room.JoinCode;
            string roomName = $"{room.FriendName} の部屋";
            bool hasPassword = room.HasPassword;
            entries.Add(new Entry
            {
                Key = $"friend:{room.JoinCode}",
                Name = $"【フレンド】{room.FriendName} の部屋（{room.RoomName}）",
                Code = code,
                Locked = hasPassword,
                Players = room.Players,
                Full = false,
                Join = () => joinDialog.Open(roomName, hasPassword, password =>
                {
                    InternetConnection internet = TitleConnection.Internet();
                    if (internet != null)
                    {
                        internet.JoinGame(code, password);
                    }
                }),
            });
        }
    }

    /// <summary>一覧に並べる公開の部屋を取る（取れなかったら少し待つ）。</summary>
    private async void QueryPublic()
    {
        InternetConnection internet = TitleConnection.Internet();
        if (internet == null)
        {
            publicProblem = "インターネットの接続の部品が見つかりません";
            return;
        }

        if (internet.IsBusy)
        {
            return;
        }

        Searching = true;
        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        Searching = false;

        // 待っている間に画面が消えた（Unity では消えた物も == null になる）
        if (owner == null)
        {
            return;
        }

        publicSessions.Clear();
        if (sessions == null)
        {
            publicProblem = (string.IsNullOrEmpty(internet.Message) ? "公開のサーバーの一覧を取れませんでした" : internet.Message) +
                            $"（{PublicRetrySeconds:0}秒後にもう一度探します）";
            // 失敗のままにすると、つないでいる途中の表示に「失敗」と出てしまう
            internet.ClearFailure();
            nextPublicQueryTime = Time.unscaledTime + PublicRetrySeconds;
        }
        else
        {
            publicProblem = string.Empty;
            publicSessions.AddRange(sessions);
        }
    }

    /// <summary>Steam のフレンドのうち、このゲームの部屋にいる人を探す。</summary>
    private async void SearchFriends()
    {
        if (!SteamFriendsService.IsAvailable)
        {
            friendRooms.Clear();
            friendStatus = SteamFriendsService.StatusMessage;
            return;
        }

        searchingFriends = true;
        SteamFriendsService.RequestFriendRooms();
        await System.Threading.Tasks.Task.Delay(FriendReplyWaitMilliseconds);
        searchingFriends = false;

        if (owner == null)
        {
            return;
        }

        friendRooms.Clear();
        friendRooms.AddRange(SteamFriendsService.FindFriendRooms());
        friendStatus = string.Empty;
    }

    /// <summary>一覧のカードを全部作り直し、一覧の下の文字も出し直す。</summary>
    private void RebuildCards(List<Entry> entries)
    {
        foreach (GameObject card in cards)
        {
            if (card != null)
            {
                Object.Destroy(card);
            }
        }
        cards.Clear();

        GameObject content = parts.Object(TitlePartRole.JoinableListContent);
        if (content != null && cardTemplate != null)
        {
            foreach (Entry entry in entries)
            {
                cards.Add(AddCard(content.transform, entry));
            }
        }

        parts.SetText(TitlePartRole.JoinableListMessage, ListMessageOf(entries.Count));
    }

    /// <summary>一覧の下に出す文字（見つからないときの案内と、取れなかった理由）。</summary>
    private string ListMessageOf(int entryCount)
    {
        StringBuilder message = new StringBuilder();
        string search = RoomCodeSearch();
        if (entryCount == 0 && search.Length > 0)
        {
            message.Append($"番号「{search}」に近い部屋は見つかりません。\nプライベートの部屋は、番号を{RoomCodeLength}文字すべて入れると出ます");
        }
        else if (entryCount == 0)
        {
            message.Append(searchPublic || searchFriends
                ? "参加できる部屋が見つかりません。\n1つ前の画面の「クイックマッチ」か「部屋を作る」で遊べます"
                : "パブリック・フレンドのチェックが外れています（LAN の部屋だけを探しています）");
        }

        if (searchPublic && !string.IsNullOrEmpty(publicProblem))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("公開のサーバー：").Append(publicProblem);
        }

        if (searchFriends && !string.IsNullOrEmpty(friendStatus))
        {
            message.Append(message.Length > 0 ? "\n" : string.Empty).Append("フレンドのサーバー：").Append(friendStatus);
        }

        return message.ToString();
    }

    /// <summary>見本のカードを複製して1行ぶんを足し、押したらその部屋に入るようにする。</summary>
    private GameObject AddCard(Transform content, Entry entry)
    {
        GameObject card = Object.Instantiate(cardTemplate, content);
        card.name = $"Server {entry.Name}";
        card.SetActive(true);

        foreach (TitlePart part in card.GetComponentsInChildren<TitlePart>(true))
        {
            Text text = part.GetComponent<Text>();
            switch (part.Role)
            {
                case TitlePartRole.ServerCardName:
                    if (text != null) text.text = entry.Name;
                    break;
                case TitlePartRole.ServerCardPlayers:
                    if (text != null) text.text = entry.Full ? $"{entry.Players} 満員" : entry.Players;
                    break;
                case TitlePartRole.ServerCardCode:
                    if (text != null) text.text = string.IsNullOrEmpty(entry.Code) ? string.Empty : $"#{entry.Code}";
                    break;
                case TitlePartRole.ServerCardLock:
                    part.gameObject.SetActive(entry.Locked);
                    break;
            }
        }

        // 見本のボタンの Action は使わず、この行の Join をつなぐ（満員なら何もつながない）
        foreach (TitleMenuButton look in card.GetComponentsInChildren<TitleMenuButton>(true))
        {
            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            button.onClick.RemoveAllListeners();
            if (!entry.Full && entry.Join != null)
            {
                button.onClick.AddListener(entry.Join);
            }
        }

        return card;
    }
}
