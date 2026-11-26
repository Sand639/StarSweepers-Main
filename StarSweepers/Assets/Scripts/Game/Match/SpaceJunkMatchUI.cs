using UnityEngine;

/// <summary>
/// **ラウンド中の画面。** マップ（ステージ）のシーンに1つ置く。
///
/// 出しているもの：
///
///   ・**残り時間**（残り10秒を切ると赤くなる。チームの得点バー <see cref="TeamScoreBarUI"/> を置いたステージでは、バーの丸に任せて出さない）
///   ・**復活までの秒数**
///   ・決着後 … **このラウンドを取ったチーム**（引き分けならその旨）
///   ・試合が終わったら … **試合に勝ったチーム**
///
/// 2026/11/25 に、画面の左のチームごとの行（チーム名・得点・勝ち数・素材の印・お題の進み具合）と、
/// 落ちたときの戻り方・カメラ切り替えの案内、真ん中の上の「第 ○ ラウンド（○ 本先取）」を消した（大槻さん）。
///
/// 数える／勝敗を決めるのは <see cref="SpaceJunkRound"/> と <see cref="SpaceJunkSession"/>（ホスト）。
/// こちらはその値を出すだけで、判定は一切していない。
///
/// フォントの素材を用意しなくても出せるように `OnGUI` で描いている。
/// **本番の画面はあとで作り直す前提。**
/// </summary>
public class SpaceJunkMatchUI : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("OFFにすると何も表示しない")]
    [SerializeField] private bool showUi = true;

    [Tooltip("文字の大きさ。**窓が小さいときは自動で縮む**ので、これは上限として使われる")]
    [Range(1f, 4f)]
    [SerializeField] private float uiScale = 1.6f;

    [Tooltip("残り時間がこの秒数を切ったら赤くする")]
    [SerializeField] private float warnSeconds = 10f;

    /// <summary>描く基準の幅（この幅に収まるように縮める）。</summary>
    private const float BaseWidth = 520f;

    // 文字の見た目（GUI.skin は OnGUI の中でしか使えないので、最初の OnGUI で作る。PrepareStyles）

    /// <summary>残り時間の大きな数字。</summary>
    private GUIStyle timerStyle;

    /// <summary>真ん中に出す大きな文字（決着・復活まで）。</summary>
    private GUIStyle resultStyle;

    // チームの得点バーが出ているか（出ていれば、残り時間はバーの丸に任せる）
    private bool timerOnBar;

    private void OnGUI()
    {
        SpaceJunkRound round = SpaceJunkRound.Current;

        if (!showUi || round == null || RoundResultPlayback.IsPresenting)
        {
            return;
        }

        PrepareStyles();

        // 拡大率：設定の倍率（1920×1080 基準）を上限に、窓が小さいときは「幅 520・高さ 360」が収まるまで縮める。
        // ただし 0.6 倍より小さくはしない（文字が読めなくなるため）
        float scale = Mathf.Min(DraggableGuiPanel.FitScale(uiScale), Screen.width / BaseWidth, Screen.height / 360f);
        scale = Mathf.Max(0.6f, scale);

        // ここから下は、拡大率で割った「縮める前の大きさ」で位置を書く（GUI.matrix がまとめて拡大する）
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float viewWidth = Screen.width / scale;

        // チームの得点バーが画面の上を使っているか
        timerOnBar = TeamScoreBarUI.OccupiedScreenHeight > 0f;

        DrawTimer(round, viewWidth);

        if (round.Phase == SpaceJunkRoundPhase.Result)
        {
            DrawResult(round, viewWidth, Screen.height / scale);
        }
        else if (SpaceJunkPlayerSetup.LocalRespawnRemaining > 0f)
        {
            // 戻った直後の、動けない時間（復活時間）
            float viewHeight = Screen.height / scale;
            int seconds = Mathf.CeilToInt(SpaceJunkPlayerSetup.LocalRespawnRemaining);
            GUI.Label(new Rect(0f, viewHeight * 0.5f - 60f, viewWidth, 60f), $"復活まで {seconds}", resultStyle);
        }

        GUI.matrix = saved;
    }

    private void PrepareStyles()
    {
        if (timerStyle == null)
        {
            timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
        }

        if (resultStyle == null)
        {
            resultStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
        }
    }

    /// <summary>画面の上に、残り時間を出す。</summary>
    private void DrawTimer(SpaceJunkRound round, float viewWidth)
    {
        // 残り時間は、チームの得点バーの丸に出ているので、ここでは出さない
        if (timerOnBar)
        {
            return;
        }

        float remaining = round.RemainingSeconds;
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        Color saved = GUI.color;
        GUI.color = remaining <= warnSeconds ? new Color(1f, 0.45f, 0.4f) : Color.white;

        GUI.Label(new Rect(0f, 30f, viewWidth, 40f), $"{minutes}:{seconds:00}", timerStyle);

        GUI.color = saved;
    }

    /// <summary>決着後の表示。</summary>
    private void DrawResult(SpaceJunkRound round, float viewWidth, float viewHeight)
    {
        SpaceJunkSession session = SpaceJunkSession.Current;

        string text;
        Color color = Color.white;

        if (session != null && session.State == SpaceJunkMatchState.MatchOver)
        {
            int winner = session.MatchWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} の勝ち！\n（{session.RoundWinsOf(winner)} 本先取）\n\nまもなくロビーへ戻ります"
                : "試合終了";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }
        }
        else
        {
            int winner = round.RoundWinner;
            text = winner >= 0
                ? $"{SpaceJunkTeams.TeamName(winner)} がラウンドを取りました\n\nまもなく次のマップへ"
                : "引き分け\nこのラウンドは誰も取りません\n\nまもなく次のマップへ";

            if (winner >= 0)
            {
                color = SpaceJunkTeams.TeamColor(winner);
            }

            // 得点制なら、各チームの得点も出す（何点差だったかが分かるように）
            // 「まもなく次のマップへ」の前の空行に、得点の一行を差し込んでいる
            if (round.Rule == SpaceJunkWinRule.Score)
            {
                text = text.Replace("\n\nまもなく", $"\n{ScoreSummary(round)}\n\nまもなく");
            }
        }

        // 画面の真ん中に、幅 440・高さ 230 の枠（左上の位置は、真ん中から幅・高さの半分ずつ戻した所）
        Rect box = new Rect(viewWidth * 0.5f - 220f, viewHeight * 0.5f - 115f, 440f, 230f);

        GUI.Box(box, GUIContent.none);

        Color saved = GUI.color;
        GUI.color = color;
        GUI.Label(box, text, resultStyle);
        GUI.color = saved;
    }

    /// <summary>「青 12点／赤 8点」のような、全チームの得点の一行。</summary>
    private static string ScoreSummary(SpaceJunkRound round)
    {
        int teamCount = SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;
        var parts = new System.Collections.Generic.List<string>();

        for (int team = 0; team < teamCount; team++)
        {
            parts.Add($"{SpaceJunkTeams.TeamName(team).Replace("チーム", string.Empty)} {round.ScoreOf(team)}点");
        }

        return string.Join("／", parts);
    }
}
