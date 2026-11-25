using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// **試合に勝ったチームの「メンバー紹介」の演出。**（2026/11/25 試作）
///
/// 遠くに3Dの台を作り、勝ったチームのメンバーの体の写しを並べる。
/// カメラが1人ずつ寄って名前の帯を出し（その人がジャンプして一回転）、最後は引きの画で全員が跳ねて「WIN!」と紙吹雪。
/// 3Dの絵はテクスチャに描いて、いちばん手前の画面に貼る（試合中の表示の上に出すため）。
/// **Enter を押すと飛ばせる**（最後の画まで進めて、すぐ終わったことにする）。
///
/// 各PCで同じように再生する（通信はしない）。始める合図は <c>SpaceJunkSession.VictoryShowcase.cs</c>。
/// 試すときは検証シーン `Scenes/Test/MatchVictoryShowcaseTest`。詳しくは `Documents/機能ドキュメント/勝利演出（メンバー紹介）.md`。
/// </summary>
public sealed class MatchVictoryShowcase : MonoBehaviour
{
    /// <summary>紹介する1人。</summary>
    public struct Member
    {
        /// <summary>名前の帯に出す名前。</summary>
        public string Name;

        /// <summary>写す体のモデル（Animator が付いている物）。</summary>
        public Transform Model;

        /// <summary>そのプレイヤーのいちばん上。モデルの向きの基準にする。null ならモデルの向きをそのまま使う。</summary>
        public Transform Root;
    }

    [Header("時間（秒）")]
    [Tooltip("始まりの、上から台へカメラが降りてくる時間")]
    [SerializeField] private float introSeconds = 1.3f;

    [Tooltip("1人を紹介する時間（5人以上のときは自動で短くする）")]
    [SerializeField] private float perMemberSeconds = 1.0f;

    [Tooltip("最後の「WIN!」を見せる時間")]
    [SerializeField] private float finaleSeconds = 3.2f;

    [Header("並び")]
    [Tooltip("メンバーの間隔（m）")]
    [SerializeField] private float memberSpacing = 1.35f;

    [Tooltip("体の写しの背の高さ（m）。モデルの大きさにかかわらず、この高さにそろえる")]
    [SerializeField] private float memberHeight = 1.6f;

    [Tooltip("紹介のときのジャンプの高さ（m）")]
    [SerializeField] private float hopHeight = 0.55f;

    /// <summary>演出が終わった（最後の画で止まっている）か。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>このPCで Enter を押して飛ばしたか。</summary>
    public bool WasSkipped { get; private set; }

    /// <summary>3Dの台を写すカメラ（確認用に外から撮るときに使う）。</summary>
    public Camera ShowcaseCamera => showcaseCamera;

    /// <summary>名前や「WIN!」を出す画面（確認用に外から撮るときに使う）。</summary>
    public Canvas OverlayCanvas => canvas;

    /// <summary>この演出の物だけを写すための層。結果画面（RoundWinnerResultUI）の 30 とは分ける。</summary>
    private const int ShowcaseLayer = 29;

    /// <summary>台を作る場所。マップから十分に離す。</summary>
    private static readonly Vector3 StageOrigin = new Vector3(0f, 3000f, 0f);

    private static readonly Color Gold = new Color(1f, 0.82f, 0.28f);
    private const int ConfettiCount = 80;

    private readonly List<Transform> slots = new List<Transform>();
    private readonly List<Vector3> slotHomes = new List<Vector3>();
    private readonly List<string> names = new List<string>();
    private readonly List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();

    private int winnerTeam;
    private Color teamColor;
    private Action finished;
    private double startTime;
    private double finishedAt = -1;

    private Transform stage;
    private Transform backdrop;
    private Camera showcaseCamera;
    private RenderTexture renderTexture;
    private Canvas canvas;
    private RawImage view;
    private Image flash;
    private RectTransform card;
    private Text cardLabel;
    private Text cardName;
    private RectTransform win;
    private Text winText;
    private RectTransform sub;
    private Text subText;
    private readonly RectTransform[] confetti = new RectTransform[ConfettiCount];
    private readonly Vector4[] confettiParams = new Vector4[ConfettiCount];
    private readonly Vector2[] confettiSway = new Vector2[ConfettiCount];

    // ------------------------------------------------------------
    // 入口
    // ------------------------------------------------------------

    /// <summary>
    /// **演出を新しく作って始める。** いまのシーンに作るので、シーンが切り替わると一緒に消える。
    /// 終わったら <paramref name="onFinished"/> を1回だけ呼ぶ（最後の画は消さずに残す）。
    /// </summary>
    public static MatchVictoryShowcase PlayNew(int winnerTeam, IList<Member> members, Action onFinished)
    {
        GameObject host = new GameObject("MatchVictoryShowcase");
        MatchVictoryShowcase showcase = host.AddComponent<MatchVictoryShowcase>();
        showcase.Play(winnerTeam, members, onFinished);
        return showcase;
    }

    /// <summary>
    /// **いまのシーンにいる、そのチームのプレイヤー（ボットも含む）を集める。**
    /// 並び順は参加番号の順（どのPCでも同じ並びになるように）。
    /// </summary>
    public static List<Member> CollectTeam(int team)
    {
        List<SweeperPlayer> players = new List<SweeperPlayer>();
        foreach (SweeperPlayer player in FindObjectsByType<SweeperPlayer>(FindObjectsSortMode.None))
        {
            if (player != null && player.TeamIndex == team && FindModel(player.transform) != null)
            {
                players.Add(player);
            }
        }

        players.Sort((a, b) =>
        {
            int byIndex = OrderOf(a).CompareTo(OrderOf(b));
            return byIndex != 0 ? byIndex : string.CompareOrdinal(a.PlayerName, b.PlayerName);
        });

        List<Member> members = new List<Member>();
        foreach (SweeperPlayer player in players)
        {
            members.Add(new Member { Name = player.PlayerName, Model = FindModel(player.transform), Root = player.transform });
        }

        return members;
    }

    /// <summary>体のモデル（Animator の付いた物）。</summary>
    public static Transform FindModel(Transform playerRoot)
    {
        Animator animator = playerRoot.GetComponentInChildren<Animator>(true);
        return animator != null ? animator.transform : null;
    }

    private static int OrderOf(SweeperPlayer player)
    {
        SweeperNetPlayer net = player.GetComponentInParent<SweeperNetPlayer>();
        return net != null && net.PlayerIndex >= 0 ? net.PlayerIndex : int.MaxValue;
    }

    /// <summary>演出を始める（作り直してから最初から）。</summary>
    public void Play(int team, IList<Member> members, Action onFinished)
    {
        winnerTeam = Mathf.Max(0, team);
        teamColor = SpaceJunkTeams.TeamColor(winnerTeam);
        finished = onFinished;
        finishedAt = -1;
        IsFinished = false;
        WasSkipped = false;

        Build(members);
        startTime = Time.unscaledTimeAsDouble;
        DrawAt(0f);
    }

    private void Update()
    {
        if (stage == null)
        {
            return;
        }

        if (renderTexture != null && (renderTexture.width != ScreenWidth || renderTexture.height != ScreenHeight))
        {
            CreateRenderTexture(ScreenWidth, ScreenHeight);
        }

        // Enter で飛ばす：最後の画（WIN!）まで進めて、すぐ終わったことにする
        Keyboard keyboard = Keyboard.current;
        if (!IsFinished && keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            WasSkipped = true;
            startTime = Time.unscaledTimeAsDouble - TotalSeconds;
            finishedAt = Time.unscaledTimeAsDouble - 0.3;
        }

        DrawAt((float)(Time.unscaledTimeAsDouble - startTime));

        if (!IsFinished || finished == null)
        {
            return;
        }

        if (finishedAt < 0) finishedAt = Time.unscaledTimeAsDouble;
        if (Time.unscaledTimeAsDouble < finishedAt + 0.3) return;

        Action callback = finished;
        finished = null;
        callback();
    }

    private void OnDestroy()
    {
        if (stage != null) DestroySafe(stage.gameObject);
        if (renderTexture != null) renderTexture.Release();
        foreach (UnityEngine.Object asset in ownedAssets)
        {
            if (asset != null) DestroySafe(asset);
        }
    }

    // ------------------------------------------------------------
    // 時間ごとの見た目
    // ------------------------------------------------------------

    private float PerMember => slots.Count > 4 ? perMemberSeconds * 0.75f : perMemberSeconds;
    private float MembersEnd => introSeconds + slots.Count * PerMember;
    private float FinaleStart => MembersEnd + 0.3f;
    private float TotalSeconds => FinaleStart + finaleSeconds;

    /// <summary>**始まってから <paramref name="t"/> 秒の見た目にする。** 毎フレーム呼ぶ（確認用に好きな時刻でも呼べる）。</summary>
    public void DrawAt(float t)
    {
        int count = slots.Count;
        float per = PerMember;
        bool inMembers = count > 0 && t >= introSeconds && t < MembersEnd;
        int current = inMembers ? Mathf.Min((int)((t - introSeconds) / per), count - 1) : -1;
        float u = inMembers ? (t - introSeconds) / per - current : 0f;
        float finale = t - FinaleStart;

        DrawCamera(t, current, u);
        DrawMembers(current, u, finale);
        backdrop.localRotation = Quaternion.Euler(0f, 0f, t * 10f);

        // 始まりはチームの色で覆った画面が晴れていく。最後の「WIN!」の瞬間は白く光る
        if (t < 0.7f)
        {
            flash.color = WithAlpha(teamColor, 1f - EaseOutCubic(t / 0.7f));
        }
        else if (finale >= 0f && finale < 0.3f)
        {
            flash.color = new Color(1f, 1f, 1f, 0.7f * (1f - finale / 0.3f));
        }
        else
        {
            flash.color = Color.clear;
        }

        DrawCard(current, u);
        DrawWin(finale);
        DrawConfetti(finale);

        IsFinished = t >= TotalSeconds;
    }

    private struct CameraPose
    {
        public Vector3 Position;
        public Vector3 Target;

        public static CameraPose Lerp(CameraPose a, CameraPose b, float k)
        {
            return new CameraPose
            {
                Position = Vector3.LerpUnclamped(a.Position, b.Position, k),
                Target = Vector3.LerpUnclamped(a.Target, b.Target, k),
            };
        }
    }

    private void DrawCamera(float t, int current, float u)
    {
        float h = memberHeight;
        Vector3 center = stage.position;
        float distance = WideDistance();

        CameraPose wide = new CameraPose
        {
            Position = center + new Vector3(0f, h * 0.75f, -distance),
            Target = center + new Vector3(0f, h * 0.55f, 0f),
        };
        CameraPose start = new CameraPose
        {
            Position = center + new Vector3(0f, h * 4f, -distance * 1.8f),
            Target = center + new Vector3(0f, h * 0.3f, 0f),
        };
        // 最後は少し低い位置から見上げて、上に「WIN!」を出す空きを作る
        CameraPose finalePose = new CameraPose
        {
            Position = center + new Vector3(0f, h * 0.7f, -distance * 1.08f),
            Target = center + new Vector3(0f, h * 1.25f, 0f),
        };

        CameraPose pose;
        if (t < introSeconds)
        {
            pose = CameraPose.Lerp(start, wide, EaseOutCubic(Mathf.Clamp01(t / introSeconds)));
        }
        else if (current >= 0)
        {
            CameraPose from = current == 0 ? wide : Close(current - 1);
            pose = CameraPose.Lerp(from, Close(current), EaseInOut(Mathf.Clamp01(u / 0.3f)));
        }
        else
        {
            CameraPose from = slots.Count > 0 ? Close(slots.Count - 1) : wide;
            pose = CameraPose.Lerp(from, finalePose, EaseInOut(Mathf.Clamp01((t - MembersEnd) / 0.45f)));
        }

        // 止まって見えないように、ほんの少しだけ揺らす
        pose.Position += new Vector3(Mathf.Sin(t * 0.7f) * 0.06f, Mathf.Sin(t * 0.9f) * 0.03f, 0f);
        showcaseCamera.transform.position = pose.Position;
        showcaseCamera.transform.LookAt(pose.Target);
    }

    /// <summary>1人に寄った画。名前の帯（左下）と重ならないよう、体を画面の少し右に置く。</summary>
    private CameraPose Close(int index)
    {
        Vector3 home = stage.TransformPoint(slotHomes[index]);
        float h = memberHeight;
        return new CameraPose
        {
            Position = home + new Vector3(-0.1f, h * 0.62f, -h * 2.9f),
            Target = home + new Vector3(-0.55f, h * 0.5f, 0f),
        };
    }

    /// <summary>全員が入る距離。</summary>
    private float WideDistance()
    {
        float tanHalf = Mathf.Tan(showcaseCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = Mathf.Max(0, slots.Count - 1) * memberSpacing * 0.5f + 1.1f;
        float aspect = Mathf.Max(0.5f, showcaseCamera.aspect);
        return Mathf.Max(halfWidth / (tanHalf * aspect), memberHeight * 1.1f / tanHalf);
    }

    private void DrawMembers(int current, float u, float finale)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            float lift = 0f;
            float spin = 0f;

            if (i == current)
            {
                float hop = Mathf.Clamp01((u - 0.3f) / 0.45f);
                lift = hopHeight * 4f * hop * (1f - hop);
                spin = 360f * Mathf.SmoothStep(0f, 1f, hop);
            }
            else if (finale > 0f)
            {
                float own = finale - i * 0.09f;
                if (own > 0f)
                {
                    float p = own / 0.62f % 1f;
                    lift = hopHeight * 0.8f * 4f * p * (1f - p);
                }
            }

            slots[i].localPosition = slotHomes[i] + Vector3.up * lift;
            slots[i].localRotation = Quaternion.Euler(0f, spin, 0f);
        }
    }

    private void DrawCard(int current, float u)
    {
        card.gameObject.SetActive(current >= 0);
        if (current < 0)
        {
            return;
        }

        float slideIn = EaseOutBack(Mathf.Clamp01((u - 0.2f) / 0.25f));
        float slideOut = EaseInCubic(Mathf.Clamp01((u - 0.85f) / 0.15f));
        card.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-1100f, 70f, slideIn) - 1300f * slideOut, 110f);
        cardLabel.text = $"MEMBER  {current + 1} / {slots.Count}";
        cardName.text = names[current];
    }

    private void DrawWin(float finale)
    {
        win.gameObject.SetActive(finale >= 0f);
        sub.gameObject.SetActive(finale >= 0.25f);
        if (finale < 0f)
        {
            return;
        }

        float scale = finale < 0.3f
            ? Mathf.LerpUnclamped(3f, 1f, EaseOutBack(finale / 0.3f))
            : 1f + 0.03f * Mathf.Sin((finale - 0.3f) * 6f);
        win.localScale = Vector3.one * scale;
        winText.color = WithAlpha(Color.white, Mathf.Clamp01(finale / 0.12f));

        float subT = EaseOutCubic(Mathf.Clamp01((finale - 0.25f) / 0.35f));
        sub.anchoredPosition = new Vector2(0f, 175f + 40f * (1f - subT));
        subText.color = WithAlpha(Color.white, subT);
    }

    private void DrawConfetti(float finale)
    {
        for (int i = 0; i < ConfettiCount; i++)
        {
            Vector4 p = confettiParams[i]; // x = 横の位置, y = 出る時刻, z = 落ちる速さ, w = 回る速さ
            float own = finale - p.y;
            bool visible = own > 0f;
            confetti[i].gameObject.SetActive(visible);
            if (!visible)
            {
                continue;
            }

            float fall = own * p.z % 1400f;
            float x = p.x + Mathf.Sin(own * confettiSway[i].y) * confettiSway[i].x;
            confetti[i].anchoredPosition = new Vector2(x, 660f - fall);
            confetti[i].localRotation = Quaternion.Euler(own * p.w * 0.6f, 0f, own * p.w);
        }
    }

    // ------------------------------------------------------------
    // 作る
    // ------------------------------------------------------------

    private static int ScreenWidth => Screen.width >= 64 ? Screen.width : 1920;
    private static int ScreenHeight => Screen.height >= 64 ? Screen.height : 1080;

    private void Build(IList<Member> members)
    {
        Clear();

        stage = new GameObject("VictoryStage").transform;
        stage.position = StageOrigin;

        BuildCamera();
        BuildPlatform(members.Count);
        BuildBackdrop();
        BuildLights();

        float firstX = -(members.Count - 1) * memberSpacing * 0.5f;
        for (int i = 0; i < members.Count; i++)
        {
            Transform slot = new GameObject($"Member{i + 1}").transform;
            slot.SetParent(stage, false);
            Vector3 home = new Vector3(firstX + i * memberSpacing, 0f, 0f);
            slot.localPosition = home;
            slots.Add(slot);
            slotHomes.Add(home);
            names.Add(string.IsNullOrEmpty(members[i].Name) ? $"プレイヤー{i + 1}" : members[i].Name);
            CopyBody(members[i], slot);
        }

        SetLayer(stage, ShowcaseLayer);
        BuildOverlay();
    }

    private void Clear()
    {
        if (stage != null) DestroySafe(stage.gameObject);
        if (canvas != null) DestroySafe(canvas.gameObject);
        slots.Clear();
        slotHomes.Clear();
        names.Clear();
    }

    /// <summary>
    /// **体のモデルを写して、台の上に正面向きで立たせる。** 背の高さは <see cref="memberHeight"/> にそろえる。
    /// 服の色（CharacterColorController の値）は写しにも引き継がれる。
    /// </summary>
    private void CopyBody(Member member, Transform slot)
    {
        if (member.Model == null)
        {
            return;
        }

        Quaternion facing = member.Root != null
            ? Quaternion.Inverse(member.Root.rotation) * member.Model.rotation
            : member.Model.rotation;

        GameObject body = Instantiate(member.Model.gameObject);
        body.name = "Body";
        body.SetActive(true);
        Transform bodyTransform = body.transform;
        bodyTransform.SetParent(slot, false);
        bodyTransform.localScale = member.Model.lossyScale;
        // 体の前（+Z）を、カメラのいる -Z へ向ける
        bodyTransform.localRotation = Quaternion.Euler(0f, 180f, 0f) * facing;
        bodyTransform.localPosition = Vector3.zero;

        Animator animator = body.GetComponent<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // 背の高さをそろえて、足を台の上に乗せる
        if (TryGetBounds(body, out Bounds bounds) && bounds.size.y > 0.01f)
        {
            bodyTransform.localScale *= memberHeight / bounds.size.y;
            TryGetBounds(body, out bounds);
            bodyTransform.position += Vector3.up * (slot.position.y - bounds.min.y);
        }
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private void BuildCamera()
    {
        showcaseCamera = new GameObject("VictoryCamera").AddComponent<Camera>();
        showcaseCamera.transform.SetParent(stage, false);
        showcaseCamera.fieldOfView = 30f;
        showcaseCamera.nearClipPlane = 0.1f;
        showcaseCamera.farClipPlane = 100f;
        showcaseCamera.clearFlags = CameraClearFlags.SolidColor;
        showcaseCamera.backgroundColor = Color.Lerp(teamColor, Color.black, 0.6f);
        showcaseCamera.cullingMask = 1 << ShowcaseLayer;
        CreateRenderTexture(ScreenWidth, ScreenHeight);
    }

    private void CreateRenderTexture(int width, int height)
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            DestroySafe(renderTexture);
            ownedAssets.Remove(renderTexture);
        }

        renderTexture = new RenderTexture(width, height, 24) { name = "VictoryShowcaseView", antiAliasing = 4 };
        ownedAssets.Add(renderTexture);
        showcaseCamera.targetTexture = renderTexture;
        showcaseCamera.aspect = (float)width / height;
        if (view != null) view.texture = renderTexture;
    }

    private void BuildPlatform(int memberCount)
    {
        float width = Mathf.Max(2.4f, memberCount * memberSpacing + 0.8f);
        Shape(PrimitiveType.Cylinder, "Platform", new Vector3(0f, -0.12f, 0f),
            new Vector3(width, 0.12f, 2.6f), Color.Lerp(teamColor, Color.white, 0.75f));
        Shape(PrimitiveType.Cylinder, "PlatformRim", new Vector3(0f, -0.3f, 0f),
            new Vector3(width + 0.5f, 0.2f, 3.1f), Color.Lerp(teamColor, Color.black, 0.25f));
    }

    private void Shape(PrimitiveType type, string objectName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = objectName;
        Collider shapeCollider = shape.GetComponent<Collider>();
        if (shapeCollider != null) DestroySafe(shapeCollider);
        shape.transform.SetParent(stage, false);
        shape.transform.localPosition = position;
        shape.transform.localScale = scale;

        Renderer shapeRenderer = shape.GetComponent<Renderer>();
        Material material = new Material(shapeRenderer.sharedMaterial) { color = color };
        ownedAssets.Add(material);
        shapeRenderer.sharedMaterial = material;
        shapeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>後ろでゆっくり回る、チームの色の放射状のしま模様。</summary>
    private void BuildBackdrop()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Backdrop";
        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null) DestroySafe(quadCollider);
        backdrop = quad.transform;
        backdrop.SetParent(stage, false);
        backdrop.localPosition = new Vector3(0f, 1.5f, 9f);
        backdrop.localScale = Vector3.one * 60f;

        Texture2D texture = SunburstTexture(Color.Lerp(teamColor, Color.white, 0.1f), Color.Lerp(teamColor, Color.black, 0.35f));
        ownedAssets.Add(texture);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Material material = new Material(shader) { mainTexture = texture, color = Color.white };
        ownedAssets.Add(material);
        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Texture2D SunburstTexture(Color light, Color dark)
    {
        const int size = 512;
        const int rays = 18;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "VictorySunburst",
            wrapMode = TextureWrapMode.Clamp,
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float angle = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f) * rays;
                bool even = (int)angle % 2 == 0;
                // しまの境目をぼかしてギザギザを消す（境目からの距離をピクセルで測る）
                float edge = angle - Mathf.Floor(angle);
                float edgePixels = Mathf.Min(edge, 1f - edge) * (Mathf.PI * 2f / rays) * radius * size * 0.5f;
                Color color = Color.Lerp(even ? dark : light, even ? light : dark, 0.5f + 0.5f * Mathf.Clamp01(edgePixels));
                color = Color.Lerp(color, Color.white, Mathf.Pow(Mathf.Clamp01(1f - radius * 3f), 2f) * 0.6f);
                color = Color.Lerp(color, dark * 0.6f, Mathf.Clamp01(radius - 0.35f) * 0.6f);
                color.a = 1f;
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>台の上を照らす明かり。この演出の層だけを照らす。</summary>
    private void BuildLights()
    {
        Light spot = new GameObject("VictorySpot").AddComponent<Light>();
        spot.transform.SetParent(stage, false);
        spot.transform.localPosition = new Vector3(0f, 6f, -4.5f);
        spot.transform.LookAt(stage.position + Vector3.up * 0.8f);
        spot.type = LightType.Spot;
        spot.range = 25f;
        spot.spotAngle = 70f;
        spot.intensity = 45f;
        spot.cullingMask = 1 << ShowcaseLayer;

        Light fill = new GameObject("VictoryFill").AddComponent<Light>();
        fill.transform.SetParent(stage, false);
        fill.transform.localPosition = new Vector3(0f, 1.8f, -3.5f);
        fill.type = LightType.Point;
        fill.range = 15f;
        fill.intensity = 12f;
        fill.color = Color.Lerp(teamColor, Color.white, 0.7f);
        fill.cullingMask = 1 << ShowcaseLayer;
    }

    private void BuildOverlay()
    {
        GameObject canvasObject = new GameObject("VictoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.layer = 5; // UI
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 結果画面の色帯（30000）より手前、場面の切り替え（SceneTransition）より奥
        canvas.sortingOrder = 30001;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canvasObject.transform;

        view = NewRect("View", root).gameObject.AddComponent<RawImage>();
        Stretch(view.rectTransform);
        view.texture = renderTexture;
        view.raycastTarget = false;

        Font font = UiFont.Find(64);

        // 名前の帯（左下からすべり込む）
        card = NewRect("NameCard", root);
        card.anchorMin = card.anchorMax = card.pivot = Vector2.zero;
        card.sizeDelta = new Vector2(980f, 190f);
        card.localRotation = Quaternion.Euler(0f, 0f, 4f);
        Image band = card.gameObject.AddComponent<Image>();
        band.color = teamColor;
        band.raycastTarget = false;
        RectTransform stripe = NewRect("Stripe", card);
        stripe.anchorMin = new Vector2(0f, 0f);
        stripe.anchorMax = new Vector2(0f, 1f);
        stripe.pivot = new Vector2(0f, 0.5f);
        stripe.sizeDelta = new Vector2(26f, 0f);
        Image stripeImage = stripe.gameObject.AddComponent<Image>();
        stripeImage.color = Gold;
        stripeImage.raycastTarget = false;
        cardLabel = NewText("Label", card, font, 34, TextAnchor.UpperLeft, new Vector2(60f, -18f), new Vector2(900f, 50f));
        cardLabel.color = Color.Lerp(Color.white, Gold, 0.6f);
        cardName = NewText("Name", card, font, 84, TextAnchor.LowerLeft, new Vector2(60f, -62f), new Vector2(900f, 110f));
        cardName.color = Color.white;
        AddOutline(cardName, Color.Lerp(teamColor, Color.black, 0.55f), 4f);

        // 「WIN!」とチーム名
        win = NewRect("Win", root);
        win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
        win.anchoredPosition = new Vector2(0f, 330f);
        win.sizeDelta = new Vector2(1400f, 300f);
        win.localRotation = Quaternion.Euler(0f, 0f, 5f);
        winText = NewText("WinText", win, font, 230, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
        Stretch(winText.rectTransform);
        winText.text = "WIN!";
        winText.fontStyle = FontStyle.BoldAndItalic;
        AddOutline(winText, Color.Lerp(teamColor, Color.black, 0.2f), 10f);
        AddOutline(winText, Color.Lerp(teamColor, Color.black, 0.65f), 16f);

        sub = NewRect("TeamName", root);
        sub.anchorMin = sub.anchorMax = new Vector2(0.5f, 0.5f);
        sub.sizeDelta = new Vector2(1400f, 110f);
        subText = NewText("TeamNameText", sub, font, 80, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
        Stretch(subText.rectTransform);
        subText.text = $"{SpaceJunkTeams.TeamName(winnerTeam)}の勝利！";
        AddOutline(subText, Color.Lerp(teamColor, Color.black, 0.6f), 6f);

        BuildConfetti(root);

        flash = NewRect("Flash", root).gameObject.AddComponent<Image>();
        Stretch(flash.rectTransform);
        flash.raycastTarget = false;
    }

    private void BuildConfetti(Transform root)
    {
        RectTransform layer = NewRect("Confetti", root);
        layer.anchorMin = layer.anchorMax = new Vector2(0.5f, 0.5f);
        Color[] palette = { teamColor, Color.white, Gold, Color.Lerp(teamColor, Color.white, 0.5f) };
        System.Random random = new System.Random(20261125);

        for (int i = 0; i < ConfettiCount; i++)
        {
            RectTransform piece = NewRect("Piece", layer);
            piece.sizeDelta = new Vector2(Range(random, 12f, 20f), Range(random, 22f, 34f));
            Image image = piece.gameObject.AddComponent<Image>();
            image.color = palette[i % palette.Length];
            image.raycastTarget = false;
            confetti[i] = piece;
            confettiParams[i] = new Vector4(Range(random, -1000f, 1000f), Range(random, 0f, 1.4f),
                Range(random, 380f, 720f), Range(random, -420f, 420f));
            confettiSway[i] = new Vector2(Range(random, 15f, 60f), Range(random, 1.5f, 3.5f));
        }
    }

    // ------------------------------------------------------------
    // 小さな道具
    // ------------------------------------------------------------

    /// <summary>再生していないとき（確認用に撮るとき）は Destroy が使えないので、すぐ消す。</summary>
    private static void DestroySafe(UnityEngine.Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static RectTransform NewRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.layer = 5;
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Text NewText(string objectName, Transform parent, Font font, int size, TextAnchor anchor,
        Vector2 position, Vector2 boxSize)
    {
        RectTransform rect = NewRect(objectName, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = Color.white;
        return text;
    }

    private static void AddOutline(Text text, Color color, float distance)
    {
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
        {
            SetLayer(child, layer);
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);
    private static float EaseInCubic(float x) => Mathf.Pow(Mathf.Clamp01(x), 3f);
    private static float EaseInOut(float x) => x * x * (3f - 2f * x);

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float k = Mathf.Clamp01(x) - 1f;
        return 1f + c3 * k * k * k + c1 * k * k;
    }
}
