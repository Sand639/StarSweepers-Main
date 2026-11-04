using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// **コントローラーのボタンを読むための、小さな道具箱。**
///
/// キーボードの `Keyboard.current[key].wasPressedThisFrame` と同じ書き方で、
/// コントローラーのボタンも読めるようにする（2026/9/22・大槻さんの依頼でコントローラー操作を追加）。
///
/// ボタンの名前は **Xbox のコントローラーの呼び方**（A・B・X・Y）にそろえている。
/// PlayStation のコントローラーでは、A＝×、B＝○、X＝□、Y＝△ にあたる。
/// </summary>
public static class GamepadInput
{
    /// <summary>そのボタンが、このフレームで押されたか。コントローラーが無ければ false。</summary>
    public static bool WasPressed(GamepadButton button)
    {
        Gamepad pad = Gamepad.current;
        return pad != null && pad[button].wasPressedThisFrame;
    }

    /// <summary>画面に出すボタンの名前（Xbox の呼び方）。</summary>
    public static string Label(GamepadButton button)
    {
        switch (button)
        {
            case GamepadButton.South: return "A";
            case GamepadButton.East: return "B";
            case GamepadButton.West: return "X";
            case GamepadButton.North: return "Y";
            case GamepadButton.Start: return "Start";
            case GamepadButton.Select: return "Back";
            case GamepadButton.LeftShoulder: return "LB";
            case GamepadButton.RightShoulder: return "RB";
            case GamepadButton.LeftTrigger: return "LT";
            case GamepadButton.RightTrigger: return "RT";
            default: return button.ToString();
        }
    }
}
