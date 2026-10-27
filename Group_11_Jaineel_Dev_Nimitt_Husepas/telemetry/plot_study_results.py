"""
Plots for the 28-Oct-2026 follow-up study (S021 - Nimitt Jain), drawn from telemetry/trial_data_real.csv only.

    docs/figures/s021_sus_boxplot.png     SUS per group (all participants, not-team, team)
    docs/figures/s021_tlx_subscales.png   NASA-TLX sub-scale ratings (all participants)

Run from the group folder:  py telemetry/plot_study_results.py
"""
import csv
import os
import statistics as st
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from analyze_real_trials import sus_score, SUS_COLS, TLX_COLS  # noqa: E402

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

FIG = os.path.join(HERE, "..", "docs", "figures")
rows = list(csv.DictReader(open(os.path.join(HERE, "trial_data_real.csv"), newline="", encoding="utf-8")))
for r in rows:
    r["sus"] = sus_score([int(r[c]) for c in SUS_COLS])
    r["sub"] = [float(r[c]) for c in TLX_COLS]

groups = [("All\n(n=%d)" % len(rows), rows),
          ("Not on the team\n(n=%d)" % sum(r["team_member"] == "no" for r in rows), [r for r in rows if r["team_member"] == "no"]),
          ("Team members\n(n=%d)" % sum(r["team_member"] == "yes" for r in rows), [r for r in rows if r["team_member"] == "yes"])]

fig, ax = plt.subplots(figsize=(5.2, 3.3), dpi=220)
ax.boxplot([[r["sus"] for r in g] for _, g in groups], showmeans=True)
ax.set_xticklabels([n for n, _ in groups], fontsize=7)
ax.axhline(68, color="#c53030", linestyle="--", linewidth=1)
ax.text(0.55, 69, "SUS 68 benchmark", fontsize=6, color="#c53030")
ax.set_ylim(0, 105)
ax.set_ylabel("SUS score (0-100)", fontsize=8)
ax.set_title("SUS, one session per person", fontsize=9)
ax.tick_params(axis="y", labelsize=7)
fig.tight_layout()
fig.savefig(os.path.join(FIG, "s021_sus_boxplot.png"))

labels = ["Mental", "Physical", "Temporal", "Performance", "Effort", "Frustration"]
fig, ax = plt.subplots(figsize=(5.2, 3.3), dpi=220)
ax.boxplot([[r["sub"][i] for r in rows] for i in range(6)], showmeans=True)
ax.set_xticklabels(labels, fontsize=7)
ax.set_ylim(-3, 103)
ax.set_ylabel("NASA-TLX rating (0-100, lower is better)", fontsize=7)
ax.set_title("NASA-TLX sub-scales (n=%d)" % len(rows), fontsize=9)
ax.tick_params(axis="y", labelsize=7)
fig.tight_layout()
fig.savefig(os.path.join(FIG, "s021_tlx_subscales.png"))

allsus = [r["sus"] for r in rows]
print("SUS per participant:", [r["sus"] for r in rows])
print("mean %.2f sd %.2f median %.2f min %.1f max %.1f  >=68: %d of %d" % (
    st.mean(allsus), st.stdev(allsus), st.median(allsus), min(allsus), max(allsus), sum(v >= 68 for v in allsus), len(allsus)))
for name, g in groups[1:]:
    v = [r["sus"] for r in g]
    print(name.replace("\n", " "), "mean %.2f sd %.2f median %.2f" % (st.mean(v), st.stdev(v), st.median(v)))
print("TLX sub-scale means:", {l: round(st.mean(r["sub"][i] for r in rows), 1) for i, l in enumerate(labels)})
tl = [sum(r["sub"]) / 6 for r in rows]
print("TLX raw per participant:", [round(t, 1) for t in tl])
print("route:", {k: sum(r["route_taken"] == k for r in rows) for k in ("STAIRS", "LIFT", "BOTH")},
      "found room yes:", sum(r["found_room"] == "YES" for r in rows),
      "dizzy:", {k: sum(r["dizziness"] == k for r in rows) for k in ("NO", "A LITTLE")})
