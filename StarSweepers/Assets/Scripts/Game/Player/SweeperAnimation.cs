using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）のアニメーションの切り替え口。**（2026/10/27・小野田さんの仕様）
///
/// ・狙い始めたら <c>A_AIM_01</c>
/// ・光線を撃った瞬間に <c>A_SHOOT</c> を流し、**終わったら <c>A_AIM_01</c> に戻る**
/// ・投げ終わったら Idle State（空なら何もしない。Animator の流れに任せる）
///
/// ・歩く速さを Animator の Speed に渡す（AC_SweeperPlayer が Idle と Walk を切り替える。2026/11/12）
///
/// Animator が無い・その名前の状態が無いときは、何もしない（エラーにもしない）。
/// </summary>
public class SweeperAnimation : MonoBehaviour
{
    [Tooltip("動かす Animator。空なら体の子から探す")]
    [SerializeField] private Animator animator;

    [Header("状態の名前（Animator の中の名前）")]
    [Tooltip("撃った瞬間に流す状態")]
    [SerializeField] private string shootState = "A_SHOOT";

    [Tooltip("狙っている間の状態。撃ったあとも、ここへ戻る")]
    [SerializeField] private string aimState = "A_AIM_01";

    [Tooltip("投げ終わったときに戻す状態。空なら何もしない")]
    [SerializeField] private string idleState = "";

    [Tooltip("歩く速さを渡す float のパラメーター名。空・無ければ渡さない")]
    [SerializeField] private string speedParameter = "Speed";

    [Tooltip("状態を切り替えるときのつなぎの秒数")]
    [Min(0f)]
    [SerializeField] private float crossFade = 0.05f;

    [Tooltip("ほかの人の体の速さを、どれだけ早く実際の動きに合わせるか（大きいほど早い）")]
    [Min(0.1f)]
    [SerializeField] private float remoteSmoothing = 10f;

    private SweeperMover mover;
    private CharacterController characterController;
    private Unity.Netcode.NetworkObject network;
    private Vector3 lastPosition;
    private float remoteSpeed;

    /// <summary>A_SHOOT を流していて、終わったら A_AIM_01 に戻す待ちか。</summary>
    private bool waitingShootEnd;

    private int shootHash;
    private int speedHash;
    private bool hasSpeed;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        mover = GetComponent<SweeperMover>();
        characterController = GetComponent<CharacterController>();
        network = GetComponent<Unity.Netcode.NetworkObject>();
        lastPosition = transform.position;
        shootHash = Animator.StringToHash(shootState);

        if (animator != null && !string.IsNullOrEmpty(speedParameter))
        {
            speedHash = Animator.StringToHash(speedParameter);
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeed = true;
                }
            }
        }
    }

    /// <summary>狙い始めた（A_AIM_01）。</summary>
    public void PlayAim()
    {
        waitingShootEnd = false;
        Play(aimState);
    }

    /// <summary>撃った（A_SHOOT を流し、終わったら A_AIM_01 に戻る）。</summary>
    public void PlayShoot()
    {
        waitingShootEnd = Play(shootState);
    }

    /// <summary>投げ終わった・やめた。</summary>
    public void PlayIdle()
    {
        waitingShootEnd = false;
        Play(idleState);
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        if (hasSpeed && mover != null)
        {
            animator.SetFloat(speedHash, ReadSpeed());
        }

        if (!waitingShootEnd || animator.IsInTransition(0))
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == shootHash && state.normalizedTime >= 1f)
        {
            waitingShootEnd = false;
            Play(aimState);
        }
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

    /// <summary>その名前の状態があれば切り替える。切り替えたら true。</summary>
    private bool Play(string stateName)
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

        animator.CrossFadeInFixedTime(hash, crossFade, 0, 0f);
        return true;
    }
}
