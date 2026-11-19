using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class SpaceJunkSession
{
    private RoundResultPlayback resultPlayback;
    private readonly HashSet<ulong> resultViewersFinished = new HashSet<ulong>();
    private int resultPresentationId;
    private bool roundResultActive;

    private IEnumerator PresentRoundResult(float fallbackSeconds)
    {
        // 3・4チームの既存ルールでは、従来の結果待ちを使う。
        if (TeamCount != 2)
        {
            yield return new WaitForSecondsRealtime(fallbackSeconds);
            yield break;
        }

        resultPresentationId++;
        resultViewersFinished.Clear();
        var style = (RoundWinnerResultUI.ResultStyle)Random.Range(0, 3);
        ShowRoundResultClientRpc(resultPresentationId, LastRoundWinner, RoundWinsOf(1), RoundWinsOf(0), RoundsToWin, (int)style);
        double waitUntil = Time.unscaledTimeAsDouble + 20;
        while (IsSpawned && IsServer && Time.unscaledTimeAsDouble < waitUntil)
        {
            bool allFinished = true;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!resultViewersFinished.Contains(clientId))
                {
                    allFinished = false;
                    break;
                }
            }
            if (allFinished) yield break;
            yield return null;
        }
        if (IsSpawned && IsServer)
        {
            Debug.LogWarning("[RESULT] 結果表示の完了を待つ時間を超えたため、次の場面へ進みます。");
        }
    }

    [ClientRpc]
    private void ShowRoundResultClientRpc(int id, int winner, int redWins, int blueWins, int winsNeeded, int style)
    {
        if (resultPlayback == null)
        {
            resultPlayback = gameObject.AddComponent<RoundResultPlayback>();
        }
        resultPlayback.Play(winner, redWins, blueWins, winsNeeded, (RoundWinnerResultUI.ResultStyle)style,
            () =>
            {
                if (IsSpawned) RoundResultFinishedRpc(id);
            });
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RoundResultFinishedRpc(int id, RpcParams rpcParams = default)
    {
        if (roundResultActive && id == resultPresentationId)
        {
            resultViewersFinished.Add(rpcParams.Receive.SenderClientId);
        }
    }

    [ClientRpc]
    private void HideRoundResultClientRpc()
    {
        if (resultPlayback != null) resultPlayback.HideResult();
    }
}
