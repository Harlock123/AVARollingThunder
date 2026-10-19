namespace AVARollingThunder.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static partial class Sim
{
    public const double WalkSpeed = 165, JumpSpeed = 560, HighJumpSpeed = 1010, JumpRun = 165, LandTime = 0.14, Gravity = 1800;
    public const int Life0 = 2, Bullets0 = 40, BulletsMax = 99, BulletsDoor = 15, RoundsDoor = 30;
    public const double PistolSpeed = 640, EmptySpeed = 260, MachineGunEvery = 0.1, MachineGunSpeed = 720;
    /// <summary>The heights bullets fly at above the shooter's feet: standing, crouched (the agent), and low (a crouching masker).</summary>
    public const double ShotHigh = 72, ShotCrouch = 40, ShotLow = 26;
    public const double HurtTime = 1.4, DoorReach = 24, WarnAt = 30;
    public const int TimePoints = 50, BossPoints = 10000, RescuePoints = 20000, BatPoints = 100, PantherPoints = 300, LavaManPoints = 500;

    public static int Points(Hood h) => h switch { Hood.Blue => 200, Hood.Red => 500, _ => 300 };

    /// <summary>A new game: the first area of the first story.</summary>
    public static void Start(World w)
    {
        w.Score = 0;
        w.Kills = 0;
        w.Agent = new Agent();
        StartStage(w, 1, 1);
    }

    /// <summary>Into an area, with the agent's guns and ammunition as they were.</summary>
    public static void StartStage(World w, int story, int area)
    {
        var old = w.Agent;
        w.Story = story;
        w.Area = area;
        w.Stage = Stage.Of(area);
        w.Doors = w.Stage.Doors.Select(d => d.Copy()).ToList();
        w.Agent = new Agent { Gun = old.Gun, Bullets = old.Bullets, Rounds = old.Rounds };
        w.Boss = w.Stage.BossX is { } bx ? new Boss { X = bx, Hp = BossHp(w), MaxHp = BossHp(w) } : null;
        w.Fired = new bool[w.Stage.Cues.Count];
        w.LavaUsed = new bool[w.Stage.Pits.Count];
        w.Checkpoint = w.Agent.X;
        w.Dice = new Dice(1986 + area * 7 + story * 101);
        w.Cleared = w.Rescued = false;
        w.Clock = 0;
        Restart(w);
    }

    private static int BossHp(World w) => w.Hard ? 120 : 90;

    /// <summary>After a death: back at the start of the area, or halfway if he got that far, with a full life meter and the clock reset.</summary>
    public static void Restart(World w)
    {
        var old = w.Agent;
        w.Agent = new Agent { X = w.Checkpoint, Gun = old.Dead ? Gun.Pistol : old.Gun, Rounds = old.Dead ? 0 : old.Rounds, Bullets = old.Dead ? Math.Max(old.Bullets, Bullets0) : old.Bullets };
        w.Maskers.Clear();
        w.Bullets.Clear();
        w.Shots.Clear();
        w.Grenades.Clear();
        w.Beasts.Clear();
        w.Fireballs.Clear();
        w.Bursts.Clear();
        foreach (var d in w.Doors) d.Open = d.Cool = 0;
        for (int i = 0; i < w.Fired.Length; i++) if (w.Stage.Cues[i].X > w.Checkpoint) w.Fired[i] = false;
        for (int i = 0; i < w.LavaUsed.Length; i++) if (w.Stage.Pits[i].X > w.Checkpoint) w.LavaUsed[i] = false;
        if (w.Boss is { Alive: true } b) { b.X = w.Stage.BossX!.Value; b.Hp = b.MaxHp; b.Mood = BossMood.Asleep; b.Volleys = 0; b.T = 0; }
        w.TimeLeft = w.Stage.Time * (w.Hard ? 0.9 : 1);
        w.Warned = false;
        w.Spawn = 2;
        w.Cause = "";
    }

    /// <summary>The first place at or after x with solid floor either side.</summary>
    public static double SafeX(Stage s, double x)
    {
        while (s.PitAt(x) >= 0 || s.PitAt(x - 60) >= 0 || s.PitAt(x + 60) >= 0) x += 50;
        return x;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        for (int i = w.Bursts.Count - 1; i >= 0; i--)
        {
            var b = w.Bursts[i];
            if (b.T + dt > 0.6) w.Bursts.RemoveAt(i);
            else w.Bursts[i] = b with { T = b.T + dt };
        }
        var a = w.Agent;
        if (w.Rescued) { Reunion(w, dt); return; }
        if (a.Dead) { a.DeadT += dt; return; }
        if (w.Cleared) return;
        w.TimeLeft -= dt;
        if (w.TimeLeft <= WarnAt && !w.Warned) { w.Warned = true; w.Heard.Add(Sfx.TimeWarning); }
        if (w.TimeLeft <= 0) { w.TimeLeft = 0; Kill(w, "time"); return; }
        Move(w, a, c, dt);
        foreach (var d in w.Doors) { d.Open -= dt; d.Cool -= dt; }
        Spawner(w, dt);
        Cues(w);
        foreach (var m in w.Maskers) Masker(w, m, dt);
        foreach (var b in w.Beasts) Beast(w, b, dt);
        FirePits(w, dt);
        Maboo(w, dt);
        Flights(w, dt);
        Touches(w);
        if (a.X > w.Stage.Length / 2 && w.Checkpoint < w.Stage.Length / 2) w.Checkpoint = SafeX(w.Stage, w.Stage.Length / 2);
        if (w.Boss == null && a.X >= w.Stage.Exit && !a.Dead)
        {
            w.Cleared = true;
            w.Score += TimeBonus(w);
            w.Heard.Add(Sfx.StageClear);
        }
        w.Maskers.RemoveAll(m => m.Gone);
        w.Beasts.RemoveAll(b => b.Gone);
        w.Bullets.RemoveAll(s => s.Gone);
        w.Shots.RemoveAll(s => s.Gone);
        w.Grenades.RemoveAll(g => g.Gone);
        w.Fireballs.RemoveAll(f => f.Gone);
    }

    public static int TimeBonus(World w) => (int)Math.Ceiling(w.TimeLeft) * TimePoints;

    // ───────────────────────────── the agent ─────────────────────────────

    private static void Move(World w, Agent a, Controls c, double dt)
    {
        a.Hurt -= dt;
        a.Reload -= dt;
        a.Flash -= dt;
        a.Drop -= dt;
        a.Landing -= dt;
        a.Moving = false;
        var s = w.Stage;
        if (a.Hidden)
        {
            a.DoorT += dt;
            // Out again with down, a step, or a jump.
            if (a.DoorT > 0.35 && (c.Dy > 0 || c.Dx != 0 || c.Jump))
            {
                w.Doors[a.Door].Open = 0.3;
                a.Door = -1;
                a.DoorT = 0;
                w.Heard.Add(Sfx.Door);
            }
            a.FirePrev = c.Fire;
            return;
        }
        if (a.Ground)
        {
            a.Crouch = c.Dy > 0 && !c.Jump;
            if (c.Dx != 0) a.Facing = c.Dx;
            if (c.Jump && a.Landing <= 0)
            {
                a.Crouch = false;
                a.Ground = false;
                a.Vx = c.Dx * JumpRun;
                if (c.Dy < 0 && a.Storey == 0 && s.WalkwayAt(a.X)) { a.Vy = -HighJumpSpeed; a.High = true; }
                else if (c.Dy > 0 && a.Storey == 1) { a.Vy = -200; a.Drop = 0.3; }
                else a.Vy = -JumpSpeed;
                w.Heard.Add(Sfx.Jump);
            }
            else if (c.Dy < 0 && DoorAt(w, a.X, a.Storey) is >= 0 and var di)
            {
                Enter(w, a, di);
                a.FirePrev = c.Fire;
                return;
            }
            else if (!a.Crouch && c.Dx != 0 && a.Landing <= 0)
            {
                a.X += c.Dx * WalkSpeed * dt;
                a.Walk += dt;
                a.Moving = true;
            }
            // Off the end of the walkway, or into a pit.
            if (a.Ground && (a.Storey == 1 ? !s.WalkwayAt(a.X) : s.PitAt(a.X) >= 0)) { a.Ground = false; a.Vy = 0; a.Vx = 0; }
        }
        if (!a.Ground)
        {
            double prev = a.Y;
            a.X += a.Vx * dt;
            a.Vy += Gravity * dt;
            a.Y += a.Vy * dt;
            if (a.Vy > 0 && Land(s, a.X, prev, ref a.Y, a.Drop > 0))
            {
                a.Ground = true;
                a.Vy = a.Vx = 0;
                a.High = false;
                a.Landing = LandTime;
            }
            else if (a.Y > Field.Floor + 140) { Kill(w, "a pit"); return; }
        }
        a.X = Math.Clamp(a.X, 40, s.Length - 40);
        if (w.Boss is { Alive: true } b) a.X = Math.Min(a.X, b.X - 54);
        Shoot(w, a, c);
    }

    /// <summary>Falling past the walkway or the floor: land there, if there's something to land on.</summary>
    public static bool Land(Stage s, double x, double prev, ref double y, bool dropping)
    {
        if (!dropping && prev <= Field.Walkway && y >= Field.Walkway && s.WalkwayAt(x)) { y = Field.Walkway; return true; }
        if (prev <= Field.Floor && y >= Field.Floor && s.PitAt(x) < 0) { y = Field.Floor; return true; }
        return false;
    }

    /// <summary>The door within reach on a storey, or -1.</summary>
    public static int DoorAt(World w, double x, int storey)
    {
        for (int i = 0; i < w.Doors.Count; i++)
            if (w.Doors[i].Storey == storey && Math.Abs(w.Doors[i].X - x) < DoorReach) return i;
        return -1;
    }

    /// <summary>Into a door: hidden, and if it's marked BULLETS or ARMS, armed.</summary>
    private static void Enter(World w, Agent a, int i)
    {
        var d = w.Doors[i];
        a.Door = i;
        a.DoorT = 0;
        a.X = d.X;
        a.Crouch = false;
        d.Open = 0.3;
        w.Heard.Add(Sfx.Door);
        if (d.Used || d.Kind == DoorKind.Plain) return;
        d.Used = true;
        if (d.Kind == DoorKind.Bullets) a.Bullets = Math.Min(BulletsMax, a.Bullets + BulletsDoor);
        else
        {
            a.Rounds = Math.Min(BulletsMax, a.Rounds + RoundsDoor);
            a.Gun = Gun.MachineGun;
        }
        w.Heard.Add(Sfx.Ammo);
    }

    /// <summary>
    /// The pistol fires on each press, one bullet in the air at a time; out of bullets, it still fires, but slowly.
    /// The machine gun fires while the button is held, until its rounds run out.
    /// </summary>
    private static void Shoot(World w, Agent a, Controls c)
    {
        bool press = c.Fire && !a.FirePrev;
        a.FirePrev = c.Fire;
        double y = a.Y - (a.Crouch ? ShotCrouch : ShotHigh), x = a.X + a.Facing * 30;
        if (a.Gun == Gun.MachineGun)
        {
            if (!c.Fire || a.Reload > 0) return;
            w.Bullets.Add(new Shot { X = x, Y = y + (w.Dice.Next() - 0.5) * 6, Vx = a.Facing * MachineGunSpeed });
            a.Reload = MachineGunEvery;
            a.Flash = 0.06;
            w.Heard.Add(Sfx.MachineGun);
            if (--a.Rounds <= 0) { a.Rounds = 0; a.Gun = Gun.Pistol; }
            return;
        }
        if (!press || w.Bullets.Count > 0) return;
        bool empty = a.Bullets <= 0;
        w.Bullets.Add(new Shot { X = x, Y = y, Vx = a.Facing * (empty ? EmptySpeed : PistolSpeed), Slow = empty, Harm = empty ? 1 : 3 });
        a.Flash = 0.08;
        if (empty) w.Heard.Add(Sfx.Empty);
        else { a.Bullets--; w.Heard.Add(Sfx.Pistol); }
    }

    /// <summary>A bullet: the end, whatever the life meter shows.</summary>
    public static void Kill(World w, string cause)
    {
        var a = w.Agent;
        if (a.Dead) return;
        a.Dead = true;
        a.DeadT = 0;
        a.Life = 0;
        a.Crouch = false;
        w.Cause = cause;
        w.Heard.Add(Sfx.Death);
    }

    /// <summary>A touch: one part of the life meter, and a knock back; the second touch is the end.</summary>
    public static void Touch(World w, string cause, double fromX)
    {
        var a = w.Agent;
        if (a.Hidden || a.Hurt > 0 || a.Dead) return;
        a.Life--;
        a.Hurt = HurtTime;
        w.Heard.Add(Sfx.Hurt);
        w.Bursts.Add(new Burst(a.X, a.Y - 60, 0, 2));
        if (a.Life <= 0) { Kill(w, cause); return; }
        double to = a.X + (a.X < fromX ? -40 : 40);
        if (a.Ground && (a.Storey == 1 ? w.Stage.WalkwayAt(to) : w.Stage.PitAt(to) < 0)) a.X = to;
    }

    // ───────────────────────────── bullets, grenades and fireballs ─────────────────────────────

    private static bool Inside(double x, double y, double cx, double half, double top, double feet) => Math.Abs(x - cx) < half && y >= top && y <= feet;

    private static void Flights(World w, double dt)
    {
        var a = w.Agent;
        foreach (var s in w.Bullets)
        {
            double steps = Math.Ceiling(Math.Abs(s.Vx) * dt / 12);
            for (int k = 0; k < steps && !s.Gone; k++)
            {
                s.X += s.Vx * dt / steps;
                foreach (var m in w.Maskers)
                    if (m.Alive && m.Out && Inside(s.X, s.Y, m.X, 17, m.Top, m.Y)) { s.Gone = true; Wound(w, m); break; }
                if (s.Gone) break;
                foreach (var b in w.Beasts)
                    if (!b.Gone && !(b.Rising && b.T < 0.6) && Inside(s.X, s.Y, b.X, b.Wide, b.Y - b.Tall, b.Y)) { s.Gone = true; Wound(w, b); break; }
                if (s.Gone) break;
                if (w.Boss is { Alive: true, Mood: not BossMood.Asleep } boss && Inside(s.X, s.Y, boss.X, Boss.Half, boss.Y - Boss.Tall, boss.Y)) { s.Gone = true; Wound(w, boss, s.Harm); }
            }
            s.Age += dt;
            if (Math.Abs(s.X - a.X) > 760 || s.X < 0 || s.X > w.Stage.Length) s.Gone = true;
        }
        foreach (var s in w.Shots)
        {
            s.X += s.Vx * dt;
            s.Age += dt;
            if (s.Age > 4) s.Gone = true;
            if (!a.Dead && !a.Hidden && Inside(s.X, s.Y, a.X, Agent.Half, a.Top, a.Y))
            {
                s.Gone = true;
                Kill(w, "a bullet");
            }
        }
        foreach (var g in w.Grenades)
        {
            if (g.Blast > 0)
            {
                g.Blast += dt;
                if (g.Blast < 0.2 && Math.Abs(a.X - g.X) < 56 && Math.Abs(a.Y - g.Floor) < 50 && a.Ground) Touch(w, "a grenade", g.X);
                if (g.Blast > 0.4) g.Gone = true;
                continue;
            }
            double prev = g.Y;
            g.X += g.Vx * dt;
            g.Vy += GrenadeGravity * dt;
            g.Y += g.Vy * dt;
            if (g.Vy > 0 && prev <= g.Floor && g.Y >= g.Floor)
            {
                bool solid = g.Floor < Field.Floor - 50 ? w.Stage.WalkwayAt(g.X) : w.Stage.PitAt(g.X) < 0;
                if (solid)
                {
                    g.Y = g.Floor;
                    g.Blast = 0.001;
                    w.Heard.Add(Sfx.Grenade);
                    w.Bursts.Add(new Burst(g.X, g.Floor, 0, 1));
                }
            }
            if (g.Y > Field.Floor + 120) g.Gone = true;
        }
        foreach (var f in w.Fireballs)
        {
            f.X += f.Vx * dt;
            f.Vy += 1200 * dt;
            f.Y += f.Vy * dt;
            if (f.Vy > 0 && f.Y > Field.Floor + 30) f.Gone = true;
            double mid = (a.Top + a.Y) / 2, half = (a.Y - a.Top) / 2;
            if (!a.Dead && Math.Abs(f.X - a.X) < 26 && Math.Abs(f.Y - mid) < half + 12) Touch(w, "a fireball", f.X);
        }
    }

    // ───────────────────────────── Leila ─────────────────────────────

    /// <summary>Maboo beaten: Leila out of her cage and running to the agent.</summary>
    private static void Reunion(World w, double dt)
    {
        var a = w.Agent;
        w.Rescue += dt;
        a.Crouch = false;
        a.Moving = false;
        a.Facing = 1;
        if (!a.Ground)
        {
            double prev = a.Y;
            a.Vy += Gravity * dt;
            a.Y += a.Vy * dt;
            if (a.Vy > 0 && Land(w.Stage, a.X, prev, ref a.Y, false)) { a.Ground = true; a.Vy = 0; }
        }
        if (w.Rescue > 0.8 && w.LeilaX > a.X + 64) w.LeilaX = Math.Max(a.X + 64, w.LeilaX - 210 * dt);
    }

    /// <summary>Where Leila is held: in a cage at the end of the last area.</summary>
    public static double CageX(Stage s) => s.Length - 220;
}
