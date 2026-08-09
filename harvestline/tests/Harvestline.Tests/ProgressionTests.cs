using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using NUnit.Framework;

namespace Harvestline.Tests;

/// <summary>Harvest demand curve, growth/shrink, Seals, Stores grace buffer, prestige (spec §5, §2).</summary>
[TestFixture]
public class ProgressionTests
{
    [Test]
    public void Demand_Follows_The_Spec_Curve()
    {
        // demand = 10 * pop^1.15
        Assert.That(HarvestResolver.Demand(10), Is.EqualTo(10 * System.Math.Pow(10, 1.15)).Within(1e-9));
        Assert.That(HarvestResolver.Demand(100), Is.GreaterThan(HarvestResolver.Demand(10) * 10),
            "the curve is super-linear, so the deadline outruns linear production");
    }

    [Test]
    public void Met_Harvest_Grows_Population_And_Awards_Seals()
    {
        var colony = new ColonyState { Population = 10 };
        var inv = new Inventory();
        inv.Set(ItemType.Rations, 100); // 100 * 8 = 800 food value, demand ~141

        var result = HarvestResolver.Resolve(colony, inv);

        Assert.That(result.Met, Is.True);
        Assert.That(colony.Population, Is.EqualTo(11)); // floor(10 * 1.12) = 11
        Assert.That(result.SealsAwarded, Is.GreaterThan(0));
        Assert.That(colony.StoresTokens, Is.EqualTo(1), "a met Harvest banks a Stores token");
    }

    [Test]
    public void Failed_Harvest_Without_Token_Shrinks_Population()
    {
        var colony = new ColonyState { Population = 20, StoresTokens = 0 };
        var inv = new Inventory(); // no food

        var result = HarvestResolver.Resolve(colony, inv);

        Assert.That(result.Met, Is.False);
        Assert.That(result.StoresTokenUsed, Is.False);
        Assert.That(colony.Population, Is.LessThan(20));
        Assert.That(colony.Population, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Failed_Harvest_Is_Absorbed_By_A_Stores_Token()
    {
        var colony = new ColonyState { Population = 20, StoresTokens = 1 };
        var inv = new Inventory();

        var result = HarvestResolver.Resolve(colony, inv);

        Assert.That(result.Met, Is.False);
        Assert.That(result.StoresTokenUsed, Is.True);
        Assert.That(colony.Population, Is.EqualTo(20), "the grace token negates the loss");
        Assert.That(colony.StoresTokens, Is.EqualTo(0));
    }

    [Test]
    public void Stores_Tokens_Cap_At_Two()
    {
        var colony = new ColonyState { Population = 10, StoresTokens = 2 };
        var inv = new Inventory();
        inv.Set(ItemType.Rations, 100);
        HarvestResolver.Resolve(colony, inv);
        Assert.That(colony.StoresTokens, Is.EqualTo(2));
    }

    [Test]
    public void Met_Harvest_Consumes_Only_The_Demand()
    {
        var colony = new ColonyState { Population = 10 };
        var inv = new Inventory();
        inv.Set(ItemType.Rations, 100); // 800 food value
        double demand = HarvestResolver.Demand(10);

        HarvestResolver.Resolve(colony, inv);

        double remainingFood = 100 * 8 - demand;
        double actual = inv.Get(ItemType.Rations) * 8;
        Assert.That(actual, Is.EqualTo(remainingFood).Within(1e-6));
    }

    [Test]
    public void Prestige_Starting_Edge_Scales_With_Lifetime_Seals_And_Clamps()
    {
        Assert.That(PrestigeCalculator.StartingEdge(0), Is.EqualTo(GridState.MinEdge));
        Assert.That(PrestigeCalculator.StartingEdge(1_000_000), Is.LessThanOrEqualTo(GridState.MaxEdge));
        Assert.That(PrestigeCalculator.StartingEdge(1000), Is.GreaterThan(GridState.MinEdge));
    }

    [Test]
    public void Seals_Formula_Matches_Spec()
    {
        // seals = floor(sqrt(surplus / 50))
        Assert.That(HarvestResolver.SealsForSurplus(50), Is.EqualTo(1));
        Assert.That(HarvestResolver.SealsForSurplus(200), Is.EqualTo(2));
        Assert.That(HarvestResolver.SealsForSurplus(0), Is.EqualTo(0));
    }
}
