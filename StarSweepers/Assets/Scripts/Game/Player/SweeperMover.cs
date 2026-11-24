using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **プレイヤー（本実装）の移動と向き。**（2026/10/27・小野田さんの仕様）
///
/// ・左スティック／WASD で動く。**画面の上下左右に合わせる**（カメラが水平に回っていたら、そのぶん入力も回す）
/// ・体は**移動している方向へ向き直す**
/// ・狙っている間（<see cref="FaceAim"/> が ON）は、**狙う方向**を向く
/// ・光線を撃ってから物資に当たるまで（伸びている・戻っている間）は、<see cref="SweeperShooter"/> が <see cref="MoveLocked"/> を ON にして止める
/// ・物資を巻き取っている間は止めずに、<see cref="MoveSpeedScale"/> で**ゆっくり歩ける**ようにする（2026/11/10）
///
/// ・**重力と、外から与えられた勢い（ベルトコンベア・爆弾・隕石・列車などの吹き飛ばし。<see cref="Launch"/>）**もここで受け持つ
///   （2026/11/24 までは釣りから持ってきた別の部品 FishingPlayerController が受け持っていた）
///
/// **この部品を止める（enabled を切る）と、体が止まる。** 試合の進行役・ロビーの端末・復活待ちはこれで体を止め、
/// <see cref="SweeperNetSync"/> がそれに合わせて狙う・撃つも止める。
/// </summary>
[DefaultExecutionOrder(-20)]
[RequireComponent(typeof(CharacterController))]
public class SweeperMover : MonoBehaviour, ILaunchable
{
    [Header("移動")]
    [Tooltip("歩く速さ（メートル／秒）")]
    [Min(0f)]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("重力（下向きの加速度。マイナスの値）")]
    [SerializeField] private float gravity = -20f;

    [Header("外から与えられた勢い")]
    [Tooltip("吹き飛ばされた横方向の勢いが弱まる速さ。大きいほど早く止まる")]
    [Min(0f)]
    [SerializeField] private float launchDamping = 4f;

    [Header("向き")]
    [Tooltip("向き直る速さ（度／秒）。大きいほどすぐ向く")]
    [Min(0f)]
    [SerializeField] private float turnSpeed = 900f;

    [Header("参照")]
    [Tooltip("狙う向き。空なら同じ体から探す")]
    [SerializeField] private SweeperAim aim;

    [Tooltip("Assets/InputSystem_Actions を入れる")]
    [SerializeField] private InputActionAsset inputActions;

    private CharacterController characterController;
    private PlayerStun stun;
    private PlayerInputSource input;
    private InputActionMap playerMap;
    private InputAction moveAction;


    /// <summary>上下方向の速さ（メートル／秒）。重力で減り、吹き飛ばしで上向きになる。</summary>
    private float verticalVelocity;

    /// <summary>吹き飛ばされた横方向の勢い（メートル／秒）。歩く速さに足され、時間で弱まる。</summary>
    private Vector3 launchVelocity;

    /// <summary>ON の間は横に動かない（向きを変えることと、落ちることはできる）。</summary>
    public bool MoveLocked { get; set; }

    /// <summary>
    /// 歩く速さに掛ける倍率（0〜1。ふだんは 1）。巻き取り中に <see cref="SweeperShooter"/> が小さくして、
    /// 「引っ張りながら少しだけ動ける」ようにする（2026/11/10）。
    /// </summary>
    public float MoveSpeedScale
    {
        get => moveSpeedScale;
        set => moveSpeedScale = Mathf.Clamp01(value);
    }

    private float moveSpeedScale = 1f;

    /// <summary>ON の間は、移動方向ではなく狙う方向（<see cref="SweeperAim.Direction"/>）を向く。</summary>
    public bool FaceAim { get; set; }

    /// <summary>
    /// ON の間は、この部品は体を回さない（向きは <see cref="SweeperShooter"/> が決める）。
    /// 光線の先を狙いで曲げる撃ち方（挙動パターン C。2026/11/18）で、体を少しだけしか回さないために使う。
    /// </summary>
    public bool TurnLocked { get; set; }

    /// <summary>歩く速さ（メートル／秒）。</summary>
    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    /// <summary>いまの横の速さ（メートル／秒）。アニメーションの切り替えに使う。</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>いま向いている方向（水平）。</summary>
    public Vector3 Facing
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        stun = GetComponent<PlayerStun>();
        input = PlayerInputSource.Get(this);

        if (aim == null)
        {
            aim = GetComponent<SweeperAim>();
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
        moveAction = playerMap.FindAction("Move", true);
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

    private void Start()
    {
        // 見下ろしでマウスを狙いに使うので、カーソルは固定せず表示したままにする
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        playerMap?.Disable();

        // 止められたときに勢いを残さない（次に動き出した瞬間に飛んでいかないように）
        verticalVelocity = 0f;
        launchVelocity = Vector3.zero;
        CurrentSpeed = 0f;
    }

    private void Update()
    {
        // 当たり判定が切られている間（ほかの人の体・始まりの演出で運ばれている間）は動かさない
        if (!characterController.enabled)
        {
            return;
        }

        Vector3 direction = Vector3.zero;

        bool canMove = !GamePause.BlocksInput && !MoveLocked && (stun == null || !stun.IsStunned);
        if (canMove)
        {
            Vector2 move = input.ReadMove(moveAction);
            direction = new Vector3(move.x, 0f, move.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            // 画面の上下左右に合わせる（カメラが北向きなら回す量は 0）
            Camera view = Camera.main;
            if (view != null)
            {
                direction = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f) * direction;
            }
        }

        // 地面に着いているときは少し下向きに押し付けておく（段差で isGrounded がちらつかないように）
        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        verticalVelocity += gravity * Time.deltaTime;

        // 横＝歩く速さ＋吹き飛ばされた勢い、縦＝重力。1回の Move にまとめて渡す
        float speed = moveSpeed * moveSpeedScale;
        Vector3 velocity = direction * speed + launchVelocity;
        velocity.y = verticalVelocity;
        CollisionFlags collisions = characterController.Move(velocity * Time.deltaTime);

        // 上に飛ばされて天井に頭をぶつけたら、上向きの勢いを消す（天井に張り付かないように）
        if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }

        // 吹き飛ばされた横の勢いを、フレームレートに依らず同じ速さで弱めていく
        launchVelocity = Vector3.Lerp(launchVelocity, Vector3.zero, 1f - Mathf.Exp(-launchDamping * Time.deltaTime));

        CurrentSpeed = direction.magnitude * speed;

        Turn(direction);
    }

    /// <summary>
    /// **スタンせずに吹き飛ばす。** ベルトコンベア・爆弾・隕石・列車などから呼ばれる。
    /// 横の勢いは上書き（連続で巻き込まれたら新しいほう）、上の勢いは上向きのときだけ受け取る。
    /// 止められている体や、オンラインの他人の体（当たり判定が切ってある）には効かない
    /// （動かしている本人のPCだけが受け取り、位置は通信で配られる）。
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        if (!isActiveAndEnabled || characterController == null || !characterController.enabled)
        {
            return;
        }

        launchVelocity = new Vector3(velocity.x, 0f, velocity.z);
        if (velocity.y > 0f)
        {
            verticalVelocity = velocity.y;
        }
    }

    /// <summary>狙っている間は狙う方向へ、それ以外は動いている方向へ向き直る。止まっているときは今の向きのまま。</summary>
    private void Turn(Vector3 moveDirection)
    {
        // 向きをほかの部品が決めている間（光線の先を狙いで曲げる撃ち方。SweeperShooter）は回さない
        if (TurnLocked)
        {
            return;
        }

        Vector3 look = Vector3.zero;

        if (FaceAim && aim != null)
        {
            look = aim.Direction;
        }
        else if (moveDirection.sqrMagnitude > 0.0001f)
        {
            look = moveDirection;
        }

        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}
