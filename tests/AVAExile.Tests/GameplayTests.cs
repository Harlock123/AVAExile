using Avalonia.Input;
using AVAExile.Engine;

namespace AVAExile.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaexile-tests-" + Guid.NewGuid().ToString("N"));
    private const double T = Field.T;

    /// <summary>A plain cave: one big open chamber, a floor along its bottom, nothing in it unless a test puts it there.</summary>
    private static World Chamber(Action<CaveDef>? build = null)
    {
        var cv = new CaveDef { Name = "TEST", ShipX = 10 * T, ShipY = (Field.Surface - 1.6) * T };
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            cv.Tiles[c, r] = r < Field.Surface ? Tile.Sky : c > 2 && c < 60 && r > 10 && r < 40 ? Tile.Open : Tile.Rock;
        build?.Invoke(cv);
        var w = new World();
        Sim.Start(w, cv, 1);
        Put(w, 20 * T, 30 * T);
        return w;
    }

    private static void Put(World w, double x, double y)
    {
        w.Hero.X = x;
        w.Hero.Y = y;
        w.Hero.Vx = w.Hero.Vy = 0;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── the jetpack ─────────────────────────────

    [Fact]
    public void Without_the_jetpack_he_falls_and_lands_on_the_floor()
    {
        var w = Chamber();
        Run(w, 120);
        Assert.True(w.Hero.Grounded);
        Assert.Equal(40 * T - Hero.HalfH, w.Hero.Y, 0);
    }

    [Fact]
    public void The_jetpack_lifts_him_and_he_keeps_his_momentum()
    {
        var w = Chamber();
        Run(w, 40, new Controls(0, -1));
        Assert.True(w.Hero.Y < 30 * T - 40);
        Assert.True(w.Hero.Vy < -100);
        double vy = w.Hero.Vy, y = w.Hero.Y;
        Run(w, 5);
        Assert.True(w.Hero.Y < y, "still rising a moment after letting go");
        Assert.True(w.Hero.Vy > vy);
    }

    [Fact]
    public void Left_and_right_push_him_and_turn_him_and_drag_slows_him()
    {
        var w = Chamber();
        Run(w, 30, new Controls(1, -1));
        Assert.True(w.Hero.Vx > 150);
        Assert.Equal(1, w.Hero.Facing);
        Run(w, 5, new Controls(-1, -1));
        Assert.Equal(-1, w.Hero.Facing);
        Put(w, 20 * T, 30 * T);
        w.Hero.Vx = 300;
        Run(w, 120, new Controls(0, -1));
        Assert.True(Math.Abs(w.Hero.Vx) < 30);
    }

    [Fact]
    public void Rock_stops_him_and_a_hard_knock_hurts()
    {
        var w = Chamber();
        Put(w, 6 * T, 30 * T);
        w.Hero.Vx = -360;
        Run(w, 30, new Controls(-1, -1));
        Assert.InRange(w.Hero.X, 3 * T + Hero.HalfW, 3 * T + Hero.HalfW + 6);
        Assert.True(w.Hero.Energy < Hero.Energy0);
        Assert.Equal("DASHED ON THE ROCKS", w.Cause);
    }

    [Fact]
    public void Water_slows_everything_and_a_draught_blows_him_along()
    {
        var wet = Chamber(cv => { for (int c = 3; c < 60; c++) for (int r = 30; r < 40; r++) cv.Water[c, r] = true; });
        Put(wet, 20 * T, 32 * T);
        Run(wet, 30);
        Assert.True(wet.Hero.Wet);
        Assert.InRange(wet.Hero.Vy, 0, 60);

        var windy = Chamber(cv => cv.Winds.Add(new WindDef(10 * T, 20 * T, 30 * T, 35 * T, 380, 0)));
        Run(windy, 30, new Controls(0, -1));
        Assert.True(windy.Hero.Vx > 100);
    }

    // ───────────────────────────── carrying ─────────────────────────────

    [Fact]
    public void He_picks_a_pod_up_carries_it_and_puts_it_down()
    {
        var w = Chamber(cv => cv.Items.Add(new ItemDef(true, -1, 20 * T, 40 * T - 12)));
        Run(w, 90);
        Sim.Step(w, new Controls(Grab: true));
        Assert.Equal(0, w.Hero.Carrying);
        Run(w, 40, new Controls(1, -1));
        Assert.Equal(w.Hero.X + 14, w.Items[0].X, 1);
        Sim.Step(w, new Controls(Grab: true));
        Assert.Equal(-1, w.Hero.Carrying);
        Run(w, 120);
        Assert.Equal(40 * T - 12, w.Items[0].Y, 0);
    }

    [Fact]
    public void A_pod_is_heavy_for_the_jetpack()
    {
        var light = Chamber();
        var heavy = Chamber(cv => cv.Items.Add(new ItemDef(true, -1, 20 * T, 30 * T)));
        heavy.Hero.Carrying = 0;
        Run(light, 30, new Controls(0, -1));
        Run(heavy, 30, new Controls(0, -1));
        Assert.True(heavy.Hero.Y > light.Hero.Y);
    }

    [Fact]
    public void A_key_opens_its_door_and_is_used_up()
    {
        var w = Chamber(cv =>
        {
            var cells = Enumerable.Range(11, 29).Select(r => (30, r)).ToList();
            foreach (var (c, r) in cells) cv.Tiles[c, r] = Tile.Door;
            cv.Doors.Add(new DoorDef(0, cells));
            cv.Items.Add(new ItemDef(false, 0, 20 * T, 40 * T - 12));
        });
        Run(w, 90);
        Sim.Step(w, new Controls(Grab: true));
        Assert.True(Sim.Solid(w, 30.5 * T, 30 * T));
        Run(w, 120, new Controls(1, -1));
        Assert.Contains(0, w.Opened);
        Assert.False(Sim.Solid(w, 30.5 * T, 30 * T));
        Assert.True(w.Items[0].Done);
        Assert.Equal(-1, w.Hero.Carrying);
        Assert.Equal(Sim.DoorPoints, w.Score);
    }

    [Fact]
    public void A_pod_carried_to_the_ship_goes_in()
    {
        var w = Chamber(cv => cv.Items.Add(new ItemDef(true, -1, 0, 0)));
        w.Hero.Carrying = 0;
        Put(w, w.Cave.ShipX + 40, w.Cave.ShipY);
        Run(w, 1);
        Assert.Equal(1, w.Delivered);
        Assert.True(w.Items[0].Done);
        Assert.True(w.Repaired);
        Assert.Equal(Sim.PodPoints, w.Score);
    }

    // ───────────────────────────── the blaster, and what lives in the caves ─────────────────────────────

    [Fact]
    public void The_blaster_fires_the_way_he_faces_and_kills_a_bird()
    {
        var w = Chamber(cv => cv.Creatures.Add(new CreatureDef(Creature.Bird, 28 * T, 30 * T)));
        w.Beasts[0].Vx = 0;
        Sim.Step(w, new Controls(1, -1, true));
        Assert.Single(w.Bolts);
        Assert.True(w.Bolts[0].Vx > 0);
        for (int i = 0; i < 40 && w.Beasts.Count > 0; i++) Sim.Step(w, new Controls(0, -1, true));
        Assert.Empty(w.Beasts);
        Assert.Equal(50, w.Score);
    }

    [Fact]
    public void A_turret_fires_at_him_when_it_can_see_him()
    {
        var w = Chamber(cv => cv.Creatures.Add(new CreatureDef(Creature.Turret, 26 * T, 40 * T - 12)));
        w.Beasts[0].Cool = 0;
        Run(w, 240);
        Assert.True(w.Hero.Energy < Hero.Energy0);
        Assert.Equal("SHOT BY A TURRET", w.Cause);
    }

    [Fact]
    public void A_nest_sends_out_wasps_that_sting()
    {
        var w = Chamber(cv => cv.Creatures.Add(new CreatureDef(Creature.Nest, 24 * T, 11 * T + 14)));
        w.Beasts[0].Cool = 0;
        Put(w, 22 * T, 16 * T);
        Run(w, 2);
        Assert.Contains(w.Beasts, b => b.Kind == Creature.Wasp);
        Run(w, 60 * 6);
        Assert.True(w.Hero.Energy < Hero.Energy0);
    }

    [Fact]
    public void Shields_run_out_and_he_is_dead_and_drops_what_he_carried()
    {
        var w = Chamber(cv => cv.Items.Add(new ItemDef(true, -1, 20 * T, 30 * T)));
        w.Hero.Carrying = 0;
        Sim.Hurt(w, 200, "TEST");
        Run(w, 1);
        Assert.True(w.Hero.Dead);
        Assert.Equal(-1, w.Hero.Carrying);
        Assert.False(w.Items[0].Done);
    }

    [Fact]
    public void Shields_build_back_up()
    {
        var w = Chamber();
        w.Hero.Energy = 50;
        Run(w, 600);
        Assert.Equal(50 + Sim.Recharge * 10, w.Hero.Energy, 1);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void Every_pod_in_and_the_ship_flies_on_to_the_next_planet()
    {
        var g = Engine();
        g.Go(0);
        g.World.Delivered = g.World.Cave.Pods;
        g.Run(1);
        Assert.Equal(Phase.Repaired, g.Phase);
        Assert.Equal(GameEngine.PlanetBonus, g.Score);
        g.Run((int)(60 * GameEngine.RepairedSeconds) + 2);
        Assert.Equal(1, g.Level);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
    }

    [Fact]
    public void Killed_he_is_back_beside_the_ship()
    {
        var g = Engine();
        g.Go(0);
        g.World.Hero.X = 50 * T;
        g.World.Hero.Y = 40 * T;
        g.World.Hero.Energy = 0;
        g.Run(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.InRange(g.World.Hero.X, g.World.Cave.ShipX, g.World.Cave.ShipX + 200);
        Assert.False(g.World.Hero.Dead);
    }

    [Fact]
    public void The_last_life_lost_is_game_over_then_the_high_scores()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 25000;
        g.World.Hero.Energy = 0;
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run(60 * 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "FIN") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("FIN", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        var at = (g.World.Hero.X, g.World.Hero.Y);
        g.Idle(60 * 6);
        Assert.NotEqual(at, (g.World.Hero.X, g.World.Hero.Y));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go(0);
        g.Run(30, new Controls(0, -1));
        g.Pause();
        var at = (g.World.Hero.X, g.World.Hero.Y);
        g.Run(120, new Controls(0, -1));
        Assert.Equal(at, (g.World.Hero.X, g.World.Hero.Y));
        g.Resume();
        g.Run(10, new Controls(0, -1));
        Assert.NotEqual(at, (g.World.Hero.X, g.World.Hero.Y));
    }

    [Fact]
    public void The_keys_fly_fire_and_grab()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Grab);
        input.OnKeyUp(Key.X);

        // Up through to the explorer: the jetpack lifts him.
        var w = Chamber();
        input.OnKeyDown(Key.Up);
        Run(w, 30, input.Player);
        Assert.True(w.Hero.Y < 30 * T);
    }
}
