using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

/// <summary>タイトル画面の「つないでいる途中の表示」（説明・失敗の理由・戻る／OK／やめる）。</summary>
public class TitleBusyOverlay
{
    private readonly TitleParts parts;
    private readonly TitleJoinDialog joinDialog;
    private readonly TitleRoomList roomList;
    private readonly TitleQuickPlay quickPlay;

    /// <summary>ホストに切られて戻ってきたときの理由。出し終わったら空にする。</summary>
    private string disconnectNotice;

    /// <summary>前に出した切られた理由（同じ理由を出し直さないため。シーンをまたいで覚えるので static）。</summary>
    private static string shownDisconnectReason = string.Empty;

    public TitleBusyOverlay(TitleParts parts, TitleJoinDialog joinDialog, TitleRoomList roomList, TitleQuickPlay quickPlay)
    {
        this.parts = parts;
        this.joinDialog = joinDialog;
        this.roomList = roomList;
        this.quickPlay = quickPlay;
        parts.SetActive(TitlePartRole.BusyOverlay, false);
    }

    /// <summary>表示が出ているか（出ている間は「戻る」を効かせない）。</summary>
    public bool IsShown
    {
        get
        {
            GameObject overlay = parts.Object(TitlePartRole.BusyOverlay);
            return overlay != null && overlay.activeSelf;
        }
    }

    /// <summary>ホストに切られて戻ってきたなら、その理由を出す用に取っておく。</summary>
    public void TakeDisconnectReason()
    {
        NetworkManager manager = NetworkManager.Singleton;
        string reason = manager != null ? manager.DisconnectReason : string.Empty;
        if (string.IsNullOrEmpty(reason) || reason == shownDisconnectReason)
        {
            return;
        }

        shownDisconnectReason = reason;

        // 「[」で始まるのは Netcode が自分で付ける記録（自分で止めたときにも付く）なので出さない
        if (reason.StartsWith("["))
        {
            return;
        }

        disconnectNotice = $"部屋に入れませんでした：{reason}";
    }

    /// <summary>表示を出し入れし、文字とボタンを今の様子に合わせる（毎フレーム呼ぶ）。</summary>
    public void Update()
    {
        GameObject overlay = parts.Object(TitlePartRole.BusyOverlay);
        if (overlay == null)
        {
            return;
        }

        Choose(TitleConnection.Internet(), NetworkManager.Singleton,
            out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action);
        Show(overlay, message, buttonLabel, action);
    }

    /// <summary>何を出すかを決める（上の条件ほど優先。出さないときは message が null）。</summary>
    private void Choose(InternetConnection internet, NetworkManager manager,
        out string message, out string buttonLabel, out UnityEngine.Events.UnityAction action)
    {
        // LAN でつなぎに行っている途中（インターネットで入る途中も同じ形になるので除く）
        bool lanConnecting = manager != null && manager.IsListening && !manager.IsServer && !manager.IsConnectedClient &&
                             (internet == null || internet.State != InternetConnection.Phase.Joining);

        message = null;
        buttonLabel = null;
        action = null;

        if (TitleConnection.IsConnected())
        {
            message = "つながりました。ロビーに移動しています…";
        }
        // 一覧を取っている間は出さない
        else if (internet != null && internet.IsBusy && !roomList.Searching)
        {
            message = string.IsNullOrEmpty(internet.Message) ? "つないでいます…" : internet.Message;
        }
        else if (quickPlay.Notice != null)
        {
            message = quickPlay.Notice;
        }
        else if (joinDialog.TryShowFailure(internet))
        {
            // 参加の確認の窓から入って失敗した。理由は窓の中に出したので、この表示は出さない
        }
        else if (internet != null && internet.State == InternetConnection.Phase.Failed)
        {
            message = internet.Message;
            buttonLabel = "戻る";
            action = () => internet.ClearFailure();
        }
        else if (!string.IsNullOrEmpty(disconnectNotice) && (manager == null || !manager.IsListening))
        {
            message = disconnectNotice;
            buttonLabel = "OK";
            action = () => disconnectNotice = null;
        }
        else if (lanConnecting)
        {
            string address = manager.NetworkConfig.NetworkTransport is UnityTransport transport ? transport.ConnectionData.Address : "?";
            message = $"LAN のサーバー（{address}）につないでいます…\nつながらないときは、IPアドレスと、同じネットワークにいるかを確かめてください。";
            buttonLabel = "やめる";
            action = () => manager.Shutdown();
        }
    }

    /// <summary>表示を出し入れし、文字とボタンを入れ替える。</summary>
    private void Show(GameObject overlay, string message, string buttonLabel, UnityEngine.Events.UnityAction action)
    {
        Button busyButton = parts.Component<Button>(TitlePartRole.BusyButton);

        bool show = message != null;
        if (overlay.activeSelf != show)
        {
            overlay.SetActive(show);

            // コントローラーでボタンを選ぶのは、出した瞬間だけ
            if (show && buttonLabel != null && busyButton != null)
            {
                TitleParts.SelectForGamepad(busyButton.gameObject);
            }
        }

        if (!show)
        {
            return;
        }

        parts.SetText(TitlePartRole.BusyMessage, message);

        if (busyButton != null)
        {
            busyButton.gameObject.SetActive(buttonLabel != null);
            busyButton.onClick.RemoveAllListeners();
            if (action != null)
            {
                busyButton.onClick.AddListener(action);
                parts.SetText(TitlePartRole.BusyButtonLabel, buttonLabel);
            }
        }
    }
}
