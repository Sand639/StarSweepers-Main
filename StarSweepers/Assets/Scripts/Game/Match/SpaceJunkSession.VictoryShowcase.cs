using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkSession の「試合に勝ったあとのメンバー紹介」（2026/11/25 試作）。演出そのものは <see cref="MatchVictoryShowcase"/>。</summary>
public partial class SpaceJunkSession
{
    private readonly HashSet<ulong> victoryViewersFinished = new HashSet<ulong>();
    private int victoryShowcaseId;
    private MatchVictoryShowcase localVictoryShowcase;

    /// <summary>
    /// **勝ったチームのメンバー紹介を全員に見せ、全員が見終わるまで待つ（ホストだけ）。**
    /// 最後のラウンドの結果表示のあと、ロビーへ戻る前に呼ぶ。待ちすぎたら（25秒）先へ進む。
    /// **ホストが Enter で飛ばしたら、ほかの人を待たずに先へ進む**（ほかの人は自分のPCで飛ばせば、その人だけ最後の画になる）。
    /// </summary>
    private IEnumerator PresentMatchVictory()
    {
        if (!IsServer || matchWinner.Value < 0)
        {
            yield break;
        }

        victoryShowcaseId++;
        victoryViewersFinished.Clear();
        ShowVictoryShowcaseClientRpc(victoryShowcaseId, matchWinner.Value);

        double waitUntil = Time.unscaledTimeAsDouble + 25;
        while (IsSpawned && IsServer && Time.unscaledTimeAsDouble < waitUntil)
        {
            bool allFinished = true;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!victoryViewersFinished.Contains(clientId))
                {
                    allFinished = false;
                    break;
                }
            }
            if (allFinished) yield break;
            if (localVictoryShowcase != null && localVictoryShowcase.WasSkipped) yield break;
            yield return null;
        }

        if (IsSpawned && IsServer)
        {
            Debug.LogWarning("[VICTORY] メンバー紹介の完了を待つ時間を超えたため、ロビーへ進みます。");
        }
    }

    [ClientRpc]
    private void ShowVictoryShowcaseClientRpc(int id, int winner)
    {
        // 演出はいまのマップのシーンに作るので、ロビーへ切り替わると一緒に消える
        localVictoryShowcase = MatchVictoryShowcase.PlayNew(winner, MatchVictoryShowcase.CollectTeam(winner), () =>
        {
            if (IsSpawned) VictoryShowcaseFinishedRpc(id);
        });
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void VictoryShowcaseFinishedRpc(int id, RpcParams rpcParams = default)
    {
        if (id == victoryShowcaseId)
        {
            victoryViewersFinished.Add(rpcParams.Receive.SenderClientId);
        }
    }
}
