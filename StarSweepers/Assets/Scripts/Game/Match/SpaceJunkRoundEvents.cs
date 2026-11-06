using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **ラウンドの途中で起きるイベント。** <see cref="SpaceJunkRound"/> の続き（同じクラスをファイルだけ分けている）。
///
/// ## 流れ（2026/9/29・大槻さん）
///
/// 1. ラウンドが始まってから <see cref="SpaceJunkEventSettings.startSeconds"/> 秒（初期値20秒）たったら
/// 2. <see cref="SpaceJunkEventSettings.pool"/>（候補の一覧）から、選ばれやすさの比で1つを選んで始める
/// 3. 候補ごとの「続く秒数」が過ぎたら終わる（0 ならラウンドの終わりまで続く）
///
/// 設定は <see cref="SpaceJunkSession"/>（プレハブ）の「イベント」の欄。**得点制（Score）のときだけ起きる。**
/// 画面に出す名前と説明もそこで変えられ、**ホストの値が全員へ配られる**。
///
/// ## デブリセット（DebrisSet）
///
/// - チームごとに、デブリを4〜6個ランダムに選んだ「お題」が出る（同じ種類が2個以上入ることもある）。
///   **個数はイベントの始まりに1回だけ決め、全チーム同じ。中身はチームごとにばらばら**（2026/9/29 修正）
/// - **お題のうち3個**（<see cref="SpaceJunkEventSettings.debrisSetGoal"/>）をゴールに入れると **10点**。
///   **そのあと新しいお題を引き直し、0から集め直す**
///   - 順不同（初期値。<see cref="SpaceJunkEventSettings.debrisSetOrdered"/> が OFF）…
///     お題に入っていて、まだ足りていない種類なら進む
///   - 順番どおり（ON）… **お題の次の1個と同じ種類のときだけ進む**（先頭から3個）
/// - お題に無い・もう足りているデブリを入れても、ふつうの1点は入る
///
/// ## 期間限定高価値デブリ（HighValueDebris。2026/9/29 修正）
///
/// - 始まると、**デブリの種類が1つランダムに選ばれる**（例：回路基板＝紫）
/// - **続く秒数のうちは、その種類を入れると +2点**（1点と合わせて3点）
/// - 画面の上に「紫（回路基板）のデブリが +2点」のように出る
///
/// ## 特殊デブリ（HeavyDebris）
///
/// - 始まると、**重さを決めた大きなデブリ**が出る（初期値は3個。重さは1個ずつ 1〜3 の間からランダム。水色に光る）
/// - **投げられない。** 右クリックのフックは引っ掛からず、左で引っ掛けて地面を引きずることしかできない
///   （ゲージが往復しない・重いほど速い・重いほど短くしか引きずれない。操作は <see cref="ThrowController"/>）
/// - ゴールに入れると、1点の代わりに高い得点（初期値20点。重さには関係ない）
///
/// ## 判定するのはホストだけ
///
/// イベントを選ぶのも、お題や高価値の種類・重いデブリを決めるのも、得点を数えるのもホスト。
/// 参加者のPCは下の一覧を受け取って出すだけ（重いデブリの見た目は、一覧を見て各PCが付ける）。
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Game/Match/SpaceJunkRound.md。
/// </summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // イベント全体
    // ------------------------------------------------------------

    /// <summary>いま起きている（起きた）イベント。</summary>
    private readonly NetworkVariable<SpaceJunkEventKind> activeEvent =
        new NetworkVariable<SpaceJunkEventKind>(SpaceJunkEventKind.None);

    /// <summary>イベントが始まる（始まった）時刻。0 なら、このラウンドではイベントは起きない。</summary>
    private readonly NetworkVariable<double> eventServerTime = new NetworkVariable<double>(0d);

    /// <summary>イベントが終わる時刻。0 ならラウンドの終わりまで続く。</summary>
    private readonly NetworkVariable<double> eventEndServerTime = new NetworkVariable<double>(0d);

    /// <summary>画面に出すイベントの名前（ホストの設定）。</summary>
    private readonly NetworkVariable<FixedString64Bytes> eventName =
        new NetworkVariable<FixedString64Bytes>(default);

    /// <summary>画面に出すイベントの説明（ホストの設定）。</summary>
    private readonly NetworkVariable<FixedString512Bytes> eventDescription =
        new NetworkVariable<FixedString512Bytes>(default);

    /// <summary>終わりの片付けをもう済ませたか（ホストだけが使う）。</summary>
    private bool serverEventFinished;

    /// <summary>いま起きている（起きた）イベント。起きていなければ None。</summary>
    public SpaceJunkEventKind ActiveEvent => activeEvent.Value;

    /// <summary>画面に出すイベントの名前。</summary>
    public string EventName => eventName.Value.ToString();

    /// <summary>画面に出すイベントの説明。</summary>
    public string EventDescription => eventDescription.Value.ToString();

    /// <summary>イベントがいま続いているか（始まっていて、終わっていない）。</summary>
    public bool IsEventRunning
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None)
            {
                return false;
            }

            return eventEndServerTime.Value <= 0d || NetworkManager.ServerTime.Time < eventEndServerTime.Value;
        }
    }

    /// <summary>イベントの残り秒数。**時間の決まっていないイベント、または起きていなければ -1。**</summary>
    public float EventRemainingSeconds
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None || eventEndServerTime.Value <= 0d)
            {
                return -1f;
            }

            return Mathf.Max(0f, (float)(eventEndServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>
    /// イベントが始まるまでの秒数。**このラウンドでイベントが起きない、またはもう始まっていれば -1。**
    /// </summary>
    public float SecondsUntilEvent
    {
        get
        {
            if (NetworkManager == null || eventServerTime.Value <= 0d || activeEvent.Value != SpaceJunkEventKind.None)
            {
                return -1f;
            }

            // 始まりの演出中は止めておく（START から減り始める。残り時間と同じ。2026/10/7）
            if (IsIntro)
            {
                return Mathf.Max(0f, (float)(eventServerTime.Value - PlayStartServerTime));
            }

            return Mathf.Max(0f, (float)(eventServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    /// <summary>イベントが始まってからの秒数。始まっていなければ -1（始まった合図の表示に使う）。</summary>
    public float SecondsSinceEventStarted
    {
        get
        {
            if (NetworkManager == null || activeEvent.Value == SpaceJunkEventKind.None)
            {
                return -1f;
            }

            return Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - eventServerTime.Value));
        }
    }

    /// <summary>ホストの設定。係がいなければ初期値。</summary>
    private static SpaceJunkEventSettings EventSettings =>
        SpaceJunkSession.Current != null ? SpaceJunkSession.Current.Events : new SpaceJunkEventSettings();

    // ------------------------------------------------------------
    // ホストだけが動かす
    // ------------------------------------------------------------

    /// <summary>
    /// ラウンドの始まりに、イベントの時刻を決める。<see cref="OnNetworkSpawn"/> から呼ぶ。
    ///
    /// 流れ：一覧を空の枠に作り直す → ホストの設定を全員へ配る値に写す →
    /// 得点制で候補があるときだけ、動き出してから <see cref="SpaceJunkEventSettings.startSeconds"/> 秒後を開始時刻にする。
    /// </summary>
    private void ServerInitEvents(float roundSeconds)
    {
        ServerResetEventLists();

        activeEvent.Value = SpaceJunkEventKind.None;
        eventServerTime.Value = 0d;
        eventEndServerTime.Value = 0d;
        eventName.Value = default;
        eventDescription.Value = default;
        serverEventFinished = false;

        SpaceJunkEventSettings settings = EventSettings;
        setOrdered.Value = settings.debrisSetOrdered;
        setGoal.Value = Mathf.Max(1, settings.debrisSetGoal);
        highValueKind.Value = -1;
        highValueBonus.Value = Mathf.Max(0, settings.highValueBonus);
        heavyPoints.Value = Mathf.Max(1, settings.heavyPoints);
        heavyBonusPerWeight.Value = settings.heavyWeightBonus ? Mathf.Max(0, settings.heavyBonusPerWeight) : 0;

        // **得点制のときだけ。** 3種類ルールはその場で決着がつくので、ボーナスの意味が無い
        bool hasCandidate = (settings.pool != null && settings.pool.Count > 0) ||
                            settings.debugForceEvent != SpaceJunkEventKind.None;

        if (rule.Value != SpaceJunkWinRule.Score || !hasCandidate)
        {
            return;
        }

        // ラウンドが終わってから始まっても意味が無い
        if (settings.startSeconds >= roundSeconds)
        {
            Debug.Log($"[JUNK] イベントの開始（{settings.startSeconds} 秒後）がラウンドの時間（{roundSeconds} 秒）より遅いので、" +
                      "このラウンドではイベントは起きません。");
            return;
        }

        // 動き出してから数える（始まりの演出があれば、その終わりから。2026/10/7）
        double playStart = PlayStartServerTime > 0d ? PlayStartServerTime : NetworkManager.ServerTime.Time;
        eventServerTime.Value = playStart + settings.startSeconds;
    }

    /// <summary>
    /// 全員へ配る一覧を、**全チームぶんの空の枠**に作り直す（お題の必要数・進み具合・並び、重いデブリの一覧）。
    /// NetworkList は「何番目」で引くので、先に枠だけ全部入れておく（あとから足すと並びがずれる）。
    /// </summary>
    private void ServerResetEventLists()
    {
        setRequired.Clear();
        setProgress.Clear();
        setBonus.Clear();
        setsCompleted.Clear();
        setSequence.Clear();
        setStep.Clear();
        heavyIds.Clear();
        heavyWeights.Clear();
        stageHeavyIds.Clear();
        stageHeavyWeights.Clear();

        // チーム × 種類
        for (int i = 0; i < SpaceJunkTeams.MaxTeams * SpaceJunkMaterials.Count; i++)
        {
            setRequired.Add(0);
            setProgress.Add(0);
        }

        // チーム × お題の枠（空いている枠は -1）
        for (int i = 0; i < SpaceJunkTeams.MaxTeams * MaxSetSize; i++)
        {
            setSequence.Add(-1);
        }

        // チームごとに1つ
        for (int team = 0; team < SpaceJunkTeams.MaxTeams; team++)
        {
            setBonus.Add(0);
            setsCompleted.Add(0);
            setStep.Add(0);
        }
    }

    /// <summary>時間が来たらイベントを始め、続く秒数が過ぎたら終わらせる。<see cref="Update"/> から呼ぶ。</summary>
    private void ServerUpdateEvents()
    {
        // ステージに置いた重いデブリは、イベントに関係なく、場外へ落ちて消えたら一覧から外す
        ServerPruneLost(stageHeavyIds, stageHeavyWeights);

        // もう始まっている：続く秒数が過ぎていれば片付ける（1回だけ）。
        // 重いデブリのイベント中は、落ちて消えたぶんを一覧から外し続ける（「あと ○個」の表示のため）
        if (activeEvent.Value != SpaceJunkEventKind.None)
        {
            if (!serverEventFinished && !IsEventRunning)
            {
                ServerFinishEvent();
            }
            else if (activeEvent.Value == SpaceJunkEventKind.HeavyDebris)
            {
                ServerPruneLost(heavyIds, heavyWeights);
            }
            return;
        }

        // まだ始まっていない：このラウンドでは起きない（0）か、開始時刻がまだなら待つ
        if (eventServerTime.Value <= 0d || NetworkManager.ServerTime.Time < eventServerTime.Value)
        {
            return;
        }

        SpaceJunkEventEntry picked = EventSettings.PickRandom();

        if (picked == null || picked.kind == SpaceJunkEventKind.None)
        {
            // 候補に None が入っていた（または選べる候補が無い）。**「今回は何も起きない」**として、もう選び直さない
            eventServerTime.Value = 0d;
            Debug.Log("[JUNK] イベントの抽選：今回は何も起きません。");
            return;
        }

        ServerStartEvent(picked);
    }

    /// <summary>
    /// 選ばれたイベントを始める。先に種類ごとの準備（お題を引く・高価値の種類を選ぶ・重いデブリを出す）をしてから、
    /// 時刻・名前・説明を全員へ配る。**<see cref="activeEvent"/> は最後に入れる**（参加者が一覧より先に「始まった」を見ないように）。
    /// </summary>
    private void ServerStartEvent(SpaceJunkEventEntry entry)
    {
        SpaceJunkEventSettings settings = EventSettings;

        switch (entry.kind)
        {
            case SpaceJunkEventKind.DebrisSet:
                // **お題の個数は、ここで1回だけ決めて全チームそろえる**（中身はチームごとにばらばら）
                int min = Mathf.Max(1, settings.debrisSetMinSize);
                int max = Mathf.Max(min, settings.debrisSetMaxSize);
                serverSetSize = Mathf.Min(Random.Range(min, max + 1), MaxSetSize);

                for (int team = 0; team < SpaceJunkTeams.MaxTeams; team++)
                {
                    ServerDrawDebrisSet(team);
                }
                break;

            case SpaceJunkEventKind.HighValueDebris:
                highValueKind.Value = SpaceJunkMaterials.RandomUsedIndex();
                Debug.Log($"[JUNK] 高価値の種類：{SpaceJunkMaterials.Name(SpaceJunkMaterials.FromIndex(highValueKind.Value))}" +
                          $"（+{highValueBonus.Value} 点）");
                break;

            case SpaceJunkEventKind.HeavyDebris:
                ServerSpawnHeavyDebris(settings);
                break;
        }

        FixedString64Bytes nameText = default;
        nameText.CopyFromTruncated(entry.NameOrDefault);
        FixedString512Bytes descriptionText = default;
        descriptionText.CopyFromTruncated(entry.DescriptionOrDefault(settings));

        // 始まった時刻は「予定の時刻」ではなく「実際に始まった時刻」にそろえる（始まった合図の表示に使う）
        double now = NetworkManager.ServerTime.Time;
        eventServerTime.Value = now;
        eventEndServerTime.Value = entry.durationSeconds > 0f ? now + entry.durationSeconds : 0d;
        eventName.Value = nameText;
        eventDescription.Value = descriptionText;
        activeEvent.Value = entry.kind;

        Debug.Log($"[JUNK] イベント開始：{entry.NameOrDefault}" +
                  (entry.durationSeconds > 0f ? $"（{entry.durationSeconds} 秒間）" : "（ラウンドの終わりまで）"));
    }

    /// <summary>続く秒数が過ぎたときの片付け。</summary>
    private void ServerFinishEvent()
    {
        serverEventFinished = true;

        if (activeEvent.Value == SpaceJunkEventKind.HeavyDebris)
        {
            // 時間の決まった特殊デブリなら、残りはふつうのデブリに戻す（消さない）
            heavyIds.Clear();
            heavyWeights.Clear();
        }

        Debug.Log($"[JUNK] イベント終了：{EventName}");
    }

    /// <summary>
    /// **デブリがゴールに入ったときの、イベントのぶんの得点。** ふつうの1点とは別に足される。
    /// <see cref="ServerAddScore"/> から呼ぶ。
    /// </summary>
    private int ServerEventBonusOnCollect(int team, SpaceJunkMaterialKind kind, NetworkObject item)
    {
        if (!IsEventRunning)
        {
            return 0;
        }

        switch (activeEvent.Value)
        {
            case SpaceJunkEventKind.DebrisSet:
                return ServerDebrisSetCollect(team, kind);
            case SpaceJunkEventKind.HighValueDebris:
                return ServerHighValueCollect(team, kind);
            case SpaceJunkEventKind.HeavyDebris:
                return ServerHeavyCollect(team, item);
            default:
                return 0;
        }
    }

}
