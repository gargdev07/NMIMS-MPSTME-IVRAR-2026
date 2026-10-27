# S021 - Sustainability & Technoeconomic Model (Nimitt Jain)

**Role:** Sustainability & Usability Analyst  |  **Branch:** `feat/s021-sustainability-usabi`
**Manuscript section:** V (Sustainability & Technoeconomics). Files: `telemetry/kiosk_analytics_roi_eval.py` (base model), `telemetry/kappa_sensitivity.py` (this document's analysis).

> **Read this first.** The base model is a **scenario model with assumed inputs**. Its headline outputs (paper sheets saved, staff hours reclaimed, kappa = 0.22, payback 15.38 months) follow arithmetically from the assumptions in the table below. They are **not measurements** and must be described as "scenario values under stated assumptions", never as results from visitors or from a deployment. No currency is used anywhere (dimensionless shares, hours, sheets and months only).

## 1. Model

```
kappa     = OpEx_kiosk / OpEx_traditional
          = (c_clean + c_cloud + c_map) / (c_paper + c_labour + c_waste)
payback   = 12 * CapEx / (OpEx_traditional - OpEx_kiosk)        [operating months, CapEx normalised to 1.0]
```

| Quantity | Formula in the code | Assumed input |
|---|---|---|
| Paper sheets per year | visitors x maps per visitor | 8,500 x 1.2 = **10,200** |
| Reception guidance hours per year | staff x hours per day x working days | 6 x 5.0 x 250 = **7,500** |
| Residual hours with the kiosk | 20 % of the above | 1,500, so **6,000 reclaimed** |
| Visitor transit hours lost | visitors x 0.22 h | 1,870 (35 % remains with the kiosk) |
| Traditional shares | paper / labour / waste | 0.42 / 0.46 / 0.12 (sum 1.00) |
| Kiosk shares | cleaning+power / cloud / map upkeep | 0.08 / 0.09 / 0.05 (sum 0.22) |

Worked check: `kappa = 0.22 / 1.00 = 0.22`; savings `1.00 - 0.22 = 0.78`; `payback = 12 x 1.0 / 0.78 = 15.38` months. Every input in the table above is an assumption of the model; none has a source.

## 2. Sensitivity: how fragile are those outputs?

Each cost share moved by -50 % and +50 % in turn (`py telemetry/kappa_sensitivity.py`):

```
base: kappa = 0.2200, payback = 15.38 months (assumed inputs)

input moved                      kappa -50% kappa +50% payback -50% payback +50%
paper printing share                0.2785    0.1818        21.05        12.12
reception labour share              0.2857    0.1789        21.82        11.88
waste handling share                0.2340    0.2075        16.67        14.29
screen cleaning + power share       0.1800    0.2600        14.63        16.22
cloud hosting share                 0.1750    0.2650        14.55        16.33
map maintenance share               0.1950    0.2450        14.91        15.89

largest effect on kappa: reception labour share
pessimistic corner (kiosk costs +50 %, traditional costs -50 %): kappa = 0.6600, payback = 70.59 months
optimistic corner  (kiosk costs -50 %, traditional costs +50 %): kappa = 0.0733, payback = 8.63 months
```

What this shows:

- kappa is **not** a single number: moving the shares by half gives anything from about 0.07 to 0.66, and the payback from about 9 to 71 operating months.
- The result is most sensitive to the **reception-labour share** and the **paper-printing share**, i.e. to quantities the team has not measured.
- Therefore the dossier and the video should say "under the stated assumptions kappa = 0.22; sensitivity range 0.07-0.66", and should not present 0.22 or 15.38 months as an outcome.

## 3. What would make it a real result

1. Obtain the real annual map print run and the reception inquiry log from the facilities office (with a source and date) and replace the shares.
2. Measure the share of reception enquiries that are wayfinding (a one-week tally).
3. Re-run `kiosk_analytics_roi_eval.py` and `kappa_sensitivity.py` with the measured values and cite them.

## 4. Sustainability notes that do not depend on the assumptions

- The Quest build contains **no printed material**; the route is delivered as an on-screen line, an arrow and a voice prompt.
- The qualitative argument (digital route instead of a disposable paper map) stands; its size does not, until step 1 above is done.
