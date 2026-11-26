using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のアニメーションの切り替え口。**（2026/10/27・小野田さんの仕様。2026/11/25 に動きのアニメーションを追加）
///
/// 優先の高い順に、1つだけ流す。
/// | いまの様子 | 流す状態 |
/// | --- | --- |
/// | 足場から落ちている | Drop（落ちる瞬間）→ DropLoot（落ちている間のくり返し。Animator の中でつながる） |
/// | 物資を両手で持っている | Throw（持ち上げて前へ投げる。投げ終わるまで流し切る） |
/// | 物資を後ろへ引っ張った | Pull（1回。いちばん引く所が、物資が飛び出す瞬間に来るよう、途中から流す。2026/11/26） |
/// | 光線を撃った | Shoot（構えたままの所は飛ばして、撃つ瞬間の少し前から流す。終わったら AimWalk へ戻る。2026/11/26） |
/// | 押して狙っている・光線を振り回している・物資を巻き取っている | AimWalk（歩く速さで再生の速さが変わる。止まっていると止まったポーズ） |
/// | それ以外 | Idle（Animator が Speed で Idle と Walk を切り替える） |
///
/// 撃つ・巻き取る・やめるは <see cref="SweeperShooter"/> が呼ぶ。引っ張ったは <see cref="SweeperReel.PulledBack"/> から（ほかの人の体は <see cref="SweeperShooter"/> から）。
/// 落ちる・両手で持つは、ここで毎フレーム調べる。
/// Animator が無い・その名前の状態が無いときは、何もしない（エラーにもしない）。
/// 詳しい説明は Documents/機能ドキュメント/プレイヤーの動きのアニメーション.md。
/// </summary>
public class SweeperAnimation : MonoBehaviour
{
    private enum Mode
    {
        Move,
        Aim,
        Shoot,
        Pull,
        Throw,
        Fall,
    }

    [Tooltip("動かす Animator。空なら体の子から探す")]
    [SerializeField] private Animator animator;

    [Header("状態の名前（Animator の中の名前）")]
    [Tooltip("撃った瞬間に流す状態")]
    [SerializeField] private string shootState = "Shoot";

    [Tooltip("狙っている間の状態。撃ったあとも、ここへ戻る")]
    [SerializeField] private string aimState = "AimWalk";

    [Tooltip("何もしていないときに戻す状態（Animator が Speed で Idle と Walk を切り替える）。空なら何もしない")]
    [SerializeField] private string idleState = "Idle";

    [Tooltip("物資を後ろへ引っ張った瞬間に1回流す状態")]
    [SerializeField] private string pullState = "Pull";

    [Tooltip("物資を両手で持って投げる状態")]
    [SerializeField] private string throwState = "Throw";

    [Tooltip("足場から落ち始めたときの状態（終わると Animator の中で落ちている間のくり返しへ移る）")]
    [SerializeField] private string dropState = "Drop";

    [Tooltip("歩く速さを渡す float のパラメーター名。空・無ければ渡さない")]
    [SerializeField] private string speedParameter = "Speed";

    [Tooltip("狙い歩きの再生の速さを渡す float のパラメーター名（歩く速さ ÷ ふだんの歩く速さ）。空・無ければ渡さない")]
    [SerializeField] private string aimSpeedParameter = "AimSpeed";

    [Tooltip("状態を切り替えるときのつなぎの秒数")]
    [Min(0f)]
    [SerializeField] private float crossFade = 0.05f;

    [Tooltip("ほかの人の体の速さを、どれだけ早く実際の動きに合わせるか（大きいほど早い）")]
    [Min(0.1f)]
    [SerializeField] private float remoteSmoothing = 10f;

    [Header("撃つ（Shoot）")]
    [Tooltip("Shoot を、アニメーションの頭から何秒の所から流すか。Shoot は初めの約 0.25 秒が構えたまま止まっていて、0.27 秒で撃った反動が来るので、" +
             "構えの所を飛ばして、押してすぐ反動が出るようにする（0 で頭から）")]
    [Min(0f)]
    [SerializeField] private float shootStartSeconds = 0.2f;

    [Header("引っ張る（Pull）")]
    [Tooltip("Pull の「いちばん引く所」（腕を振り上げて一気に後ろへ引く瞬間）が、アニメーションの頭から何秒か。ここが物資の飛び出す瞬間に来るように流す")]
    [Min(0f)]
    [SerializeField] private float pullPeakSeconds = 0.95f;

    [Tooltip("押してから「いちばん引く所」まで、最低何秒かけるか（引く前の振りかぶりをどれだけ見せるか）。0 で押した瞬間がいちばん引く所。" +
             "クリティカルで物資が一瞬止まるときは、その間のぶんだけ前から流す")]
    [Min(0f)]
    [SerializeField] private float pullLeadSeconds = 0.1f;

    [Tooltip("いちばん引く所からこの秒数たったら、歩き出したときに Pull を切って歩きへ戻す（止まっていれば最後まで流す）")]
    [Min(0f)]
    [SerializeField] private float pullWalkCancelSeconds = 0.3f;

    [Header("落ちる（Drop）")]
    [Tooltip("最後に立っていた高さより、この距離（m）下へ落ちたら「落ちている」とする")]
    [Min(0f)]
    [SerializeField] private float fallDepth = 1f;

    [Tooltip("下向きにこの速さ（m/秒）より速く動いているときだけ「落ちている」とする（ゆっくり下がる床で誤って流さないため）")]
    [Min(0f)]
    [SerializeField] private float fallSpeed = 3f;

    [Tooltip("上下の動きがこの秒数止まったら、立っている（着いた）とする")]
    [Min(0f)]
    [SerializeField] private float landSeconds = 0.15f;

    [Header("両手（両手で持った物を手に合わせる）")]
    [Tooltip("左手の骨の名前")]
    [SerializeField] private string leftHandBone = "JNT_Hand_L";

    [Tooltip("右手の骨の名前")]
    [SerializeField] private string rightHandBone = "JNT_Hand_R";

    /// <summary>上下の速さがこれより小さければ「止まっている」（m/秒）。</summary>
    private const float StillSpeed = 0.5f;

    /// <summary>1 フレームでこれより大きく上下に動いたら、瞬間移動（出てくる場所へ戻された等）とみなす（m）。</summary>
    private const float TeleportDistance = 2f;

    private SweeperMover mover;
    private SweeperReel reel;
    private SweeperNetSync netSync;
    private CharacterController characterController;
    private Unity.Netcode.NetworkObject network;
    private Transform leftHand;
    private Transform rightHand;
    private Vector3 lastPosition;
    private float remoteSpeed;

    /// <summary>光線の様子から決まる状態（<see cref="SweeperShooter"/> が呼ぶ）。</summary>
    private Mode shotMode = Mode.Move;

    /// <summary>いま流している状態。</summary>
    private Mode current = Mode.Move;

    /// <summary>Shoot を流していて、終わったら AimWalk に戻す待ちか。</summary>
    private bool waitingShootEnd;

    /// <summary>引っ張った知らせが来て、次の Update で Pull を流す待ちか。</summary>
    private bool pullRequested;

    /// <summary>Pull を、アニメーションの頭から何秒の所から流すか（引っ張った知らせのときに決める）。</summary>
    private float pullOffset;

    /// <summary>Pull の「いちばん引く所」に来る時刻（Time.time）。</summary>
    private float pullPeakTime;

    private bool falling;
    private float groundY;
    private float lastY;
    private float stillTime;

    private int shootHash;
    private int throwHash;
    private int pullHash;
    private int speedHash;
    private int aimSpeedHash;
    private bool hasSpeed;
    private bool hasAimSpeed;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        mover = GetComponent<SweeperMover>();
        reel = GetComponent<SweeperReel>();
        netSync = GetComponent<SweeperNetSync>();
        characterController = GetComponent<CharacterController>();
        network = GetComponent<Unity.Netcode.NetworkObject>();
        lastPosition = transform.position;
        groundY = lastY = transform.position.y;
        shootHash = Animator.StringToHash(shootState);
        throwHash = Animator.StringToHash(throwState);
        pullHash = Animator.StringToHash(pullState);

        if (animator != null)
        {
            hasSpeed = HasFloat(speedParameter, out speedHash);
            hasAimSpeed = HasFloat(aimSpeedParameter, out aimSpeedHash);
            leftHand = FindChild(animator.transform, leftHandBone);
            rightHand = FindChild(animator.transform, rightHandBone);
        }
    }

    /// <summary>狙い始めた（AimWalk）。</summary>
    public void PlayAim()
    {
        SetShotMode(Mode.Aim);
    }

    /// <summary>撃った（Shoot を流し、終わったら AimWalk に戻る）。</summary>
    public void PlayShoot()
    {
        SetShotMode(Mode.Shoot);
    }

    private void OnEnable()
    {
        if (reel != null)
        {
            reel.PulledBack += PlayPullRelease;
        }
    }

    private void OnDisable()
    {
        if (reel != null)
        {
            reel.PulledBack -= PlayPullRelease;
        }
    }

    /// <summary>
    /// 物資をつかんで巻き取り始めた。巻き取っている間は構えたまま（AimWalk）。
    /// 撃った反動（Shoot）の途中なら、それを流し切ってから AimWalk へ戻る。（2026/11/26：前は巻き取りの間ずっと Pull をくり返していた）
    /// </summary>
    public void PlayPull()
    {
        if (shotMode != Mode.Shoot)
        {
            shotMode = Mode.Aim;
        }
    }

    /// <summary>
    /// 物資を後ろへ引っ張った（Pull を1回流す）。<paramref name="launchDelay"/> は物資が実際に飛び出すまでの秒数（クリティカルの一瞬止まる間）。
    /// 「いちばん引く所」が飛び出す瞬間に来るよう、Pull を途中から流す。
    /// </summary>
    public void PlayPullRelease(float launchDelay)
    {
        float lead = Mathf.Max(pullLeadSeconds, Mathf.Max(0f, launchDelay));
        pullOffset = Mathf.Clamp(pullPeakSeconds - lead, 0f, pullPeakSeconds);
        pullRequested = true;
    }

    /// <summary>投げ終わった・やめた。</summary>
    public void PlayIdle()
    {
        SetShotMode(Mode.Move);
    }

    /// <summary>
    /// 投げるアニメーション（Throw）を流している間だけ、両手の真ん中の位置を返す。
    /// <see cref="SweeperReel"/> が、両手で持った物を手に合わせて動かすのに使う。
    /// </summary>
    public bool TryGetHandsCenter(out Vector3 center)
    {
        center = default;
        if (animator == null || !animator.isActiveAndEnabled || leftHand == null || rightHand == null || current != Mode.Throw)
        {
            return false;
        }

        center = (leftHand.position + rightHand.position) * 0.5f;
        return true;
    }

    private void SetShotMode(Mode mode)
    {
        shotMode = mode;
        // 同じ撃つ・狙うを続けて呼ばれたときも、頭から流し直す（今までと同じ動き）
        if (mode == current && (mode == Mode.Shoot || mode == Mode.Aim))
        {
            Enter(mode);
        }
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        float speed = mover != null ? ReadSpeed() : 0f;
        if (hasSpeed)
        {
            animator.SetFloat(speedHash, speed);
        }
        if (hasAimSpeed)
        {
            float normal = mover != null ? mover.MoveSpeed : 0f;
            animator.SetFloat(aimSpeedHash, normal > 0.01f ? Mathf.Clamp(speed / normal, 0f, 1.5f) : 0f);
        }

        UpdateFalling();

        Mode want = falling ? Mode.Fall : ReadCarrying() ? Mode.Throw : shotMode;

        // 引っ張った：Pull を1回流す（落ちている・両手で投げている間は流さない）
        if (pullRequested)
        {
            pullRequested = false;
            if (want != Mode.Fall && want != Mode.Throw && current != Mode.Throw)
            {
                Enter(Mode.Pull);
                return;
            }
        }

        // 投げる動きは、持ち終わっても振り切るまで流す（落ちた・次を撃ったときは切り替える）
        if (current == Mode.Throw && want == Mode.Move && IsPlaying(throwHash))
        {
            want = Mode.Throw;
        }

        // 引っ張る動きも振り切るまで流す。ただし、いちばん引く所を過ぎてから歩き出したら歩きへ戻す（足が滑って見えないように）。次を撃ったら切り替える
        if (current == Mode.Pull && want == Mode.Move && IsPlaying(pullHash))
        {
            bool walking = speed > 0.1f;
            if (!walking || Time.time < pullPeakTime + pullWalkCancelSeconds)
            {
                want = Mode.Pull;
            }
        }

        if (want != current)
        {
            Enter(want);
            return;
        }

        if (waitingShootEnd && !animator.IsInTransition(0) && IsFinished(shootHash))
        {
            waitingShootEnd = false;
            Play(aimState);
        }
    }

    /// <summary>その状態に入る（アニメーションを頭から流す）。</summary>
    private void Enter(Mode mode)
    {
        current = mode;
        waitingShootEnd = false;
        switch (mode)
        {
            case Mode.Fall: Play(dropState); break;
            case Mode.Throw: Play(throwState); break;
            case Mode.Pull:
                Play(pullState, pullOffset);
                pullPeakTime = Time.time + (pullPeakSeconds - pullOffset);
                break;
            case Mode.Shoot: waitingShootEnd = Play(shootState, shootStartSeconds); break;
            case Mode.Aim: Play(aimState); break;
            default: Play(idleState); break;
        }
    }

    /// <summary>
    /// 足場から落ちているかを調べる。位置の上下の動きだけで決めるので、ほかの人の体（通信で位置だけ届く）でも同じに働く。
    /// 吹き飛ばされて跳ねた・ゆっくり下がる床に乗っている・出てくる場所へ戻されたときは、落ちたことにしない。
    /// </summary>
    private void UpdateFalling()
    {
        float dt = Time.deltaTime;
        float y = transform.position.y;
        float moved = y - lastY;
        lastY = y;
        if (dt <= 0f)
        {
            return;
        }

        if (Mathf.Abs(moved) > TeleportDistance)
        {
            falling = false;
            groundY = y;
            stillTime = 0f;
            return;
        }

        float vy = moved / dt;
        stillTime = Mathf.Abs(vy) < StillSpeed ? stillTime + dt : 0f;
        bool standing = stillTime >= landSeconds;

        if (standing)
        {
            falling = false;
            groundY = y;
        }
        else if (!falling && vy < -fallSpeed && y < groundY - fallDepth)
        {
            falling = true;
        }
    }

    /// <summary>物資を両手で持っているか。自分で動かしている体は <see cref="SweeperReel"/>、ほかの人の体は通信で届いた値。</summary>
    private bool ReadCarrying()
    {
        if (network != null && network.IsSpawned && !network.IsOwner)
        {
            return netSync != null && netSync.IsCarrying;
        }

        return reel != null && reel.IsCarrying;
    }

    /// <summary>
    /// 歩く速さ。自分で動かしている体は SweeperMover の速さ、
    /// ほかの人の体（通信で位置だけ届く）は、横に動いた距離から求める。
    /// </summary>
    private float ReadSpeed()
    {
        if (network != null && network.IsSpawned && !network.IsOwner)
        {
            Vector3 position = transform.position;
            Vector3 moved = position - lastPosition;
            lastPosition = position;
            moved.y = 0f;

            float dt = Time.deltaTime;
            float raw = dt > 0f ? moved.magnitude / dt : 0f;
            // 届く位置はがたつくので、なめらかにする
            remoteSpeed = Mathf.Lerp(remoteSpeed, raw, 1f - Mathf.Exp(-remoteSmoothing * dt));
            return remoteSpeed;
        }

        lastPosition = transform.position;
        // 当たり判定が切られている間（運ばれている間など）は、SweeperMover が速さを更新しないので 0
        return characterController != null && !characterController.enabled ? 0f : mover.CurrentSpeed;
    }

    /// <summary>その状態を流していて、まだ終わりまで来ていないか。</summary>
    private bool IsPlaying(int hash)
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == hash && state.normalizedTime < 1f;
    }

    /// <summary>その状態を終わりまで流したか。</summary>
    private bool IsFinished(int hash)
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == hash && state.normalizedTime >= 1f;
    }

    /// <summary>その名前の状態があれば切り替える。<paramref name="startSeconds"/> はアニメーションの頭から何秒の所から流すか。切り替えたら true。</summary>
    private bool Play(string stateName, float startSeconds = 0f)
    {
        if (animator == null || !animator.isActiveAndEnabled || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            return false;
        }

        animator.CrossFadeInFixedTime(hash, crossFade, 0, Mathf.Max(0f, startSeconds));
        return true;
    }

    private bool HasFloat(string parameterName, out int hash)
    {
        hash = 0;
        if (string.IsNullOrEmpty(parameterName))
        {
            return false;
        }

        hash = Animator.StringToHash(parameterName);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Float)
            {
                return true;
            }
        }
        return false;
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (string.IsNullOrEmpty(childName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }
        return null;
    }
}
