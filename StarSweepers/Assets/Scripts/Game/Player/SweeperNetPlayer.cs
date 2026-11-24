using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **オンラインのプレイヤー1人分の「参加者としての情報」。**
///
/// 役割は3つ。
///   ① **参加番号・名前を決める**（ホストが決めて全員に配る）
///   ② **自分の1人だけが操作できるようにする**（他の人のぶんは当たり判定を切り、操作スクリプトを止める）
///   ③ 生まれたときに、出てくる場所とカメラを合わせる
///
/// チームはロビーでホストが決めたもの（<see cref="SpaceJunkSession"/>）を使う（<see cref="TeamIndex"/>）。
/// 移動そのものは <see cref="SweeperMover"/> が動かし、位置は `NetworkTransform` が配る（`動きは本人` の形）。
/// 光線の様子を配るのは <see cref="SweeperNetSync"/>。このスクリプトは参加者の情報だけを持つ。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class SweeperNetPlayer : NetworkBehaviour
{
    /// <summary>いまシーンにいるオンラインプレイヤー全員。人数とチーム分けに使う。</summary>
    public static readonly List<SweeperNetPlayer> All = new List<SweeperNetPlayer>();

    [Header("自分だけが動かすもの")]
    [Tooltip("自分のぶんだけ有効にするスクリプト。他の人のぶんは止める")]
    [SerializeField] private MonoBehaviour[] ownerOnlyScripts;

    [Tooltip("他の人のぶんでは切る CharacterController（切らないと送られてきた位置に移せない）")]
    [SerializeField] private CharacterController characterController;

    [Header("出てくる場所")]
    [Tooltip("中心からどれだけ離れた場所に出てくるか（メートル）。宇宙ごみでは SpaceJunkPlayerSetup がこのあと置き直す")]
    [SerializeField] private float spawnRadius = 6f;

    // ---- ホストが決めて全員に配るもの ----

    /// <summary>参加番号（0から）。**ホストが決める。**</summary>
    private readonly NetworkVariable<int> playerIndex = new NetworkVariable<int>(-1);

    /// <summary>
    /// **そのPCの何人目か（0から）。ホストが出すときに決める。**
    /// ふつうは 0（1台に1人）。1台で複数人のとき、2人目以降はホストが追加で出し、1・2… が入る（<see cref="LocalMultiplayer"/>）。
    /// </summary>
    private readonly NetworkVariable<int> localSeat = new NetworkVariable<int>(0);

    // 名前の実体は文字列ではなく、CSVの前・中・後の番号。書き込みはホストだけ。
    private readonly NetworkVariable<int> namePrefixIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> nameConnectorIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> nameSuffixIndex = new NetworkVariable<int>(-1);

    public int NamePrefixIndex => namePrefixIndex.Value;
    public int NameConnectorIndex => nameConnectorIndex.Value;
    public int NameSuffixIndex => nameSuffixIndex.Value;

    private NameDataManager nameDatabase;

    /// <summary>参加番号（0から）。まだ決まっていなければ -1。</summary>
    public int PlayerIndex => playerIndex.Value;

    /// <summary>そのPCの何人目か（0から）。1台に1人なら常に 0。</summary>
    public int LocalSeat => localSeat.Value;

    /// <summary>
    /// **人ごとの番号。** チーム分け（SpaceJunkSession の席）はこれで見分ける。
    /// 1人目は接続番号（OwnerClientId）そのままなので、1台に1人なら今までと同じ番号。
    /// </summary>
    public ulong PlayerKey => LocalMultiplayer.MakeKey(OwnerClientId, localSeat.Value);

    /// <summary>そのPCの何人目かを決める。**ホストが、出す（Spawn）前に呼ぶ。**</summary>
    public void ServerInitLocalSeat(int seat)
    {
        localSeat.Value = Mathf.Max(0, seat);
    }

    /// <summary>
    /// 所属チーム。**ロビーでホストが決めたチーム**（<see cref="SpaceJunkSession"/>）。
    /// まとめ役がいない場面（検証用のシーンなど）では、参加番号 2 人ずつで分ける。
    /// </summary>
    public int TeamIndex
    {
        get
        {
            SpaceJunkSession session = SpaceJunkSession.Current;
            if (session != null)
            {
                return session.TeamOf(PlayerKey);
            }

            return Mathf.Clamp(Mathf.Max(0, playerIndex.Value) / 2, 0, SpaceJunkTeams.MaxTeams - 1);
        }
    }

    /// <summary>参加番号 <paramref name="index"/> の人のチーム。その人が見つからなければ -1。</summary>
    public static int TeamOfPlayerIndex(int index)
    {
        foreach (SweeperNetPlayer player in All)
        {
            if (player != null && player.PlayerIndex == index)
            {
                return player.TeamIndex;
            }
        }

        return -1;
    }

    /// <summary>画面に出す名前。</summary>
    public string DisplayName
    {
        get
        {
            string generated = GetNameDatabase()?.GetName(NamePrefixIndex, NameConnectorIndex, NameSuffixIndex);
            return !string.IsNullOrEmpty(generated)
                ? generated
                : playerIndex.Value < 0 ? "参加中…" : $"プレイヤー{playerIndex.Value + 1}";
        }
    }

    /// <summary>NetworkManagerにある一覧を使う。古い検証シーンに無ければ同じ部品を補う。</summary>
    private NameDataManager GetNameDatabase()
    {
        if (nameDatabase == null && NetworkManager != null)
        {
            nameDatabase = NetworkManager.GetComponent<NameDataManager>();
            if (nameDatabase == null)
            {
                nameDatabase = NetworkManager.gameObject.AddComponent<NameDataManager>();
            }
        }
        return nameDatabase;
    }

    /// <summary>
    /// このプレイヤーの名前を番号で指定する。ホスト、または本人のPCから呼ぶ。
    /// 参加者の戻り値は「要求を送れたか」。ホストでも番号を検査してから全員に配る。
    /// </summary>
    public bool SetName(int prefix, int connector, int suffix)
    {
        if (!IsSpawned || (!IsServer && !IsOwner))
        {
            return false;
        }

        NameDataManager database = GetNameDatabase();
        if (database == null || !database.IsValidName(prefix, connector, suffix))
        {
            return false;
        }

        if (IsServer)
        {
            namePrefixIndex.Value = prefix;
            nameConnectorIndex.Value = connector;
            nameSuffixIndex.Value = suffix;
        }
        else
        {
            SetNameServerRpc(prefix, connector, suffix);
        }
        return true;
    }

    // 既定の所有者制限により、本人以外からこのRPCを送ることはできない。
    [ServerRpc]
    private void SetNameServerRpc(int prefix, int connector, int suffix)
    {
        SetName(prefix, connector, suffix);
    }

    private PlayerInputSource input;

    private void Awake()
    {
        input = PlayerInputSource.Get(this);

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    /// <summary>
    /// 通信上で生まれたとき（**全員のPCで、全員ぶん呼ばれる**）。
    /// 自分が操作するぶんなら出てくる場所とカメラを合わせ、他の人のぶんなら操作を止める。
    /// </summary>
    public override void OnNetworkSpawn()
    {
        // ホストが参加番号を決める。**空いている一番小さい番号を使う**
        if (IsServer)
        {
            playerIndex.Value = FirstFreeIndex();

            NameDataManager database = GetNameDatabase();
            if (database != null && database.RandomName(out int prefix, out int connector, out int suffix))
            {
                SetName(prefix, connector, suffix);
            }
            else
            {
                Debug.LogWarning("[NAME] 名前の前・中・後の候補がそろっていないので、仮の名前を使います。", this);
            }
        }

        All.Add(this);

        Debug.Log($"[PLAYER] プレイヤー {OwnerClientId} が出てきました" +
                  $"（参加番号 {playerIndex.Value} ／ 自分が操作する：{IsOwner}）");

        if (IsOwner)
        {
            input.SeatIndex = LocalMultiplayer.IsActive ? localSeat.Value : -1;
            MoveToSpawnPoint();
            FollowWithCamera();
        }
        else
        {
            // 他の人のぶん。**このPCでは操作しない。**
            SetOwnerScriptsEnabled(false);

            if (characterController != null)
            {
                characterController.enabled = false;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
    }

    /// <summary>
    /// **まだ誰も使っていない、一番小さい参加番号を返す**（ホストだけが呼ぶ）。
    ///
    /// 以前は「いまいる人数」をそのまま番号にしていたが、
    /// **途中で抜けた人がいると番号が空き、次に入ってきた人が誰かと同じ番号になっていた**
    /// （例：0〜4 の5人 → 2番が抜ける → 残りは 0・1・3・4 で人数は4 → 次の人も4番。2026/9/24・大槻さん）。
    ///
    /// 番号は画面の名前（プレイヤー1…）だけでなく、**物資を誰が引っ掛けているかの判断**にも使うので、
    /// かぶると別の人の物資を投げられてしまう。
    /// </summary>
    private static int FirstFreeIndex()
    {
        for (int index = 0; index < SpaceJunkTeams.MaxPlayers; index++)
        {
            bool used = false;

            foreach (SweeperNetPlayer player in All)
            {
                if (player != null && player.playerIndex.Value == index)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
            {
                return index;
            }
        }

        // 満員のときの保険。ふだんは人数で先に断られるので、ここには来ない
        Debug.LogWarning("[PLAYER] 参加番号がすべて使われています。一番大きい番号を使います。");
        return SpaceJunkTeams.MaxPlayers - 1;
    }

    private void Update()
    {
        if (IsOwner)
        {
            // 1台で複数人なら、そのPCの何人目かの席（P1・P2…）の機器だけで動かす。1人なら今までどおり
            input.SeatIndex = LocalMultiplayer.IsActive ? localSeat.Value : -1;
        }
    }

    /// <summary>自分のぶんだけ操作スクリプトを有効／無効にする。</summary>
    private void SetOwnerScriptsEnabled(bool enabledState)
    {
        if (ownerOnlyScripts == null)
        {
            return;
        }

        foreach (MonoBehaviour script in ownerOnlyScripts)
        {
            if (script != null)
            {
                script.enabled = enabledState;
            }
        }
    }

    /// <summary>参加番号ごとに、円周上の違う場所へ移す。全員が重なって出てくるのを防ぐため。</summary>
    private void MoveToSpawnPoint()
    {
        // 円周を最大人数で等分し、参加番号ぶんだけ回した場所に出す。向きは +180° で円の中心のほう
        int index = Mathf.Max(0, playerIndex.Value);
        float angle = index * (360f / SpaceJunkTeams.MaxPlayers);
        Vector3 position = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * spawnRadius);

        if (characterController != null)
        {
            // CharacterController が有効なままだと、transform を直接動かしても元の位置に戻されることがあるので、いったん切って移す
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, angle + 180f, 0f));
        }
    }

    /// <summary>
    /// **シーンに置いてあるカメラと自分を結びつける。**
    /// プレイヤーはプレハブから生まれるので、シーンの中身をあらかじめ入れておけない。
    /// </summary>
    private void FollowWithCamera()
    {
        // カメラは画面に1つしか無いので、そのPCの1人目にだけ結びつける
        if (localSeat.Value != 0)
        {
            return;
        }

        TopDownCameraFollow follow = FindFirstObjectByType<TopDownCameraFollow>();

        if (follow != null)
        {
            follow.SetTarget(transform);
        }
    }
}
