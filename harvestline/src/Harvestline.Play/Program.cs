using System.Globalization;
using System.Text;
using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using Harvestline.Core.Save;
using Harvestline.Core.Simulation;

namespace Harvestline.Play;

/// <summary>
/// Interactive, installable playable of Harvestline on the real simulation core.
/// Build a factory, let time pass (offline accrual), and race the Harvest deadline —
/// the whole hypothesis of the game (spec §1/§12), playable in a terminal.
/// </summary>
internal static class Program
{
    private static readonly ContentDatabase Db = ContentDatabase.CreateDefault();
    private static GameState _game = null!;
    private static long _now; // virtual clock, seconds since first launch
    private static readonly string SavePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } d
            ? d : ".", "harvestline.save.json");

    private static readonly (string id, char glyph)[] Glyphs =
    {
        ("soil_plot", '"'), ("water_pump", '~'), ("quarry", '#'), ("woodlot", '♣'),
        ("mill", 'M'), ("coop", 'C'), ("kiln", 'K'), ("sawmill", 'W'),
        ("bakery", 'B'), ("dairy", 'D'), ("cannery", 'N'),
        ("composter", 'o'), ("ration_line", 'R'),
        ("silo", 'I'), ("biomass_generator", 'G'), ("coal_generator", 'G'), ("greenhouse", 'H'),
    };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        NewOrLoad();
        Banner();

        if (args.Length > 0 && args[0] == "--demo") { RunDemo(); return 0; }

        while (true)
        {
            Console.Write("\n> ");
            string? line = Console.ReadLine();
            if (line is null) break; // EOF
            line = line.Trim();
            if (line.Length == 0) continue;
            if (!Handle(line)) break;
        }
        return 0;
    }

    private static bool Handle(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();
        try
        {
            switch (cmd)
            {
                case "help": case "?": Help(); break;
                case "look": case "l": Look(); break;
                case "shop": case "list": Shop(); break;
                case "build": case "b": Build(parts); break;
                case "silo": BuildSilo(parts); break;
                case "remove": case "rm": Remove(parts); break;
                case "expand": Expand(); break;
                case "sell": Sell(); break;
                case "forecast": case "f": Forecast(); break;
                case "advance": case "a": Advance(parts); break;
                case "save": Save(); break;
                case "load": NewOrLoad(true); Look(); break;
                case "new": _game = GameState.NewGame(Db, 0, 1); _now = 0; Console.WriteLine("New colony started."); Look(); break;
                case "quit": case "q": case "exit": Save(); Console.WriteLine("Saved. Bye!"); return false;
                default: Console.WriteLine($"Unknown command '{cmd}'. Type 'help'."); break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"! {e.Message}");
        }
        return true;
    }

    // ---------------------------------------------------------------- display

    private static void Banner()
    {
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║   H A R V E S T L I N E   —   playtest build   ║");
        Console.WriteLine("║   Automate. Feed the colony. Beat the clock.   ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine("Type 'help' for commands, 'look' to see your colony,");
        Console.WriteLine("'advance 24' to let 24 hours of production run.");
    }

    private static void Help()
    {
        Console.WriteLine(@"Commands:
  look                 show the grid, stores, and status
  shop                 list buildings you can construct + costs
  build <id> <x> <y>   place a building (e.g. 'build bakery 3 0')
  silo <item> <x> <y>  place a silo assigned to an item (e.g. 'silo bread 6 6')
  remove <x> <y>       demolish the building on a tile
  expand               grow the grid by one ring (costs Credits)
  sell                 sell all non-food surplus for Credits
  forecast             projected food vs next Harvest demand
  advance <hours>      let time pass; production accrues, Harvests fire
  save / load / new    manage your colony
  quit                 save and exit
Grid coords are x (column, →) and y (row, ↑), starting at 0.");
    }

    private static void Look()
    {
        var g = _game.Grid;
        int edge = g.Edge;
        Console.WriteLine();
        // Render top-down, y descending so row 0 is at the bottom.
        for (int y = edge - 1; y >= 0; y--)
        {
            var sb = new StringBuilder();
            sb.Append(y.ToString().PadLeft(2)).Append(' ');
            for (int x = 0; x < edge; x++)
            {
                var s = g.StructureAt(x, y);
                sb.Append(s == null ? " ." : $" {GlyphOf(s.Def.Id)}");
            }
            Console.WriteLine(sb.ToString());
        }
        var footer = new StringBuilder("   ");
        for (int x = 0; x < edge; x++) footer.Append((x % 10).ToString().PadLeft(2));
        Console.WriteLine(footer.ToString());

        StatusLine();
    }

    private static void StatusLine()
    {
        double demand = HarvestResolver.Demand(_game.Colony.Population);
        double food = FactorySimulator.FoodStock(_game.Grid.Inventory);
        double toHarvest = SecondsToNextHarvest() / 3600.0;
        Console.WriteLine();
        Console.WriteLine($"Day {_now / 86400.0:0.0}   Pop {_game.Colony.Population}   Credits {_game.Colony.Credits}   " +
                          $"Seals {_game.Colony.Seals}   Stores {_game.Colony.StoresTokens}/2");
        Console.WriteLine($"Next Harvest in {toHarvest:0.#}h — need {demand:0} food, have {food:0}" +
                          (food >= demand ? "  ✅" : "  ⚠️  SHORT"));
        Console.Write("Stores:");
        bool any = false;
        for (int i = 0; i < ItemTypeExtensions.Count; i++)
        {
            var item = (ItemType)i;
            double q = _game.Grid.Inventory.Get(item);
            if (q < 0.5) continue;
            Console.Write($"  {item} {q:0}/{_game.Grid.Inventory.Capacity(item):0}");
            any = true;
        }
        Console.WriteLine(any ? "" : "  (empty)");
    }

    private static void Shop()
    {
        Console.WriteLine("Buildings (id — size — cost — recipe):");
        foreach (var kv in Db.Structures)
        {
            var d = kv.Value;
            string recipe = d.Recipe == null ? d.Category.ToString() : d.Recipe.ToString();
            Console.WriteLine($"  {d.Id,-18} {d.Size}x{d.Size}  {Db.BuildCost(d.Id),4}c  {recipe}");
        }
        Console.WriteLine("Food values: Bread 1, Egg 0.5, Milk 1, Preserves 3, Rations 8.");
    }

    private static void Forecast()
    {
        var sim = new FactorySimulator(_game.Grid, _game.OutputMultiplier);
        double perHour = sim.ProjectedFoodPerHour(_game.Grid.Inventory);
        double demand = HarvestResolver.Demand(_game.Colony.Population);
        double hours = SecondsToNextHarvest() / 3600.0;
        double onHand = FactorySimulator.FoodStock(_game.Grid.Inventory);
        double cap = FoodStorageCapacity();
        double projected = Math.Min(cap, onHand + perHour * hours);
        Console.WriteLine($"Projected food output: {perHour:0}/h  (storage cap {cap:0})");
        Console.WriteLine($"At next Harvest ({hours:0.#}h): ~{projected:0} food vs demand {demand:0}");
        Console.WriteLine(projected >= demand
            ? $"  ✅ On track — surplus ~{projected - demand:0}"
            : $"  ⚠️  SHORT by ~{demand - projected:0} — build storage (silo) or more food output");
    }

    // ---------------------------------------------------------------- actions

    private static void Build(string[] p)
    {
        if (p.Length < 4) { Console.WriteLine("Usage: build <id> <x> <y>"); return; }
        if (!Db.TryGet(p[1], out var def)) { Console.WriteLine($"No such building '{p[1]}'. Try 'shop'."); return; }
        int x = int.Parse(p[2], CultureInfo.InvariantCulture), y = int.Parse(p[3], CultureInfo.InvariantCulture);
        int cost = Db.BuildCost(def.Id);
        if (_game.Colony.Credits < cost) { Console.WriteLine($"Need {cost}c, have {_game.Colony.Credits}c."); return; }
        if (!_game.Grid.IsFootprintFree(x, y, def.Size)) { Console.WriteLine("That footprint is out of bounds or occupied."); return; }
        _game.Grid.Place(def, x, y);
        _game.Colony.Credits -= cost;
        Console.WriteLine($"Built {def.Id} at ({x},{y}) for {cost}c.");
    }

    private static void BuildSilo(string[] p)
    {
        if (p.Length < 4) { Console.WriteLine("Usage: silo <item> <x> <y>  (e.g. 'silo bread 6 6')"); return; }
        if (!Enum.TryParse<ItemType>(p[1], true, out var item) || item == ItemType.None)
        { Console.WriteLine($"Unknown item '{p[1]}'."); return; }
        int x = int.Parse(p[2], CultureInfo.InvariantCulture), y = int.Parse(p[3], CultureInfo.InvariantCulture);
        int cost = Db.BuildCost("silo");
        if (_game.Colony.Credits < cost) { Console.WriteLine($"Need {cost}c, have {_game.Colony.Credits}c."); return; }
        if (!_game.Grid.IsFootprintFree(x, y, 1)) { Console.WriteLine("That tile is occupied."); return; }
        var s = _game.Grid.Place(Db.Get("silo"), x, y);
        s.AssignedItem = item;
        _game.Grid.RecomputeStorageCapacity();
        _game.Colony.Credits -= cost;
        Console.WriteLine($"Built silo for {item} at ({x},{y}) (+200 storage).");
    }

    private static void Remove(string[] p)
    {
        if (p.Length < 3) { Console.WriteLine("Usage: remove <x> <y>"); return; }
        int x = int.Parse(p[1], CultureInfo.InvariantCulture), y = int.Parse(p[2], CultureInfo.InvariantCulture);
        var s = _game.Grid.StructureAt(x, y);
        if (s == null) { Console.WriteLine("Nothing there."); return; }
        _game.Grid.Remove(s.InstanceId);
        _game.Grid.RecomputeStorageCapacity();
        Console.WriteLine($"Removed {s.Def.Id}.");
    }

    private static void Expand()
    {
        int owned = _game.Grid.TotalTiles;
        long cost = owned * 3L; // quadratic-ish with total tiles owned (spec §3)
        if (_game.Colony.Credits < cost) { Console.WriteLine($"Expansion costs {cost}c, have {_game.Colony.Credits}c."); return; }
        if (!_game.Grid.Expand()) { Console.WriteLine("Already at the maximum 24x24 grid."); return; }
        _game.Colony.Credits -= cost;
        Console.WriteLine($"Grid expanded to {_game.Grid.Edge}x{_game.Grid.Edge} for {cost}c.");
    }

    private static void Sell()
    {
        long earned = 0;
        for (int i = 0; i < ItemTypeExtensions.Count; i++)
        {
            var item = (ItemType)i;
            if (ContentDatabase.FoodValueOf(item) > 0) continue; // keep food for the Harvest
            if (!_game.Market.Commodities.ContainsKey(item)) continue;
            double q = _game.Grid.Inventory.Get(item);
            if (q < 0.5) continue;
            earned += _game.Market.Sell(item, q);
            _game.Grid.Inventory.Set(item, 0);
        }
        _game.Colony.Credits += earned;
        Console.WriteLine($"Sold surplus for {earned}c. Credits: {_game.Colony.Credits}.");
    }

    private static void Advance(string[] p)
    {
        double hours = p.Length > 1 ? double.Parse(p[1], CultureInfo.InvariantCulture) : 24;
        if (hours <= 0) return;
        long target = _now + (long)(hours * 3600);
        var sim = new FactorySimulator(_game.Grid, _game.OutputMultiplier);
        long interval = (long)HarvestResolver.HarvestIntervalSeconds;

        while (_now < target)
        {
            long nextBoundary = (_now / interval + 1) * interval;
            long segEnd = Math.Min(nextBoundary, target);
            double dt = segEnd - _now;
            var report = sim.Simulate(_game.Grid.Inventory, dt);
            _now = segEnd;

            if (segEnd == nextBoundary)
                ResolveHarvest(report);
        }
        _game.Market.AdvanceBy(hours * 3600);
        _game.LastSimulatedUtc = _now;
        Console.WriteLine($"…{hours:0.#}h passed. Now day {_now / 86400.0:0.0}.");
        Forecast();
    }

    private static void ResolveHarvest(BottleneckReport report)
    {
        var r = HarvestResolver.Resolve(_game.Colony, _game.Grid.Inventory);
        Console.WriteLine();
        Console.WriteLine("──────── HARVEST ────────");
        if (r.Met)
            Console.WriteLine($"✅ Met! Fed {r.Demand:0}. Population grows to {r.NewPopulation} (+{r.SealsAwarded} Seals).");
        else if (r.StoresTokenUsed)
            Console.WriteLine($"⚠️  Short by {r.Demand - r.FoodAvailable:0} — a Stores token saved you. Population holds at {r.NewPopulation}.");
        else
            Console.WriteLine($"❌ FAILED — only {r.FoodAvailable:0}/{r.Demand:0} food. Population falls to {r.NewPopulation}.");

        var worst = report.WorstOffender();
        if (worst != null && (worst.StarvedTime + worst.BlockedTime + worst.ThrottledTime) > 60)
            Console.WriteLine($"   Bottleneck: {worst.StructureId} was {worst.WorstReason}.");
        Console.WriteLine("─────────────────────────");

        // Auto-prestige suggestion.
        if (PrestigeCalculator.ShouldResettle(_game.Colony.Seals, _game.Colony.LifetimeSeals - _game.Colony.Seals))
            Console.WriteLine("(Tip: you have enough Seals to Resettle for a permanent boost — not yet automated in this build.)");
    }

    // ---------------------------------------------------------------- save / misc

    private static void NewOrLoad(bool verbose = false)
    {
        if (File.Exists(SavePath))
        {
            try
            {
                _game = SaveSerializer.Deserialize(File.ReadAllText(SavePath), Db);
                _now = _game.LastSimulatedUtc;
                if (verbose) Console.WriteLine($"Loaded save from {SavePath}.");
                return;
            }
            catch (Exception e) { Console.WriteLine($"(Save unreadable: {e.Message}. Starting fresh.)"); }
        }
        _game = GameState.NewGame(Db, 0, 1);
        _now = 0;
        SeedTutorialColony();
    }

    /// <summary>Give a fresh colony a small working bread line + starting purse, like the tutorial.</summary>
    private static void SeedTutorialColony()
    {
        var g = _game.Grid;
        g.Place(Db.Get("soil_plot"), 0, 0);
        g.Place(Db.Get("soil_plot"), 1, 0);
        g.Place(Db.Get("water_pump"), 2, 0);
        g.Place(Db.Get("mill"), 3, 0);
        g.Place(Db.Get("bakery"), 4, 0);
        g.Place(Db.Get("woodlot"), 0, 2);
        g.Place(Db.Get("biomass_generator"), 1, 2);
        _game.Colony.Credits = 120;
    }

    private static void Save()
    {
        _game.LastSimulatedUtc = _now;
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        File.WriteAllText(SavePath, SaveSerializer.Serialize(_game, indent: true));
    }

    private static double SecondsToNextHarvest()
    {
        long interval = (long)HarvestResolver.HarvestIntervalSeconds;
        return interval - (_now % interval);
    }

    private static double FoodStorageCapacity()
    {
        double total = 0;
        for (int i = 0; i < ItemTypeExtensions.Count; i++)
        {
            var item = (ItemType)i;
            double v = ContentDatabase.FoodValueOf(item);
            if (v > 0 && ProducesFood(item)) total += v * _game.Grid.Inventory.Capacity(item);
        }
        return total;
    }

    /// <summary>True if any placed structure outputs the given item (so its storage counts).</summary>
    private static bool ProducesFood(ItemType item)
    {
        foreach (var s in _game.Grid.Structures)
        {
            if (s.Def.Recipe == null) continue;
            foreach (var o in s.Def.Recipe.Outputs) if (o.Item == item) return true;
        }
        return false;
    }

    private static char GlyphOf(string id)
    {
        foreach (var (gid, glyph) in Glyphs) if (gid == id) return glyph;
        return '?';
    }

    /// <summary>A scripted showcase so a first run demonstrates the loop without typing.</summary>
    private static void RunDemo()
    {
        string[] script =
        {
            "look", "forecast", "silo bread 6 6", "advance 72",
            "sell", "silo bread 6 7", "advance 72", "forecast", "look",
        };
        foreach (var c in script)
        {
            Console.WriteLine($"\n> {c}");
            Handle(c);
        }
    }
}
