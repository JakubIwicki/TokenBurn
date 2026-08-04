#!/usr/bin/env python3
"""Expected agent_runs count for a pinned delegate ledger fixture.

Mirror of the DelegateLedgerAdapter's derivation:
- Rows whose handle equals "test" or starts with "test-" are dropped
  (IsTestHandle, case-insensitive) and never become agent_runs rows.
- Every surviving row maps to the pipeline's unique key (session_id, agent_id)
  with agent_id = handle; a missing or whitespace-only session_id falls back to
  the handle (mirroring IsNullOrWhiteSpace in the adapter).
- The later-ended_at-wins upsert collapses same-key rows into one agent_run, so
  the expected row count is the number of DISTINCT (derived_session_id, handle)
  keys.

Usage: python3 derive-expected-count.py <path-to-jsonl-fixture>
Prints the expected count (bare integer) to stdout; breakdown to stderr.
"""

import json
import sys


def is_test_handle(handle: str) -> bool:
    """Mirror of DelegateLedgerAdapter.IsTestHandle (case-insensitive)."""
    lowered = handle.lower()
    return lowered == "test" or lowered.startswith("test-")


def main() -> int:
    path = sys.argv[1]
    total = 0
    dropped = 0
    keys: set[tuple[str, str]] = set()
    session_ids: set[str] = set()
    derived_by_handle: dict[str, set[str]] = {}

    with open(path, encoding="utf-8") as fixture:
        for line in fixture:
            if not line.strip():
                continue
            row = json.loads(line)
            total += 1
            handle = str(row.get("handle") or "")
            if is_test_handle(handle):
                dropped += 1
                continue
            session_id = row.get("session_id") or ""
            derived = handle if not session_id.strip() else session_id
            keys.add((derived, handle))
            session_ids.add(derived)
            derived_by_handle.setdefault(handle, set()).add(derived)

    double_appearing = sum(1 for derived in derived_by_handle.values() if len(derived) >= 2)

    print(len(keys))
    print(
        f"# total={total} dropped_test={dropped} "
        f"distinct_keys={len(keys)} distinct_session_ids={len(session_ids)} "
        f"double_appearing_handles={double_appearing}",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
