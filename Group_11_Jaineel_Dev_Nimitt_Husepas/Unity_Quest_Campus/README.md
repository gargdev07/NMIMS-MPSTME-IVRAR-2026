# Unity_Quest_Campus - NMIMS MPSTME VR campus wayfinding (Meta Quest 2)

Standalone Unity project for the Group 11 capstone. It is kept in its own folder so it does not mix with the older WebXR scripts in `../Assets/Scripts/`.

## Open and run

| Step | Detail |
|---|---|
| Unity version | **6000.5.5f1** (see `ProjectSettings/ProjectVersion.txt`) |
| Open | Unity Hub > Add > select this folder. The first import takes several minutes. |
| Scene | `Assets/S014_MobileAR/Quest/Campus_Wayfinding_Quest.unity` (the only scene in the build list) |
| Editor play (no headset) | press Play. Walk **W A S D**, look with the right mouse button, point and click with the mouse, **Space** confirms SCAN, **K** opens the kiosk beside you |
| Quest play | build for Android, install on a Quest 2, wear the headset. Left thumbstick walks, right thumbstick snap-turns, right trigger selects, **X** opens the kiosk anywhere, **Y** resets to the gate |

## Build for the headset

1. *File > Build Profiles > Meta Quest* (Android). Scripting backend IL2CPP, ARM64, Vulkan only, ASTC textures.
2. Package id `com.nmims.mpstme.ivrar.group11.quest`, minimum Android API 32, target API 34, Quest 2 only.
3. Build, then `adb install -r <file>.apk` with the headset in developer mode. **Do not commit the APK**; it belongs in the GitHub Release `group-11-v1.0`.

## What is in here

| Folder | Content |
|---|---|
| `Assets/S014_MobileAR/Quest/` | the campus scene |
| `Assets/S014_MobileAR/CampusVR/` | gameplay scripts, materials, textures, voice clips (`Resources/Voice`), baked walk grid (`Resources/S014WalkGrid.bytes`) |
| `Assets/Editor/` | route tool (re-plans all 96 routes), self-test, Android packaging helper |
| `Assets/XR`, `Assets/Oculus`, `Assets/Resources`, `Assets/Settings`, `Assets/Plugins` | XR, Meta and render-pipeline settings needed to build |
| `Packages/`, `ProjectSettings/` | package manifest and project settings |

## Maintainer menu (Unity editor)

- **S014 > Routes > Rebuild walkable routes (apply)** recomputes the stairs and lift routes of all destinations from the scene colliders and rebakes the walk grid. Run it after any geometry change, then re-run the self-test. **S014 > Routes > Check walkability (report only)** changes nothing; **S014 > Routes > Bake runtime walk grid** rebakes only the grid.
- Automated test (Play mode): `S014.CampusVR.EditorTools.S014SelfTest.Start(stairs, lift, onlyId, timeScale)`; results are written to `Logs/S014SelfTest.txt`. See `../docs/S021_Validation_Results.md`.

## Split across branches

The team's role branches each add a part of this project: the kiosk (`feat/r057-smart-kiosk-webxr-le`), the route planner and its bake tool (`feat/j032-spatial-telemetry-an`) and the self-test (`feat/s021-sustainability-usabi`). The scene needs all of them, so **merge all four branches before opening the project**; on this branch alone Unity will report missing scripts.

## Not in the repository (on purpose)

`Library/`, `Temp/`, `Logs/`, builds and `.apk` files; third-party asset packs that the scene does not use; the editor assistant settings file that holds a personal access token.
