using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>タイトル画面の入力欄（サーバーの名前・パスワード・部屋番号）の値を持ち、欄とつなぐ。</summary>
public class TitleInputFields
{
    private const int ServerNameMaxLength = 25;
    private const int RoomCodeMaxLength = 12;

    /// <summary>パスワードが長すぎるときの文字数表示の色。</summary>
    private static readonly Color CounterTooLongColor = new Color(1f, 0.45f, 0.4f, 1f);

    /// <summary>部屋を作る画面のサーバーの名前。</summary>
    public string ServerName { get; private set; } = string.Empty;

    /// <summary>作る部屋のパスワード（空ならなし）。</summary>
    public string CreatePassword { get; private set; } = string.Empty;

    /// <summary>部屋を探す画面の、部屋番号の検索欄の文字。</summary>
    public string RoomCode { get; private set; } = string.Empty;

    /// <summary>入る部屋のパスワード（参加の確認の窓で入れる）。</summary>
    public string JoinPassword { get; private set; } = string.Empty;

    /// <summary>文字数表示の、プレハブで決めた色（赤から戻すため）。</summary>
    private readonly Dictionary<Text, Color> counterColors = new Dictionary<Text, Color>();

    /// <summary>root の下の入力欄に今の値と文字数の上限を入れ、打ったら値を覚えるようにする。</summary>
    public TitleInputFields(GameObject root)
    {
        foreach (TitleInput input in root.GetComponentsInChildren<TitleInput>(true))
        {
            InputField field = input.Field;
            TitleInputKind kind = input.Kind;
            Text counter = input.Counter;

            field.characterLimit = InputLimitOf(kind);
            field.text = ValueOf(kind);

            field.onValueChanged.AddListener(value =>
            {
                SetValue(kind, value);
                UpdateCounter(kind, counter, value);
            });

            UpdateCounter(kind, counter, field.text);
        }
    }

    /// <summary>その画面の入力欄に、今の値を入れ直す。</summary>
    public void Refresh(GameObject page)
    {
        foreach (TitleInput input in page.GetComponentsInChildren<TitleInput>(true))
        {
            string value = ValueOf(input.Kind);
            if (input.Field.text != value)
            {
                input.Field.SetTextWithoutNotify(value);
                UpdateCounter(input.Kind, input.Counter, value);
            }
        }
    }

    private static bool IsPassword(TitleInputKind kind)
    {
        return kind == TitleInputKind.CreatePassword || kind == TitleInputKind.JoinPassword;
    }

    /// <summary>入力欄に打てる文字数（パスワードは長すぎを知らせるため、少し多めに打てる）。</summary>
    private static int InputLimitOf(TitleInputKind kind)
    {
        return IsPassword(kind) ? InternetConnection.PasswordInputLimit : MaxLengthOf(kind);
    }

    /// <summary>入力欄の最大の文字数。</summary>
    private static int MaxLengthOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                return ServerNameMaxLength;
            case TitleInputKind.PrivateCode:
                return RoomCodeMaxLength;
            case TitleInputKind.CreatePassword:
            case TitleInputKind.JoinPassword:
                return InternetConnection.PasswordMaxLength;
            default:
                return 0;
        }
    }

    private string ValueOf(TitleInputKind kind)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                return ServerName;
            case TitleInputKind.PrivateCode:
                return RoomCode;
            case TitleInputKind.CreatePassword:
                return CreatePassword;
            case TitleInputKind.JoinPassword:
                return JoinPassword;
            default:
                return string.Empty;
        }
    }

    private void SetValue(TitleInputKind kind, string value)
    {
        switch (kind)
        {
            case TitleInputKind.ServerName:
                ServerName = value;
                break;
            case TitleInputKind.PrivateCode:
                RoomCode = value;
                break;
            case TitleInputKind.CreatePassword:
                CreatePassword = value;
                break;
            case TitleInputKind.JoinPassword:
                JoinPassword = value;
                break;
        }
    }

    /// <summary>入力欄の右端の文字数表示を出し直す（パスワードが長すぎたら注意を赤で出す）。</summary>
    private void UpdateCounter(TitleInputKind kind, Text counter, string value)
    {
        if (counter == null)
        {
            return;
        }

        if (!counterColors.TryGetValue(counter, out Color normal))
        {
            normal = counter.color;
            counterColors[counter] = normal;
        }

        int max = MaxLengthOf(kind);
        bool tooLong = IsPassword(kind) && value.Length > max;

        counter.horizontalOverflow = HorizontalWrapMode.Overflow;
        counter.text = tooLong ? InternetConnection.PasswordTooLongMessage : $"{value.Length}/{max}";
        counter.color = tooLong ? CounterTooLongColor : normal;
    }
}
