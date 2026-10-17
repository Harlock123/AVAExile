using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAExile.Engine;

namespace AVAExile.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAExile/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 672;
    private double _camX, _camY;
    private static readonly uint[] DoorColours = [0xFFE04040, 0xFF40C060, 0xFF4080F0, 0xFFE0C040];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawCaves(dc, g.World);
                DrawScanner(dc, g.World);
                DrawHud(dc, g.World);
                if (g.Message is { } msg)
                {
                    double wdt = Measure(msg, 40) + 40;
                    dc.FillRectangle(Brush(0xC0000000), new Rect(512 - wdt / 2, 250, wdt, 60), 8);
                    Text(dc, msg, new Point(512, 280), 40, g.Phase == Phase.Dying ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, ViewH - 18), 22, Yellow, center: true);
    }

    private static double Measure(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(White)).Width;

    private static uint Shade(uint c, double k)
    {
        uint r = (uint)Math.Min(255, ((c >> 16) & 0xFF) * k), gg = (uint)Math.Min(255, ((c >> 8) & 0xFF) * k), b = (uint)Math.Min(255, (c & 0xFF) * k);
        return 0xFF000000 | r << 16 | gg << 8 | b;
    }

    // ───────────────────────────── the caves ─────────────────────────────

    private void DrawCaves(DrawingContext dc, World w)
    {
        var h = w.Hero;
        var cv = w.Cave;
        _camX = Math.Clamp(h.X - Field.Width / 2, 0, Field.Cols * Field.T - Field.Width);
        _camY = Math.Clamp(h.Y - ViewH / 2, 0, Field.Rows * Field.T - ViewH);
        using (dc.PushClip(new Rect(0, 0, Field.Width, ViewH)))
        using (dc.PushTransform(Matrix.CreateTranslation(-_camX, -_camY)))
        {
            const double T = Field.T;
            int c0 = Math.Max(0, (int)(_camX / T) - 1), r0 = Math.Max(0, (int)(_camY / T) - 1);
            int c1 = Math.Min(Field.Cols - 1, c0 + (int)(Field.Width / T) + 3), r1 = Math.Min(Field.Rows - 1, r0 + (int)(ViewH / T) + 3);
            // The open caves (dark), and the sky above the surface.
            dc.FillRectangle(Brush(cv.DeepColour), new Rect(_camX, _camY, Field.Width, ViewH));
            double skyBottom = Field.Surface * T;
            if (_camY < skyBottom)
            {
                dc.FillRectangle(Brush(0xFF1A2A5A), new Rect(_camX, _camY, Field.Width, skyBottom - _camY));
                dc.FillRectangle(Brush(0xFF3A4A8A), new Rect(_camX, skyBottom - 90, Field.Width, 90));
                for (int i = 0; i < 40; i++)
                {
                    uint hh = Hash(i, 7, 3);
                    double sx = _camX + hh % (uint)Field.Width, sy = (hh >> 12) % (uint)(skyBottom - 100);
                    if (sy > _camY) dc.FillRectangle(Brush(0xFFE0E0FF), new Rect(sx, sy, 2, 2));
                }
            }
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                var t = cv.Tiles[c, r];
                double x = c * T, y = r * T;
                if (t == Tile.Rock)
                {
                    uint hh = Hash(c, r, cv.Level);
                    double k = 0.85 + (hh % 30) / 100.0;
                    dc.FillRectangle(Brush(Shade(cv.RockColour, k * (r < Field.Surface + 1 ? 1.1 : 1))), new Rect(x, y, T, T));
                    // Edges where the rock meets the open.
                    bool Open(int cc, int rr) => cv.At(cc, rr) is Tile.Open or Tile.Sky;
                    uint edge = Shade(cv.RockColour, 1.45);
                    if (Open(c, r - 1)) dc.FillRectangle(Brush(r == Field.Surface ? 0xFF5A8A3A : edge), new Rect(x, y, T, 4));
                    if (Open(c, r + 1)) dc.FillRectangle(Brush(Shade(cv.RockColour, 0.6)), new Rect(x, y + T - 3, T, 3));
                    if (Open(c - 1, r)) dc.FillRectangle(Brush(Shade(cv.RockColour, 1.2)), new Rect(x, y, 3, T));
                    if (Open(c + 1, r)) dc.FillRectangle(Brush(Shade(cv.RockColour, 0.7)), new Rect(x + T - 3, y, 3, T));
                    if ((hh >> 8) % 5 == 0) dc.FillEllipse(x + 8 + (hh >> 12) % 16, y + 8 + (hh >> 16) % 16, 2.5, Shade(cv.RockColour, 0.6));
                }
                else if (t == Tile.Door)
                {
                    int di = cv.Doors.First(d => d.Cells.Contains((c, r))).Index;
                    uint col = DoorColours[di % DoorColours.Length];
                    dc.FillRectangle(Brush(Shade(col, 0.5)), new Rect(x, y, T, T));
                    dc.FillRectangle(Brush(col), new Rect(x + 4, y + 4, T - 8, T - 8), 3);
                    dc.FillRectangle(Brush(Shade(col, 1.4)), new Rect(x + 12, y + 12, 8, 8));
                }
            }
            // Water.
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                if (!cv.Water[c, r]) continue;
                double x = c * T, y = r * T;
                dc.FillRectangle(Brush(0x90306AC0), new Rect(x, y, T, T));
                if (r > 0 && !cv.Water[c, r - 1]) dc.FillRectangle(Brush(0xC080C0FF), new Rect(x, y + Math.Sin(_clock * 3 + c) * 2, T, 3));
            }
            // Draughts.
            foreach (var wd in cv.Winds)
            {
                if (wd.X1 < _camX || wd.X0 > _camX + Field.Width || wd.Y1 < _camY || wd.Y0 > _camY + ViewH) continue;
                for (int i = 0; i < 12; i++)
                {
                    uint hh = Hash(i, (int)wd.X0, 9);
                    double len = 18, along = (_clock * 220 + hh % 400) % 1;
                    double px = wd.X0 + (hh % 1000) / 1000.0 * (wd.X1 - wd.X0), py = wd.Y0 + ((hh >> 10) % 1000) / 1000.0 * (wd.Y1 - wd.Y0);
                    double t = (_clock * 0.9 + i * 0.37) % 1;
                    px += wd.Fx == 0 ? 0 : Math.Sign(wd.Fx) * (t - 0.5) * (wd.X1 - wd.X0);
                    py += wd.Fy == 0 ? 0 : Math.Sign(wd.Fy) * (t - 0.5) * (wd.Y1 - wd.Y0);
                    px = wd.X0 + ((px - wd.X0) % (wd.X1 - wd.X0) + (wd.X1 - wd.X0)) % (wd.X1 - wd.X0);
                    py = wd.Y0 + ((py - wd.Y0) % (wd.Y1 - wd.Y0) + (wd.Y1 - wd.Y0)) % (wd.Y1 - wd.Y0);
                    if (cv.AtPoint(px, py) != Tile.Open) continue;
                    dc.DrawLine(Pen(0x50E0F0FF, 2), new Point(px, py), new Point(px + Math.Sign(wd.Fx) * len, py + Math.Sign(wd.Fy) * len));
                    _ = along;
                }
            }
            DrawShip(dc, cv.ShipX, cv.ShipY, w.Delivered, cv.Pods);
            foreach (var it in w.Items)
                if (!it.Done) DrawItem(dc, it.Def, it.X, it.Y);
            foreach (var b in w.Beasts) DrawBeast(dc, b);
            foreach (var bl in w.Bolts)
                dc.DrawLine(Pen(bl.Mine ? 0xFF80FFFF : 0xFFFF6040, 3), new Point(bl.X, bl.Y), new Point(bl.X - bl.Vx * 0.02, bl.Y - bl.Vy * 0.02));
            if (!h.Dead && (h.Hurt <= 0 || (int)(_clock * 12) % 2 == 0)) DrawHero(dc, h);
            foreach (var bm in w.Booms)
            {
                double r = (bm.Big ? 30 : 12) * Math.Min(1, bm.T * 5);
                using (dc.PushOpacity(Math.Clamp(1 - bm.T * 1.3, 0, 1)))
                {
                    dc.FillEllipse(bm.X, bm.Y, r, 0xFFFF9030);
                    dc.FillEllipse(bm.X, bm.Y, r * 0.5, 0xFFFFF0A0);
                }
            }
        }
    }

    private void DrawShip(DrawingContext dc, double x, double y, int done, int pods)
    {
        // The Pericles, down on the surface: hull, fins, the bay door, a light for each pod put in.
        Poly(dc, 0xFFB8C0CC, 0xFF505868, 2, new Point(x - 110, y + 40), new Point(x - 90, y - 20), new Point(x + 60, y - 30), new Point(x + 130, y + 10), new Point(x + 100, y + 40));
        Poly(dc, 0xFF8890A0, 0xFF505868, 2, new Point(x - 100, y + 40), new Point(x - 130, y + 52), new Point(x - 70, y + 40));
        Poly(dc, 0xFF8890A0, 0xFF505868, 2, new Point(x + 80, y + 40), new Point(x + 110, y + 52), new Point(x + 50, y + 40));
        dc.FillRectangle(Brush(0xFF303848), new Rect(x - 30, y - 6, 60, 46), 4);
        dc.FillRectangle(Brush(0xFF80D0FF), new Rect(x + 70, y - 14, 30, 12), 4);
        for (int i = 0; i < pods; i++)
            dc.FillEllipse(x - 70 + i * 16, y - 12, 5, i < done ? 0xFF60FF80 : 0xFF404850);
        Text(dc, "PERICLES", new Point(x + 10, y + 26), 16, 0xFF202838, center: true, shadow: false);
    }

    private static void DrawItem(DrawingContext dc, ItemDef def, double x, double y)
    {
        if (def.Pod)
        {
            dc.FillEllipse(x, y, 16, 0x40FFA030);
            dc.FillRectangle(Brush(0xFFE08020), new Rect(x - 9, y - 12, 18, 24), 5);
            dc.FillRectangle(Brush(0xFFFFD060), new Rect(x - 5, y - 8, 10, 16), 3);
            dc.FillRectangle(Brush(0xFF603010), new Rect(x - 9, y - 2, 18, 3));
        }
        else
        {
            uint c = DoorColours[def.Door % DoorColours.Length];
            dc.DrawEllipse(null, Pen(c, 4), new Point(x - 6, y), 6, 6);
            dc.DrawLine(Pen(c, 4), new Point(x, y), new Point(x + 12, y));
            dc.DrawLine(Pen(c, 3), new Point(x + 9, y), new Point(x + 9, y + 6));
        }
    }

    private void DrawBeast(DrawingContext dc, Beast b)
    {
        double x = b.X, y = b.Y;
        switch (b.Kind)
        {
            case Creature.Bird:
            {
                double flap = Math.Sin(b.T * 14) * 9;
                int f = b.Vx >= 0 ? 1 : -1;
                Poly(dc, 0xFF4A6AC0, null, 0, new Point(x - 4, y), new Point(x - 16, y - flap), new Point(x + 2, y - 2));
                Poly(dc, 0xFF4A6AC0, null, 0, new Point(x + 4, y), new Point(x + 16, y - flap), new Point(x - 2, y - 2));
                dc.DrawEllipse(Brush(0xFF6A8AE0), null, new Point(x, y), 10, 7);
                Poly(dc, 0xFFFFC040, null, 0, new Point(x + f * 9, y - 2), new Point(x + f * 17, y), new Point(x + f * 9, y + 2));
                dc.FillEllipse(x + f * 5, y - 3, 2, 0xFF000000);
                break;
            }
            case Creature.Wasp:
                dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(x - 3, y - 6 + Math.Sin(b.T * 40) * 2), 5, 3);
                dc.DrawEllipse(Brush(0xFFF0D020), null, new Point(x, y), 7, 5);
                dc.FillRectangle(Brush(0xFF202020), new Rect(x - 2, y - 5, 2, 10));
                dc.FillRectangle(Brush(0xFF202020), new Rect(x + 2, y - 5, 2, 10));
                break;
            case Creature.Imp:
            {
                double step = Math.Sin(b.T * 12) * 3;
                dc.FillRectangle(Brush(0xFF8A1A1A), new Rect(x - 6 + step, y + 4, 4, 10));
                dc.FillRectangle(Brush(0xFF8A1A1A), new Rect(x + 2 - step, y + 4, 4, 10));
                dc.DrawEllipse(Brush(0xFFD03030), null, new Point(x, y - 2), 10, 10);
                Poly(dc, 0xFFD03030, null, 0, new Point(x - 8, y - 8), new Point(x - 6, y - 18), new Point(x - 2, y - 10));
                Poly(dc, 0xFFD03030, null, 0, new Point(x + 8, y - 8), new Point(x + 6, y - 18), new Point(x + 2, y - 10));
                dc.FillEllipse(x - 4, y - 3, 2, 0xFFFFF060);
                dc.FillEllipse(x + 4, y - 3, 2, 0xFFFFF060);
                break;
            }
            case Creature.Turret:
            {
                var h = Engine.World.Hero;
                double a = Math.Atan2(h.Y - y, h.X - x);
                dc.DrawLine(Pen(0xFF404850, 6), new Point(x, y - 6), new Point(x + Math.Cos(a) * 20, y - 6 + Math.Sin(a) * 20));
                dc.DrawEllipse(Brush(0xFF8890A0), Pen(0xFF404850, 2), new Point(x, y), 14, 10);
                dc.FillRectangle(Brush(0xFF606878), new Rect(x - 16, y + 4, 32, 8));
                dc.FillEllipse(x, y - 3, 3, (int)(_clock * 4) % 2 == 0 ? 0xFFFF4040 : 0xFF802020);
                break;
            }
            default:
                // A hive hanging from the roof.
                dc.DrawLine(Pen(0xFF5A4A2A, 3), new Point(x, y - 14), new Point(x, y - 4));
                dc.DrawEllipse(Brush(0xFFB08A40), Pen(0xFF6A5020, 2), new Point(x, y + 4), 14, 16);
                for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFF6A5020, 1.5), new Point(x - 13, y - 4 + i * 8), new Point(x + 13, y - 4 + i * 8));
                dc.FillEllipse(x, y + 14, 4, 0xFF201000);
                break;
        }
    }

    /// <summary>Mike Finn in his spacesuit and jetpack, flame under him when it's firing.</summary>
    private void DrawHero(DrawingContext dc, Hero h)
    {
        double x = h.X, y = h.Y;
        int f = h.Facing;
        if (h.Thrusting)
        {
            double fl = 10 + Math.Abs(Math.Sin(_clock * 40)) * 10;
            Poly(dc, 0xFFFFA020, null, 0, new Point(x - f * 10 - 4, y + 6), new Point(x - f * 10 + 4, y + 6), new Point(x - f * 10, y + 6 + fl));
            Poly(dc, 0xFFFFF080, null, 0, new Point(x - f * 10 - 2, y + 6), new Point(x - f * 10 + 2, y + 6), new Point(x - f * 10, y + 2 + fl * 0.6));
        }
        dc.FillRectangle(Brush(0xFF707880), new Rect(x - f * 10 - 5, y - 10, 10, 16), 3);
        dc.FillRectangle(Brush(0xFFE8E8F0), new Rect(x - 7, y - 6, 14, 14), 3);
        dc.FillRectangle(Brush(0xFFD0D0D8), new Rect(x - 6, y + 8, 5, 8));
        dc.FillRectangle(Brush(0xFFD0D0D8), new Rect(x + 1, y + 8, 5, 8));
        dc.FillEllipse(x, y - 11, 8, 0xFFE8E8F0);
        dc.DrawEllipse(Brush(0xFF2A4A7A), null, new Point(x + f * 3, y - 11), 5, 4);
        dc.FillEllipse(x + f * 4, y - 12, 1.5, 0xC0FFFFFF);
        // His arm and blaster.
        dc.DrawLine(Pen(0xFFD0D0D8, 4), new Point(x, y - 2), new Point(x + f * 12, y - 2));
        dc.DrawLine(Pen(0xFF404040, 3), new Point(x + f * 10, y - 4), new Point(x + f * 18, y - 4));
    }

    // ───────────────────────────── the scanner and the gauges ─────────────────────────────

    private void DrawScanner(DrawingContext dc, World w)
    {
        const double sw = 220, sh = 168;
        double sx = Field.Width - sw - 10, sy = 10, k = sw / (Field.Cols * Field.T);
        dc.FillRectangle(Brush(0xC0000000), new Rect(sx - 4, sy - 4, sw + 8, sh + 8), 6);
        dc.DrawRectangle(null, Pen(0xFF40A060, 2), new Rect(sx - 4, sy - 4, sw + 8, sh + 8), 6, 6);
        var cv = w.Cave;
        for (int c = 0; c < Field.Cols; c += 2)
        for (int r = Field.Surface; r < Field.Rows; r += 2)
            if (cv.Tiles[c, r] != Tile.Rock) dc.FillRectangle(Brush(cv.Water[c, r] ? 0xFF204870 : 0xFF2A3A2A), new Rect(sx + c * Field.T * k, sy + r * Field.T * k, 2 * Field.T * k + 0.5, 2 * Field.T * k + 0.5));
        foreach (var it in w.Items)
            if (!it.Done) dc.FillEllipse(sx + it.X * k, sy + it.Y * k, 2.5, it.Def.Pod ? 0xFFFFA030 : DoorColours[it.Def.Door % DoorColours.Length]);
        dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(sx + (cv.ShipX - 60) * k, sy + cv.ShipY * k - 2, 120 * k, 4));
        var h = w.Hero;
        if ((int)(_clock * 5) % 3 != 0) dc.FillEllipse(sx + h.X * k, sy + h.Y * k, 3, 0xFFFFFF60);
    }

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        var h = w.Hero;
        double y = ViewH;
        dc.FillRectangle(Brush(0xFF14181E), new Rect(0, y, Field.Width, Field.Height - y));
        dc.FillRectangle(Brush(0xFF40A060), new Rect(0, y, Field.Width, 2));
        Text(dc, $"SCORE {g.Score:000000}", new Point(16, y + 26), 28, White);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(16, y + 62), 22, Dim);
        Text(dc, "SHIELDS", new Point(230, y + 24), 20, Dim);
        dc.FillRectangle(Brush(0xFF303840), new Rect(310, y + 14, 160, 18), 3);
        dc.FillRectangle(Brush(h.Energy > 50 ? 0xFF40C060 : h.Energy > 25 ? Orange : Red), new Rect(310, y + 14, 160 * Math.Max(0, h.Energy) / Hero.Energy0, 18), 3);
        Text(dc, "CARRYING", new Point(230, y + 62), 20, Dim);
        dc.FillRectangle(Brush(0xFF303840), new Rect(320, y + 44, 40, 40), 4);
        if (h.Carrying >= 0) DrawItem(dc, w.Items[h.Carrying].Def, 340, y + 64);
        Text(dc, $"PODS {w.Delivered} / {w.Cave.Pods}", new Point(520, y + 26), 28, w.Repaired ? 0xFF60FF80 : 0xFFFFC060);
        Text(dc, w.Cave.Name, new Point(520, y + 62), 22, Dim);
        for (int i = 0; i < Math.Min(6, g.Lives - 1); i++)
        {
            double lx = 800 + i * 32, ly = y + 48;
            dc.FillEllipse(lx, ly - 8, 7, 0xFFE8E8F0);
            dc.FillRectangle(Brush(0xFFE8E8F0), new Rect(lx - 6, ly - 2, 12, 14), 3);
            dc.DrawEllipse(Brush(0xFF2A4A7A), null, new Point(lx + 2, ly - 8), 4, 3);
        }
    }

    // ───────────────────────────── the title and the end ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0E1A), new Rect(0, 0, Field.Width, Field.Height));
        // A cave mouth, and the explorer rising out of it.
        Poly(dc, 0xFF4A3A2E, null, 0, new Point(0, 420), new Point(380, 400), new Point(430, 470), new Point(600, 470), new Point(650, 400), new Point(1024, 420), new Point(1024, 768), new Point(0, 768));
        Poly(dc, 0xFF1A120E, null, 0, new Point(430, 470), new Point(600, 470), new Point(580, 768), new Point(450, 768));
        DrawShip(dc, 210, 360, 2, 6);
        var hero = new Hero { X = cx + Math.Sin(_clock) * 20, Y = 380 + Math.Sin(_clock * 1.3) * 16, Facing = 1, Thrusting = true };
        using (dc.PushTransform(Matrix.CreateScale(1.8, 1.8) * Matrix.CreateTranslation(-hero.X * 0.8, -hero.Y * 0.8)))
            DrawHero(dc, hero);
        DrawItem(dc, new ItemDef(true, -1, 0, 0), 760, 452);
        Text(dc, "AVA EXILE", new Point(cx, 90), 110, 0xFFFFC060, center: true);
        Text(dc, "AFTER PETER IRVIN AND JEREMY SMITH'S 1988 GAME FOR THE BBC MICRO", new Point(cx, 156), 22, Cyan, center: true);
        Text(dc, "YOUR SHIP, THE PERICLES, IS DOWN ON A HOSTILE PLANET WITH ITS POWER PODS SCATTERED IN THE CAVES.", new Point(cx, 532), 20, White, center: true);
        Text(dc, "FLY DOWN WITH YOUR JETPACK, FIND THEM, AND CARRY THEM BACK. KEYS OPEN THE COLOURED DOORS.", new Point(cx, 558), 20, White, center: true);
        Text(dc, "MIND THE GRAVITY, THE DRAUGHTS, THE WATER - AND WHAT LIVES DOWN THERE.", new Point(cx, 584), 20, White, center: true);
        Text(dc, "PRESS ENTER TO START", new Point(cx, 640), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: JETPACK AND THRUST   SPACE: BLASTER   X: PICK UP / PUT DOWN   P: PAUSE   M: SOUND", new Point(cx, 694), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 734), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 240), 110, Red, center: true);
        Text(dc, "THE PERICLES WILL NOT FLY AGAIN", new Point(cx, 330), 34, White, center: true);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 420), 44, Yellow, center: true);
        Text(dc, $"PLANETS {Engine.Cleared}", new Point(cx, 476), 30, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE GREATEST EXPLORERS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  PLANETS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,5}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
