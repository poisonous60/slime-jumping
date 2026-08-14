"""Batch-import solo piano audio into the auto-play song set.

Per input file: transkun transcribes it to MIDI, that MIDI becomes
`Assets/Audio/Music/<Name>_score.json`, and the audio is copied next to it as
`<Name>.mp3` plus archived in `sound/music/` (Assets/Audio is gitignored).
`extract_preview.py` then bakes the `preview` field. Register the pair in
`PianoConfig.autoPlay.songs` afterwards — this script does not touch Unity.

Needs the transkun venv (Python 3.11, `uv pip install transkun "ncls==0.0.39" pretty_midi`):

    <venv>/Scripts/python tools/add_songs.py "C:/Downloads/Selected Tracks"
    <venv>/Scripts/python tools/add_songs.py --selftest

Judge the printed table before registering: key confidence >= 0.6, and a note
count that matches what the ear hears. High n/s with low confidence means the
recording was not solo piano.
"""
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MUSIC_DIR = REPO / "SlimeJumping_v2" / "Assets" / "Audio" / "Music"
SOURCE_DIR = REPO / "sound" / "music"

PC_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]

# Krumhansl-Kessler key profiles, correlated against the pitch-class DURATION
# histogram (not note counts — a held bass note is more tonally load-bearing
# than a passing sixteenth).
KS_MAJOR = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
KS_MINOR = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]

# Library filenames carry a length + catalogue tail: "-30-SM391", "_Full_FM2195".
TAIL = re.compile(r"[-_](?:30|full)[-_]", re.IGNORECASE)
LEAD_INDEX = re.compile(r"^\d+[-_]")


def asset_name(stem):
    base = TAIL.split(LEAD_INDEX.sub("", stem), 1)[0]
    return "".join(w[:1].upper() + w[1:] for w in re.split(r"[^A-Za-z0-9]+", base) if w)


def correlate(hist, profile):
    n = len(hist)
    mh, mp = sum(hist) / n, sum(profile) / n
    num = sum((hist[i] - mh) * (profile[i] - mp) for i in range(n))
    den = (sum((hist[i] - mh) ** 2 for i in range(n))
           * sum((profile[i] - mp) ** 2 for i in range(n))) ** 0.5
    return num / den if den else 0.0


def estimate_key(notes):
    hist = [0.0] * 12
    for n in notes:
        hist[n["midi"] % 12] += n["dur"]
    best = (-2.0, 0, "major")
    for pc in range(12):
        rot = hist[pc:] + hist[:pc]
        for profile, mode in ((KS_MAJOR, "major"), (KS_MINOR, "minor")):
            r = correlate(rot, profile)
            if r > best[0]:
                best = (r, pc, mode)
    r, pc, mode = best
    return {"tonicPc": pc, "tonic": PC_NAMES[pc], "mode": mode, "confidence": round(r, 3)}


def score_from_midi(pm, source_name):
    notes = sorted(
        ({"t": round(n.start, 4), "midi": n.pitch,
          "vel": round(n.velocity / 127.0, 3), "dur": round(n.end - n.start, 3)}
         for inst in pm.instruments for n in inst.notes),
        key=lambda n: (n["t"], n["midi"]))

    pedal, down = [], None
    for cc in sorted((c for inst in pm.instruments for c in inst.control_changes
                      if c.number == 64), key=lambda c: c.time):
        if cc.value >= 64 and down is None:
            down = cc.time
        elif cc.value < 64 and down is not None:
            pedal.append({"t": round(down, 3), "dur": round(cc.time - down, 3)})
            down = None
    if down is not None:
        pedal.append({"t": round(down, 3), "dur": round(pm.get_end_time() - down, 3)})

    try:
        tempo = round(float(pm.estimate_tempo()), 2)
    except Exception:
        tempo = 0.0
    return {"source": source_name,
            "duration": round(pm.get_end_time(), 3),
            "tempo": tempo,
            "key": estimate_key(notes),
            "pedal": pedal,
            "notes": notes}


def transcribe(audio, work):
    mid = work / (audio.stem + ".mid")
    if not mid.exists():
        subprocess.run([sys.executable, "-m", "transkun.transcribe",
                        str(audio), str(mid)], check=True)
    return mid


def add(audio, work, force):
    import pretty_midi

    name = asset_name(audio.stem)
    out_json = MUSIC_DIR / (name + "_score.json")
    if out_json.exists() and not force:
        print("%-30s SKIP (already imported)" % name)
        return None

    score = score_from_midi(pretty_midi.PrettyMIDI(str(transcribe(audio, work))), audio.name)
    out_json.write_text(json.dumps(score, indent=1), encoding="utf-8")
    shutil.copyfile(audio, MUSIC_DIR / (name + audio.suffix.lower()))
    archive = SOURCE_DIR / audio.name
    if not archive.exists():
        shutil.copyfile(audio, archive)

    k, d, n = score["key"], score["duration"], len(score["notes"])
    print("%-30s %5.1fs  %4d notes  %4.1f n/s  %-3s %-5s conf %.2f%s"
          % (name, d, n, n / d if d else 0, k["tonic"], k["mode"], k["confidence"],
             "   <-- LOW" if k["confidence"] < 0.6 else ""))
    return out_json


def selftest():
    import pretty_midi

    pm = pretty_midi.PrettyMIDI()
    inst = pretty_midi.Instrument(program=0)
    for i, pitch in enumerate([60, 64, 67, 72, 67, 64, 60]):   # C major arpeggio
        inst.notes.append(pretty_midi.Note(velocity=64, pitch=pitch,
                                           start=i * 0.5, end=i * 0.5 + 0.45))
    inst.control_changes += [pretty_midi.ControlChange(64, 100, 0.2),
                             pretty_midi.ControlChange(64, 0, 1.7)]
    pm.instruments.append(inst)

    s = score_from_midi(pm, "selftest.mp3")
    assert len(s["notes"]) == 7, s["notes"]
    assert s["key"]["tonic"] == "C" and s["key"]["mode"] == "major", s["key"]
    assert s["pedal"] == [{"t": 0.2, "dur": 1.5}], s["pedal"]
    assert s["notes"][0] == {"t": 0.0, "midi": 60, "vel": 0.504, "dur": 0.45}, s["notes"][0]
    assert asset_name("07-Just-an-Ordinary-Day-30-SM372") == "JustAnOrdinaryDay"
    assert asset_name("41-A--Mile-in-Your-Shoes_Full_FM2194") == "AMileInYourShoes"
    print("selftest OK")


def main(argv):
    if "--selftest" in argv:
        selftest()
        return 0
    force = "--force" in argv
    args = [a for a in argv if not a.startswith("--")]
    if not args:
        print(__doc__)
        return 1

    audio = []
    for a in args:
        p = Path(a)
        audio += sorted(p.glob("*.mp3")) if p.is_dir() else [p]

    work = Path(tempfile.gettempdir()) / "piano_transcribe"
    work.mkdir(parents=True, exist_ok=True)
    written = [j for j in (add(f, work, force) for f in audio) if j is not None]
    if not written:
        return 0

    print()
    subprocess.run([sys.executable, str(Path(__file__).parent / "extract_preview.py")]
                   + [str(j) for j in written], check=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
