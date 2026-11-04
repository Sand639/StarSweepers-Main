using UnityEngine;

/// <summary>
/// **ゲームが止まっているか**を、どこからでも見られるようにしたもの。
///
/// ポーズ画面（<see cref="PauseMenu"/>）が開いている間だけ true になる。
///
/// ## なぜ必要か
///
/// 止めている間は `Time.timeScale` が 0 になるので、**歩く・落ちるは自動的に止まる。**
/// けれど、**マウスでの視点移動は止まらない**（動いた量をそのまま使っているため）。
/// カーソルの出し入れも、放っておくと**カメラ側とポーズ画面で取り合いになる。**
///
/// そこで「いま止まっているか」を1か所で持ち、
/// **カメラ側がそれを見て、自分から手を引く**形にしてある。
/// </summary>
public static class GamePause
{
    /// <summary>いまゲームが止まっているか。</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>止める・動かすが切り替わったフレーム。まだ一度も切り替わっていなければ -1。</summary>
    private static int lastChangedFrame = -1;

    /// <summary>このフレームに、止める・動かすが切り替わったか。</summary>
    public static bool ChangedThisFrame => Time.frameCount == lastChangedFrame;

    /// <summary>
    /// **いま、遊びの操作を受け付けてはいけないか。**
    /// 入力を読む側（プレイヤー・カメラ・複製配置など）は、`IsPaused` ではなくこちらを見る。
    ///
    /// ## なぜ「切り替わったフレーム」も含めるか
    ///
    /// ポーズ画面は Escape や左クリックで閉じるが、**閉じるのと同じフレームのうちに**
    /// 「止まっていない」へ戻る。そのあとに動く側は、**同じ Escape・同じクリックをもう一度拾う。**
    /// `wasPressedThisFrame` はそのフレーム中ずっと true のままだからである。
    ///
    /// 実際に、**ポーズ画面を閉じた瞬間に複製の下書きが消える／物が置かれる**という形で出る
    /// （`リスクリスト.md` 2026/9/14 登録）。1フレームだけ入力を捨てれば、この取り合いは起きない。
    /// </summary>
    public static bool BlocksInput => IsPaused || ChangedThisFrame;

    /// <summary>
    /// 止める・動かすを切り替える。
    /// **`Time.timeScale` はここでしか触らない**（あちこちで触ると戻し忘れる）。
    ///
    /// <paramref name="freezeTime"/> を false にすると、**世界の時間は止めずに、操作だけ受け付けなく**なる。
    /// **オンラインではこちらを使うこと。** 自分のPCだけ時間を止めても、
    /// ホストと他の参加者は動き続けるので、閉じた瞬間に相手が飛んで見える
    /// （`リスクリスト.md` 2026/9/16 登録）。
    /// </summary>
    public static void SetPaused(bool paused, bool freezeTime = true)
    {
        if (IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        lastChangedFrame = Time.frameCount;

        // **オンラインでつながっている間は、世界の時間を止めない。**
        //
        // 自分のPCだけ `Time.timeScale` を 0 にしても、ホストと他の参加者は動き続ける。
        // ホストが止めた場合はもっと悪く、**ホストが動かしている物や物資が全員の画面で
        // 空中に止まるのに、試合の残り時間（共有の時計で数えている）だけは減り続ける。**
        // 閉じた瞬間に相手が飛んで見える原因にもなる。
        //
        // 呼ぶ側が freezeTime を指定し忘れても事故にならないよう、ここでまとめて面倒を見る。
        // （`リスクリスト.md` 2026/9/16・2026/9/17 に登録されていた件。2026/9/20 に対応）
        if (paused && IsOnline())
        {
            freezeTime = false;
        }

        if (paused)
        {
            timeWasFrozen = freezeTime;

            if (freezeTime)
            {
                Time.timeScale = 0f;
            }

            return;
        }

        // 止めたときに時間を止めていたときだけ戻す（止めていなければ触らない）
        if (timeWasFrozen)
        {
            Time.timeScale = 1f;
            timeWasFrozen = false;
        }
    }

    /// <summary>止めたときに、世界の時間まで止めたか。戻すときに使う。</summary>
    private static bool timeWasFrozen;

    /// <summary>
    /// いま通信でつながっているか（1人で遊んでいるのではないか）。
    ///
    /// **ここでしか Netcode を見ていない。** 通信を使わない遊びでも
    /// `NetworkManager` が無ければ false になるだけなので、影響しない。
    /// </summary>
    private static bool IsOnline()
    {
        return Unity.Netcode.NetworkManager.Singleton != null
            && Unity.Netcode.NetworkManager.Singleton.IsListening;
    }

    /// <summary>
    /// 再生を始めるたびに、止まっていない状態から始める。
    ///
    /// **この値はゲームを通してひとつしかない**ので、
    /// 止めたまま再生を止めると、次に始めたときも止まったままになりかねない。
    /// その事故を防ぐためのもの。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnStart()
    {
        IsPaused = false;
        lastChangedFrame = -1;
        timeWasFrozen = false;
        Time.timeScale = 1f;
    }
}
