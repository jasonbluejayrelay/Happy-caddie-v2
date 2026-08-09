using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Economy;
using Harvestline.Core.Model;
using Harvestline.Core.Rng;
using Harvestline.Core.Save;
using NUnit.Framework;

namespace Harvestline.Tests;

/// <summary>Market walk + player pressure (spec §6), RNG determinism, and save round-trip (spec §8).</summary>
[TestFixture]
public class EconomyAndSaveTests
{
    [Test]
    public void Rng_Is_Deterministic_For_A_Seed()
    {
        var a = new DeterministicRng(42);
        var b = new DeterministicRng(42);
        for (int i = 0; i < 100; i++)
            Assert.That(b.NextUInt64(), Is.EqualTo(a.NextUInt64()));
    }

    [Test]
    public void Rng_State_Round_Trips_The_Stream()
    {
        var rng = new DeterministicRng(7);
        for (int i = 0; i < 10; i++) rng.NextUInt64();
        ulong saved = rng.State;

        var expected = new ulong[5];
        for (int i = 0; i < 5; i++) expected[i] = rng.NextUInt64();

        var restored = new DeterministicRng(0) { State = saved };
        for (int i = 0; i < 5; i++)
            Assert.That(restored.NextUInt64(), Is.EqualTo(expected[i]));
    }

    [Test]
    public void Market_Mean_Reverts_Toward_Base_Price()
    {
        var rng = new DeterministicRng(1);
        var market = new MarketSimulator(rng);
        var c = market.Register(ItemType.Flour, basePrice: 5.0, marketDepth: 250);
        c.Price = 20.0; // start far above base

        double avg = 0;
        for (int i = 0; i < 200; i++) { market.Tick(); avg += c.Price; }
        avg /= 200;

        Assert.That(avg, Is.LessThan(12.0), "price should revert toward the 5.0 base over time");
        Assert.That(avg, Is.GreaterThan(1.0));
    }

    [Test]
    public void Selling_A_Large_Lot_Nets_Less_Per_Unit_Than_Selling_Small()
    {
        // Player pressure: dumping depresses the effective price (spec §6).
        var big = MakeMarket();
        long bulk = big.Sell(ItemType.Flour, 500);

        var small = MakeMarket();
        long piece = small.Sell(ItemType.Flour, 100);

        double bulkPerUnit = bulk / 500.0;
        double piecePerUnit = piece / 100.0;
        Assert.That(bulkPerUnit, Is.LessThan(piecePerUnit));

        static MarketSimulator MakeMarket()
        {
            var m = new MarketSimulator(new DeterministicRng(1));
            m.Register(ItemType.Flour, 5.0, 250);
            return m;
        }
    }

    [Test]
    public void Economy_Is_Stable_Over_30_Days()
    {
        // M5 gate (spec §10): a headless 30-day economy shows no runaway inflation or
        // price collapse under steady, depth-proportional player selling.
        var report = Harvestline.Core.Samples.EconomySimulator.Run(30);
        Assert.That(report.IsStable(), Is.True);
        foreach (var s in report.Stats)
        {
            Assert.That(s.MinRatio, Is.GreaterThan(0.15), $"{s.Item} collapsed");
            Assert.That(s.MaxRatio, Is.LessThan(4.0), $"{s.Item} inflated away");
        }
    }

    [Test]
    public void Economy_Is_Deterministic()
    {
        var a = Harvestline.Core.Samples.EconomySimulator.Run(30, seed: 7);
        var b = Harvestline.Core.Samples.EconomySimulator.Run(30, seed: 7);
        for (int i = 0; i < a.Stats.Count; i++)
            Assert.That(b.Stats[i].AvgRatio, Is.EqualTo(a.Stats[i].AvgRatio));
    }

    [Test]
    public void Save_Round_Trips_All_State()
    {
        var db = ContentDatabase.CreateDefault();
        var game = GameState.NewGame(db, nowUtc: 1_000_000, seed: 99);
        game.Grid.Place(db.Get("soil_plot"), 0, 0);
        game.Grid.Place(db.Get("bakery"), 2, 0);
        var silo = game.Grid.Place(db.Get("silo"), 5, 5);
        silo.AssignedItem = ItemType.Bread;
        game.Grid.RecomputeStorageCapacity();
        game.Grid.Inventory.Set(ItemType.Bread, 42.5);
        game.Colony.Population = 17;
        game.Colony.Credits = 350;
        game.Colony.Seals = 9;
        game.LastSimulatedUtc = 1_050_000;
        for (int i = 0; i < 5; i++) game.Rng.NextUInt64();

        string json = SaveSerializer.Serialize(game);
        var r = SaveSerializer.Deserialize(json, db);

        Assert.That(r.Colony.Population, Is.EqualTo(17));
        Assert.That(r.Colony.Credits, Is.EqualTo(350));
        Assert.That(r.Colony.Seals, Is.EqualTo(9));
        Assert.That(r.Grid.Inventory.Get(ItemType.Bread), Is.EqualTo(42.5).Within(1e-9));
        Assert.That(r.Grid.Inventory.Capacity(ItemType.Bread), Is.EqualTo(300).Within(1e-9)); // 100 + 200 silo
        Assert.That(r.Rng.State, Is.EqualTo(game.Rng.State));
        Assert.That(r.LastSimulatedUtc, Is.EqualTo(1_050_000));
        Assert.That(r.Grid.Structures.Count, Is.EqualTo(3));
    }

    [Test]
    public void Clock_Rollback_Is_Rejected()
    {
        var db = ContentDatabase.CreateDefault();
        var game = GameState.NewGame(db, nowUtc: 1_000_000, seed: 1);
        game.LastSimulatedUtc = 1_000_000;

        double elapsed = SaveSerializer.ElapsedSinceLastSim(game, nowUtc: 900_000, out bool rolledBack);
        Assert.That(rolledBack, Is.True);
        Assert.That(elapsed, Is.EqualTo(0));

        double ok = SaveSerializer.ElapsedSinceLastSim(game, nowUtc: 1_003_600, out bool rb2);
        Assert.That(rb2, Is.False);
        Assert.That(ok, Is.EqualTo(3600));
    }

    [Test]
    public void Json_Round_Trips_Nested_Structures()
    {
        var obj = JsonValue.Object();
        obj.Set("n", JsonValue.Of(-3.5));
        obj.Set("s", JsonValue.Of("he said \"hi\"\n"));
        obj.Set("b", JsonValue.Of(true));
        var arr = JsonValue.Array();
        arr.Add(JsonValue.Of(1L)).Add(JsonValue.Of(2L));
        obj.Set("a", arr);

        string s = obj.ToJsonString();
        var parsed = (JsonObject)JsonValue.Parse(s);

        Assert.That(parsed.GetDouble("n"), Is.EqualTo(-3.5));
        Assert.That(parsed.GetString("s"), Is.EqualTo("he said \"hi\"\n"));
        Assert.That(parsed.GetBool("b"), Is.True);
        Assert.That(parsed.GetArray("a").Count, Is.EqualTo(2));
    }
}
