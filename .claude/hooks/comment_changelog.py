#!/usr/bin/env python3
"""Blocking PreToolUse hook: refuse C# edits whose new comments narrate the change.

CLAUDE.md: "코드 변경했으면 주석에 변경한 내용 덧붙이지 말고 전체 행동만 써", "주석에 오류
고쳤다고 쓰지 마". A comment must describe what the code does now, never what it used to do,
what was tried, or what got fixed — that history belongs in git, and it rots in place.

Unlike comment_length.py this one denies the tool call, because the model has already been
told the rule and still writes these; an advisory note after the fact does not stop it.
Runs on PreToolUse, so old_string/content are the *proposed* text and nothing is on disk yet.

Both // and /// are checked: the worst offenders live in class-top doc blocks, which the
length rule deliberately exempts. Only lines the edit ADDS are considered.

Patterns are deliberately narrow — past-tense verbs about the code itself and explicit
before/after markers. A comment saying the code "no longer allocates" is still history.
Any failure degrades to allow: a broken hook must never wedge editing.
"""
import json
import re
import sys

PATTERNS = [
    r"\bwas (tried|pulled|removed|renamed|reverted|replaced|dropped|added|changed|broken)\b",
    r"\bwere (tried|removed|replaced|dropped|added|changed)\b",
    r"\b(used to|previously|formerly|originally)\b",
    r"\bno longer\b",
    r"\bfix(ed|es)? (a |the )?(bug|issue|crash|regression|typo)\b",
    r"\bbug ?fix\b",
    r"\b(before|after) (this|the) (change|fix|edit|patch|refactor)\b",
    r"\binstead of (the |our )?(old|previous|former)\b",
    r"\b(now|already) (uses|returns|does|handles|runs) .*\binstead\b",
    r"기존에는|예전에는|이전에는|원래는|원래\s*는",
    r"버그\s*수정|수정했|수정함|고쳤|고침|제거했|제거함|변경했|변경함|바꿨|바꿈|추가했|추가함",
]
RX = [re.compile(p, re.IGNORECASE) for p in PATTERNS]


def comment_lines(text):
    """Every // or /// line in the text, stripped."""
    out = []
    for line in (text or "").splitlines():
        s = line.strip()
        if s.startswith("//"):
            out.append(s)
    return out


def main():
    data = json.load(sys.stdin)
    if data.get("tool_name") not in ("Edit", "Write"):
        return
    ti = data.get("tool_input") or {}
    path = (ti.get("file_path") or "").replace("\\", "/")
    if not path.endswith(".cs"):
        return

    new = ti.get("new_string")
    if new is None:
        new = ti.get("content") or ""
    prior = set(comment_lines(ti.get("old_string") or ""))

    hits = [(l, rx.pattern) for l in comment_lines(new) if l not in prior
            for rx in RX if rx.search(l)]
    if not hits:
        return

    lines = ["This edit writes change history into a comment, which CLAUDE.md forbids:"]
    seen = set()
    for text, pat in hits:
        if text in seen:
            continue
        seen.add(text)
        lines.append("  {}     [matched: {}]".format(text, pat))
    lines.append("")
    lines.append("Comments state what the code does NOW. Drop the before/after, the "
                 "what-was-tried, and the what-got-fixed, then retry the edit. "
                 "That history belongs in the commit message.")

    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": "\n".join(lines),
        }
    }))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass
    sys.exit(0)
