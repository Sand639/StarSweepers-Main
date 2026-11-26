using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **宇宙ごみ用に、プレイヤーへ上乗せする部品。**
///
/// フックの操作そのものは釣りの部品（<c>SweeperNetPlayer</c> / <c>HookController</c> など）を
/// **そのまま共用している。** ただし、そのままでは次の2つが合わない。
///
/// ### ① マップに着いても、位置とカメラが合わない
///
/// <c>SweeperNetPlayer</c> は「釣り会場に着いたか」を **<c>FishingMatch</c> がいるかどうか**で判断している。
/// 宇宙ごみのマップには <c>FishingMatch</c> を置かない（釣りの時間制ルールが動いてしまうため）ので、
/// **着いたことに気づかず、ロビーの位置のまま・カメラも追ってこない。**
/// → ここで <see cref="SpaceJunkRound"/> が現れたかどうかで同じことをやり直す。
///
/// ### ② チームの色が食い違う
///
/// <c>SweeperNetPlayer</c> は**参加した順に2人ずつ**でチームの色を塗る。
/// 宇宙ごみは**ホストが自由にチームを決める**ので、そのままでは色が合わない。
/// → ここで <see cref="SpaceJunkSession"/> が持っているチームの色に塗り直す。
///
/// ### 出てくる場所
///
/// **ロビーでは、番号付きのスポーンポイント（SpaceJunkLobbyPoint）**のうち、自分の参加番号と同じ所に出る（見つからなければ決まった場所）。
/// **ラウンド中は、自分のチームのゴールエリアの上の決まった場所**に出る（ゴールから見て横に、チームの人数ぶん均等に並ぶ。2026/10/27）。
/// Spawn On Goal Area を OFF にするか、ゴールエリアに床が無いマップでは、**ゴールの手前に横一列**で出る（2026/10/27 の朝の形）。
/// ゴールが見つからなければ、接続番号ごとに円周上へ散らす。
///
/// 使い方：宇宙ごみ用のプレイヤーのプレハブに、釣りの部品と一緒に付ける。
/// **釣りのプレハブには付けないこと**（釣りの動きが変わってしまう）。
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Game/Player/SpaceJunkPlayerSetup.md。
/// </summary>
public partial class SpaceJunkPlayerSetup : MonoBehaviour
{

    [Header("見た目")]
    [Tooltip("チームの色に塗る見た目。**釣りのプレハブと同じものを入れる**")]
    [SerializeField] private Renderer[] teamRenderers;

    [Header("出てくる場所")]
    [Tooltip("ON：自分のゴールエリアの上の決まった場所に出る（2026/10/27〜。ラウンドの始まりにゴールエリアに乗って飛んでくる演出と合わせた）。" +
             "OFF：ゴールから Spawn Inset だけ内側に、横一列で出る（2026/10/27 の朝の形）。ゴールエリアに床が無いマップでは、ON でも横一列になる")]
    [SerializeField] private bool spawnOnGoalArea = true;

    [Tooltip("ゴールエリアの上には、ゴールエリアの幅をチームの人数で等分した場所に並ぶ。" +
             "これは、ゴールエリアの見た目が無くて幅が分からないときに使う、となりの人との間隔（メートル）")]
    [SerializeField] private float goalAreaSpacing = 1.5f;

    [Tooltip("（横一列のとき）自分のゴールから、ステージの中心へ向かって何m離れた場所に出るか")]
    [SerializeField] private float spawnInset = 4f;

    [Tooltip("（横一列のとき）チームの仲間と並ぶときの、となりの人との間隔（メートル）。ゴールから見て横に、人数ぶん均等に並ぶ")]
    [SerializeField] private float spawnSpacing = 2f;

    [Tooltip("ゴールが見つからないとき（ロビーなど）に、重ならないようにどれだけばらけさせるか（メートル）")]
    [SerializeField] private float spawnScatter = 1.5f;

    [Tooltip("ゴールが見つからないときに使う、中心からの距離")]
    [SerializeField] private float fallbackRadius = 6f;

    [Header("落ちたときの立て直し")]
    [Tooltip("押すと、その場から**出てくる場所へ戻る**キー。落ちて戻れなくなったとき用")]
    [SerializeField] private Key resetKey = Key.R;

    [Tooltip("同じことをする、コントローラーのボタン（North＝Xbox の Y）")]
    [SerializeField] private GamepadButton resetButton = GamepadButton.North;

    [Tooltip("この高さより下まで落ちたら、**自動で出てくる場所へ戻す**（メートル）")]
    [SerializeField] private float fallResetHeight = -8f;

    /// <summary>戻る高さ（<see cref="fallResetHeight"/>）より何メートル上で、落ちる音を鳴らすか。</summary>
    private const float FallSoundAboveReset = 6f;

    /// <summary>今回の落下で、落ちる音を鳴らしたか。</summary>
    private bool fallSoundPlayed;

    [Tooltip("**ラウンド中に**戻ったとき（キー・落下）、動けない秒数（復活時間）。0 で止めない。ロビーでは止めない")]
    [Min(0f)]
    [SerializeField] private float respawnLockSeconds = 3f;

    [Header("レーダー（自分のぶんにだけ付く）")]
    [Tooltip("オンなら、画面の右下にレーダーを出す（2026/11/12 大槻さんの依頼で、一旦オフにした）")]
    [SerializeField] private bool showRadar;

    [Tooltip("レーダーの外枠の画像（Assets/Art/Sprites/Radar）")]
    [SerializeField] private Sprite radarFrame;

    [Tooltip("レーダーの方角（N/E/S/W）の輪の画像。自分の向きに合わせて回る")]
    [SerializeField] private Sprite radarCompass;

    [Tooltip("レーダーの真ん中に出す自分の印の画像")]
    [SerializeField] private Sprite radarSelf;

    /// <summary>
    /// このPCで操作している人の「戻る」キーの名前。画面の案内に使う。
    /// 自分のぶんが動き出すまでは空。
    /// </summary>
    public static string LocalResetKeyName { get; private set; } = string.Empty;

    /// <summary>
    /// このPCで操作している人の、**復活までの残り秒数**。0 なら動ける。画面の案内に使う。
    /// </summary>
    public static float LocalRespawnRemaining { get; private set; }

    // 同じプレハブに付いている釣りの部品（Awake で取る）。無いものは null のまま使う

    /// <summary>通信上のプレイヤー（自分のぶんか・何人目か・接続番号）。</summary>
    private SweeperNetPlayer netPlayer;

    /// <summary>歩く・落ちるの当たり判定。位置を直接動かすときはいったん切る。</summary>
    private CharacterController characterController;

    /// <summary>歩く部品。動けなくするときに enabled を切る。</summary>
    private SweeperMover mover;

    /// <summary>キー／コントローラーの入力（1台で複数人のとき、自分の担当だけを読む）。</summary>
    private PlayerInputSource input;

    /// <summary>復活までの残り秒数（自分のぶんだけ使う）。</summary>
    private float respawnRemaining;

    /// <summary>マップに着いて、場所とカメラを合わせ終わったか。</summary>
    private bool placedInRound;

    /// <summary>最初の参加番号確定後にロビー配置を始めたか。</summary>
    private bool initialLobbyPlacementQueued;

    /// <summary>ロビーの初回配置が同期するまで見た目を隠しているか。</summary>
    private bool lobbyVisualsHidden;

    /// <summary>見た目を隠してから何秒たったか（<see cref="LobbyRevealTimeout"/> で必ず見せるために数える）。</summary>
    private float lobbyHiddenSeconds;

    /// <summary>
    /// ロビーで隠すために、こちらで消した描画の部品。見せるときは**これだけ**を戻す。
    /// 元から消えていた物（出していない光線の線など）まで点けると、体のそばに白い円盤が出る（2026/11/5）。
    /// </summary>
    private readonly System.Collections.Generic.HashSet<Renderer> lobbyHiddenRenderers = new System.Collections.Generic.HashSet<Renderer>();

    /// <summary>出現場所からこの距離（水平・m）以内にいたら「着いた」とみなして見せる。</summary>
    private const float LobbyRevealDistance = 0.1f;

    /// <summary>隠してからこの秒数たったら、どこにいても見せる（隠しっぱなしを防ぐ）。</summary>
    private const float LobbyRevealTimeout = 1f;

    /// <summary>名前のタグのプレハブの名前（Assets/Resources の中）。</summary>
    private const string NameplatePrefabName = "PlayerNameplate";

    /// <summary>頭の上の名前のタグ。</summary>
    private SpaceJunkNameplate nameplate;

    /// <summary>直前に塗ったチーム。変わったときだけ塗り直すために持つ。-999 は「まだ一度も塗っていない」印（チーム番号としてあり得ない値）。</summary>
    private int lastAppliedTeam = -999;

    private void Awake()
    {
        netPlayer = GetComponent<SweeperNetPlayer>();
        characterController = GetComponent<CharacterController>();
        mover = GetComponent<SweeperMover>();
        input = PlayerInputSource.Get(this);

        baseScale = transform.localScale;

        if (IsInLobbyScene())
        {
            lobbyVisualsHidden = true;
            SetLobbyRenderersVisible(false);
        }
    }

    /// <summary>ロビーでのプレイヤーの大きさの倍率（Hans さん了承済み・2026/11/17）。</summary>
    private const float LobbyScaleMultiplier = 1.1f;

    /// <summary>元の大きさ（ロビー以外ではこれに戻す）。</summary>
    private Vector3 baseScale = Vector3.one;

    /// <summary>ロビーにいる間だけ、プレイヤーを1.1倍にする。</summary>
    private void ApplyLobbyScale()
    {
        Vector3 target = IsInLobbyScene() ? baseScale * LobbyScaleMultiplier : baseScale;
        if (transform.localScale != target)
        {
            transform.localScale = target;
        }
    }

    /// <summary>
    /// 毎フレーム。前半（色・名前のタグ）は**全員のぶん**、後半は**自分が操作しているぶんだけ**。
    /// 後半の流れ：案内とレーダー → 置き直しと復活待ちの進み → ラウンドの始まり／ロビーへ戻ったときの切り替え → 落ちた・R キーの立て直し。
    /// </summary>
    private void Update()
    {
        // チームの色は、**自分のぶんも他の人のぶんも**塗る（誰が味方か分かるように）
        ApplyTeamColor();

        ApplyLobbyScale();

        // 頭の上の名前のタグも、**全員のぶん**に付ける
        EnsureNameplate();

        if (lobbyVisualsHidden)
        {
            // Awake 後に作られた名前タグなども、配置完了までは描かない。
            SetLobbyRenderersVisible(false);
            lobbyHiddenSeconds += Time.unscaledDeltaTime;
            TryRevealAfterLobbyPositionSync();
        }

        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        // 最初のシーン読み込み前後に生成されたP1も含め、番号が届いたら一度だけ置き直す。
        if (!initialLobbyPlacementQueued && IsInLobbyScene() && netPlayer.PlayerIndex >= 0)
        {
            initialLobbyPlacementQueued = true;
            BeginPlacement();
        }

        // 画面の案内とレーダーは1つしか無いので、そのPCの1人目のぶんだけ（1台で複数人のとき）
        if (IsFirstLocalPlayer)
        {
            LocalResetKeyName = $"{resetKey}／{GamepadInput.Label(resetButton)}";
            EnsureRadar();
        }

        TickPlacement();
        TickRespawnLock();

        bool inRound = SpaceJunkRound.Current != null;

        if (inRound && !placedInRound)
        {
            placedInRound = true;

            // 新しいラウンドの始まり。前のラウンドの復活待ちが残っていても、ここで解く
            respawnRemaining = 0f;
            if (IsFirstLocalPlayer)
            {
                LocalRespawnRemaining = 0f;
            }

            // 置き直しは、**自分のチームとゴールが届くまで待ってから**（TickPlacement）
            BeginPlacement();
        }
        else if (!inRound && placedInRound)
        {
            // ロビーへ戻った。**ここでは置き直さない。**
            // この瞬間はまだシーンの入れ替わりの途中で、前のマップの物が残っている。
            // 置き直すのは、ロビーのシーンが読み込み終わってから（OnSceneLoaded → TickPlacement）
            placedInRound = false;
        }

        // 置き直しを待っている間は、落下の判定もしない（止めて待っているため）
        if (!placementPending && LobbyTestDeparture.Current?.IsBoarding != true)
        {
            CheckReset();
        }
    }

    // シーンの読み込みを見張る（プレイヤーはシーンをまたいで生き続けるので、移った先で置き直す必要がある）

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// **シーンが読み込まれたら、置き直しを待つ。**
    ///
    /// プレイヤーはシーンをまたいで生き続けるので、**移った直後は
    /// 「前のシーンで立っていた場所」のまま**になる。そのままだと新しいシーンの地面の外で落ちる
    /// （2026/9/20・大槻さんの報告）。
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (netPlayer == null || !netPlayer.IsOwner)
        {
            return;
        }

        BeginPlacement();
    }

    /// <summary>
    /// 頭の上の名前のタグ（<see cref="SpaceJunkNameplate"/>）を付ける。
    /// 見た目は `Assets/Resources/PlayerNameplate.prefab` から作る（無ければ初期値で作る）。
    /// プレイヤーの子にするので、シーンが変わっても一緒に残る。
    /// </summary>
    private void EnsureNameplate()
    {
        if (nameplate != null || netPlayer == null)
        {
            return;
        }

        SpaceJunkNameplate prefab = Resources.Load<SpaceJunkNameplate>(NameplatePrefabName);

        if (prefab != null)
        {
            nameplate = Instantiate(prefab, transform);
        }
        else
        {
            GameObject plate = new GameObject(NameplatePrefabName);
            plate.transform.SetParent(transform, false);
            nameplate = plate.AddComponent<SpaceJunkNameplate>();
        }

        nameplate.Attach(netPlayer);
    }

    /// <summary>
    /// **落ちて戻れなくなったときの立て直し。**
    ///
    /// ・自分で押して戻る（<see cref="resetKey"/>）
    /// ・下まで落ちたら自動で戻す（<see cref="fallResetHeight"/>）
    ///
    /// 穴に落ちても待っていれば戻るが、**引っかかって落ちきらないこともある**ので、
    /// 自分で戻せるキーも用意してある。
    /// </summary>
    private void CheckReset()
    {
        // 戻る高さの少し上まで落ちたら、落ちる音を1回鳴らす（戻ったら、また鳴らせるようにする）
        if (!fallSoundPlayed && transform.position.y < fallResetHeight + FallSoundAboveReset)
        {
            fallSoundPlayed = true;
            GameAudio.PlaySe(GameSe.PlayerFall);
        }
        else if (fallSoundPlayed && transform.position.y > fallResetHeight + FallSoundAboveReset + 1f)
        {
            // R キーで戻ったときなど、上へ戻っていたら次の落下でまた鳴らす
            fallSoundPlayed = false;
        }

        // 下まで落ちたら、押されなくても戻す
        if (transform.position.y < fallResetHeight)
        {
            fallSoundPlayed = false;
            MoveToSpawnPoint();
            FollowWithCamera();
            StartRespawnLock();
            return;
        }

        // ポーズ中（と閉じたフレーム）と、ロビーの設定画面を開いている間は入力を読まない
        if (GamePause.BlocksInput || SpaceJunkLobbyScreen.IsSettingsOpen)
        {
            return;
        }

        // 復活待ちの間は、もう一度押しても何もしない（押し続けて待ち時間を延ばせないように）
        if (respawnRemaining > 0f)
        {
            return;
        }

        if (input.WasPressed(resetKey, resetButton))
        {
            MoveToSpawnPoint();
            FollowWithCamera();
            StartRespawnLock();
        }
    }

    /// <summary>
    /// **自分のぶんにだけ、レーダーを付ける。**（2026/9/22）
    /// 他の人のぶんに付けると、その人を中心にしたレーダーがもう1枚出てしまう。
    /// レーダーはマップにいる間だけ出る（<see cref="SpaceJunkRadar"/>）。
    /// </summary>
    private void EnsureRadar()
    {
        // 一旦オフ（Show Radar をオンにすれば、また出る）
        if (!showRadar || GetComponent<SpaceJunkRadar>() != null)
        {
            return;
        }

        SpaceJunkRadar radar = gameObject.AddComponent<SpaceJunkRadar>();
        radar.Setup(radarFrame, radarCompass, radarSelf);
    }

    /// <summary>そのPCの1人目か（1台に1人なら常に true）。</summary>
    private bool IsFirstLocalPlayer => netPlayer == null || netPlayer.LocalSeat == 0;

    /// <summary>このプレイヤーのチーム。係がいなければ 0。</summary>
    private int MyTeam()
    {
        if (SpaceJunkSession.Current == null)
        {
            return 0;
        }

        // 人ごとの番号で見る（1台で複数人のとき、同じPCの人でもチームが違うことがある）
        return SpaceJunkSession.Current.TeamOf(netPlayer != null ? netPlayer.PlayerKey : 0UL);
    }

    /// <summary>ホストが決めたチームの色に塗り直す。</summary>
    private void ApplyTeamColor()
    {
        if (teamRenderers == null || SpaceJunkSession.Current == null)
        {
            return;
        }

        int team = MyTeam();

        if (team == lastAppliedTeam)
        {
            return;
        }

        lastAppliedTeam = team;
        Color color = SpaceJunkTeams.TeamColor(team);

        foreach (Renderer renderer in teamRenderers)
        {
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }

    /// <summary>
    /// **シーンに置いてあるカメラと自分を結びつける。**
    /// プレイヤーはプレハブから生まれるので、シーンの中身をあらかじめ入れておけない。
    /// </summary>
    private void FollowWithCamera()
    {
        // カメラは画面に1つしか無いので、そのPCの1人目にだけ結びつける（1台で複数人のとき）
        if (!IsFirstLocalPlayer)
        {
            return;
        }

        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();
        if (follow != null)
        {
            follow.SetTarget(transform);
        }

        // 自陣が手前に来るように回るカメラ（SpaceJunkMap01TeamCam など）
        SpaceJunkTeamFollowCamera teamCamera = FindFirstObjectByType<SpaceJunkTeamFollowCamera>();
        if (teamCamera != null)
        {
            teamCamera.SetTarget(transform);
        }
    }
}
