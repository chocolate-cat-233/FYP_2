using UnityEngine;

namespace SuperFinalFuse.RevealDisc
{
    /// <summary>
    /// Generates a procedural horizontal disc mesh used for the stencil write
    /// and the cosmetic decal. The mesh is a unit-radius circle in the XZ plane
    /// (Y-up), subdivided into concentric rings for smooth edge transitions.
    /// </summary>
    public static class RevealDiscMesh
    {
        private const int DefaultRadialSegments = 64;
        private const int DefaultRings = 4;

        private static Mesh cachedMesh;

        /// <summary>
        /// Returns a shared unit-radius disc mesh in the XZ plane.
        /// The mesh is centered at origin, radius = 1, and includes
        /// concentric ring subdivisions for smooth gradient sampling.
        /// </summary>
        public static Mesh GetDiscMesh()
        {
            if (cachedMesh != null)
                return cachedMesh;

            cachedMesh = CreateDiscMesh(DefaultRadialSegments, DefaultRings);
            cachedMesh.name = "RevealDiscMesh (Procedural)";
            return cachedMesh;
        }

        /// <summary>
        /// Creates a new disc mesh with the given subdivision.
        /// </summary>
        /// <param name="radialSegments">Number of segments around the circumference.</param>
        /// <param name="rings">Number of concentric rings from center to edge.</param>
        public static Mesh CreateDiscMesh(int radialSegments, int rings)
        {
            Mesh mesh = new Mesh();

            int vertexCount = (radialSegments + 1) * (rings + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            int[] triangles = new int[radialSegments * rings * 6];

            // Generate vertices in concentric rings
            for (int ring = 0; ring <= rings; ring++)
            {
                float radius = (float)ring / rings; // 0 at center, 1 at edge
                for (int seg = 0; seg <= radialSegments; seg++)
                {
                    float angle = (float)seg / radialSegments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;

                    int index = ring * (radialSegments + 1) + seg;
                    vertices[index] = new Vector3(x, 0f, z);
                    uv[index] = new Vector2((float)seg / radialSegments, radius);
                }
            }

            // Generate triangle indices
            int triIndex = 0;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < radialSegments; seg++)
                {
                    int current = ring * (radialSegments + 1) + seg;
                    int next = current + radialSegments + 1;

                    // Two triangles per quad
                    triangles[triIndex++] = current;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = current + 1;

                    triangles[triIndex++] = current + 1;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
