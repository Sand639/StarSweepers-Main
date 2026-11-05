using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「素材を受け取る（ホストだけ）」（SpaceJunkRound.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ------------------------------------------------------------
    // 素材を受け取る（**ホストだけが呼ぶ**）
    // ------------------------------------------------------------

    /// <summary>
    /// **ゴールに素材が入ったことを受け取る。** <see cref="SpaceJunkGoal"/> から呼ばれる。
    ///
    /// 得点制なら点を足す。3種類ルールなら、すでに持っている種類のときは何もしない（素材は消えるだけ）。
    /// </summary>
    /// <returns>数えたら true（ゴールの床が光る）。</returns>
    public bool ServerCollect(int team, SpaceJunkMaterialKind kind, NetworkObject item = null)
    {
        if (!IsServer || phase.Value != SpaceJunkRoundPhase.Playing)
        {
            return false;
        }

        if (team < 0 || team >= collected.Count)
        {
            return false;
        }

        // **誰もいないチームのぶんは数えない。**
        // 数えてしまうと、投げ間違いが積み重なるだけで
        // 「誰もいないチーム」がラウンドを取ってしまう
        if (SpaceJunkSession.Current != null && !SpaceJunkSession.Current.HasPlayers(team))
        {
            return false;
        }

        if (rule.Value == SpaceJunkWinRule.Score)
        {
            ServerAddScore(team, kind, item);
            return true;
        }

        return ServerCollectKind(team, kind);
    }

    /// <summary>
    /// **3種類ルールの受け取り方。** 新しい種類なら印を付け、そのマップで出る種類が全部そろったらその場でラウンド勝利。
    /// もう持っている種類なら何も起きない（false）。
    /// </summary>
    private bool ServerCollectKind(int team, SpaceJunkMaterialKind kind)
    {
        int bit = KindBit(kind);

        if ((collected[team] & bit) != 0)
        {
            // もう持っている種類。**何も起きない**
            return false;
        }

        collected[team] = collected[team] | bit;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を集めました" +
                  $"（{KindCountOf(team)} / {SpaceJunkMaterials.Count} 種類）。");

        // そのマップで出る種類が全部そろったら、その場でラウンド勝利
        if ((collected[team] & FullMask) == FullMask)
        {
            FinishRound(team);
        }

        return true;
    }

    /// <summary>
    /// **得点制の点の足し方。** 1個で1点。イベントが起きていれば、そのぶんのボーナスも足す
    /// （お題セットなら、お題がそろった1個でボーナスが入る）。
    /// </summary>
    private void ServerAddScore(int team, SpaceJunkMaterialKind kind, NetworkObject item)
    {
        // 集めた種類の印も付けておく（画面の表示に使う）
        collected[team] = collected[team] | KindBit(kind);

        // ステージに置いた重いデブリなら、その得点（イベントのボーナスとは重ねない）
        int bonus = ServerTryStageHeavyCollect(team, item, out int heavyBonus)
            ? heavyBonus
            : ServerEventBonusOnCollect(team, kind, item);

        // 真ん中の特殊デブリ（SpaceJunkBonusDebris）なら、その点数も足す
        if (item != null && item.TryGetComponent(out SpaceJunkBonusDebris bonusDebris))
        {
            bonus += bonusDebris.BonusPoints;
        }

        int gained = PointPerItem + bonus;

        scores[team] = scores[team] + gained;

        Debug.Log($"[JUNK] {SpaceJunkTeams.TeamName(team)} が {SpaceJunkMaterials.Name(kind)} を入れました" +
                  $"（+{gained} 点{(bonus > 0 ? $"。ボーナス {bonus} 点込み" : string.Empty)}／" +
                  $"合計 {scores[team]} 点）。");
    }
}
