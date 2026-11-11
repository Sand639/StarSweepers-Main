using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// **シーンを切り替えるときの演出（トランジション）。** 画面を覆う → シーンを切り替える → 次のシーンで画面を開く、を行う。
///
/// どのシーンにも置かなくてよい。ゲームが始まると自動で1つ作られ、シーンをまたいで残る（設定は `Resources/SceneTransition.prefab`）。
/// 呼び方・流れ・種類の増やし方は `Documents/スクリプトの説明/Common/UI/SceneTransition.md`。
/// </summary>
public class SceneTransition : MonoBehaviour
{
    /// <summary>設定を入れたプレハブ（Resources フォルダ）。</summary>
    public const string PrefabResourcePath = "SceneTransition";

    /// <summary>ホストから参加者へ「画面を覆え」と知らせる通信のメッセージの名前。</summary>
    private const string CoverMessageName = "StarSweepers.SceneTransition.Cover";

    /// <summary>読み込みで1フレームが長くなっても、演出が一気に進まないように抑える1フレームの長さ。</summary>
    private const float MaxStepSeconds = 1f / 30f;

    /// <summary>いま動いている係。</summary>
    public static SceneTransition Current { get; private set; }

    [Header("演出の種類")]
    [Tooltip("シーンを切り替えるときに出す演出。種類の増やし方は SceneTransitionEffect.cs の一番上")]
    [SerializeField] private SceneTransitionType transitionType = SceneTransitionType.Fade;

    [Header("時間（秒）")]
    [Tooltip("画面を覆いきるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float coverSeconds = 0.5f;

    [Tooltip("次のシーンが読み込まれてから、覆ったまま待つ秒数（読み込んだ直後のカクつきを隠す）")]
    [Min(0f)]
    [SerializeField] private float holdSeconds = 0.2f;

    [Tooltip("画面が開ききるまでの秒数")]
    [Min(0f)]
    [SerializeField] private float revealSeconds = 0.5f;

    [Tooltip("次のシーンが読み込まれないとき、覆ったまま待つ最長の秒数。過ぎたら開く（真っ暗のまま止まらないように）")]
    [Min(1f)]
    [SerializeField] private float maxWaitSeconds = 15f;

    [Header("見た目")]
    [Tooltip("画面を覆う色")]
    [SerializeField] private Color color = Color.black;

    private GameObject canvasObject;
    private RectTransform effectRoot;
    private bool playing;
    private bool sceneLoadedSinceCover;
    private CustomMessagingManager registeredMessaging;

    // ------------------------------------------------------------
    // 呼び出す関数
    // ------------------------------------------------------------

    /// <summary>自分の画面だけで演出を出し、覆いきったら onCovered を呼ぶ（その中でシーンを切り替える）。演出中なら false。</summary>
    public static bool Play(Action onCovered, SceneTransitionType? type = null)
    {
        SceneTransition transition = GetOrCreate();
        return transition.Begin(type ?? transition.transitionType, onCovered);
    }

    /// <summary>ホストが全員のシーンを切り替えるとき用。全員の画面で同じ演出を出し、ホストの画面を覆いきったら onCovered を呼ぶ。</summary>
    public static bool PlayForEveryone(Action onCovered, SceneTransitionType? type = null)
    {
        SceneTransition transition = GetOrCreate();
        SceneTransitionType chosen = type ?? transition.transitionType;

        if (!transition.Begin(chosen, onCovered))
        {
            return false;
        }

        transition.SendCoverToClients(chosen);
        return true;
    }

    // ------------------------------------------------------------
    // 自動で作る
    // ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStart()
    {
        GetOrCreate();
    }

    /// <summary>係を返す。まだ無ければ、Resources のプレハブから作る（プレハブが無ければ初期値で作る）。</summary>
    private static SceneTransition GetOrCreate()
    {
        if (Current != null)
        {
            return Current;
        }

        GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
        GameObject created = prefab != null ? Instantiate(prefab) : new GameObject("SceneTransition", typeof(SceneTransition));
        created.name = "SceneTransition";
        return Current;
    }

    private void Awake()
    {
        // 2つ目は使わない（どこかのシーンに置かれていた場合など）
        if (Current != null && Current != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        Current = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unregister();
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>ほかのどの画面よりも手前に描く画面を作る（演出していない間は隠しておく）。</summary>
    private void BuildCanvas()
    {
        canvasObject = new GameObject("TransitionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1; // 画面のポインター（UiPointer）だけは、さらに手前に出す

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        effectRoot = (RectTransform)canvasObject.transform;
        canvasObject.SetActive(false);
    }

    // ------------------------------------------------------------
    // 演出
    // ------------------------------------------------------------

    /// <summary>演出を始める。演出中なら何もせず false。</summary>
    private bool Begin(SceneTransitionType type, Action onCovered)
    {
        if (playing)
        {
            Debug.LogWarning("[TRANSITION] 演出の途中なので、新しい演出は出しません。");
            return false;
        }

        playing = true;
        StartCoroutine(Run(SceneTransitionEffect.Create(type), onCovered));
        return true;
    }

    /// <summary>覆う → onCovered → 次のシーンが読み込まれるまで待つ → 開く。</summary>
    private IEnumerator Run(SceneTransitionEffect effect, Action onCovered)
    {
        sceneLoadedSinceCover = false;
        ClearEffect();
        effect.Build(effectRoot, color);
        effect.Apply(0f, false);
        canvasObject.SetActive(true);

        yield return Animate(effect, coverSeconds, false);

        if (onCovered != null)
        {
            try
            {
                onCovered();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        float waitStartedAt = Time.unscaledTime;
        while (!sceneLoadedSinceCover)
        {
            if (Time.unscaledTime - waitStartedAt >= maxWaitSeconds)
            {
                Debug.LogWarning($"[TRANSITION] 画面を覆ってから {maxWaitSeconds} 秒たっても次のシーンが読み込まれないので、画面を開きます。");
                break;
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(holdSeconds);
        yield return Animate(effect, revealSeconds, true);

        canvasObject.SetActive(false);
        ClearEffect();
        playing = false;
    }

    /// <summary>覆い具合を、時間に合わせて 0→1（開くときは 1→0）に動かす。</summary>
    private static IEnumerator Animate(SceneTransitionEffect effect, float seconds, bool revealing)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            float progress = elapsed / seconds;
            effect.Apply(Mathf.SmoothStep(0f, 1f, revealing ? 1f - progress : progress), revealing);
            yield return null;
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
        }

        effect.Apply(revealing ? 0f : 1f, revealing);
    }

    /// <summary>前の演出の部品を消す。</summary>
    private void ClearEffect()
    {
        for (int i = effectRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(effectRoot.GetChild(i).gameObject);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            sceneLoadedSinceCover = true;
        }
    }

    // ------------------------------------------------------------
    // 通信（ホスト → 参加者）
    // ------------------------------------------------------------

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        CustomMessagingManager messaging = manager != null && manager.IsListening ? manager.CustomMessagingManager : null;

        // つなぎ直すと通信の係が別物になるので、変わったら受け口を作り直す
        if (messaging != registeredMessaging)
        {
            Unregister();

            if (messaging != null)
            {
                messaging.RegisterNamedMessageHandler(CoverMessageName, OnCoverReceived);
                registeredMessaging = messaging;
            }
        }
    }

    private void Unregister()
    {
        if (registeredMessaging == null)
        {
            return;
        }

        // 通信を止めたあとは、係の中身がもう片付いていることがある
        try
        {
            registeredMessaging.UnregisterNamedMessageHandler(CoverMessageName);
        }
        catch (Exception)
        {
        }

        registeredMessaging = null;
    }

    /// <summary>（ホスト）ほかの参加者全員に「この種類で画面を覆え」と送る。</summary>
    private void SendCoverToClients(SceneTransitionType type)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer || manager.CustomMessagingManager == null)
        {
            return;
        }

        // 自分（ホスト）には送らない。送ると自分の画面でもう一度受け取ってしまう
        List<ulong> targets = new List<ulong>();
        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (clientId != manager.LocalClientId)
            {
                targets.Add(clientId);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        using (FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
        {
            writer.WriteValueSafe((int)type);
            manager.CustomMessagingManager.SendNamedMessage(CoverMessageName, targets, writer);
        }
    }

    /// <summary>（参加者）ホストから「画面を覆え」が届いた。</summary>
    private void OnCoverReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out int type);
        Begin((SceneTransitionType)type, null);
    }
}
