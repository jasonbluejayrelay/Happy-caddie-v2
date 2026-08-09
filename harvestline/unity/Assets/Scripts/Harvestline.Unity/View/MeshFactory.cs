using System.Collections.Generic;
using UnityEngine;

namespace Harvestline.Unity.View
{
    /// <summary>
    /// Builds flat-shaded low-poly meshes at runtime from vertex arrays (spec §9).
    /// Every triangle gets its own vertices so normals are hard (no smoothing groups),
    /// and colour is carried in the vertex stream — one material for the whole scene,
    /// no textures. Shapes are simple prisms/boxes chosen so each structure reads at
    /// 40px by silhouette and height alone.
    /// </summary>
    public static class MeshFactory
    {
        private sealed class Builder
        {
            private readonly List<Vector3> _v = new();
            private readonly List<Color> _c = new();
            private readonly List<int> _t = new();

            /// <summary>Add a quad (4 corners, CCW). Splits into two hard-normal triangles.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _c.Add(color); _c.Add(color); _c.Add(color); _c.Add(color);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            /// <summary>Axis-aligned box from (min) to (max) with a single colour.</summary>
            public void Box(Vector3 min, Vector3 max, Color color)
            {
                Vector3 a = new(min.x, min.y, min.z);
                Vector3 b = new(max.x, min.y, min.z);
                Vector3 c = new(max.x, min.y, max.z);
                Vector3 d = new(min.x, min.y, max.z);
                Vector3 e = new(min.x, max.y, min.z);
                Vector3 f = new(max.x, max.y, min.z);
                Vector3 g = new(max.x, max.y, max.z);
                Vector3 h = new(min.x, max.y, max.z);

                Quad(h, g, f, e, color);           // top (+y)
                Quad(a, b, c, d, color);           // bottom (-y)
                Quad(e, f, b, a, color);           // front (-z)
                Quad(g, h, d, c, color);           // back (+z)
                Quad(h, e, a, d, color);           // left (-x)
                Quad(f, g, c, b, color);           // right (+x)
            }

            /// <summary>A tapered box (roof/hopper): top face shrunk toward centre by inset.</summary>
            public void Frustum(Vector3 min, Vector3 max, float topInset, Color color)
            {
                float y0 = min.y, y1 = max.y;
                Vector3 a = new(min.x, y0, min.z);
                Vector3 b = new(max.x, y0, min.z);
                Vector3 c = new(max.x, y0, max.z);
                Vector3 d = new(min.x, y0, max.z);
                Vector3 e = new(min.x + topInset, y1, min.z + topInset);
                Vector3 f = new(max.x - topInset, y1, min.z + topInset);
                Vector3 g = new(max.x - topInset, y1, max.z - topInset);
                Vector3 h = new(min.x + topInset, y1, max.z - topInset);

                Quad(h, g, f, e, color);
                Quad(a, b, c, d, color);
                Quad(e, f, b, a, color);
                Quad(g, h, d, c, color);
                Quad(h, e, a, d, color);
                Quad(f, g, c, b, color);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (_v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(_v);
                mesh.SetColors(_c);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals(); // hard normals: each tri has unique verts
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>A flat tile plate used for the ground grid cells.</summary>
        public static Mesh TilePlate(float size, float thickness, Color top, Color side)
        {
            var b = new Builder();
            float h = size * 0.5f;
            b.Box(new Vector3(-h, -thickness, -h), new Vector3(h, 0, h), side);
            // Slightly inset top face for a bevel read.
            b.Quad(
                new Vector3(-h * 0.96f, 0.001f, -h * 0.96f),
                new Vector3(h * 0.96f, 0.001f, -h * 0.96f),
                new Vector3(h * 0.96f, 0.001f, h * 0.96f),
                new Vector3(-h * 0.96f, 0.001f, h * 0.96f),
                top);
            return b.ToMesh("tile");
        }

        /// <summary>
        /// Build a structure mesh sized to its footprint (tiles) and a visual archetype.
        /// The archetype drives silhouette so machines are told apart by shape/height.
        /// </summary>
        public static Mesh Structure(StructureArchetype archetype, int footprintTiles, float tileSize,
            Color body, Color accent)
        {
            var b = new Builder();
            float w = footprintTiles * tileSize * 0.5f * 0.86f; // slight inset within footprint
            switch (archetype)
            {
                case StructureArchetype.FieldLow: // soil plot / woodlot: flat, low
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.12f * tileSize, w), body);
                    b.Box(new Vector3(-w * 0.5f, 0.12f * tileSize, -w * 0.5f),
                          new Vector3(w * 0.5f, 0.20f * tileSize, w * 0.5f), accent);
                    break;

                case StructureArchetype.Pump: // water pump: short cylinder-ish box + stack
                    b.Box(new Vector3(-w * 0.7f, 0, -w * 0.7f), new Vector3(w * 0.7f, 0.4f * tileSize, w * 0.7f), body);
                    b.Box(new Vector3(-w * 0.15f, 0.4f * tileSize, -w * 0.15f),
                          new Vector3(w * 0.15f, 0.9f * tileSize, w * 0.15f), accent);
                    break;

                case StructureArchetype.Mill: // mill: box with a tall wheel accent
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.6f * tileSize, w), body);
                    b.Frustum(new Vector3(-w, 0.6f * tileSize, -w), new Vector3(w, 0.95f * tileSize, w), w * 0.4f, accent);
                    break;

                case StructureArchetype.Hall: // bakery/dairy/cannery: big processing hall (2x2)
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.55f * tileSize, w), body);
                    b.Frustum(new Vector3(-w, 0.55f * tileSize, -w), new Vector3(w, 0.85f * tileSize, w), w * 0.25f, accent);
                    // chimney
                    b.Box(new Vector3(w * 0.5f, 0.55f * tileSize, w * 0.5f),
                          new Vector3(w * 0.72f, 1.15f * tileSize, w * 0.72f), accent);
                    break;

                case StructureArchetype.Tower: // silo: tall narrow tower
                    b.Box(new Vector3(-w * 0.55f, 0, -w * 0.55f), new Vector3(w * 0.55f, 1.2f * tileSize, w * 0.55f), body);
                    b.Frustum(new Vector3(-w * 0.55f, 1.2f * tileSize, -w * 0.55f),
                              new Vector3(w * 0.55f, 1.5f * tileSize, w * 0.55f), w * 0.5f, accent);
                    break;

                case StructureArchetype.Generator: // generator: wide low block + vent
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.5f * tileSize, w), body);
                    b.Box(new Vector3(-w * 0.8f, 0.5f * tileSize, -w * 0.2f),
                          new Vector3(w * 0.8f, 0.75f * tileSize, w * 0.2f), accent);
                    break;

                case StructureArchetype.Greenhouse: // greenhouse: low glass roof (2x2)
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.25f * tileSize, w), body);
                    b.Frustum(new Vector3(-w, 0.25f * tileSize, -w), new Vector3(w, 0.7f * tileSize, w), w * 0.15f, accent);
                    break;

                default:
                    b.Box(new Vector3(-w, 0, -w), new Vector3(w, 0.5f * tileSize, w), body);
                    break;
            }
            return b.ToMesh($"structure_{archetype}");
        }

        /// <summary>A single quad billboard mesh for instanced conveyor items (spec §9).</summary>
        public static Mesh ItemQuad(float size)
        {
            var b = new Builder();
            float h = size * 0.5f;
            b.Quad(new Vector3(-h, 0, -h), new Vector3(h, 0, -h),
                   new Vector3(h, 0, h), new Vector3(-h, 0, h), Color.white);
            return b.ToMesh("item_quad");
        }
    }

    /// <summary>Visual archetypes — how a structure reads in silhouette (spec §9).</summary>
    public enum StructureArchetype
    {
        FieldLow, Pump, Mill, Hall, Tower, Generator, Greenhouse, Default,
    }
}
