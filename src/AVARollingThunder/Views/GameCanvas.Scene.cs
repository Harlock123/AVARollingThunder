using Avalonia;
using Avalonia.Media;
using AVARollingThunder.Engine;

namespace AVARollingThunder.Views;

/// <summary>The areas: a far background drifting slowly, the buildings with their doors, the walkway, the floor and its pits.</summary>
public sealed partial class GameCanvas
{
    /// <summary>The colours of an area.</summary>
    private readonly record struct Palette(uint SkyTop, uint SkyLow, uint Far, uint Wall, uint WallDark, uint Door, uint Walk, uint Floor, uint Accent);

    private static Palette Colours(Scenery t) => t switch
    {
        Scenery.Docks => new(0xFF10163A, 0xFF6A3A7A, 0xFF1E1838, 0xFF9A4430, 0xFF5E2618, 0xFF2E7A8E, 0xFF6E7080, 0xFF454656, 0xFFFFD040),
        Scenery.Caverns => new(0xFF0E0A12, 0xFF46283A, 0xFF22141E, 0xFF6E5A48, 0xFF46362A, 0xFF8A5226, 0xFF66523E, 0xFF3A2E26, 0xFF5AF0C8),
        Scenery.Street => new(0xFF080A26, 0xFF4A1E66, 0xFF181438, 0xFF5E6E80, 0xFF3A4656, 0xFFC0446A, 0xFF34343E, 0xFF2A2A34, 0xFFFF40C0),
        Scenery.Furnace => new(0xFF240806, 0xFFB04412, 0xFF381208, 0xFF5E4E4C, 0xFF3A2E2E, 0xFF8A7438, 0xFF4E3C34, 0xFF2E2420, 0xFFFFA020),
        _ => new(0xFF12061C, 0xFF46205A, 0xFF22102C, 0xFF4E3E62, 0xFF2E2238, 0xFF962A30, 0xFF7A6638, 0xFF2A2030, 0xFFF0C030),
    };

    private const double Top = 48, Awning = 440, DoorTall = 132;

    /// <summary>The whole area at a camera position: background, buildings, doors, pits, everyone in it, and the railings.</summary>
    private void DrawArea(DrawingContext dc, World w, double cam, bool people = true)
    {
        var s = w.Stage;
        var p = Colours(s.Scenery);
        DrawBackground(dc, s.Scenery, p, cam);
        DrawBuildings(dc, w, p, cam);
        if (s.BossX != null) DrawLeila(dc, w, cam);
        else DrawExit(dc, s, p, cam);
        if (!people) { DrawRailings(dc, s, p, cam); return; }
        foreach (var m in w.Maskers) if (Visible(m.X, cam)) DrawMasker(dc, m, cam);
        foreach (var b in w.Beasts) if (Visible(b.X, cam)) DrawBeast(dc, b, cam);
        if (w.Boss is { } boss && Visible(boss.X, cam)) DrawBoss(dc, boss, cam);
        DrawAgent(dc, w.Agent, cam);
        DrawRailings(dc, s, p, cam);
        DrawFlights(dc, w, cam);
    }

    private static bool Visible(double x, double cam) => x > cam - 150 && x < cam + Field.Width + 150;

    // ───────────────────────────── far away ─────────────────────────────

    private void DrawBackground(DrawingContext dc, Scenery t, Palette p, double cam)
    {
        for (int i = 0; i < 16; i++)
            dc.FillRectangle(Brush(Blend(p.SkyTop, p.SkyLow, i / 15.0)), new Rect(0, Top + i * 26, Field.Width, 27));
        Halftone(dc, p.SkyLow, 0.35, Top + 200, 220, cam * 0.1);
        double far = cam * 0.3;
        switch (t)
        {
            case Scenery.Docks:
            {
                for (int i = 0; i < 40; i++) dc.FillEllipse(Hash(i, 1, 3) % 1024, Top + 10 + Hash(i, 2, 3) % 200, 1.3, 0xC0FFFFFF);
                double mx = 760 - cam * 0.05;
                dc.DrawEllipse(Brush(0xFFF4ECC8), Pen(Ink, 3), new Point(mx, 150), 62, 62);
                dc.FillEllipse(mx + 18, 136, 12, 0x30A08850);
                dc.FillEllipse(mx - 20, 170, 9, 0x30A08850);
                // Cranes along the quay, and the sea.
                dc.FillRectangle(Brush(0xFF1A2A4A), new Rect(0, 380, Field.Width, 60));
                for (int k = (int)Math.Floor(far / 520) - 1; k <= (int)Math.Floor((far + Field.Width) / 520) + 1; k++)
                {
                    double x = k * 520 - far + Hash(k, 4, 3) % 120;
                    dc.FillRectangle(Brush(p.Far), new Rect(x, 150, 16, 290));
                    Poly(dc, p.Far, null, 0, new Point(x - 120, 160), new Point(x + 220, 150), new Point(x + 220, 160), new Point(x - 120, 170));
                    dc.DrawLine(Pen(p.Far, 2), new Point(x + 8, 120), new Point(x - 110, 162));
                    dc.DrawLine(Pen(p.Far, 2), new Point(x + 8, 120), new Point(x + 210, 152));
                    dc.DrawLine(Pen(p.Far, 2), new Point(x + 150, 158), new Point(x + 150, 260));
                    Poly(dc, p.Far, null, 0, new Point(x - 12, 120), new Point(x + 28, 120), new Point(x + 8, 96));
                }
                break;
            }
            case Scenery.Caverns:
            {
                for (int k = (int)Math.Floor(far / 90) - 1; k <= (int)Math.Floor((far + Field.Width) / 90) + 1; k++)
                {
                    double x = k * 90 - far, len = 60 + Hash(k, 1, 7) % 160;
                    Poly(dc, p.Far, null, 0, new Point(x - 30, Top), new Point(x + 34, Top), new Point(x + 6, Top + len));
                    if (Hash(k, 3, 7) % 4 == 0)
                    {
                        double cx = x + 40, cy = 300 + Hash(k, 5, 7) % 100;
                        Poly(dc, p.Accent, Ink, 2, new Point(cx, cy), new Point(cx + 7, cy - 22), new Point(cx + 14, cy));
                        Poly(dc, Darker(p.Accent, 0.7), Ink, 2, new Point(cx + 9, cy), new Point(cx + 18, cy - 14), new Point(cx + 24, cy));
                    }
                }
                break;
            }
            case Scenery.Street:
            {
                for (int k = (int)Math.Floor(far / 110) - 1; k <= (int)Math.Floor((far + Field.Width) / 110) + 1; k++)
                {
                    double x = k * 110 - far, h = 140 + Hash(k, 1, 9) % 220;
                    dc.FillRectangle(Brush(p.Far), new Rect(x, 440 - h, 104, h));
                    for (int r = 0; r < h / 22 - 1; r++)
                    for (int c = 0; c < 4; c++)
                        if (Hash(k * 7 + c, r, 9) % 3 == 0) dc.FillRectangle(Brush(0xFFF0D070), new Rect(x + 10 + c * 24, 440 - h + 14 + r * 22, 10, 9));
                    if (Hash(k, 2, 9) % 5 == 0)
                    {
                        uint neon = Hash(k, 3, 9) % 2 == 0 ? p.Accent : 0xFF40F0FF;
                        dc.DrawRectangle(Brush(0xFF100818), Pen(neon, 3), new Rect(x + 14, 440 - h - 34, 76, 26), 4, 4);
                        Text(dc, Hash(k, 4, 9) % 2 == 0 ? "HOTEL" : "BAR", new Point(x + 52, 440 - h - 21), 24, neon, center: true, shadow: false);
                    }
                }
                break;
            }
            case Scenery.Furnace:
            {
                for (int k = (int)Math.Floor(far / 300) - 1; k <= (int)Math.Floor((far + Field.Width) / 300) + 1; k++)
                {
                    double x = k * 300 - far;
                    dc.FillRectangle(Brush(p.Far), new Rect(x + 40, Top + 40 + Hash(k, 1, 11) % 80, 60, 400));
                    dc.FillEllipse(x + 70, Top + 30 + Hash(k, 1, 11) % 80, 30 + 6 * Math.Sin(_clock * 2 + k), 0x40FF8020);
                    dc.FillRectangle(Brush(Darker(p.Far, 0.8)), new Rect(x - 20, 250 + Hash(k, 2, 11) % 60, 320, 22));
                }
                break;
            }
            default:
            {
                for (int k = (int)Math.Floor(far / 240) - 1; k <= (int)Math.Floor((far + Field.Width) / 240) + 1; k++)
                {
                    double x = k * 240 - far;
                    dc.FillRectangle(Brush(p.Far), new Rect(x, Top, 50, 400));
                    dc.FillRectangle(Brush(Darker(p.Far, 0.7)), new Rect(x - 8, Top + 20, 66, 18));
                    // A Geldra banner, with its eye.
                    double bx = x + 145;
                    Poly(dc, 0xFF7A1A24, Ink, 2.5, new Point(bx - 34, Top + 40), new Point(bx + 34, Top + 40), new Point(bx + 34, Top + 250), new Point(bx, Top + 220), new Point(bx - 34, Top + 250));
                    Emblem(dc, bx, Top + 120, 22);
                }
                break;
            }
        }
    }

    /// <summary>The Geldra's mark: an eye in a ring of spikes.</summary>
    private static void Emblem(DrawingContext dc, double x, double y, double r)
    {
        Star(dc, x, y, r * 1.25, r * 0.85, 0xFFF0C030, Ink, 10);
        dc.DrawEllipse(Brush(0xFF1A0A20), Pen(Ink, 2), new Point(x, y), r * 0.8, r * 0.45);
        dc.FillEllipse(x, y, r * 0.3, 0xFFFF3030);
    }

    // ───────────────────────────── the buildings ─────────────────────────────

    private void DrawBuildings(DrawingContext dc, World w, Palette p, double cam)
    {
        var s = w.Stage;
        // The upper storey's wall, wherever there's a walkway to stand on in front of it.
        foreach (var (from, to) in s.Walkways)
        {
            double x0 = from - cam, x1 = to - cam;
            if (x1 < -20 || x0 > Field.Width + 20) continue;
            dc.FillRectangle(Brush(p.Wall), new Rect(x0, 170, x1 - x0, Field.Walkway - 170));
            Texture(dc, s.Scenery, p, x0, x1, 170, Field.Walkway, cam);
            dc.FillRectangle(Brush(p.WallDark), new Rect(x0 - 6, 156, x1 - x0 + 12, 18));
            dc.DrawRectangle(null, Pen(Ink, 3), new Rect(x0 - 6, 156, x1 - x0 + 12, 18));
        }
        // The lower storey, the whole way along.
        dc.FillRectangle(Brush(p.Wall), new Rect(0, Awning, Field.Width, Field.Floor - Awning));
        Texture(dc, s.Scenery, p, 0, Field.Width, Awning, Field.Floor, cam);
        Halftone(dc, Ink, 0.3, Field.Floor - 90, 90, cam);
        // The awning along its top, or the walkway where there is one.
        Poly(dc, p.WallDark, Ink, 3, new Point(-10, Awning - 16), new Point(Field.Width + 10, Awning - 16), new Point(Field.Width + 10, Awning + 6), new Point(-10, Awning + 6));
        for (int i = 0; i < w.Doors.Count; i++)
            if (Visible(w.Doors[i].X, cam)) DrawDoor(dc, w, i, p, cam);
        foreach (var (from, to) in s.Walkways)
        {
            double x0 = from - cam, x1 = to - cam;
            if (x1 < -20 || x0 > Field.Width + 20) continue;
            dc.DrawRectangle(Brush(p.Walk), Pen(Ink, 3), new Rect(x0, Field.Walkway, x1 - x0, 20));
            dc.DrawLine(Pen(Blend(p.Walk, 0xFFFFFFFF, 0.35), 3), new Point(x0 + 3, Field.Walkway + 4), new Point(x1 - 3, Field.Walkway + 4));
            for (double bx = Math.Ceiling((from - cam) / 160) * 160; bx < x1; bx += 160)
                Poly(dc, p.WallDark, Ink, 2.5, new Point(bx - 8, Field.Walkway + 20), new Point(bx + 8, Field.Walkway + 20), new Point(bx + 2, Awning - 16), new Point(bx - 2, Awning - 16));
        }
        // The floor, and its pits.
        dc.FillRectangle(Brush(p.Floor), new Rect(0, Field.Floor, Field.Width, 50));
        dc.DrawLine(Pen(Blend(p.Floor, 0xFFFFFFFF, 0.3), 3), new Point(0, Field.Floor + 2), new Point(Field.Width, Field.Floor + 2));
        for (double x = -(cam % 80); x < Field.Width; x += 80) dc.DrawLine(Pen(Darker(p.Floor, 0.7), 2), new Point(x, Field.Floor + 6), new Point(x - 14, Field.Floor + 50));
        dc.DrawLine(Pen(Ink, 3), new Point(0, Field.Floor), new Point(Field.Width, Field.Floor));
        foreach (var pit in s.Pits)
            if (Visible(pit.X, cam)) DrawPit(dc, pit, pit.X - cam);
    }

    /// <summary>The surface of a wall: brick, rock, concrete, iron plate or marble.</summary>
    private static void Texture(DrawingContext dc, Scenery t, Palette p, double x0, double x1, double y0, double y1, double cam)
    {
        var line = Pen(p.WallDark, 2);
        using var _ = dc.PushClip(new Rect(x0, y0, x1 - x0, y1 - y0));
        switch (t)
        {
            case Scenery.Docks:
                for (double y = y0 + 6; y < y1; y += 22)
                {
                    dc.DrawLine(line, new Point(x0, y), new Point(x1, y));
                    double off = (int)((y - y0) / 22) % 2 * 24;
                    for (double x = x0 - (cam + off) % 48; x < x1; x += 48) dc.DrawLine(line, new Point(x, y), new Point(x, y + 22));
                }
                break;
            case Scenery.Caverns:
                for (int k = (int)Math.Floor((x0 + cam) / 70); k * 70 - cam < x1; k++)
                for (double y = y0 + 20; y < y1; y += 60)
                {
                    double x = k * 70 - cam + Hash(k, (int)y, 13) % 30;
                    dc.DrawEllipse(Brush(p.WallDark), null, new Point(x, y + Hash(k, (int)y, 14) % 20), 18 + Hash(k, (int)y, 15) % 14, 9);
                }
                break;
            case Scenery.Street:
                for (double x = x0 - (cam % 200); x < x1; x += 200)
                {
                    dc.DrawLine(line, new Point(x, y0), new Point(x, y1));
                    if (y1 - y0 > 150) dc.DrawRectangle(Brush(0xFF2A3644), Pen(Ink, 2), new Rect(x + 120, y0 + 30, 56, 70));
                }
                dc.DrawLine(line, new Point(x0, y0 + 40), new Point(x1, y0 + 40));
                break;
            case Scenery.Furnace:
                for (double x = x0 - (cam % 120); x < x1; x += 120)
                {
                    dc.DrawLine(Pen(p.WallDark, 3), new Point(x, y0), new Point(x, y1));
                    for (double y = y0 + 12; y < y1; y += 30) { dc.FillEllipse(x + 8, y, 2.5, p.WallDark); dc.FillEllipse(x + 112, y, 2.5, p.WallDark); }
                }
                dc.DrawLine(Pen(p.WallDark, 3), new Point(x0, (y0 + y1) / 2), new Point(x1, (y0 + y1) / 2));
                break;
            default:
                for (double x = x0 - (cam % 150); x < x1; x += 150)
                {
                    dc.FillRectangle(Brush(p.WallDark), new Rect(x, y0, 14, y1 - y0));
                    dc.DrawLine(Pen(p.Accent, 2), new Point(x + 14, y0), new Point(x + 14, y1));
                }
                dc.DrawLine(Pen(p.Accent, 3), new Point(x0, y0 + 10), new Point(x1, y0 + 10));
                break;
        }
    }

    /// <summary>A door: shut, swung open as someone comes out (or goes in), or ajar with the agent hiding in it.</summary>
    private void DrawDoor(DrawingContext dc, World w, int i, Palette p, double cam)
    {
        var d = w.Doors[i];
        double x = d.X - cam, feet = d.Feet, top = feet - DoorTall;
        bool hiding = w.Agent.Door == i;
        dc.DrawRectangle(Brush(p.WallDark), Pen(Ink, 3), new Rect(x - 40, top - 10, 80, DoorTall + 10));
        dc.FillRectangle(Brush(0xFF07060A), new Rect(x - 33, top, 66, DoorTall));
        double open = d.Open > 0 ? 1 : hiding ? 0.55 : 0;
        if (hiding)
        {
            // He's in there: a shape in the dark, eyes on the street.
            using (dc.PushClip(new Rect(x - 33, top, 66, DoorTall)))
            using (dc.PushOpacity(0.75))
                Figure(dc, x + 6, feet, w.Agent.Facing, AgentLook with { Coat = Darker(AgentLook.Coat, 0.55), Legs = Darker(AgentLook.Legs, 0.6) }, Pose.Stand, 0, gun: true);
        }
        // The door leaf, hinged on the left, foreshortened as it swings.
        double leaf = 66 * (1 - open * 0.8);
        if (leaf > 2)
        {
            dc.DrawRectangle(Brush(p.Door), Pen(Ink, 3), new Rect(x - 33, top, leaf, DoorTall));
            if (leaf > 30)
            {
                dc.DrawRectangle(null, Pen(Darker(p.Door, 0.7), 3), new Rect(x - 27, top + 8, leaf - 12, 50));
                dc.DrawRectangle(null, Pen(Darker(p.Door, 0.7), 3), new Rect(x - 27, top + 68, leaf - 12, 56));
                dc.FillEllipse(x - 33 + leaf - 9, top + 66, 3.5, p.Accent);
            }
        }
        else dc.DrawLine(Pen(Ink, 3), new Point(x - 33, top), new Point(x - 33, feet));
        // The sign over an ammunition door, dimmed once it's given what it had.
        if (d.Kind != DoorKind.Plain)
        {
            bool arms = d.Kind == DoorKind.Arms;
            uint sign = d.Used ? 0xFF6A6A70 : arms ? 0xFFE83A2A : 0xFFFFD83A;
            dc.DrawRectangle(Brush(sign), Pen(Ink, 3), new Rect(x - 46, top - 40, 92, 26), 4, 4);
            Text(dc, arms ? "ARMS" : "BULLETS", new Point(x, top - 27), 25, d.Used ? 0xFF3A3A40 : arms ? 0xFFFFF4E0 : 0xFF1A1208, center: true, shadow: false);
        }
        else
        {
            dc.FillEllipse(x, top - 18, 6, d.Open > 0 ? 0xFFFF5040 : p.Accent);
            dc.DrawEllipse(null, Pen(Ink, 2), new Point(x, top - 18), 6, 6);
        }
    }

    private void DrawPit(DrawingContext dc, Pit pit, double x)
    {
        double l = x - Pit.Wide / 2;
        switch (pit.Kind)
        {
            case PitKind.Dark:
                dc.FillRectangle(Brush(0xFF000000), new Rect(l, Field.Floor - 1, Pit.Wide, 52));
                break;
            case PitKind.Fire:
                dc.FillRectangle(Brush(0xFF1A0402), new Rect(l, Field.Floor - 1, Pit.Wide, 52));
                dc.FillRectangle(Brush(0xFFC03010), new Rect(l, Field.Floor + 30, Pit.Wide, 21));
                for (int k = 0; k < 4; k++)
                {
                    double fx = l + 8 + k * 16, h = 18 + 10 * Math.Sin(_clock * 11 + k * 1.7 + x * 0.01);
                    Poly(dc, 0xFFFF8A20, null, 0, new Point(fx - 8, Field.Floor + 34), new Point(fx, Field.Floor + 34 - h), new Point(fx + 8, Field.Floor + 34));
                    Poly(dc, 0xFFFFE070, null, 0, new Point(fx - 4, Field.Floor + 34), new Point(fx, Field.Floor + 40 - h), new Point(fx + 4, Field.Floor + 34));
                }
                break;
            default:
                dc.FillRectangle(Brush(0xFF2A0A04), new Rect(l, Field.Floor - 1, Pit.Wide, 14));
                dc.FillRectangle(Brush(0xFFFF6A10), new Rect(l, Field.Floor + 12, Pit.Wide, 39));
                dc.DrawLine(Pen(0xFFFFD040, 3), new Point(l, Field.Floor + 13), new Point(l + Pit.Wide, Field.Floor + 13));
                for (int k = 0; k < 3; k++)
                {
                    double t = (_clock * 0.9 + k * 0.37 + x * 0.001) % 1;
                    dc.DrawEllipse(Brush(0xFFFFC040), Pen(0xFFB03A08, 1.5), new Point(l + 12 + k * 20, Field.Floor + 30 - t * 14), 3 + t * 4, 3 + t * 4);
                }
                break;
        }
        dc.DrawLine(Pen(Ink, 3), new Point(l, Field.Floor), new Point(l, Field.Floor + 50));
        dc.DrawLine(Pen(Ink, 3), new Point(l + Pit.Wide, Field.Floor), new Point(l + Pit.Wide, Field.Floor + 50));
    }

    /// <summary>The railings along the walkway, drawn in front of whoever is up there.</summary>
    private static void DrawRailings(DrawingContext dc, Stage s, Palette p, double cam)
    {
        foreach (var (from, to) in s.Walkways)
        {
            double x0 = from - cam, x1 = to - cam;
            if (x1 < -20 || x0 > Field.Width + 20) continue;
            for (double x = Math.Ceiling(from / 40) * 40 - cam; x <= x1; x += 40)
                if (x >= x0) Limb(dc, new Point(x, Field.Walkway), new Point(x, Field.Walkway - 34), 3, p.Walk);
            Limb(dc, new Point(x0, Field.Walkway - 36), new Point(x1, Field.Walkway - 36), 5, Blend(p.Walk, 0xFFFFFFFF, 0.25));
        }
    }

    /// <summary>The way out at the end of an area: a lit gateway.</summary>
    private void DrawExit(DrawingContext dc, Stage s, Palette p, double cam)
    {
        double x = s.Exit - cam;
        if (x < -150 || x > Field.Width + 150) return;
        dc.DrawRectangle(Brush(p.WallDark), Pen(Ink, 3), new Rect(x - 20, Field.Floor - 200, 140, 200));
        dc.FillRectangle(Brush(0xFFFFF2B0), new Rect(x - 4, Field.Floor - 180, 108, 180));
        dc.FillRectangle(Brush(0x60FFE060), new Rect(x - 40, Field.Floor - 220, 180, 220));
        dc.DrawRectangle(Brush(0xFF1A9A3A), Pen(Ink, 3), new Rect(x + 6, Field.Floor - 250, 88, 34), 4, 4);
        Text(dc, "EXIT", new Point(x + 50, Field.Floor - 233), 34, 0xFFFFFFFF, center: true, shadow: false);
        if ((int)(_clock * 3) % 2 == 0) Poly(dc, p.Accent, Ink, 3, new Point(x - 70, Field.Floor - 120), new Point(x - 40, Field.Floor - 100), new Point(x - 70, Field.Floor - 80));
    }
}
