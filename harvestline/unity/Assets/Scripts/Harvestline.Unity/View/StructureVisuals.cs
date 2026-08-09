using System.Collections.Generic;
using Harvestline.Core.Model;
using UnityEngine;

namespace Harvestline.Unity.View
{
    /// <summary>Resolved visual for a structure id: archetype + body/accent colours.</summary>
    public readonly struct StructureVisual
    {
        public readonly StructureArchetype Archetype;
        public readonly Color Body;
        public readonly Color Accent;
        public StructureVisual(StructureArchetype a, Color body, Color accent)
        { Archetype = a; Body = body; Accent = accent; }
    }

    /// <summary>
    /// Maps each content structure id to a silhouette archetype and palette colours, and
    /// caches one baked mesh per id (meshes are shared and GPU-instanced by GridRenderer).
    /// This is the one place that knows "what a Bakery looks like"; it derives everything
    /// from the Core <see cref="StructureDef"/> category so a new machine gets a sensible
    /// default look without code changes.
    /// </summary>
    public sealed class StructureVisuals
    {
        private readonly Palette _palette;
        private readonly float _tileSize;
        private readonly Dictionary<string, Mesh> _meshCache = new();

        public StructureVisuals(Palette palette, float tileSize)
        {
            _palette = palette;
            _tileSize = tileSize;
        }

        public StructureVisual Resolve(StructureDef def)
        {
            // Explicit archetypes for the recognisable machines; category default otherwise.
            switch (def.Id)
            {
                case "soil_plot": return new(StructureArchetype.FieldLow, _palette.Extraction, new Color(0.5f, 0.7f, 0.3f));
                case "woodlot": return new(StructureArchetype.FieldLow, _palette.Extraction, new Color(0.3f, 0.55f, 0.3f));
                case "water_pump": return new(StructureArchetype.Pump, _palette.Extraction, _palette.Throttled);
                case "quarry": return new(StructureArchetype.Hall, _palette.Extraction, _palette.Storage);
                case "mill": return new(StructureArchetype.Mill, _palette.Processing, _palette.Accent);
                case "sawmill": return new(StructureArchetype.Mill, _palette.Processing, _palette.Extraction);
                case "kiln": return new(StructureArchetype.Mill, _palette.Processing, _palette.Food);
                case "coop": return new(StructureArchetype.Hall, _palette.Food, _palette.Accent);
                case "bakery": return new(StructureArchetype.Hall, _palette.Food, _palette.Accent);
                case "dairy": return new(StructureArchetype.Hall, _palette.Food, _palette.Processing);
                case "cannery": return new(StructureArchetype.Hall, _palette.Food, _palette.Power);
                case "composter": return new(StructureArchetype.Mill, _palette.Modifier, _palette.Extraction);
                case "ration_line": return new(StructureArchetype.Hall, _palette.Food, _palette.CraftPulse);
                case "silo": return new(StructureArchetype.Tower, _palette.Storage, _palette.Accent);
                case "biomass_generator": return new(StructureArchetype.Generator, _palette.Power, _palette.Accent);
                case "coal_generator": return new(StructureArchetype.Generator, _palette.Power, _palette.Starved);
                case "greenhouse": return new(StructureArchetype.Greenhouse, _palette.Modifier, _palette.Accent);
            }

            return def.Category switch
            {
                StructureCategory.Extractor => new(StructureArchetype.FieldLow, _palette.Extraction, _palette.Accent),
                StructureCategory.Processor => new(StructureArchetype.Mill, _palette.Processing, _palette.Accent),
                StructureCategory.Generator => new(StructureArchetype.Generator, _palette.Power, _palette.Accent),
                StructureCategory.Storage => new(StructureArchetype.Tower, _palette.Storage, _palette.Accent),
                StructureCategory.Modifier => new(StructureArchetype.Greenhouse, _palette.Modifier, _palette.Accent),
                _ => new(StructureArchetype.Default, _palette.Processing, _palette.Accent),
            };
        }

        /// <summary>Get (baking on first use) the shared mesh for a structure def.</summary>
        public Mesh MeshFor(StructureDef def)
        {
            if (_meshCache.TryGetValue(def.Id, out var m)) return m;
            var vis = Resolve(def);
            m = MeshFactory.Structure(vis.Archetype, def.Size, _tileSize, vis.Body, vis.Accent);
            _meshCache[def.Id] = m;
            return m;
        }
    }
}
