namespace AVARollingThunder.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The area's card: a moment before the agent sets off (or sets off again).</summary>
    Start,
    Play,
    /// <summary>Down: a moment, then back at the start of the area (or halfway), or the game is over.</summary>
    Dying,
    /// <summary>Out of the area: the time bonus, then the next.</summary>
    Cleared,
    /// <summary>Maboo beaten and Leila free: then the second story, or the end.</summary>
    Rescued,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Pistol, MachineGun, Empty, EnemyShot, Throw, Grenade, Door, Jump, EnemyHit, Clang, Hurt, Death, Ammo,
    TimeWarning, StageClear, OneUp, Bats, Growl, Lava, Fireball, Laugh, BossDown, Rescue,
    SayHelp,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 3, AgainSeconds = 1.6, DyingSeconds = 2.6, ClearedSeconds = 4, RescueSeconds = 9, OverSeconds = 5;
    public const int Lives0 = 3, ExtraLifeEvery = 30000, Stories = 2;

    private double _accumulator, _idle, _startFor = StartSeconds;
    private bool _startPrev, _tunePending = true, _music;
    private int _nextLife = ExtraLifeEvery;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>How far the agent got, counting every area of both stories (shown with the high score).</summary>
    public int Reached => (World.Story - 1) * Stage.Areas + World.Area;
    /// <summary>Both stories done: the game is over because it's won.</summary>
    public bool Completed { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>How long the area's card stays up this time (shorter after a death).</summary>
    public double StartFor => _startFor;

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Reached);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Sim.Start(World);
        Lives = Lives0;
        Completed = false;
        _nextLife = ExtraLifeEvery;
        BeginStage(1, 1);
    }

    private void BeginStage(int story, int area)
    {
        if (story != World.Story || area != World.Area) Sim.StartStage(World, story, area);
        SetPhase(Phase.Start);
        _startFor = StartSeconds;
        Message = null;
        Pilot.Reset();
        if (area == Stage.Areas && State is GameState.Playing or GameState.Attract) Play(Sfx.SayHelp);
    }

    public void StartGame()
    {
        State = GameState.Playing;
        NewGame();
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        NewGame();
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > _startFor) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                Sim.Step(w, c);
                if (w.Score >= _nextLife)
                {
                    _nextLife += ExtraLifeEvery;
                    Lives++;
                    Play(Sfx.OneUp);
                }
                if (w.Cleared)
                {
                    SetPhase(Phase.Cleared);
                    Music(false);
                    Message = $"AREA {w.Area} CLEAR";
                    MessageTime = ClearedSeconds;
                }
                else if (w.Rescued)
                {
                    SetPhase(Phase.Rescued);
                    Music(false);
                    Play(Sfx.Victory);
                }
                else if (w.Agent.Dead) { SetPhase(Phase.Dying); Music(false); }
                break;
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Restart(w);
                    Pilot.Reset();
                    SetPhase(Phase.Start);
                    _startFor = AgainSeconds;
                }
                break;
            case Phase.Cleared:
                Sim.Step(w, default);
                if (PhaseTime > ClearedSeconds) { BeginStage(w.Story, w.Area + 1); return; }
                break;
            case Phase.Rescued:
                Sim.Step(w, default);
                if (PhaseTime > RescueSeconds)
                {
                    if (w.Story < Stories) { BeginStage(w.Story + 1, 1); return; }
                    Completed = true;
                    End();
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        if (!Completed) Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into an area, past its card.</summary>
    public void Go(int area = 1, int story = 1)
    {
        World = new World();
        Sim.Start(World);
        Lives = Lives0;
        Completed = false;
        _nextLife = ExtraLifeEvery;
        State = GameState.Playing;
        BeginStage(story, area);
        SetPhase(Phase.Play);
        Message = null;
    }

    /// <summary>Into an area showing its card, as a new game does.</summary>
    public void Card(int area = 1, int story = 1)
    {
        Go(area, story);
        SetPhase(Phase.Start);
    }

    public void SetLives(int n) => Lives = n;
}
