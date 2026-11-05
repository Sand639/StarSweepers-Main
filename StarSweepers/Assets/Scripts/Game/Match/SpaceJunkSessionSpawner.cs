using Unity.Netcode;
using UnityEngine;

/// <summary>
/// **試合の係（<see cref="SpaceJunkSession"/>）を、ホストが1つだけ出す役。**
///
/// ロビーのシーンの `NetworkManager` と同じ場所に置いておく。
///
/// ## なぜ「シーンに置く」ではだめなのか
///
/// シーンに置いた通信オブジェクトは、**シーンを切り替えると一緒に消えてしまう。**
/// この遊びはラウンドごとにマップが切り替わるので、それでは設定も勝ち数も消える。
///
/// **プレハブから出した通信オブジェクトはシーンをまたいで生き続ける**ため、
/// ホストがここで1つ出して、あとはずっとそれを使い回す。
///
/// 2回目以降（ロビーへ戻ってきたとき）は、すでに係がいるので何もしない。
/// </summary>
public class SpaceJunkSessionSpawner : MonoBehaviour
{
    [Header("出すもの")]
    [Tooltip("SpaceJunkSession が付いたプレハブ。**DefaultNetworkPrefabs に登録されていること**")]
    [SerializeField] private GameObject sessionPrefab;

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;

        // つながっていない／ホストではないときは何もしない
        if (manager == null || !manager.IsListening || !manager.IsServer)
        {
            return;
        }

        // すでに係がいる（ロビーへ戻ってきたときなど）
        if (SpaceJunkSession.Current != null)
        {
            return;
        }

        // **ラウンドの最中なら、絶対に作らない。**
        // ここで作ってしまうと、設定も勝ち数も入っていないまっさらな係ができて、
        // ラウンドの結果を受け取らずに捨ててしまう（試合が進まなくなる）。
        // この役は NetworkManager に付いていてシーンをまたいで生き残るので、
        // 何も考えないとマップでも動いてしまう
        if (SpaceJunkRound.Current != null)
        {
            Debug.LogError(
                "[JUNK] ラウンド中なのに試合の係がいません。**ロビーからやり直してください。**\n" +
                "係はシーンを切り替えても消えないはずです（Spawn の destroyWithScene が false であること）。");
            enabled = false;
            return;
        }

        if (sessionPrefab == null)
        {
            // 毎フレーム出すとログが埋まるので、1度だけ出して自分を止める
            Debug.LogError("[JUNK] Session Prefab が入っていません。試合の係を出せません。", this);
            enabled = false;
            return;
        }

        GameObject spawned = Instantiate(sessionPrefab);
        NetworkObject networkObject = spawned.GetComponent<NetworkObject>();

        if (networkObject == null)
        {
            Debug.LogError("[JUNK] Session Prefab に NetworkObject がありません。", this);
            Destroy(spawned);
            enabled = false;
            return;
        }

        // ⚠ **引数は「シーンが変わったら消すか（destroyWithScene）」。**
        //   ここを true にすると、ロビーからマップへ移った瞬間に係が消えてしまい、
        //   設定も勝ち数も失われる。**必ず false（＝引数なし）で出すこと。**
        //   （2026/9/20：true にしていたため、ラウンド終了後に次へ進まなくなっていた）
        networkObject.Spawn(false);

        Debug.Log("[JUNK] 試合の係（SpaceJunkSession）を出しました。ここから先はシーンを切り替えても消えません。");
    }
}
