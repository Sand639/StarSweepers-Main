using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ゴールの輸送機（SM_Transporter）の4方向の手すり。となりに地面がある側の手すりを、着地したときに床下へしまう。
/// 説明は Documents/スクリプトの説明/Game/Match/SpaceJunkGoalFence.md
/// </summary>
public class SpaceJunkGoalFence : MonoBehaviour
{
    /// <summary>手すりごとの、しまうかどうかの決め方。</summary>
    public enum SideMode
    {
        /// <summary>となりに地面があればしまう</summary>
        Auto,

        /// <summary>いつもしまう</summary>
        Lower,

        /// <summary>しまわない</summary>
        Keep
    }

    /// <summary>モデルの中の手すりの名前（前・後ろ・左・右）。前が、元のモデルで手すりの無かった側。</summary>
    public static readonly string[] FenceNames = { "Fence_Front", "Fence_Back", "Fence_Left", "Fence_Right" };

    [Header("どの手すりをしまうか（Auto＝となりに地面があればしまう）")]
    [SerializeField] private SideMode front = SideMode.Auto;
    [SerializeField] private SideMode back = SideMode.Auto;
    [SerializeField] private SideMode left = SideMode.Auto;
    [SerializeField] private SideMode right = SideMode.Auto;

    [Header("しまう動き")]
    [Tooltip("しまうのにかける秒数")]
    [Min(0.01f)]
    [SerializeField] private float lowerSeconds = 0.6f;

    [Tooltip("着地してから、しまい始めるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float lowerDelay = 0.15f;

    [Tooltip("試合の進行役（SpaceJunkRound）が無いシーン（ロビー・検証シーン）で、始まってからしまい始めるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float noRoundDelay = 0.5f;

    [Header("地面の調べ方")]
    [Tooltip("手すりから外側へ何 m の所に地面があるかを調べるか")]
    [Min(0.05f)]
    [SerializeField] private float probeDistance = 0.5f;

    [Tooltip("輸送機の床の高さから上下何 m までの面を、地面とみなすか")]
    [Min(0.05f)]
    [SerializeField] private float heightTolerance = 0.5f;

    /// <summary>しまうときに、床より少し深く下げる長さ（m）。上の端が床から出ないように。</summary>
    private const float ExtraDrop = 0.02f;

    /// <summary>進行役がつながらないまま、この秒数たったら、進行役が無いシーンとして扱う（通信なしで再生したとき）。</summary>
    private const float OfflineFallbackSeconds = 3f;

    /// <summary>地面を調べるときの当たりの入れ物（使い回す）。</summary>
    private static readonly RaycastHit[] rayHits = new RaycastHit[16];

    /// <summary>手すり1枚ぶん。</summary>
    private class Fence
    {
        public string Name;
        public SideMode Mode;
        public Transform Transform;
        public Renderer Renderer;
        public Collider Collider;
        public Vector3 RaisedPosition;
        public float Drop;
        public float HalfLength;
        public bool Lower;
    }

    private enum State
    {
        Waiting,
        Lowering,
        Done
    }

    private Fence[] fences;
    private Renderer bodyRenderer;
    private SpaceJunkRound round;
    private bool hasRound;
    private State state = State.Waiting;
    private float animationStart;
    private float startedAt;
    private double handledPlayStart = -1d;

    /// <summary>しまい終えたか（検証用）。</summary>
    public bool Finished => state == State.Done;

    // ------------------------------------------------------------
    // 輸送機のモデルを置いたシーンに、自動で付ける
    // ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoaded()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AttachTo(scene);
    }

    /// <summary>そのシーンにある、手すり（Fence_Front）を子に持つ物すべてに、この部品を付ける（付いていれば何もしない）。</summary>
    public static void AttachTo(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Transform body = child.parent;
                if (child.name == FenceNames[0] && body != null && body.GetComponent<SpaceJunkGoalFence>() == null)
                {
                    body.gameObject.AddComponent<SpaceJunkGoalFence>();
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 用意
    // ------------------------------------------------------------

    private void Awake()
    {
        bodyRenderer = GetComponent<Renderer>();
        SideMode[] modes = { front, back, left, right };
        fences = new Fence[FenceNames.Length];

        for (int i = 0; i < FenceNames.Length; i++)
        {
            Transform fenceTransform = transform.Find(FenceNames[i]);
            if (fenceTransform == null)
            {
                continue;
            }

            Fence fence = new Fence
            {
                Name = FenceNames[i],
                Mode = modes[i],
                Transform = fenceTransform,
                Renderer = fenceTransform.GetComponent<Renderer>(),
                RaisedPosition = fenceTransform.localPosition
            };

            // しまう長さと、手すりの長さの半分は、形（メッシュ）の大きさから決める
            MeshFilter filter = fenceTransform.GetComponent<MeshFilter>();
            Bounds bounds = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.up * 0.2f, new Vector3(1.5f, 0.4f, 0.05f));
            Vector3 scale = fenceTransform.localScale;
            fence.Drop = bounds.max.y * Mathf.Abs(scale.y) + ExtraDrop;
            Vector3 lossy = fenceTransform.lossyScale;
            fence.HalfLength = Mathf.Max(bounds.extents.x * Mathf.Abs(lossy.x), bounds.extents.z * Mathf.Abs(lossy.z));

            // モデルには当たり判定が入っていないので、ここで足す（しまうかどうか決まるまでは切っておく）
            fence.Collider = fenceTransform.GetComponent<Collider>();
            if (fence.Collider == null)
            {
                BoxCollider box = fenceTransform.gameObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                fence.Collider = box;
            }
            fence.Collider.enabled = false;

            fences[i] = fence;
        }
    }

    private void Start()
    {
        startedAt = Time.time;
        round = FindRoundInScene();
        hasRound = round != null;
        SyncMaterials();
    }

    private void Update()
    {
        if (state == State.Waiting)
        {
            if (TryGetSecondsUntilLanding(out float untilLanding) && untilLanding <= 0f)
            {
                DecideSides();
                // 遅れて始まった（途中から入った）ときは、そのぶん進んだ所から動かす
                animationStart = Time.time + untilLanding + (hasRound ? lowerDelay : noRoundDelay);
                state = State.Lowering;
            }
        }

        if (state == State.Lowering)
        {
            float t = Mathf.Clamp01((Time.time - animationStart) / lowerSeconds);
            ApplyProgress(t);
            if (t >= 1f)
            {
                state = State.Done;
            }
        }

        // 同じシーンのまま次のラウンドが始まったら、手すりを戻して、また着地を待つ
        if (state != State.Waiting && hasRound && round != null && round.IsSpawned && round.PlayStartServerTime != handledPlayStart)
        {
            Raise();
        }
    }

    private void LateUpdate()
    {
        // チームの色・光らせる色は本体の材質に入るので、手すりにも同じ材質を使う（始まりの演出が写しを作るより前に）
        SyncMaterials();
    }

    // ------------------------------------------------------------
    // いつしまうか
    // ------------------------------------------------------------

    /// <summary>このシーンの進行役（無ければ null）。</summary>
    private SpaceJunkRound FindRoundInScene()
    {
        if (SpaceJunkRound.Current != null && SpaceJunkRound.Current.gameObject.scene == gameObject.scene)
        {
            return SpaceJunkRound.Current;
        }

        foreach (SpaceJunkRound found in FindObjectsByType<SpaceJunkRound>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (found.gameObject.scene == gameObject.scene)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 着地するまでの秒数（過ぎていればマイナス）。まだ分からなければ false。
    /// 進行役が無いシーンでは、始まった時点を着地とみなす。
    /// </summary>
    private bool TryGetSecondsUntilLanding(out float seconds)
    {
        seconds = 0f;

        if (!hasRound || round == null)
        {
            hasRound = false;
            return true;
        }

        if (!round.IsSpawned)
        {
            // 通信なしで再生したときは、進行役がいつまでもつながらないので、進行役が無いシーンとして扱う
            Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
            if ((network == null || !network.IsListening) && Time.time - startedAt > OfflineFallbackSeconds)
            {
                hasRound = false;
                return true;
            }

            return false;
        }

        double playStart = round.PlayStartServerTime;
        handledPlayStart = playStart;
        if (playStart <= 0d)
        {
            // 始まりの演出なし
            return true;
        }

        double landing = round.IntroStartServerTime;
        switch (round.IntroStyle)
        {
            case SpaceJunkIntroStyle.GoalFlyIn:
                landing += round.FlyInSeconds + round.FlyInLandSeconds;
                break;
            case SpaceJunkIntroStyle.LandingTour:
                landing += round.LandingSeconds;
                break;
        }

        seconds = (float)(landing - round.ServerNow);
        return true;
    }

    // ------------------------------------------------------------
    // どの手すりをしまうか
    // ------------------------------------------------------------

    /// <summary>手すりごとに、しまうかどうかを決める。残す手すりだけ当たり判定を入れる。</summary>
    public void DecideSides()
    {
        foreach (Fence fence in fences)
        {
            if (fence == null)
            {
                continue;
            }

            fence.Lower = fence.Mode == SideMode.Lower || (fence.Mode == SideMode.Auto && HasGroundBeside(fence));
            fence.Collider.enabled = !fence.Lower;
        }
    }

    /// <summary>その手すりの外側に、輸送機の床と同じくらいの高さの地面があるか（手すりの中央と両端寄りの3か所のうち2か所以上）。</summary>
    private bool HasGroundBeside(Fence fence)
    {
        Vector3 center = fence.Transform.position;
        Vector3 outward = center - transform.position;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        outward.Normalize();
        Vector3 along = Vector3.Cross(Vector3.up, outward);
        float deckY = center.y;

        int found = 0;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 point = center + outward * probeDistance + along * (k * fence.HalfLength * 0.8f);
            if (IsGround(point, deckY))
            {
                found++;
            }
        }

        return found >= 2;
    }

    /// <summary>その場所の真下（床の高さの上下 heightTolerance m 以内）に、地面になる当たり判定があるか。</summary>
    private bool IsGround(Vector3 point, float deckY)
    {
        Vector3 origin = new Vector3(point.x, deckY + heightTolerance + 0.5f, point.z);
        float length = heightTolerance * 2f + 0.5f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, length, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = rayHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform) || hit is CharacterController)
            {
                continue;
            }

            // 床より高すぎる面（壁や箱の上）は地面にしない
            if (Mathf.Abs(rayHits[i].point.y - deckY) > heightTolerance)
            {
                continue;
            }

            // 物資など、動く物は地面にしない
            if (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    // ------------------------------------------------------------
    // 動かす
    // ------------------------------------------------------------

    /// <summary>しまう手すりを、t（0〜1）の所まで下げる。下げ終えたら見えなくする。</summary>
    public void ApplyProgress(float t)
    {
        float eased = Mathf.SmoothStep(0f, 1f, t);

        foreach (Fence fence in fences)
        {
            if (fence == null || !fence.Lower)
            {
                continue;
            }

            fence.Transform.localPosition = fence.RaisedPosition + Vector3.down * (fence.Drop * eased);
            if (fence.Renderer != null && t >= 1f)
            {
                fence.Renderer.enabled = false;
            }
        }
    }

    /// <summary>手すりを全部上げた状態に戻し、また着地を待つ。</summary>
    [ContextMenu("手すりを上げ直す（もう一度しまうのを見る）")]
    public void Raise()
    {
        foreach (Fence fence in fences)
        {
            if (fence == null)
            {
                continue;
            }

            fence.Lower = false;
            fence.Transform.localPosition = fence.RaisedPosition;
            fence.Collider.enabled = false;
            if (fence.Renderer != null)
            {
                fence.Renderer.enabled = true;
            }
        }

        startedAt = Time.time;
        state = State.Waiting;
    }

    /// <summary>手すりの材質を、本体の材質（チームの色が入った物）にそろえる。</summary>
    private void SyncMaterials()
    {
        if (bodyRenderer == null)
        {
            return;
        }

        Material body = bodyRenderer.sharedMaterial;
        foreach (Fence fence in fences)
        {
            if (fence != null && fence.Renderer != null && fence.Renderer.sharedMaterial != body)
            {
                fence.Renderer.sharedMaterial = body;
            }
        }
    }
}
