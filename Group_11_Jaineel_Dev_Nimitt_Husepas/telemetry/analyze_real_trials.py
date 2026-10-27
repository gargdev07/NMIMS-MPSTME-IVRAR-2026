"""
Analysis of REAL usability-trial data for the Quest campus wayfinding build (S021 - Nimitt Jain).

Input : a CSV in the layout of telemetry/TRIAL_DATA_TEMPLATE.csv (one row per participant per condition,
        anonymous IDs P01, P02, ...). Fill it only with data from real volunteers who tested the build.
Output: console summary (N, mean, SD, median, 95% CI), optional paired / Welch comparison of two conditions,
        and a box-plot of SUS per condition (docs/figures/s021_sus_boxplot.png) when matplotlib is available.

Standard library only for the statistics, so the numbers can be checked by hand.

    SUS  = 2.5 * ( sum(odd items - 1) + sum(5 - even items) )                       (Brooke, 1996; 0-100)
    TLX  = mean of the six sub-scales (raw NASA-TLX, 0-100)
    paired t:  t = mean(d) / ( sd(d) / sqrt(n) ),  df = n - 1,  d = A - B for the same participant
    Welch t :  t = (mA - mB) / sqrt(vA/nA + vB/nB), Welch-Satterthwaite df
    Cohen's d (paired: dz = mean(d)/sd(d); independent: pooled SD)

Run:  python telemetry/analyze_real_trials.py telemetry/<your_real_data>.csv
"""
import csv
import math
import os
import statistics as st
import sys
from collections import defaultdict

SUS_COLS = ["sus_q%d" % i for i in range(1, 11)]
TLX_COLS = ["tlx_mental", "tlx_physical", "tlx_temporal", "tlx_performance", "tlx_effort", "tlx_frustration"]
MIN_N = 10  # course minimum (dossier Section 4)


# ----------------------------------------------------------------------------- scoring

def sus_score(items):
    if len(items) != 10 or any(not (1 <= v <= 5) for v in items):
        raise ValueError("SUS needs ten answers between 1 and 5")
    odd = sum(items[i] - 1 for i in range(0, 10, 2))
    even = sum(5 - items[i] for i in range(1, 10, 2))
    return 2.5 * (odd + even)


def tlx_raw(values):
    if len(values) != 6 or any(not (0 <= v <= 100) for v in values):
        raise ValueError("NASA-TLX needs six ratings between 0 and 100")
    return sum(values) / 6.0


# ----------------------------------------------------------------------------- Student t distribution

def _betacf(a, b, x):
    tiny = 1e-30
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    d = tiny if abs(d) < tiny else d
    d = 1.0 / d
    h = d
    for m in range(1, 300):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d; d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c; c = tiny if abs(c) < tiny else c
        d = 1.0 / d; h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d; d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c; c = tiny if abs(c) < tiny else c
        d = 1.0 / d; delta = d * c; h *= delta
        if abs(delta - 1.0) < 3e-12:
            break
    return h


def _betainc(a, b, x):
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    bt = math.exp(math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log(1.0 - x))
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def t_cdf(t, df):
    x = df / (df + t * t)
    p = 0.5 * _betainc(df / 2.0, 0.5, x)
    return 1.0 - p if t > 0 else p


def t_two_sided_p(t, df):
    return 2.0 * (1.0 - t_cdf(abs(t), df))


def t_crit(df, conf=0.95):
    target = 1.0 - (1.0 - conf) / 2.0
    lo, hi = 0.0, 50.0
    for _ in range(100):
        mid = (lo + hi) / 2.0
        if t_cdf(mid, df) < target:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2.0


# ----------------------------------------------------------------------------- summaries

def describe(vals):
    n = len(vals)
    m = st.mean(vals)
    sd = st.stdev(vals) if n > 1 else float("nan")
    ci = t_crit(n - 1) * sd / math.sqrt(n) if n > 1 else float("nan")
    return {"n": n, "mean": m, "sd": sd, "median": st.median(vals), "ci95": ci}


def paired_test(a, b):
    d = [x - y for x, y in zip(a, b)]
    n = len(d)
    sd = st.stdev(d)
    t = st.mean(d) / (sd / math.sqrt(n)) if sd > 0 else float("inf")
    return {"t": t, "df": n - 1, "p": t_two_sided_p(t, n - 1) if sd > 0 else 0.0, "dz": st.mean(d) / sd if sd > 0 else float("inf")}


def welch_test(a, b):
    va, vb = st.variance(a), st.variance(b)
    na, nb = len(a), len(b)
    se = math.sqrt(va / na + vb / nb)
    t = (st.mean(a) - st.mean(b)) / se
    df = (va / na + vb / nb) ** 2 / ((va / na) ** 2 / (na - 1) + (vb / nb) ** 2 / (nb - 1))
    sp = math.sqrt(((na - 1) * va + (nb - 1) * vb) / (na + nb - 2))
    return {"t": t, "df": df, "p": t_two_sided_p(t, df), "d": (st.mean(a) - st.mean(b)) / sp}


def load(path):
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if not r.get("participant_id") or r["participant_id"].startswith("#"):
                continue
            items = [int(r[c]) for c in SUS_COLS]
            tlx = [float(r[c]) for c in TLX_COLS]
            rows.append({
                "pid": r["participant_id"], "cond": r["condition"],
                "time": float(r["task_completion_time_s"]), "wrong": float(r["wrong_turns"]),
                "sus": sus_score(items), "tlx": tlx_raw(tlx),
            })
    return rows


def main(path):
    rows = load(path)
    pids = {r["pid"] for r in rows}
    print("participants (N):", len(pids), "  rows:", len(rows))
    if len(pids) < MIN_N:
        print("WARNING: N < %d - below the course minimum; do not report these as the study result." % MIN_N)
    by = defaultdict(list)
    for r in rows:
        by[r["cond"]].append(r)
    for cond, rs in sorted(by.items()):
        print("\nCondition:", cond)
        for label, key in (("SUS (0-100)", "sus"), ("NASA-TLX raw (0-100)", "tlx"), ("Completion time (s)", "time"), ("Wrong turns", "wrong")):
            d = describe([r[key] for r in rs])
            print("  %-22s N=%d mean=%.2f SD=%.2f median=%.2f 95%%CI=+/-%.2f" % (label, d["n"], d["mean"], d["sd"], d["median"], d["ci95"]))
    conds = sorted(by)
    if len(conds) == 2:
        a, b = by[conds[0]], by[conds[1]]
        same = sorted(r["pid"] for r in a) == sorted(r["pid"] for r in b)
        print("\nComparison %s vs %s (%s):" % (conds[0], conds[1], "paired, same participants" if same else "independent groups"))
        for label, key in (("SUS", "sus"), ("NASA-TLX", "tlx"), ("Completion time", "time"), ("Wrong turns", "wrong")):
            if same:
                av = [r[key] for r in sorted(a, key=lambda r: r["pid"])]
                bv = [r[key] for r in sorted(b, key=lambda r: r["pid"])]
                t = paired_test(av, bv)
                print("  %-16s t(%d)=%.3f p=%.4f dz=%.2f" % (label, t["df"], t["t"], t["p"], t["dz"]))
            else:
                t = welch_test([r[key] for r in a], [r[key] for r in b])
                print("  %-16s t(%.1f)=%.3f p=%.4f d=%.2f" % (label, t["df"], t["t"], t["p"], t["d"]))
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
        fig, ax = plt.subplots(figsize=(5, 4), dpi=200)
        ax.boxplot([[r["sus"] for r in by[c]] for c in conds], showmeans=True)
        ax.set_xticklabels(conds)
        ax.axhline(68, color="#c53030", linestyle="--", linewidth=1)
        ax.text(0.55, 69, "SUS 68 benchmark", fontsize=7, color="#c53030")
        ax.set_ylabel("SUS score (0-100)")
        ax.set_title("SUS per condition (N = %d)" % len(pids))
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "figures", "s021_sus_boxplot.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        fig.tight_layout()
        fig.savefig(out)
        print("\nfigure written:", os.path.normpath(out))
    except ImportError:
        print("\nmatplotlib not installed - figure skipped")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1])
