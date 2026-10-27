"""
Route-model analytics for the Quest campus build (J032 - Husepas Marfatia).

Input : telemetry/MODEL_route_metrics.csv  (one row per destination, produced inside the Unity project by the
        baked walkable-grid planner: stairs route length, lobby-to-lift length, lift-to-room length, lift ride time)
Output: telemetry/MODEL_route_summary.csv  (per-level statistics)
        docs/figures/j032_route_length_by_level.png

IMPORTANT: every number here is a deterministic property of the 3D model and the planner. None of it comes from
people. It is NOT usability or participant data and must not be presented as such.

Run from the group folder:  python telemetry/analyze_route_metrics.py
"""
import csv
import os
import statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "MODEL_route_metrics.csv")
OUT_CSV = os.path.join(HERE, "MODEL_route_summary.csv")
OUT_PNG = os.path.join(HERE, "..", "docs", "figures", "j032_route_length_by_level.png")
ORDER = ["LG", "G", "1", "2", "3", "4", "5", "6", "7", "8"]
WALK_SPEED = 1.2  # m/s, assumption used only for the time estimate


def load():
    with open(SRC, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    for r in rows:
        for k in ("stairs_route_m", "lift_route_total_m", "lobby_to_lift_m", "lift_to_room_m", "lift_ride_s"):
            r[k] = float(r[k])
        r["stairs_walkable"] = r["stairs_walkable"] == "True"
    return rows


def summarise(rows):
    out = []
    for lvl in ORDER:
        sel = [r for r in rows if r["floor_label"] == lvl]
        if not sel:
            continue
        s = [r["stairs_route_m"] for r in sel]
        l = [r["lift_route_total_m"] for r in sel]
        out.append({
            "level": lvl, "rooms": len(sel),
            "stairs_mean_m": round(st.mean(s), 1), "stairs_min_m": round(min(s), 1), "stairs_max_m": round(max(s), 1),
            "lift_mean_m": round(st.mean(l), 1), "lift_min_m": round(min(l), 1), "lift_max_m": round(max(l), 1),
            "lift_ride_s": round(st.mean(r["lift_ride_s"] for r in sel), 1),
        })
    return out


def main():
    rows = load()
    summ = summarise(rows)
    with open(OUT_CSV, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=list(summ[0].keys()))
        w.writeheader()
        w.writerows(summ)

    s_all = [r["stairs_route_m"] for r in rows]
    l_all = [r["lift_route_total_m"] for r in rows]
    shorter_lift = sum(1 for r in rows if r["lift_route_total_m"] < r["stairs_route_m"])
    print("destinations:", len(rows), " reachable on foot:", sum(r["stairs_walkable"] for r in rows))
    print("stairs route m  min/mean/median/max = %.1f / %.1f / %.1f / %.1f" % (min(s_all), st.mean(s_all), st.median(s_all), max(s_all)))
    print("lift   route m  min/mean/median/max = %.1f / %.1f / %.1f / %.1f" % (min(l_all), st.mean(l_all), st.median(l_all), max(l_all)))
    print("lift route shorter than stairs route for %d of %d rooms" % (shorter_lift, len(rows)))
    print("at %.1f m/s: stairs mean %.0f s, lift mean %.0f s (excluding lift wait)" % (WALK_SPEED, st.mean(s_all) / WALK_SPEED, st.mean(l_all) / WALK_SPEED))
    print("\nlevel  rooms  stairs_mean  lift_mean")
    for r in summ:
        print("%-6s %5d  %10.1f  %9.1f" % (r["level"], r["rooms"], r["stairs_mean_m"], r["lift_mean_m"]))

    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print("matplotlib not installed - figure skipped")
        return
    xs = list(range(len(summ)))
    fig, ax = plt.subplots(figsize=(8, 4.2), dpi=200)
    ax.bar([x - 0.2 for x in xs], [r["stairs_mean_m"] for r in summ], width=0.4, label="Stairs route", color="#2b6cb0")
    ax.bar([x + 0.2 for x in xs], [r["lift_mean_m"] for r in summ], width=0.4, label="Lift route", color="#dd6b20")
    ax.set_xticks(xs)
    ax.set_xticklabels([r["level"] for r in summ])
    ax.set_xlabel("Destination level (LG = lower ground, G = ground)")
    ax.set_ylabel("Mean route length from the main gate (m)")
    ax.set_title("Planned route length by level - model-derived (96 destinations)")
    ax.legend()
    ax.grid(axis="y", alpha=0.3)
    fig.text(0.01, 0.01, "Model output of the walkable-grid planner. Not participant data.", fontsize=7, color="#555555")
    fig.tight_layout(rect=(0, 0.03, 1, 1))
    os.makedirs(os.path.dirname(OUT_PNG), exist_ok=True)
    fig.savefig(OUT_PNG)
    print("figure written:", os.path.normpath(OUT_PNG))


if __name__ == "__main__":
    main()
