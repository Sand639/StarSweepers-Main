using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// **ロビーの画面。**（2026/10/6・大槻さん「タイトルと同じ要領で、めっちゃカメレオンを参考に作り直して」）
///
/// 画面そのものは**プレハブ `Assets/Resources/LobbyScreen.prefab`**。ロビーのシーン（SpaceJunkLobby）に置いてある。
/// **ボタンや文字は、エディタで自由に動かし・大きさや画像・色を変えてよい。**
/// このスクリプトは、部品に付いている印（<see cref="SpaceJunkLobbyPart"/> / <see cref="SpaceJunkLobbyButton"/> /
/// <see cref="SpaceJunkLobbyValueRow"/>）を頼りに中身を動かす。
///
/// ## ふだん（左上）
///
/// 次に遊ぶマップの画像と名前・人数・参加者（チームの色）・いまの設定・案内。**全員に見える。**
///
/// ## ゲーム設定（ホストだけ。設定端末に近づいて E）
///
/// | 左：ゲーム設定 | 右：マップ |
/// | --- | --- |
/// | チーム数（＜ ＞）／何本先取（±1）／1ラウンドの最大時間（±1・±10・±50）／チーム分け（1人ずつ ＜ ＞、ランダムに割り振る） | 画像（無ければ「画像 未設定」）と、マップの一覧 |
///
/// **マップの選び方（めっちゃカメレオンと同じ）：**
///
///   ・**マップの名前を押す** → そのマップに決まる。**全部のラウンドをそのマップで遊ぶ**
///   ・**「ランダム」を押す** → **右の四角にチェックが付いたマップ**から、ラウンドごとにランダムに選ぶ（直前と同じマップは選ばない）
///   ・右の四角を押すと、チェックを付け外しする（緑＝候補に入れる／赤＝入れない）
///
/// 下の「ゲームを始める」で全員がマップへ移る。**閉じるのは Esc／B／「戻る」／もう一度 E。**
/// Esc はこの画面が開いている間はこちらが使う（ポーズ画面には渡さない。<see cref="BlocksEscape"/>）。
///
/// マップの画像は、マップの一覧（`Tools > StarSweepers > 宇宙ごみのマップの一覧を開く`）の **Thumbnail** に入れる。
/// </summary>
[DisallowMultipleComponent]
public partial class SpaceJunkLobbyScreen : MonoBehaviour
{
    [Tooltip("ON：再生したときに、文字をパソコンの日本語フォントに差し替える（日本語が四角にならないように）")]
    [SerializeField] private bool useOsJapaneseFont = true;

    [Tooltip("左上の案内を書き直す間隔（秒）")]
    [SerializeField] private float hudRefreshSeconds = 0.2f;

    /// <summary>プレハブの作りの版（作るツールが入れる。古ければツールが作り直す）。</summary>
    [SerializeField, HideInInspector] private int prefabVersion;

    public int PrefabVersion => prefabVersion;

#if UNITY_EDITOR
    /// <summary>プレハブを作るツールが版を入れる。</summary>
    public void EditorSetPrefabVersion(int value)
    {
        prefabVersion = value;
    }
#endif

    /// <summary>
    /// **ゲーム設定の入力欄に数字を打ち込んでいる最中か。** 打ち込み中は、E（設定端末）や Esc（閉じる）を読まない。
    /// </summary>
    public static bool IsTyping { get; private set; }

    /// <summary>前のフレームで打ち込み中だったか。入力欄を Esc で抜けたフレームに、画面まで閉じないようにするため。</summary>
    private static bool wasTyping;

    /// <summary>ゲーム設定の画面が開いているか。開いている間は、ほかの操作（R で戻る など）を止める。</summary>
    public static bool IsSettingsOpen { get; private set; }

    /// <summary>この画面が Esc／B で閉じたフレーム。同じ Esc でポーズ画面が開かないように持つ。</summary>
    private static int closedByBackFrame = -1;

    /// <summary>
    /// **Esc をこの画面が使っているか**（開いている間と、Esc で閉じたフレーム）。ポーズ画面はこれを見て開かない。
    /// 同じキーを2か所で読むと取り合いになるので、持ち主をはっきりさせる（AIの申し送り 2026/9/8・9/16）。
    /// </summary>
    public static bool BlocksEscape => IsSettingsOpen || closedByBackFrame == Time.frameCount;

    /// <summary>
    /// プレハブの中の部品を、役割（印 <see cref="SpaceJunkLobbyPart"/>）で引けるようにしたもの。
    /// 同じ役割が2つあれば最初に見つかったほう。行の見本の中の部品は入れない。
    /// </summary>
    private readonly Dictionary<SpaceJunkLobbyPartRole, SpaceJunkLobbyPart> parts =
        new Dictionary<SpaceJunkLobbyPartRole, SpaceJunkLobbyPart>();

    /// <summary>ゲーム設定の数字の行（チーム数・何本先取・最大時間）。</summary>
    private readonly List<SpaceJunkLobbyValueRow> valueRows = new List<SpaceJunkLobbyValueRow>();

    /// <summary>参加者1人ぶんの行の見本。非表示にしておき、複製して使う。</summary>
    private GameObject playerRowTemplate;

    /// <summary>マップ1つぶんの行の見本。非表示にしておき、複製して使う。</summary>
    private GameObject mapRowTemplate;

    /// <summary>いま出している参加者の行（見本の複製）。作り直すときに消す。</summary>
    private readonly List<GameObject> playerRows = new List<GameObject>();

    /// <summary>いま出しているマップの行（見本の複製）。作り直すときに消す。</summary>
    private readonly List<MapRow> mapRows = new List<MapRow>();

    /// <summary>
    /// 前回、参加者の行を作ったときの中身をつないだ文字列。
    /// 毎フレーム作り直すと重い（ボタンの押下も途切れる）ので、これが変わったときだけ作り直す。
    /// </summary>
    private string playerRowsSignature = string.Empty;

    /// <summary>前回、マップの行を作ったときの候補をつないだ文字列。変わったときだけ作り直す。</summary>
    private string mapRowsSignature = string.Empty;

    /// <summary>左上の案内を次に書き直すまでの秒数（Hud Refresh Seconds から減らしていく）。</summary>
    private float hudTimer;

    /// <summary>日本語が出せる文字（Use Os Japanese Font が ON のときだけ入る）。</summary>
    private Font font;

    /// <summary>マップの一覧の1行（複製したもの）。</summary>
    private sealed class MapRow
    {
        /// <summary>このマップのシーンの名前。押したときにホストへ伝える値。</summary>
        public string SceneName;

        /// <summary>行そのもの（消すときに使う）。</summary>
        public GameObject Root;

        /// <summary>「いま選ばれている」の目印。固定のマップがこの行のときだけ出す。</summary>
        public GameObject Highlight;

        /// <summary>右の四角のチェックあり（緑＝ランダムの候補に入れる）。</summary>
        public GameObject CheckOn;

        /// <summary>右の四角のチェックなし（赤＝候補に入れない）。</summary>
        public GameObject CheckOff;
    }

    // ------------------------------------------------------------
    // 出し入れ
    // ------------------------------------------------------------

    /// <summary>
    /// 起動時：文字を日本語フォントに差し替え、プレハブの中の部品を集めてボタンをつなぐ。
    /// 画面は両方とも閉じた状態から始める（出すかどうかは毎フレーム <see cref="Update"/> で決める）。
    /// </summary>
    private void Awake()
    {
        if (useOsJapaneseFont)
        {
            // 32 は文字の大きさの基準（この大きさで字を作る）
            font = UiFont.Find(32);
            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.font = font;
            }
        }

        CollectParts();
        WireButtons();

        SetActive(SpaceJunkLobbyPartRole.SettingsWindow, false);
        SetActive(SpaceJunkLobbyPartRole.Hud, false);
    }

    /// <summary>画面が消えるとき（シーンの切り替えなど）、static の「開いている」「打ち込み中」を残さない。</summary>
    private void OnDisable()
    {
        IsSettingsOpen = false;
        IsTyping = false;
        wasTyping = false;
    }

    /// <summary>
    /// 毎フレーム。流れ：
    /// 1. ロビーにいるか（つながっていて、ラウンドが無い）を見て、左上の案内を出し入れする
    /// 2. ホストが設定端末を開いていればゲーム設定を開く。Esc／B なら端末ごと閉じる
    /// 3. 開いた瞬間に1回だけ準備（行を作り直させる・パスワード欄・コントローラーの選択）
    /// 4. 開いている間はゲーム設定を毎フレーム、左上の案内は Hud Refresh Seconds ごとに書き直す
    /// </summary>
    private void Update()
    {
        wasTyping = IsTyping;
        IsTyping = IsSettingsOpen && IsInputFocused();

        NetworkManager manager = NetworkManager.Singleton;
        bool connected = manager != null && (manager.IsServer || manager.IsConnectedClient);
        bool inLobby = connected && SpaceJunkRound.Current == null;

        SetActive(SpaceJunkLobbyPartRole.Hud, inLobby && !IsSettingsOpen);

        SpaceJunkSession session = SpaceJunkSession.Current;
        SpaceJunkLobbyTerminal terminal = SpaceJunkLobbyTerminal.Current;

        // 開くかどうかは設定端末（SpaceJunkLobbyTerminal）が決める。この画面はそれに合わせるだけ
        bool open = inLobby && manager.IsServer && session != null && terminal != null && terminal.IsOpen;

        if (open && WasBackPressed())
        {
            terminal.Close();
            closedByBackFrame = Time.frameCount;
            open = false;
        }

        if (open != IsSettingsOpen)
        {
            IsSettingsOpen = open;
            SetActive(SpaceJunkLobbyPartRole.SettingsWindow, open);

            if (open)
            {
                EnsureEventSystem();
                // 前回開いたときの行が残っていても、必ず作り直す
                playerRowsSignature = string.Empty;
                mapRowsSignature = string.Empty;
                ResetPasswordSection();
                SelectFirstForGamepad();
            }
        }

        if (open)
        {
            RefreshSettings(session);
        }

        // 左上の案内は毎フレーム書き直すほどのものではないので、間隔を空ける（ポーズ中も進むよう unscaled）
        hudTimer -= Time.unscaledDeltaTime;
        if (inLobby && !open && hudTimer <= 0f)
        {
            hudTimer = hudRefreshSeconds;
            RefreshHud(manager, session, terminal);
        }
    }

    /// <summary>Esc（キーボード）か B（コントローラー）が押されたか。ポーズ画面が開いている間は見ない。</summary>
    private static bool WasBackPressed()
    {
        // 打ち込み中の Esc は「入力をやめる」に使う（画面までは閉じない）
        if (GamePause.BlocksInput || IsTyping || wasTyping)
        {
            return false;
        }

        Keyboard keyboard = Keyboard.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || GamepadInput.WasPressed(GamepadButton.East);
    }

    // ------------------------------------------------------------
    // 部品を見つけて、つなぐ
    // ------------------------------------------------------------

    /// <summary>
    /// プレハブの中から、印（<see cref="SpaceJunkLobbyPart"/>）の付いた部品を集める。
    /// 行の見本は非表示にして取っておき、見本の中の印は <see cref="parts"/> に入れない（複製した行ごとに別のものになるため）。
    /// </summary>
    private void CollectParts()
    {
        // 行の見本（複製して使う）を先に探し、その中の印は数えない
        foreach (SpaceJunkLobbyPart part in GetComponentsInChildren<SpaceJunkLobbyPart>(true))
        {
            if (part.Role == SpaceJunkLobbyPartRole.PlayerRowTemplate)
            {
                playerRowTemplate = part.gameObject;
            }
            else if (part.Role == SpaceJunkLobbyPartRole.MapRowTemplate)
            {
                mapRowTemplate = part.gameObject;
            }
        }

        foreach (SpaceJunkLobbyPart part in GetComponentsInChildren<SpaceJunkLobbyPart>(true))
        {
            if (part.Role == SpaceJunkLobbyPartRole.None || IsInsideTemplate(part.transform) || parts.ContainsKey(part.Role))
            {
                continue;
            }

            parts[part.Role] = part;
        }

        if (playerRowTemplate != null)
        {
            playerRowTemplate.SetActive(false);
        }

        if (mapRowTemplate != null)
        {
            mapRowTemplate.SetActive(false);
        }

        valueRows.AddRange(GetComponentsInChildren<SpaceJunkLobbyValueRow>(true));
        WireInputs();
    }

    /// <summary>その部品が、行の見本（参加者・マップ）の中にあるか。</summary>
    private bool IsInsideTemplate(Transform target)
    {
        return (playerRowTemplate != null && target.IsChildOf(playerRowTemplate.transform)) ||
               (mapRowTemplate != null && target.IsChildOf(mapRowTemplate.transform));
    }

    /// <summary>行の外にあるボタン（閉じる・始める・±・ランダム など）を、押したときの振り分け（<see cref="Do"/>）につなぐ。</summary>
    private void WireButtons()
    {
        foreach (SpaceJunkLobbyButton button in GetComponentsInChildren<SpaceJunkLobbyButton>(true))
        {
            // 行の中のボタンは、行を作るときにつなぐ
            if (IsInsideTemplate(button.transform))
            {
                continue;
            }

            // ラムダに渡すボタンを、この回のぶんとして固定しておく
            SpaceJunkLobbyButton target = button;
            button.Button.onClick.AddListener(() => Do(target));
        }
    }

    /// <summary>ボタンが押されたときの振り分け。**ホストのときだけ効く**（設定を変えられるのはホストだけ）。</summary>
    private void Do(SpaceJunkLobbyButton button)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsServer)
        {
            return;
        }

        switch (button.Action)
        {
            case SpaceJunkLobbyAction.Close:
                if (SpaceJunkLobbyTerminal.Current != null)
                {
                    SpaceJunkLobbyTerminal.Current.Close();
                }
                break;

            case SpaceJunkLobbyAction.StartMatch:
                StartMatch(session);
                break;

            case SpaceJunkLobbyAction.RandomAssign:
                session.ServerRandomAssign();
                break;

            case SpaceJunkLobbyAction.SelectRandomMap:
                session.ServerSetFixedMap(string.Empty);
                break;

            case SpaceJunkLobbyAction.ChangePassword:
                ChangePassword();
                break;

            case SpaceJunkLobbyAction.Step:
                SpaceJunkLobbyValueRow row = button.GetComponentInParent<SpaceJunkLobbyValueRow>();
                if (row != null)
                {
                    Step(session, row.Kind, button.Amount);
                }
                break;
        }
    }

    // ------------------------------------------------------------
    // 補助
    // ------------------------------------------------------------

    /// <summary>画像があれば画像を、無ければ代わりの枠（文字つき）を出す。</summary>
    private void ShowPicture(SpaceJunkLobbyPartRole imageRole, SpaceJunkLobbyPartRole placeholderRole, Sprite sprite, string placeholderText)
    {
        Image image = PartComponent<Image>(imageRole);
        GameObject placeholder = PartObject(placeholderRole);

        if (image != null)
        {
            if (image.sprite != sprite)
            {
                image.sprite = sprite;
            }
            SetActive(image.gameObject, sprite != null);
        }

        if (placeholder != null)
        {
            SetActive(placeholder, sprite == null);
            Text text = placeholder.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                SetText(text, placeholderText);
            }
        }
    }

    /// <summary>その役割の部品。プレハブに無ければ null（無くても落ちないように、呼ぶ側は null を許す）。</summary>
    private GameObject PartObject(SpaceJunkLobbyPartRole role)
    {
        return parts.TryGetValue(role, out SpaceJunkLobbyPart part) && part != null ? part.gameObject : null;
    }

    /// <summary>その役割の部品に付いている <typeparamref name="T"/>（Text・Image・Button など）。無ければ null。</summary>
    private T PartComponent<T>(SpaceJunkLobbyPartRole role) where T : Component
    {
        GameObject target = PartObject(role);
        return target != null ? target.GetComponent<T>() : null;
    }

    /// <summary>その役割の部品を出す／隠す。無ければ何もしない。</summary>
    private void SetActive(SpaceJunkLobbyPartRole role, bool active)
    {
        SetActive(PartObject(role), active);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    /// <summary>その役割の文字を書き換える。無ければ何もしない。</summary>
    private void SetText(SpaceJunkLobbyPartRole role, string value)
    {
        SetText(PartComponent<Text>(role), value);
    }

    /// <summary>文字が変わったときだけ書き換える（毎回書くと、画面の組み直しが毎フレーム起きる）。</summary>
    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    /// <summary>その人（人ごとの番号）が、このPCで操作している人か（1台で複数人なら、このPCの全員）。</summary>
    private static bool IsLocalPlayer(ulong key)
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == LocalMultiplayer.ClientOf(key);
    }

    /// <summary>コントローラーがつながっていれば、設定の最初の項目を選んでおく（十字キーで動かせるように）。</summary>
    private void SelectFirstForGamepad()
    {
        GameObject window = PartObject(SpaceJunkLobbyPartRole.SettingsWindow);
        if (Gamepad.current == null || window == null || EventSystem.current == null)
        {
            return;
        }

        Selectable first = window.GetComponentInChildren<Selectable>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }

    /// <summary>UIのクリックを受け取る仕組みがなければ作る（タイトル画面・ポーズ画面と同じ。シーンが変わっても残す）。</summary>
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
