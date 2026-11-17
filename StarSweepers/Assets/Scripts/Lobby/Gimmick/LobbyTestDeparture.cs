using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>検証用ロビーで、チームの床を出発する船として動かす。</summary>
public sealed class LobbyTestDeparture : MonoBehaviour
{
    [SerializeField] private BoxCollider blueDeck;
    [SerializeField] private BoxCollider redDeck;
    [SerializeField] private string defaultStage = "STAGE_Simple";

    private const float BoardingPause = 0.75f;
    private const float FlightSeconds = 2.5f;
    private readonly Dictionary<FishingNetPlayer, SweeperNameTag> marks = new();
    private readonly List<FishingNetPlayer> removedMarks = new();
    private Vector3 blueStart;
    private Vector3 redStart;

    public static LobbyTestDeparture Current { get; private set; }
    public float Duration => BoardingPause + FlightSeconds;
    public string DefaultStage => defaultStage;
    public bool IsBoarding => SpaceJunkSession.Current?.TestBoardingTime > 0;

    private void Awake()
    {
        blueStart = blueDeck.transform.position;
        redStart = redDeck.transform.position;
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
        foreach (SweeperNameTag mark in marks.Values)
        {
            if (mark != null)
            {
                Destroy(mark.gameObject);
            }
        }
    }

    private void LateUpdate()
    {
        SpaceJunkSession session = SpaceJunkSession.Current;
        if (session == null || !session.IsSpawned)
        {
            RemoveUnusedMarks(null);
            blueDeck.transform.position = blueStart;
            redDeck.transform.position = redStart;
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        bool carrying = IsBoarding && manager.ServerTime.Time >= session.TestBoardingTime;
        if (carrying)
        {
            float elapsed = (float)(manager.ServerTime.Time - session.TestBoardingTime);
            float progress = Mathf.Clamp01((elapsed - BoardingPause) / FlightSeconds);
            float travel = progress * progress;
            blueDeck.transform.position = blueStart + FlightOffset(travel, outerSide: 1f, turnDepth: 2f);
            redDeck.transform.position = redStart + FlightOffset(travel, outerSide: -1f, turnDepth: -3f);
        }

        RemoveUnusedMarks(session);
        foreach (FishingNetPlayer player in FishingNetPlayer.All)
        {
            if (player == null || !player.IsSpawned || player.NetworkManager != manager)
            {
                continue;
            }

            ulong key = player.PlayerKey;
            if (carrying && session.TryGetTestBoardingSeat(key, out int team, out int seat))
            {
                BoxCollider deck = Deck(team);
                Vector3 offset = new Vector3(
                    (seat % 4 - 1.5f) * deck.size.x * 0.22f,
                    0.02f,
                    (seat / 4 - 0.5f) * deck.size.z * 0.4f);
                Vector3 position = deck.transform.TransformPoint(offset);
                if (player.IsOwner && player.TryGetComponent(out SpaceJunkPlayerSetup setup))
                {
                    setup.MoveWithLobbyTestShip(position);
                }
                else
                {
                    player.transform.position = position;
                }
            }

            if (session.IsTestReady(key))
            {
                ShowMark(player);
            }
            else if (!IsBoarding && player.IsOwner && !SpaceJunkSession.IsBotKey(key) &&
                     !SpaceJunkLobbyScreen.IsSettingsOpen && PauseMenu.Current?.IsOpen != true &&
                     IsOverDeck(player, Deck(session.TeamOf(key))) &&
                     PlayerInputSource.Get(player).WasPressed(Key.Enter, GamepadButton.East))
            {
                session.RequestTestReady(key);
            }
        }
    }

    private BoxCollider Deck(int team) => team == 0 ? blueDeck : redDeck;

    private static Vector3 FlightOffset(float t, float outerSide, float turnDepth)
    {
        // いったん外へふくらみ、交差しながらカメラに寄って画面外へ抜ける。
        Vector3 outerTurn = new Vector3(10f * outerSide, 0.5f, turnDepth);
        Vector3 crossingTurn = new Vector3(7f * outerSide, 2.8f, -11.5f);
        Vector3 exit = new Vector3(-17f * outerSide, 4.7f, -21.5f);
        float remaining = 1f - t;
        return 3f * remaining * remaining * t * outerTurn +
               3f * remaining * t * t * crossingTurn +
               t * t * t * exit;
    }

    private static bool IsOverDeck(FishingNetPlayer player, BoxCollider deck)
    {
        Vector3 local = deck.transform.InverseTransformPoint(player.transform.position) - deck.center;
        return Mathf.Abs(local.x) <= deck.size.x * 0.5f &&
               Mathf.Abs(local.z) <= deck.size.z * 0.5f &&
               local.y >= -deck.size.y * 0.5f - 0.1f &&
               local.y <= deck.size.y * 0.5f;
    }

    private void ShowMark(FishingNetPlayer player)
    {
        if (marks.ContainsKey(player))
        {
            return;
        }

        GameObject badge = new GameObject("ReadyMark");
        badge.transform.SetParent(player.transform, false);
        badge.transform.localPosition = Vector3.up * 0.65f;
        SweeperNameTag mark = badge.AddComponent<SweeperNameTag>();
        mark.SetName("√");
        mark.SetColor(new Color(0.35f, 1f, 0.5f));
        marks.Add(player, mark);
    }

    private void RemoveUnusedMarks(SpaceJunkSession session)
    {
        foreach (var entry in marks)
        {
            FishingNetPlayer player = entry.Key;
            if (session == null || player == null || !player.IsSpawned || !session.IsTestReady(player.PlayerKey))
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value.gameObject);
                }
                removedMarks.Add(player);
            }
        }

        foreach (FishingNetPlayer player in removedMarks)
        {
            marks.Remove(player);
        }
        removedMarks.Clear();
    }
}
