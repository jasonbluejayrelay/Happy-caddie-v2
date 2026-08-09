namespace Harvestline.Core.Model
{
    /// <summary>
    /// Every material that can exist in an inventory or flow through the factory.
    /// Power is deliberately NOT an item — it is a global budget (see PowerBudget).
    ///
    /// Items are a fixed, fundamental vocabulary, so they are an enum rather than
    /// data. Structures and recipes — the things a designer adds — are data
    /// (see ContentDatabase / StructureDef); the solver never switches on a
    /// structure id, only on generic item flows, so adding a machine never
    /// touches the solver (spec §8).
    /// </summary>
    public enum ItemType
    {
        None = 0,

        // Tier 0 — raw extraction
        Grain,
        Water,
        Stone,
        Timber,

        // Tier 1 — basic processing
        Flour,
        Egg,
        Brick,
        Plank,

        // Tier 2 — food goods
        Bread,
        Milk,
        Preserves,

        // Tier 3 — compound / high value
        Fertilizer,
        Rations,

        // Fuels (purchased / mined; Coal has no extractor in v1 — bought from market)
        Coal,
    }

    public static class ItemTypeExtensions
    {
        /// <summary>Total number of concrete item slots (excludes <see cref="ItemType.None"/>).</summary>
        public const int Count = (int)ItemType.Coal + 1;

        /// <summary>Dense 0-based index used to key rate/stock arrays deterministically.</summary>
        public static int Index(this ItemType item) => (int)item;
    }
}
