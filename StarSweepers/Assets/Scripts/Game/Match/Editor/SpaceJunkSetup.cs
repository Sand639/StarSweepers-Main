using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **宇宙ごみのマップ（ステージ）を点検するツールと、ロビー・タイトルの場所。**
///
/// Unityのメニュー「Tools &gt; StarSweepers &gt; 宇宙ごみのマップを点検する（作り直さない）」から実行する。
/// マップの一覧（SpaceJunkMapList.asset）に入っているマップを順に開いて、
/// 進行役・ゴール・スポナー・画面がそろっているかを確かめる。
///
/// もとは「釣りのマップをコピーして宇宙ごみ用に差し替える」ツールだったが、コピー元の釣りのマップがこのリポジトリに無く、
/// その部分はもう動かなかったので、2026/11/24 に点検だけを残した。マップの作り方は `Documents/宇宙ごみのマップの作り方.md`。
///
/// ※ Editor フォルダにあるため、ゲームのビルドには含まれない。
/// </summary>
public static class SpaceJunkSetup
{
    public const string LobbySceneName = "Lobby";

    /// <summary>
    /// シーン全体の置き場所。この中の `MainLoop`（タイトルとロビー）・`Stage`（STAGE_〜）・`HansStages` に分かれている。
    /// 使うマップの一覧（SpaceJunkMapList.asset）はこの直下。
    /// </summary>
    public const string MapSceneFolder = "Assets/Scenes";

    /// <summary>タイトルとロビーのシーンの置き場所。</summary>
    public const string MainSceneFolder = MapSceneFolder + "/MainLoop";

    /// <summary>ロビーがどこにも無いときに使う場所。</summary>
    private const string DefaultLobbyScenePath = MainSceneFolder + "/" + LobbySceneName + ".unity";

    /// <summary>
    /// **ロビーのシーンの置き場所。名前（Lobby）で探す**ので、Unity の中で移動しても追いかける。
    /// 見つからなければ <see cref="DefaultLobbyScenePath"/>。
    /// </summary>
    public static string LobbyScenePath => FindScenePathByName(LobbySceneName) ?? DefaultLobbyScenePath;

    /// <summary>タイトルのシーンの名前（ゲームはここから始まる）。</summary>
    public const string TitleSceneName = "TitleScene";

    /// <summary>**タイトルのシーンの置き場所。名前（TitleScene）で探す。** 見つからなければ null。</summary>
    public static string TitleScenePath => FindScenePathByName(TitleSceneName);

    /// <summary>
    /// **名前がぴったり同じシーンを、プロジェクト全体から探す。** 見つからなければ null。
    /// `FindAssets` は名前の一部が合うものも返すことがあるので、ファイル名を見比べて、ぴったり同じものだけを返す。
    /// </summary>
    private static string FindScenePathByName(string sceneName)
    {
        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// **すでにあるマップを、作り直さずに点検するだけのメニュー。**
    ///
    /// 「ゴールが足りない」「進行役がいない」といった、遊ばないと気づきにくい抜けを探す。
    /// 手でマップを直したあとの確認にも使える。
    /// </summary>
    [MenuItem("Tools/StarSweepers/宇宙ごみのマップを点検する（作り直さない）")]
    public static void VerifyAllMaps()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        int checkedCount = 0;

        foreach (string path in SpaceJunkMapListSetup.ScenePaths())
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                continue;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            VerifyMap(path);
            checkedCount++;
        }

        if (checkedCount == 0)
        {
            Debug.LogWarning("[JUNK] 点検できる宇宙ごみのマップがありません（マップの一覧が空です）。");
            return;
        }

        // どの方角にゴールがあるかを一覧に控える（ロビーでチーム数に合わないマップを外すため）
        SpaceJunkMapListSetup.RefreshGoalInfo();

        Debug.Log($"[JUNK] 宇宙ごみのマップ {checkedCount} 個を点検しました。" +
                  "**赤いエラーが出ていなければ、全部そろっています。**");
    }

    /// <summary>
    /// **開いているマップに、遊ぶのに必要な部品がそろっているかを確かめる。**
    /// 足りないまま気づかずに遊ぶと、**あるチームだけ点が入らない**といった分かりにくい形で出るので、ここで知らせる。
    /// </summary>
    internal static void VerifyMap(string scenePath)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        // 見つけた問題を、この順に並べて最後にまとめて出す（進行役 → ゴール → スポナー → 画面）
        List<string> problems = new List<string>();

        int rounds = FindAllInScene<SpaceJunkRound>().Length;

        if (rounds != 1)
        {
            problems.Add($"ラウンドの進行役（SpaceJunkRound）が {rounds} 個です。**1個必要**");
        }

        SpaceJunkGoal[] goals = FindAllInScene<SpaceJunkGoal>();

        // **ゴールは4つでなくてもよい**（1対1のマップは2つ。2026/9/30）。
        // 何チームまで遊べるかは、どの方角にゴールがあるかで決まり、ロビーはそれに合わせて候補を絞る
        if (goals.Length == 0)
        {
            problems.Add("ゴールが1つもありません。**どのチームも点を入れられません**");
        }

        // ゴールの番号（北=0/東=1/南=2/西=3）が重複していないか
        int goalMask = CheckGoalIndices(goals, problems);

        int spawners = FindAllInScene<SpaceJunkSpawner>().Length;

        if (spawners != 1)
        {
            problems.Add($"素材のスポナー（SpaceJunkSpawner）が {spawners} 個です。**1個必要**");
        }

        int uis = FindAllInScene<SpaceJunkMatchUI>().Length;

        if (uis != 1)
        {
            problems.Add($"プレイ中の画面（SpaceJunkMatchUI）が {uis} 個です。**1個必要**");
        }

        if (goals.Length > 0 && SpaceJunkTeams.MaxPlayableTeams(goalMask) < 2)
        {
            // エラーにはしない（1人で試すだけのマップもありうる）。ただ、対戦には使えないので知らせる
            Debug.LogWarning($"[JUNK] {name} … {SpaceJunkMapListSetup.DescribeGoals(goalMask)}。" +
                             "**2チームの対戦には使えません**（ゴールが2つ以上要る）。");
        }

        if (problems.Count == 0)
        {
            Debug.Log($"[JUNK] {name} … 点検OK（{SpaceJunkMapListSetup.DescribeGoals(goalMask)}。進行役・スポナー・画面がそろっています）");
            return;
        }

        Debug.LogError(
            $"[JUNK] **{name} は、このままでは正しく遊べません。**\n・" +
            string.Join("\n・", problems) +
            "\n\nUnity でこのシーンを開いて直してください。" +
            "ゴールを足すときは、**今あるマップのゴール（Goal_〜）をコピーして**貼り付け、" +
            "その `SpaceJunkGoal` の Goal Index を空いている番号にします" +
            "（4つなら 0＝北・1＝東・2＝南・3＝西。4つ未満なら、番号の小さい順に 青・赤・緑… のゴールになる）。");
    }

    /// <summary>
    /// ゴールの番号（北=0/東=1/南=2/西=3）が、範囲の外になっていないか・重複していないかを調べ、
    /// 見つけた問題を problems に書き足す。
    /// 戻り値は、使われている番号をまとめた数（北=1 / 東=2 / 南=4 / 西=8 を足したもの。範囲の外の番号は入れない）。
    /// </summary>
    private static int CheckGoalIndices(SpaceJunkGoal[] goals, List<string> problems)
    {
        bool[] used = new bool[SpaceJunkTeams.GoalCount];

        foreach (SpaceJunkGoal goal in goals)
        {
            int index = goal.GoalIndex;

            if (index < 0 || index >= used.Length)
            {
                problems.Add($"ゴール「{goal.name}」の Goal Index が {index} です（0〜{used.Length - 1} のはず）");
                continue;
            }

            if (used[index])
            {
                problems.Add($"ゴールの Goal Index {index}（{SpaceJunkTeams.GoalPlaceName(index)}）が重複しています");
            }

            used[index] = true;
        }

        int goalMask = 0;
        for (int i = 0; i < used.Length; i++)
        {
            if (used[i])
            {
                goalMask |= 1 << i;
            }
        }

        return goalMask;
    }

    /// <summary>開いているシーンから、その部品を**隠れている物も含めて**全部探す（並び順は決まっていない）。</summary>
    private static T[] FindAllInScene<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }
}
