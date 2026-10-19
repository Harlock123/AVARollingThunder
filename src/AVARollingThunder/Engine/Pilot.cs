namespace AVARollingThunder.Engine;

/// <summary>
/// The computer agent, for the demonstration and the tests. Left to itself it has an agent's reflexes: it walks on,
/// shoots the nearest of the Geldra in front of it (crouching for a panther), ducks a high bullet and jumps a low
/// one, jumps the pits, and calls in at every BULLETS and ARMS door it passes. When there's trouble near, it tries
/// every move it has on a copy of the moment, each followed by those same reflexes for most of a second, and takes
/// whichever gets furthest and hits most without being hit. The Geldra roll the same dice in the copy, so it sees
/// exactly what's coming.
/// </summary>
public sealed class Pilot
{
    public const int Hold = 8, Ahead = 54, Every = 3;
    private static readonly Controls[] Acts =
    [
        new(Dx: 1), new(Dx: 1, Fire: true), new(Fire: true), new(Dy: 1, Fire: true), new(Dx: -1), new(Dx: -1, Dy: 1, Fire: true),
        new(Dx: 1, Dy: 1, Fire: true), new(Jump: true), new(Dx: 1, Jump: true), new(Dx: -1, Jump: true), new(Dy: -1, Jump: true),
        new(Dx: 1, Dy: -1, Jump: true), new(Dy: 1, Jump: true), new(Dy: -1), new(Fire: true, Jump: true),
    ];
    private Controls _choice;
    private int _wait;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _wait = 0;
        _choice = default;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var a = w.Agent;
        if (a.Dead || w.Cleared || w.Rescued) return default;
        if (--_wait > 0) return Pulse(a, _choice with { Jump = false });
        _wait = Every;
        if (!Trouble(w))
        {
            _choice = Reflex(w);
            Doing = a.Hidden ? "in a door" : "walking on";
            return Pulse(a, _choice);
        }
        double best = double.NegativeInfinity;
        var pick = Acts[0];
        foreach (var act in Acts)
        {
            double v = Value(w, act);
            if (v > best) { best = v; pick = act; }
        }
        _choice = pick;
        Doing = best < -5000 ? "cornered" : a.Hidden ? "hiding" : pick.Jump ? "jumping" : pick.Fire ? "shooting" : pick.Dy > 0 ? "ducking" : "dodging";
        return Pulse(a, pick);
    }

    /// <summary>The pistol fires on a press, so a held trigger is let go every other moment.</summary>
    private static Controls Pulse(Agent a, Controls c) => c.Fire && a.Gun == Gun.Pistol && a.FirePrev ? c with { Fire = false } : c;

    /// <summary>Anything about that could hurt: worth thinking about, rather than just walking on.</summary>
    private static bool Trouble(World w)
    {
        var a = w.Agent;
        if (w.Shots.Count > 0 || w.Grenades.Count > 0 || w.Beasts.Count > 0 || w.Boss is { Mood: not BossMood.Asleep }) return true;
        if (w.Fireballs.Any(f => Math.Abs(f.X - a.X) < 300)) return true;
        if (w.Maskers.Any(m => m.Alive && Math.Abs(m.X - a.X) < 700)) return true;
        return w.Stage.Pits.Any(p => p.X - a.X is > -60 and < 200);
    }

    private static double Value(World w, Controls act)
    {
        var c = w.Copy();
        var a = c.Agent;
        double x0 = a.X;
        int life = a.Life, score = c.Score, bullets = a.Bullets, rounds = a.Rounds, boss = c.Boss?.Hp ?? 0;
        double v = 0;
        for (int i = 0; i < Ahead; i++)
        {
            var ctl = i < Hold ? act with { Jump = act.Jump && i == 0 } : Reflex(c);
            Sim.Step(c, Pulse(c.Agent, ctl));
            if (c.Agent.Dead) { v -= 10000 - i * 20; break; }
            if (c.Cleared || c.Rescued) { v += 3000; break; }
        }
        a = c.Agent;
        v += (a.X - x0) * 1.0;
        v -= (life - a.Life) * 700;
        v += (c.Score - score) * 0.25;
        v += (a.Bullets - bullets) * (bullets < 30 ? 5 : 2) + (a.Rounds - rounds) * 3;
        if (c.Boss is { } b) v += (boss - b.Hp) * 60;
        if (a.Hidden) v -= 30;
        if (a.Storey == 1) v -= 10;
        return v;
    }

    /// <summary>An agent's reflexes: duck, jump, shoot, collect, walk on.</summary>
    public static Controls Reflex(World w)
    {
        var a = w.Agent;
        var s = w.Stage;
        if (a.Hidden)
        {
            bool quiet = !w.Shots.Any(sh => Math.Abs(sh.X - a.X) < 320) && !w.Maskers.Any(m => m.Alive && m.Storey == a.Storey && Math.Abs(m.X - a.X) < 140)
                         && !w.Grenades.Any(g => Math.Abs(g.X - a.X) < 120) && !w.Beasts.Any(b => Math.Abs(b.X - a.X) < 200);
            return quiet && a.DoorT > 0.4 ? new Controls(Dy: 1) : default;
        }
        // The nearest of them on this storey, in front or close behind.
        double best = 640;
        int dir = 0;
        bool low = false;
        foreach (var m in w.Maskers)
        {
            if (!m.Alive || !m.Out || m.Storey != a.Storey || !a.Ground && Math.Abs(m.Y - a.Y) > 50) continue;
            double d = m.X - a.X;
            if (d < -260 || Math.Abs(d) >= best) continue;
            best = Math.Abs(d);
            dir = d < 0 ? -1 : 1;
            low = false;
        }
        foreach (var b in w.Beasts)
        {
            double d = b.X - a.X;
            if (b.Rising || d < -260 || Math.Abs(d) >= best || b.Y - b.Tall > a.Y - 40 && b.Kind != BeastKind.Panther || Math.Abs(b.Y - a.Y) > 130) continue;
            best = Math.Abs(d);
            dir = d < 0 ? -1 : 1;
            low = b.Kind == BeastKind.Panther && b.Ground;
        }
        if (w.Boss is { Alive: true, Mood: not BossMood.Asleep } boss && boss.X - a.X < best) { best = boss.X - a.X; dir = 1; low = false; }
        bool fire = dir != 0;
        if (!a.Ground) return new Controls(Fire: fire && dir == a.Facing);
        // Bullets coming: duck the high ones, jump the low.
        foreach (var sh in w.Shots)
        {
            double toward = (a.X - sh.X) * Math.Sign(sh.Vx);
            if (toward < -10 || toward > 190) continue;
            double h = a.Y - sh.Y;
            if (Math.Abs(h - Sim.ShotHigh) < 10) return new Controls(Dx: dir != 0 && dir != a.Facing ? dir : 0, Dy: 1, Fire: fire);
            if (Math.Abs(h - Sim.ShotLow) < 10 && toward < 120) return new Controls(Jump: true, Fire: fire && dir == a.Facing);
        }
        if (fire)
        {
            if (low || dir != a.Facing) return new Controls(Dx: dir, Dy: 1, Fire: true);
            if (best < 420 || a.Gun == Gun.MachineGun && best < 520) return new Controls(Fire: true);
        }
        // Ammunition: into every BULLETS and ARMS door on the way.
        foreach (var d in w.Doors)
        {
            if (d.Used || d.Kind == DoorKind.Plain || d.Storey != a.Storey) continue;
            double dx = d.X - a.X;
            if (Math.Abs(dx) < Sim.DoorReach - 6) return new Controls(Dy: -1);
            if (dx is > 0 and < 60) return new Controls(Dx: 1);
        }
        int pit = s.PitAt(a.X + 30);
        if (a.Storey == 0 && pit >= 0 && s.PitAt(a.X) < 0) return new Controls(Dx: 1, Jump: true);
        return new Controls(Dx: 1, Fire: fire);
    }
}
