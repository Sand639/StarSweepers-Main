using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>PauseMenu の「確かめる画面（ロビー・タイトルに戻る、ゲームをやめる）」（PauseMenu.cs から分けた部分）。</summary>
public partial class PauseMenu
{
    // ------------------------------------------------------------
    // 確かめる画面（ロビーに戻る・ゲームをやめる）
    // ------------------------------------------------------------

    /// <summary>
    /// **本当にいいか確かめる画面を出す。**
    /// 上の項目（<paramref name="cancelLabel"/>）は閉じて遊びに戻る、下の項目（<paramref name="okLabel"/>）で実行する。
    /// コントローラーでは**上の「つづける」側を選んだ状態**で開く（うっかり抜けないように）。
    /// </summary>
    private void ShowConfirm(string message, string note, string cancelLabel, string okLabel,
        UnityEngine.Events.UnityAction onOk)
    {
        CloseConfirm();

        mainPage.SetActive(false);
        settingsPage.SetActive(false);

        confirmPage = new GameObject("ConfirmPage", typeof(RectTransform));
        confirmPage.transform.SetParent(root.transform, false);
        Stretch(confirmPage.GetComponent<RectTransform>());

        GameObject messageObject = CreateText("Message", confirmPage.transform, message, fontSize + 4);
        PlaceText(messageObject, 150f, 56f);

        GameObject noteObject = CreateText("Note", confirmPage.transform, note, fontSize - 8);
        PlaceText(noteObject, 100f, 36f);

        CreateMenuItem(confirmPage.transform, cancelLabel, 10f, Close);
        CreateMenuItem(confirmPage.transform, okLabel, 10f - (ItemHeight + ItemGap), onOk);

        SelectFirstForGamepad(confirmPage);
    }

    /// <summary>確かめる画面を消す（出ていなければ何もしない）。</summary>
    private void CloseConfirm()
    {
        if (confirmPage != null)
        {
            Destroy(confirmPage);
            confirmPage = null;
        }
    }

    /// <summary>画面の真ん中の、高さ <paramref name="y"/> に横長の文字を置く。</summary>
    private static void PlaceText(GameObject textObject, float y, float height)
    {
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1200f, height);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    /// <summary>「ロビーに戻る」を押したとき。確かめてから戻る。</summary>
    private void ConfirmReturnToLobby()
    {
        ShowConfirm(
            "試合から抜けてロビーに戻ります",
            "※ホストの場合は全員がロビーに戻されます",
            "試合をつづける",
            "ロビーに戻る",
            () =>
            {
                // 止めた時間やカーソルを元に戻してから移る
                Close();
                ReturnToLobby?.Invoke();
            });
    }

    /// <summary>「ゲームをやめる」を押したとき。確かめてから終わる。</summary>
    private void ConfirmQuit()
    {
        ShowConfirm(
            IsInMatch() ? "試合から抜けてゲームを終了します" : "ゲームを終了します",
            "※ホストの場合は全員がタイトル画面に戻されます",
            "ゲームをつづける",
            "ゲームをやめる",
            QuitGame);
    }

    /// <summary>「タイトルにもどる」を押したとき。確かめてから戻る。</summary>
    private void ConfirmReturnToTitle()
    {
        ShowConfirm(
            IsInMatch() ? "試合から抜けてタイトル画面に戻ります" : "ロビーから抜けてタイトル画面に戻ります",
            "※ホストの場合は全員がタイトル画面に戻されます",
            "ゲームをつづける",
            "タイトルにもどる",
            () =>
            {
                // 止めた時間やカーソルを元に戻してから移る
                Close();
                ReturnToTitle?.Invoke();
            });
    }

    /// <summary>いま「タイトルにもどる」を出せる場面か。</summary>
    private static bool CanGoToTitle()
    {
        return CanReturnToTitle != null && ReturnToTitle != null && CanReturnToTitle();
    }

    /// <summary>いま「ロビーに戻る」を出せる場面（試合中）か。</summary>
    private static bool IsInMatch()
    {
        return CanReturnToLobby != null && ReturnToLobby != null && CanReturnToLobby();
    }

    /// <summary>
    /// **コントローラーがつながっていれば、その画面の最初の項目を選んでおく。**
    /// 何も選ばれていないと、十字キーやスティックを倒しても何も動かないため（2026/9/22）。
    /// 選んだ項目は A で決定できる。
    /// </summary>
    private void SelectFirstForGamepad(GameObject page)
    {
        if (Gamepad.current == null || page == null || !page.activeInHierarchy || EventSystem.current == null)
        {
            return;
        }

        Selectable first = page.GetComponentInChildren<Selectable>();
        EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
    }
}
