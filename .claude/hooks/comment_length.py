#!/usr/bin/env python3
"""Advisory PostToolUse hook: surface newly added multi-line // comments in C# edits.

Never blocks. On a Write/Edit to a *.cs file it collects the comment lines the edit ADDS,
and if any of them form a run of 2-3 consecutive lines it returns additionalContext so the
model re-reads them against the project rule. It does not decide — a comment that earns its
place stays, and no reply is expected when the added comments already meet the bar.

Modelled on b-editor/beutl .claude/hooks/check-comment-necessity.sh and
erigontech/erigon .claude/hooks/comment-policy.py, both advisory PostToolUse hooks.

Only genuinely new runs are flagged: anything already present in old_string is skipped, so
moving or reindenting the repo's existing comments never fires. The edited file itself is
NOT consulted — PostToolUse runs after the write, so the file already contains the new run
and every added comment would look pre-existing.
Rule: CLAUDE.md "주석 길게 쓰지 마". A 4+ line class-top requirement block is sanctioned
and is not flagged.

Any failure degrades to silence — the edit already happened and must not be disturbed.
"""
import json
import sys


def runs(text):
    """Every maximal run of consecutive // lines, as stripped text."""
    out, run = [], []
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("//") and not s.startswith("///"):
            run.append(s)
            continue
        if run:
            out.append(run)
        run = []
    if run:
        out.append(run)
    return out


def known(text):
    return "\n".join("\n".join(r) for r in runs(text))


def main():
    data = json.load(sys.stdin)
    ti = data.get("tool_input") or {}
    path = (ti.get("file_path") or "").replace("\\", "/")
    if not path.endswith(".cs"):
        return

    new = ti.get("new_string")
    if new is None:
        new = ti.get("content") or ""

    prior = known(ti.get("old_string") or "")

    flagged = [r for r in runs(new) if 2 <= len(r) <= 3 and "\n".join(r) not in prior]
    if not flagged:
        return

    msg = ["This edit added a {}-line comment to {}:".format(len(flagged[0]), path.rsplit("/", 1)[-1])]
    for run in flagged[:4]:
        msg += ["  " + line for line in run] + [""]
    msg.append("Project rule is one line. Cut it to the single line that states what the code "
               "cannot show, or drop it. If the explanation is genuinely needed at length, it "
               "belongs in the class-top requirement block, which is exempt. A comment that "
               "earns its place stays — no reply needed if these already meet the bar.")

    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": "\n".join(msg),
        }
    }))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        pass
    sys.exit(0)
