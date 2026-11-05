using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「ラウンドイベント：デブリセット」（SpaceJunkRoundEvents.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // デブリセットの状態
    // ------------------------------------------------------------

    /// <summary>お題のデブリのいちばん多い数。**設定でこれより大きくしても、ここで切り詰める。**</summary>
    public const int MaxSetSize = 8;

    /// <summary>
    /// お題の「必要な数」。**チーム × デブリの種類**で並べてある
    /// （<c>team * SpaceJunkMaterials.Count + 種類</c>）。
    /// </summary>
    private readonly NetworkList<int> setRequired = new NetworkList<int>();

    /// <summary>お題の「もう入れた数」。並びは <see cref="setRequired"/> と同じ。</summary>
    private readonly NetworkList<int> setProgress = new NetworkList<int>();

    /// <summary>チームごとの、いまのお題をそろえたときのボーナス。</summary>
    private readonly NetworkList<int> setBonus = new NetworkList<int>();

    /// <summary>チームごとの、お題をそろえた回数。</summary>
    private readonly NetworkList<int> setsCompleted = new NetworkList<int>();

    /// <summary>
    /// お題の中身を**並び順のまま**持つ。チームごとに <see cref="MaxSetSize"/> 個ぶんの枠
    /// （<c>team * MaxSetSize + 何番目</c>）。空いている枠は -1。
    /// </summary>
    private readonly NetworkList<int> setSequence = new NetworkList<int>();

    /// <summary>順番どおりのとき、チームごとに「何個目まで入れたか」。</summary>
    private readonly NetworkList<int> setStep = new NetworkList<int>();

    /// <summary>お題を順番どおりに入れる必要があるか。**ホストの設定を全員へ配る。**</summary>
    private readonly NetworkVariable<bool> setOrdered = new NetworkVariable<bool>(false);

    /// <summary>お題のうち、何個入れたらそろったことになるか。**ホストの設定を全員へ配る。**</summary>
    private readonly NetworkVariable<int> setGoal = new NetworkVariable<int>(3);

    /// <summary>このイベントで使うお題の個数（全チーム同じ。ホストだけが使う）。</summary>
    private int serverSetSize;

    /// <summary>お題を順番どおりに入れる必要があるか。</summary>
    public bool SetOrdered => setOrdered.Value;

    /// <summary>そのチームが、いまのお題で何個入れたらそろうか（お題の個数より多くはならない）。</summary>
    public int SetGoalOf(int team)
    {
        return Mathf.Min(setGoal.Value, SetSizeOf(team));
    }

    /// <summary>そのチームが、いまのお題でもう何個入れたか。</summary>
    public int SetDoneOf(int team)
    {
        if (setOrdered.Value)
        {
            return SetStepOf(team);
        }

        int done = 0;
        for (int k = 0; k < SpaceJunkMaterials.Count; k++)
        {
            done += SetProgressOf(team, SpaceJunkMaterials.FromIndex(k));
        }
        return done;
    }

    /// <summary>そのチームのお題の個数。</summary>
    public int SetSizeOf(int team)
    {
        int size = 0;
        while (size < MaxSetSize && SetItemAt(team, size) >= 0)
        {
            size++;
        }
        return size;
    }

    /// <summary>そのチームのお題の n 番目の種類（番号）。無ければ -1。</summary>
    public int SetItemAt(int team, int n)
    {
        if (team < 0 || team >= SpaceJunkTeams.MaxTeams || n < 0 || n >= MaxSetSize)
        {
            return -1;
        }

        int i = team * MaxSetSize + n;
        return i < setSequence.Count ? setSequence[i] : -1;
    }

    /// <summary>順番どおりのとき、そのチームが何個目まで入れたか。</summary>
    public int SetStepOf(int team)
    {
        return team >= 0 && team < setStep.Count ? setStep[team] : 0;
    }

    /// <summary>お題で、そのチームがその種類をいくつ入れる必要があるか。</summary>
    public int SetRequiredOf(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);
        return i >= 0 && i < setRequired.Count ? setRequired[i] : 0;
    }

    /// <summary>お題で、そのチームがその種類をもういくつ入れたか。</summary>
    public int SetProgressOf(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);
        return i >= 0 && i < setProgress.Count ? setProgress[i] : 0;
    }

    /// <summary>そのチームの、いまのお題をそろえたときのボーナス。</summary>
    public int SetBonusOf(int team)
    {
        return team >= 0 && team < setBonus.Count ? setBonus[team] : 0;
    }

    /// <summary>そのチームが、このラウンドでお題をそろえた回数。</summary>
    public int SetsCompletedOf(int team)
    {
        return team >= 0 && team < setsCompleted.Count ? setsCompleted[team] : 0;
    }

    /// <summary><see cref="setRequired"/> / <see cref="setProgress"/> の中での、そのチーム・その種類の位置。チームが範囲外なら -1。</summary>
    private static int SetIndex(int team, SpaceJunkMaterialKind kind)
    {
        if (team < 0 || team >= SpaceJunkTeams.MaxTeams)
        {
            return -1;
        }

        return team * SpaceJunkMaterials.Count + (int)kind;
    }

    // ------------------------------------------------------------
    // デブリセット
    // ------------------------------------------------------------

    /// <summary>
    /// そのチームのお題を、新しく引き直す（進み具合は0に戻る）。
    /// **個数はイベントの始まりに決めた数（全チーム同じ）**、中身はチームごとにばらばら。
    /// </summary>
    private void ServerDrawDebrisSet(int team)
    {
        SpaceJunkEventSettings settings = EventSettings;
        int size = Mathf.Clamp(serverSetSize, 1, MaxSetSize);

        for (int k = 0; k < SpaceJunkMaterials.Count; k++)
        {
            int i = SetIndex(team, SpaceJunkMaterials.FromIndex(k));
            setRequired[i] = 0;
            setProgress[i] = 0;
        }

        for (int n = 0; n < MaxSetSize; n++)
        {
            int kindIndex = n < size ? SpaceJunkMaterials.RandomUsedIndex() : -1;
            setSequence[team * MaxSetSize + n] = kindIndex;

            if (kindIndex >= 0)
            {
                int i = SetIndex(team, SpaceJunkMaterials.FromIndex(kindIndex));
                setRequired[i] = setRequired[i] + 1;
            }
        }

        setStep[team] = 0;
        setBonus[team] = Mathf.Max(0, settings.debrisSetBonus);
    }

    /// <summary>
    /// お題のデブリなら進める。**お題のうち <see cref="SetGoalOf"/> 個（初期値3個）入れたら**ボーナスを返して、お題を引き直す。
    /// 順番どおりのときは、**お題の次の1個と同じ種類のときだけ**進める。
    /// </summary>
    private int ServerDebrisSetCollect(int team, SpaceJunkMaterialKind kind)
    {
        int i = SetIndex(team, kind);

        if (i < 0)
        {
            return 0;
        }

        if (setOrdered.Value)
        {
            int step = setStep[team];

            if (SetItemAt(team, step) != (int)kind)
            {
                // 次の1個ではない。**進まないし、戻りもしない**
                return 0;
            }

            setStep[team] = step + 1;
            setProgress[i] = setProgress[i] + 1;
        }
        else
        {
            if (setProgress[i] >= setRequired[i])
            {
                // お題に無い、またはもう足りているデブリ
                return 0;
            }

            setProgress[i] = setProgress[i] + 1;
        }

        if (SetDoneOf(team) < SetGoalOf(team))
        {
            return 0;
        }

        // そろった
        int bonus = setBonus[team];
        setsCompleted[team] = setsCompleted[team] + 1;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} がデブリセットをそろえました（+{bonus} 点）。新しいお題を引きます。");

        ServerDrawDebrisSet(team);
        return bonus;
    }
}
