using System.Collections;
using UnityEngine;

/// <summary>
/// **プレイヤー（本実装）の巻き取りと投げ。**（2026/10/27・小野田さんの仕様）
///
/// 光線が物資（<see cref="HookableObject"/>）に当たると、<see cref="SweeperShooter"/> が <see cref="Begin"/> を呼ぶ。
///
/// | いつ | どうなる |
/// | --- | --- |
/// | 当たった瞬間から | 物資を、プレイヤーの手前まで引き寄せる。**距離に関係なく Reel Seconds（初期値1.5秒）で**着く（つかんでいる間は重力なし。持つ高さより上では落ちる） |
/// | 途中でボタンを押す | **後ろへ投げる。** 押した瞬間の位置から、プレイヤーの頭を越える放物線を描いて後ろへ飛ぶ |
/// | 決まった瞬間（クリティカルの受付）に押す | **クリティカルプル。** 光が出て、もっと高く遠くへ飛ぶ。飛んでいる間はだれもつかめない（2026/11/24） |
/// | 手前（Near Throw Distance）で押す | **両手で持って、向いている方向（前）へ投げる**（2026/11/24） |
/// | 最後まで押さない | 手前に着いたところで、**ふつうの引っ張りの強さで後ろへ引っ張り切る**（2026/11/24。前は前へ投げていた） |
/// | ほかの人も同じ物をつかむ | **引っ張り合い**（<see cref="SupplyTug"/>。2026/11/24）。力で負けた側の光線が切れる |
///
/// 引く時間を固定しているのは、ボタンを押すタイミングを毎回同じにするため。
///
/// **引いている途中で向きを変えると、物資はしなった光線に沿って、プレイヤーのまわりを前の側へ回り込む**（2026/11/4）。
/// 回り込む先は <see cref="SweeperShooter.TryGetBendDirection"/>（手元から光線が出ている向き）。向きを変えなければ今までどおりまっすぐ来る。
/// 着く時間は変わらない（クリティカルのタイミングも同じ）。前に来てから投げるので、「前へ」「後ろへ」がまっすぐ飛ぶ。
///
/// **回り込んでいる途中で押すと、回っている勢いを残して斜め後ろへ飛ぶ**（2026/11/4）。
/// 飛ぶ向きは「体の真後ろ」と「物資がいま動いている向き（接線）」を Throw Follow Motion の割合で混ぜる。
/// 0 で今までどおり真後ろ、1 で動いている向きそのまま。まっすぐ引いているときはどちらでも真後ろになる。
/// **混ぜる割合は、引き寄せの進み具合で切り替わる**（2026/11/10・小野田さんの案）。道のりの Throw Follow Switch（初期値 8 割）までは
/// Throw Follow Motion（接線に近い向き）、そこを超えたら Throw Follow Near（体の真後ろに、ほんの少しだけ接線を足す。初期値 0.15）。
/// クリティカルの合図は物資のまわりにだけ出す（ゲージなどの画面の表示は出さない）。
/// 1回押したら、それで決まる（連打で当てられないように）。
///
/// ### 重い物（<see cref="HeavyHookable"/>。宇宙ごみの特殊デブリ。2026/11/5）
///
/// 重い物は**投げられず、床の上を引きずるだけ**。光線が当たると、重さに合わせてゆっくり手前へ引きずり、
/// 1回で動かせる距離（Heavy Drag Distance ÷ 重さ）を引いたら、**その場に置いて離す**。途中で押しても、その場で離す。
/// クリティカルの合図は出ない。何回も撃って、少しずつゴールまで運ぶ遊び方になる。
///
/// ### 距離で変わる操作（設定「光線の操作：距離で変わる」。2026/11/17・大槻さんの案）
///
/// ポーズ画面の設定で選んだ人だけ（ボットはいつも今までどおり）。引き寄せはいつもどおりで、
/// **途中で押した瞬間の距離**（ビームの長さの上限 <see cref="SweeperShooter.MaxReach"/>。初期値 12m を 100% とした割合。溜めた長さには関係ない）で、飛ばし方と強さが決まる。
///
/// | 押した瞬間の距離 | どうなる |
/// | --- | --- |
/// | 0〜20%（Throw Zone End まで） | **前へ投げる**（いつもの前へ投げる速さ × 投げる強さ）。体のそばで強さ 1 → 20% で 0 |
/// | 20〜29%（Critical Near まで） | **引っ張る**＝いつもの後ろへの投げの速さ × 引っ張る強さ。**どこで押しても 0.5**（Middle Pull Strength） |
/// | 29〜32%（Critical Far まで） | **クリティカルプル**（今までどおり。強さは掛けない） |
/// | 32〜100% | **引っ張る**。100% で 0.2（Far Pull Min Strength）→ クリティカルに近づくほど強くなり、32% で 1 |
///
/// 前へ投げるの「0」は、Throw Min Strength で上げられる（初期値は 0）。（2026/11/18：引っ張りの強さを大槻さんの指定に変えた）
///
/// （大槻さんの指定：投げ 0〜20・引っ張り 20〜28・クリティカルプル 29〜31・引っ張り 32〜100。数字は、ビームの長さの上限（12m）に対する %。物資に当たった距離・溜めた長さではない）
///
/// **クリティカルプルは距離で取る**（大槻さんの指定）。Critical Near〜Critical Far の距離で押すと、
/// 今までどおりのクリティカルプル（強さは掛けない。エフェクトも同じ）。合図（縮んでくる輪・金色のフチ）は、物資がその距離を通る時間に合わせて出す。
/// **物資は一定の速さ（Distance Reel Speed）で、光線の形（しなっていれば曲線）にそって寄ってくる**（Reel Seconds の「何秒で着く」は使わない。大槻さんの指定）。
/// 押さずに手前まで来たときは、今までどおり前へ投げる（強さは掛けない）。重い物は今までどおり引きずるだけ。
///
/// ### クリティカルのエフェクト（仮。2026/11/4）
///
/// プレハブ（Critical Cue Prefab / Critical Hit Prefab）が空のときは、仮のエフェクトを出す。どれを出すかはインスペクターで選べる（組み合わせてもよい）。
///
/// | いつ | 仮のエフェクト |
/// | --- | --- |
/// | 合図（受付の Cue Lead 秒前から） | 縮んでくる輪（案1）・物資のフチが金色に光る（案4）・前からの広がる輪（受付が始まった瞬間） |
/// | 決まったとき | 星のかけらが飛び散る（案1）・物資が白く光って一瞬止まる（案4）・**推進の衝撃波**（物資の、引っ張られている側と逆の側から輪が噴き出す）・前からの大きな輪 |
///
/// 合図を受付より前から出すのは、受付が0.2秒しかなく、光ったのを見てから押すのでは間に合わないため。
///
/// ### 光線がちぎれる（2026/11/10）
///
/// 動く物（Is Kinematic の Rigidbody。回る床・上下する床・ベルトコンベア・動く障害物など）に物資を押し付けたまま引くと、
/// めり込みを押し戻す力で物資がものすごい勢いで飛んだり、回転が増え続けたりしていた。これを防ぐため、次のどちらかで**光線がちぎれ、物資はその場に落ちる**。
///
/// | ちぎれる条件 | 中身 |
/// | --- | --- |
/// | **たまった力** | 引きたい距離に対して、物資が動けなかった距離をためていく（引っかかりが取れていれば少しずつ減る）。**Snap Strain** m たまったらちぎれる。初期設定では、動く物に触れている間だけためる |
/// | **貫通** | 光線（手元から物資までの曲線）が動く物を **Kinematic Snap Seconds** 秒つらぬいたらちぎれる。**ふつうの壁（Rigidbody が無い物）では、ちぎれない** |
///
/// あわせて、引きずる速さに上限（**Max Drag Speed**）を付けた。前は、引っかかって遅れた分を残りの時間で取り戻そうとして、
/// 最後の一瞬に秒速数百 m を出すことがあった。
///
/// ## オンライン（宇宙ごみのオンラインの体。2026/10/28）
///
/// 物資に <see cref="NetSupply"/> が付いていて通信中なら、今の物資の通信の決まりに合わせる。
/// ・当たったら**ホストに引っ掛けを頼み**、認められて持ち主が自分に移ってから巻き取る（Reel Seconds はそこから数える）。
///   ほかの人が先に引っ掛けていた・返事が来ないときは、投げずにやめる
/// ・途中でホストに外された（爆風など）ら、投げずにやめる
/// ・投げるときは、ここで計算した速さを**そのままホストへ渡す**（落ちた場所・ゴールの判定はホスト）
/// </summary>
public class SweeperReel : MonoBehaviour
{
    [Header("巻き取り")]
    [Tooltip("当たってから手前に着くまでの秒数。距離に関係なく、いつもこの時間で着く")]
    [Min(0.1f)]
    [SerializeField] private float reelSeconds = 1.5f;

    [Tooltip("物資を止める位置（プレイヤーの中心からの距離 m）")]
    [Min(0.3f)]
    [SerializeField] private float stopDistance = 1.4f;

    [Tooltip("光線でつかんでいる間、物資の重力を切る（離したら元に戻す）。オフにすると前のように重力のまま床を引きずる")]
    [SerializeField] private bool noGravityWhileHeld = true;

    [Tooltip("重力を切っている間も、つかんだときの高さ（持つ高さ）より上に浮いたら重力で落とす（人や物にぶつかって跳ね上がったまま戻らないのを防ぐ。2026/11/24）")]
    [SerializeField] private bool holdAtGrabHeight = true;

    [Tooltip("持つ高さの上限（プレイヤーの足元から m）。跳ねて高い所にある物をつかんだときも、ここより上を持つ高さにしない")]
    [SerializeField] private float maxHoldAbove = 1.5f;

    [Tooltip("持つ高さより上にいるときに効かせる重力の強さ（1 でふつうの重力）")]
    [SerializeField] private float aboveHoldGravityScale = 1f;

    [Tooltip("引いている間の物資の回転を弱める強さ（0 で弱めない）。転がりすぎるときに上げる")]
    [Min(0f)]
    [SerializeField] private float spinDamping = 6f;

    [Tooltip("向きを変えたとき、物資がしなった光線に沿ってプレイヤーのまわりを回り込む速さの上限（度/秒）。0 で回り込まない（まっすぐ引く）")]
    [Min(0f)]
    [SerializeField] private float maxSwingSpeed = 540f;

    [Tooltip("引きずる速さの上限（m/秒）。0 で上限なし。ふつうに引くときは 7〜9 m/秒ほどなので、それより大きくしておく")]
    [Min(0f)]
    [SerializeField] private float maxDragSpeed = 20f;

    [Header("光線がちぎれる（2026/11/10）")]
    [Tooltip("引きたかったのに動けなかった距離（m）がこれだけたまったら、光線がちぎれて物資をその場に落とす。0 でちぎれない")]
    [Min(0f)]
    [SerializeField] private float snapStrain = 2f;

    [Tooltip("引っかかりが取れているとき、たまった距離が減る速さ（m/秒）。床との摩擦などの小さな遅れでちぎれないようにする")]
    [Min(0f)]
    [SerializeField] private float strainRecovery = 2f;

    [Tooltip("ON：たまった力を、動く物（Is Kinematic の Rigidbody）に触れている間だけためる。OFF：ふつうの壁に引っかかっていてもためる")]
    [SerializeField] private bool strainOnlyOnKinematic = true;

    [Tooltip("光線が動く物（Is Kinematic の Rigidbody）をつらぬいたら、ちぎれる。ふつうの壁ではちぎれない")]
    [SerializeField] private bool snapOnKinematicPierce = true;

    [Tooltip("動く物をこの秒数つらぬき続けたら、ちぎれる。0 ですぐ（一瞬かすっただけでちぎれないよう、少しだけ待つ）")]
    [Min(0f)]
    [SerializeField] private float kinematicSnapSeconds = 0.1f;

    [Tooltip("つらぬいたかを見るとき、光線の両端（手元と物資のそば）を何 m 見ないか。物資が乗っている床に、先端がかすってちぎれないようにする")]
    [Min(0f)]
    [SerializeField] private float pierceIgnoreEnds = 0.3f;

    [Tooltip("つらぬいたか・触れているかを見る相手のレイヤー")]
    [SerializeField] private LayerMask snapCheckMask = ~0;

    [Header("後ろへ投げる（途中で押したとき）")]
    [Tooltip("プレイヤーの中心から、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float backDistance = 6f;

    [Tooltip("放物線のいちばん高い所（プレイヤーの足元から m）。頭（約2m）より高くする")]
    [Min(0.5f)]
    [SerializeField] private float backArcHeight = 3.2f;

    [Tooltip("【引き寄せの前半（Throw Follow Switch まで）】投げる向きを、物資がいま動いている向き（回り込みの勢い＝接線）にどれだけ寄せるか。0 でいつも体の真後ろ、1 で動いている向きそのまま（ハンマー投げのように横へも飛ぶ）。まっすぐ引いているときはどちらでも真後ろ")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowMotion = 0.5f;

    [Tooltip("【引き寄せの後半（Throw Follow Switch を超えてから）】接線にどれだけ寄せるか。体の真後ろを基本に、ほんの少しだけ勢いを残す。0 で真後ろ")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowNear = 0.15f;

    [Tooltip("前半と後半を切り替える、引き寄せの進み具合。0.8 なら、当たった位置から止める位置（Stop Distance）までの道のりを 8 割引いたところで後半に切り替わる")]
    [Range(0f, 1f)]
    [SerializeField] private float throwFollowSwitch = 0.8f;

    [Header("押さずに手前まで来たとき（2026/11/24）")]
    [Tooltip("ON：押さずに手前（Stop Distance）まで来たら、ふつうの引っ張り（クリティカルではない後ろ投げ）の強さで、そのまま後ろへ引っ張り切る。OFF：前からの「前へはじく」。距離で変わる操作（パターンB）はいつも前へはじく")]
    [SerializeField] private bool arriveThrowsBack = true;

    [Header("手前で押すと両手で持って投げる（2026/11/24）")]
    [Tooltip("押した瞬間、物資がプレイヤーの中心からこの距離（m）より近ければ、両手で持って向いている方向へ投げる（クリティカルの受付中はクリティカルを優先）。0 でしない")]
    [SerializeField] private float nearThrowDistance = 2.4f;

    [Tooltip("両手で持つ位置：体の前へ何 m")]
    [SerializeField] private float carryForward = 0.75f;

    [Tooltip("両手で持つ位置：足元から何 m 上")]
    [SerializeField] private float carryHeight = 0.8f;

    [Tooltip("押してから、物資が両手の位置に来るまでの秒数")]
    [SerializeField] private float carryInSeconds = 0.1f;

    [Tooltip("両手の位置に来てから投げるまでの秒数（投げる動きの溜め。投げるアニメーション AN_Throw の振り下ろす瞬間＝約 0.73 秒に、Carry In Seconds と合わせて合わせる）")]
    [SerializeField] private float carryWindup = 0.25f;

    [Tooltip("投げた物が落ちる所：プレイヤーの中心から向いている方向へ何 m（ふつうの引っ張りの Back Distance くらい。クリティカルより弱く）")]
    [SerializeField] private float carryThrowDistance = 7f;

    [Tooltip("投げた物の放物線のいちばん高い所（足元から m）")]
    [SerializeField] private float carryArcHeight = 2.4f;

    [Tooltip("投げるアニメーション（Throw）を流している間は、持った物を両手の真ん中に合わせて動かす（2026/11/25）。OFF なら上の位置のまま")]
    [SerializeField] private bool carryWithHands = true;

    [Tooltip("両手に合わせるとき、両手の真ん中からさらに体の前へ何 m ずらすか（物が体にめり込まないように）")]
    [SerializeField] private float carryHandsForward = 0.3f;

    [Header("前へ投げる（距離で変わる操作で、押さなかったとき）")]
    [Tooltip("前へ飛ばす速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float forwardSpeed = 12f;

    [Tooltip("前へ飛ばすときの上向きの速さ（m/秒）。小さいほど床すれすれ")]
    [Min(0f)]
    [SerializeField] private float forwardLift = 1.5f;

    [Header("重い物（引きずるだけ。2026/11/5）")]
    [Tooltip("重さ 1 のとき、1回で引きずれる距離（m）。重さで割る（重さ 2 なら半分）")]
    [Min(0f)]
    [SerializeField] private float heavyDragDistance = 4f;

    [Tooltip("どんなに重くても、1回でこれだけは引きずれる距離（m）")]
    [Min(0f)]
    [SerializeField] private float heavyDragMinDistance = 0.8f;

    [Tooltip("重さ 1 のとき、引きずる速さ（m/秒）。重さで割る（重いほどゆっくり）")]
    [Min(0.1f)]
    [SerializeField] private float heavyDragSpeed = 3f;

    [Tooltip("重い物を止める位置（プレイヤーの中心からの距離 m）。重い物は大きいので、ふつうの物より手前で止める")]
    [Min(0.3f)]
    [SerializeField] private float heavyStopDistance = 2f;

    [Header("距離で変わる操作（設定「光線の操作：距離で変わる」のときだけ。2026/11/17）")]
    [Tooltip("押した瞬間、物資がこの割合（ビームの長さの上限 Max Reach を 1 とする）より近ければ前へ投げる（体のそばで強さ 1 → ここで 0）。遠ければ引っ張る")]
    [Range(0f, 1f)]
    [SerializeField] private float throwZoneEnd = 0.2f;

    [Tooltip("クリティカルプルを取れる距離の、近いほうの端（ビームの長さの上限に対する割合）。Throw Zone End からここまでは、引っ張る強さが 0 → 1 に増える")]
    [Range(0f, 1f)]
    [SerializeField] private float criticalNear = 0.29f;

    [Tooltip("クリティカルプルを取れる距離の、遠いほうの端（ビームの長さの上限に対する割合）。ここから上限までは、引っ張る強さが 1 → 0 に減る。Critical Near と同じにするとクリティカルなし")]
    [Range(0f, 1f)]
    [SerializeField] private float criticalFar = 0.32f;

    [Tooltip("前へ投げる強さの最小値（0〜1）。いちばん弱い所（Throw Zone End）で押したときの強さ。体のそばでは 1")]
    [Range(0f, 1f)]
    [SerializeField] private float throwMinStrength = 0f;

    [Tooltip("投げとクリティカルの間（Throw Zone End〜Critical Near）の引っ張る強さ（0〜1）。この間はどこで押しても同じ強さ")]
    [Range(0f, 1f)]
    [SerializeField] private float middlePullStrength = 0.5f;

    [Tooltip("クリティカルより遠い引っ張りの、いちばん弱い所（ビームの長さの上限）の強さ（0〜1）。クリティカルに近づくほど強くなり、Critical Far で 1")]
    [Range(0f, 1f)]
    [SerializeField] private float farPullMinStrength = 0.2f;

    [Tooltip("物資が寄ってくる速さ（m/秒）。この操作では、距離に関係なくいつもこの速さで近づき（Reel Seconds は使わない）、手前（Stop Distance）に着いたら前へ投げる")]
    [Min(0.1f)]
    [SerializeField] private float distanceReelSpeed = 6f;

    [Header("引っ張り合い（2人が同じ物をつかんだとき。2026/11/24）")]
    [Tooltip("ON：ほかの人がつかんでいる物も光線でつかめて、引っ張り合いになる。力の強いほうが残り、負けた側の光線が切れる")]
    [SerializeField] private bool tugEnabled = true;

    [Tooltip("ON：重い物（引きずるだけの特殊デブリ）でも引っ張り合いになる")]
    [SerializeField] private bool tugOnHeavy = false;

    [Tooltip("ON：味方どうしでも引っ張り合いになる")]
    [SerializeField] private bool tugWithTeammates = true;

    [Tooltip("いつも自分のほうへ引く力（m/秒²）")]
    [Min(0f)]
    [SerializeField] private float tugBasePull = 6f;

    [Tooltip("物資から離れる向きに歩いた速さ（m/秒）1 あたり、引く力をどれだけ足すか")]
    [Min(0f)]
    [SerializeField] private float tugYankGain = 3f;

    [Tooltip("体を回して光線の先を振る速さ（m/秒）1 あたり、引く力をどれだけ足すか（振り回すほど強い）")]
    [Min(0f)]
    [SerializeField] private float tugSwingGain = 0.6f;

    [Tooltip("振る速さを数える上限（m/秒）。速く回しすぎても、ここまでしか強くならない")]
    [Min(0f)]
    [SerializeField] private float tugMaxSwingTip = 12f;

    [Tooltip("振り回したときに、物資を横（回した向き）へ振る力の割合")]
    [Min(0f)]
    [SerializeField] private float tugSwingSide = 0.5f;

    [Tooltip("物資の速さが落ちていく強さ（大きいほど止まりやすい）")]
    [Min(0f)]
    [SerializeField] private float tugDrag = 3f;

    [Tooltip("引っ張り合いで物資が動く速さの上限（m/秒）")]
    [Min(0.1f)]
    [SerializeField] private float tugMaxSpeed = 10f;

    [Tooltip("引きたいのに物資が離れていった距離（m）がこれだけたまった側の光線が切れる。小さいほど早く決着する")]
    [Min(0.05f)]
    [SerializeField] private float tugSnapStrain = 1.2f;

    [Tooltip("物資が自分のほうへ来ている間、たまった距離が減る速さ（m/秒）")]
    [Min(0f)]
    [SerializeField] private float tugStrainRecovery = 1.5f;

    [Tooltip("この秒数たっても決着しなければ、両方の光線が切れる。0 で時間切れなし")]
    [Min(0f)]
    [SerializeField] private float tugMaxSeconds = 3f;

    [Tooltip("光線の長さの上限（Shooter の Max Reach）の何倍まで離れたら、その側の光線が切れるか")]
    [Min(0.5f)]
    [SerializeField] private float tugMaxLengthScale = 1.15f;

    [Tooltip("ON：引っ張り合いに負けた人のチームは、勝った人が持っている間その物をつかめない（引っ張り合いも挑めない）。勝った人が投げる・離すと解ける（2026/11/27）")]
    [SerializeField] private bool tugLoserTeamLockout = true;

    [Header("投げた直後・つかんだ直後のロック（2026/11/27）")]
    [Tooltip("投げた物は、この秒数のあいだ誰もつかめない（投げた瞬間にほかの人に取られないように）。0 でロックしない")]
    [Min(0f)]
    [SerializeField] private float throwLockSeconds = 0.75f;

    [Tooltip("つかんだ物（引っ張り合いに勝って取った物も）は、この秒数のあいだほかの人が引っ張り合いを挑めない。0 でロックしない")]
    [Min(0f)]
    [SerializeField] private float grabLockSeconds = 0.75f;

    [Header("クリティカルプル")]
    [Tooltip("クリティカルの受付が始まる時刻を、Reel Seconds（手前に着くまでの秒数）の割合で。0.5 なら引き寄せの真ん中。Reel Seconds を変えても同じ所に付いてくる（2026/11/24。前は秒で持っていた）")]
    [Range(0f, 1f)]
    [SerializeField] private float criticalStartShare = 0.5f;

    [Tooltip("ON：クリティカルで投げた物は、飛んでいる間（着くまでの時間＋Critical Protect Extra）だれもつかめない（2026/11/24）")]
    [SerializeField] private bool criticalCannotBeStolen = true;

    [Tooltip("クリティカルで投げた物を、着く予定の時間からさらに何秒つかめなくするか")]
    [Min(0f)]
    [SerializeField] private float criticalProtectExtra = 0.3f;

    [Tooltip("クリティカルの受付の長さ（秒）。短いほど難しい")]
    [Min(0.01f)]
    [SerializeField] private float criticalLength = 0.2f;

    [Tooltip("クリティカルのとき、後ろのどこに落とすか（m）")]
    [Min(0f)]
    [SerializeField] private float criticalBackDistance = 11f;

    [Tooltip("クリティカルのときの放物線のいちばん高い所（足元から m）")]
    [Min(0.5f)]
    [SerializeField] private float criticalArcHeight = 4.5f;

    [Header("クリティカルのエフェクト")]
    [Tooltip("受付が始まった瞬間に、物資の位置に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalCuePrefab;

    [Tooltip("クリティカルが決まった瞬間に出すエフェクトのプレハブ。空なら仮の光の輪を出す")]
    [SerializeField] private GameObject criticalHitPrefab;

    [Tooltip("仮の光の輪の色")]
    [SerializeField] private Color flashColor = new Color(1f, 0.82f, 0.2f, 1f);

    [Tooltip("仮の光の輪に使うマテリアル（Art/Materials/SweeperRing.mat）。空なら再生したときに作る")]
    [SerializeField] private Material flashMaterial;

    [Tooltip("物資の金色のフチに使うマテリアル（Art/Materials/SweeperItemOutline.mat）。空なら再生したときに作るが、ビルドでは出ないことがある")]
    [SerializeField] private Material outlineMaterial;

    [Tooltip("物資が白く光るのに使うマテリアル（Art/Materials/SweeperItemFlash.mat）。空なら再生したときに作るが、ビルドでは出ないことがある")]
    [SerializeField] private Material whiteFlashMaterial;

    [Header("クリティカルの仮のエフェクトの選び方（プレハブが空のとき）")]
    [Tooltip("合図を、受付が始まる何秒前から出すか（縮んでくる輪・金色のフチ）。0 なら受付が始まった瞬間。Critical Start より長くはならない")]
    [Min(0f)]
    [SerializeField] private float cueLead = 0.5f;

    [Tooltip("合図：縮んでくる輪（案1）。物資のふちの輪に重なった瞬間が受付の始まり")]
    [SerializeField] private bool cueShrinkRing = true;

    [Tooltip("合図：物資のフチが金色に光る（案4）。だんだん強くなり、受付の間はくっきり光る")]
    [SerializeField] private bool cueGoldOutline = true;

    [Tooltip("合図：受付が始まった瞬間に、足元に光の輪が広がる（前からの仮）")]
    [SerializeField] private bool cueSpreadRing;

    [Tooltip("金色のフチの厚み（m）")]
    [Min(0.005f)]
    [SerializeField] private float outlineWidth = 0.05f;

    [Tooltip("決まったとき：星のかけらが飛び散る（案1）")]
    [SerializeField] private bool hitShards = true;

    [Tooltip("決まったとき：物資が白く光る（案4）")]
    [SerializeField] private bool hitWhiteFlash = true;

    [Tooltip("決まったとき：物資をその場で止めてから飛ばす秒数（案4）。0 で止めない")]
    [Min(0f)]
    [SerializeField] private float hitStopSeconds = 0.06f;

    [Tooltip("決まったとき：推進の衝撃波。物資の、引っ張られている側と逆の側から輪が噴き出し、飛んでいく間も少し後ろへ出し続ける")]
    [SerializeField] private bool hitThrustWave = true;

    [Tooltip("決まったとき：足元に大きな光の輪が広がる（前からの仮）")]
    [SerializeField] private bool hitSpreadRing;

    private Collider playerCollider;
    private SweeperShooter shooter;
    private SweeperAnimation animationPlayer;

    /// <summary>つかむ前の物資の重力の設定（離したときに戻す）。</summary>
    private bool itemGravityWas;

    /// <summary>この巻き取りで、物資の重力を切っているか。</summary>
    private bool gravityCut;
    private HookableObject item;
    private Rigidbody body;
    private float startTime;
    private bool cueShown;

    /// <summary>持つ高さ（ワールドの y）。つかんだときの高さ。これより上に浮いたら重力で落とす（2026/11/24）。</summary>
    private float holdY;

    /// <summary>持つ高さのまわり、どれだけのずれは「その高さにいる」とみなすか（m）。</summary>
    private const float HoldTolerance = 0.05f;

    /// <summary>持つ高さより下に押し下げられたとき、下向きの速さを止める速さ（m/秒²）。持ち上げはしない。</summary>
    private const float HoldSettle = 20f;

    /// <summary>両手で持っている最中か（押してから投げるまで。2026/11/24）。</summary>
    private bool carrying;

    /// <summary>両手で持ち始めた時刻と、そのときの物資の位置。</summary>
    private float carryStartTime;
    private Vector3 carryFrom;

    // ---- 引っ張り合い（2026/11/24） ----

    /// <summary>いま有効な巻き取りの一覧（通信なしで、同じ物資をつかんでいるほかの体を探すのに使う）。</summary>
    private static readonly System.Collections.Generic.List<SweeperReel> AllReels = new System.Collections.Generic.List<SweeperReel>();

    /// <summary>引っ張り合いの最中か。この間は物資を自分で動かさない（<see cref="SupplyTug"/> が動かす）。</summary>
    private bool tugging;

    /// <summary>通信なしの引っ張り合い（オンラインはホストが持っているので null）と、自分がどちら側か（0 つかんでいた・1 後から来た）。</summary>
    private SupplyTug localTug;
    private int tugSide;

    /// <summary>オンライン：引っ張り合いにしたいとホストへ頼んで、返事を待っている間。</summary>
    private bool tugJoinPending;

    /// <summary>オンライン：引っ張り合いに勝って、物資の持ち主が自分に移るのを待っている間。</summary>
    private bool tugWonPending;
    private float tugWonWait;

    /// <summary>引っ張り合いで光線が切れたときの <see cref="LastResult"/>。</summary>
    private const string TugLostResult = "引っ張り合いで切れた";

    /// <summary>巻き取りで物資を動かしている横の速さ（FixedUpdate で決めた値。ぶつかった跳ね返りは入らない）。投げる向きに使う。</summary>
    private Vector3 reelVelocity;

    /// <summary>これより遅いときは「動いている向き」が当てにならないので、体の真後ろへ投げる（m/秒）。</summary>
    private const float MinFollowSpeed = 0.5f;

    /// <summary>引き始めたときの、プレイヤーの中心から物資までの距離（真上から見た距離）。引き寄せの進み具合を出すのに使う。</summary>
    private float reelStartDistance;

    /// <summary>いま引いている物が重い物か（<see cref="Begin"/> で決める）。</summary>
    private bool heavy;

    /// <summary>重い物の重さ（1 が基準）。</summary>
    private float heavyWeight = 1f;

    /// <summary>重い物を、この1回であと何m引きずれるか。</summary>
    private float heavyRemaining;

    /// <summary>重い物が回り込む速さの上限を、ふつうの物（Max Swing Speed）の何倍にするか（そこからさらに重さで割る）。</summary>
    private const float HeavySwingShare = 0.25f;

    /// <summary>距離で変わる操作で引いているか（<see cref="Begin"/> で決める）。</summary>
    private bool distanceMode;

    /// <summary>
    /// この巻き取りで、合図を出すのに使うクリティカルの受付の始まりと長さ（当たってからの秒数）。
    /// ふだんは Critical Start / Critical Length。距離で変わる操作では、物資がクリティカルの距離を通る時間を見積もって入れる。
    /// </summary>
    private float windowStart = 1f;
    private float windowLength = 0.2f;

    /// <summary>この体の入力（ボットか、光線の操作の設定を見るのに使う）。</summary>
    private PlayerInputSource input;

    /// <summary>自分の参加番号を持つ通信上のプレイヤー（オフラインの検証シーンでは null）。</summary>
    private SweeperNetPlayer netPlayer;

    /// <summary>巻き取っている物資の通信の部品。オンラインで巻き取っている間だけ入る。</summary>
    private NetSupply supply;

    /// <summary>ホストの返事（引っ掛けてよいか）を待っている間。この間は引かない。</summary>
    private bool waitingForClaim;

    /// <summary>返事を待ち始めてからの秒数。</summary>
    private float claimWait;

    /// <summary>これだけ待っても返事が来なければ、あきらめて光線を戻す（秒）。</summary>
    private const float ClaimTimeout = 1.5f;

    // ---- 光線がちぎれる（2026/11/10） ----

    /// <summary>ちぎれたときの <see cref="LastResult"/>。</summary>
    private const string SnapResult = "ちぎれた";

    /// <summary>引きたかったのに動けなかった距離のたまり（m）。<see cref="snapStrain"/> を超えたらちぎれる。</summary>
    private float strain;

    /// <summary>前のステップで速さを入れたときの物資の位置と、そのステップで動かしたかった横の距離。</summary>
    private Vector3 lastPosition;
    private Vector3 lastStep;
    private bool hasLastStep;

    /// <summary>光線が動く物をつらぬき続けている秒数。</summary>
    private float pierceTime;

    /// <summary>光線の形（手元→物資）を受け取る入れ物。</summary>
    private readonly Vector3[] beamPath = new Vector3[9];

    /// <summary>当たり判定を受け取る入れ物（毎回作らないように使い回す）。</summary>
    private readonly RaycastHit[] pierceHits = new RaycastHit[16];
    private readonly Collider[] touchBuffer = new Collider[16];

    /// <summary>物資のまわり、どれだけ離れていても「触れている」とみなすか（m）。</summary>
    private const float TouchMargin = 0.05f;

    /// <summary>エフェクトの種類（オンラインでほかの人の画面にも出すときに使う）。</summary>
    public enum Effect
    {
        CriticalCue,
        CriticalHit,
        Snap // 光線がちぎれた（2026/11/10）
    }

    /// <summary>エフェクトを出した瞬間に呼ばれる（<see cref="SweeperNetSync"/> がほかの人へ知らせる）。</summary>
    public event System.Action<Effect, HookableObject> EffectShown;

    /// <summary>巻き取っている最中か。</summary>
    public bool IsActive => item != null;

    /// <summary>巻き取っている物資。</summary>
    public HookableObject Item => item;

    /// <summary>当たってから（オンラインでは、ホストに認められてから）の秒数。</summary>
    public float Elapsed => IsActive && !waitingForClaim && !tugging && !tugWonPending ? Time.time - startTime : 0f;

    /// <summary>いま引っ張り合いの最中か（2026/11/24）。</summary>
    public bool IsTugging => IsActive && tugging;

    /// <summary>いまクリティカルの受付中か（重い物を引きずっている間・引っ張り合いの最中は、いつも false）。</summary>
    public bool InCriticalWindow => IsActive && !waitingForClaim && !heavy && !tugging && !tugWonPending
        && (distanceMode ? InCriticalBand() : Elapsed >= CriticalStartSeconds && Elapsed <= CriticalStartSeconds + criticalLength);

    /// <summary>いま重い物を引きずっているか。</summary>
    public bool IsHeavyDrag => IsActive && heavy;

    /// <summary>オンラインでホストの返事（引っ掛けてよいか）を待っている間か。</summary>
    public bool IsWaitingForClaim => IsActive && waitingForClaim;

    // ---- ボット（SweeperBot）が、投げる先を決めるのに読む値 ----

    /// <summary>当たってから手前に着くまでの秒数（押さなければ、このとき前へ投げる）。</summary>
    public float ReelSeconds => reelSeconds;

    /// <summary>後ろへ投げたとき、プレイヤーの中心からどれだけ離れた所に落ちるか（m）。</summary>
    public float BackDistance => backDistance;

    /// <summary>クリティカルのとき、プレイヤーの中心からどれだけ離れた所に落ちるか（m）。</summary>
    public float CriticalBackDistance => criticalBackDistance;

    /// <summary>当たってから、クリティカルの受付が終わるまでの秒数。</summary>
    public float CriticalWindowEnd => CriticalStartSeconds + criticalLength;

    /// <summary>当たってから、クリティカルの受付が始まるまでの秒数。</summary>
    public float CriticalWindowStart => CriticalStartSeconds;

    /// <summary>当たってから、クリティカルの受付が始まるまでの秒数（Reel Seconds × Critical Start Share）。</summary>
    private float CriticalStartSeconds => reelSeconds * criticalStartShare;

    /// <summary>両手で持っている最中か（押してから投げるまで）。アニメーションや、ぶつかった判定で読む。</summary>
    public bool IsCarrying => IsActive && carrying;

    /// <summary>両手で持ち始めた瞬間に呼ばれる（投げるアニメーションを始めるのに使う予定）。</summary>
    public event System.Action CarryStarted;

    /// <summary>両手で持った物を投げた瞬間に呼ばれる。</summary>
    public event System.Action CarryThrown;

    /// <summary>
    /// 物資を後ろへ引っ張った（押した・クリティカル・手前まで来て引っ張り切った）瞬間に呼ばれる（2026/11/26）。
    /// 引数は、物資が実際に飛び出すまでの秒数（クリティカルの一瞬止まる間。ふだんは 0）。引っ張るアニメーションの「いちばん引く所」を飛び出す瞬間に合わせるのに使う。
    /// </summary>
    public event System.Action<float> PulledBack;

    /// <summary>
    /// **当たってから、物資が「両手で持って投げる」距離（Near Throw Distance）に入るまでの秒数**（ボットが押す時刻に使う）。
    /// いつもの引き寄せは、距離が時間に比例して縮むので計算で出せる。初めからその距離にいれば 0。
    /// </summary>
    public float NearThrowStartTime
    {
        get
        {
            float span = reelStartDistance - stopDistance;
            if (nearThrowDistance <= 0f || span <= 0.01f || reelStartDistance <= nearThrowDistance)
            {
                return 0f;
            }

            return Mathf.Clamp01((reelStartDistance - nearThrowDistance) / span) * reelSeconds;
        }
    }

    /// <summary>合図を受付の何秒前から出すか（当たってからの秒数より前にはならない）。</summary>
    private float CueLead => Mathf.Min(cueLead, windowStart);

    /// <summary>
    /// その物資を引っ掛けてよいか。オンラインで**ほかの人が引っ掛けている物は false**（すり抜ける）。
    /// オフラインの検証シーンでは、いつも true。
    /// </summary>
    public bool CanHook(HookableObject target)
    {
        if (target == null)
        {
            return false;
        }

        NetSupply netSupply = target.GetComponent<NetSupply>();
        bool online = netSupply != null && netSupply.IsSpawned && IsOnline;

        // 自分がつかんでいる物は、ロックに関係なくつかみ直せる
        if (online && netSupply.IsClaimed && netSupply.HookedByPlayerIndex == MyIndex)
        {
            return true;
        }

        // クリティカルで飛んでいる物・投げた直後の物・つかまれた直後の物は、だれもつかめない（2026/11/24・11/27）
        if (target.IsProtected)
        {
            return false;
        }

        if (!online)
        {
            // 通信なし：ほかの体がつかんでいる物は、引っ張り合いになれるときだけ
            return !target.IsHooked || CanJoinLocalTug(target, out _);
        }

        if (!netSupply.IsClaimed)
        {
            return true;
        }

        // ほかの人がつかんでいる物は、引っ張り合いになれるときだけ（決めるのはホスト）
        return CanJoinOnlineTug(target, netSupply);
    }

    // ------------------------------------------------------------
    // 引っ張り合い（2026/11/24）
    // ------------------------------------------------------------

    /// <summary>引っ張り合いの値（<see cref="SupplyTug"/> に渡す）。</summary>
    public SupplyTug.Settings TugSettings => new SupplyTug.Settings
    {
        basePull = tugBasePull,
        yankGain = tugYankGain,
        swingGain = tugSwingGain,
        maxSwingTip = tugMaxSwingTip,
        swingSide = tugSwingSide,
        drag = tugDrag,
        maxSpeed = tugMaxSpeed,
        snapStrain = tugSnapStrain,
        strainRecovery = tugStrainRecovery,
        maxSeconds = tugMaxSeconds,
        winDistance = stopDistance,
        maxLength = BeamReach * tugMaxLengthScale,
        handHeight = 0f,
    };

    /// <summary>この物資で、つかんでいた人（チーム <paramref name="holderTeam"/>）と後から来た人で引っ張り合いにしてよいか（ホストも使う）。</summary>
    public bool AllowsTug(HookableObject target, int holderTeam, int challengerTeam)
    {
        if (!tugEnabled || target == null || target.IsVanished || target.IsProtected)
        {
            return false;
        }

        if (!tugOnHeavy && HeavyHookable.TryGetWeight(target, out _))
        {
            return false;
        }

        return tugWithTeammates || holderTeam < 0 || challengerTeam < 0 || holderTeam != challengerTeam;
    }

    /// <summary>通信なし：この物資をつかんでいるほかの体の巻き取り（無ければ null）。</summary>
    private SweeperReel FindLocalHolder(HookableObject target)
    {
        foreach (SweeperReel other in AllReels)
        {
            if (other != null && other != this && other.IsActive && other.item == target)
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>通信なし：ほかの体がつかんでいるこの物資に、引っ張り合いで加われるか。</summary>
    private bool CanJoinLocalTug(HookableObject target, out SweeperReel holder)
    {
        holder = FindLocalHolder(target);
        return holder != null && !holder.tugging && !holder.waitingForClaim && !holder.IsCarrying && !holder.InCriticalWindow
            && SupplyTug.Of(target) == null && holder.AllowsTug(target, -1, -1)
            // 引っ張り合いに負けた人（のチーム）は、勝った人が持っている間は挑めない（2026/11/27）
            && !target.IsTugLockedOut(holder, this, MyTeam);
    }

    /// <summary>オンライン：ほかの人がつかんでいるこの物資に、引っ張り合いで加われそうか（最後はホストが決める）。</summary>
    private bool CanJoinOnlineTug(HookableObject target, NetSupply netSupply)
    {
        if (netSupply.IsTugging || SweeperNetSync.IsPlayerCarrying(netSupply.HookedByPlayerIndex))
        {
            return false;
        }

        // 引っ張り合いに負けた人（のチーム）は、勝った人が持っている間は挑めない（2026/11/27。最後はホストも確かめる）
        if (netSupply.IsTugLockedOut(MyIndex, MyTeam))
        {
            return false;
        }

        int holderTeam = SweeperNetPlayer.TeamOfPlayerIndex(netSupply.HookedByPlayerIndex);
        return AllowsTug(target, holderTeam, netPlayer != null ? netPlayer.TeamIndex : -1);
    }

    private void OnEnable()
    {
        if (!AllReels.Contains(this))
        {
            AllReels.Add(this);
        }
    }

    /// <summary>通信なし：2つの体で引っ張り合いを始める（後から来た体の <see cref="Begin"/> から）。</summary>
    private void StartLocalTug(SweeperReel holder)
    {
        SweeperReel challenger = this;
        SupplyTug tug = SupplyTug.Begin(item, holder.transform, transform, holder.TugSettings,
            result => OnLocalTugEnded(holder, challenger, result));
        if (tug == null)
        {
            return;
        }

        // 重力は、つかんでいた人がつかむ前の設定を引き継ぐ（自分がつかんだときには、もう切れていたため）
        itemGravityWas = holder.itemGravityWas;

        holder.EnterTug(tug, 0);
        EnterTug(tug, 1);
    }

    /// <summary>引っ張り合いに入る。</summary>
    private void EnterTug(SupplyTug tug, int side)
    {
        tugging = true;
        localTug = tug;
        tugSide = side;
        waitingForClaim = false;
        tugJoinPending = false;
    }

    /// <summary>通信なしの引っ張り合いが決着した。</summary>
    private static void OnLocalTugEnded(SweeperReel holder, SweeperReel challenger, int result)
    {
        HookableObject target = holder.item != null ? holder.item : challenger.item;
        bool lockout = holder.tugLoserTeamLockout;
        switch (result)
        {
            case SupplyTug.HolderLost:
                holder.LoseTug(true);
                challenger.WinTug();
                if (lockout && challenger.IsActive && challenger.item == target)
                {
                    target.SetTugLock(challenger, holder, holder.MyTeam);
                }
                break;
            case SupplyTug.ChallengerLost:
                challenger.LoseTug(true);
                holder.WinTug();
                if (lockout && holder.IsActive && holder.item == target)
                {
                    target.SetTugLock(holder, challenger, challenger.MyTeam);
                }
                break;
            default:
                challenger.LoseTug(true);
                holder.LoseTug(false);
                break;
        }
    }

    /// <summary>自分のチーム（オンラインでチームが決まっているときだけ。ほかは -1）。</summary>
    private int MyTeam => netPlayer != null && netPlayer.IsSpawned ? netPlayer.TeamIndex : -1;

    /// <summary>投げた物を誰もつかめなくする秒数（ホストも読む）。</summary>
    public float ThrowLockSeconds => throwLockSeconds;

    /// <summary>つかんだ物にほかの人が引っ張り合いを挑めない秒数（ホストも読む）。</summary>
    public float GrabLockSeconds => grabLockSeconds;

    /// <summary>引っ張り合いに負けた人のチームを、勝った人が持っている間しめ出すか（ホストも読む）。</summary>
    public bool TugLoserTeamLockout => tugLoserTeamLockout;

    /// <summary>引っ張り合いに勝った：いつもの引き寄せに戻り、いまの位置から引き直す（時間とクリティカルも数え直す）。</summary>
    private void WinTug()
    {
        if (!IsActive)
        {
            return;
        }

        tugging = false;
        localTug = null;
        ResumeAfterTug();

        // 取った直後は、ほかの人が続けて引っ張り合いを挑めない（2026/11/27）。オンラインはホストが記録する
        if (supply == null && item != null && grabLockSeconds > 0f)
        {
            item.Protect(grabLockSeconds);
        }
    }

    /// <summary>いまの位置から引き寄せをやり直す。</summary>
    private void ResumeAfterTug()
    {
        waitingForClaim = false;
        tugWonPending = false;
        startTime = Time.time;
        reelStartDistance = FlatDistanceToPlayer();
        cueShown = false;
        windowStart = CriticalStartSeconds;
        windowLength = criticalLength;
        strain = 0f;
        hasLastStep = false;
        pierceTime = 0f;
        if (body != null)
        {
            body.WakeUp();
        }
    }

    /// <summary>
    /// 引っ張り合いに負けた：光線が切れる。<paramref name="handOver"/> が true なら、物資の重力は戻さない
    /// （相手がまだつかんでいるので、相手が離すときに戻す）。
    /// </summary>
    private void LoseTug(bool handOver)
    {
        if (!IsActive)
        {
            return;
        }

        tugging = false;
        localTug = null;
        if (handOver)
        {
            gravityCut = false;
        }

        PlayEffect(Effect.Snap);
        Finish(TugLostResult, false, false);
    }

    /// <summary>通信上のプレイヤーとして動いているか。</summary>
    private bool IsOnline => netPlayer != null && netPlayer.IsSpawned;

    /// <summary>自分の参加番号（物資の通信で「誰が引っ掛けているか」に使う）。</summary>
    private int MyIndex => netPlayer != null ? Mathf.Max(0, netPlayer.PlayerIndex) : 0;

    /// <summary>最後の投げがクリティカルだったか（エフェクトや検証用の表示に使う）。</summary>
    public bool LastWasCritical { get; private set; }

    /// <summary>最後の投げの結果（「後ろ」「前」「クリティカル」）。検証用の表示に使う。</summary>
    public string LastResult { get; private set; } = "";

    /// <summary>最後の巻き取りが、光線がちぎれて終わったか。</summary>
    public bool LastWasSnapped => LastResult == SnapResult;

    private void Awake()
    {
        playerCollider = GetComponent<Collider>();
        shooter = GetComponent<SweeperShooter>();
        animationPlayer = GetComponent<SweeperAnimation>();
        netPlayer = GetComponent<SweeperNetPlayer>();
        input = PlayerInputSource.Get(this);
        windowStart = CriticalStartSeconds;
        windowLength = criticalLength;

        // 引っ張っている物にぶつかった人を吹き飛ばす部品（2026/11/21）。プレハブに無ければここで付ける
        if (GetComponent<SweeperPulledHit>() == null)
        {
            gameObject.AddComponent<SweeperPulledHit>();
        }
    }

    /// <summary>つかんだまま体が消えた・止められたときに、物資が浮いたままにならないよう重力だけは戻す。</summary>
    private void OnDisable()
    {
        AllReels.Remove(this);

        // 引っ張り合いの最中に体が止められたら、相手の勝ちにする（相手がつかんだままなので、重力は戻さない）
        if (IsActive && tugging && localTug != null)
        {
            Finish("止められた", false, false);
            return;
        }

        RestoreGravity();
    }

    /// <summary>巻き取りを始める。物資の Rigidbody の物理はそのまま使う（つかんでいる間は重力を切る）。</summary>
    public void Begin(HookableObject target)
    {
        if (target == null || target.Body == null)
        {
            return;
        }

        // 前の物の重力を切ったままなら、先に戻す（念のため）
        RestoreGravity();

        // ほかの人がつかんでいる物なら、引っ張り合いにする（2026/11/24）
        NetSupply joinSupply = target.GetComponent<NetSupply>();
        bool onlineSupply = IsOnline && joinSupply != null && joinSupply.IsSpawned;
        bool joinOnlineTug = onlineSupply && joinSupply.IsClaimed && joinSupply.HookedByPlayerIndex != MyIndex;
        SweeperReel localHolder = null;
        if (!onlineSupply && target.IsHooked && !CanJoinLocalTug(target, out localHolder))
        {
            return;
        }

        tugging = false;
        localTug = null;
        tugJoinPending = false;
        tugWonPending = false;

        item = target;
        body = target.Body;
        startTime = Time.time;
        cueShown = false;
        waitingForClaim = false;
        supply = null;
        reelVelocity = Vector3.zero;
        reelStartDistance = FlatDistanceToPlayer();
        windowStart = CriticalStartSeconds;
        windowLength = criticalLength;
        strain = 0f;
        hasLastStep = false;
        pierceTime = 0f;
        carrying = false;
        // 持つ高さ＝つかんだときの高さ（高すぎるときは足元から Max Hold Above まで）
        holdY = Mathf.Min(body.position.y, transform.position.y + maxHoldAbove);

        // 重い物は投げずに引きずるだけ。1回で引きずれる距離は重さで割る
        heavy = HeavyHookable.TryGetWeight(target, out heavyWeight);
        heavyWeight = Mathf.Max(0.1f, heavyWeight);
        heavyRemaining = heavy ? Mathf.Max(heavyDragDistance / heavyWeight, heavyDragMinDistance) : 0f;
        distanceMode = !heavy && input != null && input.BeamOperation == BeamOperationType.DistanceStrength;
        if (distanceMode)
        {
            EstimateCriticalWindow();
        }

        // オンラインなら、ホストに引っ掛けを頼む。認められるまでは引かない
        NetSupply netSupply = target.GetComponent<NetSupply>();
        if (IsOnline && netSupply != null && netSupply.IsSpawned)
        {
            supply = netSupply;
            waitingForClaim = true;
            claimWait = 0f;
            if (joinOnlineTug)
            {
                // ほかの人がつかんでいる：引っ張り合いにしたいと頼む（認められるとホストが物資を動かす）
                tugJoinPending = true;
                supply.RequestTug(MyIndex);
            }
            else
            {
                supply.RequestClaim(MyIndex);
            }
        }

        item.SetHooked(true);
        // 手前まで来たときに、自分の体とぶつかって跳ねないようにする
        ThrowPassThrough.Hold(item.gameObject, playerCollider);
        CutGravity();
        body.WakeUp();

        if (localHolder != null)
        {
            StartLocalTug(localHolder);
        }
        else if (supply == null && grabLockSeconds > 0f)
        {
            // つかんだ直後は、ほかの人が引っ張り合いを挑めない（2026/11/27）。オンラインはホストが記録する（NetSupply）
            item.Protect(grabLockSeconds);
        }

        LightFuseIfBomb(target);
    }

    /// <summary>
    /// **爆弾をつかんだら、導火線に火をつける**（つかんだ瞬間から数え始める）。
    /// 旧フック（<see cref="HookController"/>）と同じ決まり。新しい光線（溜め撃ち・鞭）の
    /// どちらでつかんでもここを通る。オンラインでは、火がついたことをホスト経由で全員に配る
    /// （手元だけで火をつけると、ほかの人には突然爆発したように見えてしまう）。
    /// </summary>
    private void LightFuseIfBomb(HookableObject target)
    {
        ExplosiveObject bomb = target.GetComponent<ExplosiveObject>();
        if (bomb == null || bomb.FuseLit)
        {
            return;
        }

        NetBomb netBomb = target.GetComponent<NetBomb>();
        if (IsOnline && netBomb != null && netBomb.IsSpawned)
        {
            netBomb.RequestLightFuse();
        }
        else
        {
            bomb.LightFuse();
        }
    }

    /// <summary>
    /// 巻き取りの途中でボタンが押された。後ろへ投げる（受付中ならクリティカル）。
    /// オンラインでホストの返事を待っている間（ほんの一瞬）は、押しても何もしない。
    /// </summary>
    public void Press()
    {
        if (!IsActive || waitingForClaim || carrying || tugging || tugWonPending)
        {
            return;
        }

        // 重い物は投げられない。押したら、その場に置いて離す
        if (heavy)
        {
            ReleaseHeavy();
            return;
        }

        if (distanceMode)
        {
            PressByDistance();
            return;
        }

        bool critical = InCriticalWindow;

        // 手前まで来ていたら、両手で持って向いている方向へ投げる（2026/11/24・小野田さんの指定）
        if (!critical && nearThrowDistance > 0f && FlatDistanceToPlayer() <= nearThrowDistance)
        {
            BeginCarry();
            return;
        }

        ThrowBack(critical);
        Finish(critical ? "クリティカル" : "後ろ", critical, false);
    }

    /// <summary>両手で持ち始める。物資は両手の位置へ寄り、溜めのあと <see cref="ThrowCarry"/> で投げる。</summary>
    private void BeginCarry()
    {
        carrying = true;
        carryStartTime = Time.time;
        carryFrom = body != null ? body.position : transform.position;
        CarryStarted?.Invoke();
    }

    /// <summary>両手で持つ位置。投げるアニメーションを流している間は両手の真ん中（の少し前）、それ以外は体の前・胸の高さ。</summary>
    private Vector3 CarryPoint
    {
        get
        {
            if (carryWithHands && animationPlayer != null && animationPlayer.TryGetHandsCenter(out Vector3 hands))
            {
                return hands + Flat(transform.forward) * carryHandsForward;
            }

            return transform.position + Flat(transform.forward) * carryForward + Vector3.up * carryHeight;
        }
    }

    /// <summary>
    /// **両手で持った物を、向いている方向へ投げる**（2026/11/24）。落ちる所はプレイヤーの中心から前へ Carry Throw Distance。
    /// 初速の計算は後ろへの投げと同じ（<see cref="BackThrowVelocity"/>）。アニメーションの「投げる瞬間」から呼べるよう public にしてある。
    /// </summary>
    public void ThrowCarry()
    {
        if (!IsCarrying || body == null)
        {
            return;
        }

        Vector3 from = body.position;
        Vector3 landing = transform.position + Flat(transform.forward) * carryThrowDistance;
        landing.y = from.y;
        float apexY = Mathf.Max(transform.position.y + carryArcHeight, from.y + 0.5f);
        Launch(body, supply, BackThrowVelocity(from, landing, apexY));
        CarryThrown?.Invoke();
        Finish("前", false, false);
    }

    /// <summary>
    /// 距離で変わる操作の 100%：**ビームの長さの上限**（<see cref="SweeperShooter.MaxReach"/>。初期値 12m。大槻さんの指定）。
    /// 溜めた長さ・物資に当たった距離には関係なく、いつも同じ長さで割る。
    /// </summary>
    private float BeamReach => Mathf.Max(0.1f, shooter != null ? shooter.MaxReach : 12f);

    /// <summary>
    /// **狙いの帯の色分けに使う、距離で変わる操作のゾーンの境目**（プレイヤーの中心からの m）。
    /// この体が距離で変わる操作のとき true（ボット・いつもの操作では false）。<see cref="SweeperShooter"/> が溜めている間に読む。
    /// </summary>
    public bool TryGetDistanceZones(out float throwEnd, out float criticalNearDistance, out float criticalFarDistance)
    {
        float reach = BeamReach;
        throwEnd = throwZoneEnd * reach;
        criticalNearDistance = criticalNear * reach;
        criticalFarDistance = criticalFar * reach;
        return input != null && input.BeamOperation == BeamOperationType.DistanceStrength;
    }

    /// <summary>距離で変わる操作で、いま物資がクリティカルプルを取れる距離（Critical Near〜Critical Far）にいるか。</summary>
    private bool InCriticalBand()
    {
        float ratio = FlatDistanceToPlayer() / BeamReach;
        return ratio >= criticalNear && ratio < criticalFar;
    }

    /// <summary>
    /// 距離で変わる操作で、物資がクリティカルの距離を通る時間を見積もり、合図の時間（<see cref="windowStart"/>・<see cref="windowLength"/>）に入れる。
    /// この操作では物資が一定の速さ（Distance Reel Speed）で寄ってくるので、「距離の差 ÷ 速さ」で計算できる。
    /// 初めからクリティカルの距離より近いときは、合図を出さない。
    /// </summary>
    private void EstimateCriticalWindow()
    {
        float reach = BeamReach;
        float far = criticalFar * reach;
        float near = Mathf.Max(stopDistance, criticalNear * reach);
        float span = reelStartDistance - stopDistance;

        if (far <= near || span <= 0.01f || reelStartDistance <= near)
        {
            windowStart = float.PositiveInfinity;
            windowLength = 0f;
            return;
        }

        float start = Mathf.Max(0f, reelStartDistance - far) / distanceReelSpeed;
        float end = (reelStartDistance - near) / distanceReelSpeed;
        windowStart = start;
        windowLength = Mathf.Max(0.01f, end - start);
    }

    /// <summary>
    /// **距離で変わる操作で押された**（大槻さんの図。2026/11/17）。クリティカルの距離なら今までどおりのクリティカルプル。それ以外は、押した瞬間の距離で、前へ投げるか引っ張る（後ろへ）かを決め、
    /// いつもの飛ぶ速さ（向きと大きさ）に強さ（0〜1）を掛けて飛ばす。距離は、ビームの長さの上限（<see cref="BeamReach"/>）の割合で見る。
    /// 投げる強さ：体のそばで 1、Throw Zone End で Throw Min Strength（まっすぐ）。
    /// 引っ張る強さ：投げとクリティカルの間はいつも Middle Pull Strength（0.5）、クリティカルより遠くは Critical Far で 1 → ビームの長さの上限で Far Pull Min Strength（0.2。まっすぐ）。
    /// </summary>
    private void PressByDistance()
    {
        // クリティカルの距離（引っ張る強さがいちばん強い所のまわり）なら、今までどおりのクリティカルプル（強さは掛けない。大槻さんの指定）
        if (InCriticalWindow)
        {
            ThrowBack(true);
            Finish("クリティカル", true, false);
            return;
        }

        float ratio = Mathf.Clamp01(FlatDistanceToPlayer() / BeamReach);

        if (ratio < throwZoneEnd)
        {
            float throwStrength = Mathf.Lerp(throwMinStrength, 1f, 1f - ratio / throwZoneEnd);
            ThrowForward(throwStrength);
            Finish($"前（強さ {throwStrength:F2}）", false, false);
            return;
        }

        float pullStrength = ratio < criticalNear
            ? middlePullStrength
            : Mathf.Lerp(farPullMinStrength, 1f, Mathf.InverseLerp(1f, criticalFar, ratio));
        ThrowBack(false, pullStrength);
        Finish($"後ろ（強さ {pullStrength:F2}）", false, false);
    }

    /// <summary>投げずにやめる（物資を離すだけ）。オンラインでは、離したことをホストへ知らせる。</summary>
    public void Cancel()
    {
        if (IsActive)
        {
            Finish("やめた", false, true);
        }
    }

    private void Update()
    {
        if (!IsActive)
        {
            return;
        }

        // 物資が消えた（ゴールに入った・爆発した・場外に落ちた）ら、投げずに終わる
        if (item.IsVanished || !item.gameObject.activeInHierarchy)
        {
            Finish("消えた", false, false);
            return;
        }

        if (supply != null && !UpdateOnlineClaim())
        {
            return;
        }

        // 通信なしの引っ張り合い：決着は SupplyTug が知らせてくる
        if (tugging)
        {
            return;
        }

        // 重い物は、引きずり終わるまで待つ（終わりは FixedUpdate が決める）。クリティカルの合図も、前へ投げるのも無い
        if (heavy)
        {
            if (heavyRemaining <= 0f)
            {
                ReleaseHeavy();
            }
            return;
        }

        // 両手で持っている：溜めが終わったら投げる（アニメーションが来たら、投げる瞬間に ThrowCarry を呼ぶ形にする）
        if (carrying)
        {
            if (Time.time >= carryStartTime + carryInSeconds + carryWindup)
            {
                ThrowCarry();
            }
            return;
        }

        if (!cueShown && Elapsed >= windowStart - CueLead)
        {
            cueShown = true;
            PlayEffect(Effect.CriticalCue);
        }

        if (distanceMode ? ArrivedAtStop() : Elapsed >= reelSeconds)
        {
            // 押さずに手前まで来た：ふつうの引っ張りの強さで、そのまま後ろへ引っ張り切る（2026/11/24・小野田さんの指定）
            if (!distanceMode && arriveThrowsBack)
            {
                ThrowBack(false);
                Finish("後ろ", false, false);
                return;
            }

            ThrowForward();
            Finish("前", false, false);
        }
    }

    /// <summary>
    /// 距離で変わる操作で、物資が手前（Stop Distance）に着いたか。着いたら前へ投げる（大槻さんの指定）。
    /// 壁に引っかかって着けないときも、着くはずの時間を 1 秒過ぎたら着いたことにする（いつまでも終わらないように）。
    /// </summary>
    private bool ArrivedAtStop()
    {
        float expected = Mathf.Max(0f, reelStartDistance - stopDistance) / distanceReelSpeed;
        return FlatDistanceToPlayer() <= stopDistance + 0.05f || Elapsed >= expected + 1f;
    }

    /// <summary>
    /// オンラインで、ホストの記録を見る。**巻き取ってよければ true。**
    /// 返事待ちのうちは false（認められた瞬間から時間を数える）。ほかの人に取られた・ホストに外されたら、投げずにやめて false。
    /// </summary>
    private bool UpdateOnlineClaim()
    {
        int me = MyIndex;

        // ---- 引っ張り合い（2026/11/24）：その間はホストが物資を動かす ----
        bool inTug = supply.IsTugging && (supply.HookedByPlayerIndex == me || supply.TugChallenger == me);
        if (inTug)
        {
            if (!tugging)
            {
                EnterTug(null, supply.TugChallenger == me ? 1 : 0);
            }
            return false;
        }

        if (tugging)
        {
            // 決着した。自分が「つかんでいる人」のままなら勝ち（持ち主が移ってくるのを待つ）、違えば負け（光線が切れる）
            tugging = false;
            if (supply.HookedByPlayerIndex == me)
            {
                tugWonPending = true;
                tugWonWait = 0f;
            }
            else
            {
                LoseTug(false);
                return false;
            }
        }

        if (tugWonPending)
        {
            if (supply.HookedByPlayerIndex != me)
            {
                Finish("外れた", false, false);
                return false;
            }

            if (supply.IsOwner)
            {
                ResumeAfterTug();
                return true;
            }

            tugWonWait += Time.deltaTime;
            if (tugWonWait >= ClaimTimeout)
            {
                Finish("返事なし", false, true);
            }
            return false;
        }

        if (tugJoinPending)
        {
            // 引っ張り合いにしたいと頼んだ返事を待っている。断られた（つかんでいた人が離した・時間切れ）らやめる
            // 見ていない間に引っ張り合いが終わって、自分の物になっていた
            if (supply.HookedByPlayerIndex == me && supply.IsOwner)
            {
                tugJoinPending = false;
                ResumeAfterTug();
                return true;
            }

            claimWait += Time.deltaTime;
            if (!supply.IsClaimed || claimWait >= ClaimTimeout)
            {
                Finish("引っ張り合いにならなかった", false, false);
            }
            return false;
        }

        bool mine = supply.IsOwner && supply.HookedByPlayerIndex == me;

        if (waitingForClaim)
        {
            if (mine)
            {
                waitingForClaim = false;
                startTime = Time.time;
                reelStartDistance = FlatDistanceToPlayer();
                if (distanceMode)
                {
                    EstimateCriticalWindow();
                }
                body.WakeUp();
                return true;
            }

            claimWait += Time.deltaTime;
            bool takenByOther = supply.IsClaimed && supply.HookedByPlayerIndex != MyIndex;
            if (takenByOther)
            {
                Finish("取られた", false, false);
            }
            else if (claimWait >= ClaimTimeout)
            {
                // 返事が来ない。あとから認められても持ったままにならないよう、離したと送っておく
                Finish("返事なし", false, true);
            }
            return false;
        }

        if (!mine)
        {
            // 爆風などで、ホストに外された
            Finish("外れた", false, false);
            return false;
        }

        return true;
    }

    private void FixedUpdate()
    {
        if (!IsActive || body == null || waitingForClaim || tugging || tugWonPending)
        {
            return;
        }

        if (carrying)
        {
            MoveToCarryPoint();
            return;
        }

        // 動く物に押し付けすぎた・光線が動く物をつらぬいたら、ちぎれて落とす（2026/11/10）
        if (CheckSnap())
        {
            return;
        }

        // プレイヤーから見た物資の「向き」と「距離」を、残りの時間で行き先へ近づける（真上から見た位置で考える）
        Vector3 player = transform.position;
        Vector3 fromPlayer = body.position - player;
        fromPlayer.y = 0f;
        float distance = fromPlayer.magnitude;
        Vector3 toward = distance > 0.01f ? fromPlayer / distance : Flat(transform.forward);

        if (heavy)
        {
            DragHeavy(player, toward, distance);
            return;
        }

        // 残りの時間のうち、この1ステップの割合＝距離に関係なく、決まった時間で着く
        float remaining = Mathf.Max(Time.fixedDeltaTime, startTime + reelSeconds - Time.time);
        float share = Mathf.Min(1f, Time.fixedDeltaTime / remaining);

        // 距離で変わる操作：残りの道のりのうち、この1ステップで進む割合＝いつも同じ速さで近づく（2026/11/17・大槻さんの指定）
        if (distanceMode)
        {
            float room = distance - stopDistance;
            share = room > 0.001f ? Mathf.Min(1f, distanceReelSpeed * Time.fixedDeltaTime / room) : 1f;
        }

        // 向き：光線がしなっていれば、手元から光線が出ている向きの側へ回り込む（2026/11/4）。
        // 向きを変えていなければ、しなりの向き＝物資への向きなので、今までどおりまっすぐ引く
        Vector3 nextDirection = toward;
        if (maxSwingSpeed > 0f && shooter != null && shooter.TryGetBendDirection(out Vector3 bendDirection))
        {
            float swing = Vector3.SignedAngle(toward, bendDirection, Vector3.up) * share;
            float limit = maxSwingSpeed * Time.fixedDeltaTime;
            nextDirection = Quaternion.AngleAxis(Mathf.Clamp(swing, -limit, limit), Vector3.up) * toward;
        }

        // 距離：手前の止める位置（Stop Distance）まで
        float nextDistance = distance + (stopDistance - distance) * share;
        Vector3 next = player + nextDirection * nextDistance;

        // 距離で変わる操作：物資だけ、光線の形（しなっていれば曲線）にそって手元へ進める（2026/11/17・大槻さんの指定）
        if (distanceMode && TryStepAlongBeam(player, out Vector3 alongBeam))
        {
            next = alongBeam;
        }

        Vector3 rest = next - body.position;
        rest.y = 0f;
        Vector3 velocity = CapSpeed(rest / Time.fixedDeltaTime);
        reelVelocity = velocity;

        if (body.isKinematic)
        {
            body.MovePosition(body.position + velocity * Time.fixedDeltaTime);
            return;
        }

        SetDragVelocity(velocity);
    }

    /// <summary>
    /// **距離で変わる操作：物資を、光線の形にそって手元のほうへ1ステップぶん進めた位置**（2026/11/17・大槻さんの指定）。
    /// 光線の形（<see cref="SweeperShooter.GetBeamPath"/>。しなっていれば見た目と同じ曲線）を、物資の側から手元へ
    /// Distance Reel Speed × 時間 だけたどり、そのぶん物資をずらす。まっすぐ引いているときは、手元へまっすぐ近づく。
    /// プレイヤーの中心から Stop Distance より手前には来ない。光線の形が取れないときは false（いつもの寄せ方にする）。
    /// 動くのは物資だけで、割合（前へ投げる・引っ張る・クリティカル）は、これまでどおりプレイヤーと物資のまっすぐの距離で決める。
    /// </summary>
    private bool TryStepAlongBeam(Vector3 player, out Vector3 next)
    {
        next = body.position;
        int count = shooter != null ? shooter.GetBeamPath(beamPath) : 0;
        if (count < 2)
        {
            return false;
        }

        // 物資の側（最後の点）から手元（最初の点）へ、決まった長さだけたどる
        float step = distanceReelSpeed * Time.fixedDeltaTime;
        Vector3 tip = beamPath[count - 1];
        Vector3 point = tip;
        for (int i = count - 1; i > 0; i--)
        {
            Vector3 from = beamPath[i];
            Vector3 to = beamPath[i - 1];
            float length = FlatLength(from, to);
            if (length >= step)
            {
                point = Vector3.Lerp(from, to, step / Mathf.Max(length, 0.0001f));
                break;
            }

            step -= length;
            point = to;
        }

        Vector3 moved = point - tip;
        moved.y = 0f;
        next = body.position + moved;

        // 止める位置（Stop Distance）より手前には来ない
        Vector3 offset = next - player;
        offset.y = 0f;
        if (offset.magnitude < stopDistance)
        {
            Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Flat(transform.forward);
            next = player + direction * stopDistance;
        }

        return true;
    }

    /// <summary>2点の、真上から見た距離。</summary>
    private static float FlatLength(Vector3 a, Vector3 b)
    {
        return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>
    /// 上下は物理のまま、横だけ決める。回転は弱める。
    /// （つかんでいる間は重力を切っている（No Gravity While Held）ので、上下は動き出したときの高さのまま。オフなら重力で床に置いたまま引きずる）
    /// 次のステップで「どれだけ動けなかったか」を測るため、いまの位置と動かしたい距離を覚えておく。
    /// </summary>
    private void SetDragVelocity(Vector3 velocity)
    {
        body.linearVelocity = new Vector3(velocity.x, HeldVerticalSpeed(), velocity.z);

        if (spinDamping > 0f)
        {
            body.angularVelocity *= Mathf.Exp(-spinDamping * Time.fixedDeltaTime);
        }

        lastPosition = body.position;
        lastStep = new Vector3(velocity.x, 0f, velocity.z) * Time.fixedDeltaTime;
        hasLastStep = true;
    }

    /// <summary>
    /// **つかんでいる間の上下の速さ**（2026/11/24・小野田さんの指定）。重力を切っていても、持つ高さ（つかんだときの高さ）より上に
    /// 浮いたら重力で落とし、持つ高さで止める。下に押し下げられたときは沈み続けないよう止めるだけ（持ち上げない）。
    /// 重力を切っていない・Hold At Grab Height がオフなら、物理のまま。
    /// </summary>
    private float HeldVerticalSpeed()
    {
        float vy = body.linearVelocity.y;
        if (!gravityCut || !holdAtGrabHeight)
        {
            return vy;
        }

        float dt = Time.fixedDeltaTime;
        float above = body.position.y - holdY;
        if (above > HoldTolerance)
        {
            vy += Physics.gravity.y * aboveHoldGravityScale * dt;
            // 持つ高さを通り過ぎない
            if (vy < 0f && body.position.y + vy * dt < holdY)
            {
                vy = (holdY - body.position.y) / dt;
            }
            return vy;
        }

        // 持つ高さ以下：沈み続けないよう下向きはすぐ止め（落ちてきた勢いで通り過ぎないように）、上向きはゆっくり止める
        return vy < 0f ? 0f : Mathf.MoveTowards(vy, 0f, HoldSettle * dt);
    }

    /// <summary>両手で持っている間：物資を両手の位置へ寄せ、体に付いて動かす（上下も決める）。</summary>
    private void MoveToCarryPoint()
    {
        float t = carryInSeconds > 0f ? Mathf.Clamp01((Time.time - carryStartTime) / carryInSeconds) : 1f;
        Vector3 target = Vector3.Lerp(carryFrom, CarryPoint, Mathf.SmoothStep(0f, 1f, t));
        Vector3 velocity = (target - body.position) / Time.fixedDeltaTime;
        reelVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (body.isKinematic)
        {
            body.MovePosition(target);
            return;
        }

        body.linearVelocity = velocity;
        if (spinDamping > 0f)
        {
            body.angularVelocity *= Mathf.Exp(-spinDamping * Time.fixedDeltaTime);
        }
    }

    /// <summary>横の速さを <see cref="maxDragSpeed"/> までに抑える（0 なら抑えない）。</summary>
    private Vector3 CapSpeed(Vector3 velocity)
    {
        if (maxDragSpeed <= 0f)
        {
            return velocity;
        }

        float speed = velocity.magnitude;
        return speed > maxDragSpeed ? velocity * (maxDragSpeed / speed) : velocity;
    }

    // ------------------------------------------------------------
    // 光線がちぎれる（2026/11/10）
    // ------------------------------------------------------------

    /// <summary>
    /// **ちぎれるかを調べ、ちぎれたら物資を落として true。** FixedUpdate の最初に呼ぶ。
    /// ①前のステップで動かしたかった距離のうち、動けなかった分をためる（<see cref="snapStrain"/> を超えたらちぎれる）
    /// ②光線が動く物をつらぬき続けたら（<see cref="kinematicSnapSeconds"/>）ちぎれる
    /// </summary>
    private bool CheckSnap()
    {
        float dt = Time.fixedDeltaTime;

        if (snapStrain > 0f && hasLastStep && !body.isKinematic)
        {
            Vector3 moved = body.position - lastPosition;
            moved.y = 0f;

            // 動かしたかった向きに、実際に進めた距離。押し戻された・止められた分が「動けなかった距離」
            float wanted = lastStep.magnitude;
            float shortfall = wanted > 0.0001f ? Mathf.Max(0f, wanted - Vector3.Dot(moved, lastStep / wanted)) : 0f;

            if (shortfall > 0.0001f && (!strainOnlyOnKinematic || TouchingKinematic()))
            {
                strain += shortfall;
            }

            strain = Mathf.Max(0f, strain - strainRecovery * dt);
            if (strain >= snapStrain)
            {
                Snap();
                return true;
            }
        }
        hasLastStep = false;

        if (snapOnKinematicPierce && BeamPiercesKinematic())
        {
            pierceTime += dt;
            if (pierceTime >= kinematicSnapSeconds)
            {
                Snap();
                return true;
            }
        }
        else
        {
            pierceTime = 0f;
        }

        return false;
    }

    /// <summary>
    /// **ちぎれる相手か。** 動く物（Is Kinematic の Rigidbody）だけ。ふつうの壁（Rigidbody が無い）・物資・プレイヤー・
    /// 自分の体・すり抜ける判定は数えない（オンラインでは、ほかの人の物資も Is Kinematic になっているため物資は除く）。
    /// </summary>
    private bool IsSnapObstacle(Collider other)
    {
        if (other == null || other.isTrigger)
        {
            return false;
        }

        Rigidbody otherBody = other.attachedRigidbody;
        if (otherBody == null || !otherBody.isKinematic || otherBody == body)
        {
            return false;
        }

        if (other.transform.IsChildOf(transform))
        {
            return false;
        }

        return other.GetComponentInParent<HookableObject>() == null
            && other.GetComponentInParent<CharacterController>() == null;
    }

    /// <summary>引いている物資が、動く物に触れているか（物資の大きさの箱を少しふくらませて調べる）。</summary>
    private bool TouchingKinematic()
    {
        Collider itemCollider = item != null ? item.GetComponent<Collider>() : null;
        if (itemCollider == null)
        {
            return false;
        }

        Bounds bounds = itemCollider.bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * TouchMargin, touchBuffer,
            Quaternion.identity, snapCheckMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (IsSnapObstacle(touchBuffer[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 光線（手元から物資まで。しなっていれば曲線）が、動く物をつらぬいているか。
    /// 両端の <see cref="pierceIgnoreEnds"/> m は見ない（物資が乗っている床に先端がかするだけでちぎれないように）。
    /// </summary>
    private bool BeamPiercesKinematic()
    {
        if (shooter == null)
        {
            return false;
        }

        int count = shooter.GetBeamPath(beamPath);
        if (count < 2)
        {
            return false;
        }

        float total = 0f;
        for (int i = 1; i < count; i++)
        {
            total += Vector3.Distance(beamPath[i - 1], beamPath[i]);
        }

        float from = pierceIgnoreEnds;
        float to = total - pierceIgnoreEnds;
        if (to <= from)
        {
            return false;
        }

        float walked = 0f;
        for (int i = 1; i < count; i++)
        {
            Vector3 a = beamPath[i - 1];
            Vector3 b = beamPath[i];
            float length = Vector3.Distance(a, b);
            float segmentStart = walked;
            walked += length;

            // この区間のうち、両端を除いた部分だけ調べる
            float s0 = Mathf.Max(segmentStart, from);
            float s1 = Mathf.Min(walked, to);
            if (length < 0.0001f || s1 <= s0)
            {
                continue;
            }

            Vector3 direction = (b - a) / length;
            Vector3 start = a + direction * (s0 - segmentStart);
            int hits = Physics.RaycastNonAlloc(start, direction, pierceHits, s1 - s0, snapCheckMask, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hits; h++)
            {
                if (IsSnapObstacle(pierceHits[h].collider))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// **つかんでいる間、物資の重力を切る**（2026/11/24・小野田さんの指示）。Begin で呼ぶ。
    /// 切った瞬間の上下の速さは 0 にする（跳ねている途中でつかんだとき、上へ流れていかないように）。
    /// No Gravity While Held がオフなら何もしない（前のように重力のまま床を引きずる）。
    /// </summary>
    private void CutGravity()
    {
        if (!noGravityWhileHeld || body == null)
        {
            return;
        }

        itemGravityWas = body.useGravity;
        gravityCut = true;
        body.useGravity = false;
        if (!body.isKinematic)
        {
            body.linearVelocity = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
        }
    }

    /// <summary>切っていた重力を、つかむ前の設定に戻す。Finish（どの終わり方でも通る）で呼ぶ。</summary>
    private void RestoreGravity()
    {
        if (!gravityCut)
        {
            return;
        }

        gravityCut = false;
        if (body != null)
        {
            body.useGravity = itemGravityWas;
        }
    }

    /// <summary>**光線がちぎれる。** ちぎれた見た目を出し（オンラインではほかの人の画面にも）、物資はその場に落とす。</summary>
    private void Snap()
    {
        PlayEffect(Effect.Snap);
        DropInPlace();
        Finish(SnapResult, false, false);
    }

    /// <summary>
    /// 物資を横に動かさず、その場に置く（落ちるのは止めない）。オンラインでは、離した位置をホストへ渡して置いてもらう
    /// （ホストに届いている位置は少し遅れているため）。
    /// </summary>
    private void DropInPlace()
    {
        if (body == null)
        {
            return;
        }

        if (!body.isKinematic)
        {
            body.linearVelocity = new Vector3(0f, Mathf.Min(0f, body.linearVelocity.y), 0f);
            body.angularVelocity = Vector3.zero;
        }

        if (supply != null && supply.IsSpawned)
        {
            supply.RequestThrow(Vector3.zero, 0f, 0f, MyIndex, true, Vector3.zero, Vector3.zero, true, body.position);
        }
    }

    /// <summary>
    /// **重い物を1ステップぶん引きずる。** 速さは Heavy Drag Speed ÷ 重さ。1回で引きずれる距離（<see cref="heavyRemaining"/>）を使い切るか、
    /// 手前（Heavy Stop Distance）に着いたら終わり（離すのは <see cref="Update"/>）。
    /// 壁に引っかかって動かなくても距離は減っていくので、いつまでも離さないことは無い。
    /// 向きを変えたときの回り込みも、重さで割った速さで効く（重いほど振り回しにくい）。
    /// </summary>
    private void DragHeavy(Vector3 player, Vector3 toward, float distance)
    {
        float dt = Time.fixedDeltaTime;
        float room = Mathf.Max(0f, distance - heavyStopDistance);
        float step = Mathf.Min(heavyDragSpeed / heavyWeight * dt, heavyRemaining, room);

        heavyRemaining -= step;
        if (room <= 0.01f)
        {
            heavyRemaining = 0f;
        }

        Vector3 nextDirection = toward;
        if (maxSwingSpeed > 0f && shooter != null && shooter.TryGetBendDirection(out Vector3 bendDirection))
        {
            float swing = Vector3.SignedAngle(toward, bendDirection, Vector3.up);
            float limit = maxSwingSpeed * HeavySwingShare / heavyWeight * dt;
            nextDirection = Quaternion.AngleAxis(Mathf.Clamp(swing, -limit, limit), Vector3.up) * toward;
        }

        Vector3 rest = player + nextDirection * (distance - step) - body.position;
        rest.y = 0f;
        Vector3 velocity = CapSpeed(rest / dt);
        reelVelocity = velocity;

        if (body.isKinematic)
        {
            body.MovePosition(body.position + velocity * dt);
            return;
        }

        SetDragVelocity(velocity);
    }

    /// <summary>
    /// **重い物を、その場に置いて離す**（投げない）。オンラインでは、離した位置をホストへ渡して置いてもらう
    /// （ホストに届いている位置は少し遅れているため。今の体の「重い物を引きずる」と同じ考え）。
    /// </summary>
    private void ReleaseHeavy()
    {
        DropInPlace();
        Finish("引きずった", false, false);
    }

    /// <summary>
    /// 押した瞬間の位置から、プレイヤーの頭を越える放物線で後ろへ投げる。
    /// 飛ぶ向きは「体の真後ろ」と「物資がいま動いている向き」を <see cref="throwFollowMotion"/> の割合で混ぜたもの（2026/11/4）。
    /// 着く場所は、その向きにまっすぐ飛んで、プレイヤーの中心から Back Distance（クリティカルは Critical Back Distance）離れた所。
    /// <paramref name="strength"/> は飛ぶ速さに掛ける強さ（距離で変わる操作で使う。1 でいつもどおり）。
    /// </summary>
    private void ThrowBack(bool critical, float strength = 1f)
    {
        if (body == null)
        {
            return;
        }

        float distance = critical ? criticalBackDistance : backDistance;
        float arcHeight = critical ? criticalArcHeight : backArcHeight;

        Vector3 from = body.position;
        Vector3 direction = ThrowBackDirection();
        Vector3 landing = BackLanding(from, direction, distance);
        landing.y = from.y;

        float apexY = Mathf.Max(transform.position.y + arcHeight, from.y + 0.5f);
        Vector3 velocity = BackThrowVelocity(from, landing, apexY) * strength;

        // クリティカルで投げた物は、飛んでいる間だれもつかめない（2026/11/24・小野田さんの指定）
        float protect = 0f;
        if (critical)
        {
            PlayEffect(Effect.CriticalHit);
            if (criticalCannotBeStolen)
            {
                protect = FlightTime(from, landing, apexY) + Mathf.Max(0f, hitStopSeconds) + criticalProtectExtra;
                item?.Protect(protect);
            }
        }

        if (critical && hitStopSeconds > 0f)
        {
            StartCoroutine(LaunchAfterStop(body, supply, velocity, hitStopSeconds, protect));
            PulledBack?.Invoke(hitStopSeconds);
        }
        else
        {
            Launch(body, supply, velocity, protect);
            PulledBack?.Invoke(0f);
        }
    }

    /// <summary><see cref="BackThrowVelocity"/> で投げたときに、着地点に着くまでの秒数。</summary>
    public static float FlightTime(Vector3 from, Vector3 landing, float apexY)
    {
        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        apexY = Mathf.Max(apexY, Mathf.Max(from.y, landing.y) + 0.01f);
        float upSpeed = Mathf.Sqrt(2f * gravity * (apexY - from.y));
        return upSpeed / gravity + Mathf.Sqrt(2f * (apexY - landing.y) / gravity);
    }

    /// <summary>頭上を越えて着地点へ投げる初速。結果画面も同じ計算を使う。</summary>
    public static Vector3 BackThrowVelocity(Vector3 from, Vector3 landing, float apexY)
    {
        float gravity = Mathf.Max(0.1f, -Physics.gravity.y);
        apexY = Mathf.Max(apexY, Mathf.Max(from.y, landing.y) + 0.01f);
        // いちばん高い所まで上がる時間と、そこから落ちる時間を足したのが、飛んでいる時間
        float upSpeed = Mathf.Sqrt(2f * gravity * (apexY - from.y));
        float flightTime = upSpeed / gravity + Mathf.Sqrt(2f * (apexY - landing.y) / gravity);

        Vector3 across = landing - from;
        across.y = 0f;
        return across / flightTime + Vector3.up * upSpeed;
    }

    /// <summary>
    /// 後ろへ投げる向き（真上から見た向き）。体の真後ろから、物資が動いている向き（接線）のほうへ、
    /// その間の角度の「寄せる割合」倍だけ回す。
    /// 寄せる割合は引き寄せの進み具合で切り替える（2026/11/10）。<see cref="throwFollowSwitch"/>（初期値 8 割）までは
    /// <see cref="throwFollowMotion"/>（接線に近い向きへ飛ぶ）、そこを超えたら <see cref="throwFollowNear"/>（真後ろに、ほんの少しだけ接線を足す）。
    /// まっすぐ引いているときは動いている向き＝プレイヤーへ向かう向き＝真後ろなので、どちらでも真後ろになる。
    /// </summary>
    private Vector3 ThrowBackDirection()
    {
        Vector3 back = -Flat(transform.forward);

        float follow = ReelProgress() < throwFollowSwitch ? throwFollowMotion : throwFollowNear;

        Vector3 motion = reelVelocity;
        motion.y = 0f;
        if (follow <= 0f || motion.sqrMagnitude < MinFollowSpeed * MinFollowSpeed)
        {
            return back;
        }

        // 角度で混ぜる（真逆どうしでも向きが決まるように、真上から見た回転の角度で考える）
        float angle = Vector3.SignedAngle(back, motion, Vector3.up) * follow;
        return Quaternion.AngleAxis(angle, Vector3.up) * back;
    }

    /// <summary>
    /// 引き寄せの進み具合（0〜1）。当たった位置で 0、止める位置（Stop Distance）に着いたら 1。
    /// 初めから止める位置のそばにいたときは 1（後半あつかい）。
    /// </summary>
    private float ReelProgress()
    {
        float span = reelStartDistance - stopDistance;
        if (span <= 0.01f)
        {
            return 1f;
        }

        return Mathf.Clamp01((reelStartDistance - FlatDistanceToPlayer()) / span);
    }

    /// <summary>プレイヤーの中心から物資までの、真上から見た距離。</summary>
    private float FlatDistanceToPlayer()
    {
        if (body == null)
        {
            return 0f;
        }

        Vector3 offset = body.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    /// <summary>
    /// <paramref name="from"/> から <paramref name="direction"/> へまっすぐ飛んで、プレイヤーの中心から <paramref name="distance"/> 離れる所。
    /// その線がその距離の円に届かない（物資がもう円の外にいて、外へ向かって動いている）ときは、物資の位置から <paramref name="distance"/> 先にする。
    /// </summary>
    private Vector3 BackLanding(Vector3 from, Vector3 direction, float distance)
    {
        Vector3 offset = from - transform.position;
        offset.y = 0f;

        // |offset + direction * t| = distance を t について解く（direction は長さ1）
        float along = Vector3.Dot(direction, offset);
        float inside = along * along - offset.sqrMagnitude + distance * distance;
        float travel = inside >= 0f ? -along + Mathf.Sqrt(inside) : -1f;
        if (travel < 0.5f)
        {
            travel = distance;
        }

        return from + direction * travel;
    }

    /// <summary>物資をその場に <paramref name="seconds"/> 秒止めてから投げる。止めている間に巻き取りは終わっている（Finish 済み）。</summary>
    private IEnumerator LaunchAfterStop(Rigidbody target, NetSupply netSupply, Vector3 velocity, float seconds, float protectSeconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                yield break;
            }

            if (!target.isKinematic)
            {
                target.linearVelocity = Vector3.zero;
                target.angularVelocity = Vector3.zero;
            }
            yield return new WaitForFixedUpdate();
        }

        if (target != null && target.gameObject.activeInHierarchy)
        {
            Launch(target, netSupply, velocity, protectSeconds);
        }
    }

    /// <summary>向いている方向（前）へ、床すれすれに投げる。<paramref name="strength"/> は速さに掛ける強さ（1 でいつもどおり）。</summary>
    private void ThrowForward(float strength = 1f)
    {
        if (body == null)
        {
            return;
        }

        Launch(body, supply, (Flat(transform.forward) * forwardSpeed + Vector3.up * forwardLift) * strength);
    }

    /// <summary><paramref name="protectSeconds"/> が 0 より大きければ、その秒数だけ誰もつかめなくする（クリティカル。ホストにも頼む）。</summary>
    private void Launch(Rigidbody target, NetSupply netSupply, Vector3 velocity, float protectSeconds = 0f)
    {
        // 投げた物は、少しのあいだ誰もつかめない（投げた瞬間にほかの人に取られないように。2026/11/27）
        protectSeconds = Mathf.Max(protectSeconds, throwLockSeconds);
        if (protectSeconds > 0f && target != null)
        {
            target.GetComponent<HookableObject>()?.Protect(protectSeconds);
        }

        if (netSupply != null)
        {
            // オンライン：飛ばすのはホスト。計算した速さをそのまま渡し、離した位置から飛ばしてもらう
            netSupply.RequestThrow(Vector3.zero, 0f, 0f, MyIndex, true, velocity, target.angularVelocity, true, target.position,
                protectSeconds);
        }

        if (target.isKinematic)
        {
            return;
        }

        // 自分の画面でも同じ速さで飛ばしておく（ホストに持ち主が戻るまでの間、止まって見えないように）
        target.linearVelocity = velocity;
        target.WakeUp();
    }

    /// <summary>
    /// 巻き取りを終える。<paramref name="releaseOnline"/> が true なら、オンラインで「離した」とホストへ知らせる
    /// （投げたとき・取られた／外されたとき・消えたときは知らせない）。
    /// </summary>
    private void Finish(string result, bool critical, bool releaseOnline)
    {
        LastResult = result;
        LastWasCritical = critical;

        // 通信なしの引っ張り合いの最中に、ほかの理由（スタン・止められた）で手を離した：相手の勝ち。
        // 相手がまだつかんでいるので、重力は戻さない（相手が離すときに戻す）
        SupplyTug abandonedTug = tugging ? localTug : null;
        int abandonedSide = tugSide;
        if (abandonedTug != null)
        {
            gravityCut = false;
        }
        tugging = false;
        localTug = null;
        tugJoinPending = false;
        tugWonPending = false;

        // オンラインの引っ張り合いの最中・頼んでいる最中でも、「離した」はホストへ知らせる（ホストが相手の勝ちにする）
        if (releaseOnline && supply != null && supply.IsSpawned)
        {
            supply.RequestRelease(Vector3.zero, 0f, MyIndex);
        }

        // つかんでいる間に切っていた重力を戻す（投げた直後もここを通るので、投げた物はいつもどおり重力で落ちる）
        RestoreGravity();

        if (item != null)
        {
            // ほかの体がまだつかんでいる（引っ張り合いの相手）なら、「つかまれている」の印は残す
            if (FindLocalHolder(item) == null)
            {
                item.SetHooked(false);
            }
            // 投げた直後に自分の体に当たらないようにする（離れたら元に戻る）
            ThrowPassThrough.Apply(item.gameObject, playerCollider);
        }

        item = null;
        body = null;
        supply = null;
        waitingForClaim = false;
        heavy = false;
        heavyRemaining = 0f;
        distanceMode = false;
        carrying = false;
        windowStart = CriticalStartSeconds;
        windowLength = criticalLength;
        hasLastStep = false;
        strain = 0f;
        pierceTime = 0f;

        // 片づけ終わってから、相手の勝ちを知らせる（相手はいつもの引き寄せに戻る）
        abandonedTug?.Abandon(abandonedSide);
    }

    /// <summary>巻き取っている物資にエフェクトを出し、出したことを知らせる（オンラインではほかの人の画面にも出る）。</summary>
    private void PlayEffect(Effect kind)
    {
        if (item == null)
        {
            return;
        }

        ShowEffect(kind, item);
        EffectShown?.Invoke(kind, item);
    }

    /// <summary>
    /// **その物資にエフェクトを出す。** 合図（<see cref="Effect.CriticalCue"/>）は受付の <c>Cue Lead</c> 秒前に呼ばれ、物資に付いていく。
    /// 決まったときの光はその場に出る。
    /// オンラインのほかの人の体では、本人から知らせが来たときに <see cref="SweeperNetSync"/> が呼ぶ（時間の値は、その体のインスペクターの値を使う）。
    /// </summary>
    public void ShowEffect(Effect kind, HookableObject target)
    {
        // ちぎれた：光線の見た目だけ（物資が見つからなくても出す）
        if (kind == Effect.Snap)
        {
            shooter?.PlaySnap();
            return;
        }

        if (target == null)
        {
            return;
        }

        if (kind == Effect.CriticalCue)
        {
            ShowCue(target);
        }
        else
        {
            ShowHit(target);
        }
    }

    /// <summary>合図を出す。巻き取りが終わったら（押した・やめた）、残っている合図はすぐ消える。</summary>
    private void ShowCue(HookableObject target)
    {
        float lead = CueLead;
        System.Func<bool> reeling = () => IsReeling(target);

        // プレハブと前からの広がる輪は、これまでどおり受付が始まった瞬間に出す
        if (criticalCuePrefab != null)
        {
            After(lead, () =>
            {
                if (IsReeling(target))
                {
                    Instantiate(criticalCuePrefab, GroundPoint(target, out _), Quaternion.identity);
                }
            });
            return;
        }

        if (cueSpreadRing)
        {
            After(lead, () =>
            {
                if (IsReeling(target))
                {
                    Vector3 ground = GroundPoint(target, out float size);
                    SweeperFlash.Spawn(ground, target.transform, flashColor, 1.2f * size, 3.2f * size, windowLength + 0.15f, flashMaterial);
                }
            });
        }

        if (cueShrinkRing)
        {
            Vector3 ground = GroundPoint(target, out float size);
            SweeperShrinkRing.Spawn(ground, target.transform, flashColor, size * 1.15f, lead, windowLength, reeling, flashMaterial);
        }

        if (cueGoldOutline)
        {
            SweeperItemGlow.SpawnOutline(target.gameObject, flashColor, outlineWidth, lead, windowLength, 0.12f, reeling, outlineMaterial);
        }
    }

    /// <summary>クリティカルが決まったときのエフェクトを出す。</summary>
    private void ShowHit(HookableObject target)
    {
        Vector3 ground = GroundPoint(target, out float size);

        if (criticalHitPrefab != null)
        {
            Instantiate(criticalHitPrefab, ground, Quaternion.identity);
            return;
        }

        if (hitSpreadRing)
        {
            SweeperFlash.Spawn(ground, null, flashColor, 1.5f * size, 6f * size, 0.5f, flashMaterial);
        }

        if (hitShards)
        {
            SweeperShards.Spawn(ground + Vector3.up * 0.3f, size * 0.6f, flashColor, flashMaterial);
        }

        if (hitWhiteFlash)
        {
            SweeperItemGlow.SpawnFlash(target.gameObject, Color.white, hitStopSeconds + 0.15f, whiteFlashMaterial);
        }

        if (hitThrustWave)
        {
            // プレイヤーから物資へ向かう向き＝物資が引っ張られている側の逆。止めてから飛ぶので、噴き出すのも飛び始めに合わせる
            Vector3 away = Flat(target.transform.position - transform.position);
            After(hitStopSeconds, () =>
            {
                if (target != null && target.gameObject.activeInHierarchy)
                {
                    SweeperThrustWave.Spawn(target.transform, away, size, flashColor, flashMaterial);
                }
            });
        }
    }

    /// <summary>その物資を、この体がいま巻き取っているか（ほかの人の体では、本人から届いた様子で見る）。</summary>
    private bool IsReeling(HookableObject target)
    {
        return target != null && (item == target || (shooter != null && shooter.ShownReelItem == target));
    }

    /// <summary>物資の足元（床の少し上）の位置と、真上から見た物資の大きさ（m）。</summary>
    private static Vector3 GroundPoint(HookableObject target, out float size)
    {
        Vector3 position = target.transform.position;
        size = 1f;

        Collider itemCollider = target.GetComponent<Collider>();
        if (itemCollider != null)
        {
            Bounds bounds = itemCollider.bounds;
            position.y = bounds.min.y + 0.03f;
            size = Mathf.Max(bounds.size.x, bounds.size.z);
        }

        return position;
    }

    /// <summary><paramref name="seconds"/> 秒後に <paramref name="action"/> を呼ぶ（0 ならすぐ）。</summary>
    private void After(float seconds, System.Action action)
    {
        if (seconds <= 0f)
        {
            action();
            return;
        }

        StartCoroutine(AfterRoutine(seconds, action));
    }

    private static IEnumerator AfterRoutine(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    private static Vector3 Flat(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
