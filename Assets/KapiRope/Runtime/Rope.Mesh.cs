using UnityEngine;
using UnityEngine.Rendering;

namespace KapiRope
{
    // Draws the rope as tubes. With Smooth Bends it's one tube swept along a curve through the physics points;
    // without, every segment is its own straight piece with rounded ends that overlap at the joints.
    // U runs around the rope and V along it, scaled so a square texture covers one circumference.
    public sealed partial class Rope
    {
        Mesh m_Mesh;
        Vector3[] m_Vertices = new Vector3[0];
        Vector3[] m_Normals = new Vector3[0];
        Vector4[] m_Tangents = new Vector4[0];
        Vector2[] m_UVs = new Vector2[0];
        int[] m_Triangles = new int[0];

        Vector3[] m_LocalPoints = new Vector3[0];
        Vector3[] m_RingCenters = new Vector3[0];
        Vector3[] m_RingTangents = new Vector3[0];
        float[] m_RingDistances = new float[0];
        float[] m_RingRadii = new float[0];

        float[] m_Cos = new float[0];
        float[] m_Sin = new float[0];
        int m_BuiltStrips = -1;
        int m_BuiltRingsPerStrip = -1;
        int m_BuiltRadial = -1;

        Vector3 m_BoundsMin;
        Vector3 m_BoundsMax;
        float m_BoundsRadius;

        void CreateMesh()
        {
            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "Rope", hideFlags = HideFlags.DontSave };
                m_Mesh.MarkDynamic();
            }
            GetComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_BuiltStrips = -1;
            m_MeshDirty = true;
        }

        void DestroyMesh()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh == m_Mesh) filter.sharedMesh = null;
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
            }
            m_Mesh = null;
        }

        void BuildMesh()
        {
            m_MeshDirty = false;
            if (m_Mesh == null || m_Count < 2) return;

            // Build in local space; the rope object's scale should stay uniform.
            m_MeshRotation = transform.rotation;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            float scale = Mathf.Abs(transform.lossyScale.x);
            if (scale < 1e-6f) scale = 1f;
            if (m_LocalPoints.Length < m_Count) m_LocalPoints = new Vector3[Mathf.NextPowerOfTwo(m_Count)];
            for (int i = 0; i < m_Count; i++)
                m_LocalPoints[i] = toLocal.MultiplyPoint3x4(m_Positions[i]);

            EnsureTrigTables(m_RadialSegments);
            m_BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            m_BoundsMax = -m_BoundsMin;
            m_BoundsRadius = 0f;

            int strips, ringsPerStrip;
            if (m_SmoothBends) BuildSmoothTube(scale, out strips, out ringsPerStrip);
            else BuildStraightPieces(scale, out strips, out ringsPerStrip);
            Upload(strips, ringsPerStrip);
        }

        void BuildSmoothTube(float scale, out int strips, out int ringsPerStrip)
        {
            int n = m_Count;
            int sub = m_Smoothness;
            int bodyRings = (n - 1) * sub + 1;
            // The top is always flat, so this object is exactly the top of the rope; the end cap is only for the free end.
            int topRings = 2;
            int bottomRings = m_EndCap == RopeEndCap.Rounded ? CapRingCount() : 2;
            strips = 1;
            ringsPerStrip = topRings + bodyRings + bottomRings;
            EnsureVertexBuffers(ringsPerStrip * (m_RadialSegments + 1));
            if (m_RingCenters.Length < bodyRings)
            {
                int capacity = Mathf.NextPowerOfTwo(bodyRings);
                m_RingCenters = new Vector3[capacity];
                m_RingTangents = new Vector3[capacity];
                m_RingDistances = new float[capacity];
                m_RingRadii = new float[capacity];
            }

            int k = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = i > 0 ? m_LocalPoints[i - 1] : 2f * m_LocalPoints[0] - m_LocalPoints[1];
                Vector3 p1 = m_LocalPoints[i];
                Vector3 p2 = m_LocalPoints[i + 1];
                Vector3 p3 = i + 2 < n ? m_LocalPoints[i + 2] : 2f * m_LocalPoints[n - 1] - m_LocalPoints[n - 2];
                for (int j = 0; j < sub; j++)
                {
                    float t = (float)j / sub;
                    m_RingCenters[k] = CatmullRom(p0, p1, p2, p3, t);
                    m_RingDistances[k] = (i + t) * m_SegmentRest;
                    k++;
                }
            }
            m_RingCenters[k] = m_LocalPoints[n - 1];
            m_RingDistances[k] = m_Length;

            float localRadius = m_Radius / scale;
            Vector3 lastTangent = Vector3.down;
            for (k = 0; k < bodyRings; k++)
            {
                Vector3 d = m_RingCenters[Mathf.Min(k + 1, bodyRings - 1)] - m_RingCenters[Mathf.Max(k - 1, 0)];
                lastTangent = d.sqrMagnitude > 1e-12f ? d.normalized : lastTangent;
                m_RingTangents[k] = lastTangent;
                m_RingRadii[k] = localRadius * Thickness(m_RingDistances[k]);
            }

            float invTile = InvTile();
            Vector3 tangent = m_RingTangents[0];
            Vector3 normal = StartNormal(tangent);
            Vector3 binormal = Vector3.Cross(tangent, normal);

            Cap(RopeEndCap.Flat, false, 0, topRings, m_RingCenters[0], tangent, normal, binormal, m_RingRadii[0], 0f, invTile);

            for (k = 0; k < bodyRings; k++)
            {
                Vector3 t = m_RingTangents[k];
                if (k > 0) Transport(t, ref normal, ref binormal);

                float slope = 0f;
                if (bodyRings > 1)
                {
                    int a = Mathf.Max(k - 1, 0);
                    int b = Mathf.Min(k + 1, bodyRings - 1);
                    float ds = m_RingDistances[b] - m_RingDistances[a];
                    if (ds > 1e-6f) slope = (m_RingRadii[b] - m_RingRadii[a]) / ds * scale;
                }
                float tilt = 1f / Mathf.Sqrt(1f + slope * slope);
                float distance = m_RingDistances[k];
                EmitRing(topRings + k, m_RingCenters[k], t, normal, binormal, m_RingRadii[k], tilt, -slope * tilt, distance * invTile, distance * m_TextureTwist);
                Encapsulate(m_RingCenters[k], m_RingRadii[k]);
            }

            int last = bodyRings - 1;
            Cap(m_EndCap, true, topRings + bodyRings, bottomRings, m_RingCenters[last], m_RingTangents[last], normal, binormal, m_RingRadii[last], m_Length, invTile);
        }

        /// <summary>Gorilla Tag style: every segment is a straight capsule, so the rope only bends at its joints.</summary>
        void BuildStraightPieces(float scale, out int strips, out int ringsPerStrip)
        {
            int pieces = m_Count - 1;
            int capRings = CapRingCount();
            strips = pieces;
            ringsPerStrip = capRings * 2 + 2;
            EnsureVertexBuffers(strips * ringsPerStrip * (m_RadialSegments + 1));

            float localRadius = m_Radius / scale;
            float invTile = InvTile();
            Vector3 tangent = m_LocalPoints[1] - m_LocalPoints[0];
            tangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.down;
            Vector3 normal = StartNormal(tangent);
            Vector3 binormal = Vector3.Cross(tangent, normal);

            for (int i = 0; i < pieces; i++)
            {
                Vector3 a = m_LocalPoints[i];
                Vector3 b = m_LocalPoints[i + 1];
                Vector3 d = b - a;
                if (d.sqrMagnitude > 1e-12f) tangent = d.normalized;
                if (i > 0) Transport(tangent, ref normal, ref binormal);

                float startDistance = i * m_SegmentRest;
                float endDistance = (i + 1) * m_SegmentRest;
                float ra = localRadius * Thickness(startDistance);
                float rb = localRadius * Thickness(endDistance);
                float slope = (rb - ra) / Mathf.Max(1e-6f, m_SegmentRest) * scale;
                float tilt = 1f / Mathf.Sqrt(1f + slope * slope);

                int ring = i * ringsPerStrip;
                // Rounded ends overlap at each joint like a knuckle; the very top stays flat at the anchor.
                Cap(i == 0 ? RopeEndCap.Flat : RopeEndCap.Rounded, false, ring, capRings, a, tangent, normal, binormal, ra, startDistance, invTile);
                ring += capRings;
                EmitRing(ring++, a, tangent, normal, binormal, ra, tilt, -slope * tilt, startDistance * invTile, startDistance * m_TextureTwist);
                EmitRing(ring++, b, tangent, normal, binormal, rb, tilt, -slope * tilt, endDistance * invTile, endDistance * m_TextureTwist);
                Cap(i == pieces - 1 ? m_EndCap : RopeEndCap.Rounded, true, ring, capRings, b, tangent, normal, binormal, rb, endDistance, invTile);

                Encapsulate(a, ra);
                Encapsulate(b, rb);
            }
        }

        /// <summary>
        /// Emits the rings of a cap at <paramref name="center"/>. A start cap goes from its tip to the rim; an end cap
        /// from the rim to its tip, so rings always run in the rope's direction.
        /// </summary>
        void Cap(RopeEndCap cap, bool atEnd, int firstRing, int rings, Vector3 center, Vector3 tangent, Vector3 normal, Vector3 binormal, float radius, float distance, float invTile)
        {
            float side = atEnd ? 1f : -1f;
            for (int c = 0; c < rings; c++)
            {
                int fromTip = atEnd ? rings - 1 - c : c;
                CapRing(cap, fromTip, rings, radius, out float offset, out float ringRadius, out float normalAlong, out float normalOut, out float arc);
                float twist = distance * m_TextureTwist;
                EmitRing(firstRing + c, center + tangent * (offset * side), tangent, normal, binormal, ringRadius, normalOut, normalAlong * side, (distance + arc * side) * invTile, twist);
            }
        }

        /// <summary>Shape of cap ring <paramref name="index"/> (0 = tip). Rounded caps are a hemisphere; flat caps are a disc.</summary>
        static void CapRing(RopeEndCap cap, int index, int capRings, float bodyRadius, out float offset, out float radius, out float normalAlong, out float normalOut, out float arc)
        {
            if (cap == RopeEndCap.Flat)
            {
                offset = 0f;
                radius = index == 0 ? 0f : bodyRadius;
                normalAlong = 1f;
                normalOut = 0f;
                arc = index == 0 ? bodyRadius : 0f;
                return;
            }

            float angle = 0.5f * Mathf.PI * (capRings - index) / capRings;
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            offset = bodyRadius * sin;
            radius = bodyRadius * cos;
            normalAlong = sin;
            normalOut = cos;
            arc = bodyRadius * angle;
        }

        void EmitRing(int ring, Vector3 center, Vector3 tangent, Vector3 normal, Vector3 binormal, float radius, float normalOut, float normalAlong, float v, float uOffset)
        {
            int radial = m_RadialSegments;
            int index = ring * (radial + 1);
            float invRadial = 1f / radial;
            for (int j = 0; j <= radial; j++, index++)
            {
                float cos = m_Cos[j];
                float sin = m_Sin[j];
                Vector3 direction = normal * cos + binormal * sin;
                m_Vertices[index] = center + direction * radius;
                m_Normals[index] = direction * normalOut + tangent * normalAlong;
                Vector3 around = binormal * cos - normal * sin;
                m_Tangents[index] = new Vector4(around.x, around.y, around.z, 1f);
                m_UVs[index] = new Vector2(j * invRadial + uOffset, v);
            }
        }

        void Upload(int strips, int ringsPerStrip)
        {
            int radial = m_RadialSegments;
            int vertexCount = strips * ringsPerStrip * (radial + 1);
            int indexCount = strips * (ringsPerStrip - 1) * radial * 6;

            bool topologyChanged = strips != m_BuiltStrips || ringsPerStrip != m_BuiltRingsPerStrip || radial != m_BuiltRadial;
            if (topologyChanged)
            {
                m_Mesh.Clear();
                m_Mesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                BuildTriangles(strips, ringsPerStrip, radial, indexCount);
            }

            m_Mesh.SetVertices(m_Vertices, 0, vertexCount);
            m_Mesh.SetNormals(m_Normals, 0, vertexCount);
            m_Mesh.SetTangents(m_Tangents, 0, vertexCount);
            m_Mesh.SetUVs(0, m_UVs, 0, vertexCount);
            if (topologyChanged)
            {
                m_Mesh.SetTriangles(m_Triangles, 0, indexCount, 0, false);
                m_BuiltStrips = strips;
                m_BuiltRingsPerStrip = ringsPerStrip;
                m_BuiltRadial = radial;
            }

            var bounds = new Bounds();
            bounds.SetMinMax(m_BoundsMin, m_BoundsMax);
            bounds.Expand(m_BoundsRadius * 2.5f);
            m_Mesh.bounds = bounds;
        }

        void BuildTriangles(int strips, int ringsPerStrip, int radial, int indexCount)
        {
            if (m_Triangles.Length < indexCount) m_Triangles = new int[Mathf.NextPowerOfTwo(indexCount)];
            int ringVerts = radial + 1;
            int index = 0;
            for (int s = 0; s < strips; s++)
            {
                for (int k = 0; k < ringsPerStrip - 1; k++)
                {
                    int row = (s * ringsPerStrip + k) * ringVerts;
                    for (int j = 0; j < radial; j++)
                    {
                        int a = row + j;
                        int b = a + 1;
                        int c = a + ringVerts;
                        int d = c + 1;
                        m_Triangles[index++] = a;
                        m_Triangles[index++] = b;
                        m_Triangles[index++] = d;
                        m_Triangles[index++] = a;
                        m_Triangles[index++] = d;
                        m_Triangles[index++] = c;
                    }
                }
            }
        }

        int CapRingCount() => Mathf.Clamp(m_RadialSegments / 3, 2, 6);

        float Thickness(float distance) => Mathf.Max(0f, m_ThicknessAlongLength.Evaluate(distance / m_Length));

        float InvTile() => 1f / Mathf.Max(1e-5f, 2f * Mathf.PI * m_Radius * m_TextureScale);

        void Encapsulate(Vector3 center, float radius)
        {
            m_BoundsMin = Vector3.Min(m_BoundsMin, center);
            m_BoundsMax = Vector3.Max(m_BoundsMax, center);
            if (radius > m_BoundsRadius) m_BoundsRadius = radius;
        }

        static Vector3 StartNormal(Vector3 tangent)
        {
            Vector3 normal = Vector3.ProjectOnPlane(Vector3.right, tangent);
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.ProjectOnPlane(Vector3.forward, tangent);
            return normal.normalized;
        }

        /// <summary>Carries the frame along the rope (parallel transport), so the texture doesn't spin as it bends.</summary>
        static void Transport(Vector3 tangent, ref Vector3 normal, ref Vector3 binormal)
        {
            normal -= tangent * Vector3.Dot(normal, tangent);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.ProjectOnPlane(Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right, tangent);
            normal.Normalize();
            binormal = Vector3.Cross(tangent, normal);
        }

        void EnsureVertexBuffers(int vertexCount)
        {
            if (m_Vertices.Length >= vertexCount) return;
            int capacity = Mathf.NextPowerOfTwo(vertexCount);
            m_Vertices = new Vector3[capacity];
            m_Normals = new Vector3[capacity];
            m_Tangents = new Vector4[capacity];
            m_UVs = new Vector2[capacity];
        }

        void EnsureTrigTables(int radial)
        {
            if (m_Cos.Length == radial + 1) return;
            m_Cos = new float[radial + 1];
            m_Sin = new float[radial + 1];
            for (int j = 0; j <= radial; j++)
            {
                float angle = 2f * Mathf.PI * j / radial;
                m_Cos[j] = Mathf.Cos(angle);
                m_Sin[j] = Mathf.Sin(angle);
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
