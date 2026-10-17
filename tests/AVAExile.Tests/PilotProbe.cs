using AVAExile.Engine;

namespace AVAExile.Tests;

/// <summary>How the computer explorer gets on on each planet (set EXILE_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_explorer_does()
    {
        if (Environment.GetEnvironmentVariable("EXILE_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < CaveBook.Count; i++)
        {
            var b = System.Diagnostics.Stopwatch.StartNew();
            var cave = CaveBook.Build(i);
            output.WriteLine($"caves {i} built in {b.ElapsedMilliseconds} ms: {cave.Pods} pods, {cave.Doors.Count} doors, {cave.Creatures.Count} creatures");
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "exile-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int t = 0;
            for (; t < 60 * 900 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? new Controls(1, t % 120 < 60 ? -1 : 0, true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[g.World.Cause] = deaths.GetValueOrDefault(g.World.Cause) + 1;
                last = g.Phase;
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            }
            var w = g.World;
            output.WriteLine($"{i + 1} {cave.Name}: {(g.Cleared > 0 ? "REPAIRED" : "not repaired")} after {t / 60}s, {deaths.Values.Sum()} deaths ({string.Join(", ", deaths.Select(d => $"{d.Key} {d.Value}"))}), pods {w.Delivered}/{w.Cave.Pods}, doors {w.Opened.Count}; {string.Join(", ", doing.OrderByDescending(d => d.Value).Take(5).Select(d => $"{d.Key} {d.Value / 60}s"))}");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
