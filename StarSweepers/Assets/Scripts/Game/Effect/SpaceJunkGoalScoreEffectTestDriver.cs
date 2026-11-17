using UnityEngine;

/// <summary>
/// ゴールの得点の演出の検証シーン専用。ゴールにチームを割り当て、決まった間隔で「得点が入った」ことにする。
/// 説明は Documents/スクリプトの説明/Game/Effect/SpaceJunkGoalScoreEffect.md
/// </summary>
public class SpaceJunkGoalScoreEffectTestDriver : MonoBehaviour
{
    [Tooltip("得点が入ったことにするゴール（順番に回す）")]
    [SerializeField] private SpaceJunkGoal[] goals;

    [Tooltip("それぞれのゴールの持ち主のチーム（0=青 1=赤 2=緑 3=黄）")]
    [SerializeField] private int[] teams = { 0, 1 };

    [Tooltip("何秒ごとに得点が入ったことにするか")]
    [SerializeField] private float interval = 1.2f;

    private float timer;
    private int next;

    private void Start()
    {
        for (int i = 0; i < goals.Length; i++)
        {
            if (goals[i] != null)
            {
                goals[i].ApplyOwnerTeam(teams.Length > 0 ? teams[i % teams.Length] : i);
            }
        }

        // 再生してすぐ1回目が見えるように
        timer = interval;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer < interval || goals.Length == 0)
        {
            return;
        }

        timer = 0f;
        SpaceJunkGoal goal = goals[next % goals.Length];
        next++;
        if (goal != null)
        {
            goal.ShowScored();
        }
    }
}
