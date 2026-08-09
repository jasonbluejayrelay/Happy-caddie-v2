using Harvestline.Core.Content;
using Harvestline.Core.Model;
using Harvestline.Core.Samples;
using Harvestline.Core.Simulation;
using NUnit.Framework;

namespace Harvestline.Tests;

/// <summary>
/// The M1 gate's core guarantees (spec §7, §10): the simulation is deterministic and
/// the single code path gives identical results whether called once over a long
/// horizon (offline accrual) or many times with small dt (online play).
/// </summary>
[TestFixture]
public class DeterminismTests
{
    private ContentDatabase _db = null!;

    [SetUp]
    public void Setup() => _db = ContentDatabase.CreateDefault();

    private static ulong HashInventory(Inventory inv)
    {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < ItemTypeExtensions.Count; i++)
        {
            long bits = System.BitConverter.DoubleToInt64Bits(inv.Get((ItemType)i));
            for (int b = 0; b < 8; b++) { h ^= (byte)(bits >> (b * 8)); h *= 1099511628211UL; }
        }
        return h;
    }

    [Test]
    public void Repeated_Runs_Are_Bit_Identical()
    {
        var g1 = SampleFactories.BreadLine(_db);
        new FactorySimulator(g1).Simulate(g1.Inventory, 1000 * 3600.0);

        var g2 = SampleFactories.BreadLine(_db);
        new FactorySimulator(g2).Simulate(g2.Inventory, 1000 * 3600.0);

        Assert.That(HashInventory(g2.Inventory), Is.EqualTo(HashInventory(g1.Inventory)));
    }

    [Test]
    public void Offline_Equals_Online_Within_Float_Tolerance()
    {
        // One long call (offline accrual) vs many small dt calls (online ticking) must
        // agree — the M3 gate, provable at M1 because it's the same code path (spec §7).
        double horizon = 6 * 3600.0;

        var offline = SampleFactories.BreadLine(_db);
        new FactorySimulator(offline).Simulate(offline.Inventory, horizon);

        var online = SampleFactories.BreadLine(_db);
        var sim = new FactorySimulator(online);
        double dt = 1.0; // one-second online ticks
        for (double t = 0; t < horizon; t += dt)
            sim.Simulate(online.Inventory, dt);

        for (int i = 0; i < ItemTypeExtensions.Count; i++)
        {
            var item = (ItemType)i;
            Assert.That(online.Inventory.Get(item), Is.EqualTo(offline.Inventory.Get(item)).Within(1e-3),
                $"item {item} diverged between offline and online");
        }
    }

    [Test]
    public void Long_Horizon_Resolves_In_Few_Events_Not_Millions_Of_Ticks()
    {
        var grid = SampleFactories.BreadLine(_db);
        var report = new FactorySimulator(grid).Simulate(grid.Inventory, 1000 * 3600.0);
        Assert.That(report.EventsResolved, Is.LessThan(50),
            "piecewise-linear integration must resolve a long horizon in a handful of events");
        Assert.That(report.HitEventCap, Is.False);
    }

    [Test]
    public void Offline_Accrual_Is_Capped_At_24_Hours()
    {
        var a = SampleFactories.BreadLine(_db);
        new FactorySimulator(a).SimulateOffline(a.Inventory, 48 * 3600.0);

        var b = SampleFactories.BreadLine(_db);
        new FactorySimulator(b).SimulateOffline(b.Inventory, 24 * 3600.0);

        Assert.That(HashInventory(a.Inventory), Is.EqualTo(HashInventory(b.Inventory)),
            "48h offline should be clamped to 24h of production");
    }
}
