using UnityEngine;

/// <summary>
/// **画面に日本語を出すための文字（フォント）を用意する。**
///
/// Unityに最初から入っている文字は**日本語を持っていない。**
/// そのまま使うと、文字が全部**四角（□□□）**になる。
///
/// **2026/11/27 から、ゲームに入れたフォント HarmonyOS Sans SC Regular（`Assets/Resources/HarmonyOS_Sans_SC_Regular.ttf`）を使う**（大槻さん）。
/// それまでは仮に、パソコンに入っている日本語フォント（Yu Gothic UI など）を借りていた。
/// このフォントに無い文字（「▶」など）は、取り込みの設定（Font Names）に並べたパソコンのフォントで代わりに出る。
/// フォントのファイルが見つからないときだけ、前と同じようにパソコンのフォントを借りる。
///
/// ライセンス（HarmonyOS Sans Fonts License Agreement）の文書は `Assets/Resources/HarmonyOS_Sans_LICENSE.txt`。
/// **ゲームの中に「HarmonyOS Sans を使っている」と目立つように書くことが条件**になっている（Documents/使用素材とライセンス.md）。
/// </summary>
public static class UiFont
{
    /// <summary>ゲームに入れたフォントの名前（`Assets/Resources` の中）。</summary>
    public const string BundledFontName = "HarmonyOS_Sans_SC_Regular";

    /// <summary>読み込んだフォント（1回だけ読む。大きさは使う側の Text が決めるので、全部で同じものを使ってよい）。</summary>
    private static Font bundled;

    /// <summary>ゲームに入れたフォントが無いときに探しに行く、パソコンの日本語フォントの名前。上から順に、入っているものを使う。</summary>
    public static readonly string[] DefaultNames =
    {
        "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS UI Gothic", "Noto Sans CJK JP",
    };

    /// <summary>
    /// 日本語が出せる文字を用意する。ふだんはゲームに入れたフォント（<see cref="BundledFontName"/>）を返す。
    /// それが無ければパソコンのフォントを探し、それも無ければ Unity の標準の文字を返す（日本語は四角になる）。
    /// </summary>
    /// <param name="size">パソコンのフォントを借りるときの文字の大きさの基準</param>
    /// <param name="names">パソコンのフォントを借りるときに探す名前。省略すると <see cref="DefaultNames"/> を使う</param>
    public static Font Find(int size, string[] names = null)
    {
        if (bundled == null)
        {
            bundled = Resources.Load<Font>(BundledFontName);
        }

        if (bundled != null)
        {
            return bundled;
        }

        Debug.LogWarning($"[UI] フォント（Assets/Resources/{BundledFontName}.ttf）が見つからないので、パソコンの日本語フォントを使います。");

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
