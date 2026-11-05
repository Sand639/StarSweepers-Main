using System.Collections.Generic;
using UnityEngine;

/// <summary>GravityGlassFloor の「割れる・戻る（破片）」（GravityGlassFloor.cs から分けた部分）。</summary>
public partial class GravityGlassFloor
{
    // ------------------------------------------------------------
    // 割れる・戻る
    // ------------------------------------------------------------

    /// <summary>**割る。** 外からすぐ割りたいときにも呼べる。</summary>
    public void Break()
    {
        if (State == FloorState.Broken || finalCracks != null)
        {
            return;
        }

        if (fractureExtensionSeconds <= 0f)
        {
            FinishBreak();
            return;
        }

        if (crackPatterns.Count == 0)
        {
            BuildCracks(transform.TransformPoint(box.center), append: true);
        }

        foreach (CrackPattern pattern in crackPatterns)
        {
            pattern.Progress = 1f;
            RevealPattern(pattern);
        }

        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(box.size, scale);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        float topY = box.center.y * scale.y + size.y * 0.5f + 0.004f;
        finalCracks = new CrackPattern();
        crackPatterns.Add(finalCracks);

        foreach (var tip in crackTips)
        {
            Vector2 end = GlassFractureGeometry.ExtendToEdge(tip.Point, tip.Direction, halfX, halfZ);
            CreateSegment(tip.Point, end, topY, 0, 1, finalCracks);
        }
        RevealPattern(finalCracks);
    }

    private void FinishBreak()
    {
        State = FloorState.Broken;
        BreakRemaining = 0f;
        respawnTimer = respawnSeconds;

        if (finalCracks == null)
        {
            SpawnPieces();
        }
        else
        {
            SpawnFracturePieces();
        }
        ClearCracks();

        // **当たり判定を消す。** 乗っていた人はそのまま落ちる
        box.enabled = false;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = false;
        }

        if (logChanges)
        {
            Debug.Log($"[GLASS] {name}：割れた", this);
        }
    }

    /// <summary>割れている間、毎フレーム呼ぶ。「元に戻る秒数」が決めてあれば、時間が来たら戻す。</summary>
    private void UpdateBroken()
    {
        if (respawnSeconds <= 0f)
        {
            return;
        }

        respawnTimer -= Time.deltaTime;

        if (respawnTimer <= 0f)
        {
            Restore();
        }
    }

    /// <summary>
    /// **元の割れていない床に戻す。**
    /// やり直しの合図（<see cref="StageReset"/>）でも呼ばれる。
    /// </summary>
    public void Restore()
    {
        ClearCracks();

        State = FloorState.Intact;
        BreakRemaining = 0f;
        stepCount = 0;
        blastHits = 0;
        accumulatedStayTime = 0f;
        previousSteppers.Clear();
        steppers.Clear();
        departedSteppers.Clear();

        box.enabled = true;

        if (meshRenderer != null)
        {
            meshRenderer.enabled = true;

            // 点滅で変えた色を消して、マテリアルそのままの見た目に戻す
            meshRenderer.SetPropertyBlock(null);
        }
    }

    private void SpawnFracturePieces()
    {
        Vector3 scale = transform.lossyScale;
        Vector3 size = Vector3.Scale(box.size, scale);
        Vector3 center = Vector3.Scale(box.center, scale);
        var cuts = new List<(Vector2 Start, Vector2 End)>();
        foreach (CrackPattern pattern in crackPatterns)
        {
            foreach (CrackSegment segment in pattern.Segments)
            {
                cuts.Add((new Vector2(segment.Start.x - center.x, segment.Start.z - center.z),
                    new Vector2(segment.End.x - center.x, segment.End.z - center.z)));
            }
        }

        foreach (List<Vector2> polygon in GlassFractureGeometry.GetFaces(cuts, size.x * 0.5f, size.z * 0.5f))
        {
            var triangles = GlassFractureGeometry.Triangulate(polygon);
            if (triangles.Count == 0)
            {
                continue;
            }

            Vector2 pivot = Vector2.zero;
            foreach (Vector2 point in polygon)
            {
                pivot += point;
            }
            pivot /= polygon.Count;

            Mesh mesh = GlassFractureGeometry.Extrude(polygon, triangles, pivot, size.y);
            var piece = new GameObject(name + "_Piece");
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>();
            SetUpPiece(piece, new Vector3(pivot.x, 0f, pivot.y), meshRenderer.sharedMaterial);
            Destroy(mesh, pieceLifeSeconds);
        }
    }

    /// <summary>
    /// 割れた破片を出す。**当たり判定は付けない**ので、床も人も素通りして落ちていく。
    /// 落ちる速さはいまの重力に従う（重いほど速く落ちる）。
    /// </summary>
    private void SpawnPieces()
    {
        if (meshRenderer == null)
        {
            return;
        }

        Material material = meshRenderer.sharedMaterial;
        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        int count = Mathf.Max(1, piecesPerSide);

        // 1枚の大きさは、マス目の 92%。すき間が空いて「割れた」と分かりやすい
        Vector3 pieceSize = new Vector3(size.x / count * 0.92f, size.y, size.z / count * 0.92f);

        for (int x = 0; x < count; x++)
        {
            for (int z = 0; z < count; z++)
            {
                // 床を count×count のマス目に分けたときの、マスの中心
                Vector3 offset = new Vector3(
                    ((x + 0.5f) / count - 0.5f) * size.x,
                    0f,
                    ((z + 0.5f) / count - 0.5f) * size.z);

                SpawnPiece(offset, pieceSize, material);
            }
        }
    }

    /// <summary>
    /// 破片を1枚出す。<paramref name="offset"/> は床の中心から見たマスの中心（床の向きで、メートル）。
    /// 決めた秒数で自分から消える。
    /// </summary>
    private void SpawnPiece(Vector3 offset, Vector3 pieceSize, Material material)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name + "_Piece";

        RemoveCollider(piece);

        piece.transform.localScale = pieceSize;
        SetUpPiece(piece, offset, material);
    }

    private void SetUpPiece(GameObject piece, Vector3 offset, Material material)
    {
        piece.transform.SetPositionAndRotation(
            transform.TransformPoint(box.center) + transform.rotation * offset,
            transform.rotation);

        MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
        pieceRenderer.sharedMaterial = material;
        pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Rigidbody は「重力で落とす・散らばらせる」ために付けている。
        // 当たり判定が無く何にもぶつからないので、重さ（0.2kg）は動きにほぼ効かない
        Rigidbody body = piece.AddComponent<Rigidbody>();
        body.mass = 0.2f;

        // 真ん中から外へ、少しだけ散らばる
        // （まん中のマスは外向きが決まらないので、ランダムな向きにする。
        //   向きに ±0.5 のばらつきを足し、回転は1秒あたり最大4ラジアンでランダムに回す）
        Vector3 outward = offset.sqrMagnitude > 0.0001f ? offset.normalized : Random.onUnitSphere;
        body.linearVelocity = (outward + Random.insideUnitSphere * 0.5f) * scatterSpeed;
        body.angularVelocity = Random.insideUnitSphere * 4f;

        Destroy(piece, pieceLifeSeconds);
    }

    /// <summary>
    /// 当たり判定を外す。**消えるのはフレームの終わり**なので、先に切っておく
    /// （切らないと、そのフレームだけ人や他の破片を押してしまう）。
    /// </summary>
    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
    }

    /// <summary>1 ÷ value。value がほぼ 0 のときは、0 で割らないよう 1 を返す（大きさ 0 の床でも止まらないように）。</summary>
    private static float SafeInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }
}
