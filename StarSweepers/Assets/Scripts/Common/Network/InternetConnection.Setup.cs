using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>InternetConnection の「準備（サインイン・同じPCでの複数起動）」（InternetConnection.cs から分けた部分）。</summary>
public partial class InternetConnection
{
    // ------------------------------------------------------------
    // 準備（初回だけ）
    // ------------------------------------------------------------

    /// <summary>
    /// 中継サーバーを使うための下準備。
    ///
    /// **初回だけ数秒かかる。** 2回目以降はすぐ終わる。
    /// 準備できたら true を返す。
    /// </summary>
    private async Task<bool> PrepareAsync()
    {
        // ★ここが一番よくある詰まりどころ。
        //   Unity Cloud への登録が済んでいないと、その先へ進めない
        if (string.IsNullOrEmpty(Application.cloudProjectId))
        {
            FailBeforeStart(
                "Unity Cloud への登録が済んでいません。\n" +
                "Unityの Project Settings → Services から\n" +
                "プロジェクトを登録し、Relay を有効にしてください。\n" +
                "（手順：Documents/機能ドキュメント/インターネットでの複数人プレイ.md）");

            Debug.LogError("[NET] Unity Cloud への登録が済んでいません。");
            return false;
        }

        State = Phase.Preparing;
        Message = "つなぐ準備をしています…";

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            // ★1台のPCで2つ起動して試すときに必要。下の説明を読むこと
            ApplyProfile();

            // 名前やパスワードは要らない。この場かぎりの身分証を発行してもらうだけ
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            return true;
        }
        catch (Exception error)
        {
            Fail("つなぐ準備に失敗しました。インターネットにつながっているか確認してください", error);
            return false;
        }
    }

    /// <summary>
    /// **1台のPCで2つ起動して試すときのための仕組み。**
    ///
    /// 匿名サインインの身分証は、**1台のPCにつき1つ**しか保存されない。
    /// そのため同じPCで2つ起動すると、**両方が「同じ人」として扱われ**、
    /// 参加しようとしたときに「すでにこの部屋の一員です」と断られる。
    ///
    /// Unityには**プロファイル**という仕組みがあり、
    /// 名前を分けると**別人として扱われる**ので、それを使う。
    ///
    /// **起動した順に自動で決まる**ので、ふつうは何もしなくてよい。
    /// 1番目はそのまま、2番目は `slot1`、3番目は `slot2` …となる。
    ///
    /// 手で決めたいときだけ、起動時の引数を使う：
    ///
    ///   ゲーム.exe -nethost -profile A
    ///
    /// **別々のPCで遊ぶ本番では、そもそもぶつからない。**
    /// PCが違えば身分証も別々になるため。
    /// </summary>
    private static void ResolveInstanceSlot()
    {
        if (profileResolved)
        {
            return;
        }

        profileResolved = true;

        // 手で指定されていれば、それを優先する
        autoProfile = ReadProfileArgument();

        if (!string.IsNullOrWhiteSpace(autoProfile))
        {
            Debug.Log($"[NET] プロファイルは引数の「{autoProfile}」を使います");
            return;
        }

        // 指定が無ければ、空いている番号を先着順で取る。
        // 鍵ファイルを掴んだままにするので、他のインスタンスは同じ番号を取れない
        for (int slot = 0; slot < MaxInstancesPerPc; slot++)
        {
            string path = Path.Combine(Application.persistentDataPath, $"instance{slot}.lock");

            try
            {
                // FileShare.None ＝ 他のプロセスはこのファイルを開けない。開けた＝この番号は空いていた
                instanceLock = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

                // 1番目はそのまま（本番はここしか通らない）
                autoProfile = slot == 0 ? null : $"slot{slot}";

                Debug.Log(autoProfile == null
                    ? "[NET] このPCで1番目の起動です（プロファイルはそのまま）"
                    : $"[NET] このPCで{slot + 1}番目の起動です。プロファイルを「{autoProfile}」にします");

                return;
            }
            catch (IOException)
            {
                // その番号は他のインスタンスが使っている。次を試す
            }
        }

        Debug.LogWarning("[NET] 空いているプロファイル番号がありません（8つまで）");
    }

    /// <summary>起動時の引数から `-profile ＜名前＞` を読む。無ければ null。</summary>
    private static string ReadProfileArgument()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-profile" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>決まったプロファイルを、サインインの前に反映する。</summary>
    private static void ApplyProfile()
    {
        if (string.IsNullOrWhiteSpace(autoProfile))
        {
            return;
        }

        if (AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        AuthenticationService.Instance.SwitchProfile(autoProfile);
        Debug.Log($"[NET] プロファイルを「{autoProfile}」に切り替えました（別人として扱われます）");
    }
}
