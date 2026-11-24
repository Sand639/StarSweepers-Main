using UnityEngine;

/// <summary>
/// **プレイヤー挙動の4つの段階**（2026/11/18・小野田さん）。数字は保存と通信に使うので、変えない。
/// </summary>
public enum PlayerBehaviorStep
{
    Aim = 0,   // 1. 狙う
    Fire = 1,  // 2. 発射
    Pull = 2,  // 3. 引っ張る（しならせる部分）
    Throw = 3  // 4. 投げる／投げ飛ばす（もう一度押すか、押さずにはじき飛ぶ）
}

/// <summary>
/// **各段階のパターン。** 数字は保存と通信に使うので、変えない（足すときは C = 2 …）。
/// </summary>
public enum PlayerBehaviorPattern
{
    A = 0,
    B = 1,
    C = 2, // 2026/11/18：狙う・発射の「先を狙いで曲げる（すぐ向く）」
    D = 3  // 2026/11/18：狙う・発射の「先を狙いで曲げる（遅れて付いていく）」
}

/// <summary>
/// **プレイヤー挙動（狙う・発射・引っ張る・投げる）の、パターンの一覧と決まり。**（2026/11/18・小野田さん）
///
/// ロビーコンソール（<see cref="SpaceJunkLobbyUI"/> の小窓）で、**ホストが段階ごとにパターンを選ぶ**。選んだ値は
/// <see cref="SpaceJunkSession"/> が全員に配り、全員の体（ボットも）がそれで動く。通信していないとき（検証シーン）は、
/// このPCに保存した値（最後にホストをしたときの選び方）を使う。
///
/// | 段階 | A | B | C | D |
/// | --- | --- | --- | --- | --- |
/// | 1 狙う・2 発射 | **鞭**（押した瞬間に狙った向きへ出て、体を振ってしならせて当てる） | **溜め**（押して長さを決め、離すとまっすぐ伸びる） | **曲げ撃ち**（体はあまり回らず、光線の先がすぐ狙いへ曲がる） | **溜め鞭**（A に溜めを足したもの。押して長さを決め、離すと鞭の光線がその長さまで出る。2026/11/21） |
/// | 3 引っ張る・4 投げる | **しなって引き寄せ、押せば後ろへ投げる／押さなければ前へはじき飛ぶ** | **距離で変わる**（押した瞬間の距離で、前へ投げる・引っ張る・クリティカルと強さが決まる。大槻さんの案。ボットは A のまま） | （まだ無い） | （まだ無い） |
///
/// D は 2026/11/21 に「曲げ撃ち（遅れ）」から「溜め鞭」に置き換えた（小野田さん「A にためを追加する形で D を上書き」）。
///
/// **「固定の組」**：A の「狙う」と「発射」は1つの流れなので、片方を A にするともう片方も A になる（B・C・D も同じ）。
/// 「引っ張る」と「投げる」の A も同じ。組にしないパターン（段階ごとに別々に選べるもの）を足すときは
/// <see cref="IsFixedPair"/> で false を返すようにする。
///
/// **パターンを足すとき**：<see cref="Available"/> に足し、<see cref="Describe"/> に説明を書き、
/// その段階を動かすスクリプト（狙う・発射＝<see cref="SweeperShooter"/>、引っ張る・投げる＝<see cref="SweeperReel"/>）で
/// <see cref="Get"/> を見て分ける。詳しくは `Documents/機能ドキュメント/プレイヤー挙動のパターン.md`。
/// </summary>
public static class PlayerBehavior
{
    /// <summary>段階の数。</summary>
    public const int StepCount = 4;

    /// <summary>段階ごとに選べるパターン（並びは <see cref="PlayerBehaviorStep"/> の数字の順）。</summary>
    private static readonly PlayerBehaviorPattern[][] Available =
    {
        new[] { PlayerBehaviorPattern.A, PlayerBehaviorPattern.B, PlayerBehaviorPattern.C, PlayerBehaviorPattern.D }, // 狙う
        new[] { PlayerBehaviorPattern.A, PlayerBehaviorPattern.B, PlayerBehaviorPattern.C, PlayerBehaviorPattern.D }, // 発射
        new[] { PlayerBehaviorPattern.A, PlayerBehaviorPattern.B }, // 引っ張る
        new[] { PlayerBehaviorPattern.A, PlayerBehaviorPattern.B }, // 投げる
    };

    /// <summary>保存の名前（PlayerPrefs のキー）の頭。後ろに段階の数字が付く。変えると、今までの保存が読めなくなる。</summary>
    private const string SaveKeyPrefix = "StarSweepers.PlayerBehavior.";

    /// <summary>通信していないときに使う値（このPCに保存した値）。最初に読まれたときに読み込む。</summary>
    private static readonly PlayerBehaviorPattern[] local = new PlayerBehaviorPattern[StepCount];

    private static bool loaded;

    /// <summary>段階の、画面に出す名前（「1 狙う」など）。</summary>
    public static string NameOf(PlayerBehaviorStep step)
    {
        switch (step)
        {
            case PlayerBehaviorStep.Aim: return "1 狙う";
            case PlayerBehaviorStep.Fire: return "2 発射";
            case PlayerBehaviorStep.Pull: return "3 引っ張る";
            case PlayerBehaviorStep.Throw: return "4 投げる";
            default: return step.ToString();
        }
    }

    /// <summary>そのパターンの短い説明（ロビーコンソールに出す）。</summary>
    public static string Describe(PlayerBehaviorStep step, PlayerBehaviorPattern pattern)
    {
        bool shot = step == PlayerBehaviorStep.Aim || step == PlayerBehaviorStep.Fire;
        if (shot)
        {
            switch (pattern)
            {
                case PlayerBehaviorPattern.B: return "溜め：押して長さを決め、離すとまっすぐ伸びる";
                case PlayerBehaviorPattern.C: return "曲げ撃ち：体はあまり回らず、光線の先がすぐ狙いへ曲がる";
                case PlayerBehaviorPattern.D: return "溜め鞭：押して長さを決め、離すと鞭の光線が出る。体を振ってしならせて当てる";
                default: return "鞭：押すとすぐ出て、体を振ってしならせて当てる";
            }
        }

        return pattern == PlayerBehaviorPattern.B
            ? "距離で変わる：押した瞬間の距離で、前へ投げる・引っ張る・クリティカルと強さが決まる"
            : "しなって引き寄せ、押せば後ろへ投げる／押さなければ前へはじき飛ぶ";
    }

    /// <summary>その段階で選べるパターン。</summary>
    public static PlayerBehaviorPattern[] PatternsOf(PlayerBehaviorStep step)
    {
        return Available[Index(step)];
    }

    /// <summary>その段階で、そのパターンが選べるか。</summary>
    public static bool IsAvailable(PlayerBehaviorStep step, PlayerBehaviorPattern pattern)
    {
        foreach (PlayerBehaviorPattern candidate in Available[Index(step)])
        {
            if (candidate == pattern)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>組になる段階（狙う⇔発射、引っ張る⇔投げる）。</summary>
    public static PlayerBehaviorStep PartnerOf(PlayerBehaviorStep step)
    {
        switch (step)
        {
            case PlayerBehaviorStep.Aim: return PlayerBehaviorStep.Fire;
            case PlayerBehaviorStep.Fire: return PlayerBehaviorStep.Aim;
            case PlayerBehaviorStep.Pull: return PlayerBehaviorStep.Throw;
            default: return PlayerBehaviorStep.Pull;
        }
    }

    /// <summary>
    /// そのパターンが「組の段階と同じパターンで固定」か。**いまあるパターンは全部固定**
    /// （鞭・溜めは狙うと発射が1つの流れ、引っ張ると投げるの A も1つの流れ。2026/11/18・小野田さん）。
    /// </summary>
    public static bool IsFixedPair(PlayerBehaviorStep step, PlayerBehaviorPattern pattern)
    {
        return true;
    }

    /// <summary>
    /// **<paramref name="values"/> の <paramref name="step"/> を <paramref name="pattern"/> にし、決まりに合わせて直す。**
    /// 固定の組なら、組の段階も同じパターンにする。組の段階がいま「固定の組」のパターンで、相手が違うものになったときも、
    /// 組の段階を相手に合わせて動かす（固定が外れた片割れを残さない）。選べないパターンなら何もしない。
    /// </summary>
    public static void Assign(PlayerBehaviorPattern[] values, PlayerBehaviorStep step, PlayerBehaviorPattern pattern)
    {
        if (values == null || values.Length < StepCount || !IsAvailable(step, pattern))
        {
            return;
        }

        values[Index(step)] = pattern;

        PlayerBehaviorStep partner = PartnerOf(step);
        PlayerBehaviorPattern partnerValue = values[Index(partner)];
        bool needsPair = IsFixedPair(step, pattern) || IsFixedPair(partner, partnerValue);
        if (needsPair && partnerValue != pattern && IsAvailable(partner, pattern))
        {
            values[Index(partner)] = pattern;
        }
    }

    /// <summary>
    /// 読み込んだ・届いた値を決まりに合わせる（選べないパターンは A に、固定の組が食い違っていたら前の段階＝狙う・引っ張るに合わせる）。
    /// </summary>
    public static void Normalize(PlayerBehaviorPattern[] values)
    {
        if (values == null || values.Length < StepCount)
        {
            return;
        }

        for (int i = 0; i < StepCount; i++)
        {
            if (!IsAvailable((PlayerBehaviorStep)i, values[i]))
            {
                values[i] = PlayerBehaviorPattern.A;
            }
        }

        Assign(values, PlayerBehaviorStep.Aim, values[Index(PlayerBehaviorStep.Aim)]);
        Assign(values, PlayerBehaviorStep.Pull, values[Index(PlayerBehaviorStep.Pull)]);
    }

    /// <summary>
    /// **いまの挙動のパターン。** 部屋（<see cref="SpaceJunkSession"/>）があれば、ホストが決めて配った値。
    /// 無ければ（通信なしの検証シーン）、このPCに保存した値。
    /// </summary>
    public static PlayerBehaviorPattern Get(PlayerBehaviorStep step)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session != null && session.IsSpawned)
        {
            return session.BehaviorOf(step);
        }

        return GetSaved(step);
    }

    /// <summary>このPCに保存した値（ホストをするときの最初の値。通信なしのときもこれを使う）。</summary>
    public static PlayerBehaviorPattern GetSaved(PlayerBehaviorStep step)
    {
        Load();
        return local[Index(step)];
    }

    /// <summary>このPCに保存した値を、全部まとめて写す（ホストが部屋を作ったときに読む）。</summary>
    public static PlayerBehaviorPattern[] CopySaved()
    {
        Load();
        return (PlayerBehaviorPattern[])local.Clone();
    }

    /// <summary>
    /// このPCに保存する（決まりに合わせてから）。ホストがロビーコンソールで変えたとき・テストから呼ぶ。
    /// </summary>
    public static void Save(PlayerBehaviorPattern[] values)
    {
        if (values == null || values.Length < StepCount)
        {
            return;
        }

        Load();
        for (int i = 0; i < StepCount; i++)
        {
            local[i] = values[i];
        }
        Normalize(local);

        for (int i = 0; i < StepCount; i++)
        {
            PlayerPrefs.SetInt(SaveKeyPrefix + i, (int)local[i]);
        }
        PlayerPrefs.Save();
    }

    private static void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        for (int i = 0; i < StepCount; i++)
        {
            local[i] = (PlayerBehaviorPattern)PlayerPrefs.GetInt(SaveKeyPrefix + i, (int)PlayerBehaviorPattern.A);
        }
        Normalize(local);
    }

    private static int Index(PlayerBehaviorStep step)
    {
        return Mathf.Clamp((int)step, 0, StepCount - 1);
    }
}
