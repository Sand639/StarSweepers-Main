using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **訓練場などに置く「誰でも使える強制リスポーンボタン」。**
/// 近づいて E キーを押すと、ホストに通信（ServerRpc）を送って指定したスポナーの物を強制的に出し直す。
/// </summary>
[RequireComponent(typeof(NetworkObject))] // オンライン同期のために必須
public class SpaceJunkForceRespawnButton : NetworkBehaviour
{
    [Header("連携するスポナー")]
    [Tooltip("強制リスポーンさせたい SpaceJunkBombPoints スクリプトを指定します")]
    [SerializeField] private SpaceJunkBombPoints targetSpawner;

    [Header("操作")]
    [Tooltip("この距離まで近づくと使える（メートル）")]
    [SerializeField] private float interactRange = 3f;

    [Tooltip("押すキー")]
    [SerializeField] private Key interactKey = Key.E;

    [Tooltip("コントローラーのボタン")]
    [SerializeField] private GamepadButton interactButton = GamepadButton.South;

    [Header("見た目")]
    [Tooltip("誰かが近づいたときに色を変えるレンダラー（空でもよい）")]
    [SerializeField] private Renderer highlightRenderer;

    [Tooltip("近づいているときの色")]
    [SerializeField] private Color nearColor = new Color(0.4f, 1f, 0.6f); // 緑系の安心色

    [Tooltip("近づいたときの色とボタン画像の設定")]
    [SerializeField] private InteractPromptVisual interactVisual = new InteractPromptVisual();

    private bool isPlayerNearby;

    private void Awake()
    {
        interactVisual.Initialize(transform, highlightRenderer);
    }

    private void Update()
    {
        // ネットワークが動いていない時は何もしない
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            isPlayerNearby = false;
            interactVisual.SetNearby(false, nearColor);
            return;
        }

        // 自分のPCで操作しているプレイヤーとの距離を測る
        Transform localPlayer = FindLocalPlayer();
        isPlayerNearby = localPlayer != null && Vector3.Distance(localPlayer.position, transform.position) <= interactRange;

        // ポーズ中などは入力をブロック
        if (GamePause.BlocksInput)
        {
            return;
        }

        // 自分が近くにいて、キーを押したら実行
        bool pressed = WasPressed(interactKey) || GamepadInput.WasPressed(interactButton);
        if (isPlayerNearby && pressed)
        {
            // ホスト・ゲスト関係なく、サーバーに対して「リスポーン要求」を送る
            RequestRespawnServerRpc();
        }

        interactVisual.SetNearby(isPlayerNearby, nearColor);
    }

    private void LateUpdate() => interactVisual.LateUpdate();

    private void OnDisable() => interactVisual.SetNearby(false, nearColor);

    private void OnDestroy() => interactVisual.Cleanup();

    /// <summary>
    /// クライアントからサーバー（ホスト）へ「リスポーンして」と頼む通信。
    /// 実行自体は必ずホストのPC上で行われる。
    /// </summary>
    [ServerRpc(RequireOwnership = false)] // 所有権を持たないゲストからの呼び出しを許可
    private void RequestRespawnServerRpc()
    {
        if (targetSpawner != null)
        {
            Debug.Log($"[JUNK] プレイヤーからの要求により、サーバー上で {targetSpawner.name} を強制リスポーンします。");
            targetSpawner.ForceRespawnAll(); // ホストの権限で安全にアイテムを消去・生成
        }
    }

    private static bool WasPressed(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard[key].wasPressedThisFrame;
    }

    private static Transform FindLocalPlayer()
    {
        foreach (SweeperNetPlayer player in SweeperNetPlayer.All)
        {
            if (player != null && player.IsOwner && player.LocalSeat == 0)
            {
                return player.transform;
            }
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
