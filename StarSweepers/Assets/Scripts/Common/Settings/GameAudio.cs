using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>流す BGM の種類。</summary>
public enum BgmTrack
{
    /// <summary>何も流さない</summary>
    None,

    /// <summary>タイトルとロビー（Time Travel）</summary>
    Title,

    /// <summary>ラウンド中（contedefees_0064）</summary>
    Round,
}

/// <summary>画面のボタンの効果音の種類。</summary>
public enum UiSound
{
    /// <summary>ボタンを押した（決定）</summary>
    Confirm,

    /// <summary>戻る・閉じる（キャンセル）</summary>
    Cancel,

    /// <summary>マウスが重なった・コントローラーで選んだ</summary>
    Hover,
}

/// <summary>
/// ゲーム中の効果音の種類（2026/11/26）。計画書（音の一覧）の番号があるものは、名前のうしろに番号を書いた。
/// **どれも仮の音**（共有フォルダの「素材集め」から選んだ。決まったら同じ名前のファイルを上書きする）。
/// </summary>
public enum GameSe
{
    // プレイヤー
    PlayerFall,      // 落ちる
    PlayerStun,      // 転ぶ（スタン）
    PlayerShoot,     // 線発射
    PlayerAim,       // 構える
    PlayerHit,       // ぶつかる（吹き飛ばされた）

    // 画面（UI）
    UiRoulette,      // ルーレットが回転する時
    UiDebrisSet,     // セットデブリの UI が出てくるとき（機械の音）
    UiCountdown,     // 3・2・1
    UiStart,         // START!

    // ステージ
    TrainWarning,    // SE_01 列車通過前の警告音
    TrainPass,       // SE_02 列車通行
    Explosion,       // SE_03 爆発
    BombFuse,        // SE_04 爆弾が起動したとき
    SpecialDebris,   // SE_05 特殊デブリが出たとき
    RotatingFloor,   // SE_06 回転土台が回るとき
    MeteorFall,      // SE_07 隕石が落ちてくるヒュー
    ConveyorReverse, // SE_09 ベルトコンベアの向きが変わるとき
    GlassCrack,      // SE_10 ガラスにヒビが入る
    GlassBreak,      // SE_11 ガラスが完全に割れる
    GoalAbsorb,      // SE_13 デブリがゴールに吸い込まれる
    DebrisScore,     // SE_14 デブリが消える（得点）
    SceneTransition, // SE_15 シーン遷移
}

/// <summary>
/// **BGM とボタンの効果音を鳴らす係**（2026/11/26・小野田さん）。
/// 説明：Documents/機能ドキュメント/BGMとボタンの効果音.md
///
/// ・**シーンにもプレハブにも置かなくてよい。** 再生を始めたときに自分で1つだけ作り、シーンを切り替えても残る
/// ・音は `Assets/Resources/Audio/`（BGM／SE）から名前で読む。**差し替えるときは、同じ名前のファイルを上書きする**
///
/// | いまの場所 | 流す BGM |
/// | --- | --- |
/// | タイトル（TitleScene）・ロビー（Lobby） | Title（タイトルからロビーへ移っても、途切れずに続く） |
/// | ラウンドの進行役（<see cref="SpaceJunkRound"/>）がいるシーン | Round（ラウンドの間のマップの読み込みでも途切れず、次のラウンドへ続く） |
/// | それ以外（検証シーンなど） | 前に流していた曲のまま（最初からならなし） |
///
/// 曲を替えるときは、前の曲を小さくしてから次の曲を大きくする（<see cref="FadeSeconds"/>）。
/// 全体の音量は <see cref="GameSettings.Volume"/>（<c>AudioListener.volume</c>）がそのまま効く。
/// </summary>
[DisallowMultipleComponent]
public class GameAudio : MonoBehaviour
{
    // ------------------------------------------------------------
    // 音のファイル（Assets/Resources/ からの場所。拡張子は付けない）
    // ------------------------------------------------------------

    private const string TitleBgmPath = "Audio/BGM/TitleTimeTravel";
    private const string RoundBgmPath = "Audio/BGM/RoundContedefees0064";
    private const string ConfirmSePath = "Audio/SE/UiConfirm";
    private const string CancelSePath = "Audio/SE/UiCancel";
    private const string HoverSePath = "Audio/SE/UiHover";

    // ------------------------------------------------------------
    // 大きさと速さ（聞いてみて変えてよい）
    // ------------------------------------------------------------

    /// <summary>BGM の大きさ（0〜1）。効果音より小さめにしてある。</summary>
    private const float BgmVolume = 0.5f;

    /// <summary>決定・キャンセルの効果音の大きさ（0〜1）。</summary>
    private const float ClickVolume = 0.8f;

    /// <summary>重なったときの効果音の大きさ（0〜1）。何度も鳴るので小さめ。</summary>
    private const float HoverVolume = 0.4f;

    /// <summary>曲を替えるとき、前の曲を消す秒数と、次の曲を大きくする秒数（それぞれ）。</summary>
    private const float FadeSeconds = 0.6f;

    /// <summary>決定・キャンセルを鳴らしてから、重なったときの音を鳴らさない秒数（画面が替わって最初のボタンが選ばれたときに重ならないように）。</summary>
    private const float HoverMuteAfterClick = 0.2f;

    /// <summary>シーンを読み込んでから、重なったときの音を鳴らさない秒数（開いた直下にマウスがあっても鳴らさない）。</summary>
    private const float HoverMuteAfterSceneLoad = 0.4f;

    /// <summary>重なったときの音を続けて鳴らす間隔の最短（マウスを速く動かしたときに鳴りすぎないように）。</summary>
    private const float HoverMinInterval = 0.05f;

    private static GameAudio instance;

    private AudioSource bgmSource;
    private AudioSource seSource;

    /// <summary>いま流している（または、これから流す）曲。</summary>
    private BgmTrack currentTrack = BgmTrack.None;

    /// <summary>次に流す曲（いまの曲を消している途中なら、消し終わったらこれにする）。</summary>
    private BgmTrack wantedTrack = BgmTrack.None;

    /// <summary>この時刻（unscaledTime）までは、重なったときの音を鳴らさない。</summary>
    private float hoverMutedUntil;

    private AudioClip titleBgm;
    private AudioClip roundBgm;
    private AudioClip confirmSe;
    private AudioClip cancelSe;
    private AudioClip hoverSe;

    /// <summary>「Audio Listener が無い」の注意を出したシーン（同じシーンで何度も出さないため）。</summary>
    private string warnedNoListenerScene;

    /// <summary>いまのシーンを読み込んだ時刻（unscaledTime）。カメラはあとから作られることがあるので、少し待ってから確かめる。</summary>
    private float sceneLoadedAt;

    /// <summary>シーンを読み込んでから、Audio Listener があるかを確かめるまでの秒数。</summary>
    private const float ListenerCheckDelay = 3f;

    // ------------------------------------------------------------
    // ゲーム中の効果音（GameSe）
    // ------------------------------------------------------------

    /// <summary>ゲーム中の効果音のファイル（Assets/Resources/ からの場所）と大きさ（0〜1）。聞いてみて大きさを変えてよい。</summary>
    private static void EntryOf(GameSe se, out string path, out float volume)
    {
        switch (se)
        {
            case GameSe.PlayerFall: path = "Audio/SE/SE_Player_Fall"; volume = 0.8f; break;
            case GameSe.PlayerStun: path = "Audio/SE/SE_Player_Stun"; volume = 0.7f; break;
            case GameSe.PlayerShoot: path = "Audio/SE/SE_Player_Shoot"; volume = 0.45f; break;
            case GameSe.PlayerAim: path = "Audio/SE/SE_Player_Aim"; volume = 0.35f; break;
            case GameSe.PlayerHit: path = "Audio/SE/SE_Player_Hit"; volume = 0.7f; break;
            case GameSe.UiRoulette: path = "Audio/SE/SE_UI_Roulette"; volume = 0.6f; break;
            case GameSe.UiDebrisSet: path = "Audio/SE/SE_UI_DebrisSet"; volume = 0.6f; break;
            case GameSe.UiCountdown: path = "Audio/SE/SE_UI_Countdown"; volume = 0.7f; break;
            case GameSe.UiStart: path = "Audio/SE/SE_UI_Start"; volume = 0.8f; break;
            case GameSe.TrainWarning: path = "Audio/SE/SE_01_TrainWarning"; volume = 0.5f; break;
            case GameSe.TrainPass: path = "Audio/SE/SE_02_TrainPass"; volume = 0.6f; break;
            case GameSe.Explosion: path = "Audio/SE/SE_03_Explosion"; volume = 0.8f; break;
            case GameSe.BombFuse: path = "Audio/SE/SE_04_BombFuse"; volume = 0.7f; break;
            case GameSe.SpecialDebris: path = "Audio/SE/SE_05_SpecialDebris"; volume = 0.7f; break;
            case GameSe.RotatingFloor: path = "Audio/SE/SE_06_RotatingFloor"; volume = 0.45f; break;
            case GameSe.MeteorFall: path = "Audio/SE/SE_07_MeteorFall"; volume = 0.8f; break;
            case GameSe.ConveyorReverse: path = "Audio/SE/SE_09_ConveyorReverse"; volume = 0.6f; break;
            case GameSe.GlassCrack: path = "Audio/SE/SE_10_GlassCrack"; volume = 0.7f; break;
            case GameSe.GlassBreak: path = "Audio/SE/SE_11_GlassBreak"; volume = 0.8f; break;
            case GameSe.GoalAbsorb: path = "Audio/SE/SE_13_GoalAbsorb"; volume = 0.5f; break;
            case GameSe.DebrisScore: path = "Audio/SE/SE_14_DebrisScore"; volume = 0.7f; break;
            case GameSe.SceneTransition: path = "Audio/SE/SE_15_SceneTransition"; volume = 0.6f; break;
            default: path = null; volume = 0f; break;
        }
    }

    /// <summary>同じ効果音を続けて鳴らす間隔の最短（同じフレームに何個も鳴ってうるさくならないように）。</summary>
    private const float SameSeMinInterval = 0.06f;

    /// <summary>途中で止める効果音を、止める前に小さくしていく秒数。</summary>
    private const float TimedSeFadeSeconds = 0.25f;

    /// <summary>途中で止める効果音を同時に鳴らせる数（これより多いと、一番古いものを止めて使い回す）。</summary>
    private const int TimedSeMax = 8;

    private AudioClip[] gameSeClips;
    private bool[] gameSeLoaded;
    private float[] gameSeLastPlayed;

    /// <summary>途中で止める効果音を鳴らしている AudioSource と、止める時刻・元の大きさ。</summary>
    private class TimedSe
    {
        public AudioSource Source;
        public float StopAt;
        public float BaseVolume;
        public float StartedAt;
    }

    private readonly System.Collections.Generic.List<TimedSe> timedSes = new System.Collections.Generic.List<TimedSe>();

    /// <summary>
    /// ゲーム中の効果音を鳴らす。<paramref name="maxSeconds"/> が 0 より大きいと、その秒数で小さくして止める（長い音を途中で切るとき）。
    /// 係がまだ無いとき（エディタで再生していないときなど）は何もしない。
    /// </summary>
    public static void PlaySe(GameSe se, float maxSeconds = 0f)
    {
        if (instance != null)
        {
            instance.PlayGameSe(se, maxSeconds);
        }
    }

    /// <summary>前に渡された、始まりの演出の大きな文字。</summary>
    private static string countdownLabel;

    /// <summary>
    /// 始まりの演出の大きな文字を毎フレーム渡す。「3」「2」「1」に変わったらカウントの音、「START!」に変わったらスタートの音を鳴らす。
    /// （演出の種類が3つあり、どれも 3・2・1・START! を出すので、ここにまとめた）
    /// </summary>
    public static void PlayCountdownLabel(string big)
    {
        if (big == countdownLabel)
        {
            return;
        }

        countdownLabel = big;
        if (big == "START!")
        {
            PlaySe(GameSe.UiStart);
        }
        else if (big == "3" || big == "2" || big == "1")
        {
            PlaySe(GameSe.UiCountdown);
        }
    }

    private void PlayGameSe(GameSe se, float maxSeconds)
    {
        int index = (int)se;
        if (gameSeClips == null)
        {
            int count = System.Enum.GetValues(typeof(GameSe)).Length;
            gameSeClips = new AudioClip[count];
            gameSeLoaded = new bool[count];
            gameSeLastPlayed = new float[count];
            for (int i = 0; i < count; i++)
            {
                gameSeLastPlayed[i] = float.NegativeInfinity;
            }
        }

        if (index < 0 || index >= gameSeClips.Length)
        {
            return;
        }

        EntryOf(se, out string path, out float volume);
        if (!gameSeLoaded[index])
        {
            gameSeLoaded[index] = true;
            gameSeClips[index] = path != null ? Load(path) : null;
        }

        AudioClip clip = gameSeClips[index];
        float now = Time.unscaledTime;
        if (clip == null || now - gameSeLastPlayed[index] < SameSeMinInterval)
        {
            return;
        }

        gameSeLastPlayed[index] = now;

        if (maxSeconds <= 0f || maxSeconds >= clip.length)
        {
            seSource.PlayOneShot(clip, volume);
            return;
        }

        TimedSe slot = FreeTimedSe();
        slot.Source.clip = clip;
        slot.Source.volume = volume;
        slot.Source.Play();
        slot.BaseVolume = volume;
        slot.StartedAt = now;
        slot.StopAt = now + maxSeconds;
    }

    /// <summary>空いている「途中で止める効果音」の入れ物を返す。足りなければ作り、上限なら一番古いものを使い回す。</summary>
    private TimedSe FreeTimedSe()
    {
        TimedSe oldest = null;
        foreach (TimedSe slot in timedSes)
        {
            if (!slot.Source.isPlaying)
            {
                return slot;
            }

            if (oldest == null || slot.StartedAt < oldest.StartedAt)
            {
                oldest = slot;
            }
        }

        if (timedSes.Count < TimedSeMax || oldest == null)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            TimedSe created = new TimedSe { Source = source };
            timedSes.Add(created);
            return created;
        }

        oldest.Source.Stop();
        return oldest;
    }

    /// <summary>止める時刻が近づいた効果音を小さくし、時刻が来たら止める。</summary>
    private void UpdateTimedSes()
    {
        float now = Time.unscaledTime;
        foreach (TimedSe slot in timedSes)
        {
            if (!slot.Source.isPlaying)
            {
                continue;
            }

            float left = slot.StopAt - now;
            if (left <= 0f)
            {
                slot.Source.Stop();
            }
            else if (left < TimedSeFadeSeconds)
            {
                slot.Source.volume = slot.BaseVolume * (left / TimedSeFadeSeconds);
            }
        }
    }

    /// <summary>
    /// ボタンの効果音を鳴らす。係がまだ無いとき（エディタで再生していないときなど）は何もしない。
    /// </summary>
    public static void PlayUi(UiSound sound)
    {
        if (instance != null)
        {
            instance.PlayUiSound(sound);
        }
    }

    /// <summary>ドメインの再読み込みを切っていても、前の再生の係が残らないようにする。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        countdownLabel = null;
    }

    /// <summary>再生を始めたら、最初のシーンを読む前に係を1つ作る。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateOnStart()
    {
        if (instance != null)
        {
            return;
        }

        GameObject holder = new GameObject("GameAudio");
        DontDestroyOnLoad(holder);
        holder.AddComponent<GameAudio>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.spatialBlend = 0f;
        bgmSource.volume = 0f;
        bgmSource.ignoreListenerPause = true;

        seSource = gameObject.AddComponent<AudioSource>();
        seSource.playOnAwake = false;
        seSource.loop = false;
        seSource.spatialBlend = 0f;
        seSource.ignoreListenerPause = true;

        titleBgm = Load(TitleBgmPath);
        roundBgm = Load(RoundBgmPath);
        confirmSe = Load(ConfirmSePath);
        cancelSe = Load(CancelSePath);
        hoverSe = Load(HoverSePath);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        sceneLoadedAt = Time.unscaledTime;
        hoverMutedUntil = Mathf.Max(hoverMutedUntil, Time.unscaledTime + HoverMuteAfterSceneLoad);
    }

    private static AudioClip Load(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null)
        {
            Debug.LogWarning($"[音] Resources/{path} が見つからないので、この音は鳴りません。");
        }

        return clip;
    }

    private void Update()
    {
        BgmTrack decided = DecideTrack();
        if (decided != wantedTrack)
        {
            wantedTrack = decided;
        }

        UpdateBgm();
        UpdateTimedSes();
        WarnIfNoListener();
    }

    /// <summary>いまの場所から、流す曲を決める（上の表）。決められないときは今の曲のまま。</summary>
    private BgmTrack DecideTrack()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == TitleScreen.TitleSceneName || scene == TitleScreen.LobbySceneName)
        {
            return BgmTrack.Title;
        }

        if (SpaceJunkRound.Current != null)
        {
            return BgmTrack.Round;
        }

        return wantedTrack;
    }

    /// <summary>曲を替える途中なら、小さくする／大きくする。同じ曲のままなら何もしない。</summary>
    private void UpdateBgm()
    {
        float step = FadeSeconds > 0f ? Time.unscaledDeltaTime * BgmVolume / FadeSeconds : BgmVolume;

        if (wantedTrack != currentTrack)
        {
            // 前の曲を小さくして、消えたら次の曲にする
            if (bgmSource.isPlaying && bgmSource.volume > 0f)
            {
                bgmSource.volume = Mathf.Max(0f, bgmSource.volume - step);
                return;
            }

            currentTrack = wantedTrack;
            bgmSource.Stop();
            bgmSource.clip = ClipOf(currentTrack);
            bgmSource.volume = 0f;
            if (bgmSource.clip != null)
            {
                bgmSource.Play();
            }

            return;
        }

        if (bgmSource.isPlaying && bgmSource.volume < BgmVolume)
        {
            bgmSource.volume = Mathf.Min(BgmVolume, bgmSource.volume + step);
        }
    }

    private AudioClip ClipOf(BgmTrack track)
    {
        switch (track)
        {
            case BgmTrack.Title: return titleBgm;
            case BgmTrack.Round: return roundBgm;
            default: return null;
        }
    }

    private void PlayUiSound(UiSound sound)
    {
        float now = Time.unscaledTime;
        switch (sound)
        {
            case UiSound.Confirm:
            case UiSound.Cancel:
                AudioClip clip = sound == UiSound.Confirm ? confirmSe : cancelSe;
                if (clip != null)
                {
                    seSource.PlayOneShot(clip, ClickVolume);
                }

                hoverMutedUntil = Mathf.Max(hoverMutedUntil, now + HoverMuteAfterClick);
                break;

            case UiSound.Hover:
                if (hoverSe == null || now < hoverMutedUntil)
                {
                    return;
                }

                seSource.PlayOneShot(hoverSe, HoverVolume);
                hoverMutedUntil = now + HoverMinInterval;
                break;
        }
    }

    /// <summary>音を聞く係（Audio Listener）がシーンに1つも無いと何も聞こえないので、そのときはコンソールに1回だけ知らせる。</summary>
    private void WarnIfNoListener()
    {
        if (currentTrack == BgmTrack.None)
        {
            return;
        }

        string scene = SceneManager.GetActiveScene().name;
        if (scene == warnedNoListenerScene || Time.unscaledTime - sceneLoadedAt < ListenerCheckDelay ||
            Time.frameCount % 60 != 0)
        {
            return;
        }

        if (FindAnyObjectByType<AudioListener>() == null)
        {
            warnedNoListenerScene = scene;
            Debug.LogWarning($"[音] シーン「{scene}」に Audio Listener がありません。BGM・効果音が聞こえません（カメラに Audio Listener を付けてください）。");
        }
    }
}
