namespace AVAExile.Engine;

/// <summary>
/// The computer explorer, for the demonstration and the tests. It finds its way through the caves square by
/// square (where it fits), to the nearest pod it can get to, or, with the pods behind doors, to the key that
/// opens the way; then carries it to the ship, or the door. It flies at the furthest point of the way it can see,
/// thrusting against gravity to hold the speed it wants, and shoots what comes at it.
/// </summary>
public sealed class Pilot
{
    private List<(int C, int R)>? _path;
    private double _replan;
    private (int C, int R) _goal;
    private bool _grabPrev, _firePrev;
    public const double Cruise = 230;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _path = null;
        _replan = 0;
        _pick = -1;
        _carrying = -2;
        _target = null;
        _grabPrev = _firePrev = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var h = w.Hero;
        var cv = w.Cave;
        if (h.Dead) return default;
        var here = cv.Cell(h.X, h.Y);
        _replan -= Field.Step;
        if (_replan <= 0 || _carrying != h.Carrying || _pick >= 0 && w.Items[_pick].Done)
        {
            _replan = 0.6;
            _carrying = h.Carrying;
            (_target, _pick) = Choose(w, h, here);
            _path = null;
        }
        var target = _target;
        int pick = _pick;
        if (target is not { } t) { Doing = "nothing to do"; return Hover(h, 0, 0); }
        var goal = cv.Cell(t.X, t.Y);
        if (_path == null || _goal != goal || !_path.Contains(here))
        {
            _goal = goal;
            _path = Route(cv, here, goal, w.Opened);
        }
        return Fly(w, h, t, pick, goal, here);
    }

    private (double X, double Y)? _target;
    private int _pick = -1, _carrying = -2;

    /// <summary>What to fetch, and where to take it.</summary>
    private ((double X, double Y)?, int) Choose(World w, Hero h, (int C, int R) here)
    {
        var cv = w.Cave;
        (double X, double Y)? target = null;
        int pick = -1;
        if (h.Carrying >= 0)
        {
            var it = w.Items[h.Carrying];
            if (it.Def.Pod) { target = (cv.ShipX, cv.ShipY + 20); Doing = "taking a pod to the ship"; }
            else
            {
                // The door's nearest square on this side.
                var door = cv.Doors[it.Def.Door];
                var reach = cv.Reach(here.C, here.R, w.Opened);
                var near = reach.Keys.OrderBy(k => door.Cells.Min(dc => Math.Abs(dc.C - k.C) + Math.Abs(dc.R - k.R))).First();
                target = ((near.C + 0.5) * Field.T, (near.R + 0.5) * Field.T);
                Doing = "taking the key to its door";
            }
        }
        else
        {
            var reach = cv.Reach(here.C, here.R, w.Opened);
            int best = int.MaxValue;
            for (int i = 0; i < w.Items.Count; i++)
            {
                var it = w.Items[i];
                if (it.Done) continue;
                var cell = cv.Cell(it.X, it.Y - 14);
                // Pods first; keys only for doors still shut, when no pod can be got to.
                if (!reach.TryGetValue(cell, out int d) && !reach.TryGetValue((cell.C, cell.R - 1), out d)) continue;
                // (A key is only worth fetching if its door can be got to from here.)
                if (!it.Def.Pod && !cv.Doors[it.Def.Door].Cells.Any(dc => reach.Keys.Any(k => Math.Abs(k.C - dc.C) + Math.Abs(k.R - dc.R) <= 3))) continue;
                int cost = d + (it.Def.Pod ? 0 : 1000);
                if (cost < best) { best = cost; pick = i; }
            }
            if (pick >= 0)
            {
                var it = w.Items[pick];
                target = (it.X, it.Y - 8);
                Doing = it.Def.Pod ? "after a pod" : "after a key";
            }
        }
        return (target, pick);
    }

    private Controls Fly(World w, Hero h, (double X, double Y) t, int pick, (int C, int R) goal, (int C, int R) here)
    {
        var cv = w.Cave;
        // Close enough to grab it.
        bool grab = false;
        if (pick >= 0 && Math.Abs(w.Items[pick].X - h.X) < Sim.GrabRange - 6 && Math.Abs(w.Items[pick].Y - h.Y) < Sim.GrabRange)
            grab = !_grabPrev;
        _grabPrev = grab;
        // The furthest square on the way that can be seen.
        (double X, double Y) aim = t;
        if (_path != null)
        {
            int at = Math.Max(0, _path.IndexOf(here));
            for (int i = Math.Min(_path.Count - 1, at + 8); i > at; i--)
            {
                double px = (_path[i].C + 0.5) * Field.T, py = (_path[i].R + 0.5) * Field.T;
                if (Clear(w, h.X, h.Y, px, py)) { aim = (px, py); break; }
                if (i == at + 1) aim = (px, py);
            }
            if (_path.Count - 1 - at > 8 || aim != t && _path.Count > 0 && cv.Cell(aim.X, aim.Y) != goal) { }
            else aim = t;
        }
        double dx = aim.X - h.X, dy = aim.Y - h.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        double tx = t.X - h.X, ty = t.Y - h.Y, left = Math.Sqrt(tx * tx + ty * ty);
        double speed = Math.Min(Cruise, Math.Max(40, left * 1.6));
        double vx = dist > 1 ? dx / dist * speed : 0, vy = dist > 1 ? dy / dist * speed : 0;
        var c = Hover(h, vx, vy);
        return c with { Fire = Shoot(w, h), Grab = grab };
    }

    /// <summary>Thrust to bring the velocity round to the one wanted.</summary>
    private static Controls Hover(Hero h, double vx, double vy)
    {
        int dx = vx - h.Vx > 25 ? 1 : vx - h.Vx < -25 ? -1 : 0;
        // The jetpack fights gravity: on while falling faster (or rising slower) than wanted.
        int dy = h.Vy > vy - 10 ? -1 : vy - h.Vy > 160 ? 1 : 0;
        return new Controls(dx, dy);
    }

    private bool Shoot(World w, Hero h)
    {
        foreach (var b in w.Beasts)
        {
            double dx = b.X - h.X, dy = b.Y - h.Y;
            if (Math.Sign(dx) != h.Facing || Math.Abs(dx) > 340 || Math.Abs(dy) > 24 + b.Half.H) continue;
            if (!Sim.Sight(w, h.X, h.Y, b.X, b.Y)) continue;
            _firePrev = !_firePrev;
            return true;
        }
        return false;
    }

    /// <summary>A clear line, wide enough for the explorer, between two points.</summary>
    private static bool Clear(World w, double ax, double ay, double bx, double by)
    {
        foreach (var (ox, oy) in new[] { (0.0, 0.0), (-Hero.HalfW, -Hero.HalfH), (Hero.HalfW, Hero.HalfH), (-Hero.HalfW, Hero.HalfH), (Hero.HalfW, -Hero.HalfH) })
            if (!Sim.Sight(w, ax + ox, ay + oy, bx + ox, by + oy)) return false;
        return true;
    }

    /// <summary>The way from one square to another where the explorer fits, through open doors.</summary>
    private static List<(int C, int R)>? Route(CaveDef cv, (int C, int R) from, (int C, int R) to, HashSet<int> opened)
    {
        var prev = new Dictionary<(int, int), (int, int)> { [from] = from };
        var q = new Queue<(int, int)>([from]);
        (int, int)? end = null;
        double bd = double.MaxValue;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            // (Or as close to it as can be got, if it can't be reached itself.)
            double d = Math.Abs(cur.Item1 - to.C) + Math.Abs(cur.Item2 - to.R);
            if (d < bd) { bd = d; end = cur; }
            if (cur == to) break;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (cur.Item1 + dc, cur.Item2 + dr);
                if (prev.ContainsKey(n) || !cv.Roomy(n.Item1, n.Item2, (x, y) => cv.Passable(x, y, opened))) continue;
                prev[n] = cur;
                q.Enqueue(n);
            }
        }
        if (end is not { } e) return null;
        var path = new List<(int, int)>();
        for (var p = e; p != from; p = prev[p]) path.Add(p);
        path.Add(from);
        path.Reverse();
        return path;
    }
}
