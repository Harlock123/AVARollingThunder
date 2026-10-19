namespace AVARollingThunder.Engine;

/// <summary>The Geldra: maskers out of the doors, the bats, panthers and lava men, the fire pits, and Maboo.</summary>
public static partial class Sim
{
    public const double ShotSpeed = 350, GrenadeGravity = 1100, GrenadeFlight = 0.9;

    private static double Hardness(World w, double easy, double hard) => w.Hard ? hard : easy;

    /// <summary>How long a masker takes to aim before he fires.</summary>
    public static double AimTime(World w) => Hardness(w, 0.42, 0.32);

    // ───────────────────────────── the maskers ─────────────────────────────

    /// <summary>Now and then a masker comes out of a door near the agent: not too near, and on the screen.</summary>
    private static void Spawner(World w, double dt)
    {
        var a = w.Agent;
        if ((w.Spawn -= dt) > 0) return;
        w.Spawn = w.Stage.SpawnEvery * Hardness(w, 1, 0.8) * w.Dice.Range(0.8, 1.25);
        int max = w.Stage.MaxFoes + (w.Hard ? 1 : 0);
        if (w.Maskers.Count(m => m.Alive) >= max) return;
        // In Maboo's hall it's Maboo who calls them.
        if (w.Boss is { } b && a.X > b.X - 1100) return;
        double cam = w.Camera;
        var doors = new List<int>();
        for (int i = 0; i < w.Doors.Count; i++)
        {
            var d = w.Doors[i];
            double dx = d.X - a.X;
            if (d.Kind != DoorKind.Plain || d.Cool > 0 || i == a.Door || Math.Abs(dx) < 230 || dx > 640 || dx < -500) continue;
            if (d.X < cam + 30 || d.X > cam + Field.Width - 30) continue;
            doors.Add(i);
            if (dx > 0) { doors.Add(i); doors.Add(i); }
        }
        // Mostly out of a door; sometimes two at once; sometimes one walks in from the edge of the screen.
        if (doors.Count == 0 || w.Dice.Next() < 0.25) { WalkIn(w, PickHood(w)); return; }
        int first = doors[w.Dice.Next(doors.Count)];
        Emerge(w, first, PickHood(w));
        doors.RemoveAll(i => i == first);
        if (doors.Count > 0 && w.Dice.Next() < 0.35 && w.Maskers.Count(m => m.Alive) < max) Emerge(w, doors[w.Dice.Next(doors.Count)], PickHood(w));
    }

    /// <summary>A masker walking in from just off the screen ahead, on the agent's storey if there's floor for him there.</summary>
    private static void WalkIn(World w, Hood hood)
    {
        var a = w.Agent;
        double x = w.Camera + Field.Width + 40;
        if (x > w.Stage.Length - 60) return;
        bool up = a.Storey == 1 && w.Stage.WalkwayAt(x);
        if (!up && w.Stage.PitAt(x) >= 0) return;
        var m = new Masker
        {
            X = x, Y = up ? Field.Walkway : Field.Floor, Hood = hood, Mood = Mood.Walk, Hp = hood == Hood.Red ? 2 : 1, Facing = -1,
            Cool = w.Dice.Range(0.6, 1.4), Range = w.Dice.Range(120, 380),
        };
        w.Maskers.Add(m);
    }

    private static Hood PickHood(World w)
    {
        var mix = w.Stage.Mix;
        int roll = w.Dice.Next(mix.Sum());
        for (int i = 0; i < mix.Count; i++)
        {
            if (roll < mix[i]) return (Hood)i;
            roll -= mix[i];
        }
        return Hood.Blue;
    }

    /// <summary>A masker out of a door.</summary>
    public static Masker Emerge(World w, int door, Hood hood)
    {
        var d = w.Doors[door];
        var m = new Masker
        {
            X = d.X, Y = d.Feet, Hood = hood, Mood = Mood.Emerge, Door = door, Hp = hood == Hood.Red ? 2 : 1,
            Facing = w.Agent.X < d.X ? -1 : 1, Cool = w.Dice.Range(0.2, 1.0),
            Range = hood switch
            {
                Hood.Green => w.Dice.Range(260, 420),
                Hood.Red => w.Dice.Range(40, 180),
                Hood.Yellow => w.Dice.Range(180, 400),
                _ => w.Dice.Range(80, 380),
            },
        };
        d.Open = 0.8;
        d.Cool = 3;
        w.Maskers.Add(m);
        w.Heard.Add(Sfx.Door);
        return m;
    }

    private static void Masker(World w, Masker m, double dt)
    {
        var a = w.Agent;
        var s = w.Stage;
        m.T += dt;
        m.Flash -= dt;
        m.Drop -= dt;
        double dx = a.X - m.X;
        switch (m.Mood)
        {
            case Mood.Emerge:
                if (m.T > 0.55) { m.Mood = Mood.Walk; m.T = 0; }
                return;
            case Mood.Down:
                if (m.T > 0.7) m.Gone = true;
                return;
            case Mood.Leap:
            {
                double prev = m.Y;
                m.X += m.Vx * dt;
                m.Vy += Gravity * dt;
                m.Y += m.Vy * dt;
                if (m.Vy > 0 && Land(s, m.X, prev, ref m.Y, m.Drop > 0)) { m.Mood = Mood.Walk; m.Vx = m.Vy = 0; m.Cool = w.Dice.Range(0.6, 1.2); }
                else if (m.Y > Field.Floor + 140) m.Gone = true;
                return;
            }
            case Mood.Aim:
                if (m.T >= AimTime(w))
                {
                    Fire(w, m);
                    m.Mood = Mood.Walk;
                    m.T = 0;
                    m.Cool = w.Dice.Range(1.5, 2.8) * Hardness(w, 1, 0.75);
                }
                return;
        }
        // Walking: up to the agent and shooting, when he's there to be seen; otherwise about their business.
        bool sees = !a.Hidden && !a.Dead && m.Storey == a.Storey && Math.Abs(a.Y - m.Y) < 60;
        double speed = (m.Hood == Hood.Red ? 120 : 80) * Hardness(w, 1, 1.15);
        int way = 0;
        m.Cool -= dt;
        if (sees)
        {
            m.Facing = dx < 0 ? -1 : 1;
            double d = Math.Abs(dx);
            if (d > m.Range + 20) way = m.Facing;
            else if (m.Hood == Hood.Green && d < m.Range - 60) way = -m.Facing;
            if (m.Cool <= 0 && d < 620)
            {
                if (m.Hood == Hood.Red && d < 280 && w.Dice.Next() < 0.6) { Leap(m, m.Facing * 240, -600, false); return; }
                m.Mood = Mood.Aim;
                m.T = 0;
                m.Low = m.Hood switch { Hood.Yellow => w.Dice.Next() < 0.7, Hood.Blue => w.Hard && w.Dice.Next() < 0.2, _ => false };
                return;
            }
        }
        else
        {
            // A red masker goes after him from storey to storey.
            if (m.Hood == Hood.Red && !a.Hidden && !a.Dead && m.Cool <= 0 && Math.Abs(dx) < 500 && a.Storey != m.Storey)
            {
                if (m.Storey == 0 && s.WalkwayAt(m.X)) { Leap(m, 0, -HighJumpSpeed, false); return; }
                if (m.Storey == 1 && s.PitAt(m.X + m.Facing * 40) < 0) { Leap(m, m.Facing * 120, -200, true); return; }
            }
            if (w.Dice.Next() < 0.006) m.Facing = -m.Facing;
            way = m.Facing;
            speed = 60;
        }
        if (way != 0)
        {
            double nx = m.X + way * speed * dt;
            bool blocked = m.Storey == 1 ? !s.WalkwayAt(nx + way * 16) : s.PitAt(nx + way * 30) >= 0;
            blocked |= nx < 30 || nx > s.Length - 30;
            if (w.Boss is { Alive: true } b) blocked |= nx > b.X + 200;
            if (!blocked) m.X = nx;
            else if (!sees) m.Facing = -m.Facing;
        }
        if (a.X - m.X > 900 || m.X - a.X > 1100) m.Gone = true;
    }

    private static void Leap(Masker m, double vx, double vy, bool drop)
    {
        m.Mood = Mood.Leap;
        m.T = 0;
        m.Vx = vx;
        m.Vy = vy;
        m.Ground = false;
        if (drop) m.Drop = 0.3;
    }

    /// <summary>A shot (high, or low from a crouch), or a grenade lobbed to land where the agent stands.</summary>
    private static void Fire(World w, Masker m)
    {
        var a = w.Agent;
        if (m.Hood == Hood.Green)
        {
            double y0 = m.Y - 90, t = GrenadeFlight;
            w.Grenades.Add(new Grenade
            {
                X = m.X + m.Facing * 10, Y = y0, Floor = m.Y, Vx = Math.Clamp((a.X - m.X) / t, -440, 440),
                Vy = (m.Y - y0 - 0.5 * GrenadeGravity * t * t) / t,
            });
            w.Heard.Add(Sfx.Throw);
            return;
        }
        w.Shots.Add(new Shot { X = m.X + m.Facing * 28, Y = m.Y - (m.Low ? ShotLow : ShotHigh), Vx = m.Facing * ShotSpeed * Hardness(w, 1, 1.2) });
        w.Heard.Add(Sfx.EnemyShot);
    }

    private static void Wound(World w, Masker m)
    {
        m.Flash = 0.15;
        w.Bursts.Add(new Burst(m.X, m.Y - 70, 0, 0));
        if (--m.Hp > 0) { w.Heard.Add(Sfx.Clang); return; }
        m.Mood = Mood.Down;
        m.T = 0;
        m.Facing = w.Agent.X < m.X ? -1 : 1;
        w.Bursts.Add(new Burst(m.X, m.Top - 20, 0, 4));
        w.Score += Points(m.Hood);
        w.Kills++;
        w.Heard.Add(Sfx.EnemyHit);
    }

    // ───────────────────────────── bats, panthers and lava men ─────────────────────────────

    /// <summary>The cues along the way: a swarm of bats, a panther running in.</summary>
    private static void Cues(World w)
    {
        var a = w.Agent;
        var s = w.Stage;
        double right = w.Camera + Field.Width;
        for (int i = 0; i < s.Cues.Count; i++)
        {
            var q = s.Cues[i];
            if (w.Fired[i] || a.X < q.X - 450) continue;
            w.Fired[i] = true;
            if (q.Kind == 'b')
            {
                int n = w.Hard ? 5 : 4;
                for (int k = 0; k < n; k++)
                    w.Beasts.Add(new Beast { Kind = BeastKind.Bat, X = right + 40 + k * 110, Base = a.Y - 70 - w.Dice.Range(-60, 60), Y = a.Y - 70, Vx = -190 * Hardness(w, 1, 1.15), T = k * 0.7 });
                w.Heard.Add(Sfx.Bats);
            }
            else if (q.Kind == 'p')
            {
                int n = w.Hard ? 2 : 1;
                for (int k = 0; k < n; k++)
                    w.Beasts.Add(new Beast { Kind = BeastKind.Panther, X = SafeX(s, right + 60 + k * 160), Y = Field.Floor, Vx = -380 * Hardness(w, 1, 1.1) });
                w.Heard.Add(Sfx.Growl);
            }
        }
        // A lava man climbs out of each lava pit as the agent comes near.
        for (int i = 0; i < s.Pits.Count; i++)
        {
            if (s.Pits[i].Kind != PitKind.Lava || w.LavaUsed[i] || Math.Abs(a.X - s.Pits[i].X) > 560) continue;
            w.LavaUsed[i] = true;
            w.Beasts.Add(new Beast { Kind = BeastKind.LavaMan, X = s.Pits[i].X, Y = Field.Floor + 112, Pit = i, Rising = true, Hp = w.Hard ? 5 : 4 });
            w.Heard.Add(Sfx.Lava);
        }
    }

    private static void Beast(World w, Beast b, double dt)
    {
        var a = w.Agent;
        var s = w.Stage;
        b.T += dt;
        b.Flash -= dt;
        switch (b.Kind)
        {
            case BeastKind.Bat:
                // Flitting at the agent's head height, back and forth past him, then away.
                b.Base += (a.Y - 70 - b.Base) * 0.5 * dt;
                b.X += b.Vx * dt;
                b.Y = b.Base + Math.Sin(b.T * 5) * 42;
                if (b.T > 14) { b.Base -= 260 * dt * 4; if (b.Y < 0) b.Gone = true; }
                else if (b.Vx < 0 && b.X < a.X - 260 || b.Vx > 0 && b.X > a.X + 260) b.Vx = -b.Vx;
                b.Facing = b.Vx < 0 ? -1 : 1;
                break;
            case BeastKind.Panther:
            {
                b.Facing = b.Vx < 0 ? -1 : 1;
                if (b.Ground)
                {
                    b.X += b.Vx * dt;
                    double ahead = b.X - a.X;
                    bool pounce = a.Storey == 0 && !a.Hidden && ahead * b.Facing < 0 && Math.Abs(ahead) < 170;
                    if (pounce || s.PitAt(b.X + b.Facing * 50) >= 0) { b.Ground = false; b.Vy = -520; }
                    else if (s.PitAt(b.X) >= 0) { b.Ground = false; b.Vy = 0; }
                }
                else
                {
                    double prev = b.Y;
                    b.X += b.Vx * dt;
                    b.Vy += Gravity * dt;
                    b.Y += b.Vy * dt;
                    if (b.Vy > 0 && prev <= Field.Floor && b.Y >= Field.Floor && s.PitAt(b.X) < 0) { b.Y = Field.Floor; b.Ground = true; b.Vy = 0; }
                    if (b.Y > Field.Floor + 140) b.Gone = true;
                }
                if ((a.X - b.X) * b.Facing < -500) b.Gone = true;
                break;
            }
            case BeastKind.LavaMan:
                if (b.Rising)
                {
                    b.Y = Math.Max(Field.Floor, b.Y - 110 * dt);
                    if (b.T > 1.2) b.Rising = false;
                    break;
                }
                b.Facing = a.X < b.X ? -1 : 1;
                double nx = b.X + b.Facing * 52 * Hardness(w, 1, 1.25) * dt;
                int pit = s.PitAt(nx + b.Facing * 20);
                if (pit < 0 || s.Pits[pit].Kind == PitKind.Lava) b.X = nx;
                if (Math.Abs(a.X - b.X) > 1000) b.Gone = true;
                break;
        }
    }

    private static void Wound(World w, Beast b)
    {
        b.Flash = 0.15;
        w.Bursts.Add(new Burst(b.X, b.Y - b.Tall / 2, 0, 0));
        if (--b.Hp > 0) { w.Heard.Add(Sfx.Clang); return; }
        b.Gone = true;
        w.Bursts.Add(new Burst(b.X, b.Y - b.Tall / 2, 0, b.Kind == BeastKind.LavaMan ? 1 : 3));
        w.Bursts.Add(new Burst(b.X, b.Y - b.Tall - 20, 0, 4));
        w.Score += b.Kind switch { BeastKind.Bat => BatPoints, BeastKind.Panther => PantherPoints, _ => LavaManPoints };
        w.Kills++;
        w.Heard.Add(Sfx.EnemyHit);
    }

    /// <summary>Each fire pit near the agent throws up a fireball every couple of seconds, each pit in its own time.</summary>
    private static void FirePits(World w, double dt)
    {
        var s = w.Stage;
        double every = Hardness(w, 2.4, 1.9);
        for (int i = 0; i < s.Pits.Count; i++)
        {
            var p = s.Pits[i];
            if (p.Kind != PitKind.Fire || Math.Abs(p.X - w.Agent.X) > 900) continue;
            double t = w.Clock + i * 0.83;
            if (Math.Floor(t / every) == Math.Floor((t - dt) / every)) continue;
            w.Fireballs.Add(new Fireball { X = p.X, Y = Field.Floor + 20, Vx = w.Dice.Range(-40, 40), Vy = -w.Dice.Range(640, 760) });
            if (Math.Abs(p.X - w.Agent.X) < 600) w.Heard.Add(Sfx.Fireball);
        }
    }

    // ───────────────────────────── Maboo ─────────────────────────────

    /// <summary>
    /// Maboo wakes with a laugh as the agent comes into his hall, then keeps his distance and fires in volleys of three,
    /// high (duck them) or low (jump them), and every third time calls a masker out of a door instead.
    /// </summary>
    private static void Maboo(World w, double dt)
    {
        if (w.Boss is not { } b) return;
        var a = w.Agent;
        b.T += dt;
        b.Flash -= dt;
        double home = w.Stage.BossX!.Value;
        switch (b.Mood)
        {
            case BossMood.Asleep:
                if (a.X > b.X - 640) { b.Mood = BossMood.Stride; b.Cool = 1.4; b.T = 0; w.Heard.Add(Sfx.Laugh); }
                return;
            case BossMood.Down:
                if (b.T > 1.4 && !w.Rescued)
                {
                    w.Rescued = true;
                    w.Rescue = 0;
                    w.LeilaX = CageX(w.Stage);
                    w.Score += RescuePoints + TimeBonus(w);
                    w.Heard.Add(Sfx.Rescue);
                }
                return;
            case BossMood.Stride:
            {
                double d = b.X - a.X;
                int way = d < 300 ? 1 : d > 460 ? -1 : 0;
                b.X = Math.Clamp(b.X + way * 60 * dt, home - 450, home + 120);
                b.Cool -= dt;
                if (b.Cool > 0) return;
                b.Volleys++;
                b.T = 0;
                if (b.Volleys % 3 == 0) b.Mood = BossMood.Summon;
                else { b.Mood = BossMood.Aim; b.Low = w.Dice.Next() < 0.5; }
                return;
            }
            case BossMood.Aim:
                if (b.T > Hardness(w, 0.6, 0.45)) { b.Mood = BossMood.Volley; b.Left = 3; b.T = 0.16; }
                return;
            case BossMood.Volley:
                if (b.T < 0.16) return;
                b.T = 0;
                w.Shots.Add(new Shot { X = b.X - 44, Y = b.Y - (b.Low ? ShotLow : ShotHigh), Vx = -ShotSpeed * Hardness(w, 1.05, 1.2) });
                w.Heard.Add(Sfx.EnemyShot);
                if (--b.Left <= 0) { b.Mood = BossMood.Stride; b.Cool = w.Dice.Range(1.2, 2.0) * Hardness(w, 1, 0.75); }
                return;
            case BossMood.Summon:
                if (b.T < 0.7) return;
                b.Mood = BossMood.Stride;
                b.Cool = 1.0;
                if (w.Maskers.Count(m => m.Alive) >= 2) return;
                int best = -1;
                for (int i = 0; i < w.Doors.Count; i++)
                {
                    var door = w.Doors[i];
                    if (door.Kind != DoorKind.Plain || door.Storey != 0 || Math.Abs(door.X - home) > 760 || Math.Abs(door.X - a.X) < 180 || i == a.Door) continue;
                    if (best < 0 || Math.Abs(door.X - a.X) > Math.Abs(w.Doors[best].X - a.X)) best = i;
                }
                if (best >= 0) Emerge(w, best, (Hood)w.Dice.Next(4));
                return;
        }
    }

    /// <summary>A pistol bullet does Maboo three times the harm of a machine-gun round.</summary>
    private static void Wound(World w, Boss b, int harm)
    {
        b.Flash = 0.12;
        w.Bursts.Add(new Burst(b.X - 10, b.Y - 90, 0, 0));
        if ((b.Hp -= harm) > 0) { w.Heard.Add(Sfx.Clang); return; }
        b.Mood = BossMood.Down;
        b.T = 0;
        w.Score += BossPoints;
        w.Kills++;
        w.Heard.Add(Sfx.BossDown);
        // His maskers flee, and every bullet in the air is spent.
        foreach (var m in w.Maskers) if (m.Alive) { m.Mood = Mood.Down; m.T = 0; }
        w.Shots.Clear();
        w.Grenades.Clear();
        for (int k = 0; k < 5; k++) w.Bursts.Add(new Burst(b.X + w.Dice.Range(-40, 40), b.Y - w.Dice.Range(20, 150), -k * 0.1, 1));
    }

    // ───────────────────────────── touches ─────────────────────────────

    private static bool Overlap(double top1, double feet1, double top2, double feet2) => top1 < feet2 && top2 < feet1;

    private static void Touches(World w)
    {
        var a = w.Agent;
        if (a.Dead || a.Hidden) return;
        foreach (var m in w.Maskers)
            if (m.Alive && m.Out && Math.Abs(m.X - a.X) < 26 && Overlap(a.Top, a.Y, m.Top, m.Y)) Touch(w, "a masker", m.X);
        foreach (var b in w.Beasts)
            if (!b.Gone && !(b.Rising && b.T < 0.8) && Math.Abs(b.X - a.X) < b.Wide / 2 + Agent.Half && Overlap(a.Top, a.Y, b.Y - b.Tall, b.Y))
                Touch(w, b.Kind switch { BeastKind.Bat => "a bat", BeastKind.Panther => "a panther", _ => "a lava man" }, b.X);
        if (w.Boss is { Alive: true } boss && Math.Abs(boss.X - a.X) < Boss.Half + Agent.Half + 6) Touch(w, "Maboo", boss.X);
    }
}
