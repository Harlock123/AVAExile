namespace AVAExile.Engine;

/// <summary>
/// The caves, step by step: the explorer's jetpack against gravity and momentum (and the draughts, and the
/// water), the blaster, picking things up and putting them down, keys opening doors and pods put into the ship;
/// and the birds, wasps, imps and gun turrets of the caves.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, T = Field.T;
    public const double BoltSpeed = 760, BoltLife = 0.7, FireEvery = 0.22, Recharge = 3, HurtTime = 0.6;
    public const double GrabRange = 34, DoorRange = 90;
    public const int PodPoints = 1000, DoorPoints = 250;

    public static void Start(World w, CaveDef c, int seed)
    {
        w.Cave = c;
        w.Dice = new Dice(seed);
        w.Clock = 0;
        w.Delivered = 0;
        w.Opened.Clear();
        w.Items = c.Items.Select(i => new Item { Def = i, X = i.X, Y = i.Y }).ToList();
        w.Beasts = c.Creatures.Select(b => new Beast
        {
            Kind = b.Kind, X = b.X, Y = b.Y, HomeX = b.X, HomeY = b.Y, Cool = 1 + w.Dice.Next() * 2,
            Hp = b.Kind switch { Creature.Imp => 2, Creature.Turret => 3, Creature.Nest => 4, _ => 1 },
        }).ToList();
        w.Bolts.Clear();
        w.Booms.Clear();
        w.Cause = "";
        Respawn(w);
    }

    /// <summary>A fresh explorer, standing by the ship.</summary>
    public static void Respawn(World w)
    {
        var c = w.Cave;
        w.Hero = new Hero { X = c.ShipX + 110, Y = Field.Surface * T - Hero.HalfH - 0.5 };
        w.Bolts.RemoveAll(b => !b.Mine);
    }

    public static bool Solid(World w, double x, double y) => w.Cave.AtPoint(x, y) is Tile.Rock or Tile.Door || y >= Field.Rows * T || x < 0 || x >= Field.Cols * T;

    private static bool Hits(World w, double x, double y, double hw, double hh)
    {
        for (double px = x - hw; px <= x + hw + 0.01; px += Math.Min(hw, T / 2))
        for (double py = y - hh; py <= y + hh + 0.01; py += Math.Min(hh, T / 2))
            if (Solid(w, Math.Min(px, x + hw), Math.Min(py, y + hh))) return true;
        return false;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var b in w.Booms) b.T += Dt;
        w.Booms.RemoveAll(b => b.T > 0.8);
        var h = w.Hero;
        if (!h.Dead) Fly(w, h, c);
        Items(w);
        Beasts(w);
        Bolts(w);
        if (!h.Dead && h.Energy <= 0)
        {
            h.Energy = 0;
            h.Dead = true;
            if (h.Carrying >= 0) { w.Items[h.Carrying].Vy = 0; h.Carrying = -1; }
            w.Booms.Add(new Boom(h.X, h.Y, true));
            w.Heard.Add(Sfx.Die);
        }
    }

    private static void Fly(World w, Hero h, Controls c)
    {
        var cv = w.Cave;
        h.Wet = cv.WetAt(h.X, h.Y);
        h.FireTimer -= Dt;
        h.Hurt -= Dt;
        if (h.Energy > 0) h.Energy = Math.Min(Hero.Energy0, h.Energy + Recharge * Dt);
        bool heavy = h.Carrying >= 0 && w.Items[h.Carrying].Def.Pod;
        double g = Hero.Gravity, lift = Hero.Lift * (heavy ? 0.85 : 1), push = Hero.Push, drag = Hero.Drag;
        if (h.Wet) { g *= 0.3; lift *= 0.45; push *= 0.5; drag = 3.5; }
        double ax = c.Dx * push, ay = g - (c.Dy < 0 ? lift : 0) + (c.Dy > 0 ? push * 0.6 : 0);
        // (Draughts blow through the air, not the water.)
        if (!h.Wet)
            foreach (var wd in cv.Winds)
                if (h.X > wd.X0 && h.X < wd.X1 && h.Y > wd.Y0 && h.Y < wd.Y1) { ax += wd.Fx; ay += wd.Fy; }
        if (c.Dx != 0) h.Facing = c.Dx;
        h.Thrusting = c.Dy < 0;
        h.Vx += (ax - h.Vx * drag) * Dt;
        h.Vy += (ay - h.Vy * drag) * Dt;
        double sp = Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy);
        if (sp > Hero.MaxSpeed) { h.Vx *= Hero.MaxSpeed / sp; h.Vy *= Hero.MaxSpeed / sp; }
        // Along, then up or down, stopping at rock (a hard knock hurts).
        double nx = h.X + h.Vx * Dt;
        if (Hits(w, nx, h.Y, Hero.HalfW, Hero.HalfH))
        {
            if (Math.Abs(h.Vx) > 330) Hurt(w, 5, "DASHED ON THE ROCKS");
            h.Vx = 0;
        }
        else h.X = nx;
        double ny = h.Y + h.Vy * Dt;
        h.Grounded = false;
        if (Hits(w, h.X, ny, Hero.HalfW, Hero.HalfH))
        {
            if (Math.Abs(h.Vy) > 330) Hurt(w, 5, "DASHED ON THE ROCKS");
            if (h.Vy > 0) { h.Grounded = true; h.Vx *= 1 - Math.Min(1, 6 * Dt); }
            h.Vy = 0;
        }
        else h.Y = Math.Max(Hero.HalfH, ny);
        if (c.Fire && h.FireTimer <= 0)
        {
            h.FireTimer = FireEvery;
            w.Bolts.Add(new Bolt { X = h.X + h.Facing * 14, Y = h.Y - 4, Vx = h.Facing * BoltSpeed + h.Vx * 0.5, Vy = h.Vy * 0.3, Life = BoltLife, Mine = true });
            w.Heard.Add(Sfx.Fire);
        }
        if (c.Grab && !h.GrabPrev) Grab(w, h);
        h.GrabPrev = c.Grab;
        if (h.Carrying >= 0)
        {
            var it = w.Items[h.Carrying];
            it.X = h.X + h.Facing * 14;
            it.Y = h.Y + 6;
            // A key at its door opens it; a pod at the ship goes in.
            if (!it.Def.Pod)
            {
                var door = cv.Doors[it.Def.Door];
                if (door.Cells.Any(k => Math.Abs((k.C + 0.5) * T - h.X) < DoorRange && Math.Abs((k.R + 0.5) * T - h.Y) < DoorRange))
                {
                    foreach (var (dc, dr) in door.Cells) cv.Tiles[dc, dr] = Tile.Open;
                    w.Opened.Add(door.Index);
                    it.Done = true;
                    h.Carrying = -1;
                    w.Score += DoorPoints;
                    w.Heard.Add(Sfx.Door);
                }
            }
            else if (Math.Abs(h.X - cv.ShipX) < 80 && Math.Abs(h.Y - cv.ShipY) < 70)
            {
                it.Done = true;
                h.Carrying = -1;
                w.Delivered++;
                w.Score += PodPoints;
                w.Heard.Add(w.Repaired ? Sfx.Repaired : Sfx.Pod);
            }
        }
    }

    private static void Grab(World w, Hero h)
    {
        if (h.Carrying >= 0)
        {
            var it = w.Items[h.Carrying];
            it.Vy = h.Vy * 0.5;
            h.Carrying = -1;
            w.Heard.Add(Sfx.Drop);
            return;
        }
        int best = -1;
        double bd = GrabRange;
        for (int i = 0; i < w.Items.Count; i++)
        {
            var it = w.Items[i];
            if (it.Done) continue;
            double d = Math.Max(Math.Abs(it.X - h.X), Math.Abs(it.Y - h.Y) - 8);
            if (d < bd) { bd = d; best = i; }
        }
        if (best < 0) return;
        h.Carrying = best;
        w.Heard.Add(Sfx.Pickup);
    }

    private static void Items(World w)
    {
        for (int i = 0; i < w.Items.Count; i++)
        {
            var it = w.Items[i];
            if (it.Done || w.Hero.Carrying == i) continue;
            // Lying on rock, or falling to it.
            if (Solid(w, it.X, it.Y + 12.5)) { it.Vy = 0; continue; }
            it.Vy = Math.Min(400, it.Vy + Hero.Gravity * Dt);
            double ny = it.Y + it.Vy * Dt;
            if (Solid(w, it.X, ny + 12)) { it.Y = Math.Floor((ny + 12) / T) * T - 12; it.Vy = 0; }
            else it.Y = ny;
        }
    }

    public static bool Sight(World w, double ax, double ay, double bx, double by)
    {
        double d = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        for (double t = 12; t < d; t += 12)
            if (w.Cave.AtPoint(ax + (bx - ax) * t / d, ay + (by - ay) * t / d) is Tile.Rock or Tile.Door) return false;
        return true;
    }

    private static void Beasts(World w)
    {
        var h = w.Hero;
        var spawned = new List<Beast>();
        foreach (var b in w.Beasts)
        {
            b.T += Dt;
            b.Cool -= Dt;
            double dx = h.X - b.X, dy = h.Y - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            bool near = !h.Dead && d < 520;
            switch (b.Kind)
            {
                case Creature.Bird:
                {
                    double tx = near && d < 420 ? h.X : b.HomeX + Math.Sin(b.T * 0.7 + b.HomeY) * 80, ty = near && d < 420 ? h.Y : b.HomeY + Math.Sin(b.T * 1.3) * 30;
                    Steer(w, b, tx, ty, 165, 3);
                    break;
                }
                case Creature.Wasp:
                    Steer(w, b, h.X + Math.Sin(b.T * 9) * 30, h.Y + Math.Cos(b.T * 7) * 30, near ? 220 : 60, 6);
                    if (b.T > 16) b.Hp = 0;
                    break;
                case Creature.Imp:
                {
                    b.Vy = Math.Min(500, b.Vy + Hero.Gravity * Dt);
                    if (near && Math.Abs(dx) < 400) b.Vx = Math.Sign(dx) * 90;
                    else b.Vx = Math.Sin(b.T * 0.5) * 40;
                    bool onFloor = Solid(w, b.X, b.Y + 15);
                    if (onFloor && b.Cool <= 0 && (near && dy < -40 && Math.Abs(dx) < 160 || Solid(w, b.X + Math.Sign(b.Vx) * 14, b.Y)))
                    {
                        b.Vy = -420;
                        b.Cool = 1.2;
                    }
                    Move(w, b);
                    break;
                }
                case Creature.Turret:
                    if (near && d < 460 && b.Cool <= 0 && Sight(w, b.X, b.Y - 6, h.X, h.Y))
                    {
                        b.Cool = 1.7;
                        double v = 330;
                        w.Bolts.Add(new Bolt { X = b.X, Y = b.Y - 8, Vx = dx / d * v, Vy = (dy - 6) / d * v, Life = 1.6 });
                        w.Heard.Add(Sfx.Turret);
                    }
                    break;
                case Creature.Nest:
                    if (near && b.Cool <= 0 && w.Beasts.Count(x => x.Kind == Creature.Wasp) + spawned.Count < 6)
                    {
                        b.Cool = 5;
                        spawned.Add(new Beast { Kind = Creature.Wasp, X = b.X, Y = b.Y + 14, HomeX = b.X, HomeY = b.Y, Hp = 1 });
                        w.Heard.Add(Sfx.Buzz);
                    }
                    break;
            }
            // Touching the explorer.
            var (hw, hh) = b.Half;
            if (!h.Dead && b.Kind is not (Creature.Turret or Creature.Nest) && Math.Abs(dx) < hw + Hero.HalfW - 2 && Math.Abs(dy) < hh + Hero.HalfH - 2 && h.Hurt <= 0)
            {
                Hurt(w, b.Kind switch { Creature.Bird => 10, Creature.Imp => 8, _ => 5 }, b.Kind switch { Creature.Bird => "PECKED TO PIECES", Creature.Imp => "SAVAGED BY AN IMP", _ => "STUNG TO DEATH" });
                h.Vx += Math.Sign(dx == 0 ? 1 : dx) * 220;
                h.Vy -= 120;
            }
        }
        w.Beasts.AddRange(spawned);
        w.Beasts.RemoveAll(b => b.Hp <= 0);
    }

    private static void Steer(World w, Beast b, double tx, double ty, double speed, double agility)
    {
        double dx = tx - b.X, dy = ty - b.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        b.Vx += (dx / d * speed - b.Vx) * Math.Min(1, agility * Dt);
        b.Vy += (dy / d * speed - b.Vy) * Math.Min(1, agility * Dt);
        Move(w, b);
    }

    private static void Move(World w, Beast b)
    {
        var (hw, hh) = b.Half;
        double nx = b.X + b.Vx * Dt;
        if (Hits(w, nx, b.Y, hw, hh)) b.Vx = -b.Vx * 0.3; else b.X = nx;
        double ny = b.Y + b.Vy * Dt;
        if (Hits(w, b.X, ny, hw, hh)) b.Vy = 0; else b.Y = ny;
    }

    private static void Bolts(World w)
    {
        var h = w.Hero;
        foreach (var bolt in w.Bolts)
        {
            bolt.X += bolt.Vx * Dt;
            bolt.Y += bolt.Vy * Dt;
            bolt.Life -= Dt;
            if (bolt.Life <= 0) continue;
            if (Solid(w, bolt.X, bolt.Y)) { bolt.Life = 0; w.Booms.Add(new Boom(bolt.X, bolt.Y, false)); continue; }
            if (bolt.Mine)
            {
                foreach (var b in w.Beasts)
                {
                    var (hw, hh) = b.Half;
                    if (b.Hp <= 0 || Math.Abs(b.X - bolt.X) > hw + 4 || Math.Abs(b.Y - bolt.Y) > hh + 4) continue;
                    bolt.Life = 0;
                    b.Hp--;
                    w.Booms.Add(new Boom(bolt.X, bolt.Y, b.Hp <= 0));
                    if (b.Hp <= 0)
                    {
                        w.Score += b.Kind switch { Creature.Bird => 50, Creature.Wasp => 20, Creature.Imp => 80, Creature.Turret => 150, _ => 200 };
                        w.Heard.Add(Sfx.Kill);
                    }
                    else w.Heard.Add(Sfx.Hit);
                    break;
                }
            }
            else if (!h.Dead && Math.Abs(bolt.X - h.X) < Hero.HalfW + 3 && Math.Abs(bolt.Y - h.Y) < Hero.HalfH + 3)
            {
                bolt.Life = 0;
                Hurt(w, 8, "SHOT BY A TURRET");
            }
        }
        w.Bolts.RemoveAll(b => b.Life <= 0);
        w.Beasts.RemoveAll(b => b.Hp <= 0);
    }

    public static void Hurt(World w, double amount, string cause)
    {
        var h = w.Hero;
        h.Energy -= amount;
        h.Hurt = HurtTime;
        w.Cause = cause;
        w.Heard.Add(Sfx.Hurt);
    }
}
