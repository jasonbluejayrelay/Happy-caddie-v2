using System.Collections.Generic;
using Harvestline.Core;
using Harvestline.Core.Model;
using Harvestline.Core.Simulation;
using UnityEngine;

namespace Harvestline.Unity.View
{
    /// <summary>
    /// Renders the whole colony every frame with GPU instancing (spec §9): ground tiles
    /// and structures are drawn with <see cref="Graphics.DrawMeshInstanced"/>, grouped by
    /// shared mesh, so a full 24×24 grid is a handful of draw calls rather than thousands
    /// of GameObjects. Reads Core state read-only; never mutates the simulation.
    ///
    /// Per-instance state (bottleneck colour, craft pulse) rides in a
    /// MaterialPropertyBlock via the shader's instanced emission tint.
    /// </summary>
    public sealed class GridRenderer : MonoBehaviour
    {
        [SerializeField] private Palette _palette = null!;
        [SerializeField] private Shader _shader = null!; // Harvestline/Palette
        [SerializeField] private float _tileSize = 1.0f;

        private Material _material = null!;
        private Mesh _tileMesh = null!;
        private StructureVisuals _visuals = null!;

        private GameState _game = null!;
        private BottleneckReport? _lastReport;

        // Reusable instancing buffers (DrawMeshInstanced caps at 1023 per call).
        private const int Batch = 1023;
        private readonly Matrix4x4[] _matrices = new Matrix4x4[Batch];
        private readonly Vector4[] _tints = new Vector4[Batch];
        private MaterialPropertyBlock _mpb = null!;
        private static readonly int InstanceTintId = Shader.PropertyToID("_InstanceTint");

        public void Initialize(GameState game)
        {
            _game = game;
            _material = new Material(_shader) { enableInstancing = true };
            _visuals = new StructureVisuals(_palette, _tileSize);
            _tileMesh = MeshFactory.TilePlate(_tileSize, _tileSize * 0.1f, _palette.GroundTop, _palette.GroundSide);
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>Supply the latest bottleneck report so machines can tint by their worst state.</summary>
        public void SetBottleneckReport(BottleneckReport report) => _lastReport = report;

        private void Update()
        {
            if (_game == null) return;
            DrawGround();
            DrawStructures();
        }

        private void DrawGround()
        {
            int edge = _game.Grid.Edge;
            int count = 0;
            for (int y = 0; y < edge; y++)
            {
                for (int x = 0; x < edge; x++)
                {
                    _matrices[count] = Matrix4x4.TRS(CellCenter(x, y, 1), Quaternion.identity, Vector3.one);
                    if (++count == Batch) { FlushGround(count); count = 0; }
                }
            }
            if (count > 0) FlushGround(count);
        }

        private void FlushGround(int count) =>
            Graphics.DrawMeshInstanced(_tileMesh, 0, _material, _matrices, count);

        private void DrawStructures()
        {
            // Group by mesh so each distinct structure type is one (or few) instanced calls.
            var byMesh = new Dictionary<Mesh, List<PlacedStructure>>();
            foreach (var s in _game.Grid.Structures)
            {
                var mesh = _visuals.MeshFor(s.Def);
                if (!byMesh.TryGetValue(mesh, out var list)) byMesh[mesh] = list = new List<PlacedStructure>();
                list.Add(s);
            }

            foreach (var kv in byMesh)
            {
                var mesh = kv.Key;
                var list = kv.Value;
                int count = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    _matrices[count] = Matrix4x4.TRS(CellCenter(s.X, s.Y, s.Def.Size), Quaternion.identity, Vector3.one);
                    _tints[count] = StateTint(s);
                    if (++count == Batch) { FlushStructures(mesh, count); count = 0; }
                }
                if (count > 0) FlushStructures(mesh, count);
            }
        }

        private void FlushStructures(Mesh mesh, int count)
        {
            _mpb.Clear();
            // Per-instance tint slice (the array length must match the instance count).
            var slice = new Vector4[count];
            System.Array.Copy(_tints, slice, count);
            _mpb.SetVectorArray(InstanceTintId, slice);
            Graphics.DrawMeshInstanced(mesh, 0, _material, _matrices, count, _mpb);
        }

        /// <summary>Per-instance emission tint: pulse on recent craft, or a bottleneck colour.</summary>
        private Vector4 StateTint(PlacedStructure s)
        {
            if (_lastReport != null)
            {
                var e = _lastReport.ForInstance(s.InstanceId);
                switch (e.WorstReason)
                {
                    case LimitReason.Starved: return Fade(_palette.Starved, e.StarvedFraction);
                    case LimitReason.Blocked: return Fade(_palette.Blocked, e.BlockedFraction);
                    case LimitReason.Throttled: return Fade(_palette.Throttled, e.ThrottledFraction);
                }
            }
            return Vector4.zero;
        }

        private static Vector4 Fade(Color c, double frac)
        {
            float f = Mathf.Clamp01((float)frac) * 0.5f;
            return new Vector4(c.r * f, c.g * f, c.b * f, 0);
        }

        /// <summary>World-space centre of a footprint whose min corner is (x,y), edge `size` tiles.</summary>
        private Vector3 CellCenter(int x, int y, int size)
        {
            float half = _game.Grid.Edge * _tileSize * 0.5f;
            float cx = (x + size * 0.5f) * _tileSize - half;
            float cz = (y + size * 0.5f) * _tileSize - half;
            return new Vector3(cx, 0, cz);
        }

        /// <summary>Convert a world hit back to a tile coordinate (for placement/selection).</summary>
        public bool WorldToTile(Vector3 world, out int tx, out int ty)
        {
            float half = _game.Grid.Edge * _tileSize * 0.5f;
            tx = Mathf.FloorToInt((world.x + half) / _tileSize);
            ty = Mathf.FloorToInt((world.z + half) / _tileSize);
            return tx >= 0 && ty >= 0 && tx < _game.Grid.Edge && ty < _game.Grid.Edge;
        }
    }
}
