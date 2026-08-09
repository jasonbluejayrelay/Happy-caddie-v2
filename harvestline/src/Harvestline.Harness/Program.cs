using System.Diagnostics;
using System.Globalization;
using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Model;
using Harvestline.Core.Samples;
using Harvestline.Core.Save;
using Harvestline.Core.Simulation;

// Harvestline M1 headless harness.
//   run   [hours]       Run the sample bread factory and print resource totals + bottlenecks.
//   gate                Run the M1 acceptance gate (1000h < 100ms, bit-identical across runs).
//   bot   [days]        Run the greedy balance bot and print the days-to-milestone table.
//   save                Round-trip a game through the save serializer and print it.

var db = ContentDatabase.CreateDefault();
string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "gate";

switch (cmd)
{
    case "run": RunFactory(args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 100); break;
    case "gate": Gate(); break;
    case "bot": Bot(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 30); break;
    case "save": SaveRoundTrip(); break;
    default: Console.WriteLine("Unknown command. Use: run | gate | bot | save"); break;
}

void RunFactory(double hours)
{
    var grid = SampleFactories.BreadLine(db);
    var sim = new FactorySimulator(grid);
    var report = sim.Simulate(grid.Inventory, hours * 3600.0);

    Console.WriteLine($"== Bread line, {hours:0.#}h ({report.EventsResolved} events{(report.HitEventCap ? ", CAPPED" : "")}) ==");
    Console.WriteLine("Inventory:");
    for (int i = 0; i < ItemTypeExtensions.Count; i++)
    {
        var item = (ItemType)i;
        double q = grid.Inventory.Get(item);
        if (q > 0) Console.WriteLine($"  {item,-11} {q,10:0.00} / {grid.Inventory.Capacity(item):0}");
    }
    Console.WriteLine($"Food value in stock: {FactorySimulator.FoodStock(grid.Inventory):0.0}");
    Console.WriteLine($"Projected food/hour: {sim.ProjectedFoodPerHour(grid.Inventory):0.0}");

    Console.WriteLine("Bottlenecks (worst first):");
    var entries = report.Entries.OrderByDescending(e => e.StarvedTime + e.BlockedTime + e.ThrottledTime);
    foreach (var e in entries)
    {
        if (e.StarvedFraction + e.BlockedFraction + e.ThrottledFraction < 0.001) continue;
        Console.WriteLine($"  {e.StructureId,-18} starved {e.StarvedFraction,5:0%}  blocked {e.BlockedFraction,5:0%}  throttled {e.ThrottledFraction,5:0%}");
    }
}

void Gate()
{
    Console.WriteLine("== M1 acceptance gate ==");

    // Determinism: two 1000h runs of the same factory must be bit-identical.
    string h1 = HashRun(1000);
    string h2 = HashRun(1000);
    bool identical = h1 == h2;
    Console.WriteLine($"Determinism (1000h ×2): {(identical ? "PASS" : "FAIL")}  [{h1}] vs [{h2}]");

    // Performance: a 1000h run must complete in < 100ms.
    var grid = SampleFactories.BreadLine(db);
    var sim = new FactorySimulator(grid);
    sim.Simulate(grid.Inventory.Clone(), 3600); // JIT warmup

    var freshGrid = SampleFactories.BreadLine(db);
    var freshSim = new FactorySimulator(freshGrid);
    var sw = Stopwatch.StartNew();
    var rep = freshSim.Simulate(freshGrid.Inventory, 1000 * 3600.0);
    sw.Stop();
    double ms = sw.Elapsed.TotalMilliseconds;
    Console.WriteLine($"Performance (1000h): {ms:0.00} ms ({rep.EventsResolved} events)  {(ms < 100 ? "PASS" : "FAIL")}");
    Console.WriteLine(identical && ms < 100 ? "GATE: PASS" : "GATE: FAIL");
}

string HashRun(double hours)
{
    var grid = SampleFactories.BreadLine(db);
    var sim = new FactorySimulator(grid);
    sim.Simulate(grid.Inventory, hours * 3600.0);
    // Hash the exact bit pattern of every stock double.
    ulong h = 1469598103934665603UL; // FNV-1a 64
    for (int i = 0; i < ItemTypeExtensions.Count; i++)
    {
        long bits = BitConverter.DoubleToInt64Bits(grid.Inventory.Get((ItemType)i));
        for (int b = 0; b < 8; b++)
        {
            h ^= (byte)(bits >> (b * 8));
            h *= 1099511628211UL;
        }
    }
    return h.ToString("x16");
}

void Bot(int days)
{
    var bot = new BalanceBot(db);
    var report = bot.Run(days);
    Console.WriteLine($"== Balance bot, {days} days ==");
    Console.WriteLine($"{"Day",3} {"Pop",5} {"Credits",8} {"Seals",6} {"Food/h",8} {"Demand",8} {"Bldgs",6}  Note");
    foreach (var d in report.Days)
        Console.WriteLine($"{d.Day,3} {d.Population,5} {d.Credits,8} {d.Seals,6} {d.FoodPerHour,8:0.0} {d.NextDemand,8:0.0} {d.Structures,6}  {d.Note}");
    Console.WriteLine($"First Harvest failure: {(report.FirstFailureDay < 0 ? "none" : "day " + report.FirstFailureDay)} (target day 5–7)");
    Console.WriteLine($"First Resettlement:    {(report.FirstResettleDay < 0 ? "none" : "day " + report.FirstResettleDay)} (target day 12–20)");
    Console.WriteLine($"Final population {report.FinalPopulation}, seals {report.FinalSeals}");
}

void SaveRoundTrip()
{
    var game = GameState.NewGame(db, 1_000_000, seed: 99);
    game.Grid.Place(db.Get("soil_plot"), 0, 0);
    game.Grid.Place(db.Get("bakery"), 2, 0);
    game.Grid.Inventory.Set(ItemType.Bread, 42.5);
    game.Colony.Population = 17;
    game.Colony.Credits = 350;
    for (int i = 0; i < 3; i++) game.Rng.NextUInt64();

    string json = SaveSerializer.Serialize(game, indent: true);
    Console.WriteLine(json);
    var restored = SaveSerializer.Deserialize(json, db);
    Console.WriteLine($"Round-trip: pop {restored.Colony.Population}, credits {restored.Colony.Credits}, " +
                      $"bread {restored.Grid.Inventory.Get(ItemType.Bread)}, rng {restored.Rng.State}");
}
