#!/usr/bin/env python3
"""Synthesises AVARollingThunder's sound effects and tunes into ../src/AVARollingThunder/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and noise voices. The title
plays In the Hall of the Mountain King (Edvard Grieg, 1875, long in the public domain), quickening as it goes, the
way the orchestra does; the spy-jazz tune in play, with its walking bass, brushes and ride cymbal, is a new
composition. Leila's cry and Maboo's laugh are spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVARollingThunder", "Sounds")
RNG = random.Random(1986)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "thin":
        return 1.0 if p < 0.125 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def add(buf, seconds, part):
    """Adds a part into a buffer, a time in."""
    start = int(seconds * RATE)
    for i, v in enumerate(part):
        if start + i < len(buf):
            buf[start + i] += v


def lowpass(samples, k):
    out, y = [], 0.0
    for s in samples:
        y += k * (s - y)
        out.append(y)
    return out


def highpass(samples, k):
    low = lowpass(samples, k)
    return [s - l for s, l in zip(samples, low)]


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


# ───────────────────────────── instruments ─────────────────────────────

def pluck(seconds, note, gain=0.6):
    """A plucked string (a double bass, a pizzicato): a thump, then dying away."""
    f = hz(note)
    body = mix(tone(seconds, f, kind="sine", attack=0.003, release=2.2, gain=gain), tone(seconds, f * 2, kind="tri", attack=0.002, release=3.5, gain=gain * 0.35))
    return lowpass(body, 0.35)


def reed(seconds, note, gain=0.5):
    """A reedy wind voice for a melody: a muted trumpet or a bassoon, with a little vibrato."""
    f = hz(note)
    held = min(seconds, max(0.06, seconds * 0.9))
    s = mix(tone(held, f, kind="pulse", attack=0.02, release=0.5, vibrato=0.006, vib_rate=5.5, gain=gain),
            tone(held, f, kind="tri", attack=0.01, release=0.4, gain=gain * 0.5))
    return cat(lowpass(s, 0.25), silence(seconds - held))


def vibes(seconds, note, gain=0.4):
    f = hz(note)
    return mix(tone(seconds, f, kind="sine", attack=0.003, release=1.6, vibrato=0.004, vib_rate=6, gain=gain), tone(min(seconds, 0.08), f * 4, kind="sine", attack=0.001, release=1, gain=gain * 0.2))


def kick(gain=1.0):
    return tone(0.16, 120, 45, "sine", attack=0.001, release=1.4, gain=gain)


def brush(gain=0.5):
    return lowpass(tone(0.16, 6000, 4000, "noise", attack=0.01, release=1.5, gain=gain), 0.4)


def ride(gain=0.3):
    return highpass(tone(0.22, 9000, kind="noise", attack=0.001, release=2.5, gain=gain), 0.5)


def play(bpm, notes, instrument, swing=0.0):
    """A line of (note, beats); '-' is a rest. With swing, off-beat eighths come late."""
    beat = 60.0 / bpm
    out, t = [], 0.0
    for note, beats in notes:
        d = beats * beat
        if swing and beats == 0.5:
            d = beat * (0.5 + swing if (t / beat) % 1 < 0.25 else 0.5 - swing)
        out.extend(silence(d) if note == "-" else instrument(d, note))
        t += beats * beat
    return out


# ───────────────────────────── the spy tune ─────────────────────────────

def spy_tune():
    """A cool, creeping spy-jazz tune of the game's own: walking bass, brushes and ride, muted horn and vibes."""
    bpm = 128
    e, q, h = 0.5, 1, 2
    chords = ["Em", "Em", "Am", "Am", "C7", "B7", "Em", "B7"]
    walks = {
        "Em": ["E2", "G2", "B2", "A#2"], "Am": ["A2", "C3", "E3", "D#3"],
        "C7": ["C3", "E3", "G3", "C#3"], "B7": ["B2", "D#3", "F#3", "F3"],
    }
    bass = []
    for _ in range(2):
        for c in chords:
            bass += [(n, q) for n in walks[c]]
    horn = [("E4", q), ("G4", e), ("B4", e), ("A#4", q), ("A4", e), ("G4", e),
            ("F#4", h), ("E4", q), ("-", q),
            ("A4", q), ("C5", e), ("E5", e), ("D#5", q), ("D5", e), ("C5", e),
            ("B4", 3), ("-", q),
            ("C5", e), ("B4", e), ("G4", e), ("E4", e), ("A#4", q), ("G4", q),
            ("F#4", e), ("A4", e), ("C5", e), ("D#5", e), ("F#5", q), ("D#5", q),
            ("E5", e), ("B4", e), ("G4", e), ("E4", e), ("F#4", q), ("G4", q),
            ("F#4", 3), ("-", q)]
    vib = [("-", 4 * 8)] + [("B4", 1.5), ("G4", 0.5), ("E4", 2), ("C5", 1.5), ("A4", 0.5), ("E4", 2),
                            ("E5", 1.5), ("C5", 0.5), ("G4", 2), ("D#5", 1.5), ("B4", 0.5), ("F#4", 2),
                            ("G5", 1.5), ("E5", 0.5), ("B4", 2), ("A#4", 4), ("B4", 4), ("D#5", 4)]
    beat = 60.0 / bpm
    bars = 16
    length = int(bars * 4 * beat * RATE)
    drums = [0.0] * length
    for b in range(bars):
        t0 = b * 4 * beat
        for k, off in enumerate([0, 1, 1 + 2 / 3, 2, 3, 3 + 2 / 3]):
            add(drums, t0 + off * beat, ride(0.22 if k in (0, 3) else 0.15))
        add(drums, t0 + beat, brush(0.35))
        add(drums, t0 + 3 * beat, brush(0.35))
        if b % 2 == 0:
            add(drums, t0, kick(0.5))
    return mix(play(bpm, bass, lambda d, n: pluck(d, n, 0.9)), play(bpm, horn + horn, lambda d, n: reed(d, n, 0.32), swing=1 / 6),
               play(bpm, vib, lambda d, n: vibes(d, n, 0.16)), drums)[:length]


# ───────────────────────────── the title: In the Hall of the Mountain King ─────────────────────────────

def mountain_king():
    """Grieg's theme, low and stealthy, then up a fifth, then again higher, quickening all the way."""
    e, q, h = 0.5, 1, 2
    a = [("B3", e), ("C#4", e), ("D4", e), ("E4", e), ("F#4", e), ("D4", e), ("F#4", q),
         ("F4", e), ("C#4", e), ("F4", q), ("E4", e), ("C4", e), ("E4", q),
         ("B3", e), ("C#4", e), ("D4", e), ("E4", e), ("F#4", e), ("D4", e), ("F#4", e), ("B4", e),
         ("A4", e), ("F#4", e), ("D4", e), ("F#4", e), ("A4", h)]
    b = [("F#4", e), ("G#4", e), ("A#4", e), ("B4", e), ("C#5", e), ("A#4", e), ("C#5", q),
         ("D5", e), ("A#4", e), ("D5", q), ("C#5", e), ("A#4", e), ("C#5", q),
         ("F#4", e), ("G#4", e), ("A#4", e), ("B4", e), ("C#5", e), ("A#4", e), ("C#5", e), ("F#5", e),
         ("E5", e), ("C#5", e), ("A#4", e), ("C#5", e), ("E5", h)]

    def up(line):
        return [(n[:-1] + str(int(n[-1]) + 1), d) for n, d in line]

    sections = [(a, "B2", "F#2"), (a, "B2", "F#2"), (b, "F#2", "C#3"), (b, "F#2", "C#3"), (up(a), "B2", "F#2"), (up(a), "B2", "F#2")]
    out = []
    bpm = 112
    for k, (line, root, fifth) in enumerate(sections):
        beat = 60.0 / bpm
        melody = []
        for note, beats in line:
            d = beats * beat
            melody.extend(reed(d, note, 0.5 + 0.06 * k) if k < 4 else mix(reed(d, note, 0.55), pluck(d, note[:-1] + str(int(note[-1]) - 1), 0.35)))
        beats = sum(d for _, d in line)
        oom = []
        for i in range(int(beats)):
            oom.extend(pluck(beat, root if i % 2 == 0 else fifth, 0.55 + 0.05 * k))
        drum = []
        if k >= 2:
            for i in range(int(beats)):
                drum.extend(cat(kick(0.35 + 0.08 * k), silence(beat - 0.16)))
        out.extend(mix(melody, oom, drum) if drum else mix(melody, oom))
        bpm += 18
    # The final crash: B, hammered.
    end = mix(pluck(0.9, "B2", 1.0), pluck(0.9, "B3", 0.8), reed(0.9, "B4", 0.5), kick(1.0), lowpass(tone(0.9, 3000, 800, "noise", attack=0.002, release=2, gain=0.4), 0.3))
    return cat(out, end, silence(1.5))


# ───────────────────────────── effects ─────────────────────────────

def bang(seconds, gain, crack, thump):
    return mix(tone(seconds * 0.5, crack, crack / 3, "noise", attack=0.0005, release=2.5, gain=gain), tone(seconds, thump, thump / 3, "sine", attack=0.001, release=2, gain=gain * 0.8))


def boom(seconds, gain=1.0):
    rumble = lowpass(tone(seconds, 1200, 200, "noise", attack=0.002, release=1.5, gain=gain), 0.15)
    return mix(rumble, tone(seconds * 0.7, 80, 30, "sine", attack=0.002, release=1.5, gain=gain * 0.9), tone(0.05, 4000, 1000, "noise", attack=0.0005, release=1, gain=gain * 0.6))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The guns.
    write("Pistol.wav", bang(0.22, 1.0, 7000, 160), 0.5)
    write("MachineGun.wav", bang(0.09, 1.0, 9000, 200), 0.36)
    write("Empty.wav", mix(tone(0.03, 3000, 2000, "noise", attack=0.0005, release=1, gain=0.6), lowpass(tone(0.18, 900, 300, "noise", attack=0.01, release=1.5, gain=0.5), 0.2)), 0.3)
    write("EnemyShot.wav", mix(tone(0.16, 1400, 260, "square", attack=0.001, release=1.4, gain=0.5), tone(0.08, 5000, 1500, "noise", attack=0.0005, release=2, gain=0.5)), 0.3)
    write("Throw.wav", lowpass(tone(0.25, 800, 3000, "noise", attack=0.05, release=0.6), 0.3), 0.22)
    write("Grenade.wav", boom(0.7), 0.6)
    # Doors, jumps, being hit.
    write("Door.wav", cat(tone(0.18, 210, 170, "saw", attack=0.02, release=0.4, vibrato=0.05, vib_rate=31, gain=0.5), lowpass(tone(0.1, 140, 60, "square", attack=0.001, release=1.5), 0.3)), 0.32)
    write("Jump.wav", tone(0.14, 300, 720, "pulse", attack=0.003, release=0.7), 0.2)
    write("EnemyHit.wav", cat(bang(0.08, 0.8, 3000, 220), tone(0.3, 620, 110, "square", attack=0.002, release=1.0, gain=0.5)), 0.38)
    write("Clang.wav", mix(tone(0.35, 1480, kind="sine", attack=0.001, release=2.5), tone(0.3, 2370, kind="sine", attack=0.001, release=3, gain=0.6), tone(0.02, 6000, kind="noise", attack=0.0005, release=1, gain=0.5)), 0.3)
    write("Hurt.wav", cat(tone(0.08, 320, 220, "square", release=0.6), tone(0.16, 240, 120, "square", release=1.2, vibrato=0.08, vib_rate=40)), 0.34)
    write("Death.wav", mix(tone(1.1, 880, 70, "square", attack=0.003, release=0.8, vibrato=0.05, vib_rate=14, gain=0.6), lowpass(tone(0.5, 2000, 200, "noise", attack=0.002, release=1.5, gain=0.5), 0.3)), 0.45)
    write("Ammo.wav", cat(*[tone(0.06, hz(n), kind="pulse", attack=0.002, release=0.6) for n in ["C6", "E6", "G6"]], tone(0.2, hz("C7"), kind="pulse", release=1.0)), 0.25)
    write("TimeWarning.wav", cat(*[cat(tone(0.14, 880, kind="square", release=0.2), tone(0.14, 660, kind="square", release=0.2)) for _ in range(3)]), 0.24)
    write("OneUp.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.5) for n in ["E5", "G5", "B5", "E6", "G6"]], tone(0.25, hz("B6"), kind="pulse", release=1)), 0.24)
    # The creatures.
    flutter = [s * (0.5 + 0.5 * math.sin(2 * math.pi * 22 * i / RATE)) for i, s in enumerate(lowpass(tone(0.9, 3000, kind="noise", attack=0.05, release=0.5), 0.3))]
    write("Bats.wav", mix(flutter, tone(0.12, 4200, 3800, "sine", attack=0.005, release=1, gain=0.2)), 0.3)
    growl = [s * (0.6 + 0.4 * math.sin(2 * math.pi * 17 * i / RATE)) for i, s in enumerate(mix(tone(0.8, 95, 70, "saw", attack=0.05, release=0.8), lowpass(tone(0.8, 600, 300, "noise", attack=0.05, release=0.8, gain=0.5), 0.2)))]
    write("Growl.wav", lowpass(growl, 0.25), 0.45)
    write("Lava.wav", cat(*[lowpass(tone(0.11, 90 + RNG.uniform(0, 80), 200 + RNG.uniform(0, 120), "sine", attack=0.01, release=1.2), 0.5) for _ in range(6)]), 0.45)
    write("Fireball.wav", lowpass(tone(0.4, 400, 1800, "noise", attack=0.02, release=1.2), 0.25), 0.2)
    write("BossDown.wav", cat(boom(0.6, 1.0), boom(1.2, 0.9)), 0.6)
    write("Rescue.wav", cat(*[vibes(0.09, n, 0.6) for n in ["E5", "G#5", "B5", "E6"]], vibes(0.6, "G#6", 0.6)), 0.4)
    # Voices: Leila's cry for help; Maboo's laugh.
    write("SayHelp.wav", say("HH EH1 L P !", rate=RATE, pitch=235, speed=0.8, size=0.82, seed=5), 0.5)
    write("Laugh.wav", say("HH AA1 , HH AA1 , HH AA1 , HH AA1 !", rate=RATE, pitch=78, speed=0.8, size=1.35, seed=9), 0.55)

    # The tunes: the spy tune in play, Grieg on the title, a sting for each clear, a fanfare for the rescue.
    write("Music.wav", spy_tune(), 0.32)
    write("TitleTune.wav", mountain_king(), 0.42)
    write("StageClear.wav", mix(play(150, [("E4", 0.5), ("G4", 0.5), ("B4", 0.5), ("E5", 0.5), ("D#5", 1), ("E5", 2)], lambda d, n: reed(d, n, 0.5)),
                                play(150, [("E3", 1), ("B2", 1), ("E3", 3)], lambda d, n: pluck(d, n, 0.8))), 0.36)
    write("Victory.wav", mix(play(120, [("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("B5", 1.5), ("A5", 0.5), ("G5", 0.5), ("F#5", 0.5), ("G#5", 4)], lambda d, n: reed(d, n, 0.5)),
                             play(120, [("E3", 2), ("C3", 2), ("E3", 4)], lambda d, n: pluck(d, n, 0.8)),
                             play(120, [("-", 4), ("B4", 4), ("E5", 4)], lambda d, n: vibes(d, n, 0.3))), 0.38)
    write("GameOver.wav", mix(play(70, [("B4", 1), ("A#4", 1), ("A4", 1), ("G#4", 1), ("E4", 3)], lambda d, n: reed(d, n, 0.5)),
                              play(70, [("E3", 2), ("C3", 2), ("E2", 3)], lambda d, n: pluck(d, n, 0.9))), 0.4)


if __name__ == "__main__":
    main()
