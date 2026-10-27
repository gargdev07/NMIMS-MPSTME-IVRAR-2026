# Toolchain & Hardware Specification
**Group ID:** IVRAR_GROUP_11  
**Project:** How can a hybrid smart AR kiosk and mobile handoff system reduce transit time and paper map waste for campus visitors navigating complex university facilities?  

## Recommended Software Stack
* **Game Engine:** Unity 2022.3 LTS (Long Term Support)
* **XR Plugin Architecture:** OpenXR Plugin (>= 1.8.0)
* **Toolkit:** XR Interaction Toolkit (XRI >= 2.5.2 or 3.0.0)
* **Graphics Pipeline:** Universal Render Pipeline (URP - Mobile/Standalone Profile)
* **Code Editor:** Visual Studio 2022 / VS Code with C# Dev Kit

## Target Deployment Platforms
* Meta Quest 2 / Quest 3 / Quest Pro (via Android OpenXR)
* Standalone PC VR (OpenXR Link / AirLink)
* WebXR (via WebGL 2.0 / Babylon.js / Three.js where applicable)

## Telemetry & Data Collection Protocol
* Sampling Rate: >= 60 Hz head/hand transform recording
* Output Format: Structured CSV (`/telemetry/trial_<id>.csv`)
* Metrics: Completion Time (s), Path Trajectory Deviation (m), Error Rates, Collision Events

## As-built stack of the Quest build (`Unity_Quest_Campus/`)

The recommended stack above was the starting plan. The project as built uses:

* **Game Engine:** Unity 6000.5.5f1
* **XR:** OpenXR 1.18 with Meta XR Core SDK 207.0.1 and Meta OpenXR 2.6.1; Input System 1.19
* **Graphics:** Universal Render Pipeline 17.5 (mobile profile), Vulkan only, ASTC textures
* **Target device:** Meta Quest 2 (Android API 32 minimum, 34 target, IL2CPP, ARM64, GameActivity)
* **Interaction:** controller ray and trigger, thumbstick locomotion; no XR Interaction Toolkit dependency (own `S014CampusPointer` and `S014VRInput`)
* **Telemetry in the Quest build:** route start and lift events written to the device log (`adb logcat -s Unity`); the study measures (time, wrong turns, SUS, NASA-TLX) are recorded by an observer, see `docs/S021_Usability_Study_Protocol.md`. The 60 Hz transform recording listed above is **not** implemented in the Quest build.
