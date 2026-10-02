"""Turns a Chrome console log from a load-test run into a Markdown table.

    python3 summarise.py <chrome log> <scenario name>

Prints the table and writes the parsed rows next to the log as JSON.
"""
import json
import re
import sys

LINE = re.compile(r'"(LOADTEST(?:_STARTUP|_TIMING|_DONE|_ERROR)?)(.*)$')


def main(path, scenario):
    rows, timings, notes = [], [], []
    for raw in open(path, errors="replace"):
        match = LINE.search(raw.rstrip())
        if not match:
            continue
        tag, rest = match.group(1), match.group(2).strip()
        # Messages logged from page JavaScript end with the source location.
        rest = re.sub(r'", source: .*$', "", rest)
        if tag == "LOADTEST":
            rows.append(json.loads(rest))
        elif tag == "LOADTEST_TIMING":
            timings.append(json.loads(rest))
        elif tag == "LOADTEST_ERROR":
            notes.append("ERROR " + rest)

    out = [f"### {scenario}", ""]
    if rows:
        first = rows[0]
        out.append(
            f"{first['layout']} layout, {first['screenWidth']}x{first['screenHeight']} px, "
            f"quality {first['quality']}{' (HD on)' if first.get('hd') else ''}, "
            f"{first['readingsPerSecond'] / first['sensors']:.0f} readings per sensor per second"
            + (f", {first['dropoutSeconds']:.0f} s data dropout" if first.get("dropoutSeconds") else "")
        )
        out.append("")
        dropout = any(r.get("dropoutSeconds") for r in rows)
        header = "| Sensors | Run | Avg fps | Median | p95 | p99 | Worst | 1% low fps | Intake ms/s | Rebuild | Pages | Heap MB |"
        rule = "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
        if dropout:
            header += " Peak stale | Stale at end |"
            rule += "---:|---:|"
        out += [header, rule]
        for r in rows:
            line = (
                f"| {r['sensors']:,} | {r.get('repetition', 1)} | {r['averageFps']:.1f} | "
                f"{r['medianFrameMs']:.0f} ms | {r['p95FrameMs']:.0f} ms | {r['p99FrameMs']:.0f} ms | "
                f"{r['worstFrameMs']:.0f} ms | {r['onePercentLowFps']:.1f} | "
                f"{r['ingestMsPerSecond']:.1f} | {r['rebuildMs']:.0f} ms | {r['pages']} | "
                f"{r['managedHeapBytes'] / 1e6:.1f} |"
            )
            if dropout:
                line += f" {r.get('peakStale', 0):,} | {r.get('staleAtEnd', 0):,} |"
            out.append(line)
    if timings:
        out += ["| Visit | Navigation to interactive |", "|---:|---:|"]
        out += [f"| {t['visit']} | {t['readyMs'] / 1000:.2f} s |" for t in timings]
    if not rows and not timings:
        out.append("No results: the run did not finish. Check the Chrome log.")
    out += notes
    print("\n".join(out) + "\n")
    json.dump({"rows": rows, "timings": timings}, open(path + ".json", "w"), indent=1)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else sys.argv[1])
