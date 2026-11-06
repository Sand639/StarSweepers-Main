using System.Collections.Generic;
using UnityEngine;

/// <summary>GravityGlassFloor の「ヒビ」（GravityGlassFloor.cs から分けた部分）。</summary>
public partial class GravityGlassFloor
{
    // ------------------------------------------------------------
    // ヒビ
    // ------------------------------------------------------------

    public void ApplyBlast(Vector3 blastCenter)
    {
        if (blastHitsToBreak <= 0 || State == FloorState.Broken || finalCracks != null)
        {
            return;
        }

        CrackPattern pattern = BuildCracks(box.ClosestPoint(blastCenter), append: true, startAtEdge: true);
        pattern.Progress = 1f;
        RevealPattern(pattern);

        blastHits++;
        if (blastHits >= blastHitsToBreak)
        {
            Break();
        }
    }

    /// <summary>
    /// **ヒビを入れる。** 外から割れかけにしたいときにも呼べる。
    /// <paramref name="from"/> は、ヒビが広がり始める場所（だいたいでよい）。
    /// </summary>
    public void Crack(Vector3 from)
    {
        if (State != FloorState.Intact)
        {
            return;
        }

        State = FloorState.Cracked;
        BreakRemaining = secondsUntilBreak;

        BuildCracks(from);

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：ヒビが入った（重力 {Mathf.Abs(WorldGravity.Value):0.0}）。" +
                      $"{secondsUntilBreak:0.0}秒後に割れる", this);
        }

        if (secondsUntilBreak <= 0f)
        {
            Break();
        }
    }

    /// <summary>
    /// ヒビが入っている間、毎フレーム呼ぶ。ヒビを広げ、ふちを点滅させ、時間が来たら割る。
    /// **重力はもう見ない**（一度ヒビが入ったら、軽くなっても止まらない）。
    /// </summary>
    private void UpdateCracked()
    {
        BreakRemaining -= Time.deltaTime;

        // 割れるまでの進み具合。ヒビが入った瞬間が 0、割れる瞬間が 1
        float progress = secondsUntilBreak > 0f ? 1f - BreakRemaining / secondsUntilBreak : 1f;

        RevealCracks(progress);
        BlinkEdge(progress);

        if (BreakRemaining <= 0f)
        {
            Break();
        }
    }

    /// <summary>
    /// ヒビの線を作る。**踏んだ場所から放射状に、ギザギザに**伸ばす。
    ///
    /// 線は最初は隠しておき、割れる時間に近づくほど**踏んだ場所に近いほうから順に見せていく**
    /// （ヒビが広がっていくように見える）。
    /// </summary>
    private CrackPattern BuildCracks(Vector3 from, bool append = false, bool startAtEdge = false)
    {
        CrackPattern pattern = new CrackPattern();
        crackPatterns.Add(pattern);

        if (crackMaterial == null)
        {
            Debug.LogWarning($"{name}: ヒビのマテリアルが入っていません。ヒビは見えませんが、割れる動きはします。", this);
            return pattern;
        }

        // Cube の Scale がゆがんでいても線が斜めにゆがまないよう、
        // 入れ物の大きさを打ち消して「1倍」に戻しておく
        Vector3 scale = transform.lossyScale;

        if (crackRoot == null)
        {
            crackRoot = new GameObject("Cracks").transform;
            crackRoot.SetParent(transform, false);
            crackRoot.localScale = new Vector3(
                SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
        }

        // ここから下は、crackRoot の中での座標（大きさを打ち消してあるので、単位はそのままメートル）。
        // 線は上面の上に平らに並べるので、2次元の (x, y) を床の (x, z) として扱う
        Vector3 size = Vector3.Scale(box.size, scale);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;

        // 上面の高さ。0.004（4ミリ）だけ浮かせて、ガラスの面とちらついて重ならないようにする
        float topY = box.center.y * scale.y + size.y * 0.5f + 0.004f;

        // 踏んだ場所を、上面の上の位置に直す（ふちに寄りすぎないように少し内側へ）
        // 爆発の場合は、ガラスのふちも起点にできる。
        Vector3 local = transform.InverseTransformPoint(from);
        float originLimit = startAtEdge ? 1f : 0.96f;
        Vector2 origin = new Vector2(
            Mathf.Clamp((local.x - box.center.x) * scale.x, -halfX * originLimit, halfX * originLimit),
            Mathf.Clamp((local.z - box.center.z) * scale.z, -halfZ * originLimit, halfZ * originLimit));

        // 破断前に伸ばす設定では、最初のヒビを短くして先端が伸びる余地を残す。
        float reach = Mathf.Max(halfX, halfZ) * (fractureExtensionSeconds > 0f ? 0.85f : 1.5f);

        // 1本目の向き（度）。毎回変えて、同じ形のヒビにならないようにする
        float startAngle = Random.Range(0f, 360f);

        for (int line = 0; line < crackLines; line++)
        {
            // 線を等間隔の向きに並べ、1本ごとに ±15度だけずらす（きれいに並びすぎないように）
            float angle = (startAngle + 360f * line / crackLines + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;

            GrowCrackLine(origin, angle, reach, halfX, halfZ, topY, pattern);
        }

        // 踏んだ場所に近い順に並べる（名前の先頭が「何本目の区切りか」）。見せるときにこの順で出す
        // ※ 名前を文字として比べているので、区切りが10個以上になると「10」が「2」より前に来てしまう。
        //    いまは1本あたり最大4区切り（0〜3）なので問題ない
        if (!append)
        {
            foreach (CrackSegment segment in pattern.Segments)
            {
                crackSegments.Add(segment.Transform.gameObject);
            }
            crackSegments.Sort((a, b) => a.name.CompareTo(b.name));
        }

        if (append) RevealPattern(pattern);
        else RevealCracks(0f);
        return pattern;
    }

    /// <summary>
    /// ヒビの線を**1本**伸ばす。<paramref name="origin"/> から <paramref name="angle"/>（ラジアン）の向きへ、
    /// 3〜4個の区切りに分けて、**少しずつ曲げながら**進む。床のふちに着いたら、そこで止める。
    ///
    /// 座標は crackRoot の中での (x, z)。<paramref name="topY"/> は線を並べる高さ。
    /// </summary>
    private void GrowCrackLine(Vector2 origin, float angle, float reach, float halfX, float halfZ, float topY, CrackPattern pattern)
    {
        Vector2 point = origin;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Random.Range(int, int) は2つ目の数を含まないので、区切りは3個か4個
        int segments = Random.Range(3, 5);
        float step = reach / segments;

        for (int s = 0; s < segments; s++)
        {
            // 少しずつ向きをずらして、ギザギザにする
            // （±0.45ラジアン＝約±26度。1区切りの長さも 0.7〜1.1倍でばらつかせる）
            float bend = angle + Random.Range(-0.45f, 0.45f);
            Vector2 next = point + new Vector2(Mathf.Cos(bend), Mathf.Sin(bend)) * step * Random.Range(0.7f, 1.1f);

            // 床の外にはみ出したら、ふちで止める
            bool outside = Mathf.Abs(next.x) > halfX || Mathf.Abs(next.y) > halfZ;
            next.x = Mathf.Clamp(next.x, -halfX, halfX);
            next.y = Mathf.Clamp(next.y, -halfZ, halfZ);

            // s は「踏んだ場所から何個目の区切りか」。見せる順番に使う
            CreateSegment(point, next, topY, s, segments, pattern);

            if ((next - point).sqrMagnitude > 0.0001f)
            {
                direction = (next - point).normalized;
            }
            point = next;

            if (outside)
            {
                break;
            }
        }
        if (fractureExtensionSeconds > 0f)
        {
            crackTips.Add((point, direction));
        }
    }

    /// <summary>
    /// ヒビの線の1区切りを作る。**細長い Cube を、<paramref name="from"/> から <paramref name="to"/> へ渡す**形。
    /// <paramref name="depth"/> は踏んだ場所から何個目の区切りか（見せる順番に使う）。
    /// </summary>
    private void CreateSegment(Vector2 from, Vector2 to, float y, int depth, int segments, CrackPattern pattern)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;

        // 1センチ未満は見えないうえ、向きが決まらないので作らない（ふちのすぐ近くで止められたときなどに起きる）
        if (length < (fractureExtensionSeconds > 0f ? 0.0001f : 0.01f))
        {
            return;
        }

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // 名前の先頭を「どれだけ奥か」にしておき、並べ替えに使う
        segment.name = $"{depth}_Crack";

        RemoveCollider(segment);

        // 2点のまん中に置き、Z（奥行き）を線の向きに合わせて、Z方向に長さぶん伸ばす。
        // 厚みは 0.008（8ミリ）。長さに線の太さを足しておくと、区切りのつなぎ目にすき間ができない
        segment.transform.SetParent(crackRoot, false);
        Vector3 centerOffset = new Vector3(box.center.x * transform.lossyScale.x, 0f, box.center.z * transform.lossyScale.z);
        Vector3 start = new Vector3(from.x, y, from.y) + centerOffset;
        Vector3 end = new Vector3(to.x, y, to.y) + centerOffset;
        segment.transform.localPosition = (start + end) * 0.5f;
        segment.transform.localRotation = Quaternion.LookRotation(new Vector3(delta.x, 0f, delta.y));
        segment.transform.localScale = new Vector3(crackWidth, 0.008f, length + crackWidth);

        MeshRenderer segmentRenderer = segment.GetComponent<MeshRenderer>();
        segmentRenderer.sharedMaterial = crackMaterial;
        segmentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        pattern.Segments.Add(new CrackSegment
        {
            Transform = segment.transform, Start = start, End = end, Length = length,
            StartProgress = (float)depth / segments, EndProgress = (float)(depth + 1) / segments,
        });
    }

    private void RevealPattern(CrackPattern pattern)
    {
        foreach (CrackSegment segment in pattern.Segments)
        {
            float amount = Mathf.InverseLerp(segment.StartProgress, segment.EndProgress, pattern.Progress);
            segment.Transform.gameObject.SetActive(amount > 0f);
            segment.Transform.localPosition = Vector3.Lerp(segment.Start, segment.End, amount * 0.5f);
            segment.Transform.localScale = new Vector3(crackWidth, 0.008f, (segment.Length + crackWidth) * amount);
        }
    }

    /// <summary>ヒビを、進み具合に合わせて見せていく。最初から4割は見えている。</summary>
    private void RevealCracks(float progress)
    {
        // 見せる割合は 4割 → 10割。progress を 1.4倍しているので、
        // 割れる時間の約7割が過ぎたところで全部見えきる（最後の3割は全部出たまま点滅だけが速くなる）
        int visible = Mathf.CeilToInt(crackSegments.Count * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(progress * 1.4f)));

        for (int i = 0; i < crackSegments.Count; i++)
        {
            bool show = i < visible;

            if (crackSegments[i].activeSelf != show)
            {
                crackSegments[i].SetActive(show);
            }
        }
    }

    /// <summary>ふちを赤く点滅させる。**割れる直前ほど速く**する。</summary>
    private void BlinkEdge(float progress)
    {
        if (!hasEdgeColor)
        {
            return;
        }

        // 点滅の速さ（1秒あたりのラジアン）。4 は約1.6秒に1回、18 は約0.35秒に1回の点滅
        float speed = Mathf.Lerp(4f, 18f, progress);

        // sin の -1〜1 を 0〜1 に直したもの
        float blink = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;

        // 一番薄いときでも 4割は赤を混ぜる（点滅の谷で、ヒビが入っていることを見失わないように）
        SetEdgeColor(Color.Lerp(originalEdgeColor, crackEdgeColor, Mathf.Lerp(0.4f, 1f, blink)));
    }

    /// <summary>この床のふちの色だけを変える（<see cref="propertyBlock"/> を通すので、マテリアルは書き換えない）。</summary>
    private void SetEdgeColor(Color color)
    {
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(EdgeColorId, color);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>ヒビの線を全部消す。線は crackRoot の子なので、親ごと消せば済む。</summary>
    private void ClearCracks()
    {
        crackSegments.Clear();
        crackPatterns.Clear();
        crackTips.Clear();
        finalCracks = null;
        fractureTime = 0f;
        standingCracks.Clear();

        if (crackRoot != null)
        {
            crackRoot.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(crackRoot.gameObject);
            else DestroyImmediate(crackRoot.gameObject);
            crackRoot = null;
        }
    }
}
