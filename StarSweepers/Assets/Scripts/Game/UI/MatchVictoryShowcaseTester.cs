using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// **勝利演出（メンバー紹介）を、試合をせずに試すための部品。** 検証シーン `Scenes/Test/MatchVictoryShowcaseTest` に置いてある。
/// 再生すると、仮の名前のメンバーで演出が始まる。スペースキーで最初から、1〜4 キーで勝ったチームを変えて流し直す。
/// </summary>
public class MatchVictoryShowcaseTester : MonoBehaviour
{
    [Tooltip("体のモデルを借りるプレハブ（SweeperPlayer）")]
    [SerializeField] private GameObject playerPrefab;

    [Tooltip("勝ったチーム（0 = 青、1 = 赤、2 = 緑、3 = 黄）")]
    [Range(0, 3)]
    [SerializeField] private int winnerTeam;

    [Tooltip("紹介するメンバーの名前（この数だけ並ぶ）")]
    [SerializeField] private string[] memberNames = { "きらめくほうき星", "まわるスイーパー", "ゆうかんなネジ", "しずかな流れ星" };

    [Tooltip("ON：終わったら少し待って、もう一度流す")]
    [SerializeField] private bool loop = true;

    /// <summary>チームごとの服の色（SweeperPlayer と同じ。赤はモデルの元の色）。</summary>
    private static readonly Color[] SuitColors =
    {
        new Color(0.192f, 0.333f, 0.808f),
        new Color(0.808f, 0.333f, 0.192f),
        new Color(0.250f, 0.640f, 0.250f),
        new Color(0.860f, 0.700f, 0.190f),
    };

    private MatchVictoryShowcase showcase;
    private Transform sources;
    private float replayAt = -1f;

    private void Start()
    {
        PlayShowcase();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) PlayShowcase();
            if (keyboard.digit1Key.wasPressedThisFrame) PlayShowcase(0);
            if (keyboard.digit2Key.wasPressedThisFrame) PlayShowcase(1);
            if (keyboard.digit3Key.wasPressedThisFrame) PlayShowcase(2);
            if (keyboard.digit4Key.wasPressedThisFrame) PlayShowcase(3);
        }

        if (replayAt > 0f && Time.unscaledTime >= replayAt)
        {
            PlayShowcase();
        }
    }

    private void PlayShowcase(int team)
    {
        winnerTeam = team;
        PlayShowcase();
    }

    private void PlayShowcase()
    {
        replayAt = -1f;
        if (showcase != null) Destroy(showcase.gameObject);
        showcase = MatchVictoryShowcase.PlayNew(winnerTeam, CreateMembers(), () =>
        {
            if (loop) replayAt = Time.unscaledTime + 1.5f;
        });
    }

    /// <summary>
    /// プレハブから体のモデルだけを写し、チームの服の色を塗って、見えない場所にしまっておく。
    /// 演出はこれをさらに写して並べる（試合ではプレイヤーの体を写すのと同じ形）。
    /// </summary>
    public List<MatchVictoryShowcase.Member> CreateMembers()
    {
        if (sources != null) Destroy(sources.gameObject);
        sources = new GameObject("ShowcaseSources").transform;
        sources.gameObject.SetActive(false);

        Transform model = playerPrefab != null ? MatchVictoryShowcase.FindModel(playerPrefab.transform) : null;
        List<MatchVictoryShowcase.Member> members = new List<MatchVictoryShowcase.Member>();

        foreach (string memberName in memberNames)
        {
            Transform copy = null;
            if (model != null)
            {
                copy = Instantiate(model.gameObject, sources).transform;
                foreach (CharacterColorController painter in copy.GetComponentsInChildren<CharacterColorController>(true))
                {
                    if (winnerTeam == 1) painter.ResetToDefault();
                    else painter.SetCustomColor(SuitColors[Mathf.Clamp(winnerTeam, 0, SuitColors.Length - 1)]);
                }
            }

            members.Add(new MatchVictoryShowcase.Member { Name = memberName, Model = copy });
        }

        if (model == null)
        {
            Debug.LogWarning("[VICTORY] Player Prefab が空か、体のモデルが見つかりません。名前と「WIN!」だけで流します。");
        }

        return members;
    }
}
