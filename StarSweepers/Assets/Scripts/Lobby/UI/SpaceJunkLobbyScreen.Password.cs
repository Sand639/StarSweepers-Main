using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>SpaceJunkLobbyScreen の「パスワード（ホストが変える）」（SpaceJunkLobbyScreen.cs から分けた部分）。</summary>
public partial class SpaceJunkLobbyScreen
{
    // ------------------------------------------------------------
    // パスワード（パスワードありの部屋だけ。ホストが変える。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>インターネット経由の部屋の情報（参加コード・パスワード）。NetworkManager に付いている。LAN なら null。</summary>
    private static InternetConnection Internet()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null ? manager.GetComponent<InternetConnection>() : null;
    }

    /// <summary>ゲーム設定を開いたとき：パスワードの欄に、いまのパスワードを入れておく。</summary>
    private void ResetPasswordSection()
    {
        InternetConnection internet = Internet();
        InputField field = PartComponent<InputField>(SpaceJunkLobbyPartRole.PasswordInput);

        if (field != null && internet != null)
        {
            field.SetTextWithoutNotify(internet.CurrentPassword);
        }

        SetText(SpaceJunkLobbyPartRole.PasswordMessage,
            $"いまのパスワードです。{InternetConnection.PasswordMinLength}〜{InternetConnection.PasswordMaxLength}文字。変えたら、入る人に伝えてください");
    }

    /// <summary>パスワードの欄は、パスワードを付けて作った部屋のときだけ出す。</summary>
    private void RefreshPasswordSection()
    {
        InternetConnection internet = Internet();
        SetActive(SpaceJunkLobbyPartRole.PasswordSection, internet != null && internet.HasPassword);
    }

    /// <summary>「変更する」を押したとき。</summary>
    private async void ChangePassword()
    {
        InternetConnection internet = Internet();
        InputField field = PartComponent<InputField>(SpaceJunkLobbyPartRole.PasswordInput);
        if (internet == null || field == null)
        {
            return;
        }

        string newPassword = field.text.Trim();
        SetText(SpaceJunkLobbyPartRole.PasswordMessage, "パスワードを変えています…");

        string problem = await internet.ChangePasswordAsync(newPassword);

        // 待っている間に画面が消えていたら（シーンが変わった など）、触らない
        if (this == null)
        {
            return;
        }

        SetText(SpaceJunkLobbyPartRole.PasswordMessage, problem ?? $"パスワードを「{newPassword}」に変えました。入る人に伝えてください");
        if (problem != null)
        {
            field.SetTextWithoutNotify(internet.CurrentPassword);
        }
    }
}
