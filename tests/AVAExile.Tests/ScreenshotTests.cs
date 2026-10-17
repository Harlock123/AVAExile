using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAExile.Engine;
using AVAExile.Views;
using Controls = AVAExile.Engine.Controls;

namespace AVAExile.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set EXILE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaexile-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("EXILE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAExile-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("FIN", 48210, 3);
        g.HighScores.Insert("AVA", 21500, 1);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A planet explored by the computer until something worth showing.</summary>
    private static void Play(int level, string name, Func<GameEngine, bool> moment, int seconds = 400)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static double Nearest(GameEngine g, Func<World, IEnumerable<(double X, double Y)>> things)
    {
        var h = g.World.Hero;
        return things(g.World).Select(p => Math.Sqrt((p.X - h.X) * (p.X - h.X) + (p.Y - h.Y) * (p.Y - h.Y))).DefaultIfEmpty(1e9).Min();
    }

    [AvaloniaFact]
    public void The_surface() => Play(0, "surface", g => g.World.Clock > 1.0 && g.World.Hero.Thrusting);

    [AvaloniaFact]
    public void Down_in_the_caves() => Play(0, "caves", g => g.World.Hero.Y > 30 * Field.T && g.World.Hero.Thrusting && g.World.Clock > 15);

    [AvaloniaFact]
    public void A_pod() => Play(1, "pod", g => g.World.Hero.Carrying >= 0 && g.World.Items[g.World.Hero.Carrying].Def.Pod && g.World.Hero.Y > 20 * Field.T);

    [AvaloniaFact]
    public void A_key_and_its_door() => Play(1, "door", g => g.World.Hero.Carrying >= 0 && !g.World.Items[g.World.Hero.Carrying].Def.Pod
        && g.World.Cave.Doors[g.World.Items[g.World.Hero.Carrying].Def.Door].Cells.Any(k => Math.Abs((k.C + 0.5) * Field.T - g.World.Hero.X) < 260 && Math.Abs((k.R + 0.5) * Field.T - g.World.Hero.Y) < 200));

    [AvaloniaFact]
    public void Water() => Play(2, "water", g => g.World.Hero.Wet && g.World.Clock > 5);

    [AvaloniaFact]
    public void Creatures() => Play(3, "creatures", g => Nearest(g, w => w.Beasts.Where(b => b.Kind != Creature.Nest).Select(b => (b.X, b.Y))) < 220 && g.World.Bolts.Count > 0);

    [AvaloniaFact]
    public void Repaired() => Play(0, "repaired", g => g.Phase == Phase.Repaired && g.PhaseTime > 0.5, 500);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 3420;
        g.World.Hero.Energy = 0;
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
