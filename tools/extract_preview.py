"""Bake per-song "preview" data into the piano score JSONs, so slime landings on the
piano platform REPRODUCE the song that is about to auto-play — its opening chord loop
AND the measured shape of how those chords are actually played.

Input : SlimeJumping_v2/Assets/Audio/Music/*_score.json (transkun transcriptions:
        unquantized notes {t, midi, vel, dur}, pedal spans, KS-estimated key).
Output: the SAME files, updated in place with a "preview" field (everything else,
        including field order and indent=1, is preserved). Runtime falls back to the
        shared authored schemas whenever "preview" is absent or unusable.

Chord extraction is a from-the-paper implementation of HarmAn (Pardo & Birmingham,
"Algorithms for Chordal Analysis", Computer Music Journal 26:2, 2002):
  - partition points where the sounding pitch set changes (no beat grid needed,
    which is exactly why it suits rubato performance transcriptions),
  - template scoring S = P - (M + N) with note weight = minimal segments spanned,
  - optimal segmentation as a DAG longest path (Relaxation), pruned by span.
Performance-MIDI onset scatter (~40 ms in these transcriptions) is absorbed by
clustering onsets/offsets with a 60 ms epsilon before building partition points
(the paper's "Performance variation" error class).

The TEXTURE block is what makes a landing sound like THIS song rather than a preset:
every field is a continuous value measured from the opening — never a toggle from a
catalog, never a hand-tuned constant:
  spreadSeconds / rollUp   how far apart (and in which direction) the song unfolds a
                           chord's notes — median intra-attack onset spread. A block
                           song measures ~0.005 and correctly plays as near-simultaneous;
                           a 24 ms roller rolls at 24 ms. No quantization, no threshold.
  releaseSeconds           how long the song lets chords ring: p75 accompaniment note
                           duration. Baked into every chord step.
  bassReleaseSeconds/-Vol  the song's low drone, if it holds one (long notes under
                           MIDI 55): the landing's bass voice rings that long instead.
  chordVolumeScale         accompaniment loudness relative to the top voice.
Voicings are likewise taken from the song: only tones the segment actually sounds, at
the octaves it sounds them, capped per step at the song's STRIKE gesture — the largest
onset cluster inside that chord's segment. Sounding overlap (long sustain, pedal) is
deliberately NOT the cap: a calm broken-chord song keeps 4-5 notes ringing while only
ever striking 1-2 together, and a landing is a strike. (Texture classification keys on
onset synchronization — Huron 1989 / ISMIR 2014; sounding-based verticals are for
harmony labels, which is what segment_chords still uses them for.)
"one landing = one sounding gesture" is a user-set contract, so a landing may never
re-strike over time (no inter-attack pattern playback) and never press more keys than
the song itself strikes at once. F2 floor is the only correction.

Usage:
  python tools/extract_preview.py                 # write all 11 songs
  python tools/extract_preview.py --dry-run       # report only, write nothing
  python tools/extract_preview.py <file.json> ... # subset
  python tools/extract_preview.py --selftest
"""

import json
import sys
from pathlib import Path

MUSIC_DIR = Path(__file__).resolve().parent.parent / "SlimeJumping_v2" / "Assets" / "Audio" / "Music"

CLUSTER_EPS = 0.06     # onset/offset epsilon (s) for HARMONY segmentation
WINDOW_SECONDS = 24.0  # only the opening is previewed; also caps cost on the 167 s song
MAX_SPAN_SECONDS = 8.0 # DAG edge pruning: no single chord segment longer than this
MAX_SPAN_MINSEGS = 48
MIN_CHORD_SECONDS = 0.35   # merged chords shorter than this are treated as passing
MIN_STEPS, MAX_STEPS = 4, 8

DRONE_MIN_DUR = 2.0        # a low note this long is a held drone, not a bass step
BASS_SPLIT = 55            # below this = bass register (same split song_character.py used)

# Template classes with tie-breaking priors. The first six are HarmAn's own set with
# the KP-corpus proportions (Table 2). min7/maj7 are added per the paper's "It is
# simple to add new templates" - this material is pop-leaning solo piano where m7/maj7
# sonorities are common, and without them a m7 with a weak fifth mislabels as hdim7.
# Their priors are only tie-breakers (placed between min and the diminished family).
TEMPLATES = [
    ("maj",   (0, 4, 7),      43.6),
    ("dom7",  (0, 4, 7, 10),  21.9),
    ("min",   (0, 3, 7),      19.4),
    ("min7",  (0, 3, 7, 10),  10.0),
    ("maj7",  (0, 4, 7, 11),   8.0),
    ("dim7",  (0, 3, 6, 9),    4.4),
    ("hdim7", (0, 3, 6, 10),   3.7),
    ("dim",   (0, 3, 6),       1.8),
]
TEMPLATE_OFFS = {name: offs for name, offs, _ in TEMPLATES}

MAJOR_SCALE = {0, 2, 4, 5, 7, 9, 11}
NATURAL_MINOR = {0, 2, 3, 5, 7, 8, 10}

# Roman numeral base by semitone distance from tonic, stored case-neutral (uppercase);
# roman() lowercases the core for minor-family qualities. Flats 'b', sharps '#'.
ROMAN_MAJOR = ["I", "bII", "II", "bIII", "III", "IV", "bV", "V", "bVI", "VI", "bVII", "VII"]
ROMAN_MINOR = ["I", "bII", "II", "III", "#III", "IV", "#IV", "V", "VI", "#VI", "VII", "#VII"]
PC_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]


def median(vals):
    vals = sorted(vals)
    return vals[len(vals) // 2] if vals else 0


def percentile75(vals):
    vals = sorted(vals)
    return vals[(len(vals) * 3) // 4] if vals else 0


def cluster_times(times, eps=CLUSTER_EPS):
    """Greedy left-to-right clustering; returns {original_time: cluster_start}."""
    out = {}
    start = None
    for t in sorted(set(times)):
        if start is None or t - start > eps:
            start = t
        out[t] = start
    return out


# ── HarmAn chord extraction ──────────────────────────────────────────────────────────

def build_minimal_segments(notes, window_end):
    """Partition points from clustered onsets/offsets -> minimal segments with
    12-dim presence vectors."""
    onmap = cluster_times([n["t"] for n in notes])
    offmap = cluster_times([min(n["t"] + n["dur"], window_end) for n in notes])
    points = sorted({onmap[n["t"]] for n in notes}
                    | {offmap[min(n["t"] + n["dur"], window_end)] for n in notes}
                    | {window_end})
    merged = [points[0]]
    for p in points[1:]:
        if p - merged[-1] > CLUSTER_EPS:
            merged.append(p)
    points = merged

    seg_hists = []
    for k in range(len(points) - 1):
        a, b = points[k], points[k + 1]
        hist = [0] * 12
        need = min(0.05, 0.5 * (b - a))
        for n in notes:
            s, e = onmap[n["t"]], n["t"] + n["dur"]
            if min(e, b) - max(s, a) >= need:
                hist[n["midi"] % 12] += 1
        seg_hists.append(hist)
    return points, seg_hists


def best_template(hist):
    """Score all templates x 12 roots on a pc histogram. Returns (S, root, name)."""
    total = sum(hist)
    best = None
    for name, offs, prior in TEMPLATES:
        for root in range(12):
            p = sum(hist[(root + o) % 12] for o in offs)
            m = sum(1 for o in offs if hist[(root + o) % 12] == 0)
            s = 2 * p - total - m
            key = (s, hist[root], prior)      # score, then root weight, then prior
            if best is None or key > best[0]:
                best = (key, root, name)
    (s, _, _), root, name = best
    return s, root, name


def segment_chords(notes, window_end):
    """HarmAn-style optimal segmentation of the opening window.
    Returns list of (t_start, t_end, root_pc, quality, P, total_weight, matched)."""
    points, seg_hists = build_minimal_segments(notes, window_end)
    n_pts = len(points)
    if n_pts < 2:
        return []

    pref = [[0] * 12]
    for h in seg_hists:
        pref.append([a + b for a, b in zip(pref[-1], h)])

    NEG = float("-inf")
    score = [NEG] * n_pts
    back = [-1] * n_pts
    label = [None] * n_pts
    score[0] = 0.0
    for j in range(1, n_pts):
        lo = max(0, j - MAX_SPAN_MINSEGS)
        for i in range(lo, j):
            if score[i] == NEG or points[j] - points[i] > MAX_SPAN_SECONDS:
                continue
            hist = [pref[j][pc] - pref[i][pc] for pc in range(12)]
            if sum(hist) == 0:
                s, lab = 0, None                 # silent gap segment
            else:
                s, root, name = best_template(hist)
                lab = (root, name)
            if score[i] + s > score[j]:
                score[j] = score[i] + s
                back[j] = i
                label[j] = lab

    segs = []
    j = n_pts - 1
    while j > 0:
        i = back[j]
        if label[j] is not None:
            hist = [pref[j][pc] - pref[i][pc] for pc in range(12)]
            root, name = label[j]
            offs = TEMPLATE_OFFS[name]
            p = sum(hist[(root + o) % 12] for o in offs)
            matched = sum(1 for o in offs if hist[(root + o) % 12] > 0)
            segs.append((points[i], points[j], root, name, p, sum(hist), matched))
        j = i
    segs.reverse()
    return segs


def merge_chords(segs):
    """Merge consecutive identical (root, quality), drop passing blips, re-merge."""
    def fuse(seq):
        out = []
        for s in seq:
            if out and out[-1][2] == s[2] and out[-1][3] == s[3]:
                p = out[-1]
                out[-1] = (p[0], s[1], s[2], s[3], p[4] + s[4], p[5] + s[5], max(p[6], s[6]))
            else:
                out.append(s)
        return out
    merged = fuse(segs)
    return fuse([s for s in merged if s[1] - s[0] >= MIN_CHORD_SECONDS])


def opening_loop(chords):
    """First MIN..MAX chords; cut where the opening chord returns, if it does."""
    if len(chords) < MIN_STEPS:
        return None
    first = (chords[0][2], chords[0][3])
    for L in range(MIN_STEPS, min(MAX_STEPS, len(chords) - 1) + 1):
        if L < len(chords) and (chords[L][2], chords[L][3]) == first:
            return chords[:L]
    return chords[:MAX_STEPS]


# ── Voicing: the octaves the song actually played ───────────────────────────────────

def voice_from_segment(root_pc, quality, seg_notes, tonic_midi, max_voices):
    """Only tones the segment ACTUALLY sounds, at the octaves it sounds them (weighted
    by dur x vel), capped at this segment's strike gesture (max_voices). Never invents
    an unheard template tone — that is exactly how a landing ends up pressing more keys
    than the song ever strikes. Root is guaranteed (harmonic anchor; placed at a low
    register only if somehow unheard). F2 floor is the only register correction."""
    offs = TEMPLATE_OFFS[quality]
    weight = {}
    for n in seg_notes:
        weight[n["midi"]] = weight.get(n["midi"], 0.0) + n["dur"] * max(0.05, n["vel"])

    picked = {}   # midi -> weight, template tones only, heard only
    for o in offs:
        pc = (root_pc + o) % 12
        cands = [(w, m) for m, w in weight.items() if m % 12 == pc]
        if not cands:
            continue
        cands.sort(key=lambda cm: (-cm[0], cm[1]))       # heaviest, tie -> LOWEST octave
        picked[cands[0][1]] = cands[0][0]

    root_octaves = sorted(m for m in weight if m % 12 == root_pc % 12)
    if len(root_octaves) >= 2 and root_octaves[-1] - root_octaves[0] >= 12:
        picked.setdefault(root_octaves[-1], weight[root_octaves[-1]])   # song doubles its root

    if not any(m % 12 == root_pc % 12 for m in picked):
        picked[48 + ((root_pc - 48) % 12)] = 0.01        # unheard root: anchor it low

    # Gesture cap: keep the bass anchor + the heaviest voices, exactly as many as the
    # song strikes at once when it states this chord, never fewer than 2.
    cap = max(2, min(5, max_voices))
    midis = sorted(picked)
    while len(midis) > cap:
        droppable = midis[1:]                            # never the bass
        drop = min(droppable, key=lambda m: picked[m])
        midis.remove(drop)

    while midis and midis[0] < 41:                        # F2 floor (pitch-shifted samples mud)
        low = midis.pop(0) + 12
        if low not in midis:
            midis.append(low)
        midis.sort()
    while midis and midis[-1] > 76:                       # keep a lane for the melody snap
        hi = midis.pop() - 12
        if hi not in midis:
            midis.append(hi)
        midis.sort()
    return [m - tonic_midi for m in midis], midis


def gesture_size(seg_notes, onmap):
    """How many notes this segment's chord-stating STRIKE presses at once: the largest
    onset cluster inside the segment. Struck, not sounding — sustained notes still
    ringing from earlier attacks never inflate a landing's key count."""
    by_attack = {}
    for n in seg_notes:
        by_attack.setdefault(onmap[n["t"]], []).append(n)
    return max(len(g) for g in by_attack.values()) if by_attack else 2


def is_diatonic(root_pc, quality, tonic_pc, minor):
    scale = NATURAL_MINOR if minor else MAJOR_SCALE
    scale_abs = {(tonic_pc + d) % 12 for d in scale}
    return all((root_pc + o) % 12 in scale_abs for o in TEMPLATE_OFFS[quality])


def roman(root_pc, quality, tonic_pc, minor):
    deg = (root_pc - tonic_pc) % 12
    base = (ROMAN_MINOR if minor else ROMAN_MAJOR)[deg]
    prefix = ""
    core = base
    while core and core[0] in "b#":
        prefix += core[0]
        core = core[1:]
    if quality in ("min", "min7", "dim", "hdim7", "dim7"):
        core = core.lower()
    suffix = {"dom7": "7", "min7": "7", "maj7": "M7", "hdim7": "o/7", "dim7": "o7", "dim": "o"}.get(quality, "")
    return prefix + core + suffix


# ── Texture: the measured shape of the opening ──────────────────────────────────────

def split_attacks(notes):
    """Onset clusters -> list of (cluster_time, [notes]) sorted by time."""
    onmap = cluster_times([n["t"] for n in notes])
    attacks = {}
    for n in notes:
        attacks.setdefault(onmap[n["t"]], []).append(n)
    return sorted(attacks.items())


def accompaniment_of(attack_notes):
    """The attack minus its melody candidate: drop the top voice of a multi-note
    attack; a lone high note IS the melody (a lone low note is bass texture)."""
    grp = sorted(attack_notes, key=lambda n: n["midi"])
    if len(grp) >= 2:
        return grp[:-1]
    return grp if grp[0]["midi"] < 60 else []


def extract_texture(notes, chords, voicing_midis_by_step):
    """Continuous, measured description of how the opening plays its chords."""
    attacks = split_attacks(notes)
    t_lo, t_hi = chords[0][0], chords[-1][1]
    window = [(at, grp) for at, grp in attacks if t_lo <= at < t_hi]

    # 1. Intra-attack spread: how the song unfolds a chord, and in which direction.
    spreads, ups = [], 0
    multi = 0
    for _at, grp in window:
        if len(grp) < 2:
            continue
        multi += 1
        by_onset = sorted(grp, key=lambda n: n["t"])
        spreads.append(by_onset[-1]["t"] - by_onset[0]["t"])
        if by_onset[-1]["midi"] >= by_onset[0]["midi"]:
            ups += 1
    spread = round(median(spreads), 4) if spreads else 0.0
    roll_up = ups * 2 >= multi if multi else True

    # 2. Ring: p75 accompaniment duration + sustain-pedal duty over the window.
    acc_notes = [n for _at, grp in window for n in accompaniment_of(grp)]
    durs = [n["dur"] for n in acc_notes]
    release = round(min(4.0, max(0.5, percentile75(durs))), 2) if durs else 1.2

    # 3. Low drone: long-held bass notes mean the landing's bass voice should ring.
    drones = [n for _at, grp in window for n in grp
              if n["midi"] < BASS_SPLIT and n["dur"] >= DRONE_MIN_DUR]
    if len(drones) >= 2:
        bass_release = round(min(6.0, median([n["dur"] for n in drones])), 2)
        all_vel = median([n["vel"] for _at, g in window for n in g]) or 1.0
        bass_vol = round(min(1.0, max(0.3, median([n["vel"] for n in drones]) / all_vel)), 3)
    else:
        bass_release, bass_vol = 0.0, 0.0

    # 4. Accompaniment loudness relative to the top voice.
    tops = [max(grp, key=lambda n: n["midi"])["vel"] for _at, grp in window if len(grp) >= 2]
    if tops and acc_notes:
        chord_vol = round(min(1.1, max(0.5, median([n["vel"] for n in acc_notes]) / (median(tops) or 1.0))), 3)
    else:
        chord_vol = 1.0

    # NOTE: no inter-attack "pattern" playback. One landing = one sounding gesture is a
    # user-set contract — a landing must never re-strike keys 0.3-0.9 s later, however
    # faithfully that would copy the song's ostinato. The song's motion across attacks
    # is conveyed by successive landings advancing the progression instead.
    return {
        "spreadSeconds": spread,
        "rollUp": roll_up,
        "releaseSeconds": release,
        "bassReleaseSeconds": bass_release,
        "bassVolumeScale": bass_vol,
        "chordVolumeScale": chord_vol,
    }


# ── Pipeline ─────────────────────────────────────────────────────────────────────────

def extract(data):
    """Full pipeline for one loaded score JSON. Returns (preview|None, report dict)."""
    key = data.get("key") or {}
    tonic_pc = int(key.get("tonicPc", 0)) % 12
    minor = key.get("mode") == "minor"
    window_end = min(float(data.get("duration", WINDOW_SECONDS)), WINDOW_SECONDS)
    notes = [n for n in data.get("notes", []) if n["t"] < window_end]
    if len(notes) < 12:
        return None, {"fallback": "too few notes in window (%d)" % len(notes)}

    segs = segment_chords(notes, window_end)
    chords = merge_chords(segs)
    loop = opening_loop(chords)
    if not loop:
        return None, {"fallback": "merged progression too short (%d chords)" % len(chords)}

    # Sparse/ambiguous openings (mono lines, dyads) produce junk labels: require most
    # loop chords to have >= 2 distinct template pcs sounding and decent coverage.
    weak = sum(1 for c in loop if c[6] < 2 or (c[4] / max(1, c[5])) < 0.55)
    if weak > len(loop) // 4:
        return None, {"fallback": "low-confidence harmony (%d/%d chords weak)" % (weak, len(loop))}

    tonic_midi = 60 + tonic_pc
    onmap = cluster_times([n["t"] for n in notes])
    steps, voicings, romans, gestures = [], [], [], []
    for (t0, t1, root, qual, p, w, _m) in loop:
        seg_notes = [n for n in notes if t0 <= onmap[n["t"]] < t1]
        gest = gesture_size(seg_notes, onmap)
        gestures.append(gest)
        offs, midis = voice_from_segment(root, qual, seg_notes, tonic_midi, gest)
        voicings.append(midis)
        rn = roman(root, qual, tonic_pc, minor)
        romans.append(rn)
        steps.append({
            "label": "%s %s%s" % (rn, PC_NAMES[root], {"min": "m", "min7": "m7", "maj7": "M7", "dim": "o", "hdim7": "o/7", "dim7": "o7", "dom7": "7"}.get(qual, "")),
            "offsetsFromTonic": offs,
            "chordTones": not is_diatonic(root, qual, tonic_pc, minor),
        })

    texture = extract_texture(notes, loop, voicings)
    p_sum = sum(s[4] for s in loop)
    w_sum = sum(s[5] for s in loop) or 1
    report = {
        "key": "%s %s" % (PC_NAMES[tonic_pc], "minor" if minor else "major"),
        "romans": romans,
        "labels": [s["label"] for s in steps],
        "voicings": voicings,
        "gestures": gestures,
        "coverage": p_sum / w_sum,
        "texture": texture,
        "warn_first": (loop[0][2] - tonic_pc) % 12 != 0,
    }
    preview = {"tonicMidi": tonic_midi, "minor": minor, "steps": steps, "texture": texture}
    return preview, report


def print_report(name, preview, rep):
    if preview is None:
        print("%-28s FALLBACK: %s" % (name, rep["fallback"]))
        return
    t = rep["texture"]
    parts = ["spread %dms %s" % (round(t["spreadSeconds"] * 1000), "up" if t["rollUp"] else "down"),
             "ring %.2fs" % t["releaseSeconds"]]
    if t["bassReleaseSeconds"] > 0:
        parts.append("drone %.1fs vol%.2f" % (t["bassReleaseSeconds"], t["bassVolumeScale"]))
    parts.append("chordVol %.2f" % t["chordVolumeScale"])
    parts.append("gesture %s -> voices %s" % ("/".join(str(g) for g in rep["gestures"]),
                 "/".join(str(len(v)) for v in rep["voicings"])))
    print("%-28s %-9s cov %.2f  %s" % (name, rep["key"], rep["coverage"], " - ".join(rep["romans"])))
    print("%-28s   chords: %s" % ("", ", ".join(rep["labels"])))
    print("%-28s   texture: %s%s" % ("", " | ".join(parts),
                                     "   [WARN first chord != I/i]" if rep["warn_first"] else ""))


def selftest():
    """Synthetic C-F-G-C block chords (strummed over 15 ms, held ~0.9 s, 4 notes held
    at once) must come out I-IV-V-I major, voiced at the octaves the 'song' used with
    all 4 voices kept (gesture 4), near-zero spread, no drone. A sparse dyad version
    of the same must cap its voicings at 2, and a broken-chord version — same tones
    struck ONE at a time but sustained so 4 always sound together — must also cap at 2:
    sounding overlap must never inflate the strike gesture."""
    def mk(chords, offsets):
        notes = []
        for k, root in enumerate(chords):
            for j, off in enumerate(offsets):
                notes.append({"t": k * 1.0 + 0.005 * j, "midi": root + off, "vel": 0.8, "dur": 0.9})
        return {"duration": len(chords) * 1.0, "key": {"tonicPc": 0, "mode": "major"}, "notes": notes}

    preview, rep = extract(mk([48, 53, 55, 48], (0, 4, 7, 12)))
    assert preview is not None, "selftest: no preview extracted"
    assert rep["romans"] == ["I", "IV", "V", "I"], "selftest: got %s" % rep["romans"]
    tex = preview["texture"]
    assert tex["spreadSeconds"] <= 0.03, "selftest: block strum measured as %s" % tex["spreadSeconds"]
    assert tex["bassReleaseSeconds"] == 0, "selftest: phantom drone"
    assert rep["voicings"][0] == [48, 52, 55, 60], "selftest: voicing %s not the song's" % rep["voicings"][0]
    assert all(min(s["offsetsFromTonic"]) + preview["tonicMidi"] >= 41 for s in preview["steps"]), \
        "selftest: bass below F2"

    _p2, rep2 = extract(mk([48, 53, 55, 48, 53, 55, 48, 53], (0, 7)))   # dyads only
    assert all(len(v) <= 2 for v in rep2["voicings"]), \
        "selftest: dyad song voiced %s — gesture cap broken" % rep2["voicings"]

    def mk_broken(chords, offsets):
        notes = []
        for k, root in enumerate(chords):
            for j, off in enumerate(offsets):
                notes.append({"t": k * 1.0 + 0.22 * j, "midi": root + off, "vel": 0.8,
                              "dur": 1.0 - 0.22 * j})   # all still sounding at bar end
        return {"duration": len(chords) * 1.0, "key": {"tonicPc": 0, "mode": "major"}, "notes": notes}

    _p3, rep3 = extract(mk_broken([48, 53, 55, 48], (0, 4, 7, 12)))
    assert all(len(v) <= 2 for v in rep3["voicings"]), \
        "selftest: broken-chord song voiced %s — sounding overlap inflated the strike gesture" % rep3["voicings"]
    print("selftest OK: I-IV-V-I / voicing [48,52,55,60] / spread %.0fms / dyad cap %s / broken cap %s"
          % (tex["spreadSeconds"] * 1000, [len(v) for v in rep2["voicings"]],
             [len(v) for v in rep3["voicings"]]))


def main(argv):
    if "--selftest" in argv:
        selftest()
        return 0
    dry = "--dry-run" in argv
    args = [a for a in argv if not a.startswith("--")]
    files = [Path(a) for a in args] if args else sorted(MUSIC_DIR.glob("*_score.json"))
    if not files:
        print("no score JSONs found in", MUSIC_DIR)
        return 1
    for f in files:
        data = json.loads(f.read_text(encoding="utf-8"))
        preview, rep = extract(data)
        print_report(f.stem.replace("_score", ""), preview, rep)
        if dry:
            continue
        if preview is None:
            data.pop("preview", None)
        else:
            data["preview"] = preview
        f.write_text(json.dumps(data, indent=1), encoding="utf-8")
    print("\n%s %d file(s)" % ("reported (dry-run)" if dry else "updated", len(files)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
