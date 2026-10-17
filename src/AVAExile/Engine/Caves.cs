namespace AVAExile.Engine;

/// <summary>
/// The controls: left and right thrust (and turn to face that way), up fires the jetpack, down pushes downwards;
/// fire the blaster; and pick up (or put down).
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Grab = false);

/// <summary>The screen, and the planet's caves as squares of rock and open space.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double T = 32;
    public const int Cols = 110, Rows = 84;
    /// <summary>The surface: sky above this row, rock and caves below.</summary>
    public const int Surface = 8;
}

public enum Tile : byte
{
    Rock,
    Open,
    Sky,
    /// <summary>A coloured door across a passage, opened by its key.</summary>
    Door,
}

public enum Creature { Bird, Wasp, Imp, Turret, Nest }

/// <summary>Something lying in the caves to be carried: one of the ship's power pods, or a door's key.</summary>
public readonly record struct ItemDef(bool Pod, int Door, double X, double Y);

/// <summary>A door: the squares it fills, and its colour (the same as its key's).</summary>
public sealed record DoorDef(int Index, List<(int C, int R)> Cells);

/// <summary>A draught through a passage: a box, and the push it gives.</summary>
public readonly record struct WindDef(double X0, double Y0, double X1, double Y1, double Fx, double Fy);

public readonly record struct CreatureDef(Creature Kind, double X, double Y);

/// <summary>A planet's caves: the rock, the water, the draughts, the doors and keys, the pods, and what lives down there.</summary>
public sealed class CaveDef
{
    public int Level;
    public string Name = "";
    public Tile[,] Tiles = new Tile[Field.Cols, Field.Rows];
    public bool[,] Water = new bool[Field.Cols, Field.Rows];
    public List<WindDef> Winds = [];
    public List<DoorDef> Doors = [];
    public List<ItemDef> Items = [];
    public List<CreatureDef> Creatures = [];
    /// <summary>The ship on the surface: its middle, where pods are put in.</summary>
    public double ShipX, ShipY;
    public uint RockColour, DeepColour;

    public int Pods => Items.Count(i => i.Pod);

    public Tile At(int c, int r) => c < 0 || r < 0 || c >= Field.Cols || r >= Field.Rows ? Tile.Rock : Tiles[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.T), (int)Math.Floor(y / Field.T));
    public bool WetAt(double x, double y)
    {
        int c = (int)Math.Floor(x / Field.T), r = (int)Math.Floor(y / Field.T);
        return c >= 0 && r >= 0 && c < Field.Cols && r < Field.Rows && Water[c, r];
    }

    /// <summary>Whether the explorer fits with a square at its middle: it and those round it open (doors count as shut unless open).</summary>
    public bool Roomy(int c, int r, Func<int, int, bool> open)
    {
        for (int dc = -1; dc <= 1; dc++)
        for (int dr = -1; dr <= 1; dr++)
            if (!open(c + dc, r + dr)) return false;
        return true;
    }

    public bool Passable(int c, int r, HashSet<int> opened) => At(c, r) switch
    {
        Tile.Open or Tile.Sky => true,
        Tile.Door => Doors.Any(d => opened.Contains(d.Index) && d.Cells.Contains((c, r))),
        _ => false,
    };

    /// <summary>The squares the explorer can get to from one (fitting through), with the doors given open.</summary>
    public Dictionary<(int C, int R), int> Reach(int c0, int r0, HashSet<int> opened)
    {
        var d = new Dictionary<(int, int), int> { [(c0, r0)] = 0 };
        var q = new Queue<(int, int)>([(c0, r0)]);
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nc = c + dc, nr = r + dr;
                if (d.ContainsKey((nc, nr)) || !Roomy(nc, nr, (x, y) => Passable(x, y, opened))) continue;
                d[(nc, nr)] = d[(c, r)] + 1;
                q.Enqueue((nc, nr));
            }
        }
        return d;
    }

    public (int C, int R) Cell(double x, double y) => ((int)Math.Floor(x / Field.T), (int)Math.Floor(y / Field.T));
}

/// <summary>The planets, one after another: deeper caves, more pods to find, more doors and more to fight.</summary>
public static class CaveBook
{
    private static readonly (string Name, uint Rock, uint Deep)[] Looks =
    [
        ("PHOEBUS", 0xFF6A4A3A, 0xFF2A1A14),
        ("TRIAXA", 0xFF3A4A6A, 0xFF141A2A),
        ("CORONIA", 0xFF4A5A3A, 0xFF1A2414),
        ("NEMESIA", 0xFF5A3A5A, 0xFF22142A),
    ];

    public static int Count => Looks.Length;

    public static CaveDef Build(int level, int seed = 0)
    {
        for (int attempt = 0; ; attempt++)
        {
            var c = Try(level, 9000 + level * 577 + seed * 31 + attempt * 7919);
            if (c != null) return c;
        }
    }

    private static CaveDef? Try(int level, int seed)
    {
        var d = new Dice(seed);
        var look = Looks[level % Looks.Length];
        var cv = new CaveDef { Level = level, Name = look.Name, RockColour = look.Rock, DeepColour = look.Deep };
        const int cols = Field.Cols, rows = Field.Rows;
        for (int c = 0; c < cols; c++)
        for (int r = 0; r < rows; r++)
            cv.Tiles[c, r] = r < Field.Surface ? Tile.Sky : Tile.Rock;
        void Carve(double x, double y, double rad)
        {
            for (int c = (int)(x - rad - 1); c <= x + rad + 1; c++)
            for (int r = (int)(y - rad - 1); r <= y + rad + 1; r++)
                if (c > 1 && r >= Field.Surface && c < cols - 2 && r < rows - 2 && (c + 0.5 - x) * (c + 0.5 - x) + (r + 0.5 - y) * (r + 0.5 - y) <= rad * rad)
                    cv.Tiles[c, r] = Tile.Open;
        }
        // Chambers.
        var rooms = new List<(double X, double Y, double Rx, double Ry)>();
        for (int tries = 0; rooms.Count < 13 + level && tries < 3000; tries++)
        {
            double x = 8 + d.Next() * (cols - 16), y = Field.Surface + 8 + d.Next() * (rows - Field.Surface - 14);
            double rx = 4 + d.Next() * 5, ry = 3 + d.Next() * 3;
            if (rooms.Any(o => Math.Abs(o.X - x) < o.Rx + rx + 4 && Math.Abs(o.Y - y) < o.Ry + ry + 4)) continue;
            rooms.Add((x, y, rx, ry));
        }
        if (rooms.Count < 10) return null;
        foreach (var (x, y, rx, ry) in rooms)
            for (int c = (int)(x - rx); c <= x + rx; c++)
            for (int r = (int)(y - ry); r <= y + ry; r++)
                if ((c + 0.5 - x) * (c + 0.5 - x) / (rx * rx) + (r + 0.5 - y) * (r + 0.5 - y) / (ry * ry) <= 1 && c > 1 && c < cols - 2 && r < rows - 2)
                    cv.Tiles[c, r] = Tile.Open;
        // Passages joining them: each to the nearest already joined, wandering a little.
        var joined = new List<int> { 0 };
        void Tunnel(double ax, double ay, double bx, double by)
        {
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            double wob = d.Next() * 6 - 3;
            for (double t = 0; t <= 1; t += 0.5 / len)
            {
                double x = ax + (bx - ax) * t + Math.Sin(t * Math.PI) * wob, y = ay + (by - ay) * t + Math.Sin(t * Math.PI * 2) * wob * 0.5;
                Carve(x, y, 2.6);
            }
        }
        while (joined.Count < rooms.Count)
        {
            int best = -1, from = -1;
            double bd = double.MaxValue;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (joined.Contains(i)) continue;
                foreach (int j in joined)
                {
                    double dd = Math.Abs(rooms[i].X - rooms[j].X) + Math.Abs(rooms[i].Y - rooms[j].Y);
                    if (dd < bd) { bd = dd; best = i; from = j; }
                }
            }
            Tunnel(rooms[from].X, rooms[from].Y, rooms[best].X, rooms[best].Y);
            joined.Add(best);
        }
        // A few more, for other ways round.
        for (int k = 0; k < 3; k++)
        {
            int a = d.Next(rooms.Count), b = d.Next(rooms.Count);
            if (a != b && Math.Abs(rooms[a].X - rooms[b].X) + Math.Abs(rooms[a].Y - rooms[b].Y) < 50) Tunnel(rooms[a].X, rooms[a].Y, rooms[b].X, rooms[b].Y);
        }
        // The way down from the surface, from beside the ship to the nearest chamber.
        double shaftX = 24;
        var top = rooms.OrderBy(o => Math.Abs(o.X - shaftX) + o.Y).First();
        Tunnel(shaftX, Field.Surface - 1, shaftX, Field.Surface + 4);
        Tunnel(shaftX, Field.Surface + 4, top.X, top.Y);
        for (int r = Field.Surface - 2; r < Field.Surface + 3; r++)
            for (int c = (int)shaftX - 2; c <= shaftX + 2; c++) cv.Tiles[c, r] = r < Field.Surface ? Tile.Sky : Tile.Open;
        cv.ShipX = 10 * Field.T;
        cv.ShipY = (Field.Surface - 1.6) * Field.T;
        var ship = cv.Cell(cv.ShipX + 3 * Field.T, cv.ShipY);
        var none = new HashSet<int>();
        var all = cv.Reach(ship.C, ship.R, none);
        if (all.Count < 1500) return null;
        // Floors: open squares with rock under them, reached from the ship.
        var floors = all.Keys.Where(k => k.R > Field.Surface + 3 && cv.At(k.C, k.R + 2) == Tile.Rock && cv.At(k.C, k.R + 1) == Tile.Open).ToList();
        // Pods, deep and far apart.
        int pods = 5 + Math.Min(level, 3);
        var podAt = new List<(int C, int R)>();
        foreach (var f in floors.OrderByDescending(f => all[f] + d.Next(40)))
        {
            if (podAt.Count >= pods) break;
            if (podAt.Any(p => Math.Abs(p.C - f.C) + Math.Abs(p.R - f.R) < 22)) continue;
            podAt.Add(f);
        }
        if (podAt.Count < pods) return null;
        // Doors across the passages leading to the furthest pods, each key somewhere reached without it.
        int doors = 1 + Math.Min(2, level);
        var opened = new HashSet<int>();
        foreach (var target in podAt.OrderByDescending(p => all[p]).Take(doors))
        {
            var path = Path(cv, ship, target, opened);
            if (path == null) return null;
            DoorDef? door = null;
            for (int i = path.Count - 12; i > path.Count / 3 && door == null; i--)
            {
                var (c, r) = path[i];
                var (pc, pr) = path[i - 1];
                bool vertical = pr == r;
                var cells = new List<(int, int)>();
                for (int k = 0; k < 9; k++)
                {
                    int x = vertical ? c : c + k - 4, y = vertical ? r + k - 4 : r;
                    if (cv.At(x, y) == Tile.Open) cells.Add((x, y));
                }
                // The door must close the passage: rock both ends of it, and not too wide.
                if (cells.Count is < 3 or > 7) continue;
                var first = cells[0];
                var last = cells[^1];
                bool sealedEnds = vertical ? cv.At(c, first.Item2 - 1) == Tile.Rock && cv.At(c, last.Item2 + 1) == Tile.Rock && cells.Count == last.Item2 - first.Item2 + 1
                                           : cv.At(first.Item1 - 1, r) == Tile.Rock && cv.At(last.Item1 + 1, r) == Tile.Rock && cells.Count == last.Item1 - first.Item1 + 1;
                if (!sealedEnds) continue;
                door = new DoorDef(cv.Doors.Count, cells);
            }
            if (door == null) return null;
            foreach (var (c, r) in door.Cells) cv.Tiles[c, r] = Tile.Door;
            // It must actually shut the pod away.
            var before = cv.Reach(ship.C, ship.R, opened);
            if (before.ContainsKey(target)) return null;
            cv.Doors.Add(door);
            var keySpots = floors.Where(f => before.ContainsKey(f) && all[f] > 30 && !podAt.Contains(f) && !cv.Items.Any(i => Math.Abs(i.X / Field.T - f.C) < 6 && Math.Abs(i.Y / Field.T - f.R) < 6)).ToList();
            if (keySpots.Count == 0) return null;
            var key = keySpots[d.Next(keySpots.Count)];
            cv.Items.Add(new ItemDef(false, door.Index, (key.C + 0.5) * Field.T, (key.R + 2) * Field.T - 12));
            opened.Add(door.Index);
        }
        foreach (var p in podAt) cv.Items.Add(new ItemDef(true, -1, (p.C + 0.5) * Field.T, (p.R + 2) * Field.T - 12));
        // Everything reached with every door open.
        var open = cv.Reach(ship.C, ship.R, opened);
        if (!podAt.All(open.ContainsKey)) return null;
        // Water lying in the bottoms of some chambers.
        foreach (var (x, y, rx, ry) in rooms.OrderBy(_ => d.Next()).Take(2 + level))
        {
            int level0 = (int)(y + ry * 0.35);
            for (int c = (int)(x - rx - 1); c <= x + rx + 1; c++)
            for (int r = level0; r <= y + ry + 1; r++)
                if (cv.At(c, r) == Tile.Open) cv.Water[c, r] = true;
        }
        // Draughts through some of the passages.
        for (int k = 0; k < 3 + level; k++)
        {
            var f = all.Keys.ElementAt(d.Next(all.Count));
            if (f.R < Field.Surface + 4) continue;
            bool wet = false;
            for (int c = f.C - 4; c <= f.C + 5 && !wet; c++)
                for (int r = f.R - 4; r <= f.R + 5 && !wet; r++)
                    wet = c >= 0 && r >= 0 && c < cols && r < rows && cv.Water[c, r];
            if (wet) continue;
            bool up = d.Next(2) == 0;
            cv.Winds.Add(new WindDef((f.C - 3) * Field.T, (f.R - 3) * Field.T, (f.C + 4) * Field.T, (f.R + 4) * Field.T, up ? 0 : (d.Next(2) == 0 ? -1 : 1) * 380, up ? -420 : 0));
        }
        // What lives down there.
        int beasts = 10 + level * 4;
        var spots = all.Keys.Where(k => k.R > Field.Surface + 8 && Math.Abs(k.C - ship.C) + Math.Abs(k.R - ship.R) > 20).ToList();
        for (int k = 0; k < beasts && spots.Count > 0; k++)
        {
            var s = spots[d.Next(spots.Count)];
            var kind = (Creature)d.Next(5);
            double x = (s.C + 0.5) * Field.T, y = (s.R + 0.5) * Field.T;
            if (kind is Creature.Imp or Creature.Turret)
            {
                // On a floor.
                int r = s.R;
                while (r < Field.Rows - 1 && cv.At(s.C, r + 1) != Tile.Rock) r++;
                y = (r + 1) * Field.T - (kind == Creature.Turret ? 12 : 14);
                if (cv.WetAt(x, y)) continue;
            }
            else if (kind == Creature.Nest)
            {
                // Hanging from a ceiling.
                int r = s.R;
                while (r > 0 && cv.At(s.C, r - 1) != Tile.Rock) r--;
                y = r * Field.T + 14;
            }
            cv.Creatures.Add(new CreatureDef(kind, x, y));
        }
        return cv;
    }

    /// <summary>The shortest way through the caves between two squares (with the doors given open), or null.</summary>
    public static List<(int C, int R)>? Path(CaveDef cv, (int C, int R) from, (int C, int R) to, HashSet<int> opened)
    {
        var prev = new Dictionary<(int, int), (int, int)>();
        var q = new Queue<(int, int)>([from]);
        prev[from] = from;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to) break;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (cur.Item1 + dc, cur.Item2 + dr);
                if (prev.ContainsKey(n) || !cv.Roomy(n.Item1, n.Item2, (x, y) => cv.Passable(x, y, opened) || cv.At(x, y) == Tile.Door)) continue;
                prev[n] = cur;
                q.Enqueue(n);
            }
        }
        if (!prev.ContainsKey(to)) return null;
        var path = new List<(int, int)>();
        for (var p = to; p != from; p = prev[p]) path.Add(p);
        path.Add(from);
        path.Reverse();
        return path;
    }
}
