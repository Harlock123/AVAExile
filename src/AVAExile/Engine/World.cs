namespace AVAExile.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The explorer in his jetpack: where he is (the middle of him), how fast, which way he faces, his shields.</summary>
public sealed class Hero
{
    public const double HalfW = 10, HalfH = 15, Gravity = 520, Lift = 1050, Push = 700, Drag = 1.4, MaxSpeed = 360, Energy0 = 100;
    public double X, Y, Vx, Vy, Energy = Energy0, FireTimer, Hurt;
    public int Facing = 1;
    /// <summary>The item he carries (its place in the list), or -1.</summary>
    public int Carrying = -1;
    public bool Grounded, Dead, GrabPrev, Wet, Thrusting;
    public Hero Copy() => (Hero)MemberwiseClone();
}

public sealed class Item
{
    public ItemDef Def;
    public double X, Y, Vy;
    /// <summary>Put into the ship (a pod), or used on its door (a key).</summary>
    public bool Done;
    public Item Copy() => (Item)MemberwiseClone();
}

public sealed class Beast
{
    public Creature Kind;
    public double X, Y, Vx, Vy, Cool, T;
    public int Hp;
    public double HomeX, HomeY;
    public Beast Copy() => (Beast)MemberwiseClone();
    public (double W, double H) Half => Kind switch
    {
        Creature.Wasp => (7, 6),
        Creature.Bird => (13, 9),
        Creature.Imp => (10, 14),
        Creature.Turret => (13, 12),
        _ => (14, 14),
    };
}

public sealed class Bolt
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine;
    public Bolt Copy() => (Bolt)MemberwiseClone();
}

public sealed record Boom(double X, double Y, bool Big)
{
    public double T;
}

/// <summary>Everything in the caves that moves or changes, kept together so it can be copied whole.</summary>
public sealed class World
{
    public CaveDef Cave = null!;
    public Hero Hero = new();
    public List<Item> Items = [];
    public List<Beast> Beasts = [];
    public List<Bolt> Bolts = [];
    public List<Boom> Booms = [];
    public HashSet<int> Opened = [];
    public List<Sfx> Heard = [];
    public Dice Dice = new(1);
    public double Clock;
    public int Score, Delivered;
    public string Cause = "";

    public bool Repaired => Delivered >= Cave.Pods;

    public World Copy() => new()
    {
        Cave = Cave, Hero = Hero.Copy(), Items = Items.Select(i => i.Copy()).ToList(), Beasts = Beasts.Select(b => b.Copy()).ToList(),
        Bolts = Bolts.Select(b => b.Copy()).ToList(), Opened = [.. Opened], Dice = Dice, Clock = Clock, Score = Score,
        Delivered = Delivered, Cause = Cause,
    };
}
