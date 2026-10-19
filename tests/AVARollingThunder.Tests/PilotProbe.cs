using AVARollingThunder.Engine;

namespace AVARollingThunder.Tests;

/// <summary>
/// How the computer agent gets on (set THUNDER_PROBE to run it: THUNDER_PROBE=1 plays the first story, =2 both;
/// THUNDER_PROBE=reflex plays on its reflexes alone, without looking ahead; THUNDER_PROBE=blind just walks on firing,
/// jumping the pits).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_agent_does()
    {
        if (Environment.GetEnvironmentVariable("THUNDER_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        Sim.Start(w);
        var p = new Pilot();
        int stories = mode == "2" ? 2 : 1, deaths = 0;
        double areaStart = 0, t = 0;
        while (t < 60 * 60 * 30 && deaths < 30)
        {
            Sim.Step(w, mode == "reflex" ? Pilot.Reflex(w) : mode == "blind" ? new Controls(Dx: 1, Fire: t % 8 < 4, Jump: w.Stage.PitAt(w.Agent.X + 30) >= 0) : p.Decide(w));
            t++;
            if (w.Agent.Dead)
            {
                deaths++;
                output.WriteLine($"   died: {w.Cause} at {w.Agent.X:0} in {w.Story}-{w.Area} [{p.Doing}], {w.TimeLeft:0}s left");
                Sim.Restart(w);
                p.Reset();
            }
            else if (w.Cleared || w.Rescued && w.Rescue > 0.1)
            {
                output.WriteLine($"{w.Story}-{w.Area} {w.Stage.Name}: {(t - areaStart) / 60:0}s, {w.TimeLeft:0}s left, score {w.Score}, bullets {w.Agent.Bullets}, rounds {w.Agent.Rounds}");
                areaStart = t;
                if (w.Rescued && w.Story >= stories) break;
                if (w.Rescued) Sim.StartStage(w, w.Story + 1, 1);
                else Sim.StartStage(w, w.Story, w.Area + 1);
                p.Reset();
            }
        }
        output.WriteLine($"{(w.Rescued ? "RESCUED" : "not rescued")} after {t / 60:0}s; score {w.Score}, kills {w.Kills}, {deaths} deaths; {sw.ElapsedMilliseconds} ms");
    }
}
