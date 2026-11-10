using System.Collections;
using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の巻き取りと投げ。**（2026/10/27・小野田さんの仕様）
///
/// 光線が物資（<see cref="HookableObject"/>）に当たると、<see cref="SweeperShooter"/> が <see cref="Begin"/> を呼ぶ。
///
/// | いつ | どうなる |
/// | --- | --- |
/// | 当たった瞬間から | 物資を**床に置いたまま**、プレイヤーの手前まで引きずる。**距離に関係なく Reel Seconds（初期値1.5秒）で**着く |
/// | 途中でボタンを押す | **後ろへ投げる。** 押した瞬間の位置から、プレイヤーの頭を越える放物線を描いて後ろへ飛ぶ |
/// | 決まった瞬間（クリティカルの受付）に押す | **クリティカルプル。** 光が出て、もっと高く遠くへ飛ぶ |
/// | 最後まで押さない | 手前に着いたところで、**向いている方向（前）へ投げる** |
///
/// 引く時間を固定しているのは、ボタンを押すタイミングを毎回同じにするため。
///
/// **引いている途中で向きを変えると、物資はしなった光線に沿って、プレイヤーのまわりを前の側へ回り込む**（2026/11/4）。
/// 回り込む先は <see cref="SweeperShooter.TryGetBendDirection"/>（手元から光線が出ている向き）。向きを変えなければ今までどおりまっすぐ来る。
/// 着く時間は変わらない（クリティカルのタイミングも同じ）。前に来てから投げるので、「前へ」「後ろへ」がまっすぐ飛ぶ。
///
/// **回り込んでいる途中で押すと、回っている勢いを残して斜め後ろへ飛ぶ**（2026/11/4）。
/// 飛ぶ向きは「体の真後ろ」と「物資がいま動いている向き（接線）」を Throw Follow Motion の割合で混ぜる。
/// 0 で今までどおり真後ろ、1 で動いている向きそのまま。まっすぐ引いているときはどちらでも真後ろになる。
/// **混ぜる割合は、引き寄せの進み具合で切り替わる**（2026/11/10・小野田さんの案）。道のりの Throw Follow Switch（初期値 8 割）までは
/// Throw Follow Motion（接線に近い向き）、そこを超えたら Throw Follow Near（体の真後ろに、ほんの少しだけ接線を足す。初期値 0.15）。
/// クリティカルの合図は物資のまわりにだけ出す（ゲージなどの画面の表示は出さない）。
/// 1回押したら、それで決まる（連打で当てられないように）。
///
/// ### 重い物（<see cref="HeavyHookable"/>。宇宙ごみの特殊デブリ。2026/11/5）
///
/// 重い物は**投げられず、床の上を引きずるだけ**。光線が当たると、重さに合わせてゆっくり手前へ引きずり、
/// 1回で動かせる距離（Heavy Drag Distance ÷ 重さ）を引いたら、**その場に置いて離す**。途中で押しても、その場で離す。
/// クリティカルの合図は出ない。何回も撃って、少しずつゴールまで運ぶ遊び方になる。
///
/// ### クリティカルのエフェクト（仮。2026/11/4）
///
/// プレハブ（Critical Cue Prefab / Critical Hit Prefab）が空のときは、仮のエフェクトを出す。どれを出すかはインスペクターで選べる（組み合わせてもよい）。
///
/// | いつ | 仮のエフェクト |
/// | --- | --- |
/// | 合図（受付の Cue Lead 秒前から） | 縮んでくる輪（案1）・物資のフチが金色に光る（案4）・前からの広がる輪（受付が始まった瞬間） |
/// | 決まったとき | 星のかけらが飛び散る（案1）・物資が白く光って一瞬止まる（案4）・**推進の衝撃波**（物資の、引っ張られている側と逆の側から輪が噴き出す）・前からの大きな輪 |
///
/// 合図を受付より前から出すのは、受付が0.2秒しかなく、光ったのを見てから押すのでは間に合わないため。
///
/// ### 光線がちぎれる（2026/11/10）
///
/// 動く物（Is Kinematic の Rigidbody。回る床・上下する床・ベルトコンベア・動く障害物など）に物資を押し付けたまま引くと、
/// めり込みを押し戻す力で物資がものすごい勢いで飛んだり、回転が増え続けたりしていた。これを防ぐため、次のどちらかで**光線がちぎれ、物資はその場に落ちる**。
///
/// | ちぎれる条件 | 中身 |
/// | --- | --- |
/// | **たまった力** | 引きたい距離に対して、物資が動けなかった距離をためていく（引っかかりが取れていれば少しずつ減る）。**Snap Strain** m たまったらちぎれる。初期設定では、動く物に触れている間だけためる |
/// | **貫通** | 光線（手元から物資までの曲線）が動く物を **Kinematic Snap Seconds** 秒つらぬいたらちぎれる。**ふつうの壁（Rigidbody が無い物）では、ちぎれない** |
///
/// あわせて、引きずる速さに上限（**Max Drag Speed**）を付けた。前は、引っかかって遅れた分を残りの時間で取り戻そうとして、
/// 最後の一瞬に秒速数百 m を出すことがあった。
///
/// ## オンライン（宇宙ごみのオンラインの体。2026/10/28）
///
/// 物資に <see cref="FishingNetSupply"/> が付いていて通信中なら、今の物資の通信の決まりに合わせる。
/// ・当たったら**ホストに引っ掛けを頼み**、認められて持ち主が自分に移ってから巻き取る（Reel Seconds はそこから数える）。
///   ほかの人が先に引っ掛けていた・返事が来ないときは、投げずにやめる
/// ・途中でホストに外された（爆風など）ら、投げずにやめる
/// ・投げるときは、ここで計算した速さを**そのままホストへ渡す**（落ちた場所・ゴールの判定はホスト）
/// </summary>
public class SweeperReel : MonoBehaviour
{
    [Header("巻き取り")]
    [Tooltip("当たってから手前に着くまでの秒数。距離に関係なく、いつもこの時間で着く")]
    [Min(0.1f)]
    [SerializeField] private float reelSeconds = 1.5f;

    [Tooltip("物資を止める位置（プレイヤーの中心からの距離 m）")]
    [Min(0.3f)]
    [SerializeField] private float stopDistance = 1.4f;

    [Tooltip("引いている間の物資の回転を弱める強さ（0 で弱めない）。転がりすぎるときに上げる")]
    [Min(0f)]
    [SerializeField] private float spinDamping = 6f;

    [Tooltip("向きを変えたとき、物資がしなった光線に沿ってプレイヤーのまわりを回り込む速さの上限（度/秒）。0 で回り込まない（まっすぐ引く）")]
    [Min(0f)]
    [SerializeField] private float maxSwingSpeed = 540f;

    [Tooltip("引きずる速さの上限（m/秒）。0 で上限なし。ふつうに引くときは 7〜9 m/秒ほどなので、それより大きくしておく")]
    [Min(0f)]
    [SerializeField] private float maxDragSpeed = 20f;

    [Header("光線がちぎれる（2026/11/10）")]
    [Tooltip("引きたかったのに動けなかった距離（m）がこれだけたまったら、光線がちぎれて物資をその場に落とす。0 でちぎれない")]
    [Min(0f)]
    [SerializeField] private float snapStrain = 2f;

    [Tooltip("引っかかりが取れているとき、たまった距離が減る速さ（m/秒）。床との摩擦などの小さな遅れでちぎれないようにする")]
    [Min(0f)]
    [SerializeField] private float strainRecovery = 2f;

    [Tooltip("ON：たまった力を、動く物（Is Kinematic の Rigidbody）に触れている間だけためる。OFF：ふつうの壁に引っかかっていてもためる")]
    [SerializeField] private bool strainOnlyOnKinematic = true;

    [Tooltip("光線が動く物（Is Kinematic の Rigidbody）をつらぬいたら、ちぎれる。ふつうの壁ではちぎれない")]
    [SerializeField] private bool snapOnKinematicPierce = true;

    [Tooltip("動く物をこの秒数つらぬき続けたら、ちぎれる。0 ですぐ（一瞬かすっただけでちぎれないよう、少しだけ待つ）")]
    [Min(0f)]
    [SerializeField] private float kinematicSnapSeconds = 0.1f;

    [Tooltip("つらぬいたかを見るとき、光線の両端（手元と物資のそば）を何 m 見ないか。物資が乗っている床に、先端がかすってちぎれないようにする")]
    [Min(0f)]
    [SerializeField] private float pierceIgnoreEnds = 0.3f;

    [Tooltip("つらぬいたか・触れているかを見る相手のレイヤー")]
    [SerializeField] private LayerMask snapCheckMask = ~0;

    [Header("後ろへ投げる（途中で押したとき）")]
    [Tooltip("プレイヤーの中心から、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float backDistance = 6f;

    [Tooltip("放物線のいちばん高い所（プレイヤーの足元から m）。頭（約2m）より高くする")]
    [Min(0.5f)]
    [SerializeField] private float backArcHeight = 3.2f;

    [Tooltip("【引き寄せの前半（Throw Follow Switch まで）】投げる向きを、物資がいま動いている向き（回り込みの勢い＝接線）にどれだけ寄せるか。0 でいつも体の真後ろ、1 で動いている向きそのまま（ハンマー投げのように横へも飛ぶ）。まっすぐ引いているときはどちらでも真後ろ")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowMotion = 0.5f;

    [Tooltip("【引き寄せの後半（Throw Follow Switch を超えてから）】接線にどれだけ寄せるか。体の真後ろを基本に、ほんの少しだけ勢いを残す。0 で真後ろ")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowNear = 0.15f;

    [Tooltip("前半と後半を切り替える、引き寄せの進み具合。0.8 なら、当たった位置から止める位置（Stop Distance）までの道のりを 8 割引いたところで後半に切り替わる")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowSwitch = 0.8f;

    [Header("前へ投げる（最後まで押さなかったとき）")]
    [Tooltip("前へ飛ばす速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float forwardSpeed = 12f;

    [Tooltip("前へ飛ばすときの上向きの速さ（m/秒）。小さいほど床すれすれ")]
    [Min(0f)]
    [SerializeField] private float forwardLift = 1.5f;

    [Header("重い物（引きずるだけ。2026/11/5）")]
    [Tooltip("重さ 1 のとき、1回で引きずれる距離（m）。重さで割る（重さ 2 なら半分）")]
    [Min(0f)]
    [SerializeField] private float heavyDragDistance = 4f;

    [Tooltip("どんなに重くても、1回でこれだけは引きずれる距離（m）")]
    [Min(0f)]
    [SerializeField] private float heavyDragMinDistance = 0.8f;

    [Tooltip("重さ 1 のとき、引きずる速さ（m/秒）。重さで割る（重いほどゆっくり）")]
    [Min(0.1f)]
    [SerializeField] private float heavyDragSpeed = 3f;

    [Tooltip("重い物を止める位置（プレイヤーの中心からの距離 m）。重い物は大きいので、ふつうの物より手前で止める")]
    [Min(0.3f)]
    [SerializeField] private float heavyStopDistance = 2f;

    [Header("クリティカルプル")]
    [Tooltip("当たってから、クリティカルの受付が始まるまでの秒数（Reel Seconds より短くする）")]
    [Min(0f)]
    [SerializeField] private float criticalStart = 1.0f;

    [Tooltip("クリティカルの受付の長さ（秒）。短いほど難しい")]
    [Min(0.01f)]
    [SerializeField] private float criticalLength = 0.2f;

    [Tooltip("クリティカルのとき、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float criticalBackDistance = 11f;

    [Tooltip("クリティカルのときの放物線のいちばん高い所（足元から m）")]
    [Min(0.5f)]
    [SerializeField] private float criticalArcHeight = 4.5f;

    [Header("クリティカルのエフェクト")]
    [Tooltip("受付が始まった瞬間に、物資の位置に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalCuePrefab;

    [Tooltip("クリティカルが決まった瞬間に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalHitPrefab;

    [Tooltip("仮の光の輪の色")]
    [SerializeField] private Color flashColor = new Color(1f, 0.82f, 0.2f, 1f);

    [Tooltip("仮の光の輪に使うマテリアル（Art/Materials/SweeperRing.mat）。空なら再生したときに作る")]
    [SerializeField] private Material flashMaterial;

    [Tooltip("物資の金色のフチに使うマテリアル（Art/Materials/SweeperItemOutline.mat）。空なら再生したときに作るが、ビルドでは出ないことがある")]
    [SerializeField] private Material outlineMaterial;

    [Tooltip("物資が白く光るのに使うマテリアル（Art/Materials/SweeperItemFlash.mat）。空なら再生したときに作るが、ビルドでは出ないことがある")]
    [SerializeField] private Material whiteFlashMaterial;

    [Header("クリティカルの仮のエフェクトの選び方（プレハブが空のとき）")]
    [Tooltip("合図を、受付が始まる何秒前から出すか（縮んでくる輪・金色のフチ）。0 なら受付が始まった瞬間。Critical Start より長くはならない")]
    [Min(0f)]
    [SerializeField] private float cueLead = 0.5f;

    [Tooltip("合図：縮んでくる輪（案1）。物資のふちの輪に重なった瞬間が受付の始まり")]
    [SerializeField] private bool cueShrinkRing = true;

    [Tooltip("合図：物資のフチが金色に光る（案4）。だんだん強くなり、受付の間はくっきり光る")]
    [SerializeField] private bool cueGoldOutline = true;

    [Tooltip("合図：受付が始まった瞬間に、足元に光の輪が広がる（前からの仮）")]
    [SerializeField] private bool cueSpreadRing;

    [Tooltip("金色のフチの厚み（m）")]
    [Min(0.005f)]
    [SerializeField] private float outlineWidth = 0.05f;

    [Tooltip("決まったとき：星のかけらが飛び散る（案1）")]
    [SerializeField] private bool hitShards = true;

    [Tooltip("決まったとき：物資が白く光る（案4）")]
    [SerializeField] private bool hitWhiteFlash = true;

    [Tooltip("決まったとき：物資をその場で止めてから飛ばす秒数（案4）。0 で止めない")]
    [Min(0f)]
    [SerializeField] private float hitStopSeconds = 0.06f;

    [Tooltip("決まったとき：推進の衝撃波。物資の、引っ張られている側と逆の側から輪が噴き出し、飛んでいく間も少し後ろへ出し続ける")]
    [SerializeField] private bool hitThrustWave = true;

    [Tooltip("決まったとき：足元に大きな光の輪が広がる（前からの仮）")]
    [SerializeField] private bool hitSpreadRing;

    private Collider playerCollider;
    private SweeperShooter shooter;
    private HookableObject item;
    private Rigidbody body;
    private float startTime;
    private bool cueShown;

    /// <summary>巻き取りで物資を動かしている横の速さ（FixedUpdate で決めた値。ぶつかった跳ね返りは入らない）。投げる向きに使う。</summary>
    private Vector3 reelVelocity;

    /// <summary>これより遅いときは「動いている向き」が当てにならないので、体の真後ろへ投げる（m/秒）。</summary>
    private const float MinFollowSpeed = 0.5f;

    /// <summary>引き始めたときの、プレイヤーの中心から物資までの距離（真上から見た距離）。引き寄せの進み具合を出すのに使う。</summary>
    private float reelStartDistance;

    /// <summary>いま引いている物が重い物か（<see cref="Begin"/> で決める）。</summary>
    private bool heavy;

    /// <summary>重い物の重さ（1 が基準）。</summary>
    private float heavyWeight = 1f;

    /// <summary>重い物を、この1回であと何m引きずれるか。</summary>
    private float heavyRemaining;

    /// <summary>重い物が回り込む速さの上限を、ふつうの物（Max Swing Speed）の何倍にするか（そこからさらに重さで割る）。</summary>
    private const float HeavySwingShare = 0.25f;

    /// <summary>自分の参加番号を持つ通信上のプレイヤー（オフラインの検証シーンでは null）。</summary>
    private FishingNetPlayer netPlayer;

    /// <summary>巻き取っている物資の通信の部品。オンラインで巻き取っている間だけ入る。</summary>
    private FishingNetSupply supply;

    /// <summary>ホストの返事（引っ掛けてよいか）を待っている間。この間は引かない。</summary>
    private bool waitingForClaim;

    /// <summary>返事を待ち始めてからの秒数。</summary>
    private float claimWait;

    /// <summary>これだけ待っても返事が来なければ、あきらめて光線を戻す（秒）。</summary>
    private const float ClaimTimeout = 1.5f;

    // ---- 光線がちぎれる（2026/11/10） ----

    /// <summary>ちぎれたときの <see cref="LastResult"/>。</summary>
    private const string SnapResult = "ちぎれた";

    /// <summary>引きたかったのに動けなかった距離のたまり（m）。<see cref="snapStrain"/> を超えたらちぎれる。</summary>
    private float strain;

    /// <summary>前のステップで速さを入れたときの物資の位置と、そのステップで動かしたかった横の距離。</summary>
    private Vector3 lastPosition;
    private Vector3 lastStep;
    private bool hasLastStep;

    /// <summary>光線が動く物をつらぬき続けている秒数。</summary>
    private float pierceTime;

    /// <summary>光線の形（手元→物資）を受け取る入れ物。</summary>
    private readonly Vector3[] beamPath = new Vector3[9];

    /// <summary>当たり判定を受け取る入れ物（毎回作らないように使い回す）。</summary>
    private readonly RaycastHit[] pierceHits = new RaycastHit[16];
    private readonly Collider[] touchBuffer = new Collider[16];

    /// <summary>物資のまわり、どれだけ離れていても「触れている」とみなすか（m）。</summary>
    private const float TouchMargin = 0.05f;

    /// <summary>エフェクトの種類（オンラインでほかの人の画面にも出すときに使う）。</summary>
    public enum Effect
    {
        CriticalCue,
        CriticalHit,
        Snap // 光線がちぎれた（2026/11/10）
    }

    /// <summary>エフェクトを出した瞬間に呼ばれる（<see cref="SweeperNetSync"/> がほかの人へ知らせる）。</summary>
    public event System.Action<Effect, HookableObject> EffectShown;

    /// <summary>巻き取っている最中か。</summary>
    public bool IsActive => item != null;

    /// <summary>巻き取っている物資。</summary>
    public HookableObject Item => item;

    /// <summary>当たってから（オンラインでは、ホストに認められてから）の秒数。</summary>
    public float Elapsed => IsActive && !waitingForClaim ? Time.time - startTime : 0f;

    /// <summary>いまクリティカルの受付中か（重い物を引きずっている間は、いつも false）。</summary>
    public bool InCriticalWindow => IsActive && !waitingForClaim && !heavy && Elapsed >= criticalStart && Elapsed <= criticalStart + criticalLength;

    /// <summary>いま重い物を引きずっているか。</summary>
    public bool IsHeavyDrag => IsActive && heavy;

    /// <summary>オンラインでホストの返事（引っ掛けてよいか）を待っている間か。</summary>
    public bool IsWaitingForClaim => IsActive && waitingForClaim;

    // ---- ボット（SweeperBot）が、投げる先を決めるのに読む値 ----

    /// <summary>当たってから手前に着くまでの秒数（押さなければ、このとき前へ投げる）。</summary>
    public float ReelSeconds => reelSeconds;

    /// <summary>後ろへ投げたとき、プレイヤーの中心からどれだけ離れた所に落ちるか（m）。</summary>
    public float BackDistance => backDistance;

    /// <summary>クリティカルのとき、プレイヤーの中心からどれだけ離れた所に落ちるか（m）。</summary>
    public float CriticalBackDistance => criticalBackDistance;

    /// <summary>当たってから、クリティカルの受付が終わるまでの秒数。</summary>
    public float CriticalWindowEnd => criticalStart + criticalLength;

    /// <summary>当たってから、クリティカルの受付が始まるまでの秒数。</summary>
    public float CriticalWindowStart => criticalStart;

    /// <summary>合図を受付の何秒前から出すか（当たってからの秒数より前にはならない）。</summary>
    private float CueLead => Mathf.Min(cueLead, criticalStart);

    /// <summary>
    /// その物資を引っ掛けてよいか。オンラインで**ほかの人が引っ掛けている物は false**（すり抜ける）。
    /// オフラインの検証シーンでは、いつも true。
    /// </summary>
    public bool CanHook(HookableObject target)
    {
        FishingNetSupply netSupply = target != null ? target.GetComponent<FishingNetSupply>() : null;
        if (netSupply == null || !netSupply.IsSpawned || !IsOnline)
        {
            return true;
        }

        return !netSupply.IsClaimed || netSupply.HookedByPlayerIndex == MyIndex;
    }

    /// <summary>通信上のプレイヤーとして動いているか。</summary>
    private bool IsOnline => netPlayer != null && netPlayer.IsSpawned;

    /// <summary>自分の参加番号（物資の通信で「誰が引っ掛けているか」に使う）。</summary>
    private int MyIndex => netPlayer != null ? Mathf.Max(0, netPlayer.PlayerIndex) : 0;

    /// <summary>最後の投げがクリティカルだったか（エフェクトや検証用の表示に使う）。</summary>
    public bool LastWasCritical { get; private set; }

    /// <summary>最後の投げの結果（「後ろ」「前」「クリティカル」）。検証用の表示に使う。</summary>
    public string LastResult { get; private set; } = "";

    /// <summary>最後の巻き取りが、光線がちぎれて終わったか。</summary>
    public bool LastWasSnapped => LastResult == SnapResult;

    private void Awake()
    {
        playerCollider = GetComponent<Collider>();
        shooter = GetComponent<SweeperShooter>();
        netPlayer = GetComponent<FishingNetPlayer>();
    }

    /// <summary>巻き取りを始める。物資の Rigidbody の物理はそのまま使う（床の上を引きずる）。</summary>
    public void Begin(HookableObject target)
    {
        if (target == null || target.Body == null)
        {
            return;
        }

        item = target;
        body = target.Body;
        startTime = Time.time;
        cueShown = false;
        waitingForClaim = false;
        supply = null;
        reelVelocity = Vector3.zero;
        reelStartDistance = FlatDistanceToPlayer();
        strain = 0f;
        hasLastStep = false;
        pierceTime = 0f;

        // 重い物は投げずに引きずるだけ。1回で引きずれる距離は重さで割る
        heavy = HeavyHookable.TryGetWeight(target, out heavyWeight);
        heavyWeight = Mathf.Max(0.1f, heavyWeight);
        heavyRemaining = heavy ? Mathf.Max(heavyDragDistance / heavyWeight, heavyDragMinDistance) : 0f;

        // オンラインなら、ホストに引っ掛けを頼む。認められるまでは引かない
        FishingNetSupply netSupply = target.GetComponent<FishingNetSupply>();
        if (IsOnline && netSupply != null && netSupply.IsSpawned)
        {
            supply = netSupply;
            waitingForClaim = true;
            claimWait = 0f;
            supply.RequestClaim(MyIndex);
        }

        item.SetHooked(true);
        // 手前まで来たときに、自分の体とぶつかって跳ねないようにする
        ThrowPassThrough.Hold(item.gameObject, playerCollider);
        body.WakeUp();
    }

    /// <summary>
    /// 巻き取りの途中でボタンが押された。後ろへ投げる（受付中ならクリティカル）。
    /// オンラインでホストの返事を待っている間（ほんの一瞬）は、押しても何もしない。
    /// </summary>
    public void Press()
    {
        if (!IsActive || waitingForClaim)
        {
            return;
        }

        // 重い物は投げられない。押したら、その場に置いて離す
        if (heavy)
        {
            ReleaseHeavy();
            return;
        }

        bool critical = InCriticalWindow;
        ThrowBack(critical);
        Finish(critical ? "クリティカル" : "後ろ", critical, false);
    }

    /// <summary>投げずにやめる（物資を離すだけ）。オンラインでは、離したことをホストへ知らせる。</summary>
    public void Cancel()
    {
        if (IsActive)
        {
            Finish("やめた", false, true);
        }
    }

    private void Update()
    {
        if (!IsActive)
        {
            return;
        }

        // 物資が消えた（ゴールに入った・爆発した・場外に落ちた）ら、投げずに終わる
        if (item.IsVanished || !item.gameObject.activeInHierarchy)
        {
            Finish("消えた", false, false);
            return;
        }

        if (supply != null && !UpdateOnlineClaim())
        {
            return;
        }

        // 重い物は、引きずり終わるまで待つ（終わりは FixedUpdate が決める）。クリティカルの合図も、前へ投げるのも無い
        if (heavy)
        {
            if (heavyRemaining <= 0f)
            {
                ReleaseHeavy();
            }
            return;
        }

        if (!cueShown && Elapsed >= criticalStart - CueLead)
        {
            cueShown = true;
            PlayEffect(Effect.CriticalCue);
        }

        if (Elapsed >= reelSeconds)
        {
            ThrowForward();
            Finish("前", false, false);
        }
    }

    /// <summary>
    /// オンラインで、ホストの記録を見る。**巻き取ってよければ true。**
    /// 返事待ちのうちは false（認められた瞬間から時間を数える）。ほかの人に取られた・ホストに外されたら、投げずにやめて false。
    /// </summary>
    private bool UpdateOnlineClaim()
    {
        bool mine = supply.IsOwner && supply.HookedByPlayerIndex == MyIndex;

        if (waitingForClaim)
        {
            if (mine)
            {
                waitingForClaim = false;
                startTime = Time.time;
                reelStartDistance = FlatDistanceToPlayer();
                body.WakeUp();
                return true;
            }

            claimWait += Time.deltaTime;
            bool takenByOther = supply.IsClaimed && supply.HookedByPlayerIndex != MyIndex;
            if (takenByOther)
            {
                Finish("取られた", false, false);
            }
            else if (claimWait >= ClaimTimeout)
            {
                // 返事が来ない。あとから認められても持ったままにならないよう、離したと送っておく
                Finish("返事なし", false, true);
            }
            return false;
        }

        if (!mine)
        {
            // 爆風などで、ホストに外された
            Finish("外れた", false, false);
            return false;
        }

        return true;
    }

    private void FixedUpdate()
    {
        if (!IsActive || body == null || waitingForClaim)
        {
            return;
        }

        // 動く物に押し付けすぎた・光線が動く物をつらぬいたら、ちぎれて落とす（2026/11/10）
        if (CheckSnap())
        {
            return;
        }

        // プレイヤーから見た物資の「向き」と「距離」を、残りの時間で行き先へ近づける（真上から見た位置で考える）
        Vector3 player = transform.position;
        Vector3 fromPlayer = body.position - player;
        fromPlayer.y = 0f;
        float distance = fromPlayer.magnitude;
        Vector3 toward = distance > 0.01f ? fromPlayer / distance : Flat(transform.forward);

        if (heavy)
        {
            DragHeavy(player, toward, distance);
            return;
        }

        // 残りの時間のうち、この1ステップの割合＝距離に関係なく、決まった時間で着く
        float remaining = Mathf.Max(Time.fixedDeltaTime, startTime + reelSeconds - Time.time);
        float share = Mathf.Min(1f, Time.fixedDeltaTime / remaining);

        // 向き：光線がしなっていれば、手元から光線が出ている向きの側へ回り込む（2026/11/4）。
        // 向きを変えていなければ、しなりの向き＝物資への向きなので、今までどおりまっすぐ引く
        Vector3 nextDirection = toward;
        if (maxSwingSpeed > 0f && shooter != null && shooter.TryGetBendDirection(out Vector3 bendDirection))
        {
            float swing = Vector3.SignedAngle(toward, bendDirection, Vector3.up) * share;
            float limit = maxSwingSpeed * Time.fixedDeltaTime;
            nextDirection = Quaternion.AngleAxis(Mathf.Clamp(swing, -limit, limit), Vector3.up) * toward;
        }

        // 距離：手前の止める位置（Stop Distance）まで
        float nextDistance = distance + (stopDistance - distance) * share;

        Vector3 rest = player + nextDirection * nextDistance - body.position;
        rest.y = 0f;
        Vector3 velocity = CapSpeed(rest / Time.fixedDeltaTime);
        reelVelocity = velocity;

        if (body.isKinematic)
        {
            body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
            return;
        }

        SetDragVelocity(velocity);
    }

    /// <summary>
    /// 上下は物理（重力）のまま、横だけ決める＝床に置いたまま引きずる。回転は弱める。
    /// 次のステップで「どれだけ動けなかったか」を測るため、いまの位置と動かしたい距離を覚えておく。
    /// </summary>
    private void SetDragVelocity(Vector3 velocity)
    {
        body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);

        if (spinDamping > 0f)
        {
            body.angularVelocity *= Mathf.Exp(-spinDamping * Time.fixedDeltaTime);
        }

        lastPosition = body.position;
        lastStep = new Vector3(velocity.x, 0f, velocity.z) * Time.fixedDeltaTime;
        hasLastStep = true;
    }

    /// <summary>横の速さを <see cref="maxDragSpeed"/> までに抑える（0 なら抑えない）。</summary>
    private Vector3 CapSpeed(Vector3 velocity)
    {
        if (maxDragSpeed <= 0f)
        {
            return velocity;
        }

        float speed = velocity.magnitude;
        return speed > maxDragSpeed ? velocity * (maxDragSpeed / speed) : velocity;
    }

    // ------------------------------------------------------------
    // 光線がちぎれる（2026/11/10）
    // ------------------------------------------------------------

    /// <summary>
    /// **ちぎれるかを調べ、ちぎれたら物資を落として true。** FixedUpdate の最初に呼ぶ。
    /// ①前のステップで動かしたかった距離のうち、動けなかった分をためる（<see cref="snapStrain"/> を超えたらちぎれる）
    /// ②光線が動く物をつらぬき続けたら（<see cref="kinematicSnapSeconds"/>）ちぎれる
    /// </summary>
    private bool CheckSnap()
    {
        float dt = Time.fixedDeltaTime;

        if (snapStrain > 0f && hasLastStep && !body.isKinematic)
        {
            Vector3 moved = body.position - lastPosition;
            moved.y = 0f;

            // 動かしたかった向きに、実際に進めた距離。押し戻された・止められた分が「動けなかった距離」
            float wanted = lastStep.magnitude;
            float shortfall = wanted > 0.0001f ? Mathf.Max(0f, wanted - Vector3.Dot(moved, lastStep / wanted)) : 0f;

            if (shortfall > 0.0001f && (!strainOnlyOnKinematic || TouchingKinematic()))
            {
                strain += shortfall;
            }

            strain = Mathf.Max(0f, strain - strainRecovery * dt);
            if (strain >= snapStrain)
            {
                Snap();
                return true;
            }
        }
        hasLastStep = false;

        if (snapOnKinematicPierce && BeamPiercesKinematic())
        {
            pierceTime += dt;
            if (pierceTime >= kinematicSnapSeconds)
            {
                Snap();
                return true;
            }
        }
        else
        {
            pierceTime = 0f;
        }

        return false;
    }

    /// <summary>
    /// **ちぎれる相手か。** 動く物（Is Kinematic の Rigidbody）だけ。ふつうの壁（Rigidbody が無い）・物資・プレイヤー・
    /// 自分の体・すり抜ける判定は数えない（オンラインでは、ほかの人の物資も Is Kinematic になっているため物資は除く）。
    /// </summary>
    private bool IsSnapObstacle(Collider other)
    {
        if (other == null || other.isTrigger)
        {
            return false;
        }

        Rigidbody otherBody = other.attachedRigidbody;
        if (otherBody == null || !otherBody.isKinematic || otherBody == body)
        {
            return false;
        }

        if (other.transform.IsChildOf(transform))
        {
            return false;
        }

        return other.GetComponentInParent<HookableObject>() == null
            && other.GetComponentInParent<CharacterController>() == null;
    }

    /// <summary>引いている物資が、動く物に触れているか（物資の大きさの箱を少しふくらませて調べる）。</summary>
    private bool TouchingKinematic()
    {
        Collider itemCollider = item != null ? item.GetComponent<Collider>() : null;
        if (itemCollider == null)
        {
            return false;
        }

        Bounds bounds = itemCollider.bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * TouchMargin, touchBuffer,
            Quaternion.identity, snapCheckMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (IsSnapObstacle(touchBuffer[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 光線（手元から物資まで。しなっていれば曲線）が、動く物をつらぬいているか。
    /// 両端の <see cref="pierceIgnoreEnds"/> m は見ない（物資が乗っている床に先端がかするだけでちぎれないように）。
    /// </summary>
    private bool BeamPiercesKinematic()
    {
        if (shooter == null)
        {
            return false;
        }

        int count = shooter.GetBeamPath(beamPath);
        if (count < 2)
        {
            return false;
        }

        float total = 0f;
        for (int i = 1; i < count; i++)
        {
            total += Vector3.Distance(beamPath[i - 1], beamPath[i]);
        }

        float from = pierceIgnoreEnds;
        float to = total - pierceIgnoreEnds;
        if (to <= from)
        {
            return false;
        }

        float walked = 0f;
        for (int i = 1; i < count; i++)
        {
            Vector3 a = beamPath[i - 1];
            Vector3 b = beamPath[i];
            float length = Vector3.Distance(a, b);
            float segmentStart = walked;
            walked += length;

            // この区間のうち、両端を除いた部分だけ調べる
            float s0 = Mathf.Max(segmentStart, from);
            float s1 = Mathf.Min(walked, to);
            if (length < 0.0001f || s1 <= s0)
            {
                continue;
            }

            Vector3 direction = (b - a) / length;
            Vector3 start = a + direction * (s0 - segmentStart);
            int hits = Physics.RaycastNonAlloc(start, direction, pierceHits, s1 - s0, snapCheckMask, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hits; h++)
            {
                if (IsSnapObstacle(pierceHits[h].collider))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>**光線がちぎれる。** ちぎれた見た目を出し（オンラインではほかの人の画面にも）、物資はその場に落とす。</summary>
    private void Snap()
    {
        PlayEffect(Effect.Snap);
        DropInPlace();
        Finish(SnapResult, false, false);
    }

    /// <summary>
    /// 物資を横に動かさず、その場に置く（落ちるのは止めない）。オンラインでは、離した位置をホストへ渡して置いてもらう
    /// （ホストに届いている位置は少し遅れているため）。
    /// </summary>
    private void DropInPlace()
    {
        if (body == null)
        {
            return;
        }

        if (!body.isKinematic)
        {
            body.linearVelocity = new Vector3(0f, Mathf.Min(0f, body.linearVelocity.y), 0f);
            body.angularVelocity = Vector3.zero;
        }

        if (supply != null && supply.IsSpawned)
        {
            supply.RequestThrow(Vector3.zero, 0f, 0f, MyIndex, true, Vector3.zero, Vector3.zero, true, body.position);
        }
    }

    /// <summary>
    /// **重い物を1ステップぶん引きずる。** 速さは Heavy Drag Speed ÷ 重さ。1回で引きずれる距離（<see cref="heavyRemaining"/>）を使い切るか、
    /// 手前（Heavy Stop Distance）に着いたら終わり（離すのは <see cref="Update"/>）。
    /// 壁に引っかかって動かなくても距離は減っていくので、いつまでも離さないことは無い。
    /// 向きを変えたときの回り込みも、重さで割った速さで効く（重いほど振り回しにくい）。
    /// </summary>
    private void DragHeavy(Vector3 player, Vector3 toward, float distance)
    {
        float dt = Time.fixedDeltaTime;
        float room = Mathf.Max(0f, distance - heavyStopDistance);
        float step = Mathf.Min(heavyDragSpeed / heavyWeight * dt, heavyRemaining, room);

        heavyRemaining -= step;
        if (room <= 0.01f)
        {
            heavyRemaining = 0f;
        }

        Vector3 nextDirection = toward;
        if (maxSwingSpeed > 0f && shooter != null && shooter.TryGetBendDirection(out Vector3 bendDirection))
        {
            float swing = Vector3.SignedAngle(toward, bendDirection, Vector3.up);
            float limit = maxSwingSpeed * HeavySwingShare / heavyWeight * dt;
            nextDirection = Quaternion.AngleAxis(Mathf.Clamp(swing, -limit, limit), Vector3.up) * toward;
        }

        Vector3 rest = player + nextDirection * (distance - step) - body.position;
        rest.y = 0f;
        Vector3 velocity = CapSpeed(rest / dt);
        reelVelocity = velocity;

        if (body.isKinematic)
        {
            body.MovePosition(body.position + velocity * dt);
            return;
        }

        SetDragVelocity(velocity);
    }

    /// <summary>
    /// **重い物を、その場に置いて離す**（投げない）。オンラインでは、離した位置をホストへ渡して置いてもらう
    /// （ホストに届いている位置は少し遅れているため。今の体の「重い物を引きずる」と同じ考え）。
    /// </summary>
    private void ReleaseHeavy()
    {
        DropInPlace();
        Finish("引きずった", false, false);
    }

    /// <summary>
    /// 押した瞬間の位置から、プレイヤーの頭を越える放物線で後ろへ投げる。
    /// 飛ぶ向きは「体の真後ろ」と「物資がいま動いている向き」を <see cref="throwFollowMotion"/> の割合で混ぜたもの（2026/11/4）。
    /// 着く場所は、その向きにまっすぐ飛んで、プレイヤーの中心から Back Distance（クリティカルは Critical Back Distance）離れた所。
    /// </summary>
    private void ThrowBack(bool critical)
    {
        if (body == null)
        {
            return;
        }

        float distance = critical ? criticalBackDistance : backDistance;
        float arcHeight = critical ? criticalArcHeight : backArcHeight;

        Vector3 from = body.position;
        Vector3 direction = ThrowBackDirection();
        Vector3 landing = BackLanding(from, direction, distance);
        landing.y = from.y;

        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        float apexY = Mathf.Max(transform.position.y + arcHeight, from.y + 0.5f);

        // いちばん高い所まで上がる時間と、そこから落ちる時間を足したのが、飛んでいる時間
        float upSpeed = Mathf.Sqrt(2f * gravity * (apexY - from.y));
        float flightTime = upSpeed / gravity + Mathf.Sqrt(2f * (apexY - landing.y) / gravity);

        Vector3 across = landing - from;
        across.y = 0f;
        Vector3 velocity = across / flightTime + Vector3.up * upSpeed;

        if (critical)
        {
            PlayEffect(Effect.CriticalHit);
        }

        if (critical && hitStopSeconds > 0f)
        {
            // 案4：白く光ったまま一瞬止めてから飛ばす（手ごたえ）。止めるのはこの物資だけ
            StartCoroutine(LaunchAfterStop(body, supply, velocity, hitStopSeconds));
        }
        else
        {
            Launch(body, supply, velocity);
        }
    }

    /// <summary>
    /// 後ろへ投げる向き（真上から見た向き）。体の真後ろから、物資が動いている向き（接線）のほうへ、
    /// その間の角度の「寄せる割合」倍だけ回す。
    /// 寄せる割合は引き寄せの進み具合で切り替える（2026/11/10）。<see cref="throwFollowSwitch"/>（初期値 8 割）までは
    /// <see cref="throwFollowMotion"/>（接線に近い向きへ飛ぶ）、そこを超えたら <see cref="throwFollowNear"/>（真後ろに、ほんの少しだけ接線を足す）。
    /// まっすぐ引いているときは動いている向き＝プレイヤーへ向かう向き＝真後ろなので、どちらでも真後ろになる。
    /// </summary>
    private Vector3 ThrowBackDirection()
    {
        Vector3 back = -Flat(transform.forward);

        float follow = ReelProgress() < throwFollowSwitch ? throwFollowMotion : throwFollowNear;

        Vector3 motion = reelVelocity;
        motion.y = 0f;
        if (follow <= 0f || motion.sqrMagnitude < MinFollowSpeed * MinFollowSpeed)
        {
            return back;
        }

        // 角度で混ぜる（真逆どうしでも向きが決まるように、真上から見た回転の角度で考える）
        float angle = Vector3.SignedAngle(back, motion, Vector3.up) * follow;
        return Quaternion.AngleAxis(angle, Vector3.up) * back;
    }

    /// <summary>
    /// 引き寄せの進み具合（0〜1）。当たった位置で 0、止める位置（Stop Distance）に着いたら 1。
    /// 初めから止める位置のそばにいたときは 1（後半あつかい）。
    /// </summary>
    private float ReelProgress()
    {
        float span = reelStartDistance - stopDistance;
        if (span <= 0.01f)
        {
            return 1f;
        }

        return Mathf.Clamp01((reelStartDistance - FlatDistanceToPlayer()) / span);
    }

    /// <summary>プレイヤーの中心から物資までの、真上から見た距離。</summary>
    private float FlatDistanceToPlayer()
    {
        if (body == null)
        {
            return 0f;
        }

        Vector3 offset = body.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    /// <summary>
    /// <paramref name="from"/> から <paramref name="direction"/> へまっすぐ飛んで、プレイヤーの中心から <paramref name="distance"/> 離れる所。
    /// その線がその距離の円に届かない（物資がもう円の外にいて、外へ向かって動いている）ときは、物資の位置から <paramref name="distance"/> 先にする。
    /// </summary>
    private Vector3 BackLanding(Vector3 from, Vector3 direction, float distance)
    {
        Vector3 offset = from - transform.position;
        offset.y = 0f;

        // |offset + direction * t| = distance を t について解く（direction は長さ1）
        float along = Vector3.Dot(direction, offset);
        float inside = along * along - offset.sqrMagnitude + distance * distance;
        float travel = inside >= 0f ? -along + Mathf.Sqrt(inside) : -1f;
        if (travel < 0.5f)
        {
            travel = distance;
        }

        return from + direction * travel;
    }

    /// <summary>物資をその場に <paramref name="seconds"/> 秒止めてから投げる。止めている間に巻き取りは終わっている（Finish 済み）。</summary>
    private IEnumerator LaunchAfterStop(Rigidbody target, FishingNetSupply netSupply, Vector3 velocity, float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                yield break;
            }

            if (!target.isKinematic)
            {
                target.linearVelocity = Vector3.zero;
                target.angularVelocity = Vector3.zero;
            }
            yield return new WaitForFixedUpdate();
        }

        if (target != null && target.gameObject.activeInHierarchy)
        {
            Launch(target, netSupply, velocity);
        }
    }

    /// <summary>向いている方向（前）へ、床すれすれに投げる。</summary>
    private void ThrowForward()
    {
        if (body == null)
        {
            return;
        }

        Launch(body, supply, Flat(transform.forward) * forwardSpeed + Vector3.up * forwardLift);
    }

    private void Launch(Rigidbody target, FishingNetSupply netSupply, Vector3 velocity)
    {
        if (netSupply != null)
        {
            // オンライン：飛ばすのはホスト。計算した速さをそのまま渡し、離した位置から飛ばしてもらう
            netSupply.RequestThrow(Vector3.zero, 0f, 0f, MyIndex, true, velocity, target.angularVelocity, true, target.position);
        }

        if (target.isKinematic)
        {
            return;
        }

        // 自分の画面でも同じ速さで飛ばしておく（ホストに持ち主が戻るまでの間、止まって見えないように）
        target.linearVelocity = velocity;
        target.WakeUp();
    }

    /// <summary>
    /// 巻き取りを終える。<paramref name="releaseOnline"/> が true なら、オンラインで「離した」とホストへ知らせる
    /// （投げたとき・取られた／外されたとき・消えたときは知らせない）。
    /// </summary>
    private void Finish(string result, bool critical, bool releaseOnline)
    {
        LastResult = result;
        LastWasCritical = critical;

        if (releaseOnline && supply != null && supply.IsSpawned)
        {
            supply.RequestRelease(Vector3.zero, 0f, MyIndex);
        }

        if (item != null)
        {
            item.SetHooked(false);
            // 投げた直後に自分の体に当たらないようにする（離れたら元に戻る）
            ThrowPassThrough.Apply(item.gameObject, playerCollider);
        }

        item = null;
        body = null;
        supply = null;
        waitingForClaim = false;
        heavy = false;
        heavyRemaining = 0f;
        hasLastStep = false;
        strain = 0f;
        pierceTime = 0f;
    }

    /// <summary>巻き取っている物資にエフェクトを出し、出したことを知らせる（オンラインではほかの人の画面にも出る）。</summary>
    private void PlayEffect(Effect kind)
    {
        if (item == null)
        {
            return;
        }

        ShowEffect(kind, item);
        EffectShown?.Invoke(kind, item);
    }

    /// <summary>
    /// **その物資にエフェクトを出す。** 合図（<see cref="Effect.CriticalCue"/>）は受付の <c>Cue Lead</c> 秒前に呼ばれ、物資に付いていく。
    /// 決まったときの光はその場に出る。
    /// オンラインのほかの人の体では、本人から知らせが来たときに <see cref="SweeperNetSync"/> が呼ぶ（時間の値は、その体のインスペクターの値を使う）。
    /// </summary>
    public void ShowEffect(Effect kind, HookableObject target)
    {
        // ちぎれた：光線の見た目だけ（物資が見つからなくても出す）
        if (kind == Effect.Snap)
        {
            shooter?.PlaySnap();
            return;
        }

        if (target == null)
        {
            return;
        }

        if (kind == Effect.CriticalCue)
        {
            ShowCue(target);
        }
        else
        {
            ShowHit(target);
        }
    }

    /// <summary>合図を出す。巻き取りが終わったら（押した・やめた）、残っている合図はすぐ消える。</summary>
    private void ShowCue(HookableObject target)
    {
        float lead = CueLead;
        System.Func<bool> reeling = () => IsReeling(target);

        // プレハブと前からの広がる輪は、これまでどおり受付が始まった瞬間に出す
        if (criticalCuePrefab != null)
        {
            After(lead, () =>
            {
                if (IsReeling(target))
                {
                    Instantiate(criticalCuePrefab, GroundPoint(target, out _), Quaternion.identity);
                }
            });
            return;
        }

        if (cueSpreadRing)
        {
            After(lead, () =>
            {
                if (IsReeling(target))
                {
                    Vector3 ground = GroundPoint(target, out float size);
                    SweeperFlash.Spawn(ground, target.transform, flashColor, 1.2f * size, 3.2f * size, criticalLength + 0.15f, flashMaterial);
                }
            });
        }

        if (cueShrinkRing)
        {
            Vector3 ground = GroundPoint(target, out float size);
            SweeperShrinkRing.Spawn(ground, target.transform, flashColor, size * 1.15f, lead, criticalLength, reeling, flashMaterial);
        }

        if (cueGoldOutline)
        {
            SweeperItemGlow.SpawnOutline(target.gameObject, flashColor, outlineWidth, lead, criticalLength, 0.12f, reeling, outlineMaterial);
        }
    }

    /// <summary>クリティカルが決まったときのエフェクトを出す。</summary>
    private void ShowHit(HookableObject target)
    {
        Vector3 ground = GroundPoint(target, out float size);

        if (criticalHitPrefab != null)
        {
            Instantiate(criticalHitPrefab, ground, Quaternion.identity);
            return;
        }

        if (hitSpreadRing)
        {
            SweeperFlash.Spawn(ground, null, flashColor, 1.5f * size, 6f * size, 0.5f, flashMaterial);
        }

        if (hitShards)
        {
            SweeperShards.Spawn(ground + Vector3.up * 0.3f, size * 0.6f, flashColor, flashMaterial);
        }

        if (hitWhiteFlash)
        {
            SweeperItemGlow.SpawnFlash(target.gameObject, Color.white, hitStopSeconds + 0.15f, whiteFlashMaterial);
        }

        if (hitThrustWave)
        {
            // プレイヤーから物資へ向かう向き＝物資が引っ張られている側の逆。止めてから飛ぶので、噴き出すのも飛び始めに合わせる
            Vector3 away = Flat(target.transform.position - transform.position);
            After(hitStopSeconds, () =>
            {
                if (target != null && target.gameObject.activeInHierarchy)
                {
                    SweeperThrustWave.Spawn(target.transform, away, size, flashColor, flashMaterial);
                }
            });
        }
    }

    /// <summary>その物資を、この体がいま巻き取っているか（ほかの人の体では、本人から届いた様子で見る）。</summary>
    private bool IsReeling(HookableObject target)
    {
        return target != null && (item == target || (shooter != null && shooter.ShownReelItem == target));
    }

    /// <summary>物資の足元（床の少し上）の位置と、真上から見た物資の大きさ（m）。</summary>
    private static Vector3 GroundPoint(HookableObject target, out float size)
    {
        Vector3 position = target.transform.position;
        size = 1f;

        Collider itemCollider = target.GetComponent<Collider>();
        if (itemCollider != null)
        {
            Bounds bounds = itemCollider.bounds;
            position.y = bounds.min.y + 0.03f;
            size = Mathf.Max(bounds.size.x, bounds.size.z);
        }

        return position;
    }

    /// <summary><paramref name="seconds"/> 秒後に <paramref name="action"/> を呼ぶ（0 ならすぐ）。</summary>
    private void After(float seconds, System.Action action)
    {
        if (seconds <= 0f)
        {
            action();
            return;
        }

        StartCoroutine(AfterRoutine(seconds, action));
    }

    private static IEnumerator AfterRoutine(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
