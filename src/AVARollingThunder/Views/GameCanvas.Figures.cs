using Avalonia;
using Avalonia.Media;
using AVARollingThunder.Engine;

namespace AVARollingThunder.Views;

/// <summary>The people and creatures: drawn in flat colour with heavy black outlines, like a comic of the time.</summary>
public sealed partial class GameCanvas
{
    private enum Pose { Stand, Walk, Crouch, Air }

    /// <summary>How a figure looks: coat, trousers, skin, and the head (0 the agent's hair, 1 a masker's hood, 2 Leila's long hair).</summary>
    private readonly record struct Look(uint Coat, uint Legs, uint Skin, uint Head, int Style, uint Trim);

    private static readonly Look AgentLook = new(0xFF2F7FD0, 0xFF27324A, 0xFFF0C090, 0xFF3A2414, 0, 0xFFE03A3A);
    private static readonly Look LeilaLook = new(0xFF22A878, 0xFF1C6A50, 0xFFF4C8A0, 0xFFB04A20, 2, 0xFFF0E070);

    private static uint HoodColour(Hood h) => h switch
    {
        Hood.Blue => 0xFF3A6AE8, Hood.Green => 0xFF36B04C, Hood.Yellow => 0xFFF0C030, _ => 0xFFE03434,
    };

    private static Look MaskerLook(Hood h) => new(HoodColour(h), 0xFF1C1C26, 0, HoodColour(h), 1, 0xFF101014);

    private static uint Darker(uint c, double k = 0.6) => Blend(0xFF000000, c, k);

    /// <summary>A limb or a thick line with a black outline round it.</summary>
    private static void Limb(DrawingContext dc, Point a, Point b, double width, uint colour)
    {
        dc.DrawLine(Pen(Ink, width + 5), a, b);
        dc.DrawLine(Pen(colour, width), a, b);
    }

    private static Point Leg(Point hip, double thigh, double shin, out Point knee)
    {
        knee = new Point(hip.X + 27 * Math.Sin(thigh), hip.Y + 27 * Math.Cos(thigh));
        return new Point(knee.X + 26 * Math.Sin(shin), knee.Y + 26 * Math.Cos(shin));
    }

    /// <summary>
    /// A figure, feet at (x, y), facing d, drawn facing right in its own coordinates and mirrored to face left. A fall
    /// tips it over backwards about the feet.
    /// </summary>
    private void Figure(DrawingContext dc, double x, double y, int d, Look look, Pose pose, double phase, bool gun, double scale = 1,
        double fall = 0, bool flash = false, bool firing = false)
    {
        var m = Matrix.CreateRotation(-fall * Math.PI / 2) * Matrix.CreateTranslation(0, -fall * 16) * Matrix.CreateScale(scale * d, scale) * Matrix.CreateTranslation(x, y);
        using var _ = dc.PushTransform(m);
        bool crouch = pose == Pose.Crouch;
        double hipY = crouch ? -30 : pose == Pose.Air ? -60 : -52;
        var hip = new Point(0, hipY);
        // The legs: the far one first.
        double t1, s1, t2, s2;
        switch (pose)
        {
            case Pose.Walk:
                t1 = 0.5 * Math.Sin(phase);
                t2 = -t1;
                s1 = t1 - 0.5 * Math.Max(0, Math.Cos(phase));
                s2 = t2 - 0.5 * Math.Max(0, -Math.Cos(phase));
                break;
            case Pose.Crouch: t1 = 1.45; s1 = 0; t2 = -0.2; s2 = -1.5; break;
            case Pose.Air: t1 = 1.0; s1 = -0.2; t2 = 0.25; s2 = -0.8; break;
            default: t1 = 0.14; s1 = 0.08; t2 = -0.14; s2 = -0.08; break;
        }
        var footB = Leg(hip, t2, s2, out var kneeB);
        var footF = Leg(hip, t1, s1, out var kneeF);
        uint legsFar = Darker(look.Legs, 0.75);
        Limb(dc, hip, kneeB, 11, legsFar);
        Limb(dc, kneeB, footB, 10, legsFar);
        Limb(dc, footB, new Point(footB.X + 9, footB.Y), 7, Ink);
        Limb(dc, hip, kneeF, 12, look.Legs);
        Limb(dc, kneeF, footF, 10, look.Legs);
        Limb(dc, footF, new Point(footF.X + 10, footF.Y), 7, Ink);
        // The body.
        double shY = crouch ? -60 : hipY - 38, lean = crouch ? 6 : 2;
        var sh = new Point(lean + 2, shY);
        uint coat = flash ? 0xFFFFFFFF : look.Coat;
        Poly(dc, coat, Ink, 3, new Point(-12, hipY + 6), new Point(13, hipY + 6), new Point(sh.X + 14, shY), new Point(sh.X - 14, shY));
        if (look.Style == 0)
        {
            // Albatross: the open collar and the red scarf.
            Poly(dc, 0xFFF2EEE0, Ink, 2, new Point(sh.X - 3, shY), new Point(sh.X + 9, shY), new Point(sh.X + 4, shY + 16));
            Limb(dc, new Point(sh.X - 6, shY + 1), new Point(sh.X - 16, shY + 12), 4, look.Trim);
        }
        else if (look.Style == 1) dc.DrawLine(Pen(look.Trim, 5), new Point(-12, hipY + 2), new Point(13, hipY + 2));
        else
        {
            dc.DrawLine(Pen(look.Trim, 4), new Point(-11, hipY + 2), new Point(12, hipY + 2));
            Poly(dc, coat, Ink, 3, new Point(-13, hipY + 6), new Point(14, hipY + 6), new Point(18, hipY + 22), new Point(-17, hipY + 22));
        }
        // The head.
        var head = new Point(sh.X + 2, shY - 12);
        if (look.Style == 1)
        {
            // A masker's cowl: a close hood falling to the shoulders, a tail at the back, and the goggles.
            uint hood = flash ? 0xFFFFFFFF : look.Head;
            Poly(dc, hood, Ink, 3, new Point(head.X - 15, head.Y + 2), new Point(head.X + 12, head.Y + 2), new Point(head.X + 17, head.Y + 19), new Point(head.X - 19, head.Y + 19));
            Poly(dc, hood, Ink, 2.5, new Point(head.X - 9, head.Y - 8), new Point(head.X - 27, head.Y + 6), new Point(head.X - 11, head.Y + 8));
            dc.DrawEllipse(Brush(hood), Pen(Ink, 3), head, 13, 14);
            dc.DrawLine(Pen(Darker(hood, 0.7), 3), new Point(head.X - 4, head.Y - 13), new Point(head.X - 10, head.Y + 8));
            dc.DrawRectangle(Brush(Ink), null, new Rect(head.X - 1, head.Y - 6, 16, 9), 4, 4);
            dc.DrawEllipse(Brush(0xFFF6F2E8), Pen(Ink, 1.5), new Point(head.X + 4, head.Y - 1.5), 3.6, 3.6);
            dc.DrawEllipse(Brush(0xFFF6F2E8), Pen(Ink, 1.5), new Point(head.X + 11, head.Y - 1.5), 3.4, 3.4);
            dc.FillEllipse(head.X + 5, head.Y - 1.5, 1.4, 0xFFD02020);
            dc.FillEllipse(head.X + 12, head.Y - 1.5, 1.3, 0xFFD02020);
        }
        else
        {
            if (look.Style == 2)
                Poly(dc, look.Head, Ink, 3, new Point(head.X - 12, head.Y - 8), new Point(head.X + 6, head.Y - 13), new Point(head.X - 4, head.Y + 30),
                    new Point(head.X - 16, head.Y + 40), new Point(head.X - 18, head.Y + 6));
            dc.DrawEllipse(Brush(look.Skin), Pen(Ink, 3), head, 11, 12);
            // Hair over the top and back of the head.
            Poly(dc, look.Head, Ink, 2.5, new Point(head.X - 12, head.Y + 4), new Point(head.X - 11, head.Y - 9), new Point(head.X - 2, head.Y - 15),
                new Point(head.X + 10, head.Y - 11), new Point(head.X + 12, head.Y - 4), new Point(head.X + 2, head.Y - 6), new Point(head.X - 4, head.Y + 2));
            dc.FillEllipse(head.X + 6, head.Y - 1, 1.8, Ink);
            dc.DrawLine(Pen(Darker(look.Skin, 0.7), 1.5), new Point(head.X + 4, head.Y + 6), new Point(head.X + 8, head.Y + 6));
        }
        // The arms: the gun held out at the height its bullets fly.
        var shoulder = new Point(sh.X + 2, shY + 6);
        uint sleeve = flash ? 0xFFFFFFFF : look.Coat, hand = look.Style == 1 ? Ink : look.Skin;
        if (gun)
        {
            double handY = crouch ? -Sim.ShotCrouch : -Sim.ShotHigh;
            var grip = new Point(shoulder.X + 26, handY + 2);
            Limb(dc, shoulder, new Point(shoulder.X + 12, shoulder.Y + 14), 8, Darker(sleeve, 0.8));
            Limb(dc, new Point(shoulder.X + 12, shoulder.Y + 14), new Point(grip.X - 4, grip.Y + 2), 7, Darker(sleeve, 0.8));
            Limb(dc, shoulder, grip, 9, sleeve);
            dc.DrawRectangle(Brush(0xFF2A2A30), Pen(Ink, 2), new Rect(grip.X, handY - 4, 20, 7));
            dc.DrawRectangle(Brush(0xFF2A2A30), Pen(Ink, 2), new Rect(grip.X + 1, handY + 1, 6, 9));
            dc.FillEllipse(grip.X, grip.Y, 5, hand);
            if (firing) Star(dc, grip.X + 28, handY - 1, 13, 6, 0xFFFFF070, 0xFFFF8020);
        }
        else
        {
            double swing = pose == Pose.Walk ? Math.Sin(phase) * 0.6 : pose == Pose.Air ? -1.6 : 0.2;
            var elbow = new Point(shoulder.X + 20 * Math.Sin(swing), shoulder.Y + 20 * Math.Cos(swing));
            var wrist = new Point(elbow.X + 18 * Math.Sin(swing + 0.5), elbow.Y + 18 * Math.Cos(swing + 0.5));
            Limb(dc, shoulder, elbow, 9, sleeve);
            Limb(dc, elbow, wrist, 8, sleeve);
            dc.FillEllipse(wrist.X, wrist.Y, 5, hand);
        }
    }

    /// <summary>A comic starburst: a muzzle flash, a hit, a blast.</summary>
    private static void Star(DrawingContext dc, double x, double y, double outer, double inner, uint fill, uint? edge, int points = 8, double turn = 0)
    {
        var pts = new Point[points * 2];
        for (int i = 0; i < pts.Length; i++)
        {
            double r = i % 2 == 0 ? outer : inner, a = turn + Math.PI * i / points;
            pts[i] = new Point(x + r * Math.Cos(a), y + r * Math.Sin(a));
        }
        Poly(dc, fill, edge, 2.5, pts);
    }

    private void DrawAgent(DrawingContext dc, Agent a, double cam)
    {
        if (a.Hidden) return;
        // After a touch he flickers while nothing can touch him again.
        if (a.Hurt > 0 && !a.Dead && (int)(_clock * 16) % 2 == 0) return;
        var pose = !a.Ground ? Pose.Air : a.Crouch ? Pose.Crouch : a.Moving ? Pose.Walk : Pose.Stand;
        double fall = a.Dead ? Math.Min(1, a.DeadT * 2.4) : 0;
        Figure(dc, a.X - cam, a.Y, a.Facing, AgentLook, pose, a.Walk * 10, gun: !Engine.World.Rescued, fall: fall, firing: a.Flash > 0);
    }

    private void DrawMasker(DrawingContext dc, Masker m, double cam)
    {
        double x = m.X - cam;
        var look = MaskerLook(m.Hood);
        if (m.Mood == Mood.Down)
        {
            using var o = dc.PushOpacity(Math.Clamp(1.4 - m.T * 2, 0, 1));
            Figure(dc, x, m.Y, m.Facing, look, Pose.Stand, 0, gun: false, fall: Math.Min(1, m.T * 3), flash: m.T < 0.1);
            return;
        }
        var pose = m.Mood switch
        {
            Mood.Leap => Pose.Air,
            Mood.Aim when m.Low => Pose.Crouch,
            Mood.Walk => Pose.Walk,
            _ => Pose.Stand,
        };
        bool gun = m.Hood != Hood.Green || m.Mood != Mood.Aim;
        Figure(dc, x, m.Y, m.Facing, look, pose, m.T * 9 + m.X * 0.02, gun: gun && m.Hood != Hood.Green, flash: m.Flash > 0,
            firing: m.Mood == Mood.Aim && m.T > Sim.AimTime(Engine.World) - 0.08);
        if (m.Hood == Hood.Green)
        {
            // A grenade in the raised hand while he winds up to throw.
            double k = m.Mood == Mood.Aim ? m.T / Sim.AimTime(Engine.World) : 0;
            dc.DrawEllipse(Brush(0xFF3A5A2A), Pen(Ink, 2.5), new Point(x - m.Facing * (6 + 10 * k), m.Y - 100 - 14 * k), 6, 6);
        }
    }

    private void DrawBeast(DrawingContext dc, Beast b, double cam)
    {
        double x = b.X - cam, y = b.Y;
        bool flash = b.Flash > 0;
        switch (b.Kind)
        {
            case BeastKind.Bat:
            {
                double flap = Math.Sin(_clock * 18 + b.X * 0.05) * 16;
                y -= 15;
                uint wing = flash ? 0xFFFFFFFF : 0xFF4A2A5A;
                Poly(dc, wing, Ink, 2.5, new Point(x, y), new Point(x - 16, y - 10 - flap), new Point(x - 30, y - 4 - flap * 1.2), new Point(x - 24, y + 4), new Point(x - 14, y + 2));
                Poly(dc, wing, Ink, 2.5, new Point(x, y), new Point(x + 16, y - 10 - flap), new Point(x + 30, y - 4 - flap * 1.2), new Point(x + 24, y + 4), new Point(x + 14, y + 2));
                dc.DrawEllipse(Brush(flash ? 0xFFFFFFFF : 0xFF2A1A34), Pen(Ink, 2.5), new Point(x, y + 2), 8, 9);
                Poly(dc, 0xFF2A1A34, Ink, 2, new Point(x - 6, y - 4), new Point(x - 5, y - 13), new Point(x - 1, y - 6));
                Poly(dc, 0xFF2A1A34, Ink, 2, new Point(x + 6, y - 4), new Point(x + 5, y - 13), new Point(x + 1, y - 6));
                dc.FillEllipse(x - 3, y, 1.8, 0xFFFF3030);
                dc.FillEllipse(x + 3, y, 1.8, 0xFFFF3030);
                break;
            }
            case BeastKind.Panther:
            {
                int d = b.Facing;
                double run = Math.Sin(_clock * 16 + b.X * 0.03);
                using var _ = dc.PushTransform(Matrix.CreateScale(d, 1) * Matrix.CreateTranslation(x, y));
                uint fur = flash ? 0xFFFFFFFF : 0xFF16161E, rim = 0xFF5A6AA0;
                bool air = !b.Ground;
                // Legs, tail, body, head: a long low cat, stretched out at the leap.
                Limb(dc, new Point(-22, -26), new Point(-30 - run * 10, air ? -14 : 0), 7, fur);
                Limb(dc, new Point(20, -26), new Point(30 + run * 10, air ? -16 : 0), 7, fur);
                Limb(dc, new Point(-26, -30), new Point(-48, -40 - run * 6), 5, fur);
                dc.DrawEllipse(Brush(fur), Pen(Ink, 3), new Point(0, -30), 32, 13);
                dc.DrawLine(Pen(rim, 2), new Point(-20, -40), new Point(18, -40));
                Limb(dc, new Point(-14, -24), new Point(-20 + run * 12, air ? -10 : 0), 7, fur);
                Limb(dc, new Point(14, -24), new Point(22 - run * 12, air ? -12 : 0), 7, fur);
                dc.DrawEllipse(Brush(fur), Pen(Ink, 3), new Point(34, -36), 13, 11);
                Poly(dc, fur, Ink, 2, new Point(28, -44), new Point(30, -52), new Point(35, -45));
                Poly(dc, 0xFFF0F0F0, null, 0, new Point(40, -30), new Point(46, -30), new Point(43, -25));
                dc.FillEllipse(39, -39, 3, 0xFFFFD020);
                dc.FillEllipse(39.5, -39, 1.2, Ink);
                break;
            }
            default:
            {
                // A lava man: a hulking shape of molten rock, cracks glowing.
                if (b.Rising) y = b.Y;
                double sway = Math.Sin(b.T * 3) * 4;
                uint rock = flash ? 0xFFFFFFFF : 0xFFB8401A, glow = 0xFFFFD040;
                using var _ = dc.PushTransform(Matrix.CreateScale(b.Facing, 1) * Matrix.CreateTranslation(x, y));
                Limb(dc, new Point(-8, -50), new Point(-12 - sway, 0), 14, Darker(rock, 0.8));
                Limb(dc, new Point(8, -50), new Point(12 + sway, 0), 14, rock);
                Poly(dc, rock, Ink, 3, new Point(-22, -48), new Point(22, -48), new Point(28, -92), new Point(14, -106), new Point(-16, -104), new Point(-28, -88));
                dc.DrawLine(Pen(glow, 3), new Point(-10, -60), new Point(-2, -78));
                dc.DrawLine(Pen(glow, 3), new Point(-2, -78), new Point(8, -72));
                dc.DrawLine(Pen(glow, 2.5), new Point(12, -56), new Point(16, -84));
                Limb(dc, new Point(18, -92), new Point(34, -62 + sway), 12, rock);
                Limb(dc, new Point(-18, -92), new Point(-30, -60 - sway), 12, Darker(rock, 0.8));
                dc.DrawEllipse(Brush(rock), Pen(Ink, 3), new Point(6, -112), 15, 13);
                dc.FillEllipse(12, -114, 3.2, glow);
                dc.FillEllipse(4, -114, 3.2, glow);
                if (b.Rising) dc.FillRectangle(Brush(0xFFFF7A20), new Rect(-Pit.Wide / 2, Field.Floor - y + 8, Pit.Wide, 40));
                break;
            }
        }
    }

    /// <summary>Maboo: half as big again as his men, robed in purple and gold behind a horned golden mask.</summary>
    private void DrawBoss(DrawingContext dc, Boss b, double cam)
    {
        double x = b.X - cam, y = b.Y;
        bool flash = b.Flash > 0 && (int)(_clock * 30) % 2 == 0;
        double fall = b.Mood == BossMood.Down ? Math.Min(1, b.T * 1.5) : 0;
        var m = Matrix.CreateRotation(-fall * Math.PI / 2) * Matrix.CreateTranslation(0, -fall * 36) * Matrix.CreateScale(b.Facing, 1) * Matrix.CreateTranslation(x, y);
        using var _ = dc.PushTransform(m);
        using var o = dc.PushOpacity(b.Mood == BossMood.Down ? Math.Clamp(2.2 - b.T, 0, 1) : 1);
        bool aiming = b.Mood is BossMood.Aim or BossMood.Volley;
        // Firing low, he stoops: the body drawn squat.
        double squat = aiming && b.Low ? 0.72 : 1;
        uint robe = flash ? 0xFFFFFFFF : 0xFF5A2A8A, dark = Darker(robe, 0.7), gold = 0xFFF0C030, cape = 0xFF9A1A2A;
        using (dc.PushTransform(Matrix.CreateScale(1, squat)))
        {
            Poly(dc, cape, Ink, 3, new Point(-14, -128), new Point(-30, -128), new Point(-64, 0), new Point(-6, 0));
            Limb(dc, new Point(-16, -40), new Point(-18, 0), 14, dark);
            Limb(dc, new Point(12, -40), new Point(16, 0), 14, dark);
            Poly(dc, robe, Ink, 3.5, new Point(-30, -24), new Point(32, -24), new Point(26, -126), new Point(-24, -126));
            Poly(dc, Darker(robe, 0.8), Ink, 3, new Point(-30, -24), new Point(32, -24), new Point(36, -6), new Point(-34, -6));
            dc.DrawLine(Pen(gold, 5), new Point(-32, -8), new Point(34, -8));
            dc.DrawLine(Pen(gold, 6), new Point(3, -124), new Point(3, -24));
            Emblem(dc, 3, -84, 12);
            // The shoulder plates.
            Poly(dc, gold, Ink, 3, new Point(-34, -116), new Point(-22, -134), new Point(4, -132), new Point(-4, -114));
            Poly(dc, gold, Ink, 3, new Point(36, -116), new Point(26, -134), new Point(2, -132), new Point(10, -114));
            // The mask: golden, horned, with burning eyes.
            double hy = -152;
            Poly(dc, gold, Ink, 3, new Point(-12, hy - 8), new Point(-30, hy - 26), new Point(-26, hy - 40), new Point(-18, hy - 16));
            Poly(dc, gold, Ink, 3, new Point(14, hy - 10), new Point(30, hy - 30), new Point(24, hy - 44), new Point(20, hy - 18));
            dc.DrawEllipse(Brush(flash ? 0xFFFFFFFF : gold), Pen(Ink, 3.5), new Point(4, hy), 17, 21);
            dc.DrawLine(Pen(Darker(gold, 0.75), 3), new Point(4, hy - 18), new Point(4, hy - 4));
            Poly(dc, Ink, null, 0, new Point(-6, hy - 4), new Point(4, hy - 1), new Point(-5, hy + 3));
            Poly(dc, Ink, null, 0, new Point(10, hy - 1), new Point(19, hy - 5), new Point(17, hy + 3));
            dc.FillEllipse(-2, hy, 2.2, 0xFFFF3020);
            dc.FillEllipse(15, hy - 1, 2.2, 0xFFFF3020);
            Poly(dc, Ink, null, 0, new Point(-3, hy + 10), new Point(13, hy + 9), new Point(5, hy + 15));
            if (b.Mood == BossMood.Summon) Limb(dc, new Point(-16, -118), new Point(-36, -186), 12, robe);
        }
        // The gun arm: at the height the volley will fly, high or low.
        var sh = new Point(18, -116 * squat);
        double gy = aiming ? -(b.Low ? Sim.ShotLow : Sim.ShotHigh) : -84;
        var hand = new Point(46, gy);
        Limb(dc, sh, hand, 13, robe);
        dc.DrawRectangle(Brush(0xFF2A2A30), Pen(Ink, 2), new Rect(hand.X, hand.Y - 6, 30, 10));
        dc.FillEllipse(hand.X, hand.Y, 7, gold);
        if (b.Mood == BossMood.Volley && b.T < 0.06) Star(dc, hand.X + 40, hand.Y - 1, 16, 7, 0xFFFFF070, 0xFFFF8020);
    }

    /// <summary>Leila: in her cage at the end of the last area, or running to the agent.</summary>
    private void DrawLeila(DrawingContext dc, World w, double cam)
    {
        double cage = Sim.CageX(w.Stage) - cam;
        if (cage < -200 || cage > Field.Width + 300) return;
        bool free = w.Rescued;
        double lx = free ? w.LeilaX - cam : cage;
        bool running = free && w.LeilaX > w.Agent.X + 65 && w.Rescue > 0.8;
        // The cage: dark bars on a stone base, the door swung open once she's out.
        double top = Field.Floor - 170;
        dc.FillRectangle(Brush(0xFF2A1A3A), new Rect(cage - 60, top, 120, 170));
        Figure(dc, lx, Field.Floor, -1, LeilaLook, running ? Pose.Walk : Pose.Stand, _clock * 10, gun: false);
        for (int i = 0; i <= 6; i++)
        {
            double bx = cage - 60 + i * 20;
            if (free && i < 3) bx = cage - 60 - i * 6;
            Limb(dc, new Point(bx, top), new Point(bx, Field.Floor), 4, 0xFF9A8AA8);
        }
        Limb(dc, new Point(cage - 66, top), new Point(cage + 66, top), 8, 0xFF7A6A88);
        Limb(dc, new Point(cage - 66, Field.Floor - 4), new Point(cage + 66, Field.Floor - 4), 8, 0xFF7A6A88);
        Poly(dc, 0xFF7A6A88, Ink, 3, new Point(cage - 30, top), new Point(cage, top - 30), new Point(cage + 30, top));
        if (!free && w.Boss is { Mood: not BossMood.Asleep } && (int)(_clock * 1.5) % 2 == 0) Balloon(dc, cage - 10, top - 50, "HELP!", 30, tailLeft: false);
        if (free && w.Rescue > 2.2) Balloon(dc, lx + 40, Field.Floor - 190, "ALBATROSS!", 30, tailLeft: true);
    }

    private void DrawFlights(DrawingContext dc, World w, double cam)
    {
        foreach (var s in w.Bullets)
        {
            double x = s.X - cam;
            if (s.Slow) dc.DrawEllipse(Brush(0xFFB0B0C0), Pen(Ink, 2), new Point(x, s.Y), 5, 4);
            else
            {
                dc.DrawLine(Pen(0x80FFF080, 3), new Point(x - Math.Sign(s.Vx) * 26, s.Y), new Point(x, s.Y));
                dc.DrawLine(Pen(0xFFFFF4A0, 4), new Point(x - Math.Sign(s.Vx) * 10, s.Y), new Point(x, s.Y));
            }
        }
        foreach (var s in w.Shots)
        {
            double x = s.X - cam;
            dc.DrawLine(Pen(0x60FF4020, 6), new Point(x - Math.Sign(s.Vx) * 22, s.Y), new Point(x, s.Y));
            dc.DrawEllipse(Brush(0xFFFF5A2A), Pen(Ink, 2), new Point(x, s.Y), 6, 5);
            dc.FillEllipse(x, s.Y, 2.4, 0xFFFFF0C0);
        }
        foreach (var g in w.Grenades)
        {
            double x = g.X - cam;
            if (g.Blast > 0)
            {
                double k = g.Blast / 0.4;
                Star(dc, x, g.Y - 30, 30 + 40 * k, 16 + 22 * k, Alpha(0xFFFFB020, 1.2 - k), Alpha(Ink, 1.2 - k), 10, k);
                Star(dc, x, g.Y - 30, 16 + 24 * k, 9 + 12 * k, Alpha(0xFFFFF4A0, 1.2 - k), null, 10, k + 0.3);
                if (k < 0.8) Comic(dc, "BOOM!", new Point(x, g.Y - 92), 30, 0xFFFFE040, center: true);
            }
            else dc.DrawEllipse(Brush(0xFF3A5A2A), Pen(Ink, 2.5), new Point(x, g.Y), 6, 6);
        }
        foreach (var f in w.Fireballs)
        {
            double x = f.X - cam, flick = Math.Sin(_clock * 30 + f.X) * 3;
            Poly(dc, 0xC0FF6A10, null, 0, new Point(x - 11, f.Y), new Point(x, f.Y + 30 + flick), new Point(x + 11, f.Y));
            dc.DrawEllipse(Brush(0xFFFF8A20), Pen(Ink, 2.5), new Point(x, f.Y), 12, 12);
            dc.FillEllipse(x - 2, f.Y - 2, 6, 0xFFFFF080);
        }
        foreach (var b in w.Bursts)
        {
            if (b.T < 0) continue;
            double x = b.X - cam, k = b.T / 0.6;
            switch (b.Kind)
            {
                case 0: if (k < 0.35) Star(dc, x, b.Y, 18 - k * 20, 7, 0xFFFFFFFF, 0xFFFF4040, 6, k * 3); break;
                case 1:
                    Star(dc, x, b.Y - 10, 24 + 50 * k, 12 + 26 * k, Alpha(0xFFFF9A20, 1 - k), Alpha(Ink, 1 - k), 9, k);
                    break;
                case 2:
                    if (k < 0.7) Comic(dc, "OOF!", new Point(x, b.Y - 70 - k * 20), 26, 0xFFFF6A40, center: true);
                    break;
                case 3:
                    dc.DrawEllipse(Brush(Alpha(0xFF9A9AB0, 0.8 - k)), null, new Point(x, b.Y), 10 + 30 * k, 8 + 20 * k);
                    break;
                default:
                    if (k < 0.75)
                    {
                        // The comic's sound for a foe going down.
                        string word = ((int)(b.X / 7) % 3) switch { 0 => "POW!", 1 => "BLAM!", _ => "KA-POW!" };
                        Star(dc, x, b.Y - k * 30, 40, 24, Alpha(0xFFFFE040, 1.4 - k * 1.6), Alpha(Ink, 1.4 - k * 1.6), 9);
                        Comic(dc, word, new Point(x, b.Y - k * 30), 24, Red, center: true, lean: 0.15);
                    }
                    break;
            }
        }
    }
}
