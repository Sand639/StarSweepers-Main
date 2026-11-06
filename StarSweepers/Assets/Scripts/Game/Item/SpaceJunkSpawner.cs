using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **宇宙船の素材（と爆弾）を、マップ上に次々と落とすスポナー。**
///
/// 決まった間隔ごとに、範囲の中のランダムな場所の少し上から1つ落とす。
/// 何を出すかは **Spawn List（プレハブと出やすさの一覧）** の割合で決める。素材も爆弾も同じ一覧に入れる（2026/10/6 から。
/// 前の「3種類＋追加の素材＋爆弾」の書き方のシーンは、自動で一覧へ移し替える）。
///
/// ・マップにある素材が **Max Objects 以上なら出さない**
/// ・出す場所に他の物やプレイヤーがいたら、別の場所を探す
/// ・**ラウンドの結果が出たら、もう出さない**
/// ・**Spawn Zones に範囲（四角）を入れたら、その範囲の中にだけ出す**（STAGE_Train の緑の範囲など）
/// ・**Spawn List に爆弾（ExplosiveObject 付き）を入れたら、素材と一緒に爆弾も出す**（マップの爆弾は Max Bombs 個まで。STAGE_Conveyor1）
/// ・**Cycle Zones を ON にすると、Spawn Zones に1回ずつ出すのを1巡として回す**（1巡ごとに順番をシャッフル。STAGE_06_A）
/// ・**Kind Zones に種類と範囲を入れたら、その種類だけその範囲に出す**（STAGE_Island の「敵陣まで取りに行く」）
/// ・**Spawn Circle Radius を入れたら円の中に出す。Require Ground Below を ON にすると、真下に床がある所にだけ出す**（STAGE_Meteor のドーナツ）
///
/// ## オンラインのとき
///
/// **出すのはホストだけ。** ホストが出して Spawn() すると、全員の画面に同じ物が現れる。
/// 素材のプレハブは `DefaultNetworkPrefabs` に登録しておく必要がある
/// （`Tools > StarSweepers > 宇宙ごみ集めのシーンを作る（ロビー＋マップ）` が自動で登録する）。
///
/// ## 釣りのスポナーとの違い
///
/// 釣り（<c>FishingObjectSpawner</c>）は「物資」と「爆発物」の2種類を、爆発物6：物資1で出していた。
/// こちらは**素材3種類を均等に出す**のと、**爆発物を出さない**のが違う
/// （爆発物は入れない、が 2026/9/17 の決まりだったが、2026/10/6 から Bomb Prefab で出せるようにした。STAGE_Conveyor1）。
///
/// ファイルの分け方と変数の置き場所は Documents/スクリプトの説明/Game/Item/SpaceJunkSpawner.md。
/// </summary>
public partial class SpaceJunkSpawner : MonoBehaviour
{
    /// <summary>出す物1つ分（プレハブと出やすさ）。</summary>
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("出すプレハブ。素材（SpaceJunkMaterial 付き）でも、爆弾（ExplosiveObject 付き）でもよい")]
        public GameObject prefab;

        [Tooltip("出やすさ。ほかの物との割合で決まる（全部 1 なら同じくらい）。0 なら出さない")]
        [Min(0f)] public float weight = 1f;
    }

    [Header("出す物（＋／−で足したり消したりできる）")]
    [Tooltip("出すプレハブと出やすさの一覧。素材も爆弾もここに入れる。" +
             "例：素材5種類を 1 ずつ、スタン爆弾を 1.25 にすると、だいたい5回に1回が爆弾になる")]
    [SerializeField] private List<SpawnEntry> spawnList = new List<SpawnEntry>();

    [Tooltip("マップにある爆弾がこの数以上なら、爆弾は出さない。**素材の Max Objects とは別に数える**")]
    [SerializeField] private int maxBombs = 2;

    /// <summary>
    /// **いまのシーンのスポナー。** どの種類の素材が出るマップかを、ラウンドのイベントや画面が調べるのに使う
    /// （ホストでも参加者でも入っている）。
    /// </summary>
    public static SpaceJunkSpawner Current { get; private set; }

    // ------------------------------------------------------------
    // 古い書き方（2026/10/6 まで）。**Spawn List が空なら、ここから自動で移し替える。**
    // インスペクターには出さない。前に作ったマップのシーンを書き直さずに済むように残してある
    // ------------------------------------------------------------

    /// <summary>古い書き方の「追加の素材」1つ分。</summary>
    [System.Serializable]
    public class ExtraMaterial
    {
        public GameObject prefab;
        public int weight = 1;
    }

    [HideInInspector][SerializeField] private GameObject platePrefab;
    [HideInInspector][SerializeField] private GameObject circuitPrefab;
    [HideInInspector][SerializeField] private GameObject fuelPrefab;
    [HideInInspector][SerializeField] private int plateWeight = 1;
    [HideInInspector][SerializeField] private int circuitWeight = 1;
    [HideInInspector][SerializeField] private int fuelWeight = 1;
    [HideInInspector][SerializeField] private ExtraMaterial[] extraMaterials = new ExtraMaterial[0];
    [HideInInspector][SerializeField] private GameObject bombPrefab;
    [HideInInspector][SerializeField] private float bombChance = 0.2f;

    [Header("数と間隔")]
    [Tooltip("マップにこの数以上あったら、もう出さない")]
    [SerializeField] private int maxObjects = 60;

    [Tooltip("ラウンドが始まった直後に、まとめて出す数")]
    [SerializeField] private int initialSpawnCount = 18;

    [Tooltip("何秒ごとに1つ出すか")]
    [SerializeField] private float spawnIntervalSeconds = 0.8f;

    [Tooltip("始まってから出し始めるまでの秒数（シーン切り替えが落ち着くのを待つ）")]
    [SerializeField] private float startDelaySeconds = 0.5f;

    [Header("出す場所")]
    [Tooltip("このオブジェクトの位置を中心に、左右（X）・前後（Z）それぞれ何mの範囲に出すか")]
    [SerializeField] private Vector2 areaHalfSize = new Vector2(16f, 16f);

    [Tooltip("床から何mの高さに出して落とすか")]
    [SerializeField] private float dropHeight = 3f;

    [Tooltip("出す場所の周りにこの半径で何かあったら、別の場所を探す")]
    [SerializeField] private float clearRadius = 0.8f;

    [Tooltip("空いた場所を探す回数。見つからなければ、その回は出さない")]
    [SerializeField] private int placementTries = 8;

    [Tooltip("ここに入れた四角の中にだけ出す（空のオブジェクトを置き、位置と Scale の X・Z で四角を決める）。" +
             "空なら Area Half Size の範囲に出す。選ぶと、シーン画面に緑の枠で見える")]
    [SerializeField] private Transform[] spawnZones = new Transform[0];

    [Tooltip("ON：Spawn Zones が2つ以上あるとき、**全部のゾーンに1回ずつ出すのを1巡とする**（順番は1巡ごとにシャッフル。" +
             "テトリスの7種1巡と同じ）。OFF：毎回ランダムなゾーン（広いゾーンほど出やすい）")]
    [SerializeField] private bool cycleZones = false;

    [Tooltip("0より大きくすると、このオブジェクトの位置を中心に、この半径の円の中に出す（Spawn Zones が空のとき）。" +
             "STAGE_Meteor のドーナツなど。0なら Area Half Size の四角")]
    [SerializeField] private float spawnCircleRadius = 0f;

    [Tooltip("ON にすると、**真下に床があるときだけ**出す（穴の上には出さない）")]
    [SerializeField] private bool requireGroundBelow = false;

    [Tooltip("ON にすると、**土台など一段高い床の上にも**出す（真下の床から Drop Height の高さに出す。STAGE_Simple の土台）。" +
             "高い床は、Drop Height より低くしておくこと。" +
             "OFF なら、このオブジェクトと同じ高さの床だけに出す（高い床の上は「何かある」とみなして出さない）")]
    [SerializeField] private bool spawnOnRaisedFloor = false;

    /// <summary>ある種類の素材だけを出す範囲。</summary>
    [System.Serializable]
    public class KindZone
    {
        [Tooltip("どの種類の素材か")]
        public SpaceJunkMaterialKind kind;

        [Tooltip("この種類だけを出す範囲（四角。Spawn Zones と同じ作り方）")]
        public Transform[] zones = new Transform[0];
    }

    [Header("種類ごとの出す範囲（STAGE_Island）")]
    [Tooltip("ここに入れた種類は、その範囲にだけ出す（入れていない種類は Spawn Zones などのふつうの範囲）。" +
             "「敵陣まで取りに行く」ように、種類ごとに出る場所を分けたいとき")]
    [SerializeField] private KindZone[] kindZones = new KindZone[0];

    /// <summary>
    /// 次に出すまでの秒数を数える。最初は「出し始めるまで」（Start Delay）、そのあとは「前に出してから」（Spawn Interval）を数える。
    /// </summary>
    private float spawnTimer;

    /// <summary>ラウンドの始まりのまとめ出し（Initial Spawn Count）を済ませたか。</summary>
    private bool initialSpawnDone;

    /// <summary>1巡法で、この巡でまだ出していないゾーン（先頭から使う）。</summary>
    private readonly List<Transform> zoneBag = new List<Transform>();

    /// <summary>シーン画面に出す「出す範囲」の枠の色（緑）。</summary>
    private static readonly Color SpawnAreaGizmoColor = new Color(0.3f, 1f, 0.4f, 0.8f);

    private void Awake()
    {
        MigrateLegacy();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 前の書き方のシーンを開いたら、インスペクターでも新しいリストで見えるようにする。
        // 移し替えたら古い項目は空にする（シーンを保存すると、新しい書き方で残る）
        if (MigrateLegacy())
        {
            platePrefab = null;
            circuitPrefab = null;
            fuelPrefab = null;
            extraMaterials = new ExtraMaterial[0];
            bombPrefab = null;
        }
    }
#endif

    /// <summary>シーンのスポナーとして名乗る（<see cref="Current"/>）。</summary>
    private void OnEnable()
    {
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>この種類の素材を出すか（プレハブが入っていて、出やすさが0より大きいか）。</summary>
    public bool UsesKind(SpaceJunkMaterialKind kind)
    {
        // Awake より先に（他の部品から）呼ばれても、古い書き方のシーンで一覧が空のままにならないように
        MigrateLegacy();

        foreach (SpawnEntry entry in spawnList)
        {
            if (entry != null && entry.prefab != null && entry.weight > 0f &&
                entry.prefab.TryGetComponent(out SpaceJunkMaterial material) && material.Kind == kind)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 流れ：Start Delay 秒待つ → Initial Spawn Count 個をまとめて出す → あとは Spawn Interval 秒ごとに1個。
    /// 出す役目でないPC（参加者）や、結果が出たあとは何もしない。
    /// </summary>
    private void Update()
    {
        if (!CanSpawnOnThisPC())
        {
            return;
        }

        spawnTimer += Time.deltaTime;

        if (!initialSpawnDone)
        {
            if (spawnTimer < startDelaySeconds)
            {
                return;
            }

            initialSpawnDone = true;
            spawnTimer = 0f;

            for (int i = 0; i < initialSpawnCount; i++)
            {
                TrySpawnOne();
            }
            return;
        }

        if (spawnTimer < spawnIntervalSeconds)
        {
            return;
        }

        spawnTimer = 0f;
        TrySpawnOne();
    }

    /// <summary>
    /// このPCが出す役目かどうか。
    /// 通信していなければ自分で出す。通信中なら**ホストだけ**が出す。
    /// ラウンドの結果が出たら出さない。
    /// </summary>
    private bool CanSpawnOnThisPC()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer)
        {
            return false;
        }

        return SpaceJunkRound.PlayAllowed;
    }

    /// <summary>
    /// **イベント用に、数の上限を気にせず1つ出す**（期間限定高価値デブリで使う）。**ホストだけが呼ぶ。**
    /// 置き場所が見つからなければ null。
    /// ※ 2026/10 時点では、特殊デブリ（重いデブリ）のイベント（<see cref="SpaceJunkRound"/> のイベント側）が使っている。
    /// </summary>
    public GameObject ServerSpawnExtra()
    {
        return SpawnOne(ignoreLimit: true);
    }

    /// <summary>
    /// このマップで出る素材のプレハブを、出やすさに合わせて1つ選ぶ（爆弾は選ばない）。
    /// 決まった場所に重いデブリを置く <see cref="SpaceJunkBombPoints"/> が使う。
    /// </summary>
    public GameObject ChooseMaterialPrefab()
    {
        return ChoosePrefab(bombsAllowed: false);
    }

    /// <summary>
    /// 選んでいる間、出す範囲をシーン画面に枠で出す。
    /// Spawn Zones は緑、Kind Zones はその素材の色。どちらも無ければ、円（Spawn Circle Radius）か四角（Area Half Size）。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // 出す範囲をシーン画面に緑の枠で出す
        Gizmos.color = SpawnAreaGizmoColor;

        bool hasZone = false;
        foreach (Transform zone in spawnZones)
        {
            if (zone == null)
            {
                continue;
            }

            hasZone = true;
            DrawZoneOutline(zone);
        }

        // 種類ごとの範囲は、その素材の色の枠で出す
        foreach (KindZone kindZone in kindZones)
        {
            if (kindZone == null || kindZone.zones == null)
            {
                continue;
            }

            Gizmos.color = SpaceJunkMaterials.Color(kindZone.kind);
            foreach (Transform zone in kindZone.zones)
            {
                if (zone == null)
                {
                    continue;
                }

                hasZone = true;
                DrawZoneOutline(zone);
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = SpawnAreaGizmoColor;

        if (!hasZone && spawnCircleRadius > 0f)
        {
            // 円を線でつないで描く（48本の線で1周）
            Vector3 center = transform.position + Vector3.up * dropHeight;
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Gizmos.DrawLine(
                    center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * spawnCircleRadius,
                    center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * spawnCircleRadius);
            }
        }
        else if (!hasZone)
        {
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * dropHeight,
                new Vector3(areaHalfSize.x * 2f, 0.1f, areaHalfSize.y * 2f));
        }
    }

    /// <summary>
    /// ゾーン（四角）の枠を、その位置・向き・大きさで描く（薄い板の形）。
    /// 呼んだあと <c>Gizmos.matrix</c> はそのゾーンのままなので、描き終わったら呼ぶ側で戻す。
    /// </summary>
    private static void DrawZoneOutline(Transform zone)
    {
        Gizmos.matrix = zone.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
    }
}
