# S021 - Follow-up Usability Study: Results (28-Oct-2026)

**Role:** Sustainability & Usability Analyst (Nimitt Jain)  |  **Branch:** `feat/s021-sustainability-usabi`
**Data:** `telemetry/trial_data_real.csv` (10 rows, anonymous codes P01-P10)  |  **Scripts:** `telemetry/analyze_real_trials.py`, `telemetry/plot_study_results.py`

> These are **real answers from real people** who tried the Quest build on 28-Oct-2026. They are **not** the simulated starter numbers. Read the *Deviations and limits* section before quoting any figure: the sample includes the four team members and the design is weaker than the protocol in `docs/S021_Usability_Study_Protocol.md`.

---

## 1. What was done

- **Participants: N = 10** adults who tried the build on a Meta Quest 2: **6 people outside the project team and 4 team members** (the column `team_member` marks this; the data are reported for all 10 and for the 6 separately).
- Each person did **one session** and chose their own way to the room: stairs (2 people), lift (4) or both (4). Everyone found the room (10 of 10).
- Afterwards each person answered an online form: consent tick-box, the 10 SUS statements (1-5) and the six NASA-TLX ratings (0-100). Scoring: `SUS = 2.5 x (sum(odd - 1) + sum(5 - even))`; raw TLX = mean of the six ratings.
- Names were collected by the form; they were replaced by codes P01-P10 **before** the data were placed in this repository. The original export (with names) is kept offline and is not committed.

## 2. Results

### 2.1 System Usability Scale (0-100)

| Group | N | Mean | SD | Median | 95 % CI of the mean | Min-max |
|---|---|---|---|---|---|---|
| All participants | 10 | **90.50** | 8.64 | 91.25 | 84.3 to 96.7 | 75.0-100.0 |
| Not on the team | 6 | 88.33 | 9.96 | 86.25 | 77.9 to 98.8 | - |
| Team members | 4 | 93.75 | 5.95 | 96.25 | - | - |

All 10 individual scores are above the commonly used benchmark of 68, and the lower end of the confidence interval (84.3 for all, 77.9 for the six outsiders) is above 68, so hypothesis H1 of the protocol ("at least acceptably usable") is supported **for this sample**. Individual scores: 97.5, 95.0, 97.5, 85.0, 100.0, 82.5, 75.0, 100.0, 87.5, 85.0.

![SUS per group](figures/s021_sus_boxplot.png)

### 2.2 NASA-TLX (0-100, lower is better)

| Measure | N | Mean | SD | Median |
|---|---|---|---|---|
| Raw TLX (mean of six) | 10 | 10.75 | 6.36 | 10.83 |

Sub-scale means: mental 14.5, physical 10.0, temporal 11.0, performance 8.5 (0 = perfect), effort 16.5, frustration 4.0. Workload was rated low on every sub-scale (hypothesis H2: low-to-moderate, supported for this sample).

![TLX sub-scales](figures/s021_tlx_subscales.png)

### 2.3 Other measures

| Measure | Result |
|---|---|
| Task success ("did you find the room") | 10 of 10 yes |
| Dizziness or discomfort | 8 "no", 2 "a little", 0 "quite a lot" |
| Completion time, wrong turns | **not measured** (see limits) |
| Free comments (2) | add opening and closing doors to the lift; add more campus detail, for example a real library entrance |

## 3. Deviations and limits (state these in the dossier, video and paper)

1. **Four of the ten are team members.** They know the system, so their scores are probably more favourable (their mean is 93.75 against 88.33 for outsiders). The protocol asked for people outside the group. Report N = 10 *and* the n = 6 subgroup.
2. **No controlled design.** One session per person, free choice of route, no counter-balancing, destinations not recorded. The planned comparisons LIFT vs STAIRS (H3, H4) **cannot be tested** with these data (2 / 4 / 4 people per route).
3. **Retrospective answers.** The questionnaires were completed after the session, so they rely on memory of the same day.
4. **Consent was a tick-box in the online form, collected after testing**, not a signed paper form. The form's footage-permission questions were left unanswered, so **no participant footage (face or headset view) should be used publicly** unless permission is obtained first.
5. **No timing data.** Completion time and wrong turns were not recorded reliably (the recordings are incomplete), so no transit-time or error claim is made.
6. **Small, friendly sample from one campus.** The very high scores may reflect politeness or novelty. Do not generalise to first-time visitors.

## 4. Reproduce

```bash
py telemetry/analyze_real_trials.py telemetry/trial_data_real.csv                      # all 10
py telemetry/analyze_real_trials.py telemetry/trial_data_real.csv --filter=team_member=no   # the 6 outsiders
py telemetry/plot_study_results.py                                                    # both figures
```

The SUS scoring was also checked by hand with plain arithmetic (for example P01: 19 + 20 = 39, x 2.5 = 97.5), and the mean of 90.5 was reproduced independently.
