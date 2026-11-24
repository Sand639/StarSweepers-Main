using System.Collections.Generic;
using UnityEngine;

/// <summary>ロビーのチームエリア。プレイヤーが1秒留まると、そのプレイヤーを指定チームへ移す。</summary>
[RequireComponent(typeof(Collider))]
public sealed class LobbyTeamZone : MonoBehaviour
{
    [Tooltip("青＝0、赤＝1。SpaceJunkTeams のチーム番号（床の色もこの番号のチーム色になる）")]
    [SerializeField, Min(0)] private int team;
    [Tooltip("チーム変更までエリア内に留まる秒数")]
    [SerializeField, Min(0f)] private float staySeconds = 1f;
    [Tooltip("チームの色に塗る見た目（SM_Transporter）。試合のゴールと同じく、始まったときに SpaceJunkTeams のチーム色を掛ける。空なら塗らない")]
    [SerializeField] private Renderer padRenderer;

    private readonly Dictionary<ulong, PlayerStay> players = new Dictionary<ulong, PlayerStay>();
    private readonly Dictionary<ulong, HashSet<Collider>> colliders = new Dictionary<ulong, HashSet<Collider>>();
    private readonly HashSet<ulong> changedPlayers = new HashSet<ulong>();

    private sealed class PlayerStay
    {
        public float Since;
    }

    private void Reset()
    {
        Collider zoneCollider = GetComponent<Collider>();
        zoneCollider.isTrigger = true;
    }

    private void Start()
    {
        // 試合のゴール（SpaceJunkGoal.ApplyOwnerTeam）と同じ塗り方
        if (padRenderer != null)
        {
            padRenderer.material.color = SpaceJunkTeams.TeamColor(team);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        FishingNetPlayer player = other.GetComponentInParent<FishingNetPlayer>();
        if (player == null || !player.IsOwner || !player.IsSpawned)
        {
            return;
        }

        ulong key = player.PlayerKey;
        if (!colliders.TryGetValue(key, out HashSet<Collider> set))
        {
            set = new HashSet<Collider>();
            colliders.Add(key, set);
        }

        set.Add(other);
        if (players.ContainsKey(key))
        {
            return;
        }

        players.Add(key, new PlayerStay { Since = Time.unscaledTime });
        changedPlayers.Remove(key);
        SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, true);
    }

    private void OnTriggerExit(Collider other)
    {
        FishingNetPlayer player = other.GetComponentInParent<FishingNetPlayer>();
        if (player == null || !player.IsOwner)
        {
            return;
        }

        ulong key = player.PlayerKey;
        if (!colliders.TryGetValue(key, out HashSet<Collider> set))
        {
            return;
        }

        set.Remove(other);
        if (set.Count > 0)
        {
            return;
        }

        colliders.Remove(key);
        players.Remove(key);
        changedPlayers.Remove(key);
        SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, false);
    }

    private void Update()
    {
        foreach (KeyValuePair<ulong, PlayerStay> pair in players)
        {
            if (!changedPlayers.Contains(pair.Key) && Time.unscaledTime - pair.Value.Since >= staySeconds)
            {
                changedPlayers.Add(pair.Key);
                SpaceJunkSession.Current?.RequestLobbyZoneTeam(pair.Key, team);
            }
        }
    }

    private void OnDisable()
    {
        foreach (ulong key in players.Keys)
        {
            SpaceJunkSession.Current?.RequestLobbyZonePresence(key, team, false);
        }

        players.Clear();
        colliders.Clear();
        changedPlayers.Clear();
    }
}
