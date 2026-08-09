using UnityEngine;

namespace Harvestline.Unity.View
{
    /// <summary>
    /// The single palette that drives the game's entire look (spec §9): ~12 colours,
    /// every structure references indices into it. Changing the game's whole palette is
    /// a one-asset edit. Authored as a ScriptableObject so it's a data asset, not code.
    /// </summary>
    [CreateAssetMenu(fileName = "Palette", menuName = "Harvestline/Palette")]
    public sealed class Palette : ScriptableObject
    {
        [Header("Ground")]
        public Color GroundTop = new(0.42f, 0.55f, 0.32f);
        public Color GroundSide = new(0.33f, 0.44f, 0.26f);
        public Color GridLine = new(0.28f, 0.37f, 0.22f);

        [Header("Structure bodies")]
        public Color Extraction = new(0.72f, 0.60f, 0.38f); // earthy
        public Color Processing = new(0.80f, 0.78f, 0.72f); // stone/plaster
        public Color Food = new(0.86f, 0.66f, 0.34f);       // warm
        public Color Power = new(0.45f, 0.47f, 0.52f);       // steel
        public Color Storage = new(0.74f, 0.72f, 0.60f);
        public Color Modifier = new(0.55f, 0.78f, 0.70f);    // greenhouse teal

        [Header("Accents & state")]
        public Color Accent = new(0.93f, 0.85f, 0.55f);
        public Color CraftPulse = new(1.00f, 0.95f, 0.70f);
        public Color Starved = new(0.86f, 0.36f, 0.30f);     // bottleneck: red
        public Color Blocked = new(0.90f, 0.70f, 0.25f);     // bottleneck: amber
        public Color Throttled = new(0.40f, 0.55f, 0.85f);   // bottleneck: blue

        [Header("Placement")]
        public Color GhostValid = new(0.55f, 0.85f, 0.55f, 0.6f);
        public Color GhostInvalid = new(0.85f, 0.40f, 0.40f, 0.6f);
    }
}
