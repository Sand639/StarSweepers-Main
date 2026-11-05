using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>SpaceJunkPlayerSetup の「ロビーで体を隠す・見せる」（SpaceJunkPlayerSetup.cs から分けた部分）。</summary>
public partial class SpaceJunkPlayerSetup
{
    private void SetLobbyRenderersVisible(bool visible)
    {
        if (visible)
        {
            // こちらで消した物だけを戻す（元から消えていた物は、そのままにする）
            foreach (Renderer hiddenRenderer in lobbyHiddenRenderers)
            {
                if (hiddenRenderer != null)
                {
                    hiddenRenderer.enabled = true;
                }
            }

            lobbyHiddenRenderers.Clear();
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer childRenderer in renderers)
        {
            if (childRenderer != null && childRenderer.enabled)
            {
                childRenderer.enabled = false;
                lobbyHiddenRenderers.Add(childRenderer);
            }
        }
    }

    private void RevealLobbyRenderers()
    {
        if (!lobbyVisualsHidden)
        {
            return;
        }

        SetLobbyRenderersVisible(true);
        lobbyVisualsHidden = false;
        lobbyHiddenSeconds = 0f;
    }

    /// <summary>他クライアントでは、同期された位置が正しいロビー位置に届いてから描画する。</summary>
    private void TryRevealAfterLobbyPositionSync()
    {
        bool inLobby = IsInLobbyScene();

        // 参加番号がまだ届いていなくても、時間切れ・ロビーの外なら見せる（隠しっぱなしにしない）
        if (netPlayer == null || !netPlayer.IsSpawned || netPlayer.PlayerIndex < 0)
        {
            if (!inLobby || lobbyHiddenSeconds >= LobbyRevealTimeout)
            {
                RevealLobbyRenderers();
            }

            return;
        }

        Vector3 expectedPosition = FallbackSpawnPosition(Vector2.zero);
        UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (SpaceJunkLobbyPoint point in SpaceJunkLobbyPoint.All)
        {
            if (point != null && point.gameObject.scene == activeScene && point.SpawnIndex == netPlayer.PlayerIndex)
            {
                expectedPosition = point.transform.position;
                break;
            }
        }

        if (ShouldRevealLobbyVisuals(inLobby, transform.position, expectedPosition, lobbyHiddenSeconds))
        {
            RevealLobbyRenderers();
        }
    }

    /// <summary>
    /// **ロビーで隠した体を、もう見せてよいか。**（2026/11/5 に条件を足した）
    ///
    /// 隠すのは、生まれてから番号付きの出現場所へ移されるまでの一瞬、ちがう場所に見えるのを防ぐため（2026/10/29）。
    /// 前は「出現場所から 10cm 以内（高さも含む）にいるのを見たとき」だけ見せていたが、出現場所は高さ 1m にあり、
    /// 体はすぐ床へ落ちる。あとから入った人から見ると、先にいた人はもう歩き出している。
    /// そのため**ほかの人の体が、ロビーでも試合中でもずっと見えなかった**。次のどれかで見せる。
    ///
    /// ・ロビーの外にいる（試合のステージへ移った）
    /// ・出現場所の真上・真下にいる（高さは見ない）
    /// ・隠してから <see cref="LobbyRevealTimeout"/> 秒たった（どこにいても）
    /// </summary>
    public static bool ShouldRevealLobbyVisuals(bool inLobby, Vector3 position, Vector3 expectedPosition, float hiddenSeconds)
    {
        if (!inLobby || hiddenSeconds >= LobbyRevealTimeout)
        {
            return true;
        }

        Vector2 offset = new Vector2(position.x - expectedPosition.x, position.z - expectedPosition.z);
        return offset.sqrMagnitude <= LobbyRevealDistance * LobbyRevealDistance;
    }
}
