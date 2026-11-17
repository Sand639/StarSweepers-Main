using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// ゴールの渦の検証シーン専用。ホストとして始め、ゴールの上から物資（の代わりの箱）を順番に落として、渦に吸い込ませる。
/// 吸い込み終わったら、得点が入ったときの光る演出も出す（検証シーンには試合の進行役が無いので、ここで出す）。
/// 説明は Documents/スクリプトの説明/Game/Effect/GoalEffect.md
/// </summary>
public class GoalEffectTestDriver : MonoBehaviour
{
    [Tooltip("物資を落とすゴール（順番に回す）")]
    [SerializeField] private SpaceJunkGoal[] goals;

    [Tooltip("それぞれのゴールの持ち主のチーム（0=青 1=赤 2=緑 3=黄）")]
    [SerializeField] private int[] teams = { 0, 1 };

    [Tooltip("何秒ごとに物資を落とすか")]
    [SerializeField] private float interval = 0.9f;

    [Tooltip("ゴールの床から何 m 上に落とすか")]
    [SerializeField] private float dropHeight = 3f;

    [Tooltip("ゴールの角（渦の外）に落とす割合。引き寄せを確かめるため")]
    [Range(0f, 1f)]
    [SerializeField] private float cornerChance = 0.35f;

    private float timer;
    private int next;
    private NetworkManager createdManager;
    private System.Random random = new System.Random(17);

    private void Start()
    {
        // ゴールが数えるのはホストだけなので、ホストとして始める
        if (NetworkManager.Singleton == null)
        {
            var root = new GameObject("VortexTestNetwork");
            UnityTransport transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17993);
            createdManager = root.AddComponent<NetworkManager>();
            createdManager.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.StartHost();
        }

        for (int i = 0; i < goals.Length; i++)
        {
            if (goals[i] != null)
            {
                goals[i].ApplyOwnerTeam(teams.Length > 0 ? teams[i % teams.Length] : i);
            }
        }

        GoalEffect.Absorbed += OnAbsorbed;
        timer = interval;
    }

    private void OnDestroy()
    {
        GoalEffect.Absorbed -= OnAbsorbed;
        if (createdManager != null)
        {
            createdManager.Shutdown();
            Destroy(createdManager.gameObject);
        }
    }

    private void OnAbsorbed(Component goal)
    {
        if (goal is SpaceJunkGoal junkGoal && junkGoal.gameObject.scene == gameObject.scene)
        {
            junkGoal.ShowScored();
        }
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
        GoalEffect vortex = GoalEffect.In(gameObject.scene);
        if (goal != null && vortex != null && vortex.TryGetArea(goal, out GoalEffect.Area area))
        {
            Drop(area);
        }
    }

    /// <summary>渦の中か、渦の外の角に、物資の代わりの箱を1つ落とす。</summary>
    private void Drop(GoalEffect.Area area)
    {
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        bool corner = random.NextDouble() < cornerChance;
        float distance = corner ? area.Radius * 1.05f : area.Radius * (float)random.NextDouble() * 0.8f;
        if (corner)
        {
            // 四角の角のほうへ（渦の外）
            angle = Mathf.PI * 0.25f + Mathf.PI * 0.5f * random.Next(4);
        }

        Vector3 position = area.Center + new Vector3(Mathf.Cos(angle) * distance, dropHeight, Mathf.Sin(angle) * distance);
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "TestJunk";
        box.transform.SetPositionAndRotation(position, Random.rotation);
        box.transform.localScale = Vector3.one * 0.6f;
        box.GetComponent<Renderer>().material.color = Color.HSVToRGB((float)random.NextDouble(), 0.5f, 0.95f);
        box.AddComponent<Rigidbody>();
        box.AddComponent<HookableObject>();
        box.AddComponent<SpaceJunkMaterial>();
    }
}
