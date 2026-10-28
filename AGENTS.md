# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A spy shoot-'em-up in the style of Namco's 1986 arcade game Rolling Thunder, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. Five areas of doors and walkways, two storeys high, in two stories. One self-contained
executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is original: the graphics are
drawn in code, and the sounds, tunes and voices are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVARollingThunder.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVARollingThunder.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVARollingThunder          # play it; --no-audio for silence
THUNDER_SHOTS=$PWD/Images dotnet test tests/AVARollingThunder.Tests --filter ScreenshotTests   # refresh the README's pictures
THUNDER_PROBE=1 dotnet test tests/AVARollingThunder.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                        # rebuild src/AVARollingThunder/Sounds (uses tools/speech.py)
```

`THUNDER_PROBE=1` plays the first story and `2` both; `blind` walks on firing and jumping only the pits.
`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVARollingThunder/Engine/`: the game, with no Avalonia drawing in it.
  - `World.cs` has the `Field` (measures, the two storeys and the 1/60 s `Step`), the `Dice` random source,
    `Controls`, the agent, doors, maskers, shots, beasts and boss, and the `World` that holds them all and can be
    copied whole (`World.Copy()`).
  - `Stage.cs` lays each area out from three rows of text: the walkway, the floor with its doors and pits, and
    the cues (bats, panthers, Maboo).
  - `Sim.cs` is the rules, one 60th of a second at a time; `Sim.Foes.cs` runs the Geldra, the beasts, the pits
    and Maboo.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `Pilot.cs` is the computer agent, used by the attract mode and the tests. It tries its moves out on copies
    of the world.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVARollingThunder/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs
  the 60 Hz timer. `GameCanvas.Draw.cs`, `.Scene.cs` (backgrounds, buildings, floors) and `.Figures.cs` (the
  people and creatures) hold the drawing.
- `src/AVARollingThunder/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVARollingThunder/Sounds/`: the generated WAV files.
- `tests/AVARollingThunder.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`
  and the opt-in `PilotProbe`.
- `tools/`: `make_sounds.py` and `speech.py`, a small formant speech synthesiser for the voices.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the simulation in fixed
  `Field.Step` (1/60 s) steps, so tests can play areas deterministically. Draw random numbers from the world's
  `Dice` (`w.Dice`, reseeded for each area and story), never from `Random.Shared`.
- **Copyable world.** Everything that changes during play lives in `World`, and `World.Copy()` must copy all of
  it, `Dice` included: the pilot looks ahead on copies, and the Geldra must roll the same dice there. A new field
  needs adding to the copy.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Y counts down the screen, so up on keys, D-pad and stick must give
  `Dy = -1` (into a door, or leaping to the walkway with jump). Test new controls through `InputHandler`, not by
  building `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVARollingThunder`); tests
  load the table from a temporary folder and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
