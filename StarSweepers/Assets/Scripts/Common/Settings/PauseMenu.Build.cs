using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>PauseMenu の「画面を組み立てる（メイン・設定のページ）」（PauseMenu.cs から分けた部分）。</summary>
public partial class PauseMenu
{
    // ------------------------------------------------------------
    // 画面を組み立てる
    // ------------------------------------------------------------

    /// <summary>
    /// 画面をまるごと作る（<see cref="Awake"/> で1回だけ）。
    /// Canvas → 暗くする板 → 最初の画面・設定の画面、の順に重ねる（あとに作った物ほど手前に出る）。
    /// </summary>
    private void Build()
    {
        EnsureEventSystem();

        // ----- 一番外側（画面いっぱいに広がる） -----
        root = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // ほかのUIより手前に出す
        canvas.sortingOrder = 100;

        // 1920x1080 を基準に、画面の大きさに合わせて拡大・縮小する（幅と高さを半分ずつ見る）。
        // このファイルの位置・大きさの数字は、すべて 1920x1080 のときのピクセル
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ----- 後ろを暗くする板（クリックが後ろへ抜けるのも防ぐ） -----
        GameObject backdrop = CreateImage("Backdrop", root.transform, backdropColor);
        Stretch(backdrop.GetComponent<RectTransform>());

        mainPage = CreatePage("MainPage", "ポーズ");
        settingsPage = CreatePage("SettingsPage", "設定");

        BuildMainPage();
        BuildSettingsPage();
    }

    /// <summary>1ページ分の入れ物を作る（見出し付き）。</summary>
    private GameObject CreatePage(string name, string title)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(root.transform, false);
        Stretch(page.GetComponent<RectTransform>());

        GameObject titleObject = CreateText("Title", page.transform, title, fontSize + 12);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = new Vector2(ItemWidth, ItemHeight);
        titleRect.anchoredPosition = new Vector2(0f, 190f);

        return page;
    }

    /// <summary>
    /// 最初の画面の項目を、上から順に並べる。y は画面の真ん中からの高さ（上が＋）で、
    /// 1つ置くごとに「項目の高さ＋すき間」だけ下げる（出ない項目があっても、すき間が空かない）。
    /// </summary>
    private void BuildMainPage()
    {
        float y = 90f;
        CreateMenuItem(mainPage.transform, "ゲームをつづける", y, Close);

        y -= ItemHeight + ItemGap;
        CreateMenuItem(mainPage.transform, "設定", y, () => ShowSettings(true));

        // 試合中だけ「ロビーに戻る」を出す（宇宙ごみ。釣りなどには出ない）
        if (IsInMatch())
        {
            y -= ItemHeight + ItemGap;
            CreateMenuItem(mainPage.transform, "ロビーに戻る", y, ConfirmReturnToLobby);
        }

        // つながっている間は「タイトルにもどる」を出す（ロビーでも、ホスト以外でも出る。2026/10/6）
        if (CanGoToTitle())
        {
            y -= ItemHeight + ItemGap;
            CreateMenuItem(mainPage.transform, "タイトルにもどる", y, ConfirmReturnToTitle);
        }

        y -= ItemHeight + ItemGap;
        CreateMenuItem(mainPage.transform, "ゲームをやめる", y, ConfirmQuit);
    }

    /// <summary>最初の画面を作り直す（「ロビーに戻る」を出すかどうかを、いまの状況に合わせる）。</summary>
    private void RebuildMainPage()
    {
        if (mainPage != null)
        {
            // Destroy はフレームの終わりに消えるので、先に隠して、新しい画面と重ならないようにする
            mainPage.SetActive(false);
            Destroy(mainPage);
        }

        mainPage = CreatePage("MainPage", "ポーズ");
        BuildMainPage();
    }

    /// <summary>
    /// 設定の画面の項目を並べる。y は画面の真ん中からの高さ（上が＋）。
    /// 1段目（y＝90）から「項目の高さ＋すき間」（88）の何段ぶん下か（× 2f、× 3f …）で置いている。
    /// ただし2本目のスライダーだけは、1段目から 96 下げた位置に直接置いている。
    /// 項目を足すときは、下の「もどる」の段の数（× 5f）も1つ増やす。
    /// </summary>
    private void BuildSettingsPage()
    {
        CreateSlider(settingsPage.transform, "音量", 90f, GameSettings.Volume, 0f, 1f,
            value => GameSettings.Volume = value);

        CreateSlider(settingsPage.transform, "マウス感度", 90f - 96f, GameSettings.MouseSensitivity, 0.2f, 3f,
            value => GameSettings.MouseSensitivity = value);

        CreateMenuItem(settingsPage.transform, ControllerOperationLabel(), 90f - (ItemHeight + ItemGap) * 2f,
            ToggleControllerOperation);

        CreateMenuItem(settingsPage.transform, AimReticleLabel(), 90f - (ItemHeight + ItemGap) * 3f,
            ToggleAimReticle);

        CreateMenuItem(settingsPage.transform, ThrowOperationLabel(), 90f - (ItemHeight + ItemGap) * 4f,
            ToggleThrowOperation);

        CreateMenuItem(settingsPage.transform, "もどる", 90f - (ItemHeight + ItemGap) * 5f,
            () => ShowSettings(false));
    }

    /// <summary>操作タイプの項目に出す文字（いまの設定を見せる）。</summary>
    private static string ControllerOperationLabel()
    {
        return GameSettings.ControllerOperation == ControllerOperationType.TypeB
            ? "操作タイプ：タイプB"
            : "操作タイプ：タイプA";
    }

    /// <summary>操作タイプを A ⇔ B に切り替えて保存し、項目の文字を新しくする。</summary>
    private void ToggleControllerOperation()
    {
        GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
            ? ControllerOperationType.TypeB
            : ControllerOperationType.TypeA;
        BuildSettingsPageRefresh();
    }

    /// <summary>レティクルの項目に出す文字（いまの設定を見せる）。</summary>
    private static string AimReticleLabel()
    {
        return GameSettings.AimReticle == AimReticleType.TypeB
            ? "レティクル：タイプB"
            : "レティクル：タイプA";
    }

    /// <summary>レティクルを A ⇔ B に切り替えて保存し、項目の文字を新しくする。</summary>
    private void ToggleAimReticle()
    {
        GameSettings.AimReticle = GameSettings.AimReticle == AimReticleType.TypeA
            ? AimReticleType.TypeB
            : AimReticleType.TypeA;
        BuildSettingsPageRefresh();
    }

    /// <summary>投げ方の項目に出す文字（いまの設定を見せる）。</summary>
    private static string ThrowOperationLabel()
    {
        return GameSettings.ThrowOperation == ThrowOperationType.OneButton
            ? "投げ方：ZRワンボタン"
            : "投げ方：左右で使い分け";
    }

    // 投げ方の切り替え（宇宙ごみ式のプレイヤーだけに効く。2026/10/15）
    private void ToggleThrowOperation()
    {
        GameSettings.ThrowOperation = GameSettings.ThrowOperation == ThrowOperationType.TwoButtons
            ? ThrowOperationType.OneButton
            : ThrowOperationType.TwoButtons;
        BuildSettingsPageRefresh();
    }

    // 表示中の設定ページを作り直し、切り替えた値をすぐに見せる。
    private void BuildSettingsPageRefresh()
    {
        // Destroy はフレームの終わりに消えるので、先に隠して、新しい画面と重ならないようにする（RebuildMainPage と同じ）
        settingsPage.SetActive(false);
        Destroy(settingsPage);
        settingsPage = CreatePage("SettingsPage", "設定");
        BuildSettingsPage();
        settingsPage.SetActive(true);

        // 作り直したので、コントローラーの選択は一番上（音量）に戻る
        SelectFirstForGamepad(settingsPage);
    }
}
