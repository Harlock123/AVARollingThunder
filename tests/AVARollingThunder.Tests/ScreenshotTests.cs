using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARollingThunder.Engine;
using AVARollingThunder.Views;
using Controls = AVARollingThunder.Engine.Controls;

namespace AVARollingThunder.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set THUNDER_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avarollingthunder-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("THUNDER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARollingThunder-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 300)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (g.Phase == Phase.Play && done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int area, string name, Func<GameEngine, bool> moment, Action<GameEngine>? setUp = null)
    {
        var (w, c, g) = Open();
        g.Go(area);
        g.SetLives(9);
        setUp?.Invoke(g);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 61850, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        c.Advance(0.3);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_card_before_an_area()
    {
        var (w, c, g) = Open();
        g.Card(3);
        g.Run(70);
        c.Advance(1.2);
        Assert.Equal(Phase.Start, g.Phase);
        Shot(w, c, "stage-card");
    }

    [AvaloniaFact]
    public void A_gunfight_in_front_of_the_doors() =>
        Moment(1, "gunfight", g =>
        {
            var w = g.World;
            var near = w.Maskers.Where(m => m.Alive && m.Storey == 0 && Math.Abs(m.X - w.Agent.X) < 520).ToList();
            int doors = w.Doors.Count(d => d.X > w.Camera + 40 && d.X < w.Camera + Field.Width - 40);
            return w.Agent.Storey == 0 && w.Agent.Ground && near.Count >= 2 && w.Shots.Count + w.Grenades.Count > 0 && w.Bullets.Count > 0 && doors >= 4;
        });

    [AvaloniaFact]
    public void Up_on_the_walkway() =>
        Moment(3, "walkway", g =>
        {
            var w = g.World;
            return w.Agent.Storey == 1 && w.Agent.Ground && w.Maskers.Any(m => m.Alive && m.Out && m.Storey == 1 && Math.Abs(m.X - w.Agent.X) < 450)
                   && w.Maskers.Any(m => m.Alive && m.Storey == 0);
        }, g =>
        {
            // Start him up on the first walkway.
            var a = g.World.Agent;
            a.X = g.World.Stage.Walkways[0].From + 160;
            a.Y = Field.Walkway;
        });

    [AvaloniaFact]
    public void Hiding_in_a_door()
    {
        var (w, c, g) = Open();
        g.Go(1);
        var world = g.World;
        // Walk to the door at 1050, a masker coming along the street behind him.
        int door = world.Doors.FindIndex(d => d.Storey == 0 && d.X > 1000 && d.Kind == DoorKind.Plain);
        var at = world.Doors[door];
        world.Agent.X = at.X - 140;
        world.Spawn = 99;
        for (int i = 0; i < 60 && !world.Agent.Hidden; i++)
        {
            g.Tick(Math.Abs(world.Agent.X - at.X) < 10 ? new Controls(Dy: -1) : new Controls(Dx: 1));
            c.Advance(0.05);
        }
        Assert.True(world.Agent.Hidden);
        int other = world.Doors.FindIndex(d => d.Storey == 0 && d.X > at.X + 250);
        Sim.Emerge(world, other, Hood.Blue);
        for (int i = 0; i < 60 * 10 && !world.Maskers.Any(m => m.Mood == Mood.Walk && Math.Abs(m.X - at.X) < 70); i++)
        {
            g.Tick(default);
            if (i % 3 == 0) c.Advance(0.05);
        }
        Assert.True(world.Agent.Hidden);
        Shot(w, c, "hiding");
    }

    [AvaloniaFact]
    public void Maboo() =>
        Moment(5, "maboo", g => g.World.Boss is { Mood: BossMood.Volley } b && b.Hp < b.MaxHp * 0.8 && g.World.Shots.Count >= 2 && g.World.Agent.Ground,
            g => g.World.Agent.X = g.World.Stage.BossX!.Value - 1200);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(2);
        g.SetLives(1);
        g.World.Score = 23450;
        // Walk on firing at nothing in particular: the caverns finish him.
        for (int i = 0; i < 60 * 150 && g.State == GameState.Playing; i++) { g.Tick(new Controls(Dx: 1)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
