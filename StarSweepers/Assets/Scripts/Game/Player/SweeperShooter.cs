using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **プレイヤー（本実装）の「狙う → 光線を伸ばす → 当たったら巻き取りへ」。**（2026/10/27・小野田さんの仕様）
///
/// | 操作 | どうなる |
/// | --- | --- |
/// | RT／左クリックを押す | 狙い始める。リングの縁から、届く長さの矢印の帯（<see cref="SweeperAimArea"/>）が出る |
/// | 押している間 | 帯が最短から最長まで伸びていく（Charge Seconds で最長）。**歩ける**。体は狙う方向を向く |
/// | 離す | 光線がその長さまで伸びる（<see cref="extendSpeed"/>）。**伸びている・戻っている間は歩けない** |
/// | 光線が物資に当たる | 自動で巻き取りへ（<see cref="SweeperReel"/>）。巻き取り中に押すと後ろへ投げる（重い物は投げずに引きずるだけ）。**巻き取り中はゆっくり歩ける**（<see cref="reelMoveScale"/>。2026/11/10） |
/// | 何にも当たらない・壁に当たる | 光線が戻ってくる。戻りきったら歩ける |
///
/// 光線の見た目は液体のビーム（<see cref="LiquidBeam"/>）。何もつかんでいない間は薄いチームの色、つかんだら濃くなる。
/// **巻き取り中に向きを変えると、光線が釣り糸のようにしなる**（手元からは体の向きに少し遅れて出て、物資へ曲がってつながる。2026/11/4）。
/// 撃った瞬間に <see cref="SweeperAnimation.PlayShoot"/>（A_SHOOT → A_AIM_01）を呼ぶ。
///
/// **撃ち方は4つから選べる**（<see cref="shotStyle"/>。2026/11/17・小野田さんの変更案）。2026/11/18 から、ロビーコンソールの
/// プレイヤー挙動「1 狙う・2 発射」（A＝Whip／B＝Aim／C＝SteerSnap／D＝SteerLag）で、ホストが全員ぶん決める（<see cref="PlayerBehavior"/>）。
/// Whip（初期値）：溜めずに、押した瞬間に狙った向きへ光線が出て、最長 1 秒出し続けられる。向きを変えると**鞭のようにしなり**、
/// 光線のどこかが物資に触れたらつかむ。Aim：上の表のとおり（溜めて長さを決めてから撃つ）。詳しくは `Documents/機能ドキュメント/鞭のように曲がる光線.md`。
/// </summary>
public class SweeperShooter : MonoBehaviour
{
    /// <summary>撃ち方（2026/11/17）。</summary>
    public enum ShotStyle
    {
        Aim,       // 今まで：押して溜めて長さを決め、離すとまっすぐ伸びる（挙動パターン B）
        Whip,      // 変更案：押すとすぐ出て、最長 1 秒出し続ける。体を振ると鞭のようにしなる（挙動パターン A）
        SteerSnap, // 曲げ撃ち：体はあまり回らず、光線の先がすぐ狙いへ曲がる（挙動パターン C。2026/11/18）
        SteerLag   // 曲げ撃ち（遅れ）：C と同じ作りで、先が遅れて狙いへ付いていく（挙動パターン D。2026/11/18）
    }

    /// <summary>
    /// **曲げ撃ち（挙動パターン C・D）の値。** C と D は作りが同じで、この値をパターンごとに別に持つ（2026/11/18・小野田さん）。
    /// 押すと体の向き（＝狙った向き）にまっすぐ光線の先が飛び出し、飛んでいる間に狙いを動かすと、**体はあまり回らずに、
    /// 光線の先が狙いの向きへ進路を変えて進む**（光線は、手元から体の向きに出て、先へ曲がってつながる）。
    /// </summary>
    [System.Serializable]
    public class SteerSettings
    {
        [Tooltip("光線を出し続けられる秒数")]
        [Min(0.1f)]
        public float seconds = 1f;

        [Tooltip("光線の先が飛ぶ速さ（m/秒）。遅いほど、伸びきるまでに長く曲げられる。伸びきったら（Max Reach）先は止まる")]
        [Min(0.1f)]
        public float headSpeed = 15f;

        [Tooltip("ON：離したらそこで光線が戻り始める（押している間だけ出る。最長 Seconds 秒）。OFF：1回押せば Seconds 秒出る")]
        public bool holdToKeep = true;

        [Tooltip("ON：光線の先の進む向きが、狙いの向きへすぐ変わる。OFF：ばね（Tip Stiffness・Tip Damping）で遅れて変わる")]
        public bool tipSnaps = true;

        [Tooltip("先の進む向きが狙いの向きに追いつく速さ（Tip Snaps が OFF のとき。大きいほどすぐ追いつく）")]
        [Min(0.5f)]
        public float tipStiffness = 8f;

        [Tooltip("先の止まりやすさ（Tip Snaps が OFF のとき。1 で行き過ぎない、小さいほど行き過ぎてからビヨンと戻る）")]
        [Range(0.1f, 1.5f)]
        public float tipDamping = 0.6f;

        [Tooltip("光線の先の進む向きが、体の向きから曲がれる最大の角度（度）。大きいと手前へ戻ってくるように曲がれる")]
        [Range(0f, 150f)]
        public float maxTipAngle = 120f;

        [Tooltip("光線を出している間に、体が撃った向きから狙いの方へ回れる最大の角度（度）。0 で体は回らない")]
        [Range(0f, 180f)]
        public float maxBodyTurn = 30f;

        [Tooltip("光線を出している間に、体が回る速さ（度／秒）")]
        [Min(0f)]
        public float bodyTurnSpeed = 120f;

        [Tooltip("光線の曲がり方の強さ（0 で手元から先までまっすぐ、1 で手元からは体の向きに出て、先へは進んできた向きから入る＝角ばった曲がり方）")]
        [Range(0f, 1f)]
        public float curveStrength = 1f;

        [Tooltip("光線を出している間に歩ける速さ（ふだんの速さに掛ける割合）。0 で歩けない")]
        [Range(0f, 1f)]
        public float moveScale = 0f;

        public SteerSettings()
        {
        }

        public SteerSettings(bool snaps)
        {
            tipSnaps = snaps;
        }
    }

    /// <summary>光線の段階。オンラインでは、ほかの人の画面にこれを配って同じ見た目を出す（<see cref="SweeperNetSync"/>）。</summary>
    public enum ShotPhase
    {
        Idle,       // 何もしていない
        Charging,   // 押して狙っている（歩ける）
        Extending,  // 光線が伸びている
        Retracting, // 何にも当たらず、光線が戻っている
        Reeling     // 物資を巻き取っている
    }

    [Header("参照（空なら同じ体から探す）")]
    [SerializeField] private SweeperAim aim;
    [SerializeField] private SweeperMover mover;
    [SerializeField] private SweeperReel reel;
    [SerializeField] private SweeperAimArea aimArea;
    [SerializeField] private SweeperRing ring;
    [SerializeField] private SweeperAnimation animationPlayer;

    [Tooltip("Assets/InputSystem_Actions を入れる（Attack＝RT・左クリックを使う）")]
    [SerializeField] private InputActionAsset inputActions;

    [Tooltip("光線の見た目（Assets/Prefabs/Effect/LiquidBeam.prefab）。空でも動く")]
    [SerializeField] private LiquidBeam beamPrefab;

    [Header("撃ち方（2026/11/17 の変更案）")]
    [Tooltip("Whip：押すとすぐ狙った向きへ光線が出て、最長 Whip Seconds 秒出し続けられる。向きを変えると鞭のようにしなり、どこかが物資に触れたらつかむ。\nAim：今までどおり、押して溜めて長さを決め、離すとまっすぐ伸びる\nSteerSnap・SteerLag（曲げ撃ち）：体はあまり回らず、光線の先が狙いの向きへ曲がる（下の「曲げ撃ち」の値）")]
    [SerializeField] private ShotStyle shotStyle = ShotStyle.Whip;

    [Tooltip("ON：ロビーコンソールでホストが決めたプレイヤー挙動「1 狙う・2 発射」（A＝鞭／B＝溜め／C・D＝曲げ撃ち）に合わせる。変わると、次に押したときから変わる（インスペクターで変えた Shot Style は、挙動が変わるまでそのまま）。通信なしのときは、このPCに保存した値。\nOFF：いつもインスペクターの Shot Style")]
    [SerializeField] private bool followSettings = true;

    [Header("鞭の光線（撃ち方が Whip のとき）")]
    [Tooltip("光線を出し続けられる秒数")]
    [Min(0.1f)]
    [SerializeField] private float whipSeconds = 1f;

    [Tooltip("ON：離したらそこで光線が戻り始める（押している間だけ出る。最長 Whip Seconds 秒）。OFF：1回押せば Whip Seconds 秒出る")]
    [SerializeField] private bool whipHoldToKeep = true;

    [Tooltip("光線の先が体の向きに追いつく速さ（ばねの硬さ。大きいほどすぐ追いつき、小さいほど遅れて大きくしなる）")]
    [Min(0.5f)]
    [SerializeField] private float whipStiffness = 7f;

    [Tooltip("しなりの止まりやすさ（1 で行き過ぎない、小さいほど体の向きを行き過ぎてからビヨンと戻る）")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float whipDamping = 0.35f;

    [Tooltip("光線の先が、体の向きから遅れられる最大の角度（度）。大きすぎると光線が輪になる")]
    [Range(0f, 150f)]
    [SerializeField] private float maxWhipAngle = 120f;

    [Tooltip("光線を出している間に歩ける速さ（ふだんの速さに掛ける割合）。0 で歩けない（向きは変えられる）")]
    [Range(0f, 1f)]
    [SerializeField] private float whipMoveScale = 0f;

    [Header("曲げ撃ち（撃ち方が SteerSnap＝挙動パターン C／SteerLag＝D のとき。2026/11/18）")]
    [Tooltip("挙動パターン C（光線の先がすぐ狙いへ向く）の値")]
    [SerializeField] private SteerSettings steerSnap = new SteerSettings(true);

    [Tooltip("挙動パターン D（光線の先が遅れて狙いへ付いていく）の値")]
    [SerializeField] private SteerSettings steerLag = new SteerSettings(false);

    [Header("伸ばせる長さ（プレイヤーの中心から m）")]
    [Tooltip("押した瞬間の長さ")]
    [Min(0.5f)]
    [SerializeField] private float minReach = 3f;

    [Tooltip("いちばん長く伸ばせる長さ")]
    [Min(0.5f)]
    [SerializeField] private float maxReach = 12f;

    [Tooltip("押しっぱなしで、いちばん長くなるまでの秒数")]
    [Min(0.01f)]
    [SerializeField] private float chargeSeconds = 1f;

    [Header("光線の速さ（m/秒）")]
    [Tooltip("光線が伸びる速さ")]
    [Min(0.1f)]
    [SerializeField] private float extendSpeed = 30f;

    [Tooltip("何にも当たらなかったとき、光線が戻る速さ")]
    [Min(0.1f)]
    [SerializeField] private float retractSpeed = 40f;

    [Header("当たり判定")]
    [Tooltip("光線の先の、当たりを見る球の半径（m）。大きいほど当てやすい")]
    [Min(0.05f)]
    [SerializeField] private float hitRadius = 0.45f;

    [Tooltip("当たりを見る高さ（足元から m）。低い物資にも当たるように、腰より低めにしてある")]
    [Min(0f)]
    [SerializeField] private float castHeight = 0.6f;

    [Tooltip("光線が当たる相手のレイヤー")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Tooltip("ON：引っ掛けられない物（壁など）に当たったら、そこで戻り始める。OFF：すり抜けて最後まで伸びる")]
    [SerializeField] private bool stopAtWalls = true;

    [Header("光線の見た目")]
    [Tooltip("光線が出てくる手元の高さ（足元から m）")]
    [Min(0f)]
    [SerializeField] private float handHeight = 1.0f;

    [Tooltip("光線が出てくる手元の、体の前への出っぱり（m）")]
    [Min(0f)]
    [SerializeField] private float handForward = 0.35f;

    [Tooltip("何もつかんでいない間の色の薄さ（0 でチームの色そのまま、1 で白）")]
    [Range(0f, 1f)]
    [SerializeField] private float idleWhiten = 0.45f;

    [Header("巻き取り中の歩き（2026/11/10）")]
    [Tooltip("巻き取り中に歩ける速さ（ふだんの速さに掛ける割合）。0 で今までどおり動けない、1 でふだんと同じ")]
    [Range(0f, 1f)]
    [SerializeField] private float reelMoveScale = 0.25f;

    [Tooltip("重い物を引きずっている間に歩ける速さ（ふだんの速さに掛ける割合）。重い物に踏ん張っている感じを出すため、ふつうより遅くする")]
    [Range(0f, 1f)]
    [SerializeField] private float heavyReelMoveScale = 0.1f;

    [Header("巻き取り中のしなり（向きを変えたとき。2026/11/4）")]
    [Tooltip("光線の曲がる強さ（0 でしならない＝いつもまっすぐ、1 で手元から出る向きがちょうど『しなりの向き』になる）")]
    [Range(0f, 1.5f)]
    [SerializeField] private float bendStrength = 1f;

    [Tooltip("しなりの向きが体の向きに追いつく速さ（ばねの硬さ。大きいほどすぐ追いつき、小さいほど遅れてゆっくりしなる）")]
    [Min(0.5f)]
    [SerializeField] private float bendStiffness = 9f;

    [Tooltip("しなりの止まりやすさ（1 で行き過ぎない、小さいほど体の向きを行き過ぎてからビヨンと戻る）")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float bendDamping = 0.45f;

    [Tooltip("光線が曲がれる最大の角度（度）。物資への向きと手元から出る向きの差をこれまでに抑える（大きすぎると光線が輪になる）")]
    [Range(0f, 150f)]
    [SerializeField] private float maxBendAngle = 110f;

    private PlayerInputSource input;
    private PlayerStun stun;
    private InputActionMap playerMap;
    private InputAction attackAction;
    private LiquidBeam beam;

    /// <summary>つかんだ物資の縁取り（光線のプレハブの子の LiquidWrap。プレハブを使わないときは null で、縁取りなし）。</summary>
    private LiquidWrap wrap;

    private ShotPhase phase = ShotPhase.Idle;
    private float charge;
    private float targetDistance;
    private float traveled;
    private Vector3 launchOrigin;
    private Vector3 launchDirection = Vector3.forward;
    private Color teamColor = new Color(0.3f, 0.6f, 0.95f);

    // しなり：手元から光線が出る向き（真上から見た角度・度）と、その回る速さ。巻き取り中だけ使う
    private bool bendActive;
    private float bendYaw;
    private float bendYawSpeed;

    /// <summary>最後に見た、プレイヤーの中心から物資への向き（真上から見た角度・度）。</summary>
    private float bendTipYaw;

    // 鞭の光線（2026/11/17）：いま出ている光線が鞭か、出してからの秒数、光線の先の向き（真上から見た角度・度）とその回る速さ
    private bool whipShot;
    private float whipTime;
    private float whipTipYaw;
    private float whipTipYawSpeed;

    // 曲げ撃ち（2026/11/18）：いま出ている光線の値（鞭・溜めのときは null）、撃った瞬間の体の向き（真上から見た角度・度）、
    // 光線の先の位置（当たりを見る高さ）と、先が飛んできた道のりの長さ。先の進む向きは whipTipYaw を使う
    private SteerSettings steer;
    private float steerLaunchYaw;
    private Vector3 steerTip;
    private float steerLength;

    /// <summary>鞭の当たりを見る点の数（手元〜先）。多いほど細かく見るが重くなる。</summary>
    private const int WhipSamples = 16;

    private readonly Vector3[] whipPoints = new Vector3[WhipSamples + 1];
    private readonly Vector3[] whipLastPoints = new Vector3[WhipSamples + 1];
    private bool whipHasLastPoints;

    /// <summary>いまの撃ち方。オンラインのほかの人の体では、本人から届いた撃ち方が毎フレーム入る（<see cref="SweeperNetSync"/>）。</summary>
    public ShotStyle Style
    {
        get => shotStyle;
        set => shotStyle = value;
    }

    /// <summary>インスペクター（プレハブ）に入っていた撃ち方。設定に合わせるのをやめたときに戻す先。</summary>
    private ShotStyle inspectorStyle;

    /// <summary>最後に合わせた、プレイヤー挙動「狙う」のパターン。変わったときだけ合わせ直すために持つ。</summary>
    private PlayerBehaviorPattern appliedSetting;

    /// <summary>Awake が済んだか。</summary>
    private bool awoken;

    /// <summary>
    /// ロビーコンソールのプレイヤー挙動「狙う・発射」に合わせるか（2026/11/18。前はポーズ画面の設定「撃ち方」）。
    /// OFF にした瞬間、インスペクターの撃ち方に戻る。
    /// </summary>
    public bool FollowSettings
    {
        get => followSettings;
        set
        {
            followSettings = value;

            // まだ Awake の前なら、Awake で合わせる（インスペクターの撃ち方も、そこで覚える）
            if (!awoken)
            {
                return;
            }

            if (value)
            {
                ApplySetting(true);
            }
            else
            {
                shotStyle = inspectorStyle;
            }
        }
    }

    /// <summary>
    /// プレイヤー挙動「狙う」のパターンを Shot Style に入れる（A＝鞭、B＝溜め。「発射」は固定の組なので同じ）。
    /// <paramref name="force"/> が false なら、パターンが変わったときだけ入れる。
    /// </summary>
    private void ApplySetting(bool force)
    {
        PlayerBehaviorPattern setting = PlayerBehavior.Get(PlayerBehaviorStep.Aim);
        if (!force && setting == appliedSetting)
        {
            return;
        }

        appliedSetting = setting;
        shotStyle = StyleOf(setting);
    }

    /// <summary>プレイヤー挙動「狙う・発射」のパターンにあたる撃ち方（A＝鞭、B＝溜め、C＝曲げ撃ち、D＝曲げ撃ち（遅れ））。</summary>
    public static ShotStyle StyleOf(PlayerBehaviorPattern pattern)
    {
        switch (pattern)
        {
            case PlayerBehaviorPattern.B: return ShotStyle.Aim;
            case PlayerBehaviorPattern.C: return ShotStyle.SteerSnap;
            case PlayerBehaviorPattern.D: return ShotStyle.SteerLag;
            default: return ShotStyle.Whip;
        }
    }

    /// <summary>押した瞬間に光線が出る撃ち方（鞭・曲げ撃ち）か。溜め（Aim）だけが false。</summary>
    public static bool IsWhipFamily(ShotStyle style)
    {
        return style != ShotStyle.Aim;
    }

    /// <summary>その撃ち方の曲げ撃ちの値。曲げ撃ちでなければ null。</summary>
    private SteerSettings SteerOf(ShotStyle style)
    {
        switch (style)
        {
            case ShotStyle.SteerSnap: return steerSnap;
            case ShotStyle.SteerLag: return steerLag;
            default: return null;
        }
    }

    /// <summary>鞭の光線を出している最中か（ボットは、この間ボタンを押し続ける）。</summary>
    public bool IsWhipping => whipShot && phase == ShotPhase.Extending;

    /// <summary>押して狙っている最中か。</summary>
    public bool IsCharging => phase == ShotPhase.Charging;

    /// <summary>光線が出ている（伸びている・戻っている・巻き取っている）か。伸びている・戻っている間は歩けず、巻き取り中はゆっくり歩ける。</summary>
    public bool IsBeamOut => phase == ShotPhase.Extending || phase == ShotPhase.Retracting || phase == ShotPhase.Reeling;

    /// <summary>いま離したら、光線がどこまで伸びるか（プレイヤーの中心から m）。</summary>
    public float CurrentReach => Mathf.Lerp(minReach, Mathf.Max(minReach, maxReach), charge);

    /// <summary>いちばん長く伸ばしたときの長さ（プレイヤーの中心から m）。ボットが撃つ場所を決めるのに使う。</summary>
    public float MaxReach => Mathf.Max(minReach, maxReach);

    /// <summary>光線の先の、当たりを見る球の半径（m）。</summary>
    public float HitRadius => hitRadius;

    /// <summary>いまの段階の名前（検証用の表示に使う）。</summary>
    public string PhaseName => phase.ToString();

    /// <summary>いまの段階。</summary>
    public ShotPhase Phase => phase;

    /// <summary>狙っている向き（狙っている間）／光線を撃った向き（撃ったあと）。</summary>
    public Vector3 ShotDirection => phase == ShotPhase.Charging && aim != null ? aim.Direction : launchDirection;

    /// <summary>光線の先の位置（巻き取り中は物資の引っ掛けた所）。</summary>
    public Vector3 BeamTip => phase == ShotPhase.Reeling && reel != null && reel.Item != null
        ? reel.Item.AnchorPoint
        : steer != null
            ? steerTip
            : whipShot
                ? WhipOrigin + YawToDirection(whipTipYaw) * Mathf.Max(0f, traveled)
                : launchOrigin + launchDirection * Mathf.Max(0f, traveled);

    /// <summary>巻き取っている物資。巻き取っていなければ null。</summary>
    public HookableObject ReelItem => phase == ShotPhase.Reeling && reel != null ? reel.Item : null;

    /// <summary>
    /// **ほかの人の体で**、本人から届いた「巻き取っている物資」。巻き取っていなければ null（本人の体ではいつも null）。
    /// クリティカルの合図を、巻き取りが終わったら消すのに使う（<see cref="SweeperReel"/>）。
    /// </summary>
    public HookableObject ShownReelItem { get; private set; }

    /// <summary>
    /// **巻き取り中に、手元から光線が出ている向き**（真上から見た向き。しなりの向き）。
    /// 向きを変えると、体の向きに少し遅れて付いていき、行き過ぎてから戻る。向きを変えなければ物資への向きのまま。
    /// <see cref="SweeperReel"/> は物資をこの向きの側へ回り込ませる。巻き取っていなければ false。
    /// </summary>
    public bool TryGetBendDirection(out Vector3 direction)
    {
        direction = YawToDirection(bendYaw);
        return bendActive;
    }

    /// <summary>
    /// **巻き取り中の光線の形を、点の並びで返す**（手元 → 物資。しなっていれば、見た目と同じ曲線）。
    /// <paramref name="points"/> の数だけ点を入れ、入れた数を返す（まっすぐなら 2）。巻き取っていなければ 0。
    /// <see cref="SweeperReel"/> が「光線が動く物をつらぬいていないか」を調べるのに使う（2026/11/10）。
    /// </summary>
    public int GetBeamPath(Vector3[] points)
    {
        if (points == null || points.Length < 2 || phase != ShotPhase.Reeling)
        {
            return 0;
        }

        Vector3 hand = HandPosition;
        Vector3 tip = BeamTip;
        Vector3 toTip = tip - hand;
        float length = toTip.magnitude;

        if (!bendActive || bendStrength <= 0f || length < 0.001f)
        {
            points[0] = hand;
            points[1] = tip;
            return 2;
        }

        // LiquidBeam.Follow と同じ曲線（2次ベジェ）。真ん中の目印を、手元からしなりの向きへずらす
        Vector3 direction = toTip / length;
        float turn = Mathf.DeltaAngle(bendTipYaw, bendYaw);
        Vector3 startDirection = (Quaternion.AngleAxis(turn, Vector3.up) * toTip).normalized;
        Vector3 middle = hand + direction * (length * 0.5f) + (startDirection - direction) * (length * 0.5f * bendStrength);

        int count = points.Length;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float u = 1f - t;
            points[i] = u * u * hand + 2f * u * t * middle + t * t * tip;
        }

        return count;
    }

    /// <summary>
    /// **光線がちぎれた見た目を出す**（<see cref="SweeperReel"/> がちぎれたときに呼ぶ。オンラインのほかの人の体では、本人から知らせが来たとき）。
    /// 光線は手元へ縮みながら消え、物資の縁取りははなす。
    /// </summary>
    public void PlaySnap()
    {
        beam?.Snap();
        wrap?.Release();

        // ほかの人の体：まだ「巻き取り中」が届いていても、光線を物資へ描き直さない
        if (remotePhase == ShotPhase.Reeling)
        {
            remoteSnapped = true;
        }
    }

    /// <summary>
    /// **撃っている光線をしまい、巻き取りもやめる**（物資は投げずに離す）。
    /// オンラインで、出てくる場所へ戻されたときなどに呼ぶ（<see cref="SweeperNetSync"/>）。
    /// </summary>
    public void CancelShot()
    {
        if (phase == ShotPhase.Idle)
        {
            return;
        }

        reel?.Cancel();
        EndShot();
    }

    private void Awake()
    {
        if (aim == null) aim = GetComponent<SweeperAim>();
        if (mover == null) mover = GetComponent<SweeperMover>();
        if (reel == null) reel = GetComponent<SweeperReel>();
        if (aimArea == null) aimArea = GetComponentInChildren<SweeperAimArea>();
        if (ring == null) ring = GetComponent<SweeperRing>();
        if (animationPlayer == null) animationPlayer = GetComponent<SweeperAnimation>();

        stun = GetComponent<PlayerStun>();
        input = PlayerInputSource.Get(this);

        inspectorStyle = shotStyle;
        awoken = true;
        if (followSettings)
        {
            ApplySetting(true);
        }

        if (inputActions == null)
        {
            Debug.LogError($"{name}: 入力の設定（InputSystem_Actions）が入っていません。", this);
            enabled = false;
            return;
        }

        // 入力の設定は、この体専用の複製を使う（ほかの体が止めたときに、自分の入力まで止まらないように）
        inputActions = Instantiate(inputActions);
        playerMap = inputActions.FindActionMap("Player", true);
        attackAction = playerMap.FindAction("Attack", true);

        beam = CreateBeam();
        wrap = beam != null ? beam.GetComponentInChildren<LiquidWrap>(true) : null;
    }

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            Destroy(inputActions);
        }
    }

    private void OnEnable()
    {
        playerMap?.Enable();
    }

    private void OnDisable()
    {
        playerMap?.Disable();
        if (phase != ShotPhase.Idle)
        {
            reel?.Cancel();
            EndShot();
        }
    }

    /// <summary>色を変える（<see cref="SweeperPlayer"/> がチームの色を入れる）。</summary>
    public void SetColor(Color color)
    {
        teamColor = color;
        if (beam != null && phase != ShotPhase.Reeling)
        {
            beam.SetColor(IdleColor);
        }
    }

    private Color IdleColor => Color.Lerp(teamColor, Color.white, idleWhiten);

    private void Update()
    {
        bool blocked = GamePause.BlocksInput;
        bool stunned = stun != null && stun.IsStunned;
        bool pressed = !blocked && input.AttackPressed(attackAction);
        bool released = !blocked && input.AttackReleased(attackAction);

        switch (phase)
        {
            case ShotPhase.Idle:
                if (pressed && !stunned)
                {
                    // ロビーコンソールで挙動が変えられていたら、ここで合わせる（撃っている途中では変えない）
                    if (followSettings)
                    {
                        ApplySetting(false);
                    }

                    if (IsWhipFamily(shotStyle))
                    {
                        BeginWhip();
                    }
                    else
                    {
                        BeginCharge();
                    }
                }
                break;

            case ShotPhase.Charging:
                UpdateCharge(released, stunned);
                break;

            case ShotPhase.Extending:
                if (whipShot)
                {
                    UpdateWhip(released, stunned);
                }
                else
                {
                    UpdateExtend();
                }
                break;

            case ShotPhase.Retracting:
                if (whipShot && steer == null)
                {
                    UpdateWhipSpring(Time.deltaTime);
                }
                traveled -= retractSpeed * Time.deltaTime;
                if (steer != null)
                {
                    ShrinkSteerTip();
                }
                if (traveled <= 0f)
                {
                    EndShot();
                }
                break;

            case ShotPhase.Reeling:
                if (pressed)
                {
                    reel.Press();
                }
                if (!reel.IsActive)
                {
                    EndShot();
                    break;
                }
                AllowSlowWalk();
                break;
        }
    }

    /// <summary>
    /// 巻き取り中は止めずに、ゆっくり歩けるようにする（2026/11/10）。
    /// 物資はいつも「いまのプレイヤーの位置」へ引き寄せられるので（<see cref="SweeperReel"/>）、歩いても着く時間とクリティカルのタイミングは変わらない。
    /// 体の向きは狙いのまま（<see cref="SweeperMover.FaceAim"/>）なので、歩いても光線はしならない。
    /// </summary>
    private void AllowSlowWalk()
    {
        if (mover == null)
        {
            return;
        }

        float scale = reel.IsHeavyDrag ? heavyReelMoveScale : reelMoveScale;
        mover.MoveLocked = scale <= 0f;
        mover.MoveSpeedScale = scale;
    }

    private void LateUpdate()
    {
        UpdateBend(phase == ShotPhase.Reeling, BeamTip, Time.deltaTime);

        if (beam == null || !IsBeamOut)
        {
            return;
        }

        FollowBeam(BeamTip);
    }

    /// <summary>光線を手元から <paramref name="tip"/> まで描く。巻き取り中は、しなりの向きに曲げる。鞭の光線は、体の向きに出て先へ曲がる。</summary>
    private void FollowBeam(Vector3 tip)
    {
        if (whipShot && phase != ShotPhase.Reeling)
        {
            FollowWhip(tip);
        }
        else if (bendActive)
        {
            // まっすぐの光線（手元→物資。下り坂の傾きを含む）を、物資への向きとしなりの向きの差だけ横に回した向きに出す。
            // 傾きはそのまま残すので、向きを変えていなければ差が 0 で、上下にも曲がらない
            Vector3 hand = HandPosition;
            float turn = Mathf.DeltaAngle(bendTipYaw, bendYaw);
            Vector3 startDirection = Quaternion.AngleAxis(turn, Vector3.up) * (tip - hand);
            beam.Follow(hand, tip, startDirection, bendStrength);
        }
        else
        {
            beam.Follow(HandPosition, tip);
        }
    }

    /// <summary>
    /// しなりの向きを1フレームぶん進める。巻き取りが始まった瞬間は物資への向き（＝まっすぐ）から始め、
    /// あとは体の向きへ、ばね（<see cref="bendStiffness"/>・<see cref="bendDamping"/>）で遅れて付いていく。
    /// 物資への向きとの差は <see cref="maxBendAngle"/> までに抑える。
    /// </summary>
    private void UpdateBend(bool reeling, Vector3 tip, float deltaTime)
    {
        if (!reeling)
        {
            bendActive = false;
            return;
        }

        Vector3 toTip = tip - transform.position;
        toTip.y = 0f;
        float tipYaw = toTip.sqrMagnitude > 0.0001f ? DirectionToYaw(toTip) : DirectionToYaw(transform.forward);
        bendTipYaw = tipYaw;

        if (!bendActive)
        {
            bendActive = true;
            bendYaw = tipYaw;
            bendYawSpeed = 0f;
            return;
        }

        if (deltaTime <= 0f)
        {
            return;
        }

        // 処理が一瞬止まったとき（大きな deltaTime）に、ばねが暴れないようにする
        deltaTime = Mathf.Min(deltaTime, 0.05f);

        // 減衰するばね：行きたい向き（体の向き）との差に引かれ、速さに比例して止められる
        float omega = bendStiffness;
        float gap = Mathf.DeltaAngle(bendYaw, DirectionToYaw(transform.forward));
        float accel = omega * omega * gap - 2f * bendDamping * omega * bendYawSpeed;
        bendYawSpeed += accel * deltaTime;
        bendYaw += bendYawSpeed * deltaTime;

        // 曲がりすぎ（光線が輪になる）を防ぐ
        float bent = Mathf.DeltaAngle(tipYaw, bendYaw);
        if (Mathf.Abs(bent) > maxBendAngle)
        {
            bendYaw = tipYaw + Mathf.Sign(bent) * maxBendAngle;
            bendYawSpeed = 0f;
        }
    }

    private static float DirectionToYaw(Vector3 direction)
    {
        return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }

    private static Vector3 YawToDirection(float yaw)
    {
        float radians = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }

    // ------------------------------------------------------------
    // オンラインのほかの人の体（2026/10/28）
    // ------------------------------------------------------------

    /// <summary>ほかの人の体で、最後に見せた段階。段階が変わった瞬間だけ色やアニメーションを切り替えるために持つ。</summary>
    private ShotPhase remotePhase = ShotPhase.Idle;

    /// <summary>ほかの人の体で、巻き取り中に「ちぎれた」が届いたか。段階が変わるまで光線を描き直さない。</summary>
    private bool remoteSnapped;

    /// <summary>
    /// **ほかの人の体に、本人から届いた光線の様子をそのまま見せる。**（<see cref="SweeperNetSync"/> が毎フレーム呼ぶ）
    /// ほかの人の体ではこの部品は止まっている（操作はしない）ので、見た目だけをここで出す。
    /// </summary>
    public void ShowRemote(ShotPhase newPhase, Vector3 direction, float reach, Vector3 tip, HookableObject item)
    {
        ShownReelItem = newPhase == ShotPhase.Reeling ? item : null;

        if (newPhase != remotePhase)
        {
            OnRemotePhaseChanged(remotePhase, newPhase);
            remotePhase = newPhase;
            remoteSnapped = false;
        }

        if (newPhase == ShotPhase.Charging)
        {
            float ringRadius = ring != null ? ring.Radius : 0.5f;
            aimArea?.Show(transform.position, direction, ringRadius, reach);
            return;
        }

        aimArea?.Hide();

        // しなりは、ほかの人の画面でもこの体の向き（通信で届く）から同じように計算する
        Vector3 shownTip = newPhase == ShotPhase.Reeling && item != null ? item.AnchorPoint : tip;
        UpdateBend(newPhase == ShotPhase.Reeling, shownTip, Time.deltaTime);

        if (beam != null && newPhase != ShotPhase.Idle && !(remoteSnapped && newPhase == ShotPhase.Reeling))
        {
            // 鞭の光線は、届いた光線の先と、この体の向き（通信で届く）から同じ曲がり方を描く
            if (IsWhipFamily(shotStyle) && newPhase != ShotPhase.Reeling)
            {
                // 曲げ撃ちは、届いた「先の進む向き」で曲がり方を決める
                if (SteerOf(shotStyle) != null && direction.sqrMagnitude > 0.0001f)
                {
                    whipTipYaw = DirectionToYaw(direction);
                }
                FollowWhip(shownTip);
            }
            else
            {
                FollowBeam(shownTip);
            }
        }
    }

    private void OnRemotePhaseChanged(ShotPhase before, ShotPhase after)
    {
        switch (after)
        {
            case ShotPhase.Idle:
                StopBeam();
                wrap?.Release();
                aimArea?.Hide();
                animationPlayer?.PlayIdle();
                break;

            case ShotPhase.Charging:
                animationPlayer?.PlayAim();
                break;

            case ShotPhase.Extending:
                beam?.SetColor(IdleColor);
                animationPlayer?.PlayShoot();
                break;

            case ShotPhase.Reeling:
                beam?.ChangeColorFromTip(teamColor);
                PlayWrap(ShownReelItem);
                break;
        }

        // 巻き取りが終わったら縁取りをはなす（Idle 以外へ移ったとき。Idle は上で処理済み）
        if (before == ShotPhase.Reeling && after != ShotPhase.Idle)
        {
            wrap?.Release();
        }
    }

    /// <summary>つかんだ物資を液体で縁取り始める。物資が無ければ何もしない。</summary>
    private void PlayWrap(HookableObject item)
    {
        if (wrap != null && item != null)
        {
            wrap.Play(item.transform, item.AnchorPoint, teamColor);
        }
    }

    /// <summary>光線が出てくる手元の位置。</summary>
    private Vector3 HandPosition
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            return transform.position + Vector3.up * handHeight + forward * handForward;
        }
    }

    private void BeginCharge()
    {
        whipShot = false;
        phase = ShotPhase.Charging;
        charge = 0f;
        if (mover != null) mover.FaceAim = true;
        animationPlayer?.PlayAim();
    }

    private void UpdateCharge(bool released, bool stunned)
    {
        if (stunned)
        {
            // スタンしたら、撃たずにやめる
            aimArea?.Hide();
            EndShot();
            return;
        }

        charge = Mathf.Clamp01(charge + Time.deltaTime / chargeSeconds);

        float ringRadius = ring != null ? ring.Radius : 0.5f;
        aimArea?.Show(transform.position, aim != null ? aim.Direction : transform.forward, ringRadius, CurrentReach);

        if (released)
        {
            Fire();
        }
    }

    private void Fire()
    {
        aimArea?.Hide();
        whipShot = false;

        launchDirection = aim != null ? aim.Direction : transform.forward;
        launchDirection.y = 0f;
        launchDirection = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : Vector3.forward;
        launchOrigin = transform.position + Vector3.up * castHeight;
        targetDistance = CurrentReach;
        traveled = 0f;
        phase = ShotPhase.Extending;

        // 撃った瞬間から投げ終わるまで歩けない（向きは狙いで変えられる）
        if (mover != null) mover.MoveLocked = true;

        if (beam != null)
        {
            beam.SetColor(IdleColor);
        }

        animationPlayer?.PlayShoot();
    }

    private void UpdateExtend()
    {
        float step = extendSpeed * Time.deltaTime;
        float from = traveled;
        float to = Mathf.Min(targetDistance, traveled + step);

        if (TryHit(from, to, out RaycastHit hit, out HookableObject hookable))
        {
            traveled = hit.distance + from;

            if (hookable != null)
            {
                phase = ShotPhase.Reeling;
                reel.Begin(hookable);
                beam?.ChangeColorFromTip(teamColor);
                PlayWrap(hookable);
                return;
            }

            // 壁などに当たった：そこから戻る
            phase = ShotPhase.Retracting;
            return;
        }

        traveled = to;
        if (traveled >= targetDistance)
        {
            phase = ShotPhase.Retracting;
        }
    }

    // ------------------------------------------------------------
    // 鞭の光線（2026/11/17・小野田さんの変更案）
    // ------------------------------------------------------------

    /// <summary>鞭の光線の根元（体の中心の、当たりを見る高さ）。光線の先は、ここから <see cref="whipTipYaw"/> の向きへ traveled だけ離れた所。</summary>
    private Vector3 WhipOrigin => transform.position + Vector3.up * castHeight;

    /// <summary>体の向き（水平・長さ1）。</summary>
    private Vector3 BodyForward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    /// <summary>押した瞬間：狙った向きへ、溜めずにすぐ光線を出し始める。</summary>
    private void BeginWhip()
    {
        Vector3 direction = aim != null ? aim.Direction : BodyForward;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : BodyForward;

        // ふだんは歩く向きを向いているので、狙った向きへすぐ出るように体を回してしまう
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        whipShot = true;
        whipTime = 0f;
        whipTipYaw = DirectionToYaw(direction);
        whipTipYawSpeed = 0f;
        whipHasLastPoints = false;
        launchDirection = direction;
        traveled = 0f;
        charge = 0f;
        phase = ShotPhase.Extending;

        // 曲げ撃ち（C・D）：体の向きはこの部品が決める（少しだけしか回さない）。鞭（A）：体は狙いへ回り、先が遅れてしなる
        steer = SteerOf(shotStyle);
        steerLaunchYaw = whipTipYaw;
        steerTip = WhipOrigin + direction * handForward;
        steerLength = 0f;
        float moveScale = steer != null ? steer.moveScale : whipMoveScale;

        if (mover != null)
        {
            mover.FaceAim = steer == null;
            mover.TurnLocked = steer != null;
            mover.MoveLocked = moveScale <= 0f;
            mover.MoveSpeedScale = moveScale;
        }

        beam?.SetColor(IdleColor);
        animationPlayer?.PlayShoot();
    }

    /// <summary>出している間：先を伸ばし、しならせ、光線のどこかが物資に触れたらつかむ。時間が来たら（離したら）戻り始める。</summary>
    private void UpdateWhip(bool released, bool stunned)
    {
        float deltaTime = Time.deltaTime;
        whipTime += deltaTime;
        if (steer != null)
        {
            UpdateSteer(deltaTime);
        }
        else
        {
            UpdateWhipSpring(deltaTime);
            traveled = Mathf.Min(MaxReach, traveled + extendSpeed * deltaTime);
        }

        if (TryWhipHit(out HookableObject hookable))
        {
            GrabByWhip(hookable);
            return;
        }

        float seconds = steer != null ? steer.seconds : whipSeconds;
        bool holdToKeep = steer != null ? steer.holdToKeep : whipHoldToKeep;
        if (stunned || whipTime >= seconds || (holdToKeep && released))
        {
            phase = ShotPhase.Retracting;
        }
    }

    /// <summary>
    /// **曲げ撃ち（C・D）の1フレーム。** 体は撃った向きから狙いの方へ、最大 <see cref="SteerSettings.maxBodyTurn"/> 度までゆっくり回る。
    /// 光線の先の進む向きは狙いの向きへ（C はすぐ、D はばねで遅れて）変わる（体の向きからは <see cref="SteerSettings.maxTipAngle"/> 度まで）。
    /// 先はその向きへ <see cref="SteerSettings.headSpeed"/> で進み、飛んできた道のりが <see cref="MaxReach"/> になったら止まる。
    /// </summary>
    private void UpdateSteer(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        // 処理が一瞬止まったとき（大きな deltaTime）に、ばねが暴れないようにする
        float springTime = Mathf.Min(deltaTime, 0.05f);
        SteerHeading(springTime);

        // 先を進める（伸びきったら止まる）
        float step = Mathf.Min(steer.headSpeed * deltaTime, Mathf.Max(0f, MaxReach - steerLength));
        if (step > 0f)
        {
            steerTip += YawToDirection(whipTipYaw) * step;
            steerLength += step;
        }

        SyncSteerTraveled();
    }

    /// <summary>曲げ撃ちの先の、根元からの水平の距離を traveled に入れる（戻るときは、ここから縮める）。</summary>
    private void SyncSteerTraveled()
    {
        Vector3 offset = steerTip - WhipOrigin;
        offset.y = 0f;
        traveled = offset.magnitude;
        launchDirection = YawToDirection(whipTipYaw);
    }

    /// <summary>曲げ撃ちの光線が戻る間：先を、根元への向きのまま traveled の距離まで寄せる（曲がりの形は、先の進む向きから描き直される）。</summary>
    private void ShrinkSteerTip()
    {
        Vector3 offset = steerTip - WhipOrigin;
        offset.y = 0f;
        Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : BodyForward;
        steerTip = WhipOrigin + direction * Mathf.Max(0f, traveled);
    }

    /// <summary>曲げ撃ち：体を少しだけ狙いへ回し、光線の先の進む向き（<see cref="whipTipYaw"/>）を狙いへ向ける。</summary>
    private void SteerHeading(float deltaTime)
    {
        float aimYaw = DirectionToYaw(AimDirection);

        // 体：撃った向きから、狙いの方へ少しだけ
        float bodyTarget = steerLaunchYaw + Mathf.Clamp(Mathf.DeltaAngle(steerLaunchYaw, aimYaw), -steer.maxBodyTurn, steer.maxBodyTurn);
        float bodyYaw = Mathf.MoveTowardsAngle(DirectionToYaw(BodyForward), bodyTarget, steer.bodyTurnSpeed * deltaTime);
        transform.rotation = Quaternion.LookRotation(YawToDirection(bodyYaw), Vector3.up);

        // 先：狙いの向き（体の向きから曲がりすぎない所まで）
        float tipTarget = bodyYaw + Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, aimYaw), -steer.maxTipAngle, steer.maxTipAngle);
        if (steer.tipSnaps)
        {
            whipTipYaw = tipTarget;
            whipTipYawSpeed = 0f;
            return;
        }

        // 減衰するばね（鞭と同じ式。行きたい向きが「体の向き」ではなく「狙いの向き」）
        float omega = steer.tipStiffness;
        float gap = Mathf.DeltaAngle(whipTipYaw, tipTarget);
        float accel = omega * omega * gap - 2f * steer.tipDamping * omega * whipTipYawSpeed;
        whipTipYawSpeed += accel * deltaTime;
        whipTipYaw += whipTipYawSpeed * deltaTime;

        float bent = Mathf.DeltaAngle(bodyYaw, whipTipYaw);
        if (Mathf.Abs(bent) > steer.maxTipAngle)
        {
            whipTipYaw = bodyYaw + Mathf.Sign(bent) * steer.maxTipAngle;
            if (Mathf.Sign(whipTipYawSpeed) == Mathf.Sign(bent))
            {
                whipTipYawSpeed = 0f;
            }
        }
    }

    /// <summary>狙っている向き（水平・長さ1）。狙いの部品が無ければ体の向き。</summary>
    private Vector3 AimDirection
    {
        get
        {
            Vector3 direction = aim != null ? aim.Direction : BodyForward;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : BodyForward;
        }
    }

    /// <summary>光線の曲がり方の強さ。曲げ撃ちはパターンごとの値、鞭は巻き取り中のしなりと共通の値。ほかの人の体でも届いた撃ち方で決まる。</summary>
    private float WhipCurveStrength
    {
        get
        {
            SteerSettings settings = SteerOf(shotStyle);
            return settings != null ? settings.curveStrength : bendStrength;
        }
    }

    /// <summary>
    /// 光線の先の向きを1フレームぶん進める。根元は体の向きに出て、先は体の向きへ、ばね（<see cref="whipStiffness"/>・<see cref="whipDamping"/>）で遅れて付いていく。
    /// 巻き取り中のしなり（<see cref="UpdateBend"/>）と同じ考え方で、こちらは「先」が遅れる。
    /// </summary>
    private void UpdateWhipSpring(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        // 処理が一瞬止まったとき（大きな deltaTime）に、ばねが暴れないようにする
        deltaTime = Mathf.Min(deltaTime, 0.05f);

        float bodyYaw = DirectionToYaw(BodyForward);
        float omega = whipStiffness;
        float gap = Mathf.DeltaAngle(whipTipYaw, bodyYaw);
        float accel = omega * omega * gap - 2f * whipDamping * omega * whipTipYawSpeed;
        whipTipYawSpeed += accel * deltaTime;
        whipTipYaw += whipTipYawSpeed * deltaTime;

        // 遅れすぎ（光線が輪になる）を防ぐ。さらに離れていく向きの勢いだけ消す
        float lag = Mathf.DeltaAngle(bodyYaw, whipTipYaw);
        if (Mathf.Abs(lag) > maxWhipAngle)
        {
            whipTipYaw = bodyYaw + Mathf.Sign(lag) * maxWhipAngle;
            if (Mathf.Sign(whipTipYawSpeed) == Mathf.Sign(lag))
            {
                whipTipYawSpeed = 0f;
            }
        }
    }

    /// <summary>当たりを見るための、光線の形の点の並び（根元 → 先。見た目と同じ曲線を、当たりを見る高さで）。</summary>
    private void BuildWhipPoints(Vector3[] points)
    {
        Vector3 forward = BodyForward;
        Vector3 start = WhipOrigin + forward * handForward;
        Vector3 tip = BeamTip;
        Vector3 toTip = tip - start;
        float length = toTip.magnitude;

        // LiquidBeam.Follow と同じ曲線（2次ベジェ）。真ん中の目印を、根元から体の向きへずらす。
        // 曲げ撃ちは、根元からは体の向き、先へは進んできた向きから入る形（LiquidBeam.FollowCurve と同じ）
        Vector3 middle;
        if (steer != null)
        {
            middle = SteerControl(start, forward, tip, whipTipYaw, steer.curveStrength);
        }
        else
        {
            middle = length > 0.001f
                ? start + toTip * 0.5f + (forward - toTip / length) * (length * 0.5f * WhipCurveStrength)
                : start;
        }

        int last = points.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            float t = i / (float)last;
            float u = 1f - t;
            points[i] = u * u * start + 2f * u * t * middle + t * t * tip;
        }
    }

    /// <summary>
    /// 光線のどこかが物資に触れたか。光線の形に沿って根元から順に見て、壁に当たったらそこで光線を短くする（壁の向こうは見ない）。
    /// 速く振ったときに物資を飛び越さないよう、点ごとに「前のフレームの位置 → いまの位置」も見る（こちらは物資だけ）。
    /// </summary>
    private bool TryWhipHit(out HookableObject hookable)
    {
        hookable = null;
        BuildWhipPoints(whipPoints);
        bool hasLast = whipHasLastPoints;
        bool found = false;

        for (int i = 1; i <= WhipSamples && !found; i++)
        {
            // 光線の形に沿って
            Vector3 from = whipPoints[i - 1];
            Vector3 along = whipPoints[i] - from;
            float length = along.magnitude;
            if (length > 0.0001f &&
                TryHitSegment(from, along / length, length, stopAtWalls, out RaycastHit hit, out HookableObject touched))
            {
                if (touched != null)
                {
                    hookable = touched;
                    found = true;
                    break;
                }

                // 壁：光線の先を、当たった所の手前までにする（次のフレームでまた伸びようとして、また止まる）
                Vector3 stop = from + along / length * hit.distance - WhipOrigin;
                stop.y = 0f;
                traveled = Mathf.Min(traveled, stop.magnitude);

                // 曲げ撃ち：先を当たった所まで戻す（戻したぶんは、まだ飛べる道のりに返す。進む向きを変えれば、また進める）
                if (steer != null)
                {
                    Vector3 stopTip = WhipOrigin + stop;
                    steerLength = Mathf.Max(0f, steerLength - Vector3.Distance(steerTip, stopTip));
                    steerTip = stopTip;
                    SyncSteerTraveled();
                }
                break;
            }

            // 前のフレームからの動きに沿って（物資だけ。壁はすり抜ける）
            if (hasLast)
            {
                Vector3 moved = whipPoints[i] - whipLastPoints[i];
                float distance = moved.magnitude;
                if (distance > 0.01f &&
                    TryHitSegment(whipLastPoints[i], moved / distance, distance, false, out _, out HookableObject swept) &&
                    swept != null)
                {
                    hookable = swept;
                    found = true;
                }
            }
        }

        System.Array.Copy(whipPoints, whipLastPoints, whipPoints.Length);
        whipHasLastPoints = true;
        return found;
    }

    /// <summary>鞭で物資をつかんだ：曲がったまま巻き取りへ移る（しなりの続きから始め、物資は前の側へ回り込んでくる）。</summary>
    private void GrabByWhip(HookableObject hookable)
    {
        phase = ShotPhase.Reeling;

        // 曲げ撃ちでつかんだら、体の向きはふだんの巻き取りと同じ（狙いへ回る）に戻す。引っ張る・投げるはパターン A のまま
        if (steer != null && mover != null)
        {
            mover.TurnLocked = false;
            mover.FaceAim = true;
        }
        steer = null;

        bendActive = true;
        bendYaw = DirectionToYaw(BodyForward);
        bendYawSpeed = 0f;

        reel.Begin(hookable);
        beam?.ChangeColorFromTip(teamColor);
        PlayWrap(hookable);
    }

    /// <summary>
    /// **曲げ撃ちの曲線の「真ん中の目印」**（2次ベジェ）。手元 <paramref name="start"/> から体の向き <paramref name="forward"/> へ延ばした線と、
    /// 先 <paramref name="tip"/> から進んできた向き（<paramref name="headingYaw"/>）を逆にたどった線の交わる所。
    /// こうすると、手元からは体の向きに出て、先へは進んできた向きから入る（絵の「まっすぐ出て、曲がって物資へ」の形）。
    /// 交わらない（平行・後ろ側で交わる）ときは、まっすぐ（中点）にする。<paramref name="strength"/> は中点からどれだけ寄せるか（0〜1）。
    /// </summary>
    private static Vector3 SteerControl(Vector3 start, Vector3 forward, Vector3 tip, float headingYaw, float strength)
    {
        Vector3 middle = (start + tip) * 0.5f;
        Vector3 heading = YawToDirection(headingYaw);
        Vector3 toTip = tip - start;
        float cross = forward.x * heading.z - forward.z * heading.x;
        float reach = new Vector2(toTip.x, toTip.z).magnitude;

        if (Mathf.Abs(cross) < 0.01f || reach < 0.01f)
        {
            return middle;
        }

        // start + forward × s ＝ tip − heading × u を解く（真上から見て）
        float s = (toTip.x * heading.z - toTip.z * heading.x) / cross;
        float u = (forward.x * toTip.z - forward.z * toTip.x) / cross;
        if (s <= 0f || u <= 0f)
        {
            return middle;
        }

        // 交わる所が遠すぎる（ほとんど平行）ときに、曲線が大きく外へふくらまないよう、手元から先までの距離までにする
        s = Mathf.Min(s, reach);
        Vector3 corner = start + forward * s;
        corner.y = middle.y;
        return Vector3.Lerp(middle, corner, Mathf.Clamp01(strength));
    }

    /// <summary>鞭の光線を描く。根元からは体の向きに出て、先 <paramref name="tip"/> へ曲がってつながる。</summary>
    private void FollowWhip(Vector3 tip)
    {
        Vector3 hand = HandPosition;
        Vector3 toTip = tip - hand;
        Vector3 flat = toTip;
        flat.y = 0f;

        // 曲げ撃ち（C・D）：手元からは体の向きに出て、先へは進んできた向き（whipTipYaw。ほかの人の体では届いた向き）から入る
        SteerSettings settings = SteerOf(shotStyle);
        if (settings != null)
        {
            beam.FollowCurve(hand, tip, SteerControl(hand, BodyForward, tip, whipTipYaw, settings.curveStrength));
            return;
        }

        float curve = WhipCurveStrength;
        if (flat.sqrMagnitude < 0.0001f || curve <= 0f)
        {
            beam.Follow(hand, tip);
            return;
        }

        // まっすぐの光線（手元→先。下り坂の傾きを含む）を、先への向きと体の向きの差だけ横に回した向きに出す（巻き取り中のしなりと同じ）
        float turn = Mathf.DeltaAngle(DirectionToYaw(flat), DirectionToYaw(BodyForward));
        Vector3 startDirection = Quaternion.AngleAxis(turn, Vector3.up) * toTip;
        beam.Follow(hand, tip, startDirection, curve);
    }

    /// <summary>
    /// 光線の先が <paramref name="from"/> から <paramref name="to"/> まで進む間に、何かに当たるか。
    /// 物資（まだ誰も引っ掛けていない物）に当たれば <paramref name="hookable"/> に入る。
    /// </summary>
    private bool TryHit(float from, float to, out RaycastHit nearest, out HookableObject hookable)
    {
        return TryHitSegment(launchOrigin + launchDirection * from, launchDirection, to - from, stopAtWalls, out nearest, out hookable);
    }

    /// <summary>
    /// <paramref name="start"/> から <paramref name="direction"/> へ <paramref name="distance"/> だけ、当たりを見る球を動かして、いちばん手前の当たりを探す。
    /// 物資（まだ誰も引っ掛けていない物）なら <paramref name="hookable"/> に入る。<paramref name="walls"/> が false なら、壁はすり抜ける。
    /// </summary>
    private bool TryHitSegment(Vector3 start, Vector3 direction, float distance, bool walls, out RaycastHit nearest, out HookableObject hookable)
    {
        nearest = default;
        hookable = null;

        if (distance <= 0f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.SphereCastAll(start, hitRadius, direction, distance, hitMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            // 自分の体には当たらない。最初から重なっている物（distance 0）は、場所が分からないので物資のときだけ使う
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // 重いデブリも引っ掛けられる。投げずに引きずるだけ（SweeperReel。2026/11/5。前は壁と同じ扱いで光線が戻っていた）
            HookableObject candidate = hit.collider.GetComponentInParent<HookableObject>();

            bool usable = candidate != null && !candidate.IsHooked && !candidate.IsVanished && reel.CanHook(candidate);
            if (!usable && (!walls || candidate != null || hit.distance <= 0f))
            {
                // 引っ掛けられない物資（誰かが引いている。オンラインではほかの人が引いている物も）はすり抜ける。壁で止まらない設定のときも同じ
                continue;
            }

            if (!found || hit.distance < nearest.distance)
            {
                nearest = hit;
                hookable = usable ? candidate : null;
                found = true;
            }
        }

        return found;
    }

    /// <summary>光線をしまい、歩けるように戻す。</summary>
    private void EndShot()
    {
        phase = ShotPhase.Idle;
        traveled = 0f;
        charge = 0f;
        whipShot = false;
        whipHasLastPoints = false;
        steer = null;

        if (mover != null)
        {
            mover.MoveLocked = false;
            mover.MoveSpeedScale = 1f;
            mover.FaceAim = false;
            mover.TurnLocked = false;
        }

        StopBeam();
        wrap?.Release();
        aimArea?.Hide();
        animationPlayer?.PlayIdle();
    }

    /// <summary>
    /// 光線を消し始め、色を薄いチームの色へ戻す。**ちぎれて縮んでいる途中なら、そのまま縮ませる**
    /// （色もチームの色のまま。次に撃つときに薄い色へ戻る）。
    /// </summary>
    private void StopBeam()
    {
        if (beam == null || beam.IsSnapping)
        {
            return;
        }

        beam.Stop();
        beam.SetColor(IdleColor);
    }

    private LiquidBeam CreateBeam()
    {
        LiquidBeam made;
        if (beamPrefab != null)
        {
            made = Instantiate(beamPrefab, transform);
        }
        else
        {
            // プレハブが入っていないとき。マテリアルは LiquidBeam が自分で作る
            GameObject child = new GameObject("SweeperBeam");
            child.transform.SetParent(transform, false);
            made = child.AddComponent<LiquidBeam>();
        }

        made.name = "SweeperBeam";
        made.HoldUntilStopped = true;
        return made;
    }
}
