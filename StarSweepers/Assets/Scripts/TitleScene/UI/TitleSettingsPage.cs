using System.Collections.Generic;
using UnityEngine;

/// <summary>タイトル画面の「設定」（サウンド・グラフィック・ゲーム全般のタブと、設定の行）。</summary>
public class TitleSettingsPage
{
    public enum Tab
    {
        Sound,
        Graphics,
        General,
    }

    private static readonly FullScreenMode[] ScreenModes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

    private readonly TitleParts parts;
    private readonly Dictionary<Tab, GameObject> groups = new Dictionary<Tab, GameObject>();
    private readonly List<TitleSettingRow> rows = new List<TitleSettingRow>();
    private Tab tab = Tab.Sound;

    /// <summary>選べる解像度（同じ大きさはまとめてある）。</summary>
    private readonly Resolution[] resolutions;

    // 選んでいる解像度と画面モード（「適用」を押すまで画面には効かせない）
    private int resolutionIndex;
    private FullScreenMode pendingScreenMode;

    /// <summary>root の下のタブと設定の行を集め、スライダーと「＜」「＞」に処理をつなぐ。</summary>
    public TitleSettingsPage(GameObject root, TitleParts parts)
    {
        this.parts = parts;
        resolutions = UniqueResolutions();

        AddGroup(Tab.Sound, TitlePartRole.SettingsSound);
        AddGroup(Tab.Graphics, TitlePartRole.SettingsGraphics);
        AddGroup(Tab.General, TitlePartRole.SettingsGeneral);

        foreach (TitleSettingRow row in root.GetComponentsInChildren<TitleSettingRow>(true))
        {
            rows.Add(row);
            TitleSettingRow target = row;

            if (row.Slider != null)
            {
                SetupSliderRange(row);
                row.Slider.onValueChanged.AddListener(value =>
                {
                    SetSliderValue(target.Kind, value);
                    RefreshRow(target);
                });
            }

            if (row.Previous != null)
            {
                row.Previous.onClick.AddListener(() =>
                {
                    Step(target.Kind, -1);
                    RefreshRow(target);
                });
            }

            if (row.Next != null)
            {
                row.Next.onClick.AddListener(() =>
                {
                    Step(target.Kind, 1);
                    RefreshRow(target);
                });
            }
        }
    }

    /// <summary>画面を開いたとき。前に開いていたタブを出す。</summary>
    public void Open()
    {
        ShowTab(tab);
    }

    /// <summary>タブを切り替え、全部の行を今の値で出し直す。</summary>
    public void ShowTab(Tab value)
    {
        tab = value;

        foreach (KeyValuePair<Tab, GameObject> pair in groups)
        {
            pair.Value.SetActive(pair.Key == value);
        }

        parts.SetSelectedLook(TitleButtonAction.TabSound, value == Tab.Sound);
        parts.SetSelectedLook(TitleButtonAction.TabGraphics, value == Tab.Graphics);
        parts.SetSelectedLook(TitleButtonAction.TabGeneral, value == Tab.General);

        // まだ「適用」していない解像度・画面モードの選択は、今の画面の値に戻る
        resolutionIndex = Mathf.Max(0, FindCurrentResolution(resolutions));
        pendingScreenMode = Screen.fullScreenMode;

        foreach (TitleSettingRow row in rows)
        {
            RefreshRow(row);
        }
    }

    /// <summary>「解像度／スクリーンモードを適用」。選んだ解像度と画面モードを画面に効かせる。</summary>
    public void ApplyScreen()
    {
        if (resolutions.Length > 0)
        {
            Resolution chosen = resolutions[resolutionIndex];
            Screen.SetResolution(chosen.width, chosen.height, pendingScreenMode);
        }
        else
        {
            Screen.fullScreenMode = pendingScreenMode;
        }
    }

    private void AddGroup(Tab value, TitlePartRole role)
    {
        GameObject target = parts.Object(role);
        if (target != null)
        {
            groups[value] = target;
        }
    }

    /// <summary>スライダーの端の値を決める（プレハブの値を上書きする）。</summary>
    private static void SetupSliderRange(TitleSettingRow row)
    {
        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                row.Slider.minValue = 0f;
                row.Slider.maxValue = 1f;
                break;
            case TitleSettingKind.MouseSensitivity:
                row.Slider.minValue = 0.2f;
                row.Slider.maxValue = 3f;
                break;
            case TitleSettingKind.FrameRateLimit:
                row.Slider.minValue = 30f;
                row.Slider.maxValue = 240f;
                break;
        }
    }

    /// <summary>スライダーを動かした値を GameSettings に入れる（すぐ効く）。</summary>
    private static void SetSliderValue(TitleSettingKind kind, float value)
    {
        switch (kind)
        {
            case TitleSettingKind.Volume:
                GameSettings.Volume = value;
                break;
            case TitleSettingKind.MouseSensitivity:
                GameSettings.MouseSensitivity = value;
                break;
            case TitleSettingKind.FrameRateLimit:
                GameSettings.FrameRateLimit = Mathf.RoundToInt(value / 10f) * 10;
                break;
        }
    }

    /// <summary>「＜」（-1）「＞」（+1）で値を1つ進める（端まで行ったら反対の端へ回る）。</summary>
    private void Step(TitleSettingKind kind, int delta)
    {
        switch (kind)
        {
            case TitleSettingKind.ControllerType:
                GameSettings.ControllerOperation = GameSettings.ControllerOperation == ControllerOperationType.TypeA
                    ? ControllerOperationType.TypeB
                    : ControllerOperationType.TypeA;
                break;

            case TitleSettingKind.Resolution:
                if (resolutions.Length > 0)
                {
                    resolutionIndex = (resolutionIndex + delta + resolutions.Length) % resolutions.Length;
                }
                break;

            case TitleSettingKind.ScreenMode:
                // 選べる3つに無い画面モードのときは、先頭から
                int index = System.Array.IndexOf(ScreenModes, pendingScreenMode);
                index = index < 0 ? 0 : (index + delta + ScreenModes.Length) % ScreenModes.Length;
                pendingScreenMode = ScreenModes[index];
                break;

            case TitleSettingKind.VSync:
                GameSettings.VSync = !GameSettings.VSync;
                break;

            case TitleSettingKind.Quality:
                string[] qualities = QualitySettings.names;
                if (qualities.Length > 0)
                {
                    GameSettings.QualityLevel = (QualitySettings.GetQualityLevel() + delta + qualities.Length) % qualities.Length;
                }
                break;
        }
    }

    /// <summary>設定の1行に、今の値を出す。</summary>
    private void RefreshRow(TitleSettingRow row)
    {
        string text;
        float? sliderValue = null;

        switch (row.Kind)
        {
            case TitleSettingKind.Volume:
                sliderValue = GameSettings.Volume;
                text = $"{Mathf.RoundToInt(GameSettings.Volume * 100f)} %";
                break;
            case TitleSettingKind.MouseSensitivity:
                sliderValue = GameSettings.MouseSensitivity;
                text = $"{GameSettings.MouseSensitivity:0.0} 倍";
                break;
            case TitleSettingKind.FrameRateLimit:
                sliderValue = GameSettings.FrameRateLimit;
                text = $"{GameSettings.FrameRateLimit} fps";
                break;
            case TitleSettingKind.ControllerType:
                text = GameSettings.ControllerOperation == ControllerOperationType.TypeB ? "タイプB" : "タイプA";
                break;
            case TitleSettingKind.Resolution:
                text = resolutions.Length == 0
                    ? "—"
                    : $"{resolutions[resolutionIndex].width}x{resolutions[resolutionIndex].height}";
                break;
            case TitleSettingKind.ScreenMode:
                text = ScreenModeName(pendingScreenMode);
                break;
            case TitleSettingKind.VSync:
                text = GameSettings.VSync ? "ON" : "OFF";
                break;
            default: // 画質
                string[] qualities = QualitySettings.names;
                text = qualities.Length == 0
                    ? "—"
                    : qualities[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, qualities.Length - 1)];
                break;
        }

        if (row.ValueText != null)
        {
            row.ValueText.text = text;
        }

        if (sliderValue.HasValue && row.Slider != null)
        {
            row.Slider.SetValueWithoutNotify(sliderValue.Value);
        }
    }

    /// <summary>同じ大きさの解像度を1つにまとめた一覧。</summary>
    private static Resolution[] UniqueResolutions()
    {
        List<Resolution> list = new List<Resolution>();
        foreach (Resolution r in Screen.resolutions)
        {
            if (!list.Exists(x => x.width == r.width && x.height == r.height))
            {
                list.Add(r);
            }
        }
        return list.ToArray();
    }

    /// <summary>今の画面の大きさが一覧の何番目か（無ければ最後、一覧が空なら -1）。</summary>
    private static int FindCurrentResolution(Resolution[] list)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].width == Screen.width && list[i].height == Screen.height)
            {
                return i;
            }
        }
        return list.Length - 1;
    }

    private static string ScreenModeName(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.ExclusiveFullScreen:
                return "フルスクリーン";
            case FullScreenMode.FullScreenWindow:
                return "ウィンドウフルスクリーン";
            case FullScreenMode.MaximizedWindow:
                return "最大化ウィンドウ";
            default:
                return "ウィンドウ";
        }
    }
}
