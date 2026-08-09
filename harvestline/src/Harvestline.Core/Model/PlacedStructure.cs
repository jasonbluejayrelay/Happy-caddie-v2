namespace Harvestline.Core.Model
{
    /// <summary>Facing direction for conveyors (producers do not rotate). Reserved for M2 transport.</summary>
    public enum Facing { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>
    /// A concrete instance of a <see cref="StructureDef"/> placed on the grid at a tile.
    /// (X,Y) is the south-west (min) corner of the footprint.
    /// </summary>
    public sealed class PlacedStructure
    {
        /// <summary>Stable per-instance id (also its index into simulation arrays).</summary>
        public int InstanceId { get; }
        public StructureDef Def { get; }
        public int X { get; }
        public int Y { get; }
        public Facing Facing { get; }

        /// <summary>For a Silo: the single item type its storage bonus is assigned to.</summary>
        public ItemType AssignedItem { get; set; }

        public PlacedStructure(int instanceId, StructureDef def, int x, int y,
            Facing facing = Facing.North, ItemType assignedItem = ItemType.None)
        {
            InstanceId = instanceId;
            Def = def;
            X = x;
            Y = y;
            Facing = facing;
            AssignedItem = assignedItem;
        }

        /// <summary>True if this structure's footprint covers tile (tx,ty).</summary>
        public bool Covers(int tx, int ty)
        {
            int s = Def.Size;
            return tx >= X && tx < X + s && ty >= Y && ty < Y + s;
        }

        /// <summary>True if <paramref name="other"/>'s footprint is orthogonally adjacent to this one.</summary>
        public bool IsAdjacentTo(PlacedStructure other)
        {
            // Two axis-aligned rectangles are orthogonally adjacent if they do not overlap
            // but their footprints touch along an edge (share a border, not just a corner).
            int aMinX = X, aMinY = Y, aMaxX = X + Def.Size - 1, aMaxY = Y + Def.Size - 1;
            int bMinX = other.X, bMinY = other.Y, bMaxX = other.X + other.Def.Size - 1, bMaxY = other.Y + other.Def.Size - 1;

            bool xOverlap = aMinX <= bMaxX && bMinX <= aMaxX;
            bool yOverlap = aMinY <= bMaxY && bMinY <= aMaxY;

            // Adjacent vertically: x-ranges overlap and y-ranges are exactly one apart.
            bool touchVert = xOverlap && (aMaxY + 1 == bMinY || bMaxY + 1 == aMinY);
            // Adjacent horizontally: y-ranges overlap and x-ranges are exactly one apart.
            bool touchHoriz = yOverlap && (aMaxX + 1 == bMinX || bMaxX + 1 == aMinX);
            return touchVert || touchHoriz;
        }
    }
}
