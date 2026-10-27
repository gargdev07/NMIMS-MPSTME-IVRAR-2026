# S021 - Validation Results (automated tests of the Quest build)

**Role:** Sustainability & Usability Analyst (Nimitt Jain)  |  **Branch:** `feat/s021-sustainability-usabi`
**Run date:** 28-Oct-2026, Unity 6000.5.5f1 editor, Play mode, standalone XR initialisation off (flat Game view), `Time.timeScale = 6`.

> These are **software test results**: a scripted visitor walks the real guide line with the same `CharacterController` the player uses. They show that every route can be completed in the model. They are **not** usability results and contain no participant data.

## 1. What the test does (`Assets/Editor/S014SelfTest.cs`)

For each of the 96 destinations and each mode (STAIRS, LIFT) the test: opens the kiosk flow, selects the room and the mode, presses SCAN, checks the QR panel hides and the guide starts with the right destination, checks the line and the arrow are visible and start near the player, then **walks the displayed line** with the character controller (stepping into the lift and waiting for the automatic ride in LIFT mode), and finally checks that arrival fires, the kiosk is offered again beside the player on the right floor, and the player stands within 3 m of the room gate.

## 2. Run A - every destination, both modes, starting at the main gate

| Result | Count |
|---|---|
| PASS | 180 |
| FAIL | 12 |
| of which: ground-floor room in LIFT mode, see note | 12 |
| any other failure | 0 |

**Note on the ground-floor cases.** The kiosk was changed so that a lift is used only when the visitor and the room are on different floors ("a visitor standing on the ground floor does not ride a lift to a ground-floor room"). The test, written before that change, still expected lift guidance for those 12 ground-floor rooms, so it stopped at that check ("lift mode did not guide to the lift"). This is an out-of-date expectation in the test, not a route failure. The expectation was corrected and the 12 rooms were re-run, see Run B.

## 3. Run B - the 12 ground-floor rooms in LIFT mode after the test fix

| Result | Count |
|---|---|
| PASS | 12 |
| FAIL | 0 |

**Combined result for the main-gate start:** 180 passes in Run A plus 12 passes in Run B = **192 of 192** destination/mode combinations completed.

## 4. Run C - start from a random walkable spot on any floor (continue-from-position)

The visitor is placed at a random reachable point (any level, stair flights included), the kiosk is summoned there, and the route is planned from that spot.

| Result | Count |
|---|---|
| PASS | 17 |
| FAIL | 1 |

Scope: the rooms matching the prefix used for this run only (not all 192 combinations).

```
FAIL  STAIRS 7  f7-e.-cc-701  from (-18, 0, 6)   stuck while following the line at (-17.8, 0.2, 7.9) (229.7 m left) heading to corner 1/87 (-17.8, 0.1, 14.8)   player=(-17.8, 0.2, 7.9)
```

**Cause of the one Run C failure (investigated, not fixed).** The visitor started on the ground floor at (-18, 0, 6) and the kiosk was summoned 2 m ahead, at (-18, 0, 8). The kiosk body is 1.9 m wide and solid, and the planned line to the room ran straight through it, so the scripted walker (which cannot step sideways) stopped at the kiosk. A probe of the scene confirmed there is no other obstacle on that line, and the grid marks the cells as walkable. A real visitor can simply walk around the kiosk, but the line is drawn through it. This is a **known limitation**; a possible fix is to start the route from a point beside the kiosk or to hide the kiosk once a route starts. It was not changed here because the kiosk behaviour had already been accepted on the headset.

## 5. Fall test

```
DONE samples=16755 trajectories=67020 falls=0 blocks=0
```

## 6. Reproduce

```text
Unity editor, Play mode:
  S014.CampusVR.EditorTools.S014SelfTest.Start(stairs: true, lift: true, onlyId: null, timeScale: 6f)   // Run A
  S014.CampusVR.EditorTools.S014SelfTest.Start(false, true, "fg-*", 6f)                                  // Run B (prefix filter)
  S014.CampusVR.EditorTools.S014SelfTest.StartAnywhere(true, true, "f7-*", 6f)                           // Run C
  S014.CampusVR.EditorTools.S014SelfTest.RunFall()                                                       // fall test
Report files: Logs/S014SelfTest.txt, Logs/S014FallTest.txt
```

## 7. What this does **not** show

- It does not show that real people find the interface usable (see `docs/S021_Usability_Study_Protocol.md`).
- It does not measure frame rate or comfort on the headset.
- The random-start run covers a subset of rooms, not every start/room combination.
