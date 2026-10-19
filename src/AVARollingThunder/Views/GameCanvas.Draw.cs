using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARollingThunder.Engine;

namespace AVARollingThunder.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Dictionary<double, Geometry> DotCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARollingThunder/Assets/Fonts#VT323"));
    private static readonly Typeface Bold = new(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold);

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint Ink = 0xFF0C0A10, White = 0xFFF6F2E8, Yellow = 0xFFFFDC3A, Red = 0xFFE8382A, Dim = 0xFF8A94B8, Cyan = 0xFF6AE8FF, Paper = 0xFFFFF6D8;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    /// <summary>Comic lettering: heavy, leaning, outlined in black with a drop shadow.</summary>
    private static void Comic(DrawingContext dc, string text, Point at, double size, uint fill, bool center = false, double outline = 0, uint shadow = Ink, double lean = 0.2)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, Brush(fill));
        if (ft.BuildGeometry(new Point(0, 0)) is not { } g) return;
        double w = ft.Width, h = ft.Height;
        double x = center ? at.X - w / 2 : at.X;
        if (outline <= 0) outline = Math.Max(3, size / 9);
        var m = Matrix.CreateTranslation(0, -h / 2) * Matrix.CreateSkew(-lean, 0) * Matrix.CreateTranslation(x, at.Y);
        using (dc.PushTransform(Matrix.CreateTranslation(outline * 0.8, outline * 0.8) * m)) dc.DrawGeometry(Brush(shadow), Pen(shadow, outline * 2), g);
        using (dc.PushTransform(m))
        {
            dc.DrawGeometry(null, Pen(Ink, outline * 2), g);
            dc.DrawGeometry(Brush(fill), null, g);
        }
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

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>
    /// Ben-Day dots: a band of dots growing from nothing at the top to full at the bottom, the printed-comic shading.
    /// Built once for each height and moved into place.
    /// </summary>
    private static void Halftone(DrawingContext dc, uint colour, double alpha, double y, double height, double scroll)
    {
        const double gap = 12;
        if (!DotCache.TryGetValue(height, out var g))
        {
            var sg = new StreamGeometry();
            using (var c = sg.Open())
            {
                for (int row = 0; row * gap < height; row++)
                for (int col = -1; col * gap < Field.Width + gap * 2; col++)
                {
                    double r = gap * 0.5 * (row * gap / height);
                    if (r < 0.6) continue;
                    double cx = col * gap + (row % 2) * gap / 2, cy = row * gap;
                    c.BeginFigure(new Point(cx + r, cy), true);
                    c.ArcTo(new Point(cx - r, cy), new Size(r, r), 0, false, SweepDirection.Clockwise);
                    c.ArcTo(new Point(cx + r, cy), new Size(r, r), 0, false, SweepDirection.Clockwise);
                    c.EndFigure(true);
                }
            }
            DotCache[height] = g = sg;
        }
        using var _ = dc.PushTransform(Matrix.CreateTranslation(-(scroll % gap) - gap, y));
        dc.DrawGeometry(Brush(Alpha(colour, alpha)), null, g);
    }

    /// <summary>A speech balloon, its tail pointing down to the speaker.</summary>
    private static void Balloon(DrawingContext dc, double x, double y, string text, double size, bool tailLeft)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, Brush(Ink));
        double w = ft.Width + 36, h = ft.Height + 20;
        double tx = tailLeft ? x - w / 2 + 24 : x + w / 2 - 24;
        Poly(dc, 0xFFFFFFFF, Ink, 3, new Point(tx - 10, y + h / 2 - 6), new Point(tx + (tailLeft ? -18 : 18), y + h / 2 + 26), new Point(tx + 10, y + h / 2 - 6));
        dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(Ink, 3), new Point(x, y), w / 2, h / 2);
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(tx - 8, y + h / 2 - 9, 16, 6));
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }

    /// <summary>A caption box, the yellow kind a comic's narrator speaks from.</summary>
    private static void Caption(DrawingContext dc, Rect r, string text, double size, uint fill = Yellow)
    {
        dc.FillRectangle(Brush(Ink), r.Translate(new Vector(5, 5)));
        dc.DrawRectangle(Brush(fill), Pen(Ink, 3), r);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, size, Brush(Ink));
        dc.DrawText(ft, new Point(r.Center.X - ft.Width / 2, r.Center.Y - ft.Height / 2));
    }

    /// <summary>Lines bursting out from a point: the background of an action panel.</summary>
    private static void Burst(DrawingContext dc, double x, double y, uint a, uint b, int rays = 28, double turn = 0)
    {
        dc.FillRectangle(Brush(a), new Rect(0, 0, Field.Width, Field.Height));
        for (int i = 0; i < rays; i += 2)
        {
            double a0 = turn + i * Math.PI * 2 / rays, a1 = turn + (i + 1) * Math.PI * 2 / rays;
            Poly(dc, b, null, 0, new Point(x, y), new Point(x + 1600 * Math.Cos(a0), y + 1600 * Math.Sin(a0)), new Point(x + 1600 * Math.Cos(a1), y + 1600 * Math.Sin(a1)));
        }
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                var w = g.World;
                if (g.Phase == Phase.Start) { DrawCard(dc, w); break; }
                DrawArea(dc, w, w.Camera);
                DrawHud(dc, w);
                if (g.Phase == Phase.Cleared) DrawCleared(dc, w);
                if (g.Phase == Phase.Rescued) DrawRescued(dc, w);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(0, 0, Field.Width, Field.Height));
            Comic(dc, "PAUSED", new Point(512, 320), 90, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 410), 30, White, center: true);
        }
        if (g.State == GameState.Attract && g.Phase != Phase.Start)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 66), 26, Yellow, center: true);
    }

    // ───────────────────────────── the score, the gun, the life meter, the clock ─────────────────────────────

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        var a = w.Agent;
        dc.FillRectangle(Brush(Ink), new Rect(0, 0, Field.Width, Top));
        Text(dc, "1UP", new Point(20, 24), 34, Red);
        Text(dc, $"{g.Score:000000}", new Point(80, 24), 34, White);
        Text(dc, "HI", new Point(420, 24), 34, Red);
        Text(dc, $"{Math.Max(g.Score, g.HighScores.TopScore):000000}", new Point(460, 24), 34, White);
        Text(dc, $"STORY {w.Story}  AREA {w.Area}", new Point(1004, 24), 34, Yellow, right: true);
        if (w.Boss is { Mood: not BossMood.Asleep } b)
        {
            dc.DrawRectangle(Brush(0xD0000000), Pen(Ink, 2), new Rect(312, 56, 400, 30), 4, 4);
            Text(dc, "MABOO", new Point(322, 71), 28, 0xFFF0C030);
            dc.FillRectangle(Brush(0xFF3A1010), new Rect(400, 63, 300, 16));
            dc.FillRectangle(Brush(Red), new Rect(400, 63, 300.0 * Math.Max(0, b.Hp) / b.MaxHp, 16));
        }
        double top = Field.Floor + 50;
        dc.FillRectangle(Brush(Ink), new Rect(0, top, Field.Width, Field.Height - top));
        dc.DrawLine(Pen(0xFF3A3448, 2), new Point(0, top + 1), new Point(Field.Width, top + 1));
        double mid = top + 39;
        // The gun and what's left for it.
        bool mg = a.Gun == Gun.MachineGun;
        dc.DrawRectangle(Brush(0xFF8A8A9A), Pen(0xFFD0D0E0, 2), new Rect(24, mid - 9, mg ? 46 : 32, 10), 2, 2);
        dc.DrawRectangle(Brush(0xFF8A8A9A), Pen(0xFFD0D0E0, 2), new Rect(28, mid - 1, 8, 13), 2, 2);
        if (mg) dc.DrawRectangle(Brush(0xFF8A8A9A), Pen(0xFFD0D0E0, 2), new Rect(48, mid, 7, 12), 2, 2);
        if (mg) Text(dc, $"MACHINE GUN {a.Rounds:00}", new Point(84, mid), 36, Red);
        else if (a.Bullets > 0) Text(dc, $"BULLET {a.Bullets:00}", new Point(84, mid), 36, a.Bullets <= 10 ? Yellow : White);
        else Text(dc, "BULLET -- EMPTY", new Point(84, mid), 36, (int)(_clock * 3) % 2 == 0 ? Red : Dim);
        // The life meter: two parts.
        Text(dc, "LIFE", new Point(400, mid), 36, Dim);
        for (int i = 0; i < Sim.Life0; i++)
        {
            bool on = i < a.Life;
            dc.DrawRectangle(Brush(on ? (a.Life == 1 ? Yellow : 0xFF4AE070) : 0xFF2A2A30), Pen(0xFFD0D0E0, 2), new Rect(462 + i * 56, mid - 12, 50, 24), 3, 3);
        }
        bool hurry = w.TimeLeft <= Sim.WarnAt;
        Text(dc, $"TIME {Math.Ceiling(w.TimeLeft):000}", new Point(640, mid), 36, hurry ? ((int)(_clock * 4) % 2 == 0 ? Red : Yellow) : White);
        for (int i = 0; i < Math.Min(g.Lives - 1, 5); i++)
        {
            double hx = 1000 - i * 34;
            dc.DrawEllipse(Brush(AgentLook.Skin), Pen(Ink, 2), new Point(hx, mid + 2), 11, 12);
            Poly(dc, AgentLook.Head, Ink, 2, new Point(hx - 12, mid + 4), new Point(hx - 11, mid - 8), new Point(hx, mid - 13), new Point(hx + 11, mid - 8), new Point(hx + 2, mid - 5));
        }
    }

    // ───────────────────────────── the area's card, the clear, the rescue ─────────────────────────────

    /// <summary>The card before each area: a comic panel with the story, the area and its name.</summary>
    private void DrawCard(DrawingContext dc, World w)
    {
        var s = w.Stage;
        var p = Colours(s.Scenery);
        double k = Math.Min(1, Engine.PhaseTime / 0.35);
        Burst(dc, 760, 420, Blend(p.SkyLow, Ink, 0.15), Blend(p.SkyLow, p.Accent, 0.3), 32, _clock * 0.05);
        Halftone(dc, Ink, 0.35, Field.Height - 300, 300, 0);
        if (s.Scenery == Scenery.Headquarters) Emblem(dc, 770, 330, 120);
        // The agent, gun up, ready to go in.
        Figure(dc, 780, 690, -1, AgentLook, Pose.Stand, 0, gun: true, scale: 3.2, firing: (int)(_clock * 2.5) % 3 == 0);
        // The panel's frame and gutter.
        dc.DrawRectangle(null, Pen(0xFFFFFFFF, 26), new Rect(0, 0, Field.Width, Field.Height));
        dc.DrawRectangle(null, Pen(Ink, 6), new Rect(16, 16, Field.Width - 32, Field.Height - 32));
        Caption(dc, new Rect(46, 50, 210, 52), $"STORY {w.Story}", 34);
        double slide = (1 - k) * -600;
        Comic(dc, "AREA", new Point(80 + slide, 220), 96, White, shadow: Red);
        Comic(dc, w.Area.ToString(), new Point(400 + slide, 250), 220, Yellow, shadow: Red, outline: 14);
        Caption(dc, new Rect(60, 400, 520, 70), s.Name, 40, Paper);
        string job = s.BossX != null ? "STOP MABOO.  FREE LEILA." : "REACH THE EXIT.";
        Text(dc, $"TIME {s.Time * (w.Hard ? 0.9 : 1):0}   {job}", new Point(70, 510), 36, White);
        if (w.Hard) Text(dc, "THE GELDRA ARE BACK - AND STRONGER.", new Point(70, 550), 32, Yellow);
        if (Engine.StartFor < GameEngine.StartSeconds) Text(dc, $"AGENTS LEFT {Engine.Lives}", new Point(70, 590), 32, Cyan);
        if (s.BossX != null && (int)(_clock * 2) % 2 == 0) Balloon(dc, 640, 120, "HELP, ALBATROSS!", 34, tailLeft: false);
        if (Engine.State == GameState.Attract) Text(dc, "DEMONSTRATION", new Point(946, 712), 28, Yellow, right: true);
    }

    private void DrawCleared(DrawingContext dc, World w)
    {
        Caption(dc, new Rect(262, 150, 500, 80), $"AREA {w.Area} CLEAR!", 50);
        if (Engine.PhaseTime > 0.8)
        {
            dc.DrawRectangle(Brush(0xE0101018), Pen(Ink, 3), new Rect(312, 250, 400, 60), 6, 6);
            Text(dc, $"TIME BONUS {Math.Ceiling(w.TimeLeft):0} x {Sim.TimePoints}", new Point(512, 280), 34, White, center: true);
        }
    }

    private void DrawRescued(DrawingContext dc, World w)
    {
        double t = Engine.PhaseTime;
        if (t > 3) Caption(dc, new Rect(232, 110, 560, 76), "LEILA IS FREE!", 48);
        if (t > 4.5)
        {
            string next = w.Story < GameEngine.Stories ? "STORY 2 BEGINS... THE GELDRA RETURN" : "MISSION COMPLETE";
            dc.DrawRectangle(Brush(0xE0101018), Pen(Ink, 3), new Rect(182, 210, 660, 60), 6, 6);
            Text(dc, next, new Point(512, 240), 36, Yellow, center: true);
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Burst(dc, cx, 250, 0xFF1A0E3A, 0xFF2A1450, 36, _clock * 0.04);
        Halftone(dc, 0xFF6A3AA0, 0.35, 150, 300, 0);
        dc.FillRectangle(Brush(0xFF22202C), new Rect(0, 600, Field.Width, 168));
        dc.DrawLine(Pen(Ink, 4), new Point(0, 600), new Point(Field.Width, 600));
        Halftone(dc, Ink, 0.5, 600, 168, 0);
        Text(dc, "A V A", new Point(cx, 44), 40, Yellow, center: true);
        Comic(dc, "ROLLING", new Point(cx - 40, 130), 116, Yellow, center: true, shadow: Red);
        Comic(dc, "THUNDER", new Point(cx + 20, 248), 116, Red, center: true, shadow: Yellow);
        // The bolt.
        Poly(dc, 0xFFFFFFFF, Ink, 4, new Point(cx + 330, 70), new Point(cx + 276, 180), new Point(cx + 308, 180), new Point(cx + 264, 300), new Point(cx + 362, 156),
            new Point(cx + 326, 156), new Point(cx + 368, 70));
        // The agent and two maskers, squaring up across the title.
        Figure(dc, 912, 690, -1, AgentLook, Pose.Stand, 0, gun: true, scale: 2.3, firing: (int)(_clock * 2) % 4 == 0);
        Figure(dc, 226, 690, 1, MaskerLook(Hood.Blue), Pose.Stand, 0, gun: true, scale: 1.45);
        Figure(dc, 96, 700, 1, MaskerLook(Hood.Red), Pose.Crouch, 0, gun: true, scale: 2.1);
        dc.DrawRectangle(Brush(0xE8100C18), Pen(Ink, 3), new Rect(cx - 300, 330, 600, 160), 8, 8);
        Text(dc, "THE SECRET SOCIETY GELDRA HAS TAKEN", new Point(cx, 352), 28, White, center: true);
        Text(dc, "AGENT LEILA. ALBATROSS OF THE WCPO'S", new Point(cx, 380), 28, White, center: true);
        Text(dc, "ROLLING THUNDER UNIT GOES IN AFTER HER:", new Point(cx, 408), 28, White, center: true);
        Text(dc, "FIVE AREAS, DOOR BY DOOR, TO MABOO.", new Point(cx, 436), 28, White, center: true);
        Text(dc, "HIDE IN DOORS. FIND BULLETS AND ARMS.", new Point(cx, 468), 28, Cyan, center: true);
        if ((int)(_clock * 2) % 2 == 0) Comic(dc, "PRESS ENTER TO START", new Point(cx + 20, 540), 38, White, center: true, lean: 0.12);
        dc.FillRectangle(Brush(0xE0000000), new Rect(0, 702, Field.Width, 66));
        Text(dc, "ARROWS: WALK, DOOR (UP), CROUCH (DOWN)   Z: FIRE   X: JUMP (+UP/DOWN: CHANGE FLOOR)   P: PAUSE", new Point(cx, 718), 21, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: IN THE HALL OF THE MOUNTAIN KING (GRIEG, 1875) AND A SPY TUNE OF ITS OWN", new Point(cx, 746), 20, Dim, center: true);
    }

    /// <summary>The first area, empty, drifting by behind the score tables.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        var w = Engine.World;
        double cam = 400 + _clock * 30 % Math.Max(1, w.Stage.Length - 1600);
        DrawArea(dc, w, cam, people: false);
        dc.FillRectangle(Brush(Ink), new Rect(0, 0, Field.Width, Top));
        dc.FillRectangle(Brush(Ink), new Rect(0, Field.Floor + 50, Field.Width, Field.Height - Field.Floor - 50));
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        if (g.Completed)
        {
            Burst(dc, cx, 520, 0xFF2A6AB0, 0xFF4A8AD0, 30, _clock * 0.08);
            Halftone(dc, Ink, 0.3, 468, 300, 0);
            Figure(dc, cx - 120, 740, 1, AgentLook, Pose.Stand, 0, gun: false, scale: 2.3);
            Figure(dc, cx + 120, 740, -1, LeilaLook, Pose.Stand, 0, gun: false, scale: 2.3);
            Comic(dc, "MISSION COMPLETE", new Point(cx, 120), 84, Yellow, center: true, shadow: Red);
            Balloon(dc, cx + 250, 300, "THANK YOU, ALBATROSS!", 30, tailLeft: true);
        }
        else
        {
            Burst(dc, cx, 520, 0xFF3A0A14, 0xFF5A1424, 30, _clock * 0.08);
            Halftone(dc, Ink, 0.4, 468, 300, 0);
            var maboo = new Boss { X = cx + 180, Facing = -1, Mood = BossMood.Stride };
            using (dc.PushTransform(Matrix.CreateTranslation(-cx - 180, -Field.Floor) * Matrix.CreateScale(2.2, 2.2) * Matrix.CreateTranslation(cx + 200, 740)))
                DrawBoss(dc, maboo, 0);
            Balloon(dc, cx - 160, 300, "HA HA HA HA!", 40, tailLeft: false);
            Comic(dc, "GAME OVER", new Point(cx, 120), 110, Red, center: true, shadow: Ink);
        }
        dc.DrawRectangle(Brush(0xE0100C18), Pen(Ink, 3), new Rect(60, 420, 420, 150), 8, 8);
        Text(dc, $"SCORE {g.Score}", new Point(270, 456), 44, White, center: true);
        Text(dc, $"STORY {g.World.Story}  AREA {g.World.Area}", new Point(270, 500), 34, Yellow, center: true);
        string cause = g.Completed ? "MABOO BEATEN TWICE" : g.World.Cause switch
        {
            "time" => "OUT OF TIME",
            "a bullet" => "SHOT DOWN",
            "a pit" => "FELL INTO A PIT",
            "" => "",
            var c => $"BROUGHT DOWN BY {c.ToUpperInvariant()}",
        };
        Text(dc, cause, new Point(270, 540), 30, Cyan, center: true);
    }

    private static string AreaName(int reached) => $"{(reached - 1) / Stage.Areas + 1}-{(reached - 1) % Stage.Areas + 1}";

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(Ink), new Rect(cx - 336, 76, 672, 636));
        dc.DrawRectangle(Brush(0xF0141020), Pen(0xFFFFFFFF, 4), new Rect(cx - 340, 70, 672, 636));
        Comic(dc, "WCPO HONOUR ROLL", new Point(cx, 124), 52, Yellow, center: true, shadow: Red);
        Text(dc, "RANK  NAME    SCORE   AREA", new Point(cx, 192), 32, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,3}.  {e.Initials}  {e.Score,8}    {AreaName(e.Stage)}", new Point(cx, 238 + i * 42), 36, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 676), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
        dc.DrawRectangle(Brush(0xF0141020), Pen(0xFFFFFFFF, 4), new Rect(cx - 330, 140, 660, 460));
        Comic(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 56, Yellow, center: true, shadow: Red);
        Text(dc, g.Score.ToString(), new Point(cx, 274), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS, AGENT", new Point(cx, 350), 34, Dim, center: true);
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
