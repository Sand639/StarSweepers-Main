using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>タイトル画面の部品（印の付いた物）を探して、文字や見た目を書き換える補助。</summary>
public class TitleParts
{
    private readonly Dictionary<TitlePartRole, TitlePart> parts = new Dictionary<TitlePartRole, TitlePart>();

    /// <summary>画面のボタン全部（一覧のカードの見本の中は除く）。</summary>
    private readonly List<TitleMenuButton> buttons = new List<TitleMenuButton>();

    /// <summary>root の下の印を集める（同じ Role が2つあれば、先に見つかったほうを使う）。</summary>
    public TitleParts(GameObject root)
    {
        foreach (TitlePart part in root.GetComponentsInChildren<TitlePart>(true))
        {
            if (part.Role != TitlePartRole.None && !parts.ContainsKey(part.Role))
            {
                parts[part.Role] = part;
            }
        }
    }

    /// <summary>「選ばれている」見た目を付け外しする対象にボタンを足す。</summary>
    public void AddButton(TitleMenuButton button)
    {
        buttons.Add(button);
    }

    /// <summary>その印の付いた部品。無ければ null。</summary>
    public GameObject Object(TitlePartRole role)
    {
        return parts.TryGetValue(role, out TitlePart part) && part != null ? part.gameObject : null;
    }

    public T Component<T>(TitlePartRole role) where T : Component
    {
        GameObject target = Object(role);
        return target != null ? target.GetComponent<T>() : null;
    }

    public Text Text(TitlePartRole role)
    {
        return Component<Text>(role);
    }

    /// <summary>その印の付いた文字を書き換える（部品が無ければ何もしない）。</summary>
    public void SetText(TitlePartRole role, string value)
    {
        Text text = Text(role);
        if (text != null)
        {
            text.text = value;
        }
    }

    /// <summary>その印の付いた部品を出し入れする（部品が無ければ何もしない）。</summary>
    public void SetActive(TitlePartRole role, bool active)
    {
        GameObject target = Object(role);
        if (target != null)
        {
            target.SetActive(active);
        }
    }

    /// <summary>その Action のボタン全部に「選ばれている」見た目を付け外しする。</summary>
    public void SetSelectedLook(TitleButtonAction action, bool selected)
    {
        foreach (TitleMenuButton look in buttons)
        {
            if (look != null && look.Action == action)
            {
                look.SetSelectedLook(selected);
            }
        }
    }

    /// <summary>チェックの印（■／□）。</summary>
    public static string CheckMark(bool on)
    {
        return on ? "■" : "□";
    }

    /// <summary>コントローラーがあれば、その項目を選ぶ。</summary>
    public static void SelectForGamepad(GameObject target)
    {
        if (Gamepad.current != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
        }
    }

    /// <summary>コントローラーがあれば、その画面の最初の項目を選ぶ（入力欄はほかに無いときだけ）。</summary>
    public static void SelectFirstForGamepad(GameObject page)
    {
        if (Gamepad.current == null || page == null || EventSystem.current == null)
        {
            return;
        }

        Selectable first = null;
        foreach (Selectable selectable in page.GetComponentsInChildren<Selectable>())
        {
            if (!(selectable is InputField))
            {
                first = selectable;
                break;
            }

            first = first != null ? first : selectable;
        }

        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }
}
