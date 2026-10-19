namespace AVARollingThunder.Engine;

/// <summary>
/// The screen and the two storeys. Y counts down the screen, the way it's drawn: the lower floor is at the bottom,
/// the walkway above it, and up (a smaller Y) is up.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>Where feet stand on the lower floor, and on the upper walkway.</summary>
    public const double Floor = 640, Walkway = 400;
    /// <summary>How far ahead of the screen's left edge the agent is kept.</summary>
    public const double Lead = 380;
}

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
    public double Range(double a, double b) => a + Next() * (b - a);
}

/// <summary>
/// The controls: left or right; up (into a door, or with jump up to the walkway) or down (crouch, out of a door, or
/// with jump down from the walkway); fire; jump.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Jump = false);

public enum Gun { Pistol, MachineGun }

/// <summary>The Rolling Thunder agent: Albatross.</summary>
public sealed class Agent
{
    public const double Tall = 104, Short = 60, Half = 14;
    public double X = 150, Y = Field.Floor, Vx, Vy;
    public int Facing = 1;
    public bool Ground = true, Crouch, Dead, FirePrev, Moving;
    /// <summary>The two-part life meter: a touch takes one part; a bullet takes everything.</summary>
    public int Life = Sim.Life0;
    /// <summary>After a touch: a moment no touch can hurt.</summary>
    public double Hurt;
    /// <summary>The door he's in (hidden), or -1; and how long he's been in it.</summary>
    public int Door = -1;
    public double DoorT;
    public Gun Gun = Gun.Pistol;
    /// <summary>Pistol bullets, and machine-gun rounds.</summary>
    public int Bullets = Sim.Bullets0, Rounds;
    public double Reload, Drop, Walk, Flash, DeadT, Landing;
    public bool High;

    public Agent Copy() => (Agent)MemberwiseClone();
    public bool Hidden => Door >= 0;
    public double Top => Y - (Crouch ? Short : Tall);
    public int Storey => Y < (Field.Floor + Field.Walkway) / 2 ? 1 : 0;
}

public enum DoorKind { Plain, Bullets, Arms }

public sealed class Door
{
    public double X;
    public int Storey;
    public DoorKind Kind;
    /// <summary>An ammunition door gives once.</summary>
    public bool Used;
    /// <summary>Open (drawn ajar) while this counts down; and a wait before another masker can come out.</summary>
    public double Open, Cool;
    public Door Copy() => (Door)MemberwiseClone();
    public double Feet => Storey == 1 ? Field.Walkway : Field.Floor;
}

/// <summary>The Geldra's hooded maskers, by the colour of the hood.</summary>
public enum Hood
{
    /// <summary>Walks up and fires.</summary>
    Blue,
    /// <summary>Throws grenades.</summary>
    Green,
    /// <summary>Drops to a crouch and fires low: jump it.</summary>
    Yellow,
    /// <summary>Leaps: at the agent, and from storey to storey. Takes two hits.</summary>
    Red,
}

public enum Mood { Emerge, Walk, Aim, Leap, Down }

public sealed class Masker
{
    public double X, Y, Vx, Vy, T, Cool, Range;
    public int Facing = -1, Hp = 1, Door = -1;
    public Hood Hood;
    public Mood Mood;
    public bool Ground = true, Low, Gone;
    public double Flash, Drop;
    public Masker Copy() => (Masker)MemberwiseClone();
    public bool Alive => Mood != Mood.Down && !Gone;
    public bool Crouched => Mood == Mood.Aim && Low;
    public double Top => Y - (Crouched ? Agent.Short : Agent.Tall);
    public int Storey => Y < (Field.Floor + Field.Walkway) / 2 ? 1 : 0;
    /// <summary>Out of the door far enough to shoot (or be shot).</summary>
    public bool Out => Mood != Mood.Emerge || T > 0.35;
}

/// <summary>A bullet in flight: the agent's, or the Geldra's.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Age;
    /// <summary>What it does to Maboo: a pistol bullet three times a machine-gun round.</summary>
    public int Harm = 1;
    public bool Slow, Gone;
    public Shot Copy() => (Shot)MemberwiseClone();
}

/// <summary>A grenade, arcing; then its blast.</summary>
public sealed class Grenade
{
    public double X, Y, Vx, Vy, Floor, Blast;
    public bool Gone;
    public Grenade Copy() => (Grenade)MemberwiseClone();
}

public enum BeastKind { Bat, Panther, LavaMan }

/// <summary>The rest of the Geldra's creatures: bats in the caves, black panthers on the street, lava men in the furnace.</summary>
public sealed class Beast
{
    public BeastKind Kind;
    public double X, Y, Vx, Vy, T, Base, Flash;
    public int Hp = 1, Pit = -1, Facing = -1;
    public bool Ground = true, Gone, Rising;
    public Beast Copy() => (Beast)MemberwiseClone();
    public double Tall => Kind switch { BeastKind.Bat => 30, BeastKind.Panther => 48, _ => 112 };
    public double Wide => Kind switch { BeastKind.Bat => 22, BeastKind.Panther => 40, _ => 22 };
}

/// <summary>A fireball leaping from a fire pit.</summary>
public sealed class Fireball
{
    public double X, Y, Vx, Vy;
    public bool Gone;
    public Fireball Copy() => (Fireball)MemberwiseClone();
}

public enum BossMood { Asleep, Stride, Aim, Volley, Summon, Down }

/// <summary>Maboo, the Geldra's leader, holding Leila at the end of the last area.</summary>
public sealed class Boss
{
    public const double Tall = 160, Half = 30;
    public double X, Y = Field.Floor, T, Cool, Flash;
    public int Hp, MaxHp, Facing = -1, Left, Volleys;
    public bool Low;
    public BossMood Mood;
    public Boss Copy() => (Boss)MemberwiseClone();
    public bool Alive => Mood != BossMood.Down;
}

/// <summary>A flash, a puff or a blast, just for the eye.</summary>
public readonly record struct Burst(double X, double Y, double T, int Kind);

/// <summary>
/// Everything in the area that moves or changes, kept together so it can be copied whole: the agent, the doors, the
/// maskers and the beasts, every bullet in the air, Maboo, the clock and the score.
/// </summary>
public sealed class World
{
    public int Story = 1, Area = 1;
    public Stage Stage = Stage.Of(1);
    public Agent Agent = new();
    public List<Door> Doors = [];
    public List<Masker> Maskers = [];
    public List<Shot> Bullets = [], Shots = [];
    public List<Grenade> Grenades = [];
    public List<Beast> Beasts = [];
    public List<Fireball> Fireballs = [];
    public List<Burst> Bursts = [];
    public Boss? Boss;
    public bool[] Fired = [], LavaUsed = [];
    public int Score, Kills;
    public double Clock, TimeLeft, Spawn, Checkpoint = 150, LeilaX, Rescue;
    public bool Cleared, Rescued, Warned;
    public Dice Dice = new(1);
    public string Cause = "";
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Story = Story, Area = Area, Stage = Stage, Agent = Agent.Copy(), Doors = Doors.Select(d => d.Copy()).ToList(),
        Maskers = Maskers.Select(m => m.Copy()).ToList(), Bullets = Bullets.Select(s => s.Copy()).ToList(),
        Shots = Shots.Select(s => s.Copy()).ToList(), Grenades = Grenades.Select(g => g.Copy()).ToList(),
        Beasts = Beasts.Select(b => b.Copy()).ToList(), Fireballs = Fireballs.Select(f => f.Copy()).ToList(), Boss = Boss?.Copy(),
        Fired = (bool[])Fired.Clone(), LavaUsed = (bool[])LavaUsed.Clone(), Score = Score, Kills = Kills, Clock = Clock,
        TimeLeft = TimeLeft, Spawn = Spawn, Checkpoint = Checkpoint, LeilaX = LeilaX, Rescue = Rescue, Cleared = Cleared,
        Rescued = Rescued, Warned = Warned, Dice = Dice, Cause = Cause,
    };

    /// <summary>The second story: the same areas, harder.</summary>
    public bool Hard => Story >= 2;

    /// <summary>The left edge of the screen in the area: following the agent, held at the area's ends.</summary>
    public double Camera => Math.Clamp(Agent.X - Field.Lead, 0, Stage.Length - Field.Width);
}
