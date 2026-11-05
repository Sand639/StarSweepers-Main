using System.Collections.Generic;
using UnityEngine;

/// <summary>GravityGlassFloor の「踏まれたかを調べる」（GravityGlassFloor.cs から分けた部分）。</summary>
public partial class GravityGlassFloor
{
    // ------------------------------------------------------------
    // 踏まれたか
    // ------------------------------------------------------------

    /// <summary>
    /// 割れていない間、毎フレーム呼ぶ。**重力が重いときに誰かが乗っていたら、ヒビを入れる。**
    /// </summary>
    private void CheckStep()
    {
        // 重力が軽いうちは、乗っても平気
        // （WorldGravity.Value は下向きでマイナスなので、大きさで比べる）
        if (Mathf.Abs(WorldGravity.Value) < breakGravity)
        {
            return;
        }

        if (TryFindStepper(out Vector3 footPosition))
        {
            Crack(footPosition);
        }
    }

    /// <summary>
    /// **上面のすぐ上に、誰か乗っているか。**
    /// 乗っていれば、その足もとの位置を返す（ヒビをそこから広げるため）。
    /// </summary>
    private bool TryFindStepper(out Vector3 footPosition)
    {
        FindSteppers();
        foreach (Vector3 position in steppers.Values)
        {
            footPosition = position;
            return true;
        }
        footPosition = transform.position;
        return false;
    }

    private void FindSteppers()
    {
        steppers.Clear();

        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        Vector3 up = transform.up;

        // 上面の少し内側から、上へ detectHeight ぶんの薄い箱
        // （横の広さは床の 96%＝半分の 0.48倍。ふちよりわずかに内側）
        Vector3 top = transform.TransformPoint(box.center) + up * (size.y * 0.5f);
        Vector3 center = top + up * (detectHeight * 0.5f);
        Vector3 halfExtents = new Vector3(size.x * 0.48f, detectHeight * 0.5f, size.z * 0.48f);

        // ~0 は「すべてのレイヤー」。すり抜ける当たり判定（トリガー）は数えない
        int count = Physics.OverlapBoxNonAlloc(
            center, halfExtents, Hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = Hits[i];

            // 床そのものと、床の子（ヒビの線など）は数えない
            if (hit == null || hit == box || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            CharacterController character = hit as CharacterController ?? hit.GetComponentInParent<CharacterController>();
            if (onlyCharacters && character == null)
            {
                continue;
            }

            // 同じプレイヤーの複数の Collider を二重に数えない。
            Component owner = character != null ? (Component)character : hit.attachedRigidbody != null ? (Component)hit.attachedRigidbody : hit;
            Vector3 foot = hit.bounds.center - up * hit.bounds.extents.y;
            if (character != null)
            {
                foot = character.transform.TransformPoint(character.center) - character.transform.up *
                    (character.height * Mathf.Abs(character.transform.lossyScale.y) * 0.5f);
            }
            float height = Vector3.Dot(foot - top, up);
            // 横から触れた人や、他の人の上に乗っている人は床の上の人数に含めない。
            if (height < -0.1f || height > detectHeight) continue;
            steppers[owner.GetInstanceID()] = foot;
        }
    }
}
