using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **Escape で開くポーズ画面。**
///
/// | 項目 | 押すとどうなるか |
/// | --- | --- |
/// | **ゲームをつづける** | 画面を閉じて、続きから遊ぶ |
/// | **設定** | 音量・マウス感度・操作タイプ・レティクル・投げ方・プレイヤーの体を変える画面へ（下の表） |
/// | **ロビーに戻る** | **宇宙ごみの試合中だけ出る。** 確かめてから試合を抜けてロビーへ（ホストなら全員で戻る）。`SpaceJunkLeaveMatch` |
/// | **タイトルにもどる** | **つながっている間（ロビーでも）出る。** 確かめてからタイトルへ（ホストなら全員がタイトルへ）。`SpaceJunkLeaveMatch` |
/// | **ゲームをやめる** | 確かめてから、ゲームを終了する（エディタでは再生を止める） |
///
/// **マウスで選ぶ。** 項目に重なると**色が変わり、少し大きくなり、左に「▶」が出る**
/// （<see cref="MenuHoverHighlight"/>）。
///
/// ## 設定の画面
///
/// 値は <see cref="GameSettings"/> に入れる（PCに保存され、次に遊ぶときも残る）。
///
/// | 項目 | 変わるもの | 使う側 |
/// | --- | --- | --- |
/// | **音量**（スライダー） | ゲーム全体の音の大きさ（0〜1） | <see cref="GameSettings.Volume"/> |
/// | **マウス感度**（スライダー） | 視点を回す速さの倍率（0.2〜3） | カメラ |
/// | **操作タイプ：タイプA／B** | コントローラーの移動・向きの操作と、フックの引き寄せ方 | FishingPlayerController・PlayerAimController・ThrowController |
/// | **レティクル：タイプA／B** | 溜めている間の狙いの見せ方 | HookChargeStrip |
/// | **投げ方：左右で使い分け／ZRワンボタン** | 宇宙ごみ式のフックの撃ち方・投げ方 | HookController・ThrowController |
///
/// 切り替えの項目は、押すたびにもう片方へ変わり、**設定の画面を作り直して**新しい文字を見せる
/// （<see cref="BuildSettingsPageRefresh"/>）。
///
/// ## コントローラーで戻る（B ボタン）
///
/// 確かめる画面・設定の画面 → 最初の画面へ。最初の画面 → 閉じる。
///
/// ## 画面はコードで組み立てている
///
/// **プレハブに画面を作り込んでいない。** 再生したときに、この部品が自分で作る。
///
/// 理由は**日本語のため。** Unityに最初から入っている文字は日本語を持っていないので、
/// そのままだと項目名が四角（□□□）になってしまう。
/// **パソコンに入っている日本語フォントを、再生したときに借りてくる**必要があり、
/// それは保存できない（プレハブに残らない）ので、作るところまで含めてコードにしてある。
///
/// ## 置き方
///
/// **空のゲームオブジェクトにこの部品を付けるだけ。**
/// カメラもUIも要らない（足りない物はこちらで作る）。
/// メニューの `Tools > StarSweepers > アーカイブ > 共通の部品 > ポーズ画面を置く` でも置ける。
///
/// **置き忘れても動く。** シーンのどこにも無ければ、再生したときに自分で1つ作る
/// （<see cref="AutoCreate"/>）。
/// </summary>
// Escape を**他の部品より先に**受け取る。
// カメラ側も Escape を見ているので、順番が決まっていないと取り合いになる
[DefaultExecutionOrder(-100)]
public partial class PauseMenu : MonoBehaviour
{
    [Header("キーの割り当て")]
    [Tooltip("ポーズ画面を開く／閉じるキー")]
    [SerializeField] private Key pauseKey = Key.Escape;

    [Tooltip("ポーズ画面を開く／閉じる、コントローラーのボタン（Xbox の Start）")]
    [SerializeField] private GamepadButton pauseButton = GamepadButton.Start;

    [Tooltip("ひとつ前へ戻る、コントローラーのボタン（East＝Xbox の B）。設定の画面なら最初の画面へ、最初の画面なら閉じる")]
    [SerializeField] private GamepadButton backButton = GamepadButton.East;

    [Tooltip("ONだと、開いている間は**世界の時間も止まる**（1人用）。\n" +
             "**オンラインのシーンでは OFF にすること。** 自分のPCだけ時間を止めても" +
             "他の人は動き続けるので、閉じた瞬間に相手が飛んで見える")]
    [SerializeField] private bool freezeTime = true;

    [Header("つなぐもの")]
    [Tooltip("Assets/InputSystem_Actions を入れる。**UIのクリックに使う。**" +
             "シーンにすでに EventSystem があるなら空でもよい")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("文字")]
    [Tooltip("使いたい日本語フォントの名前。上から順に、パソコンに入っているものを探す")]
    [SerializeField]
    private string[] fontNames =
    {
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic", "Noto Sans CJK JP",
    };

    [Header("見た目")]
    [Tooltip("後ろを暗くする色")]
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.65f);

    [Tooltip("項目の色（ふだん）")]
    [SerializeField] private Color itemColor = new Color(0.16f, 0.18f, 0.22f, 0.95f);

    [Tooltip("項目の色（マウスが重なったとき）")]
    [SerializeField] private Color itemHoverColor = new Color(0.30f, 0.55f, 0.85f, 1f);

    [Tooltip("文字の色")]
    [SerializeField] private Color textColor = Color.white;

    [Tooltip("項目の文字の大きさ")]
    [Range(14, 60)]
    [SerializeField] private int fontSize = 28;

    /// <summary>いまポーズ画面が開いているか。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>いま使われているポーズ画面。無ければ null。</summary>
    public static PauseMenu Current { get; private set; }

    /// <summary>
    /// **このゲームにポーズ画面があるか。**
    ///
    /// カメラ側がこれを見て、「Escape は自分のものではない」と判断している。
    /// </summary>
    public static bool Exists => Current != null;

    /// <summary>
    /// **「ロビーに戻る」を出してよいか。** 遊びの側（宇宙ごみなど）が入れる差し込み口。
    /// 入っていない・false なら、ボタンを出さない（釣りなど、ロビーへ戻る処理が無い遊び）。
    /// 宇宙ごみは <c>SpaceJunkLeaveMatch</c> が入れている。
    /// </summary>
    public static System.Func<bool> CanReturnToLobby;

    /// <summary>「ロビーに戻る」を決定したときに呼ぶ処理。遊びの側が入れる。</summary>
    public static System.Action ReturnToLobby;

    /// <summary>
    /// **「タイトルにもどる」を出してよいか。** 遊びの側が入れる差し込み口（2026/10/6・大槻さん）。
    /// 宇宙ごみは <c>SpaceJunkLeaveMatch</c> が入れている（つながっていてタイトル以外にいるとき。ロビーでも出る）。
    /// </summary>
    public static System.Func<bool> CanReturnToTitle;

    /// <summary>「タイトルにもどる」を決定したときに呼ぶ処理。ホストなら全員がタイトルに戻る。</summary>
    public static System.Action ReturnToTitle;

    /// <summary>画面いっぱいの入れ物（Canvas）。開く・閉じるは、これを出し入れするだけ。</summary>
    private GameObject root;

    /// <summary>最初の画面・設定の画面・確かめる画面。どれか1つだけを出す。</summary>
    private GameObject mainPage;
    private GameObject settingsPage;

    /// <summary>確かめる画面。出すたびに作り、閉じたら消すので、出ていない間は null。</summary>
    private GameObject confirmPage;

    /// <summary>パソコンから借りてきた日本語フォント（<see cref="FindFont"/>）。</summary>
    private Font font;

    // 開く前のカーソルの状態（閉じたときに戻すため）
    private CursorLockMode cursorLockBeforeOpen = CursorLockMode.Locked;
    private bool cursorVisibleBeforeOpen;

    // 画面の作り。1920x1080 を基準にした大きさ
    private const float ItemWidth = 420f;
    private const float ItemHeight = 72f;
    private const float ItemGap = 16f;

    private void Awake()
    {
        // **1つだけにする。** 2つあると Escape で開いた直後に閉じてしまう
        if (Current != null && Current != this)
        {
            Debug.LogWarning("[UI] ポーズ画面が2つあったので、あとの1つを消しました。", gameObject);
            Destroy(gameObject);

            return;
        }

        Current = this;

        font = FindFont();

        Build();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>
    /// **シーンにポーズ画面が無ければ、再生したときに自分で作る。**
    ///
    /// 置き忘れると「Escape を押しても何も起きない」ことになり、
    /// 原因が分かりにくいため。手で置いてあれば何もしない。
    /// 一度作れば、シーンが変わっても残る。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        GameObject created = new GameObject("PauseMenu (自動)");
        created.AddComponent<PauseMenu>();
        DontDestroyOnLoad(created);

        Debug.Log("[UI] シーンにポーズ画面が無かったので、自動で用意しました。Escape で開きます。");
    }

    /// <summary>
    /// 毎フレーム、開く・閉じる・戻るのボタンを見る。
    /// Escape／Start は開く・閉じるの切り替え、B は「ひとつ前へ戻る」。
    /// </summary>
    private void Update()
    {
        // タイトル画面が出ている間は開かない（Esc はタイトル画面の「戻る」に使う。2026/10/6）
        // ロビーの設定画面が開いている間（と閉じたフレーム）も開かない（Esc・B は設定画面の「戻る」に使う。2026/10/6）
        if (TitleScreen.IsVisible || SpaceJunkLobbyScreen.BlocksEscape)
        {
            if (IsOpen)
            {
                Close();
            }
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if ((keyboard != null && keyboard[pauseKey].wasPressedThisFrame) || GamepadInput.WasPressed(pauseButton))
        {
            Toggle();
        }
        else if (IsOpen && GamepadInput.WasPressed(backButton))
        {
            if (confirmPage != null && confirmPage.activeSelf)
            {
                // 確かめる画面からは、最初の画面へ戻る
                ShowSettings(false);
            }
            else if (settingsPage != null && settingsPage.activeSelf)
            {
                ShowSettings(false);
            }
            else
            {
                Close();
            }
        }
    }

    private void OnDisable()
    {
        // 開いたまま消えると、**止まったまま戻せなくなる**
        if (IsOpen)
        {
            Close();
        }
    }

    // ------------------------------------------------------------
    // 開く・閉じる
    // ------------------------------------------------------------

    /// <summary>開いていれば閉じ、閉じていれば開く。</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>ポーズ画面を開く。**ゲームは止まる。**</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;

        // **開くたびに確かめる。**
        // シーンが切り替わると、シーンに置かれていた EventSystem は消えてしまう。
        // 無ければここで作り直さないと、ボタンが反応しない
        EnsureEventSystem();

        // 「ロビーに戻る」を出すかは、開くときの状況（試合中か）で変わるので、毎回作り直す
        RebuildMainPage();

        SetVisible(true);
        ShowSettings(false);

        GamePause.SetPaused(true, freezeTime);

        // 閉じたときに元へ戻せるよう、開く前の状態を控えておく
        cursorLockBeforeOpen = Cursor.lockState;
        cursorVisibleBeforeOpen = Cursor.visible;

        // マウスで選ぶので、カーソルを出す
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>ポーズ画面を閉じる。**ゲームが動き出す。**</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;

        SetVisible(false);
        GamePause.SetPaused(false);

        // 遊びに戻るので、**開く前の状態に戻す**
        Cursor.lockState = cursorLockBeforeOpen;
        Cursor.visible = cursorVisibleBeforeOpen;
    }

    /// <summary>設定の画面と、最初の画面を切り替える。</summary>
    public void ShowSettings(bool show)
    {
        if (mainPage != null)
        {
            mainPage.SetActive(!show);
        }

        if (settingsPage != null)
        {
            settingsPage.SetActive(show);
        }

        CloseConfirm();

        SelectFirstForGamepad(show ? settingsPage : mainPage);
    }

    /// <summary>
    /// ゲームを終了する。
    /// **エディタで遊んでいるときは、再生を止めるだけ**（エディタごと閉じたら困るため）。
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[UI] ゲームを終了します");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    /// <summary>
    /// **UIのクリックを受け取る仕組みがなければ作る。**
    ///
    /// このプロジェクトは新しい入力方式（Input System）を使っているので、
    /// 古い受け取り方（`StandaloneInputModule`）ではエラーになる。
    ///
    /// ## シーンが変わっても残るようにしてある（2026/9/20 修正）
    ///
    /// 以前はここで作った `EventSystem` に `DontDestroyOnLoad` を付けていなかった。
    /// ポーズ画面そのものは残るのに、**クリックを受け取る係だけがシーンと一緒に消える**ため、
    /// オンラインで会場へ移ったあと、**ポーズ画面は開くのにボタンが反応しない**状態になっていた
    /// （ロビーでは同じシーンにいるので気づけない）。
    ///
    /// 作り直せるよう、**開くたびに呼ぶ**ようにもしてある。
    /// シーンに置かれていた `EventSystem` が消えた場合にも、ここで作り直される。
    /// </summary>
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem",
            typeof(EventSystem), typeof(InputSystemUIInputModule));

        // **ポーズ画面と一緒に生き残らせる。** これが無いと、
        // シーンを切り替えた先でボタンが押せなくなる
        DontDestroyOnLoad(eventSystem);

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();

        if (inputActions != null)
        {
            // 入れてあれば、そこにある「UI」の設定を使う
            module.actionsAsset = inputActions;
        }
        else
        {
            // **入れ忘れ・自動で作ったときの手当て。**
            // 何も入っていないと、クリックが届かない
            module.AssignDefaultActions();
        }

        Debug.Log("[UI] UIのクリックを受け取る EventSystem を作りました");
    }
}
