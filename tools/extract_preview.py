"""Bake per-song "preview" data into the piano score JSONs, so a run of slime landings
REPRODUCES the opening of the song that is about to auto-play.

Input : SlimeJumping_v2/Assets/Audio/Music/*_score.json (transkun transcriptions:
        unquantized notes {t, midi, vel, dur}, pedal spans, KS-estimated key).
Output: the SAME files, updated in place with a "preview" field. Runtime falls back to
        the shared authored schemas whenever "preview" is absent or unusable.

WHY THIS SHAPE (2026-07-27 rewrite). The previous version reduced the opening to 4-8
chord LABELS and one global "texture" of five scalars, then rebuilt each chord from a
template, re-octaved it by a generic voice-leading rule and topped it with a pentatonic
snap of the landed key. Everything that differs between two songs sharing a progression
-- which notes, in which order, how hard, how long, which one sings on top -- was
averaged away, so every song came out as the same block harmony. Two songs with the
same chords do not sound alike, and that difference lives entirely above the chord
symbol.

So a step is no longer a chord. **One step = one attack the song actually played**:
the notes of a single onset cluster, each with its measured offset inside the cluster,
its measured loudness, and how long it actually sounded (pedal included). Successive
landings walk the opening attack by attack, at the player's own pace -- an arpeggiated
song arpeggiates, a block song blocks, a sparse song plays single notes, because that
is what its attacks are. No template, no catalogue, no threshold: the strike IS the
measurement.

  step.support     the attack's notes minus its melody voice, verbatim
                   (midi, dt = seconds after the attack's first note,
                    vel = the transcribed velocity as measured, NOT normalized per song,
                    ring = how long it sounds, pedal sustain included)
  step.melodyPcs   the pitch classes the song's melody sings over this chord -- the
                   snap target for the key the slime landed on, so the top voice is the
                   song's own material while still answering the player's input
  step.melodyVel   how the song's melody sits against its accompaniment, measured

Loudness is NOT normalized at all -- the transcribed velocity travels through as measured.
Normalizing per attack made every landing equally loud; normalizing per song made every
piece equally loud, because each one then reaches 1.0 somewhere (a soft piece averaging
0.54 was scaled to 0.77 while a heavy one averaging 0.72 only reached 0.86). Absolute level
is masterVolume's job, and it is one knob for all songs.

Chord labels (used for the melody snap set and for the report) still come from HarmAn
(Pardo & Birmingham, "Algorithms for Chordal Analysis", Computer Music Journal 26:2,
2002): partition points where the sounding pitch set changes (no beat grid needed,
which is why it suits rubato transcriptions), template scoring S = P - (M + N), optimal
segmentation as a DAG longest path.

Usage:
  python tools/extract_preview.py                 # write all songs
  python tools/extract_preview.py --dry-run       # report only, write nothing
  python tools/extract_preview.py <file.json> ... # subset
  python tools/extract_preview.py --selftest
"""

import json
import sys
from pathlib import Path

MUSIC_DIR = Path(__file__).resolve().parent.parent / "SlimeJumping_v2" / "Assets" / "Audio" / "Music"

CLUSTER_EPS = 0.06     # onset/offset epsilon (s): one strike, however rolled, is one attack
WINDOW_SECONDS = 24.0  # only the opening is previewed; also caps cost on the 167 s song
MAX_SPAN_SECONDS = 8.0 # DAG edge pruning: no single chord segment longer than this
MAX_SPAN_MINSEGS = 48
MIN_CHORD_SECONDS = 0.35   # merged chords shorter than this are treated as passing
MIN_CHORDS = 3             # fewer than this in the opening = no usable harmony reading

MAX_STEPS = 16         # landings needed to walk the opening once, then it loops
GESTURE_WINDOW = 0.35  # one landing may carry the song's attacks inside this window, at their real
                       # spacing. A fast piece (attacks ~0.2 s apart) is fast BECAUSE of that spacing,
                       # and a landing every second cannot convey it one attack at a time. Slower
                       # pieces have nothing inside the window, so they stay one strike per landing.
                       # The window is a gesture budget, not a pattern: it always ends well before the
                       # next landing, so two landings never overlap.
MAX_RING_SECONDS = 6.0 # how long a landing may ring
MIN_MIDI = 41          # F2 floor: below this the pitch-shifted key samples turn to mud
MELODY_LONE_MIDI = 60  # a lone note this high is the melody; a lone low note is bass

# Template classes with tie-breaking priors. The first six are HarmAn's own set with
# the KP-corpus proportions (Table 2). min7/maj7 are added per the paper's "It is
# simple to add new templates" - this material is pop-leaning solo piano where m7/maj7
# sonorities are common, and without them a m7 with a weak fifth mislabels as hdim7.
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

ROMAN_MAJOR = ["I", "bII", "II", "bIII", "III", "IV", "bV", "V", "bVI", "VI", "bVII", "VII"]
ROMAN_MINOR = ["I", "bII", "II", "III", "#III", "IV", "#IV", "V", "VI", "#VI", "VII", "#VII"]
PC_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
QUAL_SUFFIX = {"min": "m", "min7": "m7", "maj7": "M7", "dim": "o", "hdim7": "o/7",
               "dim7": "o7", "dom7": "7"}


def median(vals):
    vals = sorted(vals)
    return vals[len(vals) // 2] if vals else 0


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


# ── Attacks: what the song actually strikes, and how ────────────────────────────────

def split_attacks(notes):
    """Onset clusters -> [(cluster_time, [notes])] in time order. One cluster = one
    strike = one landing: a 24 ms roll stays a single gesture, a 300 ms arpeggio does
    not (it is several attacks, i.e. several landings)."""
    onmap = cluster_times([n["t"] for n in notes])
    attacks = {}
    for n in notes:
        attacks.setdefault(onmap[n["t"]], []).append(n)
    return sorted(attacks.items())


def hold_fall(note, pedal_spans):
    """Split the note into (hold, fall): how long the key is DOWN, then how long the string
    is left to die away.

    These are not the same measurement and collapsing them is what made a staccato song
    sound sustained. Pedal down at key release does not mean the note stays loud until the
    pedal lifts -- it means the damper never arrives, so the string decays on its own. A
    0.2 s bounce under a 3.4 s pedal is a short strike with a long tail, not a 3.4 s note.
    fall = 0 means the damper falls immediately (the runtime supplies its own damper time)."""
    hold = min(MAX_RING_SECONDS, max(0.05, note["dur"]))
    end = note["t"] + note["dur"]
    fall = 0.0
    for sp in pedal_spans:
        s, e = sp["t"], sp["t"] + sp["dur"]
        if s <= end < e:
            fall = min(MAX_RING_SECONDS, e - end)
            break
    return round(hold, 2), round(fall, 2)


def support_note(note, t0, pedal_spans, sings=False):
    """One struck note of a figure, timed against its first note. `sings` marks the voice
    the song is singing with — kept in the figure (it is part of the music), and only
    dropped when the runtime is in lead mode, where the landed key takes its place.

    vel is the transcribed velocity AS MEASURED — deliberately not normalized against the
    piece's own loudest note. Dividing by a per-song peak forces every song to reach 1.0
    somewhere, which erases how loudly the piece is played: a soft piece then comes out as
    loud as a heavy one (ASpringGreen 0.54 mean scaled to 0.77 while ADarkBlue 0.72 only
    reached 0.86). Absolute level belongs to HarmonizeSettings.masterVolume."""
    hold, fall = hold_fall(note, pedal_spans)
    return {
        "midi": lift_above_floor(note["midi"]),
        "dt": round(note["t"] - t0, 4),
        "vel": round(note["vel"], 3),
        "hold": hold,
        "fall": fall,
        "sings": sings,
    }


def melody_of(attack_notes):
    """The attack's singing voice: the top note of a multi-note strike; a lone high note
    is itself the melody, a lone low note is bass texture and the attack has none."""
    grp = sorted(attack_notes, key=lambda n: n["midi"])
    if len(grp) >= 2:
        return grp[-1]
    return grp[0] if grp[0]["midi"] >= MELODY_LONE_MIDI else None


def chord_at(chords, t):
    """The merged chord segment containing time t (nearest preceding one)."""
    found = None
    for c in chords:
        if c[0] <= t < c[1]:
            return c
        if c[1] <= t:
            found = c
    return found if found is not None else (chords[0] if chords else None)


def lift_above_floor(midi):
    """F2 floor is the one register correction: the key samples are pitch-shifted and
    turn to mud below it. Octave transposition, so the voicing keeps its pitch class."""
    while midi < MIN_MIDI:
        midi += 12
    return midi


def group_gestures(attacks):
    """One landing's worth of music: the first attack, plus any that follow inside
    GESTURE_WINDOW. Returns [(t0, [attacks])].

    A piece is fast because its attacks are close together; a landing every second cannot
    show that one attack at a time. Grouping keeps the song's real spacing (nothing is
    quantised or re-timed) and stops at the window, so the figure is always over well
    before the next landing. Pieces whose attacks are further apart than the window come
    out unchanged: one strike per landing."""
    out = []
    i = 0
    while i < len(attacks) and len(out) < MAX_STEPS:
        t0 = attacks[i][0]
        j = i + 1
        while j < len(attacks) and attacks[j][0] - t0 <= GESTURE_WINDOW:
            j += 1
        out.append((t0, attacks[i:j]))
        i = j
    return out


def build_steps(notes, chords, pedal_spans, tonic_pc, minor, tonic_midi):
    """One step per landing: the song's own strike (or quick figure), measured."""
    attacks = split_attacks(notes)
    t_lo = chords[0][0]
    attacks = [(at, grp) for at, grp in attacks if at >= t_lo]
    gestures = group_gestures(attacks)
    if not gestures:
        return [], {}
    attacks = [a for _t0, grp in gestures for a in grp]     # only what the steps actually use

    ref_vel = median([n["vel"] for _at, grp in attacks for n in grp]) or 0.6

    # The melody material of each chord segment: every singing note the song places over
    # that chord. The landed key snaps into this set, so the top voice answers the player
    # while staying the song's own notes.
    seg_melody = {}
    for _at, grp in attacks:
        mel = melody_of(grp)
        if mel is None:
            continue
        c = chord_at(chords, _at)
        if c is None:
            continue
        seg_melody.setdefault((c[0], c[1]), []).append(mel)

    steps = []
    for t0, group in gestures:
        at, grp = group[0]
        c = chord_at(chords, at)
        root, qual = c[2], c[3]
        mel_notes = seg_melody.get((c[0], c[1]), [])
        if not mel_notes:                                  # bass-only stretch: fall back
            mel_notes = [n for _a, g in attacks for n in g if n["midi"] >= MELODY_LONE_MIDI]

        # The whole figure is kept, singing voice included: the song's own line is what makes
        # a landing sound like the piece, and cutting it out to make room for the landed key
        # left an arbitrary note sitting on top of a song that had lost its melody.
        mel = melody_of(grp)
        figure = [n for _a, g in group for n in g]

        # The landed key inherits THIS strike's dynamics when the song sang here, and
        # the segment's median only when it did not — otherwise every landing inside a
        # chord comes out equally loud, which is the flattening this rewrite exists to
        # undo.
        mel_vel = (mel["vel"] if mel is not None
                   else median([n["vel"] for n in mel_notes]) if mel_notes else ref_vel)
        if mel is not None:
            mel_hold, mel_fall = hold_fall(mel, pedal_spans)
        elif mel_notes:
            pairs = [hold_fall(n, pedal_spans) for n in mel_notes]
            mel_hold = median([hf[0] for hf in pairs])
            mel_fall = median([hf[1] for hf in pairs])
        else:
            mel_hold, mel_fall = 1.0, 0.0

        step = {
            "label": "%s %s%s" % (roman(root, qual, tonic_pc, minor), PC_NAMES[root],
                                  QUAL_SUFFIX.get(qual, "")),
            "chordTones": not is_diatonic(root, qual, tonic_pc, minor),
            "melodyPcs": sorted({n["midi"] % 12 for n in mel_notes}),
            "melodyVel": round(mel_vel, 3),
            "melodyHold": round(mel_hold, 2),
            "melodyFall": round(mel_fall, 2),
            "melodyStruck": mel is not None,
            # Where the landed key goes when it is blended into the texture instead of
            # replacing the melody: the pitch classes the song is using right here — the
            # figure's own notes plus the line it sings over this chord.
            "snapPcs": sorted({n["midi"] % 12 for n in figure}
                              | {n["midi"] % 12 for n in mel_notes}),
            "support": [support_note(n, t0, pedal_spans, sings=(n is mel))
                        for n in sorted(figure, key=lambda n: n["t"])],
        }
        # Kept for the fallback melody-gap math and for anything still reading a chord
        # shape; the gesture above is what actually sounds.
        voiced = sorted({s["midi"] for s in step["support"]})
        step["offsetsFromTonic"] = [m - tonic_midi for m in voiced] if voiced else [0]
        steps.append(step)

    rep = {
        "attacks": len(steps),
        "sizes": [len(s["support"]) for s in steps],   # the figure; the landed key adds one more
        "figure_ms": round(1000 * median([max(n["dt"] for n in s["support"]) for s in steps
                                          if s["support"]]), 0),
        "spread_ms": round(1000 * median([s["support"][-1]["dt"] for s in steps
                                          if len(s["support"]) >= 2]), 1),
        "hold": round(median([s["melodyHold"] for s in steps]), 2),
        "fall": round(median([s["melodyFall"] for s in steps]), 2),
        "vel_lo": round(min(s["melodyVel"] for s in steps), 2),
        "vel_hi": round(max(s["melodyVel"] for s in steps), 2),
        "seconds": round(max(a for a, _ in attacks) - t_lo, 1),
    }
    return steps, rep


# ── Pipeline ─────────────────────────────────────────────────────────────────────────

def extract(data):
    """Full pipeline for one loaded score JSON. Returns (preview|None, report dict)."""
    key = data.get("key") or {}
    tonic_pc = int(key.get("tonicPc", 0)) % 12
    minor = key.get("mode") == "minor"
    window_end = min(float(data.get("duration", WINDOW_SECONDS)), WINDOW_SECONDS)
    notes = [n for n in data.get("notes", []) if n["t"] < window_end]
    pedal_spans = [p for p in (data.get("pedal") or []) if p["t"] < window_end]
    if len(notes) < 12:
        return None, {"fallback": "too few notes in window (%d)" % len(notes)}

    chords = merge_chords(segment_chords(notes, window_end))
    if len(chords) < MIN_CHORDS:
        return None, {"fallback": "merged progression too short (%d chords)" % len(chords)}

    # Sparse/ambiguous openings (mono lines, dyads) produce junk labels.
    weak = sum(1 for c in chords if c[6] < 2 or (c[4] / max(1, c[5])) < 0.55)
    if weak > len(chords) // 4:
        return None, {"fallback": "low-confidence harmony (%d/%d chords weak)" % (weak, len(chords))}

    tonic_midi = 60 + tonic_pc
    steps, rep = build_steps(notes, chords, pedal_spans, tonic_pc, minor, tonic_midi)
    if len(steps) < 2:
        return None, {"fallback": "too few attacks in the opening (%d)" % len(steps)}

    p_sum = sum(s[4] for s in chords)
    w_sum = sum(s[5] for s in chords) or 1
    rep.update({
        "key": "%s %s" % (PC_NAMES[tonic_pc], "minor" if minor else "major"),
        "romans": [roman(c[2], c[3], tonic_pc, minor) for c in chords][:8],
        "coverage": p_sum / w_sum,
        "labels": [s["label"] for s in steps],
    })
    preview = {"tonicMidi": tonic_midi, "minor": minor, "steps": steps}
    return preview, rep


def print_report(name, preview, rep):
    if preview is None:
        print("%-28s FALLBACK: %s" % (name, rep["fallback"]))
        return
    print("%-28s %-9s cov %.2f  %s" % (name, rep["key"], rep["coverage"], " - ".join(rep["romans"])))
    print("%-28s   %d landings over %.1fs, notes %s"
          % ("", rep["attacks"], rep["seconds"], "".join(str(min(9, s)) for s in rep["sizes"])))
    print("%-28s   figure %.0fms | hold %.2fs + fall %.2fs | melody vel %.2f..%.2f"
          % ("", rep["figure_ms"], rep["hold"], rep["fall"], rep["vel_lo"], rep["vel_hi"]))


def selftest():
    """Three synthetic songs, same chords, different playing. They must come out as
    three DIFFERENT gesture streams -- that is the whole point of the rewrite.
    1) block: 4 notes struck together, so each landing strikes 4;
    2) broken: the same tones one at a time, so each landing strikes 1 and it takes
       four landings to state the chord;
    3) soft/loud alternation: window-relative velocity must keep the quiet strikes
       quiet instead of normalizing every attack to full."""
    def score(notes, pedal=None):
        return {"duration": 8.0, "key": {"tonicPc": 0, "mode": "major"},
                "notes": notes, "pedal": pedal or []}

    block, broken, dyn = [], [], []
    for k, root in enumerate([48, 53, 55, 48, 53, 55, 48, 53]):
        for j, off in enumerate((0, 4, 7, 12)):
            block.append({"t": k * 1.0 + 0.005 * j, "midi": root + off, "vel": 0.8, "dur": 0.9})
            broken.append({"t": k * 1.0 + 0.22 * j, "midi": root + off, "vel": 0.8, "dur": 1.0})
            dyn.append({"t": k * 1.0 + 0.005 * j, "midi": root + off,
                        "vel": 0.9 if k % 2 == 0 else 0.3, "dur": 0.9})

    pb, rb = extract(score(block))
    assert pb is not None, "selftest: block song produced no preview"
    assert all(len(s["support"]) == 4 for s in pb["steps"]), \
        "selftest: block strike lost notes -> %s" % rb["sizes"]
    assert all(sum(1 for n in s["support"] if n["sings"]) == 1 for s in pb["steps"]), \
        "selftest: the singing voice must be marked exactly once per figure"

    pk, rk = extract(score(broken))
    assert pk is not None, "selftest: broken song produced no preview"
    # Its tones are struck one at a time 0.22 s apart, so a landing may carry two of them —
    # but SEPARATED, never stacked. Sustain overlap must not turn them into a block chord.
    for s in pk["steps"]:
        at_onset = [n for n in s["support"] if n["dt"] <= CLUSTER_EPS]
        assert len(at_onset) <= 1, \
            "selftest: broken song stacked %s notes at one instant - sustain overlap inflated the strike" \
            % len(at_onset)

    pd, _ = extract(score(dyn))
    vels = [s["melodyVel"] for s in pd["steps"]]
    assert max(vels) - min(vels) > 0.4, \
        "selftest: dynamics flattened (%s) - velocity must be window-relative" % vels

    # A fast piece must arrive as a figure at its own spacing; a slow one must not be
    # glued together. Block above is 1 s apart, so it stays one strike per landing.
    assert all(all(n["dt"] <= CLUSTER_EPS for n in s["support"]) for s in pb["steps"]), \
        "selftest: 1 s apart attacks were grouped into one landing"
    fast = []
    for k in range(24):
        fast.append({"t": k * 0.12, "midi": 60 + (k % 4) * 3, "vel": 0.8, "dur": 0.1})
    pf, rf = extract(score(fast + [{"t": k * 0.96, "midi": 41, "vel": 0.8, "dur": 0.9}
                                   for k in range(3)]))
    if pf is not None:
        spans = [max(n["dt"] for n in s["support"]) for s in pf["steps"] if s["support"]]
        assert spans and max(spans) > 0.2, \
            "selftest: fast piece did not become a figure (%s) - its speed is lost" % spans
        assert max(spans) <= GESTURE_WINDOW + 1e-6, \
            "selftest: figure ran past the gesture budget (%.3f)" % max(spans)

    # A short note under a long pedal is a SHORT strike with a LONG tail, never a long
    # note: hold must stay the key-down time and only fall may grow. Getting this wrong is
    # what made a staccato song ring for three seconds per landing.
    staccato = [{"t": k * 1.0, "midi": 72, "vel": 0.8, "dur": 0.2} for k in range(8)]
    pp, _ = extract(score(block + staccato, pedal=[{"t": 0.0, "dur": 2.5}]))
    tops = [s for s in pp["steps"] if s["melodyStruck"]]
    assert tops, "selftest: staccato top voice never registered as melody"
    assert all(s["melodyHold"] <= 0.25 for s in tops), \
        "selftest: pedal inflated hold to %s - staccato lost" % [s["melodyHold"] for s in tops]
    early = [s for s in tops if s["melodyFall"] > 0]
    assert early, "selftest: pedal tail never measured"
    assert all(s["melodyFall"] == 0 for s in tops[3:]), \
        "selftest: pedal tail applied after the pedal lifted"

    assert all(m >= MIN_MIDI for s in pb["steps"] for m in [n["midi"] for n in s["support"]]), \
        "selftest: support below the F2 floor"
    print("selftest OK: block voices %s / broken voices %s / dynamics %.2f..%.2f / staccato hold %.2fs + fall %.2fs"
          % (rb["sizes"][:4], rk["sizes"][:4], min(vels), max(vels),
             early[0]["melodyHold"], early[0]["melodyFall"]))


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
