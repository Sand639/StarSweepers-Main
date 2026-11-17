using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゴールの渦に吸い込まれている物資1つ分（吸い込みが始まったときに付き、終わったら外れる）。
/// 説明は Documents/スクリプトの説明/Game/Effect/SpaceJunkGoalVortex.md
/// </summary>
public class SpaceJunkVortexAbsorb : MonoBehaviour
{
    /// <summary>参加者のPCで、吸い込み終わってもホストから消されなかったときに元へ戻すまでの秒数。</summary>
    private const float ClientRestoreSeconds = 3f;

    private HookableObject item;
    private NetworkManager manager;
    private bool authority;
    private Vector3 center;
    private float startTime;
    private float seconds;
    private Vector3 startScale;
    private Quaternion startRotation;
    private float startRadius;
    private float startAngle;
    private float startY;
    private Action<HookableObject> onFinished;
    private SpaceJunkGoalVortex settings;
    private bool finished;
    private float finishedAt;

    /// <summary>吸い込みの進み具合（0〜1）。</summary>
    public float Progress { get; private set; }

    /// <summary>
    /// 物資の吸い込みを始める。位置を動かすのはホスト（1人用ならそのPC）だけで、参加者のPCは小さくするだけ
    /// （位置と回転はホストから NetworkTransform で届く）。startTime は <see cref="ClockOf"/> の時刻。
    /// </summary>
    public static SpaceJunkVortexAbsorb Begin(HookableObject item, Vector3 center, float startTime, float seconds,
        Action<HookableObject> onFinished)
    {
        if (item == null || item.GetComponent<SpaceJunkVortexAbsorb>() != null)
        {
            return null;
        }

        SpaceJunkVortexAbsorb absorb = item.gameObject.AddComponent<SpaceJunkVortexAbsorb>();
        absorb.Setup(item, center, startTime, seconds, onFinished);
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

    private void Setup(HookableObject target, Vector3 vortexCenter, float start, float duration, Action<HookableObject> finishedCallback)
    {
        item = target;
        manager = ManagerOf(target);
        authority = manager == null || manager.IsServer;
        center = vortexCenter;
        startTime = start;
        seconds = Mathf.Max(0.05f, duration);
        onFinished = finishedCallback;
        settings = SpaceJunkGoalVortex.In(gameObject.scene);
        startScale = transform.localScale;
        startRotation = transform.rotation;

        Vector3 offset = transform.position - center;
        startRadius = new Vector2(offset.x, offset.z).magnitude;
        startAngle = Mathf.Atan2(offset.z, offset.x);
        startY = transform.position.y;

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

    /// <summary>進み具合 t の姿にする（小さくする・回す・中心へ寄せる）。</summary>
    private void Apply(float t)
    {
        Progress = t;
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

        // 中心へ寄りながら、だんだん速く回る（渦の見た目と同じ、上から見て左回り）
        float inward = t * t * (3f - 2f * t);
        float radius = startRadius * (1f - inward);
        float angle = startAngle + turns * Mathf.PI * 2f * t * t;
        float y = Mathf.Lerp(startY, center.y + 0.2f, 1f - (1f - t) * (1f - t)) - sink * t * t * t * t;

        transform.position = new Vector3(center.x + Mathf.Cos(angle) * radius, y, center.z + Mathf.Sin(angle) * radius);
        transform.rotation = Quaternion.AngleAxis(-selfSpin * t * t, Vector3.up) * startRotation;
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
