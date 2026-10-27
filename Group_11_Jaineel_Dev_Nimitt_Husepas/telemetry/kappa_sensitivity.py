"""
One-at-a-time sensitivity of the cost parity ratio kappa (S021 - Nimitt Jain).

The base model in kiosk_analytics_roi_eval.py uses ASSUMED normalised cost shares (not measured values).
This script moves each share by -50 % and +50 % and reports how far kappa and the payback horizon move,
so a reader can see which assumption matters. No currency is used: every term is a dimensionless share.

    kappa   = (c_clean + c_cloud + c_bim) / (c_paper + c_labour + c_waste)
    payback = 12 * capex / (OpEx_traditional - OpEx_kiosk)        [operating months]

Run:  python telemetry/kappa_sensitivity.py
"""
BASE = {
    "paper printing share": 0.42, "reception labour share": 0.46, "waste handling share": 0.12,
    "screen cleaning + power share": 0.08, "cloud hosting share": 0.09, "map maintenance share": 0.05,
}
TRAD = ["paper printing share", "reception labour share", "waste handling share"]
CAPEX = 1.0  # normalised initial investment (assumed)


def evaluate(p):
    trad = sum(p[k] for k in TRAD)
    kiosk = sum(v for k, v in p.items() if k not in TRAD)
    kappa = kiosk / trad
    saving = trad - kiosk
    return kappa, (12.0 * CAPEX / saving if saving > 0 else float("inf"))


def main():
    k0, m0 = evaluate(BASE)
    print("base: kappa = %.4f, payback = %.2f months (assumed inputs)\n" % (k0, m0))
    print("%-32s %9s %9s %10s %10s" % ("input moved", "kappa -50%", "kappa +50%", "payback -50%", "payback +50%"))
    rows = []
    for name in BASE:
        lo = dict(BASE); lo[name] *= 0.5
        hi = dict(BASE); hi[name] *= 1.5
        (klo, mlo), (khi, mhi) = evaluate(lo), evaluate(hi)
        rows.append((abs(khi - klo), name, klo, khi, mlo, mhi))
        print("%-32s %9.4f %9.4f %12.2f %12.2f" % (name, klo, khi, mlo, mhi))
    rows.sort(reverse=True)
    print("\nlargest effect on kappa:", rows[0][1])
    allhi = {k: v * (1.5 if k not in TRAD else 0.5) for k, v in BASE.items()}
    alllo = {k: v * (0.5 if k not in TRAD else 1.5) for k, v in BASE.items()}
    print("pessimistic corner (kiosk costs +50 %%, traditional costs -50 %%): kappa = %.4f, payback = %.2f months" % evaluate(allhi))
    print("optimistic corner  (kiosk costs -50 %%, traditional costs +50 %%): kappa = %.4f, payback = %.2f months" % evaluate(alllo))


if __name__ == "__main__":
    main()
