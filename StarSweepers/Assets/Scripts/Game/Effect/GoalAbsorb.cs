using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゴールの渦に吸い込まれている物資1つ分（吸い込みが始まったときに付き、終わったら外れる）。
/// 説明は Documents/スクリプトの説明/Game/Effect/GoalEffect.md
/// </summary>
public class GoalAbsorb : MonoBehaviour
{
    /// <summary>参加者のPCで、吸い込み終わってもホストから消されなかったときに元へ戻すまでの秒数。</summary>
    private const float ClientRestoreSeconds = 3f;

    private HookableObject item;
    private NetworkManager manager;
    private bool authority;
    private Vector3 center;
    private GoalEffect.Area area;
    private float startTime;
    private float seconds;
    private Vector3 startScale;
    private Quaternion startRotation;
    private float startRadius;
    private float startAngle;
    private float startY;
    private Action<HookableObject> onFinished;
    private GoalEffect settings;
    private bool finished;
    private float finishedAt;
    private bool pillar;
    private Vector3 startPosition;
    private float itemSize = 0.6f;

    /// <summary>いま吸い込み中の物資（光の柱が、消える前の光を描くのに使う）。</summary>
    public static readonly System.Collections.Generic.List<GoalAbsorb> All = new System.Collections.Generic.List<GoalAbsorb>();

    /// <summary>吸い込みの進み具合（0〜1）。</summary>
    public float Progress { get; private set; }

    /// <summary>光の柱の動きか（始まったときの見た目の種類で決まる）。</summary>
    public bool IsPillar => pillar;

    /// <summary>物資の見た目のおおよその大きさ（m）。消える瞬間の光の大きさに使う。</summary>
    public float ItemSize => itemSize;

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// 物資の吸い込みを始める。位置を動かすのはホスト（1人用ならそのPC）だけで、参加者のPCは小さくするだけ
    /// （位置と回転はホストから NetworkTransform で届く）。startTime は <see cref="ClockOf"/> の時刻。
    /// </summary>
    public static GoalAbsorb Begin(HookableObject item, GoalEffect.Area area, float startTime, float seconds,
        Action<HookableObject> onFinished)
    {
        if (item == null || item.GetComponent<GoalAbsorb>() != null)
        {
            return null;
        }

        GoalAbsorb absorb = item.gameObject.AddComponent<GoalAbsorb>();
        absorb.Setup(item, area, startTime, seconds, onFinished);
        return absorb;
    }

    /// <summary>吸い込みの時刻を測る時計。オンラインなら全員で合わせたホストの時計、そうでなければこのPCの時計。</summary>
    public static float ClockOf(HookableObject item)
    {
        NetworkManager owner = ManagerOf(item);
        return owner != null ? (float)owner.ServerTime.Time : Time.time;
    }

    private static NetworkManager ManagerOf(HookableObject item)
    {
        NetworkObject network = item != null ? item.GetComponent<NetworkObject>() : null;
        return network != null && network.IsSpawned ? network.NetworkManager : null;
    }

    private void Setup(HookableObject target, GoalEffect.Area vortexArea, float start, float duration, Action<HookableObject> finishedCallback)
    {
        item = target;
        manager = ManagerOf(target);
        authority = manager == null || manager.IsServer;
        area = vortexArea;
        center = vortexArea.Center;
        startTime = start;
        seconds = Mathf.Max(0.05f, duration);
        onFinished = finishedCallback;
        settings = GoalEffect.In(gameObject.scene);
        startScale = transform.localScale;
        startRotation = transform.rotation;

        Vector3 offset = transform.position - center;
        startRadius = new Vector2(offset.x, offset.z).magnitude;
        startAngle = Mathf.Atan2(offset.z, offset.x);
        startY = transform.position.y;
        startPosition = transform.position;
        // 参加者のPCには円柱の大きさが届かない（位置はホストが動かす）ので、見た目の種類だけで決める
        pillar = settings != null && settings.Style == GoalEffectStyle.Pillar;

        Renderer look = GetComponentInChildren<Renderer>();
        if (look != null)
        {
            itemSize = Mathf.Max(0.2f, look.bounds.size.magnitude);
        }

        item.SetAbsorbing(true);

        // 吸い込み中は、誰にも当たらない・引っ掛けられない
        foreach (Collider part in GetComponentsInChildren<Collider>())
        {
            part.enabled = false;
        }

        if (authority && item.Body != null)
        {
            if (!item.Body.isKinematic)
            {
                item.Body.linearVelocity = Vector3.zero;
                item.Body.angularVelocity = Vector3.zero;
            }
            item.Body.isKinematic = true;
        }

        Apply(0f);
    }

    private void Update()
    {
        if (item == null)
        {
            Destroy(this);
            return;
        }

        // ほかの理由で消された（参加者ならホストが消した）。見えないうちに大きさだけ戻して終わる
        if (!item.IsAbsorbing && !finished)
        {
            transform.localScale = startScale;
            Destroy(this);
            return;
        }

        float now = manager != null ? (float)manager.ServerTime.Time : Time.time;
        float t = Mathf.Clamp01((now - startTime) / seconds);

        if (!finished)
        {
            Apply(t);
        }

        if (t < 1f)
        {
            return;
        }

        if (authority)
        {
            Finish();
            return;
        }

        // 参加者：ホストが消すのを待つ。消されなかったら元に戻す
        if (!finished)
        {
            finished = true;
            finishedAt = Time.time;
            if (pillar && settings != null)
            {
                settings.PlayFlash(transform.position, itemSize);
            }
        }
        else if (!item.IsAbsorbing)
        {
            transform.localScale = startScale;
            Destroy(this);
        }
        else if (Time.time - finishedAt > ClientRestoreSeconds)
        {
            Restore();
        }
    }

    /// <summary>進み具合 t の姿にする（渦：小さくしながら回して中心へ／光の柱：真ん中の上へ運んで、ゆっくり上げる）。</summary>
    private void Apply(float t)
    {
        Progress = t;

        if (pillar)
        {
            // 光の柱：大きさはそのまま。位置はホストだけが動かす
            if (authority && area.Radius > 0f)
            {
                ApplyPillar(t);
            }
            return;
        }

        float endScale = settings != null ? settings.EndScale : 0.05f;
        float shrink = Mathf.Lerp(1f, endScale, Mathf.Pow(t, 1.6f));
        transform.localScale = startScale * shrink;

        if (!authority)
        {
            return;
        }

        float turns = settings != null ? settings.SpinTurns : 1.25f;
        float selfSpin = settings != null ? settings.SelfSpinDegrees : 540f;
        float sink = settings != null ? settings.SinkDepth : 0.4f;

        // 渦：中心へ寄りながら、だんだん速く回る（渦の見た目と同じ、上から見て左回り）
        float inward = t * t * (3f - 2f * t);
        float radius = startRadius * (1f - inward);
        float angle = startAngle + turns * Mathf.PI * 2f * t * t;
        // 高さ：すり鉢の内側の面に沿って下りる（はじめから面より低い物は、そのままの高さから下りる）
        float surface = settings != null && area.Radius > 0f ? settings.FunnelSurfaceY(area, radius) : center.y;
        float target = Mathf.Min(startY, surface + 0.2f);
        float y = Mathf.Lerp(startY, target, 1f - (1f - t) * (1f - t)) - sink * t * t * t * t;

        transform.position = new Vector3(center.x + Mathf.Cos(angle) * radius, y, center.z + Mathf.Sin(angle) * radius);
        transform.rotation = Quaternion.AngleAxis(-selfSpin * t * t, Vector3.up) * startRotation;
    }

    /// <summary>
    /// 光の柱の動き。前半（Pillar Move Seconds）で、真ん中の上（光の柱の一番上あたり）へ一直線に運ぶ。
    /// 後半（Pillar Rise Seconds）で、そこからゆっくり上げる。最後に光って消える（光は <see cref="GoalEffect.PlayFlash"/>）。
    /// </summary>
    private void ApplyPillar(float t)
    {
        float move = settings.PillarMoveSeconds;
        float total = Mathf.Max(0.05f, move + settings.PillarRiseSeconds);
        float elapsed = t * total;
        Vector3 top = new Vector3(center.x, settings.PillarTopY(area), center.z);

        if (elapsed < move)
        {
            float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, move));
            float ease = 1f - (1f - k) * (1f - k);
            transform.position = Vector3.Lerp(startPosition, top, ease);
            return;
        }

        float r = Mathf.Clamp01((elapsed - move) / Mathf.Max(0.01f, total - move));
        float riseEase = r * r * (3f - 2f * r);
        transform.position = top + Vector3.up * (settings.PillarRiseDistance * riseEase);
        transform.rotation = Quaternion.AngleAxis(-90f * r, Vector3.up) * startRotation;
    }

    /// <summary>ホスト：吸い込み終わり。大きさを戻してから知らせる（知らせた先ですぐ消すので、戻した姿は見えない）。</summary>
    private void Finish()
    {
        if (finished)
        {
            return;
        }

        finished = true;
        item.SetAbsorbing(false);
        transform.localScale = startScale;

        if (pillar && settings != null && !item.IsVanished)
        {
            settings.PlayFlash(transform.position, itemSize);
        }

        // 吸い込み中に、ほかの理由（爆発など）で消されていたら、何もしない（得点にしない）
        if (!item.IsVanished)
        {
            if (onFinished != null)
            {
                onFinished(item);
            }
            else
            {
                item.Vanish();
            }
        }

        Destroy(this);
    }

    /// <summary>参加者：ホストから消されなかったので、元の姿に戻す。</summary>
    private void Restore()
    {
        item.SetAbsorbing(false);
        transform.localScale = startScale;
        foreach (Collider part in GetComponentsInChildren<Collider>(true))
        {
            part.enabled = true;
        }
        Destroy(this);
    }
}
