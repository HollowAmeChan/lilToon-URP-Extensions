from __future__ import annotations

import argparse
from datetime import date
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description="Append an evidence-backed note to the Ho MMD scene skill log.")
    parser.add_argument("heading")
    parser.add_argument("--stage", default="general")
    parser.add_argument("--evidence", required=True)
    parser.add_argument("--confidence", choices=("confirmed", "working hypothesis"), default="confirmed")
    parser.add_argument("note")
    parser.add_argument("--log", type=Path, default=Path(__file__).parents[1] / "references" / "experience-log.md")
    args = parser.parse_args()
    path = args.log.resolve()
    with path.open("a", encoding="utf-8") as stream:
        stream.write(f"\n## {date.today().isoformat()} — {args.heading}\n\n")
        stream.write(f"- Stage/subsystem: {args.stage}. {args.note}\n")
        stream.write(f"- Evidence: `{args.evidence}`. Confidence: {args.confidence}.\n")


if __name__ == "__main__":
    main()
