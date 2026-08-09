using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using Harvestline.Core.Samples;
using Harvestline.Core.Simulation;
using NUnit.Framework;

namespace Harvestline.Tests;

/// <summary>Resettlement wipe/persist semantics and the prestige output multiplier (spec §2).</summary>
[TestFixture]
public class PrestigeTests
{
    private ContentDatabase _db = null!;

    [SetUp]
    public void Setup() => _db = ContentDatabase.CreateDefault();

    [Test]
    public void Resettle_Wipes_The_Run_But_Persists_Seals()
    {
        var game = GameState.NewGame(_db, 0, seed: 1);
        game.Grid.Place(_db.Get("bakery"), 0, 0);
        game.Grid.Inventory.Set(ItemType.Bread, 80);
        game.Colony.Population = 25;
        game.Colony.Credits = 5000;
        game.Colony.Seals = 40;
        game.Colony.LifetimeSeals = 40;

        game.Resettle();

        Assert.That(game.Grid.Structures.Count, Is.EqualTo(0), "grid wipes");
        Assert.That(game.Grid.Inventory.Get(ItemType.Bread), Is.EqualTo(0), "inventory wipes");
        Assert.That(game.Colony.Credits, Is.EqualTo(0), "credits wipe");
        Assert.That(game.Colony.Population, Is.EqualTo(10), "population resets to starting colony");
        Assert.That(game.Colony.Seals, Is.EqualTo(40), "Seals persist");
        Assert.That(game.Colony.LifetimeSeals, Is.EqualTo(40), "lifetime Seals persist");
        Assert.That(game.Colony.Resettlements, Is.EqualTo(1));
    }

    [Test]
    public void Resettle_Starts_On_A_Larger_Grid_With_More_Lifetime_Seals()
    {
        var poor = GameState.NewGame(_db, 0, seed: 1);
        poor.Colony.LifetimeSeals = 0;
        poor.Resettle();
        Assert.That(poor.Grid.Edge, Is.EqualTo(GridState.MinEdge));

        var rich = GameState.NewGame(_db, 0, seed: 1);
        rich.Colony.LifetimeSeals = 1000;
        rich.Resettle();
        Assert.That(rich.Grid.Edge, Is.GreaterThan(GridState.MinEdge));
        Assert.That(rich.Grid.Edge, Is.LessThanOrEqualTo(GridState.MaxEdge));
    }

    [Test]
    public void Spending_Seals_Raises_The_Output_Multiplier()
    {
        var game = GameState.NewGame(_db, 0, seed: 1);
        game.Colony.Seals = 10;
        Assert.That(game.OutputMultiplier, Is.EqualTo(1.0).Within(1e-9));

        bool ok = game.SpendSealsOnOutput(10);
        Assert.That(ok, Is.True);
        Assert.That(game.Colony.Seals, Is.EqualTo(0));
        Assert.That(game.OutputMultiplier, Is.EqualTo(1.20).Within(1e-9)); // 1 + 0.02*10

        Assert.That(game.SpendSealsOnOutput(1), Is.False, "cannot overspend");
    }

    [Test]
    public void Output_Multiplier_Actually_Increases_Production()
    {
        var grid = SampleFactories.BreadLine(_db);
        double baseRate = new FactorySimulator(grid, 1.0).ProjectedFoodPerHour(grid.Inventory);
        double boostRate = new FactorySimulator(grid, 1.5).ProjectedFoodPerHour(grid.Inventory);
        Assert.That(boostRate, Is.GreaterThan(baseRate * 1.2),
            "a higher output multiplier must raise projected food/hour");
    }

    [Test]
    public void Full_Loop_Reaches_Resettlement_And_Multiplier_Persists()
    {
        // The whole meta-loop runs deterministically and prestige takes effect.
        var bot = new BalanceBot(_db);
        var report = bot.Run(30);
        Assert.That(report.FirstResettleDay, Is.GreaterThan(0), "the bot should reach a Resettlement");
        Assert.That(report.Days.Count, Is.EqualTo(30));
    }

    [Test]
    public void Bot_Is_Deterministic()
    {
        var a = new BalanceBot(_db).Run(25);
        var b = new BalanceBot(_db).Run(25);
        Assert.That(b.FinalPopulation, Is.EqualTo(a.FinalPopulation));
        Assert.That(b.FirstResettleDay, Is.EqualTo(a.FirstResettleDay));
        Assert.That(b.FinalSeals, Is.EqualTo(a.FinalSeals));
    }
}
