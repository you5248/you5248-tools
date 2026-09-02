using System;
using System.Collections.Generic;
using UnityEngine;

namespace you5248.LODGenerator
{
    /// <summary>
    /// 構造簡略化（Limited Dissolve 相当）。
    ///
    /// 「Cube＋ベベル」のように作り込まれた箱状ジオメトリを、
    /// 元の UV・マテリアル（＝法線マップ）を保ったまま箱に近づける。
    ///   1. 位置で頂点を溶接（トポロジー用）
    ///   2. ベベル（短い feature エッジ）を溶接して角を立てる
    ///   3. 同一平面の面をクラスタリング（角度しきい値・UV連続性で判定）
    ///   4. 各平面領域を境界ループから最小三角形に貼り直す
    ///
    /// 異常な領域（穴あき・非単純境界など）は元の三角形を残す安全側フォールバック。
    /// メッシュ簡略化と違い「平面の無駄な細分」と「面取り」を狙って消すのが目的。
    /// </summary>
    public static class LODStructuralSimplifier
    {
        public struct Options
        {
            /// <summary>この角度(度)以下の隣接面は同一平面として結合する。</summary>
            public float coplanarAngleDeg;
            /// <summary>この長さ未満で、かつ折れ目(crease)になっているエッジを溶接（ベベル除去）。0で無効。</summary>
            public float bevelLength;
            /// <summary>結合した平面領域にフラット法線を割り当てる（箱の角をシャープに保つ）。</summary>
            public bool flatShadeRegions;
        }

        public static Options DefaultOptions
        {
            get { return new Options { coplanarAngleDeg = 3f, bevelLength = 0f, flatShadeRegions = true }; }
        }

        private class Face
        {
            public readonly int[] o = new int[3]; // 元の頂点インデックス
            public readonly int[] s = new int[3]; // spatial(溶接後) インデックス
            public Vector3 n;                     // 面法線
        }

        public static Mesh Simplify(Mesh src, Options opts, out string report)
        {
            report = null;
            if (src == null) { report = "null mesh"; return null; }
            if (!src.isReadable)
            {
                report = $"'{src.name}' is not readable (enable Read/Write).";
                return null;
            }

            Vector3[] srcVerts = src.vertices;
            Vector3[] srcNormals = src.normals;
            Vector2[] srcUV = src.uv;
            Vector2[] srcUV2 = src.uv2;   // ライトマップUV
            Vector2[] srcUV3 = src.uv3;
            Vector2[] srcUV4 = src.uv4;
            Color[] srcColors = src.colors;
            bool hasNormals = srcNormals != null && srcNormals.Length == srcVerts.Length;
            bool hasUV = srcUV != null && srcUV.Length == srcVerts.Length;
            bool hasUV2 = srcUV2 != null && srcUV2.Length == srcVerts.Length;
            bool hasUV3 = srcUV3 != null && srcUV3.Length == srcVerts.Length;
            bool hasUV4 = srcUV4 != null && srcUV4.Length == srcVerts.Length;
            bool hasColors = srcColors != null && srcColors.Length == srcVerts.Length;
            int subCount = src.subMeshCount;

            float diag = Mathf.Max(1e-4f, src.bounds.size.magnitude);
            float weldEps = diag * 1e-5f;

            // 1) 位置で溶接
            int[] spatial = BuildSpatialIds(srcVerts, weldEps, out List<Vector3> spatialPos);

            // 2) ベベル（短い crease エッジ）溶接
            if (opts.bevelLength > 0f)
                WeldShortEdges(src, opts, ref spatial, ref spatialPos);

            int srcTriTotal = 0;
            int dstTriTotal = 0;

            // 出力アキュムレータ
            var outVerts = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUV = new List<Vector2>();
            var outUV2 = new List<Vector2>();
            var outUV3 = new List<Vector2>();
            var outUV4 = new List<Vector2>();
            var outColors = new List<Color>();
            var vertKey = new Dictionary<VKey, int>();
            var outTrisPerSub = new List<int>[subCount];

            float posQ = Mathf.Max(weldEps, 1e-6f);

            // 元コーナーの全頂点属性（UV0 / ライトマップUV2 / UV3 / UV4 / 頂点カラー）を引き継いで出力する
            int AddVert(Vector3 p, Vector3 n, int oc)
            {
                Vector2 uv0 = hasUV && oc >= 0 ? srcUV[oc] : Vector2.zero;
                Vector2 uv2 = hasUV2 && oc >= 0 ? srcUV2[oc] : Vector2.zero;
                Vector2 uv3 = hasUV3 && oc >= 0 ? srcUV3[oc] : Vector2.zero;
                Vector2 uv4 = hasUV4 && oc >= 0 ? srcUV4[oc] : Vector2.zero;
                Color col = hasColors && oc >= 0 ? srcColors[oc] : Color.white;

                var key = new VKey(p, n, uv0, uv2, col, posQ);
                if (vertKey.TryGetValue(key, out int idx)) return idx;
                idx = outVerts.Count;
                vertKey[key] = idx;
                outVerts.Add(p);
                outNormals.Add(n);
                outUV.Add(uv0);
                outUV2.Add(uv2);
                outUV3.Add(uv3);
                outUV4.Add(uv4);
                outColors.Add(col);
                return idx;
            }

            Vector3 CornerNormal(int original) => hasNormals && original >= 0 ? srcNormals[original] : Vector3.up;

            for (int s = 0; s < subCount; s++)
            {
                outTrisPerSub[s] = new List<int>();
                int[] tris = src.GetTriangles(s);
                srcTriTotal += tris.Length / 3;

                var faces = BuildFaces(tris, spatial, spatialPos);
                if (faces.Count == 0) continue;

                int[] regionOf = ClusterRegions(faces, spatialPos, opts.coplanarAngleDeg, srcUV, hasUV, srcUV2, hasUV2);

                // 領域ごとに面をまとめる
                var regions = new Dictionary<int, List<int>>();
                for (int f = 0; f < faces.Count; f++)
                {
                    if (!regions.TryGetValue(regionOf[f], out var list))
                    {
                        list = new List<int>();
                        regions[regionOf[f]] = list;
                    }
                    list.Add(f);
                }

                foreach (var kv in regions)
                {
                    var faceIdx = kv.Value;
                    bool ok = TryRetriangulateRegion(
                        faces, faceIdx, spatialPos,
                        opts.flatShadeRegions, hasNormals,
                        CornerNormal, AddVert,
                        outTrisPerSub[s]);

                    if (!ok)
                    {
                        // フォールバック: 元の三角形を（溶接後の位置で）そのまま残す
                        foreach (int fi in faceIdx)
                        {
                            var f = faces[fi];
                            int ia = AddVert(spatialPos[f.s[0]], CornerNormal(f.o[0]), f.o[0]);
                            int ib = AddVert(spatialPos[f.s[1]], CornerNormal(f.o[1]), f.o[1]);
                            int ic = AddVert(spatialPos[f.s[2]], CornerNormal(f.o[2]), f.o[2]);
                            if (ia == ib || ib == ic || ia == ic) continue;
                            outTrisPerSub[s].Add(ia);
                            outTrisPerSub[s].Add(ib);
                            outTrisPerSub[s].Add(ic);
                        }
                    }
                }

                dstTriTotal += outTrisPerSub[s].Count / 3;
            }

            if (dstTriTotal == 0)
            {
                report = "structural simplify produced no triangles";
                return null;
            }
            if (dstTriTotal >= srcTriTotal)
            {
                report = $"structural simplify: no gain ({srcTriTotal} tris). skipped.";
                return null;
            }

            var mesh = new Mesh();
            mesh.name = src.name + "_Struct";
            mesh.indexFormat = outVerts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(outVerts);
            mesh.SetNormals(outNormals);
            if (hasUV) mesh.SetUVs(0, outUV);
            if (hasUV2) mesh.SetUVs(1, outUV2);   // ライトマップUV
            if (hasUV3) mesh.SetUVs(2, outUV3);
            if (hasUV4) mesh.SetUVs(3, outUV4);
            if (hasColors) mesh.SetColors(outColors);
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
                mesh.SetTriangles(outTrisPerSub[s], s);
            mesh.RecalculateBounds();
            if (hasUV) mesh.RecalculateTangents();

            report = $"structural simplify: tris {srcTriTotal} -> {dstTriTotal}, verts {srcVerts.Length} -> {outVerts.Count}";
            return mesh;
        }

        // ===================================================================
        //  位置溶接
        // ===================================================================

        private static int[] BuildSpatialIds(Vector3[] verts, float eps, out List<Vector3> spatialPos)
        {
            spatialPos = new List<Vector3>();
            var map = new Dictionary<long, int>(verts.Length);
            var ids = new int[verts.Length];
            float inv = 1f / Mathf.Max(eps, 1e-9f);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                long key = QuantKey(v, inv);
                if (!map.TryGetValue(key, out int id))
                {
                    id = spatialPos.Count;
                    map[key] = id;
                    spatialPos.Add(v);
                }
                ids[i] = id;
            }
            return ids;
        }

        private static long QuantKey(Vector3 v, float inv)
        {
            long x = (long)Mathf.Round(v.x * inv);
            long y = (long)Mathf.Round(v.y * inv);
            long z = (long)Mathf.Round(v.z * inv);
            // 21bitずつに詰める（広い範囲をカバー、衝突は実用上無視できる）
            unchecked
            {
                long h = (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
                return h;
            }
        }

        // ===================================================================
        //  ベベル（短い crease エッジ）溶接
        // ===================================================================

        private static void WeldShortEdges(Mesh src, Options opts, ref int[] spatial, ref List<Vector3> spatialPos)
        {
            int spatialCount = spatialPos.Count;
            var faces = BuildFaces(src.triangles, spatial, spatialPos);

            // エッジ -> 隣接面（最大2面のみ扱う）
            var edgeFaces = new Dictionary<long, int>(); // 1面目
            var edgeFace2 = new Dictionary<long, int>(); // 2面目
            for (int f = 0; f < faces.Count; f++)
            {
                var fc = faces[f];
                for (int e = 0; e < 3; e++)
                {
                    int a = fc.s[e], b = fc.s[(e + 1) % 3];
                    long key = EdgeKey(a, b);
                    if (!edgeFaces.ContainsKey(key)) edgeFaces[key] = f;
                    else if (!edgeFace2.ContainsKey(key)) edgeFace2[key] = f;
                }
            }

            var uf = new UnionFind(spatialCount);
            float bevelSq = opts.bevelLength * opts.bevelLength;

            foreach (var kv in edgeFaces)
            {
                long key = kv.Key;
                if (!edgeFace2.TryGetValue(key, out int f1)) continue; // 開いた辺は対象外
                int f0 = kv.Value;
                DecodeEdge(key, out int a, out int b);

                float lenSq = (spatialPos[a] - spatialPos[b]).sqrMagnitude;
                if (lenSq >= bevelSq) continue;

                float angle = Vector3.Angle(faces[f0].n, faces[f1].n);
                if (angle <= opts.coplanarAngleDeg) continue; // 平面内の短辺は対象外（細分を守る）

                uf.Union(a, b); // 折れ目になっている短辺＝ベベルの段 → 溶接
            }

            // 新しい spatial id を作り直す（グループ重心へスナップ）
            var sum = new Dictionary<int, Vector3>();
            var cnt = new Dictionary<int, int>();
            for (int i = 0; i < spatialCount; i++)
            {
                int r = uf.Find(i);
                if (!sum.ContainsKey(r)) { sum[r] = Vector3.zero; cnt[r] = 0; }
                sum[r] += spatialPos[i];
                cnt[r] += 1;
            }
            var rootToNew = new Dictionary<int, int>();
            var newPos = new List<Vector3>();
            foreach (var kv in sum)
            {
                rootToNew[kv.Key] = newPos.Count;
                newPos.Add(kv.Value / Mathf.Max(1, cnt[kv.Key]));
            }
            var oldToNew = new int[spatialCount];
            for (int i = 0; i < spatialCount; i++)
                oldToNew[i] = rootToNew[uf.Find(i)];

            for (int i = 0; i < spatial.Length; i++)
                spatial[i] = oldToNew[spatial[i]];
            spatialPos = newPos;
        }

        // ===================================================================
        //  平面クラスタリング
        // ===================================================================

        private static int[] ClusterRegions(List<Face> faces, List<Vector3> spatialPos, float coplanarAngleDeg,
            Vector2[] uv, bool hasUV, Vector2[] uv2, bool hasUV2)
        {
            var uf = new UnionFind(faces.Count);

            // エッジ -> (面, そのエッジの spatial 端点) の対応
            var first = new Dictionary<long, int>();   // faceIndex
            var second = new Dictionary<long, int>();
            for (int f = 0; f < faces.Count; f++)
            {
                var fc = faces[f];
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(fc.s[e], fc.s[(e + 1) % 3]);
                    if (!first.ContainsKey(key)) first[key] = f;
                    else if (!second.ContainsKey(key)) second[key] = f;
                }
            }

            foreach (var kv in first)
            {
                long key = kv.Key;
                if (!second.TryGetValue(key, out int f1)) continue;
                int f0 = kv.Value;

                if (Vector3.Angle(faces[f0].n, faces[f1].n) > coplanarAngleDeg) continue;

                DecodeEdge(key, out int sa, out int sb);
                if (hasUV)
                {
                    // 端点UVが連続、かつ同一アフィンUV写像のときだけ結合する。
                    // （平面でもUVが折れている/島が違う面を結合すると、貼り直しでUVが崩れるため）
                    if (!UvContinuous(faces[f0], faces[f1], sa, sb, uv)) continue;
                    if (!AffineUVConsistent(faces[f0], faces[f1], sa, sb, uv, spatialPos)) continue;
                    if (!AffineUVConsistent(faces[f1], faces[f0], sa, sb, uv, spatialPos)) continue;
                }
                if (hasUV2)
                {
                    // ライトマップUV(uv2)も守る（チャート境界をまたいで結合させない）
                    if (!UvContinuous(faces[f0], faces[f1], sa, sb, uv2)) continue;
                    if (!AffineUVConsistent(faces[f0], faces[f1], sa, sb, uv2, spatialPos)) continue;
                    if (!AffineUVConsistent(faces[f1], faces[f0], sa, sb, uv2, spatialPos)) continue;
                }

                uf.Union(f0, f1);
            }

            var region = new int[faces.Count];
            for (int f = 0; f < faces.Count; f++)
                region[f] = uf.Find(f);
            return region;
        }

        private static bool UvContinuous(Face f0, Face f1, int sa, int sb, Vector2[] uv)
        {
            int oa0 = CornerAt(f0, sa), ob0 = CornerAt(f0, sb);
            int oa1 = CornerAt(f1, sa), ob1 = CornerAt(f1, sb);
            if (oa0 < 0 || ob0 < 0 || oa1 < 0 || ob1 < 0) return false;
            const float eps = 1e-4f;
            return (uv[oa0] - uv[oa1]).sqrMagnitude < eps && (uv[ob0] - uv[ob1]).sqrMagnitude < eps;
        }

        /// <summary>
        /// fb の「共有辺に乗っていない頂点」のUVを、fa のアフィンUV写像で予測し、実UVと一致するか。
        /// 一致すれば fa∪fb は単一アフィン写像 → 貼り直してもUVが変わらない。
        /// </summary>
        private static bool AffineUVConsistent(Face fa, Face fb, int sa, int sb, Vector2[] uv, List<Vector3> pos)
        {
            int sc = -1, ocB = -1;
            for (int i = 0; i < 3; i++)
            {
                if (fb.s[i] != sa && fb.s[i] != sb) { sc = fb.s[i]; ocB = fb.o[i]; break; }
            }
            if (sc < 0) return false;

            Vector3 a = pos[fa.s[0]], b = pos[fa.s[1]], c = pos[fa.s[2]];
            if (!Barycentric(pos[sc], a, b, c, out float u, out float v, out float w)) return false;

            Vector2 predicted = u * uv[fa.o[0]] + v * uv[fa.o[1]] + w * uv[fa.o[2]];
            Vector2 actual = uv[ocB];

            float scale = Mathf.Max(
                (uv[fa.o[1]] - uv[fa.o[0]]).magnitude,
                (uv[fa.o[2]] - uv[fa.o[0]]).magnitude);
            float tol = scale * 0.03f + 1e-4f;
            return (predicted - actual).magnitude <= tol;
        }

        /// <summary>p（a,b,c と同一平面上）の重心座標を返す。退化時 false。</summary>
        private static bool Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
            out float u, out float v, out float w)
        {
            u = v = w = 0f;
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-12f) return false;
            v = (d11 * d20 - d01 * d21) / den;
            w = (d00 * d21 - d01 * d20) / den;
            u = 1f - v - w;
            return true;
        }

        // ===================================================================
        //  領域の貼り直し
        // ===================================================================

        private static bool TryRetriangulateRegion(
            List<Face> faces, List<int> faceIdx, List<Vector3> spatialPos,
            bool flatShade, bool hasNormals,
            Func<int, Vector3> cornerNormal, Func<Vector3, Vector3, int, int> addVert,
            List<int> outTris)
        {
            if (faceIdx.Count <= 1) return false; // 単一三角形は貼り直す意味がない → そのまま

            // 領域内エッジの出現回数。1回 = 境界エッジ
            var edgeCount = new Dictionary<long, int>();
            var faceset = new HashSet<int>(faceIdx);
            foreach (int fi in faceIdx)
            {
                var fc = faces[fi];
                for (int e = 0; e < 3; e++)
                {
                    long key = EdgeKey(fc.s[e], fc.s[(e + 1) % 3]);
                    edgeCount.TryGetValue(key, out int c);
                    edgeCount[key] = c + 1;
                }
            }

            // 有向境界エッジ（面の巻き順のまま）を集める。from の元コーナーも保持。
            var nextOf = new Dictionary<int, int>();      // from spatial -> to spatial
            var cornerAtFrom = new Dictionary<int, int>(); // from spatial -> 元コーナー(UV/法線用)
            int boundaryEdges = 0;
            foreach (int fi in faceIdx)
            {
                var fc = faces[fi];
                for (int e = 0; e < 3; e++)
                {
                    int a = fc.s[e], b = fc.s[(e + 1) % 3];
                    if (edgeCount[EdgeKey(a, b)] != 1) continue; // 境界のみ
                    if (nextOf.ContainsKey(a)) return false;     // 分岐 = 非単純 → フォールバック
                    nextOf[a] = b;
                    cornerAtFrom[a] = fc.o[e];
                    boundaryEdges++;
                }
            }
            if (boundaryEdges < 3) return false;

            // ループを1本だけ辿る（複数ループ/穴あきはフォールバック）
            var loop = new List<int>();
            int start = -1;
            foreach (var k in nextOf.Keys) { start = k; break; }
            int cur = start;
            var guard = 0;
            while (true)
            {
                loop.Add(cur);
                if (!nextOf.TryGetValue(cur, out int nxt)) return false;
                cur = nxt;
                if (cur == start) break;
                if (++guard > boundaryEdges + 2) return false;
            }
            if (loop.Count != boundaryEdges) return false; // 全境界が1ループに収まらない = 穴あき等

            // 領域法線（面法線の平均）
            Vector3 regionN = Vector3.zero;
            foreach (int fi in faceIdx) regionN += faces[fi].n;
            if (regionN.sqrMagnitude < 1e-12f) return false;
            regionN.Normalize();

            // 2D へ射影（右手系基底: cross(t1,t2)=regionN）
            Vector3 t1 = Mathf.Abs(regionN.x) < 0.9f ? Vector3.right : Vector3.up;
            t1 = (t1 - regionN * Vector3.Dot(t1, regionN)).normalized;
            Vector3 t2 = Vector3.Cross(regionN, t1);

            int n = loop.Count;
            var pts2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = spatialPos[loop[i]];
                pts2[i] = new Vector2(Vector3.Dot(p, t1), Vector3.Dot(p, t2));
            }

            // 符号付き面積で CCW を保証
            float area2 = SignedArea(pts2);
            if (Mathf.Abs(area2) < 1e-12f) return false;
            bool reversed = area2 < 0f;
            if (reversed)
            {
                Array.Reverse(pts2);
                loop.Reverse();
            }

            // ear clipping（単純多角形のみ）
            var triLocal = EarClip(pts2);
            if (triLocal == null) return false;

            // 出力頂点（フラットなら領域法線、そうでなければ元コーナー法線）
            var outIdx = new int[n];
            for (int i = 0; i < n; i++)
            {
                int sid = loop[i];
                int corner = cornerAtFrom.TryGetValue(sid, out int c) ? c : -1;
                Vector3 nrm = flatShade || !hasNormals || corner < 0 ? regionN : cornerNormal(corner);
                outIdx[i] = addVert(spatialPos[sid], nrm, corner);
            }

            for (int i = 0; i < triLocal.Count; i += 3)
            {
                int x = outIdx[triLocal[i]];
                int y = outIdx[triLocal[i + 1]];
                int z = outIdx[triLocal[i + 2]];
                if (x == y || y == z || x == z) continue;
                outTris.Add(x);
                outTris.Add(y);
                outTris.Add(z);
            }
            return true;
        }

        private static float SignedArea(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 c = p[i];
                Vector2 nx = p[(i + 1) % p.Length];
                a += c.x * nx.y - nx.x * c.y;
            }
            return a * 0.5f;
        }

        /// <summary>CCW 単純多角形の ear clipping。失敗時は null。</summary>
        private static List<int> EarClip(Vector2[] poly)
        {
            int n = poly.Length;
            if (n < 3) return null;
            var tris = new List<int>((n - 2) * 3);
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            int guard = 0;
            int maxGuard = n * n + 10;
            while (idx.Count > 3)
            {
                bool clipped = false;
                int m = idx.Count;
                for (int i = 0; i < m; i++)
                {
                    int i0 = idx[(i - 1 + m) % m];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % m];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];

                    // 凸かどうか（CCW 前提）
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cross <= 0f) continue;

                    // 他の点が三角形内に無いか
                    bool any = false;
                    for (int j = 0; j < m; j++)
                    {
                        int ij = idx[j];
                        if (ij == i0 || ij == i1 || ij == i2) continue;
                        if (PointInTri(poly[ij], a, b, c)) { any = true; break; }
                    }
                    if (any) continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) return null; // 自己交差など
                if (++guard > maxGuard) return null;
            }
            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            return tris;
        }

        private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        // ===================================================================
        //  共通ヘルパー
        // ===================================================================

        private static List<Face> BuildFaces(int[] tris, int[] spatial, List<Vector3> spatialPos)
        {
            var faces = new List<Face>(tris.Length / 3);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                int sa = spatial[a], sb = spatial[b], sc = spatial[c];
                if (sa == sb || sb == sc || sa == sc) continue;

                Vector3 pa = spatialPos[sa], pb = spatialPos[sb], pc = spatialPos[sc];
                Vector3 nrm = Vector3.Cross(pb - pa, pc - pa);
                if (nrm.sqrMagnitude < 1e-14f) continue;

                var f = new Face { n = nrm.normalized };
                f.o[0] = a; f.o[1] = b; f.o[2] = c;
                f.s[0] = sa; f.s[1] = sb; f.s[2] = sc;
                faces.Add(f);
            }
            return faces;
        }

        private static int CornerAt(Face f, int spatialId)
        {
            if (f.s[0] == spatialId) return f.o[0];
            if (f.s[1] == spatialId) return f.o[1];
            if (f.s[2] == spatialId) return f.o[2];
            return -1;
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static void DecodeEdge(long key, out int a, out int b)
        {
            a = (int)(key >> 32);
            b = (int)(key & 0xFFFFFFFF);
        }

        private struct VKey : IEquatable<VKey>
        {
            private readonly int px, py, pz, nx, ny, nz, u, v, u2, v2, cr, cg, cb, ca;

            public VKey(Vector3 p, Vector3 n, Vector2 uv0, Vector2 uv2, Color col, float posQ)
            {
                float invP = 1f / Mathf.Max(posQ, 1e-6f);
                px = Mathf.RoundToInt(p.x * invP);
                py = Mathf.RoundToInt(p.y * invP);
                pz = Mathf.RoundToInt(p.z * invP);
                nx = Mathf.RoundToInt(n.x * 100f);
                ny = Mathf.RoundToInt(n.y * 100f);
                nz = Mathf.RoundToInt(n.z * 100f);
                u = Mathf.RoundToInt(uv0.x * 10000f);
                v = Mathf.RoundToInt(uv0.y * 10000f);
                u2 = Mathf.RoundToInt(uv2.x * 10000f);
                v2 = Mathf.RoundToInt(uv2.y * 10000f);
                cr = Mathf.RoundToInt(col.r * 255f);
                cg = Mathf.RoundToInt(col.g * 255f);
                cb = Mathf.RoundToInt(col.b * 255f);
                ca = Mathf.RoundToInt(col.a * 255f);
            }

            public bool Equals(VKey o)
                => px == o.px && py == o.py && pz == o.pz
                && nx == o.nx && ny == o.ny && nz == o.nz
                && u == o.u && v == o.v && u2 == o.u2 && v2 == o.v2
                && cr == o.cr && cg == o.cg && cb == o.cb && ca == o.ca;

            public override bool Equals(object obj) => obj is VKey o && Equals(o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = px * 73856093;
                    h ^= py * 19349663;
                    h ^= pz * 83492791;
                    h ^= nx * 15485863;
                    h ^= ny * 32452843;
                    h ^= nz * 49979687;
                    h ^= u * 86028121;
                    h ^= v * 15485867;
                    h ^= u2 * 6291469;
                    h ^= v2 * 12582917;
                    h ^= cr * 3145739;
                    h ^= cg * 1572869;
                    h ^= cb * 786433;
                    h ^= ca * 393241;
                    return h;
                }
            }
        }

        private class UnionFind
        {
            private readonly int[] parent;
            private readonly int[] rank;

            public UnionFind(int n)
            {
                parent = new int[n];
                rank = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
            }

            public int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (rank[ra] < rank[rb]) { int t = ra; ra = rb; rb = t; }
                parent[rb] = ra;
                if (rank[ra] == rank[rb]) rank[ra]++;
            }
        }
    }
}
