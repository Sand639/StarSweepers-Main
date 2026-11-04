using UnityEngine;

/// <summary>
/// **画面に日本語を出すための文字（フォント）を用意する。**
///
/// Unityに最初から入っている文字は**日本語を持っていない。**
/// そのまま使うと、文字が全部**四角（□□□）**になる。
///
/// そこで**パソコンに入っている日本語フォントを借りてくる。**
/// ただしこれは**保存できない**（プレハブやシーンに残らない）ので、
/// **再生したときに毎回ここで用意する**必要がある。
///
/// > 日本語を持つTextMeshProのフォントを作れば、この手当ては要らなくなる。
/// > 見た目を凝りたくなったら、そのとき作り直すのがよい。
/// </summary>
public static class UiFont
{
    /// <summary>探しに行く日本語フォントの名前。上から順に、入っているものを使う。</summary>
    public static readonly string[] DefaultNames =
    {
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic", "Noto Sans CJK JP",
    };

    /// <summary>
    /// 日本語が出せる文字を用意する。
    /// 見つからなければ、Unityの標準の文字を返す（日本語は四角になる）。
    /// </summary>
    /// <param name="size">使う文字の大きさ。ここで指定した大きさを基準に作られる</param>
    /// <param name="names">探したい名前。省略すると <see cref="DefaultNames"/> を使う</param>
    public static Font Find(int size, string[] names = null)
    {
        string[] wanted = names != null && names.Length > 0 ? names : DefaultNames;

        Font found = Font.CreateDynamicFontFromOSFont(wanted, size);

        if (found != null)
        {
            return found;
        }

        Debug.LogWarning("[UI] 日本語のフォントが見つかりませんでした。文字が四角になるかもしれません。");

        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
