using System;
using System.Globalization;
using Harvestline.Core.Content;
using Harvestline.Core.Economy;
using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using Harvestline.Core.Rng;

namespace Harvestline.Core.Save
{
    /// <summary>
    /// Serializes <see cref="GameState"/> to/from the save JSON (spec §8). Schema is
    /// versioned with a forward migration chain, so an old save is upgraded on load
    /// rather than discarded. The RNG state and <c>lastSimulatedUtc</c> are persisted
    /// so a resumed session continues the exact market stream and rejects device-clock
    /// rollback.
    ///
    /// The caller writes the returned string atomically (temp file → File.Replace);
    /// that file I/O lives in the Unity layer (SaveIO), not here, keeping Core free of
    /// any platform dependency.
    /// </summary>
    public static class SaveSerializer
    {
        public const int CurrentSchemaVersion = 1;

        public static string Serialize(GameState state, bool indent = false)
        {
            var root = JsonValue.Object();
            root.Set("schemaVersion", JsonValue.Of((long)CurrentSchemaVersion));
            root.Set("firstLaunchUtc", JsonValue.Of(state.FirstLaunchUtc));
            root.Set("lastSimulatedUtc", JsonValue.Of(state.LastSimulatedUtc));
            // RNG state as a string so its full 64-bit value survives (JSON numbers are doubles).
            root.Set("rngState", JsonValue.Of(state.Rng.State.ToString(CultureInfo.InvariantCulture)));

            // Colony.
            var colony = JsonValue.Object();
            colony.Set("population", JsonValue.Of((long)state.Colony.Population));
            colony.Set("credits", JsonValue.Of(state.Colony.Credits));
            colony.Set("seals", JsonValue.Of(state.Colony.Seals));
            colony.Set("lifetimeSeals", JsonValue.Of(state.Colony.LifetimeSeals));
            colony.Set("storesTokens", JsonValue.Of((long)state.Colony.StoresTokens));
            colony.Set("harvestsSucceeded", JsonValue.Of((long)state.Colony.HarvestsSucceeded));
            colony.Set("harvestsFailed", JsonValue.Of((long)state.Colony.HarvestsFailed));
            root.Set("colony", colony);

            // Grid.
            var grid = JsonValue.Object();
            grid.Set("edge", JsonValue.Of((long)state.Grid.Edge));
            var structures = JsonValue.Array();
            foreach (var s in state.Grid.Structures)
            {
                var so = JsonValue.Object();
                so.Set("instanceId", JsonValue.Of((long)s.InstanceId));
                so.Set("def", JsonValue.Of(s.Def.Id));
                so.Set("x", JsonValue.Of((long)s.X));
                so.Set("y", JsonValue.Of((long)s.Y));
                so.Set("facing", JsonValue.Of((long)s.Facing));
                so.Set("assigned", JsonValue.Of((long)s.AssignedItem));
                structures.Add(so);
            }
            grid.Set("structures", structures);
            root.Set("grid", grid);

            // Inventory (only non-zero stocks, deterministic order).
            var inv = JsonValue.Array();
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                double stock = state.Grid.Inventory.Get((ItemType)i);
                if (stock <= 0) continue;
                var e = JsonValue.Object();
                e.Set("item", JsonValue.Of((long)i));
                e.Set("qty", JsonValue.Of(stock));
                inv.Add(e);
            }
            root.Set("inventory", inv);

            // Market.
            var market = JsonValue.Array();
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (!state.Market.Commodities.TryGetValue(item, out var c)) continue;
                var co = JsonValue.Object();
                co.Set("item", JsonValue.Of((long)i));
                co.Set("base", JsonValue.Of(c.BasePrice));
                co.Set("price", JsonValue.Of(c.Price));
                co.Set("pressure", JsonValue.Of(c.Pressure));
                co.Set("depth", JsonValue.Of(c.MarketDepth));
                market.Add(co);
            }
            root.Set("market", market);

            return root.ToJsonString(indent);
        }

        public static GameState Deserialize(string json, ContentDatabase content)
        {
            var root = (JsonObject)JsonValue.Parse(json);
            int schema = root.Has("schemaVersion") ? root.GetInt("schemaVersion") : 0;
            if (schema > CurrentSchemaVersion)
                throw new NotSupportedException($"Save schema {schema} is newer than supported {CurrentSchemaVersion}.");

            root = Migrate(root, schema);

            long firstLaunch = root.GetLong("firstLaunchUtc");
            long lastSim = root.GetLong("lastSimulatedUtc");
            ulong rngState = ulong.Parse(root.GetString("rngState"), CultureInfo.InvariantCulture);
            var rng = new DeterministicRng(rngState);

            // Grid + structures.
            var gridObj = root.GetObject("grid");
            var grid = new GridState(gridObj.GetInt("edge"));
            var structures = gridObj.GetArray("structures");
            for (int i = 0; i < structures.Count; i++)
            {
                var so = (JsonObject)structures[i];
                var def = content.Get(so.GetString("def"));
                grid.Restore(
                    so.GetInt("instanceId"), def,
                    so.GetInt("x"), so.GetInt("y"),
                    (Facing)so.GetInt("facing"),
                    (ItemType)so.GetInt("assigned"));
            }
            grid.RecomputeStorageCapacity();

            // Inventory.
            var inv = root.GetArray("inventory");
            for (int i = 0; i < inv.Count; i++)
            {
                var e = (JsonObject)inv[i];
                grid.Inventory.Set((ItemType)e.GetInt("item"), e.GetDouble("qty"));
            }

            // Colony.
            var co = root.GetObject("colony");
            var colony = new ColonyState
            {
                Population = co.GetInt("population"),
                Credits = co.GetLong("credits"),
                Seals = co.GetLong("seals"),
                LifetimeSeals = co.GetLong("lifetimeSeals"),
                StoresTokens = co.GetInt("storesTokens"),
                HarvestsSucceeded = co.GetInt("harvestsSucceeded"),
                HarvestsFailed = co.GetInt("harvestsFailed"),
            };

            // Market.
            var market = new MarketSimulator(rng);
            var marr = root.GetArray("market");
            for (int i = 0; i < marr.Count; i++)
            {
                var mo = (JsonObject)marr[i];
                var c = market.Register((ItemType)mo.GetInt("item"), mo.GetDouble("base"), mo.GetDouble("depth"));
                c.Price = mo.GetDouble("price");
                c.Pressure = mo.GetDouble("pressure");
            }

            return new GameState(content, grid, colony, rng, market, firstLaunch, lastSim);
        }

        /// <summary>
        /// Anti-cheat: elapsed real seconds since the last simulation, clamped to be
        /// non-negative. A negative value means the device clock rolled back; the
        /// caller should treat <paramref name="clockRolledBack"/> as suspicious and
        /// accrue nothing (spec §8).
        /// </summary>
        public static double ElapsedSinceLastSim(GameState state, long nowUtc, out bool clockRolledBack)
        {
            long delta = nowUtc - state.LastSimulatedUtc;
            clockRolledBack = delta < 0;
            return clockRolledBack ? 0 : delta;
        }

        /// <summary>Forward migration chain. Each step upgrades one schema version.</summary>
        private static JsonObject Migrate(JsonObject root, int fromSchema)
        {
            // v0 -> v1: the initial versioned format. Older unversioned saves (schema 0)
            // are assumed to already carry the v1 field set in this prototype; future
            // versions add cases here, e.g.:
            //   if (fromSchema < 2) { ...transform...; fromSchema = 2; }
            return root;
        }
    }
}
