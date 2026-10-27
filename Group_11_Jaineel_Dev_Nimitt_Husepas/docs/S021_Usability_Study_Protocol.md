# S021 - Usability Study Protocol (Quest campus wayfinding)

**Role:** Sustainability & Usability Analyst (Nimitt Jain)  |  **Branch:** `feat/s021-sustainability-usabi`
**Purpose:** a ready-to-run protocol for the human-in-the-loop evaluation required by the course (dossier Section 4: real volunteers, **N >= 10**, SUS plus NASA-TLX, consent forms, anonymised IDs).

> **Status: protocol only.** No participant has been tested yet, so this repository contains **no study results**. The numbers in the starter files (`SIMULATED_*.csv`, `docs/figures/figure*.png`) are simulated and must not be reported as results.

---

## 1. Question and hypotheses

Authorised question: *How can a hybrid smart AR kiosk and mobile handoff system reduce transit time and paper map waste for campus visitors navigating complex university facilities?* In the Quest build the visitor-facing system is the kiosk plus the guided route. The study asks whether first-time users find it usable and how the two route modes compare.

| # | Hypothesis | Measure | Decision rule |
|---|---|---|---|
| H1 | The system is at least *acceptably* usable | mean SUS | 95 % confidence interval lower bound above the 68 benchmark |
| H2 | Perceived workload is moderate or lower | raw NASA-TLX | report mean, SD, median (no fixed threshold) |
| H3 | LIFT and STAIRS differ in completion time | completion time (s) | paired t-test, p < 0.05, report Cohen's dz |
| H4 | LIFT and STAIRS differ in wrong turns | count | paired t-test, p < 0.05 |

A paper-map baseline cannot be tested inside a VR build, so no claim of "faster than paper" is made from this study.

## 2. Design

- **Within-subject, two conditions:** `STAIRS` and `LIFT`, each participant does both.
- **Counter-balanced order:** odd-numbered participants do STAIRS first, even-numbered LIFT first (column `order`).
- **Matched tasks (same level, near-equal model route length):**

| Task | Destination (`destination_id`) | Level | Stairs route | Lift route |
|---|---|---|---|---|
| Task A | `f6-ai-lab` (AI Lab) | 6 | 173.9 m | 57.7 m |
| Task B | `f6-advance-communication-lab` (Advance Communication Lab) | 6 | 175.8 m | 59.6 m |

(Lengths from `telemetry/MODEL_route_metrics.csv`; they differ by 1.9 m. Alternate which task goes with which mode so the room is not confounded with the mode.)

## 3. Participants

- **N >= 10**, target 12-20. Adult (18+) volunteers, for example students outside the project team.
- Not part of the project group, no prior use of this build.
- Exclusion: history of epilepsy, severe motion sickness or photosensitivity; anyone who does not want to continue. **Non-clinical volunteers only.** Any participant may stop at any time without giving a reason.
- Written consent (section 9) before anything starts. Keep the signed forms offline; **never commit them**.

## 4. Equipment

Meta Quest 2 with the build from the project release (`group-11-v1.0`, tag pending), cleared 3 x 3 m play area, an observer with a stopwatch or a screen recorder (cast the headset view; **do not record faces**), printed SUS and NASA-TLX sheets or a form.

## 5. Procedure (about 15 minutes per person)

| Min | Step |
|---|---|
| 0-2 | Welcome, explain the study, consent, assign ID `P01`, `P02`... |
| 2-4 | Headset fitted. Free practice in the lobby: point, trigger, snap-turn, X to open the kiosk. No route yet. |
| 4-7 | **Task 1** (first mode): "Find *Task room* using the kiosk." Observer starts the clock when the visitor presses SCAN, stops it when the arrival banner appears. |
| 7-9 | SUS (10 items) and NASA-TLX (6 ratings) for this mode. |
| 9-12 | **Task 2** (other mode, other room), same timing. |
| 12-14 | SUS and NASA-TLX again; optional simulator-sickness questionnaire; one open comment. |
| 14-15 | Debrief, thank you. |

Stop immediately if the participant feels unwell.

## 6. Measures and how they are recorded

| Variable | Column | Definition |
|---|---|---|
| Completion time | `task_completion_time_s` | SCAN pressed to arrival banner, seconds |
| Wrong turns | `wrong_turns` | observer counts each time the visitor leaves the route by more than about 4 m and the line re-routes |
| SUS | `sus_q1`..`sus_q10` | 1-5 agreement per item (standard SUS wording). Score = 2.5 x (sum of (odd - 1) + sum of (5 - even)) |
| NASA-TLX | `tlx_mental` ... `tlx_frustration` | six ratings, 0-100; raw TLX is the mean |
| Simulator sickness | `ssq_total` | optional total from the questionnaire |

Honest note on the build: it logs route start and lift events to the device log, but it does **not** record per-frame position, wrong turns or arrival time. These come from the observer, which is why the columns above are filled by hand.

## 7. Data handling

- Anonymous IDs only (`P01`...). No names, SAP IDs, phone numbers, e-mail addresses or faces in any file.
- One row per participant per condition in `telemetry/` (copy `TRIAL_DATA_TEMPLATE.csv`, name it for example `trial_data_real.csv`).
- Delete or keep clearly named (`SIMULATED_*`) any generated file. Never mix generated and real rows.

## 8. Analysis

```bash
py telemetry/analyze_real_trials.py telemetry/trial_data_real.csv
```

The script scores SUS and raw TLX, prints N, mean, SD, median and 95 % CI per condition, runs a paired t-test (or Welch if participants differ) with Cohen's d, warns if N < 10, and writes `docs/figures/s021_sus_boxplot.png`. Its Student-t routines were checked against reference values (critical t at df = 5, 10, 29; p-values for known t) and its SUS scoring against the known boundary cases (all-best = 100, neutral = 50, all-worst = 0).

Report: N, mean, SD, median, the box-plot, and the limitations below. Re-plot every dossier figure from your own CSV.

## 9. Consent form (template)

> **Participation consent - IVRAR Group 11 campus wayfinding study.**
> You are invited to try a virtual-reality campus guide and answer two short questionnaires (about 15 minutes). Participation is voluntary and you may stop at any time without giving a reason. You must be 18 or over. Some people feel mild dizziness in VR; tell the observer straight away if you do. We record your answers, the time you take and the number of wrong turns, under an anonymous code. Your name is not stored with your data and your face is not recorded. The anonymised data may be used in a university project report.
> Name: ____________  Signature: ____________  Date: ________  Participant code (filled by the team): ______

## 10. Limitations to state in the dossier and the paper

- Volunteer sample from one campus; small N.
- The route is a VR model of the building, not the physical building; results describe the interface, not real-world walking.
- No baseline condition (printed map) is available inside the build.
- Wrong turns and times are observer-recorded.
