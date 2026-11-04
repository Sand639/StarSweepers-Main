using System;

/// <summary>
/// **「ステージを最初の状態に戻して」という合図。**
///
/// 割れた床を元に戻す、といった**やり直しのときに戻したい物**が、この合図を聞いている。
/// 合図を出すのは、やり直しを受け持つ側（例：レースの R キー → `RaceManager`）。
///
/// ## なぜ合図にしたか
///
/// 出す側と聞く側が**お互いを知らなくて済む**ようにするため。
/// 床は「レースかどうか」を知らなくてよく、レースは「どんな仕掛けが置いてあるか」を知らなくてよい。
/// **仕掛けが増えても、やり直しの側は書き換えずに済む。**
/// </summary>
public static class StageReset
{
    /// <summary>やり直しの合図が出たときに呼ばれる。</summary>
    public static event Action Requested;

    /// <summary>やり直しの合図を出す。</summary>
    public static void Request()
    {
        Requested?.Invoke();
    }
}
