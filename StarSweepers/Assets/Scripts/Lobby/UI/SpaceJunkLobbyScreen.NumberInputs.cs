using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>SpaceJunkLobbyScreen の「数字の入力欄」（SpaceJunkLobbyScreen.cs から分けた部分）。</summary>
public partial class SpaceJunkLobbyScreen
{
    // ------------------------------------------------------------
    // 数字の入力欄（めっちゃカメレオンと同じく、± のボタンのほかに打ち込める。2026/10/6）
    // ------------------------------------------------------------

    /// <summary>数字の行に入力欄があれば、打ち込める形に整えて、確定したときの反映をつなぐ。</summary>
    private void WireInputs()
    {
        foreach (SpaceJunkLobbyValueRow row in valueRows)
        {
            InputField input = row.Input;
            if (input == null)
            {
                continue;
            }

            // ラムダに渡す行を、この回のぶんとして固定しておく
            SpaceJunkLobbyValueRow target = row;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 3; // 3桁まで

            // **全角数字は半角に直し、数字以外は受け付けない**
            input.onValidateInput = (text, index, c) => ToHalfWidthDigit(c);

            // Enter か、入力欄から外れたときに反映する（1文字ごとに反映すると「120」の途中の「1」が一瞬入るため）
            input.onEndEdit.AddListener(value => ApplyTyped(target, value));
        }
    }

    /// <summary>半角数字はそのまま、全角数字（０〜９）は半角に直す。それ以外は受け付けない（'\0' を返す）。</summary>
    public static char ToHalfWidthDigit(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c;
        }

        if (c >= '０' && c <= '９')
        {
            return (char)('0' + (c - '０'));
        }

        return '\0';
    }

    /// <summary>文字列の中の全角数字を半角に直し、数字だけを残す（貼り付けなどで入ってきたとき用）。</summary>
    public static string ToHalfWidthDigits(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder digits = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            char converted = ToHalfWidthDigit(c);
            if (converted != '\0')
            {
                digits.Append(converted);
            }
        }

        return digits.ToString();
    }

    /// <summary>打ち込んだ数字を、範囲に収めてホストの設定に入れる。空・数字でなければ元の値に戻す。</summary>
    private void ApplyTyped(SpaceJunkLobbyValueRow row, string typed)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsServer)
        {
            return;
        }

        string digits = ToHalfWidthDigits(typed);
        if (int.TryParse(digits, out int value))
        {
            switch (row.Kind)
            {
                case SpaceJunkLobbyValueKind.TeamCount:
                    session.ServerSetTeamCount(Mathf.Clamp(value, 1, SpaceJunkTeams.MaxTeams));
                    break;
                case SpaceJunkLobbyValueKind.RoundsToWin:
                    session.ServerSetRoundsToWin(value);
                    break;
                case SpaceJunkLobbyValueKind.RoundSeconds:
                    session.ServerSetRoundSeconds(value);
                    break;
            }
        }

        // 範囲に収めた値・元の値を出し直す
        if (row.Input != null)
        {
            row.Input.SetTextWithoutNotify(ValueOf(session, row.Kind).ToString());
        }
    }

    /// <summary>その行が表す、ホストの設定のいまの値（最大時間は秒を四捨五入した整数）。</summary>
    private static int ValueOf(SpaceJunkSession session, SpaceJunkLobbyValueKind kind)
    {
        switch (kind)
        {
            case SpaceJunkLobbyValueKind.TeamCount:
                return session.TeamCount;
            case SpaceJunkLobbyValueKind.RoundsToWin:
                return session.RoundsToWin;
            default:
                return Mathf.RoundToInt(session.RoundSeconds);
        }
    }

    /// <summary>いま入力欄に打ち込んでいるか（数字の欄・パスワードの欄）。</summary>
    private bool IsInputFocused()
    {
        foreach (SpaceJunkLobbyValueRow row in valueRows)
        {
            if (row.Input != null && row.Input.isFocused)
            {
                return true;
            }
        }

        InputField password = PartComponent<InputField>(SpaceJunkLobbyPartRole.PasswordInput);
        return password != null && password.isFocused;
    }
}
