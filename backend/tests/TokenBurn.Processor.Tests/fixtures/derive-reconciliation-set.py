#!/usr/bin/env python3
"""Derive the pinned reconciliation corpus for TokenBurn Phase 2 pricing.

The product reconciles the pricing engine's recomputed cost against the
delegate ledger's reported cost. This script produces the pinned corpus
(reconciliation-ledger.jsonl) and the pinned comparison set
(reconciliation-comparison-set.json) from a staged ledger and model registry.

Pipeline (mirror of the C# engine's shared spec, order matters):
  1. Drop test handles (IsTestHandle: "test" or "test-*", case-insensitive).
  2. Row eligibility: the row must carry all three token counts as ints AND a
     `peak` field. Rows missing any are counted but excluded from the corpus.
  3. Key collapse on (derived_session_id, handle); survivor = max ended_at
     (ts-as-UTC + duration_s); exact tie -> the later row in the file wins.
  4. Resolve the surviving row's model slug; unresolved -> coverage gap.
  5. Peak classification from the surviving row's ts (Asia/Shanghai); only
     multiplier == 1.0 rows join the comparison set.
  6. Recompute cost at registry prices x 1.0; |computed - cost_usd| <= 1e-6 pins.

Usage:
    python3 derive-reconciliation-set.py [--ledger PATH] [--models PATH]
                                         [--outdir PATH]
Analysis block prints to stderr; fixtures are written to --outdir.
"""

import argparse
import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

CORPUS_FIELDS = (
    "ts", "handle", "persona", "model", "status", "session_id",
    "cost_usd", "duration_s", "hit_tokens", "miss_tokens", "output_tokens", "peak",
)
PIN_TOLERANCE = 1e-6
MILLION = 1_000_000
PEAK_MULTIPLIER = 2.0
PEAK_WINDOWS = ((9.0, 12.0), (14.0, 18.0))


def shanghai_tz():
    """Asia/Shanghai, falling back to a fixed +08:00 if zoneinfo data is missing."""
    try:
        return ZoneInfo("Asia/Shanghai")
    except Exception:
        return timezone(timedelta(hours=8))


SHANGHAI = shanghai_tz()


def is_test_handle(handle: str) -> bool:
    """Mirror of DelegateLedgerAdapter.IsTestHandle (case-insensitive)."""
    lowered = handle.lower()
    return lowered == "test" or lowered.startswith("test-")


def parse_ts_utc(ts: str) -> datetime:
    """Parse a naive "YYYY-MM-DDTHH:MM:SS" ts and treat it as UTC."""
    return datetime.fromisoformat(ts).replace(tzinfo=timezone.utc)


def peak_multiplier(ts: str) -> float:
    """Compute the peak multiplier for a run from its start ts.

    ts is parsed as UTC, converted to Asia/Shanghai, and classified against the
    half-open windows [09:00,12:00) and [14:00,18:00) local time.
    """
    local = parse_ts_utc(ts).astimezone(SHANGHAI)
    hour_of_day = local.hour + local.minute / 60 + local.second / 3600
    for start, end in PEAK_WINDOWS:
        if start <= hour_of_day < end:
            return PEAK_MULTIPLIER
    return 1.0


def resolve_model(model: str, models: dict) -> str | None:
    """Resolve a ledger model string to a registry slug per the shared spec.

    Strip a trailing [1m] (the only suffix stripped), then try the registry key,
    then exact alias membership. Returns None when unresolved.
    """
    slug = model[:-3] if model.endswith("[1m]") else model
    if slug in models:
        return slug
    for registry_slug, entry in models.items():
        if slug in entry.get("aliases", []):
            return registry_slug
    return None


def load_registry(path: Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        payload = json.load(handle)
    return payload["models"]


def row_tokens(row: dict) -> int:
    """Total token volume for a row (hit + miss + output), missing counts as 0."""
    return (
        (row.get("hit_tokens") or 0)
        + (row.get("miss_tokens") or 0)
        + (row.get("output_tokens") or 0)
    )


def pct(part: int, whole: int) -> str:
    return f"{100.0 * part / whole:.1f}%" if whole else "n/a"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Derive the pinned reconciliation corpus for Phase 2 pricing."
    )
    parser.add_argument(
        "--ledger",
        default="~/.claude/mcp/delegate/logs/ledger.jsonl",
        help="Delegate ledger JSONL (default: ~/.claude/mcp/delegate/logs/ledger.jsonl)",
    )
    parser.add_argument(
        "--models",
        default="~/.claude/config/models.json",
        help="Versioned model registry JSON (default: ~/.claude/config/models.json)",
    )
    parser.add_argument(
        "--outdir",
        default=str(Path(__file__).resolve().parent),
        help="Directory for the emitted fixtures (default: script directory)",
    )
    args = parser.parse_args(argv)

    ledger_path = Path(args.ledger).expanduser()
    models_path = Path(args.models).expanduser()
    outdir = Path(args.outdir).expanduser()
    outdir.mkdir(parents=True, exist_ok=True)

    models = load_registry(models_path)

    total_rows = 0
    test_dropped = 0
    token_less_or_no_peak = 0
    nul_corrupted_lines = 0
    all_rows = []
    eligible_rows = []

    with ledger_path.open(encoding="utf-8", errors="replace") as handle:
        for line in handle:
            # Some staged ledger exports were corrupted with leading NUL bytes
            # on a single line; strip them so the real run survives.
            if "\x00" in line:
                nul_corrupted_lines += 1
                line = line.replace("\x00", "")
            if not line.strip():
                continue
            row = json.loads(line)
            total_rows += 1
            all_rows.append(row)
            handle_raw = row.get("handle") or ""
            if is_test_handle(handle_raw):
                test_dropped += 1
                continue
            tokens_ok = all(
                isinstance(row.get(key), int)
                for key in ("hit_tokens", "miss_tokens", "output_tokens")
            )
            if not tokens_ok or "peak" not in row:
                token_less_or_no_peak += 1
                continue
            eligible_rows.append(row)

    # Key collapse: survivor per (derived_session_id, handle) is max ended_at;
    # on exact tie the later row in the file wins (upsert's >= tie-branch).
    survivors = {}
    for row in eligible_rows:
        session_id = row.get("session_id") or ""
        handle_raw = row.get("handle") or ""
        derived_session_id = session_id if session_id.strip() else handle_raw
        key = (derived_session_id, handle_raw)
        ended_at = parse_ts_utc(row["ts"]) + timedelta(seconds=row.get("duration_s") or 0.0)
        current = survivors.get(key)
        if current is None or ended_at >= current["ended_at"]:
            survivors[key] = {"row": row, "ended_at": ended_at, "key": key}

    distinct_keys = len(survivors)

    resolved_count = 0
    unresolved_count = 0
    unresolved_models = {}
    corpus_rows = []
    for key in sorted(survivors):
        row = survivors[key]["row"]
        model = row.get("model") or ""
        resolved = resolve_model(model, models)
        if resolved is None:
            unresolved_count += 1
            unresolved_models[model] = unresolved_models.get(model, 0) + 1
            continue
        resolved_count += 1
        corpus_rows.append({"row": row, "resolved_slug": resolved, "key": survivors[key]["key"]})

    off_peak_candidates = 0
    peak_rows_excluded = 0
    pinned_rows = []
    near_miss_rows = []
    for entry in corpus_rows:
        entry["pinned"] = False
        prices = models[entry["resolved_slug"]]["prices"]
        computed = (
            entry["row"]["hit_tokens"] * prices["hit"]
            + entry["row"]["miss_tokens"] * prices["miss"]
            + entry["row"]["output_tokens"] * prices["output"]
        ) / MILLION
        entry["computed_cost_usd"] = computed
        multiplier = peak_multiplier(entry["row"]["ts"])
        if multiplier != 1.0:
            peak_rows_excluded += 1
            continue
        off_peak_candidates += 1
        reported = entry["row"].get("cost_usd")
        if reported is not None and abs(computed - reported) <= PIN_TOLERANCE:
            entry["pinned"] = True
            pinned_rows.append(entry)
        else:
            entry["pinned"] = False
            near_miss_rows.append(entry)

    # Emit fixture A: the corpus, one JSON line per surviving key.
    corpus_path = outdir / "reconciliation-ledger.jsonl"
    with corpus_path.open("w", encoding="utf-8") as handle:
        for entry in corpus_rows:
            row = {field: entry["row"][field] for field in CORPUS_FIELDS}
            handle.write(json.dumps(row) + "\n")

    # Emit fixture B: the pinned comparison set, ordered by (session_id, handle).
    comparison_path = outdir / "reconciliation-comparison-set.json"
    comparison = [
        {
            "session_id": entry["key"][0],
            "agent_id": entry["key"][1],
            "model_slug": entry["row"]["model"],
            "reported_cost_usd": entry["row"]["cost_usd"],
        }
        for entry in sorted(pinned_rows, key=lambda e: e["key"])
    ]
    with comparison_path.open("w", encoding="utf-8") as handle:
        json.dump(comparison, handle, indent=2)
        handle.write("\n")

    # Self-check: the emitted fixtures must satisfy the acceptance criteria.
    assert comparison, "comparison set must be non-empty"
    pinned_by_key = {entry["key"]: entry for entry in pinned_rows}
    for entry in comparison:
        key = (entry["session_id"], entry["agent_id"])
        pinned = pinned_by_key[key]
        assert entry["session_id"] == pinned["key"][0]
        assert entry["agent_id"] == pinned["key"][1]
        assert entry["model_slug"] == pinned["row"]["model"]
        assert entry["reported_cost_usd"] == pinned["row"]["cost_usd"]
        prices = models[pinned["resolved_slug"]]["prices"]
        recomputed = (
            pinned["row"]["hit_tokens"] * prices["hit"]
            + pinned["row"]["miss_tokens"] * prices["miss"]
            + pinned["row"]["output_tokens"] * prices["output"]
        ) / MILLION
        assert abs(recomputed - pinned["row"]["cost_usd"]) <= PIN_TOLERANCE, key
    assert sorted(comparison, key=lambda e: (e["session_id"], e["agent_id"])) == comparison

    total_tokens = sum(row_tokens(row) for row in all_rows)
    resolved_tokens = sum(row_tokens(entry["row"]) for entry in corpus_rows)
    pinned_tokens = sum(row_tokens(entry["row"]) for entry in pinned_rows)

    residuals = [
        abs(entry["computed_cost_usd"] - entry["row"]["cost_usd"])
        for entry in near_miss_rows
        if entry["row"].get("cost_usd") is not None
    ]
    max_residual = max(residuals) if residuals else 0.0
    exceeding = sum(1 for r in residuals if r > PIN_TOLERANCE)

    model_stats = {}
    for entry in corpus_rows:
        slug = entry["resolved_slug"]
        stat = model_stats.setdefault(slug, {"rows": 0, "pinned": 0, "residuals": []})
        stat["rows"] += 1
        if entry["pinned"]:
            stat["pinned"] += 1
        if entry["row"].get("cost_usd") is not None:
            stat["residuals"].append(abs(entry["computed_cost_usd"] - entry["row"]["cost_usd"]))

    report = [
        "=== reconciliation derivation report ===",
        f"total_rows={total_rows}",
        f"test_dropped={test_dropped}",
        f"token_less_or_no_peak={token_less_or_no_peak}",
        f"nul_corrupted_lines={nul_corrupted_lines}",
        f"distinct_keys={distinct_keys}",
        "--- resolution ---",
        f"resolved={resolved_count}",
        f"unresolved={unresolved_count}",
    ]
    for model in sorted(unresolved_models):
        report.append(f"  unresolved model {model!r}: {unresolved_models[model]}")
    report.extend([
        "--- peak classification ---",
        f"off_peak_candidates={off_peak_candidates}",
        f"peak_rows_excluded={peak_rows_excluded}",
        "--- comparison ---",
        f"pinned={len(pinned_rows)}",
        f"near_miss={len(near_miss_rows)}",
        f"near_miss_max_abs_residual={max_residual:.12g}",
        f"near_miss_exceeding_1e-6={exceeding}",
        "--- token coverage ---",
        f"total_tokens={total_tokens}",
        f"resolved_tokens={resolved_tokens} ({pct(resolved_tokens, total_tokens)})",
        f"pinned_tokens={pinned_tokens} ({pct(pinned_tokens, total_tokens)})",
        "--- per resolved model ---",
    ])
    for slug in sorted(model_stats):
        stat = model_stats[slug]
        mx = max(stat["residuals"]) if stat["residuals"] else 0.0
        report.append(
            f"model={slug} rows={stat['rows']} pinned={stat['pinned']} max_abs_residual={mx:.12g}"
        )
    report.append("--- sanity: first real ledger row ---")
    if all_rows:
        first = all_rows[0]
        resolved = resolve_model(first.get("model") or "", models)
        mult = peak_multiplier(first["ts"])
        tokens_present = all(
            isinstance(first.get(key), int)
            for key in ("hit_tokens", "miss_tokens", "output_tokens")
        )
        report.append(
            f"handle={first.get('handle')} model={first.get('model')!r} resolves={resolved!r} "
            f"peak_multiplier={mult} tokens_present={tokens_present}"
        )
        report.append(
            "note: sanity row is off-peak (multiplier 1.0); it cannot reconcile because the "
            "staged ledger carries no token fields on this row (excluded by step 2 eligibility)."
        )
    report.append(f"wrote {corpus_path.name} ({len(corpus_rows)} rows)")
    report.append(f"wrote {comparison_path.name} ({len(comparison)} rows)")

    print("\n".join(report), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
