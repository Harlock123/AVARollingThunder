namespace AVARollingThunder.Engine;

public enum Scenery { Docks, Caverns, Street, Furnace, Headquarters }

public enum PitKind
{
    /// <summary>A drop to nowhere.</summary>
    Dark,
    /// <summary>Fireballs leap out of it.</summary>
    Fire,
    /// <summary>Molten; a lava man climbs out of it.</summary>
    Lava,
}

public readonly record struct Pit(double X, PitKind Kind)
{
    public const double Wide = 64;
}

/// <summary>Something that happens when the agent comes near: a swarm of bats, a panther, Maboo.</summary>
public readonly record struct Cue(double X, char Kind);

/// <summary>
/// One area of the story, laid out from three rows of text, a character to every 100 units: the walkway (=, with
/// D a door, B a BULLETS door and A an ARMS door), the floor (_, the same doors, and o, f and l for a dark pit, a
/// fire pit and a lava pit), and the cues (b bats, p a panther, M Maboo).
/// </summary>
public sealed class Stage
{
    public const double Cell = 100;
    public const int Areas = 5;

    public int Area { get; }
    public string Name { get; }
    public Scenery Scenery { get; }
    public double Length { get; }
    public double Time { get; }
    public IReadOnlyList<(double From, double To)> Walkways { get; }
    public IReadOnlyList<Door> Doors { get; }
    public IReadOnlyList<Pit> Pits { get; }
    public IReadOnlyList<Cue> Cues { get; }
    /// <summary>How often a masker comes out of a door, and how many can be about at once.</summary>
    public double SpawnEvery { get; }
    public int MaxFoes { get; }
    /// <summary>How likely each hood is (blue, green, yellow, red).</summary>
    public IReadOnlyList<int> Mix { get; }
    public double? BossX { get; }
    /// <summary>Where the area ends: the exit, or Leila's cage.</summary>
    public double Exit => Length - 150;

    private Stage(int area, string name, Scenery theme, double time, double spawnEvery, int maxFoes, int[] mix, string upper, string lower, string cues)
    {
        Area = area;
        Name = name;
        Scenery = theme;
        Time = time;
        SpawnEvery = spawnEvery;
        MaxFoes = maxFoes;
        Mix = mix;
        int n = Math.Max(upper.Length, Math.Max(lower.Length, cues.Length));
        upper = upper.PadRight(n);
        lower = lower.PadRight(n, '_');
        cues = cues.PadRight(n);
        Length = n * Cell;
        var walkways = new List<(double, double)>();
        var doors = new List<Door>();
        var pits = new List<Pit>();
        var events = new List<Cue>();
        double Mid(int i) => i * Cell + Cell / 2;
        for (int i = 0; i < n; i++)
        {
            if (upper[i] != ' ')
            {
                if (i > 0 && upper[i - 1] != ' ') walkways[^1] = (walkways[^1].Item1, (i + 1) * Cell);
                else walkways.Add((i * Cell, (i + 1) * Cell));
            }
            if (Kind(upper[i]) is { } u) doors.Add(new Door { X = Mid(i), Storey = 1, Kind = u });
            if (Kind(lower[i]) is { } l) doors.Add(new Door { X = Mid(i), Storey = 0, Kind = l });
            switch (lower[i])
            {
                case 'o': pits.Add(new Pit(Mid(i), PitKind.Dark)); break;
                case 'f': pits.Add(new Pit(Mid(i), PitKind.Fire)); break;
                case 'l': pits.Add(new Pit(Mid(i), PitKind.Lava)); break;
            }
            if (cues[i] == 'M') BossX = Mid(i);
            else if (cues[i] != ' ') events.Add(new Cue(Mid(i), cues[i]));
        }
        Walkways = walkways;
        Doors = doors;
        Pits = pits;
        Cues = events;
    }

    private static DoorKind? Kind(char c) => c switch { 'D' => DoorKind.Plain, 'B' => DoorKind.Bullets, 'A' => DoorKind.Arms, _ => null };

    public bool WalkwayAt(double x) => Walkways.Any(w => x >= w.From && x <= w.To);

    /// <summary>The pit under a point, if there is one.</summary>
    public int PitAt(double x)
    {
        for (int i = 0; i < Pits.Count; i++)
            if (Math.Abs(x - Pits[i].X) < Pit.Wide / 2) return i;
        return -1;
    }

    private static readonly Stage[] All =
    [
        new(1, "THE DOCKS", Scenery.Docks, 150, 1.5, 5, [6, 2, 0, 0],
            "      ===D====A===D===      ====D===B====D===       ===D=====D===        ",
            "___D___B__D___D__o___A__D___D___B__D___o__D____A__D___D___B___D________",
            ""),
        new(2, "THE CAVERNS", Scenery.Caverns, 150, 1.5, 5, [4, 2, 3, 0],
            "     ====D===B====      ===D====A===       ====D====B===      ==D===     ",
            "___D__f___B__D___o___D__f__A__D___f__D__B___o__D___f___A__D__f___D_____",
            "         b               b                 b                b           "),
        new(3, "THE NIGHT STREET", Scenery.Street, 150, 1.4, 5, [3, 1, 2, 3],
            "    ====D===A====D===      ===D===B===D====      ====D===A===D===        ",
            "___D___B__D__o__D___A__D____D__o___B__D___D____o__A__D___D___B___D_____",
            "            p              p           p               p         p      "),
        new(4, "THE FURNACE", Scenery.Furnace, 150, 1.4, 6, [2, 3, 3, 2],
            "     ===D====B===D===       ===D===A====D==       ===D====B===D===       ",
            "___D__l___B__D__f__D___A__l___D__D__f___B___l___D___A__f__D__l___D_____",
            ""),
        new(5, "GELDRA HEADQUARTERS", Scenery.Headquarters, 170, 1.3, 6, [3, 2, 2, 3],
            "     ===D===B====D===A===       ===D====B===D===                         ",
            "___D___B__D__f__D___A__D___o___D__B___D___l__D___A___D____D______D_____",
            "                  b                    p                   M           "),
    ];

    public static Stage Of(int area) => All[(area - 1) % Areas];
}
