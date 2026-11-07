#!/usr/bin/env python3
"""Compare two PoE2 fragment tables and write a reviewable diff.

The report lists every changed pattern with the old and new fragment, both
lengths, and the length delta. Added and removed patterns carry the signed
length of the side that exists. Keep the report outside the repository; the
shipped table and proof remain the source of truth.

Usage:
  python3 tools/fragment-diff/run.py \
    --old /tmp/opencode/poe2.fragments.main.json \
    --new data/catalogs/poe2.fragments.json \
    --reason "advanced oracle forms" \
    --out-md /tmp/opencode/fragment-diff.md \
    --out-json /tmp/opencode/fragment-diff.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def load_table(path: Path) -> tuple[dict[str, str], dict[str, dict[str, str]]]:
    data = json.loads(path.read_text())
    return data.get("Global", {}), data.get("ByClass", {})


def display(value: str | None) -> str:
    if value is None:
        return "—"
    return (
        value.replace("\x01", "#")
        .replace("|", "\\|")
        .replace("`", "\\`")
        .replace("\r", "")
        .replace("\n", "<br>")
    )


def signed_delta(delta: int) -> str:
    return f"{delta:+d}" if delta else "0"


def diff_scope(old: dict[str, str], new: dict[str, str]) -> list[dict]:
    changes = []
    for pattern in sorted(set(old) | set(new)):
        before = old.get(pattern)
        after = new.get(pattern)
        if before == after:
            continue
        if before is None:
            delta = len(after or "")
            kind = "added"
        elif after is None:
            delta = -len(before)
            kind = "removed"
        else:
            delta = len(after) - len(before)
            kind = "changed"
        changes.append(
            {
                "kind": kind,
                "pattern": pattern,
                "old": before,
                "new": after,
                "oldLength": None if before is None else len(before),
                "newLength": None if after is None else len(after),
                "delta": delta,
            }
        )
    return changes


def scope_summary(name: str, old: dict[str, str], new: dict[str, str],
                  changes: list[dict]) -> dict:
    return {
        "scope": name,
        "oldPatterns": len(old),
        "newPatterns": len(new),
        "changed": sum(c["kind"] == "changed" for c in changes),
        "added": sum(c["kind"] == "added" for c in changes),
        "removed": sum(c["kind"] == "removed" for c in changes),
        "netLengthDelta": sum(c["delta"] for c in changes),
        "changes": changes,
    }


def fragment_cell(value: str | None, length: int | None) -> str:
    if value is None:
        return "—"
    return f"`{display(value)}` ({length})"


def markdown_table(rows: list[dict], include_scope: bool = False) -> list[str]:
    if include_scope:
        lines = [
            "| Class | Pattern | Old | New | Δ |",
            "|---|---|---|---|---:|",
        ]
    else:
        lines = [
            "| Pattern | Old | New | Δ |",
            "|---|---|---|---:|",
        ]
    for change in rows:
        values = [
            f"`{display(change['pattern'])}`",
            fragment_cell(change["old"], change["oldLength"]),
            fragment_cell(change["new"], change["newLength"]),
            signed_delta(change["delta"]),
        ]
        if include_scope:
            values.insert(0, display(change["scope"]))
        lines.append("| " + " | ".join(values) + " |")
    return lines


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old", required=True, type=Path,
                        help="fragment table from the target/base branch")
    parser.add_argument("--new", required=True, type=Path,
                        help="fragment table from the change branch")
    parser.add_argument("--reason", default="",
                        help="why the table changed; copied into the report")
    parser.add_argument("--out-md", default="/tmp/opencode/fragment-diff.md",
                        type=Path)
    parser.add_argument("--out-json", default="/tmp/opencode/fragment-diff.json",
                        type=Path)
    args = parser.parse_args()

    old_global, old_classes = load_table(args.old)
    new_global, new_classes = load_table(args.new)

    global_changes = diff_scope(old_global, new_global)
    class_summaries = []
    for name in sorted(set(old_classes) | set(new_classes)):
        class_changes = diff_scope(old_classes.get(name, {}), new_classes.get(name, {}))
        class_summaries.append(
            scope_summary(name, old_classes.get(name, {}), new_classes.get(name, {}),
                          class_changes)
        )

    global_summary = scope_summary("global", old_global, new_global, global_changes)
    all_summaries = [global_summary, *class_summaries]
    totals = {
        "changed": sum(s["changed"] for s in all_summaries),
        "added": sum(s["added"] for s in all_summaries),
        "removed": sum(s["removed"] for s in all_summaries),
        "netLengthDelta": sum(s["netLengthDelta"] for s in all_summaries),
    }

    lines = [
        "# Fragment diff report",
        "",
        f"- Old table: `{args.old}`",
        f"- New table: `{args.new}`",
    ]
    if args.reason:
        lines.append(f"- Reason: {args.reason}")
    lines += [
        "",
        "Δ is the new fragment length minus the old fragment length. Added rows "
        "carry `+new len`; removed rows carry `-old len`.",
        "",
        "## Summary",
        "",
        "| Scope | Old patterns | New patterns | Changed | Added | Removed | Net Δ |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for summary in all_summaries:
        if not summary["changes"]:
            continue
        lines.append(
            f"| {display(summary['scope'])} | {summary['oldPatterns']} | "
            f"{summary['newPatterns']} | {summary['changed']} | {summary['added']} | "
            f"{summary['removed']} | {signed_delta(summary['netLengthDelta'])} |"
        )
    lines += [
        "",
        f"Total: **{totals['changed']} changed**, **{totals['added']} added**, "
        f"**{totals['removed']} removed**, net length "
        f"**{signed_delta(totals['netLengthDelta'])}**.",
        "",
        "## Global changes",
        "",
    ]
    if global_changes:
        lines += markdown_table(global_changes)
    else:
        lines.append("No global changes.")

    lines += ["", "## Class changes", ""]
    class_rows = [
        {**change, "scope": summary["scope"]}
        for summary in class_summaries
        for change in summary["changes"]
    ]
    if class_rows:
        lines += markdown_table(class_rows, include_scope=True)
    else:
        lines.append("No class changes.")

    args.out_md.parent.mkdir(parents=True, exist_ok=True)
    args.out_md.write_text("\n".join(lines).rstrip() + "\n")

    payload = {
        "old": str(args.old),
        "new": str(args.new),
        "reason": args.reason,
        "totals": totals,
        "global": global_summary,
        "classes": class_summaries,
    }
    args.out_json.parent.mkdir(parents=True, exist_ok=True)
    args.out_json.write_text(json.dumps(payload, indent=2) + "\n")

    print(
        f"fragment-diff: {totals['changed']} changed, {totals['added']} added, "
        f"{totals['removed']} removed, net Δ {signed_delta(totals['netLengthDelta'])}"
    )
    print(f"  {args.out_md}")
    print(f"  {args.out_json}")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
