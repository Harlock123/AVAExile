#!/usr/bin/env python3
"""Synthesises AVAExile's sound effects and tunes into ../src/AVAExile/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions: a slow, echoing figure for the caves and a rising theme for the title. Run it
from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAExile", "Sounds")
RNG = random.Random(1988)


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


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Fire.wav", tone(0.14, 2200, 500, "square", attack=0.001, release=0.9, gain=0.5), 0.14)
    write("Hit.wav", mix(tone(0.08, 900, 300, "square", attack=0.001, release=1.2, gain=0.5), tone(0.06, 2500, 800, "noise", release=1.5, gain=0.4)), 0.24)
    write("Kill.wav", mix(tone(0.3, 1800, 200, "noise", attack=0.001, release=0.9), tone(0.25, 500, 90, "square", release=0.8, gain=0.5)), 0.3)
    write("Hurt.wav", mix(tone(0.15, 500, 160, "square", attack=0.001, release=1.0, gain=0.6), tone(0.12, 2000, 500, "noise", release=1.2, gain=0.5)), 0.28)
    write("Die.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["E5", "C5", "A4", "F4", "D4"]], tone(0.5, hz("C4"), kind="pulse", vibrato=0.04, release=0.6)), 0.34)
    write("Pickup.wav", cat(tone(0.05, hz("C5"), kind="square", release=0.4), tone(0.08, hz("G5"), kind="square", release=0.5)), 0.2)
    write("Drop.wav", cat(tone(0.05, hz("G4"), kind="square", release=0.4), tone(0.08, hz("C4"), kind="square", release=0.5)), 0.2)
    write("Door.wav", cat(tone(0.5, 120, 260, "saw", attack=0.02, release=0.3, gain=0.5), *[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5"]]), 0.3)
    write("Pod.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6"]]), 0.3)
    write("Repaired.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.8, hz("C7"), kind="pulse", vibrato=0.02, release=0.6)), 0.36)
    write("Turret.wav", tone(0.2, 900, 300, "saw", attack=0.002, release=0.8, gain=0.5), 0.16)
    write("Buzz.wav", tone(0.5, 180, 220, "saw", attack=0.05, vibrato=0.15, vib_rate=50, release=0.4, gain=0.4), 0.14)
    write("Splash.wav", tone(0.4, 3000, 700, "noise", attack=0.01, release=0.8, gain=0.6), 0.2)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)
    write("LowEnergy.wav", cat(*[tone(0.15, 900, kind="square", release=0.2, gain=0.5) for _ in range(3)]), 0.22)

    # The caves (an original composition): a slow figure that seems to echo off the rock.
    e = 0.5
    line = [("A4", e), ("-", e), ("E5", e), ("-", e), ("D5", 1), ("C5", e), ("B4", e), ("A4", 1), ("-", 1), ("G4", e), ("A4", e), ("C5", 2), ("-", 2),
            ("F4", e), ("-", e), ("C5", e), ("-", e), ("B4", 1), ("A4", e), ("G4", e), ("E4", 1), ("-", 1), ("G4", e), ("A4", e), ("A4", 2), ("-", 2)]
    echo = [("-", 0.75)] + [(n, b) for n, b in line]
    bass = [("A2", 4), ("A2", 4), ("F2", 4), ("E2", 4)]
    write("Music.wav", mix(tune(96, line, bass, kind="tri"), [s * 0.3 for s in seq(96, echo, kind="tri", gain=0.5)]), 0.24)
    title = [("A4", 1), ("C5", 1), ("E5", 1), ("A5", 2), ("G5", 1), ("E5", 1), ("D5", 1), ("E5", 3),
             ("F5", 1), ("E5", 1), ("C5", 1), ("D5", 2), ("B4", 1), ("C5", 1), ("A4", 4)]
    tbass = [("A2", 4), ("C3", 4), ("D3", 4), ("E3", 4), ("A2", 4)]
    write("TitleTune.wav", cat(tune(118, title, tbass, kind="pulse"), silence(0.8)), 0.34)
    write("Cleared.wav", tune(150, [("E5", 0.5), ("A5", 0.5), ("C6", 0.5), ("E6", 0.5), ("A6", 2)], [("A2", 1), ("E3", 1), ("A2", 2)], kind="pulse"), 0.36)
    write("Victory.wav", tune(150, [("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 1.5), ("G#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 3)],
                              [("A2", 1), ("E2", 1), ("F#2", 1), ("D2", 1), ("E2", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
