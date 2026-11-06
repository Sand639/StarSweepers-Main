using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// タイトル画面のまとめ役。画面を切り替え、ボタンを押したら各係（部屋を作る・部屋を探す・設定など）へ振り分ける。
/// 詳しい説明は Documents/スクリプトの説明/TitleScene/UI/TitleScreen.md。
/// </summary>
[DisallowMultipleComponent]
public class TitleScreen : MonoBehaviour
{
    public const string TitleSceneName = "TitleScene";
    public const string LobbySceneName = "SpaceJunkLobby";

    /// <summary>Resources の中の画面のプレハブの名前。</summary>
    public const string PrefabResourcePath = "TitleScreen";

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

    private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
    private Page current = Page.Main;
    private bool initialized;

    // 係（Initialize で作る）
    private TitleParts parts;
    private TitleInputFields inputs;
    private TitleTablet tablet;
    private TitleCreateRoom createRoom;
    private TitleJoinDialog joinDialog;
    private TitleRoomList roomList;
    private TitleQuickPlay quickPlay;
    private TitleBusyOverlay busy;
    private TitleSettingsPage settings;

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
        busy.TakeDisconnectReason();
    }

    private void OnDisable()
    {
        IsVisible = false;
        roomList.SetListening(false);
        UiPointer.StickScrollTarget = null;
        tablet.Close();
        ShowCursor();
    }

    private void Update()
    {
        ShowCursor();

        TitleConnection.JoinFromSteamRequest(inputs.JoinPassword);
        busy.Update();

        if (current == Page.FindRoom)
        {
            roomList.Update(quickPlay.IsRunning);
        }

        tablet.Update();

        // つないでいる途中の表示が出ている間は戻らない（表示のボタンで閉じる）
        if (WasBackPressed() && !busy.IsShown)
        {
            GoBack();
        }
    }

    /// <summary>最初に1回だけ、フォントを差し替え、係を作って、ボタンをつなぐ。</summary>
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

        parts = new TitleParts(gameObject);
        AddPage(Page.Main, TitlePartRole.PageMain);
        AddPage(Page.MultiMenu, TitlePartRole.PageMultiMenu);
        AddPage(Page.FindRoom, TitlePartRole.PageMulti);
        AddPage(Page.Create, TitlePartRole.PageCreate);
        AddPage(Page.Settings, TitlePartRole.PageSettings);

        inputs = new TitleInputFields(gameObject);
        tablet = new TitleTablet(gameObject);
        createRoom = new TitleCreateRoom(parts, inputs);
        joinDialog = new TitleJoinDialog(parts, inputs);
        roomList = new TitleRoomList(this, parts, inputs, joinDialog);
        quickPlay = new TitleQuickPlay(this, roomList);
        busy = new TitleBusyOverlay(parts, joinDialog, roomList, quickPlay);
        settings = new TitleSettingsPage(gameObject, parts);

        WireButtons();
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

    private void AddPage(Page page, TitlePartRole role)
    {
        GameObject target = parts.Object(role);
        if (target != null)
        {
            pages[page] = target;
        }
        else
        {
            Debug.LogWarning($"[UI] タイトル画面のプレハブに、画面「{role}」が見つかりません（TitlePart の Role を確かめてください）。");
        }
    }

    /// <summary>ボタンを押したら <see cref="Do"/> に Action が届くようにする。</summary>
    private void WireButtons()
    {
        GameObject cardTemplate = roomList.CardTemplate;
        foreach (TitleMenuButton look in GetComponentsInChildren<TitleMenuButton>(true))
        {
            // 一覧のカードのボタンは、カードを作るときにつなぐ
            if (cardTemplate != null && look.transform.IsChildOf(cardTemplate.transform))
            {
                continue;
            }

            // Button の無い物も、選ばれている見た目のために入れておく
            parts.AddButton(look);

            Button button = look.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            TitleButtonAction action = look.Action;
            button.onClick.AddListener(() => Do(action));
        }
    }

    /// <summary>右下のバージョンの文字の {version} を、Project Settings の Version に置き換える。</summary>
    private void ApplyVersionText()
    {
        Text version = parts.Text(TitlePartRole.VersionText);
        if (version != null)
        {
            version.text = version.text.Replace("{version}", Application.version);
        }
    }

    // ------------------------------------------------------------
    // ボタン
    // ------------------------------------------------------------

    /// <summary>ボタンを押したときの入口。Action ごとに係へ振り分ける。</summary>
    private void Do(TitleButtonAction action)
    {
        switch (action)
        {
            case TitleButtonAction.QuickPlay: quickPlay.Begin(); break;
            case TitleButtonAction.PlaySolo: TitleConnection.StartPractice(); break;
            case TitleButtonAction.OpenMulti: ShowPage(Page.MultiMenu); break;
            case TitleButtonAction.OpenFindRoom: ShowPage(Page.FindRoom); break;
            case TitleButtonAction.OpenCreate: ShowPage(Page.Create); break;
            case TitleButtonAction.OpenSettings: ShowPage(Page.Settings); break;
            case TitleButtonAction.Quit: QuitGame(); break;
            case TitleButtonAction.Back: GoBack(); break;

            case TitleButtonAction.ToggleSearchPublic: roomList.ToggleSearchPublic(); break;
            case TitleButtonAction.ToggleSearchFriends: roomList.ToggleSearchFriends(); break;
            case TitleButtonAction.ToggleSearchNoPassword: roomList.ToggleSearchNoPassword(); break;
            case TitleButtonAction.RefreshRoomList: roomList.RefreshNow(); break;
            case TitleButtonAction.ConfirmJoinDialog: joinDialog.Confirm(); break;
            case TitleButtonAction.CloseJoinDialog: joinDialog.Close(); break;

            case TitleButtonAction.CreateServer: createRoom.Create(); break;
            case TitleButtonAction.MaxPlayersDown: createRoom.ChangeMaxPlayers(-1); break;
            case TitleButtonAction.MaxPlayersUp: createRoom.ChangeMaxPlayers(1); break;
            case TitleButtonAction.SetPublic: createRoom.SetPrivate(false); break;
            case TitleButtonAction.SetPrivate: createRoom.SetPrivate(true); break;
            case TitleButtonAction.SetLan: createRoom.SetLan(); break;

            case TitleButtonAction.TabSound: settings.ShowTab(TitleSettingsPage.Tab.Sound); break;
            case TitleButtonAction.TabGraphics: settings.ShowTab(TitleSettingsPage.Tab.Graphics); break;
            case TitleButtonAction.TabGeneral: settings.ShowTab(TitleSettingsPage.Tab.General); break;
            case TitleButtonAction.ApplyScreen: settings.ApplyScreen(); break;
        }
    }

    // ------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------

    /// <summary>タブレットに出す画面か（マルチプレイ・部屋を探す・部屋を作る）。</summary>
    private static bool IsTabletPage(Page page)
    {
        return page == Page.MultiMenu || page == Page.FindRoom || page == Page.Create;
    }

    /// <summary>画面を切り替え、中身を今の値で出し直し、コントローラーなら最初の項目を選ぶ。</summary>
    private void ShowPage(Page page)
    {
        current = page;
        roomList.SetListening(page == Page.FindRoom);

        // タブレットの画面の間は、後ろにメインの画面も出したままにする
        bool tabletPage = IsTabletPage(page);
        foreach (KeyValuePair<Page, GameObject> pair in pages)
        {
            pair.Value.SetActive(pair.Key == page || (tabletPage && pair.Key == Page.Main));
        }

        pages.TryGetValue(page, out GameObject shown);
        tablet.Show(shown, tabletPage);
        parts.SetSelectedLook(TitleButtonAction.OpenMulti, tabletPage);
        UiPointer.StickScrollTarget = page == Page.FindRoom ? roomList.ScrollRect() : null;

        switch (page)
        {
            case Page.Create:
                createRoom.Refresh();
                break;
            case Page.FindRoom:
                roomList.Open(quickPlay.IsRunning);
                break;
            case Page.Settings:
                settings.Open();
                break;
        }

        if (shown == null)
        {
            return;
        }

        inputs.Refresh(shown);

        // 部屋を探す画面は、部屋があれば一番上の部屋を選ぶ（検索欄から始めると入力に入ってしまうため）
        GameObject firstCard = roomList.FirstCard;
        if (page == Page.FindRoom && firstCard != null)
        {
            TitleParts.SelectForGamepad(firstCard);
        }
        else
        {
            TitleParts.SelectFirstForGamepad(shown);
        }
    }

    /// <summary>1つ前の画面へ戻る（参加の確認の窓が開いていれば、窓を閉じるだけ）。</summary>
    private void GoBack()
    {
        if (joinDialog.IsOpen)
        {
            joinDialog.Close();
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

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

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

    private static void QuitGame()
    {
        Debug.Log("[UI] タイトル画面からゲームを終了します");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
