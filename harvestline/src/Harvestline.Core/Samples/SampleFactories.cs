using Harvestline.Core.Content;
using Harvestline.Core.Model;

namespace Harvestline.Core.Samples
{
    /// <summary>
    /// Hand-built factory layouts used by the console harness and unit tests so both
    /// exercise the same known-good configurations. Pure data construction — no Unity.
    /// </summary>
    public static class SampleFactories
    {
        /// <summary>
        /// A complete Bread supply chain: Grain -> Flour -> Bread, plus Water, on an
        /// 8×8 grid. Balanced so no single stage starves the next at steady state.
        /// </summary>
        public static GridState BreadLine(ContentDatabase db)
        {
            var grid = new GridState(GridState.MinEdge);
            // Extraction.
            grid.Place(db.Get("soil_plot"), 0, 0);
            grid.Place(db.Get("soil_plot"), 1, 0);
            grid.Place(db.Get("soil_plot"), 2, 0);
            grid.Place(db.Get("soil_plot"), 3, 0);
            grid.Place(db.Get("water_pump"), 0, 2);
            grid.Place(db.Get("water_pump"), 1, 2);
            // Processing.
            grid.Place(db.Get("mill"), 3, 2);
            grid.Place(db.Get("mill"), 4, 2);
            grid.Place(db.Get("bakery"), 5, 0); // 2×2
            // Power: a couple of woodlots feeding a biomass generator.
            grid.Place(db.Get("woodlot"), 0, 4);
            grid.Place(db.Get("woodlot"), 1, 4);
            grid.Place(db.Get("biomass_generator"), 0, 6);
            return grid;
        }

        /// <summary>
        /// The Composter -> Greenhouse -> Soil Plot feedback loop (spec §7 cycle case):
        /// a Greenhouse boosts adjacent Soil Plots while fed Fertilizer from a Composter,
        /// which needs Timber + Water. Exercises the fixed-point solver.
        /// </summary>
        public static GridState GreenhouseCycle(ContentDatabase db)
        {
            var grid = new GridState(GridState.MinEdge);
            // Greenhouse is 2×2 at (2,2); soil plots hug its edges so they are adjacent.
            grid.Place(db.Get("greenhouse"), 2, 2);
            grid.Place(db.Get("soil_plot"), 1, 2);
            grid.Place(db.Get("soil_plot"), 1, 3);
            grid.Place(db.Get("soil_plot"), 2, 4);
            grid.Place(db.Get("soil_plot"), 3, 4);
            // Fertilizer supply.
            grid.Place(db.Get("woodlot"), 0, 0);
            grid.Place(db.Get("woodlot"), 1, 0);
            grid.Place(db.Get("water_pump"), 2, 0);
            grid.Place(db.Get("composter"), 4, 2);
            // Power for the water pump.
            grid.Place(db.Get("biomass_generator"), 6, 0);
            return grid;
        }
    }
}
