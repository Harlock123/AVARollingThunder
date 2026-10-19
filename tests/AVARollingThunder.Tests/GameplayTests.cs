using Avalonia.Input;
using AVARollingThunder.Engine;

namespace AVARollingThunder.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avarollingthunder-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>An area with nobody in it: no masker comes out of a door unless a test puts him there.</summary>
    private static World Area(int area = 1)
    {
        var w = new World();
        Sim.Start(w);
        if (area != 1) Sim.StartStage(w, 1, area);
        w.Spawn = 1e9;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>A masker standing on a storey: he won't fire, and with a long range he won't come closer either.</summary>
    private static Masker Put(World w, double x, Hood hood = Hood.Blue, int storey = 0, double range = 5000)
    {
        var m = new Masker { X = x, Y = storey == 1 ? Field.Walkway : Field.Floor, Hood = hood, Mood = Mood.Walk, Hp = hood == Hood.Red ? 2 : 1, Cool = 99, Range = range };
        w.Maskers.Add(m);
        return m;
    }

    /// <summary>A stretch of lower floor with no pit or door near, and no walkway over it.</summary>
    private static double Clear(Stage s) =>
        Enumerable.Range(3, 60).Select(i => i * Stage.Cell + 50).First(x => !s.WalkwayAt(x) && !s.WalkwayAt(x + 300) && Enumerable.Range(-4, 9).All(k => s.PitAt(x + k * 40) < 0));

    private static double UnderWalkway(Stage s) => s.Walkways[0].From + 150;

    // ───────────────────────────── walking, jumping, the two storeys ─────────────────────────────

    [Fact]
    public void The_agent_walks_and_crouching_stops_him()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        double x = a.X;
        Run(w, 60, new Controls(Dx: 1));
        Assert.Equal(x + Sim.WalkSpeed, a.X, 0.5);
        Run(w, 30, new Controls(Dx: 1, Dy: 1));
        Assert.True(a.Crouch);
        Assert.Equal(x + Sim.WalkSpeed, a.X, 0.5);
        Assert.Equal(a.Y - Agent.Short, a.Top);
        Run(w, 30, new Controls(Dx: -1, Dy: 1));
        Assert.Equal(-1, a.Facing);
    }

    [Fact]
    public void Up_and_jump_leaps_to_the_walkway_and_down_and_jump_drops_back()
    {
        var w = Area();
        var a = w.Agent;
        a.X = UnderWalkway(w.Stage);
        Sim.Step(w, new Controls(Dy: -1, Jump: true));
        Run(w, 90);
        Assert.True(a.Ground);
        Assert.Equal(Field.Walkway, a.Y);
        Assert.Equal(1, a.Storey);
        Sim.Step(w, new Controls(Dy: 1, Jump: true));
        Run(w, 90);
        Assert.Equal(Field.Floor, a.Y);
        Assert.Equal(0, a.Storey);
    }

    [Fact]
    public void Where_there_is_no_walkway_above_up_and_jump_is_an_ordinary_jump()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        double top = a.Y;
        Sim.Step(w, new Controls(Dy: -1, Jump: true));
        for (int i = 0; i < 90; i++) { Sim.Step(w, default); top = Math.Min(top, a.Y); }
        Assert.Equal(Field.Floor, a.Y);
        Assert.InRange(Field.Floor - top, 60, 120);
    }

    [Fact]
    public void Walking_off_the_end_of_the_walkway_drops_him_to_the_floor()
    {
        var w = Area();
        var a = w.Agent;
        var (_, to) = w.Stage.Walkways[0];
        a.X = to - 40;
        a.Y = Field.Walkway;
        Run(w, 60, new Controls(Dx: 1));
        Run(w, 60);
        Assert.Equal(Field.Floor, a.Y);
        Assert.False(a.Dead);
    }

    [Fact]
    public void Walk_into_a_pit_and_its_the_end_but_a_jump_clears_it()
    {
        var w = Area();
        var pit = w.Stage.Pits[0];
        w.Agent.X = pit.X - 120;
        Run(w, 120, new Controls(Dx: 1));
        Assert.True(w.Agent.Dead);
        Assert.Equal("a pit", w.Cause);
        w = Area();
        var a = w.Agent;
        a.X = pit.X - 120;
        while (a.X < pit.X - 62) Sim.Step(w, new Controls(Dx: 1));
        Sim.Step(w, new Controls(Dx: 1, Jump: true));
        Run(w, 90, new Controls(Dx: 1));
        Assert.False(a.Dead);
        Assert.True(a.X > pit.X + Pit.Wide / 2);
    }

    // ───────────────────────────── doors ─────────────────────────────

    [Fact]
    public void Up_at_a_door_hides_him_bullets_pass_him_by_and_down_brings_him_out()
    {
        var w = Area();
        var a = w.Agent;
        int i = w.Doors.FindIndex(d => d.Storey == 0 && d.Kind == DoorKind.Plain);
        a.X = w.Doors[i].X + 10;
        Sim.Step(w, new Controls(Dy: -1));
        Assert.True(a.Hidden);
        Assert.Equal(i, a.Door);
        w.Shots.Add(new Shot { X = a.X + 100, Y = a.Y - Sim.ShotHigh, Vx = -Sim.ShotSpeed });
        Run(w, 60, new Controls(Dy: -1));
        Assert.False(a.Dead);
        Assert.True(a.Hidden);
        // A masker walking into him touches nothing.
        Put(w, a.X + 60, range: 0);
        Run(w, 60);
        Assert.Equal(Sim.Life0, a.Life);
        w.Maskers.Clear();
        Run(w, 5, new Controls(Dy: 1));
        Assert.False(a.Hidden);
    }

    [Fact]
    public void A_BULLETS_door_gives_bullets_once_and_an_ARMS_door_the_machine_gun()
    {
        var w = Area();
        var a = w.Agent;
        var bullets = w.Doors.First(d => d.Kind == DoorKind.Bullets && d.Storey == 0);
        a.X = bullets.X;
        a.Bullets = 10;
        Sim.Step(w, new Controls(Dy: -1));
        Assert.Equal(10 + Sim.BulletsDoor, a.Bullets);
        Assert.Contains(Sfx.Ammo, w.Heard);
        Run(w, 30);
        Run(w, 5, new Controls(Dy: 1));
        Run(w, 5);
        Sim.Step(w, new Controls(Dy: -1));
        Assert.True(a.Hidden);
        Assert.Equal(10 + Sim.BulletsDoor, a.Bullets);
        Assert.True(bullets.Used);
        var arms = w.Doors.First(d => d.Kind == DoorKind.Arms && d.Storey == 0);
        a.Door = -1;
        a.X = arms.X;
        Sim.Step(w, new Controls(Dy: -1));
        Assert.Equal(Gun.MachineGun, a.Gun);
        Assert.Equal(Sim.RoundsDoor, a.Rounds);
    }

    [Fact]
    public void Maskers_come_out_of_the_doors_near_him_but_never_the_one_hes_in()
    {
        var w = Area();
        var a = w.Agent;
        a.X = 1500;
        w.Spawn = 0;
        var doors = new HashSet<int>();
        int hiding = w.Doors.FindIndex(d => d.Storey == 0 && Math.Abs(d.X - a.X) < 300 && d.Kind == DoorKind.Plain);
        a.X = w.Doors[hiding].X;
        Sim.Step(w, new Controls(Dy: -1));
        for (int t = 0; t < 60 * 120; t++)
        {
            Sim.Step(w, new Controls(Dy: -1));
            foreach (var m in w.Maskers) if (m.Door >= 0) doors.Add(m.Door);
            w.Maskers.RemoveAll(m => m.Mood == Mood.Walk);
        }
        Assert.NotEmpty(doors);
        Assert.DoesNotContain(hiding, doors);
        Assert.All(doors, d => Assert.Equal(DoorKind.Plain, w.Doors[d].Kind));
        Assert.All(doors, d => Assert.InRange(Math.Abs(w.Doors[d].X - a.X), 230, 640));
    }

    // ───────────────────────────── the guns ─────────────────────────────

    [Fact]
    public void The_pistol_has_one_bullet_in_the_air_at_a_time()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        int bullets = a.Bullets;
        for (int i = 0; i < 20; i++) Sim.Step(w, new Controls(Fire: i % 2 == 0));
        Assert.Single(w.Bullets);
        Assert.Equal(bullets - 1, a.Bullets);
        Run(w, 90);
        Assert.Empty(w.Bullets);
        Sim.Step(w, new Controls(Fire: true));
        Assert.Equal(bullets - 2, a.Bullets);
    }

    [Fact]
    public void Out_of_bullets_the_pistol_still_fires_but_slowly()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        a.Bullets = 0;
        Sim.Step(w, new Controls(Fire: true));
        var s = Assert.Single(w.Bullets);
        Assert.True(s.Slow);
        Assert.Equal(Sim.EmptySpeed, s.Vx);
        Assert.Equal(0, a.Bullets);
        Assert.Contains(Sfx.Empty, w.Heard);
    }

    [Fact]
    public void The_machine_gun_fires_while_held_until_its_rounds_run_out()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        a.Gun = Gun.MachineGun;
        a.Rounds = 5;
        Run(w, 12, new Controls(Fire: true));
        Assert.Equal(3, a.Rounds);
        Assert.Equal(2, w.Bullets.Count);
        Run(w, 60, new Controls(Fire: true));
        Assert.Equal(0, a.Rounds);
        Assert.Equal(Gun.Pistol, a.Gun);
    }

    // ───────────────────────────── bullets kill; touches drain ─────────────────────────────

    [Fact]
    public void One_bullet_kills_even_with_a_full_life_meter_but_he_can_duck_a_high_one()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        w.Shots.Add(new Shot { X = a.X + 150, Y = a.Y - Sim.ShotHigh, Vx = -Sim.ShotSpeed });
        Run(w, 40);
        Assert.True(a.Dead);
        Assert.Equal("a bullet", w.Cause);
        w = Area();
        a = w.Agent;
        a.X = Clear(w.Stage);
        w.Shots.Add(new Shot { X = a.X + 150, Y = a.Y - Sim.ShotHigh, Vx = -Sim.ShotSpeed });
        Run(w, 60, new Controls(Dy: 1));
        Assert.False(a.Dead);
    }

    [Fact]
    public void A_low_shot_from_a_crouch_must_be_jumped()
    {
        foreach (bool jump in new[] { false, true })
        {
            var w = Area();
            var a = w.Agent;
            a.X = Clear(w.Stage);
            w.Shots.Add(new Shot { X = a.X + 110, Y = a.Y - Sim.ShotLow, Vx = -Sim.ShotSpeed });
            Sim.Step(w, jump ? new Controls(Jump: true) : new Controls(Dy: 1));
            Run(w, 60, jump ? default : new Controls(Dy: 1));
            Assert.Equal(!jump, a.Dead);
        }
    }

    [Fact]
    public void A_touch_takes_half_the_life_meter_and_a_second_touch_the_rest()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        Put(w, a.X + 80, range: 0);
        Run(w, 50);
        Assert.Equal(Sim.Life0 - 1, a.Life);
        Assert.False(a.Dead);
        Assert.True(a.Hurt > 0);
        Run(w, 200);
        Assert.True(a.Dead);
        Assert.Equal("a masker", w.Cause);
    }

    [Fact]
    public void A_grenade_and_a_fireball_drain_the_life_meter_rather_than_kill()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        var m = Put(w, a.X + 320, Hood.Green, range: 300);
        m.Cool = 0;
        for (int i = 0; i < 150 && a.Life == Sim.Life0; i++) Sim.Step(w, default);
        Assert.Equal(Sim.Life0 - 1, a.Life);
        Assert.False(a.Dead);
        w = Area(2);
        a = w.Agent;
        a.X = Clear(w.Stage);
        w.Fireballs.Add(new Fireball { X = a.X, Y = a.Y - 50 });
        Sim.Step(w, default);
        Assert.Equal(Sim.Life0 - 1, a.Life);
        Assert.Equal("", w.Cause);
    }

    [Fact]
    public void Fire_pits_throw_up_fireballs()
    {
        var w = Area(2);
        var pit = w.Stage.Pits.First(p => p.Kind == PitKind.Fire);
        w.Agent.X = pit.X - 300;
        int most = 0;
        for (int i = 0; i < 60 * 4; i++) { Sim.Step(w, new Controls(Dy: 1)); most = Math.Max(most, w.Fireballs.Count(f => Math.Abs(f.X - pit.X) < 60)); }
        Assert.True(most >= 1);
    }

    // ───────────────────────────── the Geldra ─────────────────────────────

    [Fact]
    public void A_masker_shot_scores_by_his_hood_and_a_red_one_takes_two_hits()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        var m = Put(w, a.X + 200);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 30);
        Assert.Equal(Mood.Down, m.Mood);
        Assert.Equal(Sim.Points(Hood.Blue), w.Score);
        Run(w, 60);
        Assert.Empty(w.Maskers);
        var red = Put(w, a.X + 200, Hood.Red);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 30);
        Assert.True(red.Alive);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 30);
        Assert.False(red.Alive);
        Assert.Equal(Sim.Points(Hood.Blue) + Sim.Points(Hood.Red), w.Score);
    }

    [Fact]
    public void A_masker_who_sees_him_walks_up_aims_and_fires()
    {
        var w = Area();
        var a = w.Agent;
        a.X = Clear(w.Stage);
        var m = Put(w, a.X + 500, range: 200);
        m.Cool = 0.5;
        double x = m.X;
        bool aimed = false;
        for (int i = 0; i < 120 && w.Shots.Count == 0; i++) { Sim.Step(w, default); aimed |= m.Mood == Mood.Aim; }
        Assert.True(m.X < x);
        Assert.True(aimed);
        var s = Assert.Single(w.Shots);
        Assert.True(s.Vx < 0);
        Assert.Equal(a.Y - Sim.ShotHigh, s.Y);
    }

    [Fact]
    public void A_panther_runs_under_a_standing_shot_crouch_to_hit_it()
    {
        var w = Area(3);
        var a = w.Agent;
        a.X = Clear(w.Stage);
        var p = new Beast { Kind = BeastKind.Panther, X = a.X + 400, Y = Field.Floor, Vx = 0 };
        w.Beasts.Add(p);
        Sim.Step(w, new Controls(Fire: true));
        Run(w, 90);
        Assert.False(p.Gone);
        Assert.Empty(w.Bullets);
        Sim.Step(w, new Controls(Dy: 1, Fire: true));
        Run(w, 50, new Controls(Dy: 1));
        Assert.True(p.Gone);
        Assert.Equal(Sim.PantherPoints, w.Score);
    }

    [Fact]
    public void Bats_swarm_at_the_cue_and_a_lava_man_climbs_out_of_his_pit()
    {
        var w = Area(2);
        var cue = w.Stage.Cues.First(c => c.Kind == 'b');
        w.Agent.X = cue.X - 500;
        Run(w, 40, new Controls(Dx: 1));
        Assert.True(w.Beasts.Count(b => b.Kind == BeastKind.Bat) >= 4);
        w = Area(4);
        var pit = w.Stage.Pits.First(p => p.Kind == PitKind.Lava);
        w.Agent.X = pit.X - 700;
        Run(w, 2);
        Assert.Empty(w.Beasts);
        w.Agent.X = pit.X - 500;
        Run(w, 2);
        var man = Assert.Single(w.Beasts);
        Assert.Equal(BeastKind.LavaMan, man.Kind);
        Assert.Equal(4, man.Hp);
        Run(w, 90);
        Assert.Equal(Field.Floor, man.Y);
    }

    // ───────────────────────────── the clock and the way through ─────────────────────────────

    [Fact]
    public void The_clock_warns_at_thirty_seconds_and_running_out_is_the_end()
    {
        var w = Area();
        w.Agent.X = Clear(w.Stage);
        w.TimeLeft = Sim.WarnAt + 0.5;
        Run(w, 40);
        Assert.Contains(Sfx.TimeWarning, w.Heard);
        w.TimeLeft = 0.5;
        Run(w, 40);
        Assert.True(w.Agent.Dead);
        Assert.Equal("time", w.Cause);
    }

    [Fact]
    public void Reaching_the_exit_clears_the_area_with_a_bonus_for_the_time_left()
    {
        var w = Area();
        w.Agent.X = w.Stage.Exit - 5;
        w.TimeLeft = 100;
        Run(w, 10, new Controls(Dx: 1));
        Assert.True(w.Cleared);
        Assert.Equal(100 * Sim.TimePoints, w.Score);
    }

    [Fact]
    public void After_a_death_past_halfway_he_starts_again_from_halfway_with_the_clock_reset()
    {
        var w = Area();
        var a = w.Agent;
        Run(w, 2);
        Assert.Equal(150, w.Checkpoint);
        a.X = w.Stage.Length / 2 + 120;
        a.Gun = Gun.MachineGun;
        a.Rounds = 20;
        a.Bullets = 3;
        Run(w, 2);
        w.TimeLeft = 20;
        Sim.Kill(w, "a bullet");
        Sim.Restart(w);
        a = w.Agent;
        Assert.False(a.Dead);
        Assert.InRange(a.X, w.Stage.Length / 2, w.Stage.Length / 2 + 200);
        Assert.Equal(Sim.Life0, a.Life);
        Assert.Equal(Gun.Pistol, a.Gun);
        Assert.Equal(Sim.Bullets0, a.Bullets);
        Assert.Equal(w.Stage.Time, w.TimeLeft);
    }

    // ───────────────────────────── Maboo ─────────────────────────────

    [Fact]
    public void Maboo_wakes_with_a_laugh_and_bars_the_way_to_Leila()
    {
        var w = Area(5);
        var b = w.Boss!;
        var a = w.Agent;
        a.X = b.X - 700;
        Run(w, 2);
        Assert.Equal(BossMood.Asleep, b.Mood);
        a.X = b.X - 600;
        Run(w, 2);
        Assert.NotEqual(BossMood.Asleep, b.Mood);
        Assert.Contains(Sfx.Laugh, w.Heard);
        a.Door = 0;
        for (int i = 0; i < 60 * 6; i++)
        {
            a.Door = -1;
            w.Shots.Clear();
            Sim.Step(w, new Controls(Dx: 1));
            a.Hurt = 1;
        }
        Assert.True(a.X < w.Boss!.X - 50);
        Assert.False(w.Rescued);
    }

    [Fact]
    public void A_pistol_bullet_hurts_Maboo_three_times_as_much_as_a_machine_gun_round_and_beating_him_frees_Leila()
    {
        var w = Area(5);
        var b = w.Boss!;
        var a = w.Agent;
        a.X = b.X - 300;
        b.Mood = BossMood.Stride;
        b.Cool = 99;
        int hp = b.Hp;
        w.Bullets.Add(new Shot { X = b.X - 36, Y = b.Y - Sim.ShotHigh, Vx = Sim.PistolSpeed, Harm = 3 });
        Sim.Step(w, default);
        Assert.Equal(hp - 3, b.Hp);
        w.Bullets.Add(new Shot { X = b.X - 36, Y = b.Y - Sim.ShotHigh, Vx = Sim.MachineGunSpeed });
        Sim.Step(w, default);
        Assert.Equal(hp - 4, b.Hp);
        b.Hp = 2;
        w.TimeLeft = 100;
        int score = w.Score;
        w.Bullets.Add(new Shot { X = b.X - 36, Y = b.Y - Sim.ShotHigh, Vx = Sim.PistolSpeed, Harm = 3 });
        Sim.Step(w, default);
        Assert.False(b.Alive);
        Run(w, 120);
        Assert.True(w.Rescued);
        Assert.InRange(w.Score - score, Sim.BossPoints + Sim.RescuePoints + 99 * Sim.TimePoints, Sim.BossPoints + Sim.RescuePoints + 100 * Sim.TimePoints);
        Run(w, 600);
        Assert.True(w.LeilaX - a.X < 80);
    }

    [Fact]
    public void Maboo_fires_in_volleys_high_and_low()
    {
        var w = Area(5);
        var b = w.Boss!;
        w.Agent.X = b.X - 600;
        var heights = new HashSet<double>();
        for (int i = 0; i < 60 * 20; i++)
        {
            w.Agent.Door = 0;
            Sim.Step(w, default);
            foreach (var s in w.Shots) heights.Add(Math.Round(b.Y - s.Y));
        }
        Assert.Contains(Sim.ShotHigh, heights);
        Assert.Contains(Sim.ShotLow, heights);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Clearing_an_area_shows_the_bonus_then_the_next_areas_card()
    {
        var g = Engine();
        g.Go();
        g.World.Agent.X = g.World.Stage.Exit - 5;
        g.Run(10, new Controls(Dx: 1));
        Assert.Equal(Phase.Cleared, g.Phase);
        g.Run((int)(60 * GameEngine.ClearedSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(2, g.World.Area);
        Assert.Equal("THE CAVERNS", g.World.Stage.Name);
    }

    [Fact]
    public void Beating_Maboo_begins_the_second_story_harder()
    {
        var g = Engine();
        g.Go(5);
        var w = g.World;
        var b = w.Boss!;
        w.Agent.X = b.X - 300;
        b.Mood = BossMood.Stride;
        b.Hp = 1;
        w.Bullets.Add(new Shot { X = b.X - 36, Y = b.Y - Sim.ShotHigh, Vx = Sim.PistolSpeed, Harm = 3 });
        for (int i = 0; i < 300 && g.Phase != Phase.Rescued; i++) g.Tick(default);
        Assert.Equal(Phase.Rescued, g.Phase);
        g.Run((int)(60 * GameEngine.RescueSeconds) + 5);
        Assert.Equal(2, g.World.Story);
        Assert.Equal(1, g.World.Area);
        Assert.True(g.World.Hard);
        Assert.Equal(Stage.Of(1).Time * 0.9, g.World.TimeLeft, 3);
        Assert.Equal(6, g.Reached);
    }

    [Fact]
    public void Three_deaths_and_the_game_is_over_then_a_high_score_is_entered()
    {
        var g = Engine();
        g.Go();
        g.World.Score = 60000;
        for (int i = 0; i < 60 * 300 && g.State == GameState.Playing; i++) g.Tick(new Controls(Dx: 1));
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(0, g.Lives);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('A');
        g.Initials.Type('L');
        g.Initials.Type('B');
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("ALB", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(0, g.NewEntryIndex);
    }

    [Fact]
    public void Every_thirty_thousand_points_brings_another_agent()
    {
        var g = Engine();
        g.Silent = false;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go();
        g.World.Agent.X = Clear(g.World.Stage);
        g.World.Score = GameEngine.ExtraLifeEvery;
        g.Tick(default);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
        Assert.Contains(Sfx.OneUp, heard);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Enter_starts_a_game_with_the_first_areas_card_and_fire_on_the_title_does_too()
    {
        var g = Engine();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.Start, g.Phase);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        Assert.Equal(Phase.Play, g.Phase);
        g = Engine();
        g.Idle(40);
        g.Tick(new Controls(Fire: true));
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void Pausing_stops_the_clock()
    {
        var g = Engine();
        g.Go();
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        double t = g.World.TimeLeft;
        g.Run(120, new Controls(Dx: 1));
        Assert.Equal(t, g.World.TimeLeft);
        g.Resume();
        g.Run(60);
        Assert.True(g.World.TimeLeft < t);
    }

    [Fact]
    public void The_keys_up_and_jump_go_up_a_storey_down_crouches()
    {
        using var input = new InputHandler();
        var w = Area();
        var a = w.Agent;
        a.X = UnderWalkway(w.Stage);
        input.OnKeyDown(Key.Up);
        input.OnKeyDown(Key.X);
        var c = input.Player;
        Assert.Equal(-1, c.Dy);
        Assert.True(c.Jump);
        Sim.Step(w, c);
        input.OnKeyUp(Key.Up);
        input.OnKeyUp(Key.X);
        Run(w, 90, input.Player);
        Assert.Equal(Field.Walkway, a.Y);
        Assert.True(a.Y < Field.Floor, "up the screen");
        input.OnKeyDown(Key.Down);
        input.OnKeyDown(Key.Z);
        c = input.Player;
        Assert.Equal(1, c.Dy);
        Assert.True(c.Fire);
        Sim.Step(w, c);
        Assert.True(a.Crouch);
        input.OnKeyUp(Key.Down);
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
    }

    // ───────────────────────────── the areas themselves ─────────────────────────────

    [Fact]
    public void Every_area_is_laid_out_fairly()
    {
        for (int area = 1; area <= Stage.Areas; area++)
        {
            var s = Stage.Of(area);
            Assert.Contains(s.Doors, d => d.Kind == DoorKind.Bullets);
            Assert.Contains(s.Doors, d => d.Kind == DoorKind.Arms);
            Assert.All(s.Doors.Where(d => d.Storey == 1), d => Assert.True(s.WalkwayAt(d.X)));
            Assert.All(s.Doors.Where(d => d.Storey == 0), d => Assert.True(s.PitAt(d.X - 40) < 0 && s.PitAt(d.X + 40) < 0));
            // Each pit has floor either side, wide enough to run and jump from.
            Assert.All(s.Pits, p => Assert.True(s.PitAt(p.X - Stage.Cell) < 0 && s.PitAt(p.X + Stage.Cell) < 0));
            Assert.True(s.PitAt(150) < 0 && s.PitAt(s.Exit) < 0);
            Assert.Equal(area == Stage.Areas, s.BossX != null);
        }
    }

    [Fact]
    public void Every_sound_the_game_asks_for_is_there()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), $"no sound for {s}");
    }

    [Fact]
    public void A_copy_of_the_world_plays_out_exactly_the_same()
    {
        var w = Area(3);
        w.Spawn = 1;
        Run(w, 200, new Controls(Dx: 1));
        var c = w.Copy();
        for (int i = 0; i < 900; i++)
        {
            var r = Pilot.Reflex(w);
            Sim.Step(w, r);
            Sim.Step(c, r);
        }
        Assert.Equal(w.Agent.X, c.Agent.X);
        Assert.Equal(w.Score, c.Score);
        Assert.Equal(w.Maskers.Count, c.Maskers.Count);
        Assert.Equal(w.Agent.Dead, c.Agent.Dead);
    }

    // ───────────────────────────── the computer agent ─────────────────────────────

    [Fact]
    public void The_computer_agent_fights_through_the_first_story_and_frees_Leila_without_a_death()
    {
        var w = new World();
        Sim.Start(w);
        var p = new Pilot();
        int deaths = 0;
        for (int t = 0; t < 60 * 60 * 8 && !w.Rescued && deaths < 3; t++)
        {
            Sim.Step(w, p.Decide(w));
            if (w.Agent.Dead) { deaths++; Sim.Restart(w); p.Reset(); }
            if (w.Cleared) { Sim.StartStage(w, 1, w.Area + 1); p.Reset(); }
        }
        Assert.True(w.Rescued);
        Assert.Equal(0, deaths);
        Assert.Equal(Stage.Areas, w.Area);
    }

    [Fact]
    public void Running_and_gunning_without_ducking_doesnt_get_far()
    {
        var w = new World();
        Sim.Start(w);
        int deaths = 0;
        for (int t = 0; t < 60 * 60 * 6 && !w.Rescued && deaths < 10; t++)
        {
            Sim.Step(w, new Controls(Dx: 1, Fire: t % 8 < 4, Jump: w.Stage.PitAt(w.Agent.X + 30) >= 0));
            if (w.Agent.Dead) { deaths++; Sim.Restart(w); }
            if (w.Cleared) Sim.StartStage(w, 1, w.Area + 1);
        }
        Assert.False(w.Rescued);
        Assert.True(deaths >= 3);
        Assert.True(w.Area <= 3);
    }
}
