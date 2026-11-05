using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>SpaceJunkPlayerSetup の「出てくる場所を決める」（SpaceJunkPlayerSetup.cs から分けた部分）。</summary>
public partial class SpaceJunkPlayerSetup
{
    /// <summary>
    /// **自分のチームのゴールの手前（横一列の自分の場所）**へ移す。見つからなければ円周上へ散らす。
    ///
    /// **移す前に、フックを手元に戻して、つかんでいる物資も離す**（2026/9/22）。
    /// R キー・落下・ラウンドの始まり・ロビーへ戻ったとき、のどれでもここを通るので、
    /// 物資をつかんだまま戻ったり、伸ばしたフックが次のラウンドに残ったりしない。
    /// </summary>
    private void MoveToSpawnPoint()
    {
        if (hookController != null)
        {
            hookController.ResetHook();
        }

        // ロビーでは、シーン内のポイント自身が持つ番号から場所を探す。
        if (IsInLobbyScene() && netPlayer != null)
        {
            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (SpaceJunkLobbyPoint point in SpaceJunkLobbyPoint.All)
            {
                if (point != null && point.gameObject.scene == activeScene && point.SpawnIndex == netPlayer.PlayerIndex)
                {
                    SetSpawnTransform(point.transform.position, point.transform.rotation);
                    RevealLobbyRenderers();
                    return;
                }
            }

            SetSpawnTransform(FallbackSpawnPosition(Vector2.zero), Quaternion.identity);
            RevealLobbyRenderers();
            return;
        }

        Vector3 position = FindSpawnPosition();
        Quaternion rotation = SpawnFacing(position);

        SetSpawnTransform(position, rotation);
    }

    private void SetSpawnTransform(Vector3 position, Quaternion rotation)
    {
        // CharacterController が付いたまま動かすと位置が戻されるので、いったん切る
        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }

    /// <summary>
    /// <paramref name="position"/> に出てきたときの向き。ステージの中心（原点）のほうを向く。ほぼ中心にいるときは向きが決まらないので、前向き。
    /// 始まりの演出でゴールエリアに乗せるときも、この向きをゴールエリアと一緒に回す（<see cref="SpaceJunkGoalCarrier"/>）。
    /// </summary>
    public static Quaternion SpawnFacing(Vector3 position)
    {
        Vector3 toCenter = new Vector3(-position.x, 0f, -position.z);
        return Quaternion.LookRotation(toCenter.sqrMagnitude > 0.01f ? toCenter : Vector3.forward);
    }

    /// <summary>
    /// 出てくる場所を決める。ラウンド中で自分のゴールが分かれば**ゴールの近く**（<see cref="TryGetGoalSpawnPosition"/>）、
    /// それ以外（ロビー・ゴールがまだ届いていない）は床のある決まった場所。
    /// ゴールが届いていなかったときは <see cref="placedByFallback"/> を立て、あとで置き直す。
    /// </summary>
    private Vector3 FindSpawnPosition()
    {
        // ゴールを当てにしないとき用。重ならないよう、Spawn Scatter（m）の円の中でばらけさせる
        Vector2 scatter = Random.insideUnitCircle * spawnScatter;

        // **ゴールを当てにするのは、ラウンドが動いているときだけ。**
        //
        // ロビーにはゴールが無い。それなのに `SpaceJunkGoal.All` を見に行くと、
        // **シーンの入れ替わりの途中でまだ消えていない「前のマップのゴール」**を
        // つかんでしまい、ロビーの地面のはるか外（中心から約21m）に置かれる。
        // ロビーの地面は20m四方（中心から10m）なので、そのまま落ちる
        // （2026/9/20・大槻さんの報告）
        if (SpaceJunkRound.Current == null)
        {
            placedByFallback = false;
            return SafeFallbackPosition(scatter);
        }

        // **このシーンにある、自分のチームのゴール**（前のマップのゴールはつかまない。2026/10/6）
        if (TryGetGoalSpawnPosition(out Vector3 goalSpot))
        {
            placedByFallback = false;
            return goalSpot;
        }

        // ゴールが見つからないとき（まだ持ち主が配られていないなど）。**あとで届いたら置き直す**
        placedByFallback = true;
        fallbackRetryTimer = 0f;
        return SafeFallbackPosition(scatter);
    }

    /// <summary>
    /// **ラウンド中の、ゴールを当てにした出てくる場所。** 自分のゴールがまだ分からなければ false。
    ///
    /// ・Spawn On Goal Area が ON … **ゴールエリアの上の決まった場所**（2026/10/27・大槻さん）。
    ///   ゴールから見て横（中心へ向かう向きと直角）に、**ゴールエリアの幅をチームの人数で等分した場所**に並ぶ
    ///   （2人なら幅の 1/4 と 3/4 の所。間を詰めすぎると名前のタグが重なるため）
    /// ・OFF か、そこに床が無いとき（ゴールが床の外にあるマップ）… **ゴールから Spawn Inset だけ内側に横一列**、
    ///   Spawn Spacing（m）おき（2026/10/27 の朝の形。前はランダムにばらけていた）
    ///
    /// どちらも列の真ん中がゴールの真ん前に来る。毎回同じ場所になる（ばらけさせない）ので、
    /// **自分のぶん以外のプレイヤーでも求められる**（始まりの演出 <see cref="SpaceJunkGoalFlyIntro"/> が、
    /// 全員を飛んでくるゴールエリアに乗せるのに使う）。
    /// </summary>
    public bool TryGetGoalSpawnPosition(out Vector3 position)
    {
        position = Vector3.zero;

        if (!TryFindOwnGoal(out SpaceJunkGoal goal))
        {
            return false;
        }

        Vector3 goalPosition = goal.transform.position;
        Vector3 toCenter = new Vector3(-goalPosition.x, 0f, -goalPosition.z);

        // ゴールがほぼ中心にあると向きが決まらないので、前向きにする
        if (toCenter.sqrMagnitude < 0.01f)
        {
            toCenter = Vector3.forward;
        }

        toCenter.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toCenter);
        GetTeamLineSlot(out int index, out int count);
        float slot = index - (count - 1) * 0.5f;

        if (spawnOnGoalArea)
        {
            // ゴールエリアの横の幅を人数で等分した間隔（見た目が無くて幅が分からなければ Goal Area Spacing）
            float spacing = goal.TryGetIntroVisualBounds(out Bounds area)
                ? (Mathf.Abs(side.x) * area.size.x + Mathf.Abs(side.z) * area.size.z) / count
                : goalAreaSpacing;

            if (TryGroundAt(goalPosition + side * (slot * spacing), out Vector3 onArea))
            {
                position = onArea;
                return true;
            }
        }

        Vector3 spot = goalPosition + toCenter * spawnInset + side * (slot * spawnSpacing);
        position = new Vector3(spot.x, goalPosition.y, spot.z);
        return true;
    }

    /// <summary>
    /// **自分がチームの列の何番目か（0から）と、チームの人数（ボットも数える）。**
    /// 並び順は席に入った順。席の一覧は全員のPCで同じなので、各自が自分の場所を決めても重ならない。
    /// 自分の席が見つからなければ、1人だけのチームとして扱う（ゴールの真ん前）。
    /// </summary>
    private void GetTeamLineSlot(out int index, out int count)
    {
        index = 0;
        count = 0;

        ulong key = netPlayer != null ? netPlayer.PlayerKey : 0UL;
        int team = MyTeam();
        bool found = false;

        foreach (SpaceJunkPlayerSlot slot in SpaceJunkSession.Current.Slots)
        {
            if (slot.Team != team)
            {
                continue;
            }

            if (slot.ClientId == key)
            {
                index = count;
                found = true;
            }

            count++;
        }

        if (!found)
        {
            index = 0;
            count = 1;
        }
    }

    /// <summary>
    /// **ゴールを当てにしないときの、床がある出てくる場所。**（2026/10/6）
    /// まず中心のまわりの決まった場所（<see cref="FallbackSpawnPosition"/>）を見て、
    /// そこに床が無ければ（ドーナツの穴・床の無い帯など）、まわりを順に探して床のある所を使う。
    /// </summary>
    private Vector3 SafeFallbackPosition(Vector2 scatter)
    {
        Vector3 first = FallbackSpawnPosition(scatter);
        if (TryGroundAt(first, out Vector3 grounded))
        {
            return grounded;
        }

        // 中心からの距離の候補（m）。設定の距離 → 近く → 中心 → 遠く の順に、それぞれ8方向（45度ずつ）を見る
        float[] radii = { fallbackRadius, 3f, 0f, 9f, 12f, 15f };
        for (int r = 0; r < radii.Length; r++)
        {
            for (int a = 0; a < 8; a++)
            {
                Vector3 candidate = Quaternion.Euler(0f, a * 45f, 0f) * (Vector3.forward * radii[r]);
                if (TryGroundAt(candidate, out grounded))
                {
                    return grounded;
                }
            }
        }

        return first;
    }

    /// <summary>その場所の真下に床があれば、床の上の位置を返す。</summary>
    private bool TryGroundAt(Vector3 spot, out Vector3 grounded)
    {
        grounded = spot;

        // その場所の 6m 上から、20m 下まで全レイヤー（~0）を見る（一段高い土台や、少し低い床でも見つかるように）
        Vector3 from = new Vector3(spot.x, spot.y + 6f, spot.z);

        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            // 自分の体や、物資・プレイヤーは床とみなさない
            if (hit.collider.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<HookableObject>() != null ||
                hit.collider.GetComponentInParent<CharacterController>() != null)
            {
                continue;
            }

            grounded = new Vector3(spot.x, hit.point.y, spot.z);
            return true;
        }

        return false;
    }

    /// <summary>
    /// **ゴールを当てにしないときの出てくる場所。** ロビーでもここを使う。
    ///
    /// 中心のまわりに、接続番号ごとに角度をずらして並べる。
    /// 全員が同じ場所に重なって出てこないようにするため。
    /// </summary>
    private Vector3 FallbackSpawnPosition(Vector2 scatter)
    {
        // 1台で複数人のときは、何人目かのぶんだけずらす（同じ場所に重ならないように）
        ulong id = netPlayer != null ? netPlayer.OwnerClientId + (ulong)netPlayer.LocalSeat : 0UL;
        float angle = (id % (ulong)SpaceJunkTeams.MaxPlayers) * (360f / SpaceJunkTeams.MaxPlayers);
        Vector3 spot = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * fallbackRadius);

        return new Vector3(spot.x + scatter.x, 0f, spot.z + scatter.y);
    }
}
