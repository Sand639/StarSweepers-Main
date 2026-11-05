using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// タイトル画面（メイン・マルチプレイ・部屋を探す・部屋を作る・設定）を動かす。
/// 詳しい説明は Documents/スクリプトの説明/UI/TitleScreen.md。
/// </summary>
[DisallowMultipleComponent]
public class TitleScreen : MonoBehaviour
{
    public const string TitleSceneName = "TitleScene";
    public const string LobbySceneName = "SpaceJunkLobby";

    /// <summary>Resources の中の画面のプレハブの名前。</summary>
    public const string PrefabResourcePath = "TitleScreen";

    private const int ServerNameMaxLength = 25;
    private const int PrivateCodeMaxLength = 12;
    private const int CreateMinPlayers = 2;

    /// <summary>パスワードが長すぎるときの文字数表示の色。</summary>
    private static readonly Color CounterTooLongColor = new Color(1f, 0.45f, 0.4f, 1f);

    /// <summary>タイトル画面が出ているか（ポーズ画面などが見て自分を隠す）。</summary>
    public static bool IsVisible { get; private set; }

    private enum Page
    {
        Main,
        MultiMenu, // マルチプレイ（クイックマッチ・部屋を探す・部屋を作る）
        FindRoom,  // 部屋を探す
        Create,    // 部屋を作る
        Settings,
    }

    private enum SettingsTab
    {
        Sound,
        Graphics,
        General,
    }

    [Tooltip("ON：再生したときに、文字をパソコンの日本語フォントに差し替える（日本語が四角にならないように）")]
    [SerializeField] private bool useOsJapaneseFont = true;

    /// <summary>プレハブの作りの版（作るツールが入れる）。</summary>
    [SerializeField, HideInInspector] private int prefabVersion;

    public int PrefabVersion => prefabVersion;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが版を入れる。</summary>
    public void EditorSetPrefabVersion(int value)
    {
        prefabVersion = value;
    }
#endif

    private readonly Dictionary<TitlePartRole, TitlePart> parts = new Dictionary<TitlePartRole, TitlePart>();
    private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    private readonly Dictionary<SettingsTab, GameObject> settingsGroups = new Dictionary<SettingsTab, GameObject>();

    /// <summary>画面のボタン全部（一覧のカードの見本の中は除く）。</summary>
    private readonly List<TitleMenuButton> buttons = new List<TitleMenuButton>();
    private readonly List<TitleSettingRow> settingRows = new List<TitleSettingRow>();

    private GameObject joinableCardTemplate;
    private readonly List<GameObject> joinableCards = new List<GameObject>();

    /// <summary>前に並べた一覧の中身を1本の文字にしたもの。変わったときだけカードを作り直す（null で必ず作り直す）。</summary>
    private string joinableSignature = string.Empty;

    private bool lanListening;

    /// <summary>ホストに切られて戻ってきたときの理由。出し終わったら空にする。</summary>
    private string disconnectNotice;

    /// <summary>前に出した切られた理由（同じ理由を出し直さないため。シーンをまたいで覚えるので static）。</summary>
    private static string shownDisconnectReason = string.Empty;

    private readonly List<SteamFriendRoom> friendRooms = new List<SteamFriendRoom>();
    private bool searchingFriends;
    private bool initialized;
    private Page current = Page.Main;

    /// <summary>タブレット（マルチプレイ・部屋を探す・部屋を作る）が開いているか。</summary>
    private bool tabletOpen;

    // 部屋を作る。LAN（createLan）／パブリック（どちらも false）／プライベート（createPrivate だけ true）
    private int createMaxPlayers = SpaceJunkTeams.MaxPlayers;
    private bool createLan;
    private bool createPrivate;
    private string createServerName = string.Empty;
    private string createPassword = string.Empty;

    /// <summary>部屋を探す画面の、部屋番号の検索欄の文字。</summary>
    private string privateCode = string.Empty;

    /// <summary>入る部屋のパスワード（参加の確認の窓で入れる）。</summary>
    private string joinPassword = string.Empty;

    /// <summary>一覧用に公開の部屋を取っている途中か。クイックマッチはこれが終わるのを待つ。</summary>
    private bool searching;

    private SettingsTab settingsTab = SettingsTab.Sound;

    /// <summary>選べる解像度（同じ大きさはまとめてある）。</summary>
    private Resolution[] resolutions = new Resolution[0];

    // 選んでいる解像度と画面モード（「適用」を押すまで画面には効かせない）
    private int resolutionIndex;
    private FullScreenMode pendingScreenMode;

    private static readonly FullScreenMode[] ScreenModes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

    // ------------------------------------------------------------
    // 出し入れ
    // ------------------------------------------------------------

    private void Awake()
    {
        Initialize();
    }

    /// <summary>出すたびに、メインの画面から始める。</summary>
    private void OnEnable()
    {
        Initialize();

        IsVisible = true;
        EnsureEventSystem();
        ShowPage(Page.Main);
        ShowCursor();
        TakeDisconnectReason();
    }

    private void OnDisable()
    {
        IsVisible = false;
        SetLanListening(false);
        UiPointer.StickScrollTarget = null;
        tabletOpen = false;
        ShowCursor();
    }

    private void Update()
    {
        ShowCursor();

        JoinFromSteamRequest();
        UpdateBusyOverlay();

        if (current == Page.FindRoom)
        {
            UpdateJoinableList();
        }

        UpdateTabletSlide();
        UpdateTabletHints();

        // つないでいる途中の表示が出ている間は戻らない（表示のボタンで閉じる）
        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        if (WasBackPressed() && (busy == null || !busy.activeSelf))
        {
            GoBack();
        }
    }

    /// <summary>「戻る」が押されたか（Esc／コントローラーの B）。</summary>
    private static bool WasBackPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
               GamepadInput.WasPressed(GamepadButton.East);
    }

    /// <summary>カーソルを画面に閉じ込めない（見え隠れは UiPointer が受け持つ）。</summary>
    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
    }

    // ------------------------------------------------------------
    // 部品を見つけて、つなぐ
    // ------------------------------------------------------------

    /// <summary>最初に1回だけ、フォントを差し替えて部品を集め、ボタン・入力欄・設定をつなぐ。</summary>
    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        if (useOsJapaneseFont)
        {
            ApplyJapaneseFont();
        }

        // CollectParts でカードの見本を見つけてから WireButtons する（見本の中のボタンをつながないため）
        CollectParts();
        WireButtons();
        WireInputs();
        WireSettings();
        ApplyVersionText();
    }

    /// <summary>文字をパソコンの日本語フォントに差し替える。</summary>
    private void ApplyJapaneseFont()
    {
        Font font = UiFont.Find(32);
        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            text.font = font;
        }
    }

    /// <summary>印（TitlePart）の付いた部品を集め、最初は隠しておく部品を隠す。</summary>
    private void CollectParts()
    {
        foreach (TitlePart part in GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role != TitlePartRole.None && !parts.ContainsKey(part.Role))
            {
                parts[part.Role] = part;
            }

            // 操作の案内はタブレットの画面ごとにあるので、全部集める
            if (part.Role == TitlePartRole.TabletHint && part.GetComponent<Text>() != null)
            {
                tabletHints.Add(part.GetComponent<Text>());
            }
        }

        AddPage(Page.Main, TitlePartRole.PageMain);
        AddPage(Page.Create, TitlePartRole.PageCreate);
        AddPage(Page.Settings, TitlePartRole.PageSettings);
        AddPage(Page.FindRoom, TitlePartRole.PageMulti);
        AddPage(Page.MultiMenu, TitlePartRole.PageMultiMenu);

        AddSettingsGroup(SettingsTab.Sound, TitlePartRole.SettingsSound);
        AddSettingsGroup(SettingsTab.Graphics, TitlePartRole.SettingsGraphics);
        AddSettingsGroup(SettingsTab.General, TitlePartRole.SettingsGeneral);

        joinableCardTemplate = PartObject(TitlePartRole.JoinableCardTemplate);
        if (joinableCardTemplate != null)
        {
            joinableCardTemplate.SetActive(false);
        }

        GameObject joinDialog = PartObject(TitlePartRole.JoinDialog);
        if (joinDialog != null)
        {
            joinDialog.SetActive(false);
        }

        GameObject busy = PartObject(TitlePartRole.BusyOverlay);
        if (busy != null)
        {
            busy.SetActive(false);
        }
    }

    private void AddPage(Page page, TitlePartRole role)
    {
        GameObject target = PartObject(role);
        if (target != null)
        {
            pages[page] = target;
        }
        else
        {
            Debug.LogWarning($"[UI] タイトル画面のプレハブに、画面「{role}」が見つかりません（TitlePart の Role を確かめてください）。");
        }
    }

    private void AddSettingsGroup(SettingsTab tab, TitlePartRole role)
    {
        GameObject target = PartObject(role);
        if (target != null)
        {
            settingsGroups[tab] = target;
        }
    }

    /// <summary>ボタンを押したら <see cref="Do"/> に Action が届くようにする。</summary>
    private void WireButtons()
    {
        foreach (TitleMenuButton look in GetComponentsInChildren<TitleMenuButton>(true))
        {
            // 一覧のカードのボタンは、カードを作るときにつなぐ
            if (joinableCardTemplate != null && look.transform.IsChildOf(joinableCardTemplate.transform))
            {
                continue;
            }

            // Button の無い物も、選ばれている見た目のために入れておく
            buttons.Add(look);

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            TitleButtonAction action = look.Action;
            button.onClick.AddListener(() => Do(action));
        }
    }

    /// <summary>入力欄に今の値と文字数の上限を入れ、打ったら値を覚えるようにする。</summary>
    private void WireInputs()
    {
        foreach (TitleInput input in GetComponentsInChildren<TitleInput>(true))
        {
            InputField field = input.Field;
            TitleInputKind kind = input.Kind;
            Text counter = input.Counter;

            field.characterLimit = InputLimitOf(kind);
            field.text = ValueOf(kind);

            field.onValueChanged.AddListener(value =>
            {
                SetValue(kind, value);
                UpdateCounter(kind, counter, value);
            });

            UpdateCounter(kind, counter, field.text);
        }
    }

    private static bool IsPassword(TitleInputKind kind)
    {
        return kind == TitleInputKind.CreatePassword || kind == TitleInputKind.JoinPassword;
    }

    /// <summary>入力欄に打てる文字数（パスワードは長すぎを知らせるため、少し多めに打てる）。</summary>
    private static int InputLimitOf(TitleInputKind kind)
    {
        return IsPassword(kind) ? InternetConnection.PasswordInputLimit : MaxLengthOf(kind);
    }

    /// <summary>文字数表示の、プレハブで決めた色（赤から戻すため）。</summary>
    private readonly Dictionary<Text, Color> counterColors = new Dictionary<Text, Color>();

    /// <summary>入力欄の右端の文字数表示を出し直す（パスワードが長すぎたら注意を赤で出す）。</summary>
    private void UpdateCounter(TitleInputKind kind, Text counter, string value)
    {
        if (counter == null)
        {
            return;
        }

        if (!counterColors.TryGetValue(counter, out Color normal))
        {
            normal = counter.color;
            counterColors[counter] = normal;
        }

        int max = MaxLengthOf(kind);
        bool tooLong = IsPassword(kind) && value.Length > max;

        counter.horizontalOverflow = HorizontalWrapMode.Overflow;
        counter.text = tooLong ? InternetConnection.PasswordTooLongMessage : $"{value.Length}/{max}";
        counter.color = tooLong ? CounterTooLongColor : normal;
    }

    /// <summary>入力欄の最大の文字数。</summary>
    private static int MaxLengthOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                return ServerNameMaxLength;
            case TitleInputKind.PrivateCode:
                return PrivateCodeMaxLength;
            case TitleInputKind.CreatePassword:
            case TitleInputKind.JoinPassword:
                return InternetConnection.PasswordMaxLength;
            default:
                return 0;
        }
    }

    /// <summary>その入力欄に入れる今の値。</summary>
    private string ValueOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                return createServerName;
            case TitleInputKind.PrivateCode:
                return privateCode;
            case TitleInputKind.CreatePassword:
                return createPassword;
            case TitleInputKind.JoinPassword:
                return joinPassword;
            default:
                return string.Empty;
        }
    }

    /// <summary>入力欄に打った値を覚える。</summary>
    private void SetValue(TitleInputKind kind, string value)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                createServerName = value;
                break;
            case TitleInputKind.PrivateCode:
                privateCode = value;
                break;
            case TitleInputKind.CreatePassword:
                createPassword = value;
                break;
            case TitleInputKind.JoinPassword:
                joinPassword = value;
                break;
        }
    }

    /// <summary>その画面の入力欄に、今の値を入れ直す。</summary>
    private void RefreshInputs(GameObject page)
    {
        foreach (TitleInput input in page.GetComponentsInChildren<TitleInput>(true))
        {
            string value = ValueOf(input.Kind);
            if (input.Field.text != value)
            {
                input.Field.SetTextWithoutNotify(value);
                UpdateCounter(input.Kind, input.Counter, value);
            }
        }
    }

    /// <summary>右下のバージョンの文字の {version} を、Project Settings の Version に置き換える。</summary>
    private void ApplyVersionText()
    {
        Text version = PartText(TitlePartRole.VersionText);
        if (version != null)
        {
            version.text = version.text.Replace("{version}", Application.version);
        }
    }

    // ------------------------------------------------------------
    // ボタン
    // ------------------------------------------------------------

    /// <summary>ボタンを押したときの入口。Action ごとに処理を呼び分ける。</summary>
    private void Do(TitleButtonAction action)
    {
        switch (action)
        {
            case TitleButtonAction.QuickPlay: QuickPlay(); break;
            case TitleButtonAction.PlaySolo: StartSolo(); break;
            case TitleButtonAction.OpenMulti: ShowPage(Page.MultiMenu); break;
            case TitleButtonAction.OpenFindRoom: ShowPage(Page.FindRoom); break;
            case TitleButtonAction.OpenCreate: ShowPage(Page.Create); break;
            case TitleButtonAction.OpenSettings: ShowPage(Page.Settings); break;
            case TitleButtonAction.Quit: QuitGame(); break;
            case TitleButtonAction.Back: GoBack(); break;

            case TitleButtonAction.ToggleSearchPublic: SetSearchPublic(!searchPublic); break;
            case TitleButtonAction.ToggleSearchFriends: SetSearchFriends(!searchFriends); break;
            case TitleButtonAction.ToggleSearchNoPassword: SetSearchNoPassword(!searchNoPassword); break;
            case TitleButtonAction.RefreshRoomList: RefreshRoomListNow(); break;
            case TitleButtonAction.ConfirmJoinDialog: ConfirmJoinDialog(); break;
            case TitleButtonAction.CloseJoinDialog: CloseJoinDialog(); break;

            case TitleButtonAction.CreateServer: CreateServer(); break;
            case TitleButtonAction.MaxPlayersDown: ChangeMaxPlayers(-1); break;
            case TitleButtonAction.MaxPlayersUp: ChangeMaxPlayers(1); break;
            case TitleButtonAction.SetPublic: SetCreatePrivate(false); break;
            case TitleButtonAction.SetPrivate: SetCreatePrivate(true); break;
            case TitleButtonAction.SetLan: SetCreateLan(); break;

            case TitleButtonAction.TabSound: ShowSettingsTab(SettingsTab.Sound); break;
            case TitleButtonAction.TabGraphics: ShowSettingsTab(SettingsTab.Graphics); break;
            case TitleButtonAction.TabGeneral: ShowSettingsTab(SettingsTab.General); break;
            case TitleButtonAction.ApplyScreen: ApplyScreen(); break;
        }
    }

    /// <summary>その Action のボタン全部に「選ばれている」見た目を付け外しする。</summary>
    private void SetSelectedLook(TitleButtonAction action, bool selected)
    {
        foreach (TitleMenuButton look in buttons)
        {
            if (look != null && look.Action == action)
            {
                look.SetSelectedLook(selected);
            }
        }
    }

    // ------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------

    /// <summary>画面を切り替え、中身を今の値で出し直し、コントローラーなら最初の項目を選ぶ。</summary>
    private void ShowPage(Page page)
    {
        FinishTabletSlide();
        current = page;
        SetLanListening(page == Page.FindRoom);
        if (page == Page.FindRoom)
        {
            joinableSignature = null;
        }

        // タブレットの画面の間は、後ろにメインの画面も出したままにする
        bool tablet = IsTabletPage(page);
        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page || (tablet && pair.Key == Page.Main));
        }

        // 閉じた状態から開いたときだけ、右から出す
        if (tablet && !tabletOpen)
        {
            StartTabletSlide(pages.TryGetValue(page, out GameObject opened) ? opened : null);
        }
        tabletOpen = tablet;

        SetSelectedLook(TitleButtonAction.OpenMulti, tablet);
        UiPointer.StickScrollTarget = page == Page.FindRoom ? JoinableScrollRect() : null;

        switch (page)
        {
            case Page.Create:
                RefreshCreatePage();
                break;
            case Page.FindRoom:
                RefreshFindRoomPage();
                break;
            case Page.Settings:
                ShowSettingsTab(settingsTab);
                break;
        }

        if (pages.TryGetValue(page, out GameObject shown))
        {
            RefreshInputs(shown);

            // 部屋を探す画面は、部屋があれば一番上の部屋を選ぶ（検索欄から始めると入力に入ってしまうため）
            if (page == Page.FindRoom && joinableCards.Count > 0 && joinableCards[0] != null)
            {
                SelectForGamepad(joinableCards[0]);
            }
            else
            {
                SelectFirstForGamepad(shown);
            }
        }
    }

    // ------------------------------------------------------------
    // タブレット
    // ------------------------------------------------------------

    private const float TabletSlideSeconds = 0.25f;

    /// <summary>出てくる前に右へずらしておく距離（1920×1080 のときのピクセル）。</summary>
    private const float TabletSlideDistance = 900f;

    private RectTransform slidingTablet;
    private float tabletSlideTime;

    /// <summary>タブレットの枠の、ずらす前の位置。</summary>
    private readonly Dictionary<RectTransform, Vector2> tabletHome = new Dictionary<RectTransform, Vector2>();

    private readonly List<Text> tabletHints = new List<Text>();

    /// <summary>操作の案内に今出しているのがコントローラー用か（null はまだ出していない）。</summary>
    private bool? tabletHintForGamepad;

    /// <summary>タブレットに出す画面か（マルチプレイ・部屋を探す・部屋を作る）。</summary>
    private static bool IsTabletPage(Page page)
    {
        return page == Page.MultiMenu || page == Page.FindRoom || page == Page.Create;
    }

    /// <summary>その画面のタブレットの枠を右へずらし、元の位置へ動かし始める。</summary>
    private void StartTabletSlide(GameObject page)
    {
        FinishTabletSlide();

        RectTransform panel = page != null ? TabletPanelOf(page) : null;
        if (panel == null)
        {
            return;
        }

        if (!tabletHome.ContainsKey(panel))
        {
            tabletHome[panel] = panel.anchoredPosition;
        }

        slidingTablet = panel;
        tabletSlideTime = 0f;
        panel.anchoredPosition = tabletHome[panel] + new Vector2(TabletSlideDistance, 0f);
    }

    /// <summary>出てくる途中のタブレットを、少しずつ元の位置へ動かす。</summary>
    private void UpdateTabletSlide()
    {
        if (slidingTablet == null)
        {
            return;
        }

        tabletSlideTime += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(tabletSlideTime / TabletSlideSeconds);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        slidingTablet.anchoredPosition = tabletHome[slidingTablet] + new Vector2(TabletSlideDistance * (1f - eased), 0f);

        if (t >= 1f)
        {
            slidingTablet = null;
        }
    }

    /// <summary>出てくる途中のタブレットを、すぐ元の位置に置く。</summary>
    private void FinishTabletSlide()
    {
        if (slidingTablet != null)
        {
            slidingTablet.anchoredPosition = tabletHome[slidingTablet];
            slidingTablet = null;
        }
    }

    /// <summary>その画面の中のタブレットの枠（印 TabletPanel）。</summary>
    private static RectTransform TabletPanelOf(GameObject page)
    {
        foreach (TitlePart part in page.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role == TitlePartRole.TabletPanel)
            {
                return part.transform as RectTransform;
            }
        }

        return null;
    }

    /// <summary>操作の案内を、最後に触った物（コントローラーかキーボード）に合わせて出し分ける。</summary>
    private void UpdateTabletHints()
    {
        bool gamepad = UiPointer.LastInputWasGamepad && Gamepad.current != null;
        if (tabletHintForGamepad == gamepad)
        {
            return;
        }

        tabletHintForGamepad = gamepad;
        bool playStation = Gamepad.current is DualShockGamepad;
        string text = !gamepad ? "<b>Esc</b>　戻る"
            : playStation ? "<b>×</b>　確認　　<b>○</b>　戻る"
            : "<b>A</b>　確認　　<b>B</b>　戻る";

        foreach (Text hint in tabletHints)
        {
            if (hint != null)
            {
                hint.text = text;
            }
        }
    }

    /// <summary>部屋の一覧のスクロール（右スティックで動かす先）。</summary>
    private ScrollRect JoinableScrollRect()
    {
        GameObject content = PartObject(TitlePartRole.JoinableListContent);
        return content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
    }

    /// <summary>1つ前の画面へ戻る（参加の確認の窓が開いていれば、窓を閉じるだけ）。</summary>
    private void GoBack()
    {
        if (IsJoinDialogOpen())
        {
            CloseJoinDialog();
            return;
        }

        switch (current)
        {
            case Page.MultiMenu:
            case Page.Settings:
                ShowPage(Page.Main);
                break;
            case Page.FindRoom:
            case Page.Create:
                ShowPage(Page.MultiMenu);
                break;
        }
    }

    /// <summary>部屋を作る画面を、今の選び方（人数・公開のしかた）で出し直す。</summary>
    private void RefreshCreatePage()
    {
        SetText(TitlePartRole.MaxPlayersText, $"{createMaxPlayers} 人");

        bool publicRoom = !createLan && !createPrivate;
        bool privateRoom = !createLan && createPrivate;
        SetText(TitlePartRole.PublicCheckText, CheckMark(publicRoom));
        SetText(TitlePartRole.PrivateCheckText, CheckMark(privateRoom));
        SetText(TitlePartRole.LanCheckText, CheckMark(createLan));
        SetSelectedLook(TitleButtonAction.SetPublic, publicRoom);
        SetSelectedLook(TitleButtonAction.SetPrivate, privateRoom);
        SetSelectedLook(TitleButtonAction.SetLan, createLan);

        // パスワードはインターネットの部屋だけ
        GameObject passwordRow = PartObject(TitlePartRole.PasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(!createLan);
        }

        RefreshCodeBox();
    }

    /// <summary>部屋を作る画面の下の枠に、参加する人へ伝えることを出す。</summary>
    private void RefreshCodeBox()
    {
        if (createLan)
        {
            SetText(TitlePartRole.CodeBoxTitle, "このPCのIPアドレス");
            SetText(TitlePartRole.CodeBoxBody, $"{LocalAddresses()}\n参加する人の「部屋を探す」の一覧の一番上に自動で出る");
        }
        else
        {
            SetText(TitlePartRole.CodeBoxTitle, "参加コード");
            SetText(TitlePartRole.CodeBoxBody,
                "サーバーを作成すると発行されます（ロビーの左上に出ます）\n参加する人は「部屋を探す」の「番号入力で検索する」に入れる");
        }
    }

    private static string CheckMark(bool on)
    {
        return on ? "■" : "□";
    }

    private void ChangeMaxPlayers(int delta)
    {
        createMaxPlayers = Mathf.Clamp(createMaxPlayers + delta, CreateMinPlayers, SpaceJunkTeams.MaxPlayers);
        RefreshCreatePage();
    }

    private void SetCreateLan()
    {
        createLan = true;
        RefreshCreatePage();
    }

    /// <summary>パブリック（false）かプライベート（true）を選ぶ。</summary>
    private void SetCreatePrivate(bool value)
    {
        createLan = false;
        createPrivate = value;
        RefreshCreatePage();
    }

    private static void QuitGame()
    {
        Debug.Log("[UI] タイトル画面からゲームを終了します");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------
    // つなぐ
    // ------------------------------------------------------------

    /// <summary>NetworkManager に付いている InternetConnection。無ければ null。</summary>
    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    /// <summary>「サーバーを作成」。LAN ならホストになって部屋を知らせ、インターネットなら部屋を作ってもらう。</summary>
    private void CreateServer()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogError("[UI] NetworkManager が見つかりません。TitleScene に SpaceJunkNetworkManager のプレハブがあるか確かめてください。");
            return;
        }

        string serverName = string.IsNullOrWhiteSpace(createServerName) ? DefaultServerName() : createServerName;

        if (createLan)
        {
            StartLanHost(manager);
            if (manager.IsServer)
            {
                LanRoomDiscovery.StartAdvertising(serverName, createMaxPlayers);
            }
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        internet.HostGame(serverName, createMaxPlayers, createPrivate, createPassword.Trim());
    }

    /// <summary>名前を入れなかったときのサーバーの名前。</summary>
    private static string DefaultServerName()
    {
        return string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "StarSweepers" : $"{GameSettings.PlayerName} のサーバー";
    }

    /// <summary>LAN のホストになる（どのネットワークから来た人も受け付ける）。</summary>
    private static void StartLanHost(NetworkManager manager)
    {
        if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            transport.SetConnectionData("127.0.0.1", transport.ConnectionData.Port, "0.0.0.0");
        }

        manager.StartHost();
    }

    // ------------------------------------------------------------
    // プラクティス・部屋を探す
    // ------------------------------------------------------------

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

    // 検索の設定（■パブリック ■フレンド ■パスワード不要）。最初に部屋を探す画面を開いたときに読む
    private bool searchPublic = true;
    private bool searchFriends = true;
    private bool searchNoPassword = true;
    private bool searchSettingsLoaded;

    private readonly List<ISessionInfo> publicSessions = new List<ISessionInfo>();

    /// <summary>公開の部屋の一覧を取れなかった理由（一覧の下に出す）。</summary>
    private string publicProblem = string.Empty;

    private float nextPublicQueryTime;

    /// <summary>フレンドの部屋を探せない理由（一覧の下に出す）。</summary>
    private string friendStatus = string.Empty;

    private float nextFriendQueryTime;

    /// <summary>部屋の一覧の1行ぶん（LAN・公開・フレンド・番号で入る部屋のどれか）。</summary>
    private struct JoinableEntry
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

    /// <summary>プラクティス。このPCの中だけのホストになる（ロビーへは TitleSceneFlow が移す）。</summary>
    private static void StartSolo()
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

    /// <summary>部屋を探す画面を開いたとき。検索の設定を出し、一覧をすぐ取り直す。</summary>
    private void RefreshFindRoomPage()
    {
        if (!searchSettingsLoaded)
        {
            searchSettingsLoaded = true;
            searchPublic = PlayerPrefs.GetInt(SearchPublicKey, 1) != 0;
            searchFriends = PlayerPrefs.GetInt(SearchFriendsKey, 1) != 0;
            searchNoPassword = PlayerPrefs.GetInt(SearchNoPasswordKey, 1) != 0;
        }

        RefreshSearchChecks();

        nextFriendQueryTime = 0f;
        joinableSignature = null;
        UpdateJoinableList();
    }

    private void RefreshSearchChecks()
    {
        SetText(TitlePartRole.SearchPublicCheckText, CheckMark(searchPublic));
        SetText(TitlePartRole.SearchFriendsCheckText, CheckMark(searchFriends));
        SetText(TitlePartRole.SearchNoPasswordCheckText, CheckMark(searchNoPassword));
        SetSelectedLook(TitleButtonAction.ToggleSearchPublic, searchPublic);
        SetSelectedLook(TitleButtonAction.ToggleSearchFriends, searchFriends);
        SetSelectedLook(TitleButtonAction.ToggleSearchNoPassword, searchNoPassword);
    }

    /// <summary>■パスワード不要 を切り替えて覚え、一覧を並べ直す。</summary>
    private void SetSearchNoPassword(bool value)
    {
        searchNoPassword = value;
        PlayerPrefs.SetInt(SearchNoPasswordKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        joinableSignature = null;
    }

    /// <summary>一覧の ⟳。公開とフレンドの部屋をすぐ取り直す。</summary>
    private void RefreshRoomListNow()
    {
        nextPublicQueryTime = 0f;
        nextFriendQueryTime = 0f;
        joinableSignature = null;
    }

    /// <summary>検索欄の文字（前後の空白と「#」を除き、大文字にそろえる）。</summary>
    private string RoomCodeSearch()
    {
        return (privateCode ?? string.Empty).Trim().TrimStart('#').Trim().ToUpperInvariant();
    }

    /// <summary>■パブリック を切り替えて覚え、一覧を並べ直す。</summary>
    private void SetSearchPublic(bool value)
    {
        searchPublic = value;
        PlayerPrefs.SetInt(SearchPublicKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        joinableSignature = null;
    }

    /// <summary>■フレンド を切り替えて覚え、フレンドの部屋をすぐ探し直す。</summary>
    private void SetSearchFriends(bool value)
    {
        searchFriends = value;
        PlayerPrefs.SetInt(SearchFriendsKey, value ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSearchChecks();
        nextFriendQueryTime = 0f;
        joinableSignature = null;
    }

    /// <summary>部屋の一覧を間隔をあけて取り直し、中身が変わったときだけ並べ直す（毎フレーム呼ぶ）。</summary>
    private void UpdateJoinableList()
    {
        float now = Time.unscaledTime;

        // クイックマッチの途中は、インターネットの係を取り合わないように取らない
        if (searchPublic && !searching && !quickPlaying && now >= nextPublicQueryTime)
        {
            nextPublicQueryTime = now + PublicRefreshSeconds;
            QueryPublicForList();
        }

        if (searchFriends && !searchingFriends && now >= nextFriendQueryTime)
        {
            nextFriendQueryTime = now + FriendRefreshSeconds;
            SearchFriendsForList();
        }

        List<JoinableEntry> entries = CollectJoinable();
        string signature = JoinableSignatureOf(entries);
        if (signature == joinableSignature)
        {
            return;
        }

        joinableSignature = signature;
        RebuildJoinableCards(entries);
    }

    /// <summary>一覧に出るもの全部を1本の文字にする（前と同じなら並べ直さない）。</summary>
    private string JoinableSignatureOf(List<JoinableEntry> entries)
    {
        StringBuilder signature = new StringBuilder();
        signature.Append(searchPublic).Append(searchFriends).Append(searchNoPassword).Append('|').Append(RoomCodeSearch())
            .Append('|').Append(publicProblem).Append('|').Append(friendStatus).Append('|');
        foreach (JoinableEntry entry in entries)
        {
            signature.Append(entry.Key).Append(',').Append(entry.Name).Append(',').Append(entry.Code).Append(',')
                .Append(entry.Players).Append(entry.Full).Append(entry.Locked).Append(';');
        }

        return signature.ToString();
    }

    /// <summary>一覧に並べる行を集める（LAN → 公開 → フレンドの順。検索欄があれば番号で絞る）。</summary>
    private List<JoinableEntry> CollectJoinable()
    {
        List<JoinableEntry> all = new List<JoinableEntry>();
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
        List<JoinableEntry> startsWith = new List<JoinableEntry>();
        List<JoinableEntry> contains = new List<JoinableEntry>();
        bool exactFound = false;
        foreach (JoinableEntry entry in all)
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
    private JoinableEntry PrivateCodeEntry(string code)
    {
        string roomName = $"部屋番号 #{code}";
        return new JoinableEntry
        {
            Key = $"code:{code}",
            Name = "プライベートの部屋（この番号で入ってみる）",
            Code = code,
            Players = "?",
            Full = false,
            Locked = false,
            Join = () => OpenJoinDialog(roomName, false, password =>
            {
                InternetConnection internet = Internet();
                if (internet != null)
                {
                    internet.JoinGame(code, password);
                }
            }, true),
        };
    }

    /// <summary>LAN の部屋の行（押すと確認なしでつなぐ）。</summary>
    private void AddLanEntries(List<JoinableEntry> entries)
    {
        foreach (LanRoomDiscovery.Room room in LanRoomDiscovery.Rooms)
        {
            string address = room.Address;
            ushort port = room.Port;
            entries.Add(new JoinableEntry
            {
                Key = $"lan:{room.Address}:{room.Port}",
                Name = $"【LAN】{room.Name}",
                Players = $"{room.Players}/{room.MaxPlayers}",
                Full = room.Players >= room.MaxPlayers,
                Join = () => JoinLanRoom(address, port),
            });
        }
    }

    /// <summary>公開の部屋の行（押すと参加の確認の窓を出す）。</summary>
    private void AddPublicEntries(List<JoinableEntry> entries)
    {
        foreach (ISessionInfo info in publicSessions)
        {
            string sessionId = info.Id;
            string roomName = string.IsNullOrEmpty(info.Name) ? "（名前なし）" : info.Name;
            bool hasPassword = info.HasPassword;
            entries.Add(new JoinableEntry
            {
                Key = $"pub:{info.Id}",
                Name = roomName,
                Code = InternetConnection.RoomCodeOf(info),
                Locked = hasPassword,
                Players = $"{PlayersIn(info)}/{info.MaxPlayers}",
                Full = info.AvailableSlots <= 0 || info.IsLocked,
                Join = () => OpenJoinDialog(roomName, hasPassword, password =>
                {
                    InternetConnection internet = Internet();
                    if (internet != null)
                    {
                        internet.JoinGameById(sessionId, password);
                    }
                }),
            });
        }
    }

    /// <summary>Steam のフレンドの部屋の行（満員かは分からないので、いつも押せる）。</summary>
    private void AddFriendEntries(List<JoinableEntry> entries)
    {
        foreach (SteamFriendRoom room in friendRooms)
        {
            string code = room.JoinCode;
            string roomName = $"{room.FriendName} の部屋";
            bool hasPassword = room.HasPassword;
            entries.Add(new JoinableEntry
            {
                Key = $"friend:{room.JoinCode}",
                Name = $"【フレンド】{room.FriendName} の部屋（{room.RoomName}）",
                Code = code,
                Locked = hasPassword,
                Players = room.Players,
                Full = false,
                Join = () => OpenJoinDialog(roomName, hasPassword, password =>
                {
                    InternetConnection internet = Internet();
                    if (internet != null)
                    {
                        internet.JoinGame(code, password);
                    }
                }),
            });
        }
    }

    /// <summary>公開の部屋に今いる人数。</summary>
    private static int PlayersIn(ISessionInfo info)
    {
        return Mathf.Max(0, info.MaxPlayers - info.AvailableSlots);
    }

    // ------------------------------------------------------------
    // 参加の確認の窓
    // ------------------------------------------------------------

    /// <summary>「はい」「参加する」で呼ぶ処理（パスワードを渡す）。</summary>
    private System.Action<string> pendingJoin;

    private bool pendingNeedsPassword;

    /// <summary>窓から入ろうとして、結果を待っているか（失敗したら窓に理由を出す）。</summary>
    private bool joinDialogWaiting;

    /// <summary>パスワードがあるか入ってみるまで分からない部屋か（番号で入る部屋）。</summary>
    private bool pendingPasswordUnknown;

    private string pendingRoomName = string.Empty;

    private bool IsJoinDialogOpen()
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        return dialog != null && dialog.activeSelf;
    }

    /// <summary>参加の確認の窓を出す（パスワードのある部屋なら、パスワードを聞く形）。</summary>
    private void OpenJoinDialog(string roomName, bool needsPassword, System.Action<string> join, bool passwordUnknown = false)
    {
        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog == null)
        {
            return;
        }

        pendingJoin = join;
        pendingRoomName = roomName;
        pendingPasswordUnknown = passwordUnknown;
        joinDialogWaiting = false;

        SetJoinDialogMode(needsPassword);
        SetText(TitlePartRole.JoinDialogMessage, string.Empty);

        dialog.SetActive(true);

        foreach (TitleMenuButton look in dialog.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.ConfirmJoinDialog)
            {
                SelectForGamepad(look.gameObject);
                break;
            }
        }
    }

    /// <summary>窓を、パスワードを聞く形（true）か「参加しますか？」の形（false）にする。</summary>
    private void SetJoinDialogMode(bool needsPassword)
    {
        pendingNeedsPassword = needsPassword;

        SetText(TitlePartRole.JoinDialogTitle, needsPassword
            ? $"「{pendingRoomName}」\nパスワードを入力してください"
            : $"「{pendingRoomName}」に参加しますか？");
        SetText(TitlePartRole.JoinDialogYesLabel, needsPassword ? "参加する" : "はい");
        SetText(TitlePartRole.JoinDialogNoLabel, needsPassword ? "やめる" : "いいえ");

        GameObject passwordRow = PartObject(TitlePartRole.JoinDialogPasswordRow);
        if (passwordRow != null)
        {
            passwordRow.SetActive(needsPassword);
        }

        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog != null)
        {
            RefreshInputs(dialog);
        }
    }

    /// <summary>「はい」「参加する」。部屋に入りに行く（結果は UpdateBusyOverlay が窓に出す）。</summary>
    private void ConfirmJoinDialog()
    {
        if (pendingJoin == null)
        {
            CloseJoinDialog();
            return;
        }

        string password = pendingNeedsPassword ? joinPassword.Trim() : string.Empty;
        if (pendingNeedsPassword && password.Length == 0)
        {
            SetText(TitlePartRole.JoinDialogMessage, "パスワードを入力してください");
            return;
        }

        InternetConnection internet = Internet();
        if (internet != null && internet.IsBusy)
        {
            SetText(TitlePartRole.JoinDialogMessage, "サーバーの一覧を取っています。少し待ってから、もう一度押してください");
            return;
        }

        SetText(TitlePartRole.JoinDialogMessage, string.Empty);
        joinDialogWaiting = true;
        pendingJoin(password);
    }

    /// <summary>「いいえ」「やめる」。窓を閉じる。</summary>
    private void CloseJoinDialog()
    {
        pendingJoin = null;
        joinDialogWaiting = false;

        GameObject dialog = PartObject(TitlePartRole.JoinDialog);
        if (dialog != null)
        {
            dialog.SetActive(false);
        }
    }

    /// <summary>一覧に並べる公開の部屋を取る（取れなかったら少し待つ）。</summary>
    private async void QueryPublicForList()
    {
        InternetConnection internet = Internet();
        if (internet == null)
        {
            publicProblem = "インターネットの接続の部品が見つかりません";
            return;
        }

        if (internet.IsBusy)
        {
            return;
        }

        searching = true;
        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        searching = false;

        // 待っている間に画面が消えた（Unity では消えた物も == null になる）
        if (this == null)
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
    private async void SearchFriendsForList()
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

        if (this == null)
        {
            return;
        }

        friendRooms.Clear();
        friendRooms.AddRange(SteamFriendsService.FindFriendRooms());
        friendStatus = string.Empty;
    }

    /// <summary>一覧のカードを全部作り直し、一覧の下の文字も出し直す。</summary>
    private void RebuildJoinableCards(List<JoinableEntry> entries)
    {
        foreach (GameObject card in joinableCards)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }
        joinableCards.Clear();

        GameObject content = PartObject(TitlePartRole.JoinableListContent);
        if (content != null && joinableCardTemplate != null)
        {
            foreach (JoinableEntry entry in entries)
            {
                joinableCards.Add(AddJoinableCard(content.transform, entry));
            }
        }

        SetText(TitlePartRole.JoinableListMessage, JoinableListMessageOf(entries.Count));
    }

    /// <summary>一覧の下に出す文字（見つからないときの案内と、取れなかった理由）。</summary>
    private string JoinableListMessageOf(int entryCount)
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
    private GameObject AddJoinableCard(Transform content, JoinableEntry entry)
    {
        GameObject card = Instantiate(joinableCardTemplate, content);
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

    // ------------------------------------------------------------
    // クイックマッチ
    // ------------------------------------------------------------

    private bool quickPlaying;

    /// <summary>次の部屋を試すまでの間に出す文（その間に表示が消えてちらつかないため）。</summary>
    private string quickPlayNotice;

    private const int QuickPlayWaitStepMilliseconds = 100;

    /// <summary>クイックマッチ。入れる公開の部屋に人の多い順に入り、無ければ部屋を作る。</summary>
    private async void QuickPlay()
    {
        if (quickPlaying)
        {
            return;
        }

        InternetConnection internet = Internet();
        if (internet == null)
        {
            Debug.LogError("[UI] InternetConnection が NetworkManager に付いていません。");
            return;
        }

        quickPlaying = true;
        try
        {
            await QuickPlayAsync(internet);
        }
        finally
        {
            quickPlaying = false;
            quickPlayNotice = null;
        }
    }

    /// <summary><see cref="QuickPlay"/> の中身。</summary>
    private async System.Threading.Tasks.Task QuickPlayAsync(InternetConnection internet)
    {
        // 一覧を取っている途中なら終わるまで待つ（インターネットの係は1つのことしかできない）
        while (searching && this != null)
        {
            await System.Threading.Tasks.Task.Delay(QuickPlayWaitStepMilliseconds);
        }

        if (this == null || internet.IsBusy)
        {
            return;
        }

        List<ISessionInfo> sessions = await internet.QueryPublicSessionsAsync();
        if (this == null || sessions == null)
        {
            return;
        }

        foreach (ISessionInfo info in OpenSessionsFullestFirst(sessions))
        {
            Debug.Log($"[UI] クイックプレイ：公開のサーバー「{info.Name}」（{PlayersIn(info)}/{info.MaxPlayers}）に入ります。");
            if (await internet.JoinGameByIdAsync(info.Id, string.Empty, true) || this == null)
            {
                return;
            }

            // つなぐ準備から失敗した（ほかの部屋でも同じなので、やめる）
            if (internet.State == InternetConnection.Phase.Failed)
            {
                return;
            }

            quickPlayNotice = "部屋に入れなかったので、ほかの部屋を探しています…";
        }

        string serverName = DefaultServerName();

        Debug.Log($"[UI] クイックプレイ：入れる公開のサーバーが無いので、「{serverName}」を作ります。");
        internet.HostGame(serverName, SpaceJunkTeams.MaxPlayers, false, string.Empty);
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
        return open.OrderByDescending(info => PlayersIn(info)).ToList();
    }

    // ------------------------------------------------------------
    // LAN の部屋
    // ------------------------------------------------------------

    /// <summary>LAN の部屋の知らせを聞き始める・やめる。</summary>
    private void SetLanListening(bool listen)
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

    /// <summary>一覧で選んだ LAN の部屋に入る。</summary>
    private void JoinLanRoom(string address, ushort port)
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

    // ------------------------------------------------------------
    // ホストに切られて戻ってきたとき
    // ------------------------------------------------------------

    /// <summary>ホストに切られて戻ってきたなら、その理由を出す用に取っておく。</summary>
    private void TakeDisconnectReason()
    {
        NetworkManager manager = NetworkManager.Singleton;
        string reason = manager != null ? manager.DisconnectReason : string.Empty;
        if (string.IsNullOrEmpty(reason) || reason == shownDisconnectReason)
        {
            return;
        }

        shownDisconnectReason = reason;

        // 「[」で始まるのは Netcode が自分で付ける記録（自分で止めたときにも付く）なので出さない
        if (reason.StartsWith("["))
        {
            return;
        }

        disconnectNotice = $"部屋に入れませんでした：{reason}";
    }

    // ------------------------------------------------------------
    // Steam から頼まれた参加・つないでいる途中の表示
    // ------------------------------------------------------------

    /// <summary>Steam のフレンド一覧の「ゲームに参加」で頼まれた部屋に入る。</summary>
    private void JoinFromSteamRequest()
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
        internet.JoinGame(code, joinPassword.Trim());
    }

    /// <summary>つないでいる途中・失敗したときの表示を出し入れする。</summary>
    private void UpdateBusyOverlay()
    {
        GameObject overlay = PartObject(TitlePartRole.BusyOverlay);
        if (overlay == null)
        {
            return;
        }

        ChooseBusyContent(Internet(), NetworkManager.Singleton,
            out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action);
        ShowBusyOverlay(overlay, message, buttonLabel, action);
    }

    /// <summary>つないでいる途中の表示に何を出すかを決める（出さないときは message が null）。</summary>
    private void ChooseBusyContent(InternetConnection internet, NetworkManager manager,
        out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action)
    {
        // LAN でつなぎに行っている途中（インターネットで入る途中も同じ形になるので除く）
        bool lanConnecting = manager != null && manager.IsListening && !manager.IsServer && !manager.IsConnectedClient &&
                             (internet == null || internet.State != InternetConnection.Phase.Joining);

        message = null;
        buttonLabel = null;
        action = null;

        if (IsConnected())
        {
            message = "つながりました。ロビーに移動しています…";
        }
        // 一覧を取っている間は出さない
        else if (internet != null && internet.IsBusy && !searching)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        else if (quickPlayNotice != null)
        {
            message = quickPlayNotice;
        }
        // 参加の確認の窓から入って失敗したら、表示ではなく窓の中に理由を出す
        else if (internet != null && internet.State == InternetConnection.Phase.Failed && joinDialogWaiting)
        {
            joinDialogWaiting = false;
            if (internet.LastJoinFailedByPassword && pendingPasswordUnknown && !pendingNeedsPassword)
            {
                SetJoinDialogMode(true);
                SetText(TitlePartRole.JoinDialogMessage, "この部屋にはパスワードがかかっています");
            }
            else
            {
                SetText(TitlePartRole.JoinDialogMessage, internet.LastJoinFailedByPassword
                    ? "パスワードが間違えています"
                    : internet.Message);
            }
            internet.ClearFailure();
        }
        else if (internet != null && internet.State == InternetConnection.Phase.Failed)
        {
            message = internet.Message;
            buttonLabel = "戻る";
            action = () => internet.ClearFailure();
        }
        else if (!string.IsNullOrEmpty(disconnectNotice) && (manager == null || !manager.IsListening))
        {
            message = disconnectNotice;
            buttonLabel = "OK";
            action = () => disconnectNotice = null;
        }
        else if (lanConnecting)
        {
            string address = manager.NetworkConfig.NetworkTransport is UnityTransport transport ? transport.ConnectionData.Address : "?";
            message = $"LAN のサーバー（{address}）につないでいます…\nつながらないときは、IPアドレスと、同じネットワークにいるかを確かめてください。";
            buttonLabel = "やめる";
            action = () => manager.Shutdown();
        }
    }

    /// <summary>つないでいる途中の表示を出し入れし、文字とボタンを入れ替える。</summary>
    private void ShowBusyOverlay(GameObject overlay, string message, string buttonLabel, UnityEngine.Events.UnityAction action)
    {
        Button busyButton = PartComponent<Button>(TitlePartRole.BusyButton);

        bool show = message != null;
        if (overlay.activeSelf != show)
        {
            overlay.SetActive(show);

            // コントローラーでボタンを選ぶのは、出した瞬間だけ
            if (show && buttonLabel != null && busyButton != null)
            {
                SelectForGamepad(busyButton.gameObject);
            }
        }

        if (!show)
        {
            return;
        }

        SetText(TitlePartRole.BusyMessage, message);

        if (busyButton != null)
        {
            busyButton.gameObject.SetActive(buttonLabel != null);
            busyButton.onClick.RemoveAllListeners();
            if (action != null)
            {
                busyButton.onClick.AddListener(action);
                SetText(TitlePartRole.BusyButtonLabel, buttonLabel);
            }
        }
    }

    /// <summary>つながっているか（ホストとして動いている／参加者としてつながり終わった）。</summary>
    private static bool IsConnected()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && (manager.IsServer || manager.IsConnectedClient);
    }

    // ------------------------------------------------------------
    // 設定
    // ------------------------------------------------------------

    /// <summary>設定の行のスライダーと「＜」「＞」に処理をつなぐ。</summary>
    private void WireSettings()
    {
        resolutions = UniqueResolutions();

        foreach (TitleSettingRow row in GetComponentsInChildren<TitleSettingRow>(true))
        {
            settingRows.Add(row);
            TitleSettingRow target = row;

            if (row.Slider != null)
            {
                SetupSliderRange(row);
                row.Slider.onValueChanged.AddListener(value =>
                {
                    SetSliderValue(target.Kind, value);
                    RefreshSettingRow(target);
                });
            }

            if (row.Previous != null)
            {
                row.Previous.onClick.AddListener(() =>
                {
                    Step(target.Kind, -1);
                    RefreshSettingRow(target);
                });
            }

            if (row.Next != null)
            {
                row.Next.onClick.AddListener(() =>
                {
                    Step(target.Kind, 1);
                    RefreshSettingRow(target);
                });
            }
        }
    }

    /// <summary>設定のタブを切り替え、全部の行を今の値で出し直す。</summary>
    private void ShowSettingsTab(SettingsTab tab)
    {
        settingsTab = tab;

        foreach (KeyValuePair<SettingsTab, GameObject> pair in settingsGroups)
        {
            pair.Value.SetActive(pair.Key == tab);
        }

        SetSelectedLook(TitleButtonAction.TabSound, tab == SettingsTab.Sound);
        SetSelectedLook(TitleButtonAction.TabGraphics, tab == SettingsTab.Graphics);
        SetSelectedLook(TitleButtonAction.TabGeneral, tab == SettingsTab.General);

        // まだ「適用」していない解像度・画面モードの選択は、今の画面の値に戻る
        resolutionIndex = Mathf.Max(0, FindCurrentResolution(resolutions));
        pendingScreenMode = Screen.fullScreenMode;

        foreach (TitleSettingRow row in settingRows)
        {
            RefreshSettingRow(row);
        }
    }

    /// <summary>スライダーの端の値を決める（プレハブの値を上書きする）。</summary>
    private static void SetupSliderRange(TitleSettingRow row)
    {
        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                row.Slider.minValue = 0f;
                row.Slider.maxValue = 1f;
                break;
            case TitleSettingKind.MouseSensitivity:
                row.Slider.minValue = 0.2f;
                row.Slider.maxValue = 3f;
                break;
            case TitleSettingKind.FrameRateLimit:
                row.Slider.minValue = 30f;
                row.Slider.maxValue = 240f;
                break;
        }
    }

    /// <summary>スライダーを動かした値を GameSettings に入れる（すぐ効く）。</summary>
    private static void SetSliderValue(TitleSettingKind kind, float value)
    {
        switch (kind)
        {
            case TitleSettingKind.Volume:
                GameSettings.Volume = value;
                break;
            case TitleSettingKind.MouseSensitivity:
                GameSettings.MouseSensitivity = value;
                break;
            case TitleSettingKind.FrameRateLimit:
                GameSettings.FrameRateLimit = Mathf.RoundToInt(value / 10f) * 10;
                break;
        }
    }

    /// <summary>「＜」（-1）「＞」（+1）で値を1つ進める（端まで行ったら反対の端へ回る）。</summary>
    private void Step(TitleSettingKind kind, int delta)
    {
        switch (kind)
        {
            case TitleSettingKind.ControllerType:
                GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
                    ? ControllerOperationType.TypeB
                    : ControllerOperationType.TypeA;
                break;

            case TitleSettingKind.Resolution:
                if (resolutions.Length > 0)
                {
                    resolutionIndex = (resolutionIndex + delta + resolutions.Length) % resolutions.Length;
                }
                break;

            case TitleSettingKind.ScreenMode:
                // 選べる3つに無い画面モードのときは、先頭から
                int index = System.Array.IndexOf(ScreenModes, pendingScreenMode);
                index = index < 0 ? 0 : (index + delta + ScreenModes.Length) % ScreenModes.Length;
                pendingScreenMode = ScreenModes[index];
                break;

            case TitleSettingKind.VSync:
                GameSettings.VSync = !GameSettings.VSync;
                break;

            case TitleSettingKind.Quality:
                string[] qualities = QualitySettings.names;
                if (qualities.Length > 0)
                {
                    GameSettings.QualityLevel = (QualitySettings.GetQualityLevel() + delta + qualities.Length) % qualities.Length;
                }
                break;
        }
    }

    /// <summary>設定の1行に、今の値を出す。</summary>
    private void RefreshSettingRow(TitleSettingRow row)
    {
        string text;
        float? sliderValue = null;

        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                sliderValue = GameSettings.Volume;
                text = $"{Mathf.RoundToInt(GameSettings.Volume * 100f)} %";
                break;
            case TitleSettingKind.MouseSensitivity:
                sliderValue = GameSettings.MouseSensitivity;
                text = $"{GameSettings.MouseSensitivity:0.0} 倍";
                break;
            case TitleSettingKind.FrameRateLimit:
                sliderValue = GameSettings.FrameRateLimit;
                text = $"{GameSettings.FrameRateLimit} fps";
                break;
            case TitleSettingKind.ControllerType:
                text = GameSettings.ControllerOperation == ControllerOperationType.TypeB ? "タイプB" : "タイプA";
                break;
            case TitleSettingKind.Resolution:
                text = resolutions.Length == 0
                    ? "—"
                    : $"{resolutions[resolutionIndex].width}x{resolutions[resolutionIndex].height}";
                break;
            case TitleSettingKind.ScreenMode:
                text = ScreenModeName(pendingScreenMode);
                break;
            case TitleSettingKind.VSync:
                text = GameSettings.VSync ? "ON" : "OFF";
                break;
            default: // 画質
                string[] qualities = QualitySettings.names;
                text = qualities.Length == 0
                    ? "—"
                    : qualities[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualities.Length - 1)];
                break;
        }

        if (row.ValueText != null)
        {
            row.ValueText.text = text;
        }

        if (sliderValue.HasValue && row.Slider != null)
        {
            row.Slider.SetValueWithoutNotify(sliderValue.Value);
        }
    }

    /// <summary>「解像度／スクリーンモードを適用」。選んだ解像度と画面モードを画面に効かせる。</summary>
    private void ApplyScreen()
    {
        if (resolutions.Length > 0)
        {
            Resolution chosen = resolutions[resolutionIndex];
            Screen.SetResolution(chosen.width, chosen.height, pendingScreenMode);
        }
        else
        {
            Screen.fullScreenMode = pendingScreenMode;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>その印（Role）の付いた部品。無ければ null。</summary>
    private GameObject PartObject(TitlePartRole role)
    {
        return parts.TryGetValue(role, out TitlePart part) && part != null ? part.gameObject : null;
    }

    private T PartComponent<T>(TitlePartRole role) where T : Component
    {
        GameObject target = PartObject(role);
        return target != null ? target.GetComponent<T>() : null;
    }

    private Text PartText(TitlePartRole role)
    {
        return PartComponent<Text>(role);
    }

    /// <summary>その印の付いた文字を書き換える（部品が無ければ何もしない）。</summary>
    private void SetText(TitlePartRole role, string value)
    {
        Text text = PartText(role);
        if (text != null)
        {
            text.text = value;
        }
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

    /// <summary>同じ大きさの解像度を1つにまとめた一覧。</summary>
    private static Resolution[] UniqueResolutions()
    {
        List<Resolution> list = new List<Resolution>();
        foreach (Resolution r in Screen.resolutions)
        {
            if (!list.Exists(x => x.width == r.width && x.height == r.height))
            {
                list.Add(r);
            }
        }
        return list.ToArray();
    }

    /// <summary>今の画面の大きさが一覧の何番目か（無ければ最後、一覧が空なら -1）。</summary>
    private static int FindCurrentResolution(Resolution[] list)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].width == Screen.width && list[i].height == Screen.height)
            {
                return i;
            }
        }
        return list.Length - 1;
    }

    private static string ScreenModeName(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.ExclusiveFullScreen:
                return "フルスクリーン";
            case FullScreenMode.FullScreenWindow:
                return "ウィンドウフルスクリーン";
            case FullScreenMode.MaximizedWindow:
                return "最大化ウィンドウ";
            default:
                return "ウィンドウ";
        }
    }

    /// <summary>コントローラーがあれば、その画面の最初の項目を選ぶ（入力欄はほかに無いときだけ）。</summary>
    private static void SelectFirstForGamepad(GameObject page)
    {
        if (Gamepad.current == null || page == null || EventSystem.current == null)
        {
            return;
        }

        Selectable first = null;
        foreach (Selectable selectable in page.GetComponentsInChildren<Selectable>())
        {
            if (!(selectable is InputField))
            {
                first = selectable;
                break;
            }

            first = first != null ? first : selectable;
        }

        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    /// <summary>コントローラーがあれば、その項目を選ぶ。</summary>
    private static void SelectForGamepad(GameObject target)
    {
        if (Gamepad.current != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
        }
    }

    /// <summary>UI のクリックを受け取る EventSystem が無ければ作る（シーンが変わっても残す）。</summary>
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
