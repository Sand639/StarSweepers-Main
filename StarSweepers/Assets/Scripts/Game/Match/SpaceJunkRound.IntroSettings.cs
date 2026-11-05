using Unity.Netcode;
using UnityEngine;

/// <summary>SpaceJunkRound の「演出の設定を演出の部品へ渡す窓口」（SpaceJunkRound.cs から分けた部分）。</summary>
public partial class SpaceJunkRound
{
    // ↓ ゴールが飛んでくる演出の設定を、演出の部品 SpaceJunkGoalFlyIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① ゴールエリアが飛んでくる秒数。</summary>
    public float FlyInSeconds => flyInSeconds;

    /// <summary>① どちらの奥から飛んでくるか（水平の向き）。</summary>
    public Vector3 FlyInFrom => flyInFrom;

    /// <summary>① 飛び始めの位置が、マップに近づいた位置よりどれだけ奥か。</summary>
    public float FlyInDistance => flyInDistance;

    /// <summary>① 飛んでいるときの高さ。</summary>
    public float FlyInHeight => flyInHeight;

    /// <summary>① ゴールエリアどうしのすき間。</summary>
    public float FlyInGap => flyInGap;

    /// <summary>① カメラが見下ろす角度（度）。</summary>
    public float FlyInCameraPitch => flyInCameraPitch;

    /// <summary>① 飛び始めのとき、カメラが ① の終わりのときの何倍離れているか。</summary>
    public float FlyInCameraStartScale => flyInCameraStartScale;

    /// <summary>② 元の位置へ飛んで設置されるまでの秒数。</summary>
    public float FlyInLandSeconds => flyInLandSeconds;

    /// <summary>① 名前のタグを強調するときの倍率。</summary>
    public float NameEmphasisScale => nameEmphasisScale;

    // ↓ ゴールエリアと一緒に着地する演出の設定を、演出の部品 SpaceJunkLandingIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① カメラが回り、ゴールエリアがプレイヤーを乗せて飛んできて着地するまでの秒数。</summary>
    public float LandingSeconds => landingSeconds;

    /// <summary>① カメラが横に回る角度。</summary>
    public float LandingOrbitDegrees => landingOrbitDegrees;

    /// <summary>① 回り始めの、カメラが見下ろす角度。</summary>
    public float LandingStartPitch => landingStartPitch;

    /// <summary>① 回る間に、カメラが上へ回る角度。</summary>
    public float LandingOrbitRiseDegrees => landingOrbitRiseDegrees;

    /// <summary>① カメラとステージの中心の距離（ステージの大きさの何倍か）。</summary>
    public float LandingCameraDistanceScale => landingCameraDistanceScale;

    /// <summary>④ カウントダウンが始まってから、全体カメラの位置へゆっくり合わせる秒数。</summary>
    public float LandingReturnSeconds => landingReturnSeconds;

    /// <summary>②③ チームを1つ映す秒数。</summary>
    public float TeamShotSeconds => teamShotSeconds;

    /// <summary>②③ チームの中心からカメラまでの距離。</summary>
    public float TeamShotDistance => teamShotDistance;

    /// <summary>②③ チームを映すときに見下ろす角度。</summary>
    public float TeamShotPitch => teamShotPitch;

    /// <summary>②③ チームを映すときの斜めの角度。</summary>
    public float TeamShotAngle => teamShotAngle;

    /// <summary>②③ 映すチームの数（遊んでいるチーム数。係がいなければ 1）。どのPCでも同じ値になる。</summary>
    public int TeamShotCount => SpaceJunkSession.Current != null ? SpaceJunkSession.Current.TeamCount : 1;

    // ↓ 演出の設定を、演出の部品 SpaceJunkRoundIntro が読むための窓口（中身は上の同じ名前の値）

    /// <summary>① マップをぐるっと見せる秒数（演出の部品 <see cref="SpaceJunkRoundIntro"/> が読む）。</summary>
    public float IntroFlyoverSeconds => introFlyoverSeconds;

    /// <summary>① カメラが向き続ける場所（マップの中央）。</summary>
    public Vector3 IntroLookCenter => introLookCenter;

    /// <summary>① 真上から何度傾けた所から見るか。</summary>
    public float IntroFlyoverTilt => introFlyoverTilt;

    /// <summary>① マップの中央を中心に回る角度。</summary>
    public float IntroFlyoverDegrees => introFlyoverDegrees;

    /// <summary>② 自分のチームへカメラが寄る秒数。</summary>
    public float IntroZoomSeconds => introZoomSeconds;

    /// <summary>② 寄ったときの、チームの中央から後ろへの距離。</summary>
    public float IntroCloseDistance => introCloseDistance;

    /// <summary>② 寄ったときの高さ。</summary>
    public float IntroCloseHeight => introCloseHeight;

    /// <summary>③ 寄ったあと止まっている秒数。</summary>
    public float IntroHoldBeforeOrbitSeconds => introHoldBeforeOrbitSeconds;

    /// <summary>④ チームの周りを回る秒数。</summary>
    public float IntroTeamOrbitSeconds => introTeamOrbitSeconds;

    /// <summary>④ チームの周りを回る角度（マイナスで逆向き）。</summary>
    public float IntroTeamOrbitDegrees => introTeamOrbitDegrees;

    /// <summary>④ 回り終わったときの、チームの中央からの距離。</summary>
    public float IntroOrbitEndDistance => introOrbitEndDistance;

    /// <summary>④ 回り終わったときの高さ。</summary>
    public float IntroOrbitEndHeight => introOrbitEndHeight;

    /// <summary>⑤ 回ったあと止まっている秒数。</summary>
    public float IntroHoldAfterOrbitSeconds => introHoldAfterOrbitSeconds;

    /// <summary>⑥ 全体カメラ（ふだんのカメラ）へ移る秒数。</summary>
    public float IntroReturnSeconds => introReturnSeconds;

    /// <summary>④ が始まってから、カウントダウンが始まるまでの秒数。</summary>
    public float IntroCountdownDelaySeconds => introCountdownDelaySeconds;

    /// <summary>3・2・1 のカウントダウンの秒数。</summary>
    public float IntroCountdownSeconds => introCountdownSeconds;
}
