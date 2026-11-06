using UnityEngine;

/// <summary>タイトル画面の「参加の確認の窓」（参加しますか？／パスワードを入力してください）。</summary>
public class TitleJoinDialog
{
    private readonly TitleParts parts;
    private readonly TitleInputFields inputs;

    /// <summary>「はい」「参加する」で呼ぶ処理（パスワードを渡す）。</summary>
    private System.Action<string> pendingJoin;

    private bool needsPassword;

    /// <summary>窓から入ろうとして、結果を待っているか（失敗したら窓に理由を出す）。</summary>
    private bool waiting;

    /// <summary>パスワードがあるか入ってみるまで分からない部屋か（番号で入る部屋）。</summary>
    private bool passwordUnknown;

    private string roomName = string.Empty;

    public TitleJoinDialog(TitleParts parts, TitleInputFields inputs)
    {
        this.parts = parts;
        this.inputs = inputs;
        parts.SetActive(TitlePartRole.JoinDialog, false);
    }

    public bool IsOpen
    {
        get
        {
            GameObject dialog = parts.Object(TitlePartRole.JoinDialog);
            return dialog != null && dialog.activeSelf;
        }
    }

    /// <summary>窓を出す（パスワードのある部屋なら、パスワードを聞く形）。</summary>
    public void Open(string name, bool withPassword, System.Action<string> join, bool unknownPassword = false)
    {
        GameObject dialog = parts.Object(TitlePartRole.JoinDialog);
        if (dialog == null)
        {
            return;
        }

        pendingJoin = join;
        roomName = name;
        passwordUnknown = unknownPassword;
        waiting = false;

        SetMode(withPassword);
        parts.SetText(TitlePartRole.JoinDialogMessage, string.Empty);

        dialog.SetActive(true);

        foreach (TitleMenuButton look in dialog.GetComponentsInChildren<TitleMenuButton>(true))
        {
            if (look.Action == TitleButtonAction.ConfirmJoinDialog)
            {
                TitleParts.SelectForGamepad(look.gameObject);
                break;
            }
        }
    }

    /// <summary>「はい」「参加する」。部屋に入りに行く（結果は <see cref="TryShowFailure"/> が窓に出す）。</summary>
    public void Confirm()
    {
        if (pendingJoin == null)
        {
            Close();
            return;
        }

        string password = needsPassword ? inputs.JoinPassword.Trim() : string.Empty;
        if (needsPassword && password.Length == 0)
        {
            parts.SetText(TitlePartRole.JoinDialogMessage, "パスワードを入力してください");
            return;
        }

        InternetConnection internet = TitleConnection.Internet();
        if (internet != null && internet.IsBusy)
        {
            parts.SetText(TitlePartRole.JoinDialogMessage, "サーバーの一覧を取っています。少し待ってから、もう一度押してください");
            return;
        }

        parts.SetText(TitlePartRole.JoinDialogMessage, string.Empty);
        waiting = true;
        pendingJoin(password);
    }

    /// <summary>「いいえ」「やめる」。窓を閉じる。</summary>
    public void Close()
    {
        pendingJoin = null;
        waiting = false;
        parts.SetActive(TitlePartRole.JoinDialog, false);
    }

    /// <summary>窓から入って失敗していたら、窓の中に理由を出して true を返す。</summary>
    public bool TryShowFailure(InternetConnection internet)
    {
        if (internet == null || internet.State != InternetConnection.Phase.Failed || !waiting)
        {
            return false;
        }

        waiting = false;
        if (internet.LastJoinFailedByPassword && passwordUnknown && !needsPassword)
        {
            SetMode(true);
            parts.SetText(TitlePartRole.JoinDialogMessage, "この部屋にはパスワードがかかっています");
        }
        else
        {
            parts.SetText(TitlePartRole.JoinDialogMessage, internet.LastJoinFailedByPassword
                ? "パスワードが間違えています"
                : internet.Message);
        }

        internet.ClearFailure();
        return true;
    }

    /// <summary>窓を、パスワードを聞く形（true）か「参加しますか？」の形（false）にする。</summary>
    private void SetMode(bool withPassword)
    {
        needsPassword = withPassword;

        parts.SetText(TitlePartRole.JoinDialogTitle, withPassword
            ? $"「{roomName}」\nパスワードを入力してください"
            : $"「{roomName}」に参加しますか？");
        parts.SetText(TitlePartRole.JoinDialogYesLabel, withPassword ? "参加する" : "はい");
        parts.SetText(TitlePartRole.JoinDialogNoLabel, withPassword ? "やめる" : "いいえ");
        parts.SetActive(TitlePartRole.JoinDialogPasswordRow, withPassword);

        GameObject dialog = parts.Object(TitlePartRole.JoinDialog);
        if (dialog != null)
        {
            inputs.Refresh(dialog);
        }
    }
}
