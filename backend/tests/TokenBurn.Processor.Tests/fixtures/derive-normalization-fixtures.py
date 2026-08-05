#!/usr/bin/env python3
"""Derive the normalization fixtures for the C# adapter + dedupe tests.

Reproduces the sampling and redaction that produced the committed fixtures in
this directory:

  transcript-<sessionId>.jsonl
      The first N events of a real Claude Code transcript, redacted to a strict
      whitelist so no prompt/response text, tool I/O, or path leaves the system.
      Kept: event ``type``, ``timestamp``, ``sessionId`` and ``message`` with
      ``role``, ``model``, the four token-count ``usage`` fields
      (input_tokens / cache_read_input_tokens / cache_creation_input_tokens /
      output_tokens) and ``content`` replaced by the ``<redacted>`` placeholder.
      Everything else is dropped.

  delegate-run-log-sample.jsonl
      The meta line (``started``, ``handle``, ``persona``), a few event lines
      and the result line (``is_error``, ``stop_reason``, ``usage``,
      ``session_id``) of real delegate run logs, redacted with the same
      whitelist discipline.

  jicaching-sample.jsonl
      A small synthetic sample documenting the provisional ring-buffer shape the
      jicaching adapter assumes; the real ~/.jicaching/state.json is unusable.

Usage:
    python3 derive-normalization-fixtures.py [--sessions ID ...] [--outdir PATH]
                                             [--event-limit N] [--run-log-count N]
Analysis block prints to stderr; fixtures are written to --outdir.
"""

import argparse
import json
import sys
from pathlib import Path

USAGE_FIELDS = (
    "input_tokens",
    "cache_read_input_tokens",
    "cache_creation_input_tokens",
    "output_tokens",
)
DEFAULT_SESSIONS = (
    "004361d7-c20b-4850-b7ed-054734d759cc",
    "084aea47-5ae6-48dd-a5ca-c3d8c626c46a",
    "00e942db-e64f-4e9e-99d9-f89c8d96d78b",
)
RUN_LOG_EXCLUDES = ("ledger.jsonl", "latest.jsonl")


def load_jsonl(path: Path) -> list[dict]:
    """Load JSONL, stripping NUL corruption and skipping unparseable lines."""
    events: list[dict] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if "\x00" in line:
            line = line.replace("\x00", "")
        if not line.strip():
            continue
        try:
            events.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return events


def redact_transcript_event(event: dict) -> dict:
    """Keep only the normalization whitelist; replace content with a placeholder."""
    redacted: dict = {}
    for key in ("type", "timestamp", "sessionId"):
        if key in event:
            redacted[key] = event[key]
    message = event.get("message")
    if isinstance(message, dict):
        kept_message: dict = {}
        for key in ("role", "model"):
            if key in message:
                kept_message[key] = message[key]
        usage = message.get("usage")
        if isinstance(usage, dict):
            kept_usage = {key: usage[key] for key in USAGE_FIELDS if key in usage}
            if kept_usage:
                kept_message["usage"] = kept_usage
        if "content" in message:
            kept_message["content"] = "<redacted>"
        redacted["message"] = kept_message
    return redacted


def redact_run_log_meta(meta: dict, handle: str) -> dict:
    """Keep started / handle / persona; the full task prompt and cwd are dropped."""
    return {"type": "meta", "started": meta.get("started"), "handle": handle, "persona": meta.get("persona")}


def redact_run_log_result(result: dict) -> dict:
    """Keep the terminal shape; the final result text is dropped."""
    redacted: dict = {
        "type": "result",
        "is_error": result.get("is_error"),
        "stop_reason": result.get("stop_reason"),
        "session_id": result.get("session_id"),
    }
    usage = result.get("usage")
    if isinstance(usage, dict):
        kept_usage = {key: usage[key] for key in USAGE_FIELDS if key in usage}
        if kept_usage:
            redacted["usage"] = kept_usage
    return redacted


def redact_run_log_event(event: dict) -> dict:
    """Keep only structural identity; message and tool payloads are dropped."""
    redacted: dict = {"type": event.get("type")}
    if event.get("type") == "system" and "subtype" in event:
        redacted["subtype"] = event["subtype"]
    if "session_id" in event:
        redacted["session_id"] = event["session_id"]
    return redacted


def sample_run_log(log_path: Path, event_limit: int) -> list[dict]:
    """Extract meta, up to event_limit representative events, and the result."""
    events = load_jsonl(log_path)
    meta = next((e for e in events if e.get("type") == "meta"), None)
    result = next((e for e in events if e.get("type") == "result"), None)
    if meta is None or result is None:
        return []
    handle = log_path.stem
    sampled = [redact_run_log_meta(meta, handle)]
    for wanted in ("system", "user", "assistant"):
        for e in events:
            if e.get("type") == wanted and e is not meta and e is not result:
                sampled.append(redact_run_log_event(e))
                break
        if len(sampled) - 1 >= event_limit:
            break
    sampled.append(redact_run_log_result(result))
    return sampled


def write_jicaching_sample(path: Path) -> int:
    """Write the synthetic ring-buffer shape sample; returns the line count."""
    entries = [
        {
            "synthetic": True,
            "record_type": "state",
            "shape": "ring-buffer",
            "capacity": 32,
            "description": "provisional jicaching usage snapshot ring; real ~/.jicaching/state.json is unusable",
        },
        {
            "synthetic": True,
            "record_type": "entry",
            "seq": 0,
            "ts": "2026-08-04T08:00:00Z",
            "session_id": "00000000-0000-4000-8000-000000000001",
            "handle": "20260804-080000-000001-deadbeef",
            "model": "deepseek-v4-flash",
            "usage": {"input_tokens": 1250, "cache_read_input_tokens": 51200, "cache_creation_input_tokens": 0, "output_tokens": 240},
            "cost_usd": 0.000851,
        },
        {
            "synthetic": True,
            "record_type": "entry",
            "seq": 1,
            "ts": "2026-08-04T08:02:15Z",
            "session_id": "00000000-0000-4000-8000-000000000002",
            "handle": "20260804-080210-000002-beef0001",
            "model": "openai/gpt-5.6-luna",
            "usage": {"input_tokens": 890, "cache_read_input_tokens": 204800, "cache_creation_input_tokens": 512, "output_tokens": 96},
            "cost_usd": 0.002437,
        },
    ]
    with path.open("w", encoding="utf-8") as handle:
        for entry in entries:
            handle.write(json.dumps(entry) + "\n")
    return len(entries)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Derive the normalization fixtures for the C# adapter + dedupe tests."
    )
    parser.add_argument(
        "--sessions",
        nargs="+",
        default=list(DEFAULT_SESSIONS),
        help="Session ids to sample transcripts for (default: the pinned fixture sessions).",
    )
    parser.add_argument(
        "--outdir",
        default=str(Path(__file__).resolve().parent),
        help="Directory for the emitted fixtures (default: script directory).",
    )
    parser.add_argument(
        "--projects-dir",
        default="~/.claude/projects",
        help="Claude Code transcript root (default: ~/.claude/projects).",
    )
    parser.add_argument(
        "--run-logs-dir",
        default="~/.claude/mcp/delegate/logs",
        help="Delegate run-log directory (default: ~/.claude/mcp/delegate/logs).",
    )
    parser.add_argument(
        "--event-limit",
        type=int,
        default=12,
        help="Number of transcript events to sample per session (default: 12).",
    )
    parser.add_argument(
        "--run-log-count",
        type=int,
        default=2,
        help="Number of completed run logs to sample (default: 2).",
    )
    args = parser.parse_args(argv)

    outdir = Path(args.outdir).expanduser()
    outdir.mkdir(parents=True, exist_ok=True)
    projects_dir = Path(args.projects_dir).expanduser()
    run_logs_dir = Path(args.run_logs_dir).expanduser()

    report: list[str] = ["=== normalization fixtures report ==="]
    written: list[str] = []

    for session_id in args.sessions:
        candidates = sorted(projects_dir.glob(f"*/{session_id}.jsonl"))
        if not candidates:
            report.append(f"session {session_id}: transcript NOT FOUND")
            continue
        transcript_path = candidates[0]
        events = load_jsonl(transcript_path)
        sampled = events[: args.event_limit]
        redacted = [redact_transcript_event(e) for e in sampled]
        fixture_path = outdir / f"transcript-{session_id}.jsonl"
        with fixture_path.open("w", encoding="utf-8") as handle:
            for event in redacted:
                handle.write(json.dumps(event) + "\n")
        report.append(
            f"session {session_id}: source={transcript_path.name} total_events={len(events)} "
            f"sampled={len(sampled)} -> {fixture_path.name}"
        )
        written.append(fixture_path.name)

    run_log_sample_path = outdir / "delegate-run-log-sample.jsonl"
    candidate_logs = sorted(
        p for p in run_logs_dir.glob("*.jsonl") if p.name not in RUN_LOG_EXCLUDES
    )
    usable = []
    for p in candidate_logs:
        lines = sample_run_log(p, args.event_limit)
        if not lines:
            continue
        result = next((line for line in lines if line.get("type") == "result"), None)
        session_ok = 0 if (result and result.get("session_id")) else 1
        success_ok = 0 if (result and result.get("is_error") is False) else 1
        usable.append((session_ok, success_ok, p.name, p, lines))
    usable.sort(key=lambda item: (item[0], item[1], item[2]))
    picked = [(item[3], item[4]) for item in usable[: args.run_log_count]]
    if not picked:
        report.append(
            "run-log sample: no usable completed run logs found; writing synthetic meta/result/event sample"
        )
        synthetic = [
            {"type": "meta", "started": 1785789000.0, "handle": "synthetic-000001-000000", "persona": "python-coder"},
            {"type": "system", "subtype": "init", "session_id": "00000000-0000-4000-8000-000000000001"},
            {"type": "user", "session_id": "00000000-0000-4000-8000-000000000001"},
            {"type": "result", "is_error": False, "stop_reason": "end_turn", "session_id": "00000000-0000-4000-8000-000000000001", "usage": {"input_tokens": 10, "cache_read_input_tokens": 20, "cache_creation_input_tokens": 0, "output_tokens": 30}},
        ]
        picked = [(Path("synthetic.jsonl"), synthetic)]
    with run_log_sample_path.open("w", encoding="utf-8") as handle:
        line_count = 0
        for log_path, lines in picked:
            for line in lines:
                handle.write(json.dumps(line) + "\n")
                line_count += 1
    report.append(
        f"run-log sample: source={[p.name for p, _ in picked]} lines={line_count} -> {run_log_sample_path.name}"
    )
    written.append(run_log_sample_path.name)

    jicaching_path = outdir / "jicaching-sample.jsonl"
    n = write_jicaching_sample(jicaching_path)
    report.append(f"jicaching sample: lines={n} (synthetic ring-buffer shape) -> {jicaching_path.name}")
    written.append(jicaching_path.name)

    report.append("--- wrote ---")
    report.extend(f"wrote {name}" for name in written)

    print("\n".join(report), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
