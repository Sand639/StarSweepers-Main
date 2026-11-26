using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のアニメーションの切り替え口。**（2026/10/27・小野田さんの仕様。2026/11/25 に動きのアニメーションを追加）
///
/// 優先の高い順に、1つだけ流す。
/// | いまの様子 | 流す状態 |
/// | --- | --- |
/// | 足場から落ちている | Drop（落ちる瞬間）→ DropLoot（落ちている間のくり返し。Animator の中でつながる） |
/// | スタンしている | Fall（倒れる。頭の構えの部分は飛ばす）→ 倒れたまま → Rise（起き上がる）。**スタンが終わる瞬間に起き上がり終わる**よう、スタンの長さから逆算する。短いスタンでは速める。2026/11/26 |
/// | 物資を両手で持っている | Throw（持ち上げて前へ投げる。投げ終わるまで流し切る） |
/// | 物資を巻き取っている | Pull（くり返し） |
/// | 光線を撃った | Shoot（終わったら AimWalk へ戻る） |
/// | 押して狙っている | AimWalk（歩く速さで再生の速さが変わる。止まっていると止まったポーズ） |
/// | それ以外 | Idle（Animator が Speed で Idle と Walk を切り替える） |
///
/// 撃つ・巻き取る・やめるは <see cref="SweeperShooter"/> が呼ぶ。落ちる・スタン・両手で持つは、ここで毎フレーム調べる。
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
        Drop,
        Stun,
        Rise,
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

    [Tooltip("物資を巻き取っている間の状態")]
    [SerializeField] private string pullState = "Pull";

    [Tooltip("物資を両手で持って投げる状態")]
    [SerializeField] private string throwState = "Throw";

    [Tooltip("足場から落ち始めたときの状態（終わると Animator の中で落ちている間のくり返しへ移る）")]
    [SerializeField] private string dropState = "Drop";

    [Tooltip("スタンしたときの状態（倒れる。2026/11/26）")]
    [SerializeField] private string stunState = "Fall";

    [Tooltip("スタンが解ける直前に流す状態（起き上がる。スタンが終わる瞬間に流し終わる）")]
    [SerializeField] private string riseState = "Rise";

    [Tooltip("倒れる・起き上がるの再生の速さを渡す float のパラメーター名（スタンが短いと速くする）。空・無ければ渡さない")]
    [SerializeField] private string stunSpeedParameter = "StunSpeed";

    [Header("スタン（Fall → Rise。2026/11/26）")]
    [Tooltip("倒れるアニメーション（AN_Fall）をこの秒から流す。頭の約 0.9 秒は銃を構えて腕を上げる動きで、撃つ動きに見えるため飛ばす")]
    [Min(0f)]
    [SerializeField] private float stunFallStart = 0.9f;

    [Tooltip("上の秒から倒れきるまでの秒数（AN_Fall は 1.7 秒で倒れきる）。スタンの長さを、倒れる・起き上がるに分けるのに使う")]
    [Min(0.01f)]
    [SerializeField] private float stunFallSeconds = 0.8f;

    [Tooltip("起き上がるアニメーション（AN_Rise）の長さ（秒）。スタンが終わる瞬間に流し終わるよう、この秒数だけ前から流す")]
    [Min(0.01f)]
    [SerializeField] private float stunRiseSeconds = 1f;

    [Tooltip("スタンが短いときに速める上限（倍）")]
    [Min(1f)]
    [SerializeField] private float stunMaxSpeed = 4f;

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
    [Tooltip("左手の骨。名前か、途中の骨からのたどり方（例：JNT_Shoulder_L/JNT_Arm_L/JNT_Hand_L）")]
    [SerializeField] private string leftHandBone = "JNT_Shoulder_L/JNT_Arm_L/JNT_Hand_L";

    [Tooltip("右手の骨。新しいリグ（2026/11/26）では右腕も JNT_Arm_L・JNT_Hand_L という名前なので、肩からのたどり方で書く")]
    [SerializeField] private string rightHandBone = "JNT_Shoulder_L2/JNT_Arm_L/JNT_Hand_L";

    /// <summary>上下の速さがこれより小さければ「止まっている」（m/秒）。</summary>
    private const float StillSpeed = 0.5f;

    /// <summary>1 フレームでこれより大きく上下に動いたら、瞬間移動（出てくる場所へ戻された等）とみなす（m）。</summary>
    private const float TeleportDistance = 2f;

    private SweeperMover mover;
    private SweeperReel reel;
    private SweeperNetSync netSync;
    private PlayerStun stun;
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

    private bool falling;
    private float groundY;
    private float lastY;
    private float stillTime;

    private int shootHash;
    private int throwHash;
    private int speedHash;
    private int aimSpeedHash;
    private bool hasSpeed;
    private bool hasAimSpeed;
    private int stunSpeedHash;
    private bool hasStunSpeed;

    /// <summary>このスタンの、倒れる・起き上がるの再生の速さ（倍）。スタンが「倒れる＋起き上がる」より短いと 1 より大きい。</summary>
    private float stunPlaySpeed = 1f;

    /// <summary>スタンの残りがこの秒数を切ったら起き上がり始める（起き上がりの長さ ÷ 速さ）。</summary>
    private float stunRiseAt;

    /// <summary>前のフレームのスタンの残り（重ねてスタンして伸びたのに気づくため）。</summary>
    private float lastStunRemaining;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        mover = GetComponent<SweeperMover>();
        reel = GetComponent<SweeperReel>();
        netSync = GetComponent<SweeperNetSync>();
        stun = GetComponent<PlayerStun>();
        characterController = GetComponent<CharacterController>();
        network = GetComponent<Unity.Netcode.NetworkObject>();
        lastPosition = transform.position;
        groundY = lastY = transform.position.y;
        shootHash = Animator.StringToHash(shootState);
        throwHash = Animator.StringToHash(throwState);

        if (animator != null)
        {
            hasSpeed = HasFloat(speedParameter, out speedHash);
            hasAimSpeed = HasFloat(aimSpeedParameter, out aimSpeedHash);
            hasStunSpeed = HasFloat(stunSpeedParameter, out stunSpeedHash);
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

    /// <summary>物資をつかんで巻き取り始めた（Pull）。</summary>
    public void PlayPull()
    {
        SetShotMode(Mode.Pull);
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

        bool stunned = stun != null && stun.IsStunned;
        if (stunned)
        {
            PlanStun(stun.Remaining);
        }
        else
        {
            lastStunRemaining = 0f;
        }

        // スタン中：残りが「起き上がりの長さ」を切るまでは倒れる、切ったら起き上がる（スタンが終わる瞬間に立ち上がり終わる）
        Mode stunMode = stunned && stun.Remaining > stunRiseAt ? Mode.Stun : Mode.Rise;
        Mode want = falling ? Mode.Drop : stunned ? stunMode : ReadCarrying() ? Mode.Throw : shotMode;

        // 投げる動きは、持ち終わっても振り切るまで流す（落ちた・次を撃ったときは切り替える）
        if (current == Mode.Throw && want == Mode.Move && IsPlaying(throwHash))
        {
            want = Mode.Throw;
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
            case Mode.Drop: Play(dropState); break;
            case Mode.Stun: Play(stunState, stunFallStart); break;
            case Mode.Rise: Play(riseState); break;
            case Mode.Throw: Play(throwState); break;
            case Mode.Pull: Play(pullState); break;
            case Mode.Shoot: waitingShootEnd = Play(shootState); break;
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

    /// <summary>
    /// スタンの長さから、倒れる・起き上がるの速さと、起き上がり始める時刻を決める。スタンし始めた・重ねて伸びたときだけ決め直す。
    /// 「倒れる（Stun Fall Seconds）＋起き上がる（Stun Rise Seconds）」よりスタンが短ければ、その割合で速める。
    /// </summary>
    private void PlanStun(float remaining)
    {
        bool started = remaining > lastStunRemaining + 0.01f;
        lastStunRemaining = remaining;
        if (!started)
        {
            return;
        }

        float need = stunFallSeconds + stunRiseSeconds;
        stunPlaySpeed = Mathf.Clamp(need / Mathf.Max(0.01f, remaining), 1f, stunMaxSpeed);
        stunRiseAt = stunRiseSeconds / stunPlaySpeed;
        if (hasStunSpeed)
        {
            animator.SetFloat(stunSpeedHash, stunPlaySpeed);
        }
    }

    /// <summary>その名前の状態があれば切り替える。切り替えたら true。</summary>
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

        animator.CrossFadeInFixedTime(hash, crossFade, 0, startSeconds);
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

    /// <summary>骨を探す。<paramref name="bonePath"/> は名前か「途中の骨/…/骨」のたどり方（同じ名前の骨が2つあるとき用）。</summary>
    private static Transform FindChild(Transform root, string bonePath)
    {
        if (string.IsNullOrEmpty(bonePath))
        {
            return null;
        }

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            Transform found = node.Find(bonePath);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
