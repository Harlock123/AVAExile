using AVAExile.Engine;

namespace AVAExile.Tests;

/// <summary>The planets' caves: built to be explored, and the ship repaired by the computer explorer.</summary>
public class CaveTests(ITestOutputHelper output)
{
    public static TheoryData<int> Planets() => [.. Enumerable.Range(0, CaveBook.Count)];

    [Theory]
    [MemberData(nameof(Planets))]
    public void Every_pod_reached_once_the_doors_are_open_and_every_door_shuts_something_away(int level)
    {
        var cv = CaveBook.Build(level);
        var ship = cv.Cell(cv.ShipX + 3 * Field.T, cv.ShipY);
        var opened = new HashSet<int>(cv.Doors.Select(d => d.Index));
        var open = cv.Reach(ship.C, ship.R, opened);
        foreach (var it in cv.Items)
        {
            var cell = cv.Cell(it.X, it.Y - 14);
            Assert.True(open.ContainsKey(cell) || open.ContainsKey((cell.C, cell.R - 1)), $"item at {cell}");
        }
        // Each door has its key, and the key can be got to with the doors before it open.
        var before = new HashSet<int>();
        foreach (var door in cv.Doors)
        {
            var key = cv.Items.Single(i => !i.Pod && i.Door == door.Index);
            var reach = cv.Reach(ship.C, ship.R, before);
            var cell = cv.Cell(key.X, key.Y - 14);
            Assert.True(reach.ContainsKey(cell) || reach.ContainsKey((cell.C, cell.R - 1)));
            before.Add(door.Index);
        }
        Assert.Equal(5 + Math.Min(level, 3), cv.Pods);
        Assert.Equal(1 + Math.Min(2, level), cv.Doors.Count);
    }

    [Fact]
    public void Each_planet_has_more_to_find_and_more_in_the_way()
    {
        for (int i = 1; i < CaveBook.Count; i++)
        {
            var a = CaveBook.Build(i - 1);
            var b = CaveBook.Build(i);
            Assert.True(b.Pods >= a.Pods);
            Assert.True(b.Doors.Count >= a.Doors.Count);
        }
        var cv = CaveBook.Build(2);
        Assert.NotEmpty(cv.Winds);
        Assert.Contains(Enumerable.Range(0, Field.Cols * Field.Rows), i => cv.Water[i % Field.Cols, i / Field.Cols]);
        Assert.True(cv.Creatures.Select(c => c.Kind).Distinct().Count() >= 3);
    }

    [Theory]
    [MemberData(nameof(Planets))]
    public void Every_ship_is_repaired_by_the_computer_explorer(int level)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaexile-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(level);
        g.SetLives(10);
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 700 && g.State == GameState.Playing && g.Phase != Phase.Repaired; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{g.World.Cave.Name}: {g.Phase} after {t / 60}s, {lost} lives lost");
        Assert.Equal(Phase.Repaired, g.Phase);
        Assert.True(lost <= 2, $"{lost} lives lost");
    }

    [Fact]
    public void Flying_about_aimlessly_brings_nothing_back()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaexile-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(0);
        for (int t = 0; t < 60 * 300 && g.State == GameState.Playing; t++) g.Tick(new Controls(1, t % 120 < 60 ? -1 : 0, true));
        Assert.Equal(0, g.World.Delivered);
    }
}
