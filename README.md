# IMTAI VR 

<p align="center">
  <img src="VR Industrial Training/Assets/_Rendering/Decals/IMTAI Logo.png" alt="VR IMTAI training environment" width="200">
</p>

<p align="center">
  <strong>Virtual Reality-Based Industrial Machinery Training Using Agentic AI for Decision Support for Industry 4.0</strong>
</p>

<p align="center">
  <a href="https://unity.com/"><img alt="Unity" src="https://img.shields.io/badge/Unity-6000.4.2f1-black?logo=unity"></a>
  <a href="https://github.com/37743/IMTAI-VR/releases"><img alt="Latest Release" src="https://img.shields.io/github/v/release/37743/IMTAI-VR?include_prereleases&label=release"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Meta%20Quest-blue">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green">
</p>

VR IMTAI is a Unity-based virtual reality training system for industrial machinery operation. It combines immersive XR interaction with agentic AI, large language model reasoning, retrieval-augmented generation, speech input, voice output, safety validation, and performance evaluation.

The current training environment focuses on a bench lathe workflow, where trainees can interact with machine controls, receive adaptive guidance, and be evaluated against procedural and safety-oriented task requirements.

## Table of Contents

- [Demo](#demo)
- [Preview](#preview)
- [Overview](#overview)
- [Core Capabilities](#core-capabilities)
- [Technology Stack](#technology-stack)
- [Installation and Setup](#installation-and-setup)
- [Repository Structure](#repository-structure)
- [Academic Context](#academic-context)
- [Credits](#credits)
- [License](#license)

## Demo

Demo media will be added here after recording the headset walkthrough.

<!--
Recommended future demo options:

### Video Walkthrough

https://github.com/user-attachments/assets/your-demo-video-id

### GIF Preview

<p align="center">
  <img src="docs/images/demo.gif" alt="VR IMTAI demo walkthrough" width="900">
</p>
-->

| Demo Slot | Planned Content |
| --- | --- |
| Headset walkthrough | End-to-end trainee flow inside the VR lathe environment. |
| AI guidance interaction | Trainee question, retrieved response, highlighting, and spoken guidance. |
| Evaluation flow | Task completion, safety validation, and performance feedback. |

## Preview

Preview images will be added here as project screenshots become available.

<!--
Uncomment these once the files are added:

<p align="center">
  <img src="docs/images/lathe-training.png" alt="Interactive lathe training scene" width="900">
</p>

<p align="center">
  <img src="docs/images/ai-guidance.png" alt="Agentic AI guidance inside the VR scene" width="900">
</p>

<p align="center">
  <img src="docs/images/performance-review.png" alt="VR IMTAI performance review interface" width="900">
</p>
-->

| Preview Slot | Placeholder |
| --- | --- |
| Training environment | `docs/images/lathe-training.png` |
| AI guidance | `docs/images/ai-guidance.png` |
| Performance review | `docs/images/performance-review.png` |

## Overview

Modern Industry 4.0 environments require operators who can work with complex machinery safely, consistently, and with strong procedural awareness. Traditional training can be expensive, hazardous, difficult to personalize, and hard to evaluate objectively.

This project explores a VR training workflow where the trainee's actions are observed in real time, interpreted against machine state and task context, and used to drive grounded guidance and assessment. The system is designed around four main responsibilities:

- Perceive trainee actions through XR interactions and machine-state tracking.
- Reason over task progress, safety constraints, and industrial knowledge.
- Guide the trainee through adaptive text, speech, and interactable highlighting.
- Evaluate performance through state-aware task completion and safety checks.

## Core Capabilities

| Area | Description |
| --- | --- |
| Immersive machinery training | Interactive lathe controls, workpiece handling, snapping, spindle/feed behavior, and procedural task flow inside VR. |
| Agentic guidance | Context-aware responses generated from trainee questions, machine state, task progress, and retrieved machine knowledge. |
| Safety validation | Runtime checks for power, emergency stop, feed, spindle, threading, and machine-control constraints. |
| Speech interface | Whisper-based speech-to-text for trainee questions and Piper-based text-to-speech for spoken guidance. |
| Performance evaluation | Step tracking, action validation, scoring hooks, and persistent previous-score support for trainee review. |
| XR deployment | Meta Quest-oriented Unity project using XR, OpenXR, Meta XR SDK, and URP. |

## Technology Stack

| Layer | Tools |
| --- | --- |
| Engine | Unity 6, Universal Render Pipeline |
| XR | Meta XR SDK, Meta XR Interaction SDK, OpenXR, Unity Input System |
| AI inference | Unity Sentis |
| Retrieval and reasoning | LangChain, Kimi-K2-Instruct, FAISS |
| Speech-to-text | Whisper |
| Text-to-speech | Piper |
| Target hardware | Meta Quest 2, Quest 3, Quest 3S |

## Installation and Setup

### Requirements

- Unity `6000.4.2f1`
- Meta Quest 2, Meta Quest 3, or Meta Quest 3S
- At least 1 GB of free headset storage
- USB-C cable, preferably USB 3.0, for development builds
- SideQuest for manual APK installation
- Meta Quest mobile app with Developer Mode enabled

### Install from APK

1. Install the Meta Quest app on your phone and sign in with your Meta account.
2. Pair your Quest headset in the Meta Quest app.
3. Enable Developer Mode from `Menu -> Devices -> your Quest -> Developer Mode`.
4. Reboot the headset.
5. Connect the headset to your computer and allow USB debugging inside the headset.
6. Install and open SideQuest on your computer.
7. Confirm the SideQuest connection indicator is green and authorized.
8. Download the latest APK from the [Releases](https://github.com/37743/IMTAI-VR/releases) page.
9. In SideQuest, select `Install APK from folder`, choose the downloaded APK, and wait for installation to finish.
10. Launch the application from `Apps -> Unknown Sources` inside the headset.

### Open the Unity Project

1. Clone this repository.
2. Open Unity Hub.
3. Add the project folder: `VR Industrial Training`.
4. Open the project with Unity `6000.4.2f1`.
5. Switch the build target to Android for Quest deployment.
6. Build and run to a connected headset when developing locally.

## Repository Structure

```text
IMTAI-VR/
|-- VR Industrial Training/        # Unity project root
|   |-- Assets/                    # Scenes, scripts, models, prefabs, and runtime assets
|   |-- Packages/                  # Unity package manifest
|   `-- ProjectSettings/           # Unity project configuration
|-- t999_user_manual.txt           # Lathe reference material used by the project
|-- LICENSE
`-- README.md
```

Key script areas:

- `Assets/_Scripts/Machinery Logic` - lathe simulation, state tracking, safety rules, guidance, snapping, and evaluation logic.
- `Assets/_Scripts/STT` - Whisper loading, recording, and transcription state machine.
- `Assets/_Scripts/TTS` - Piper text-to-speech integration.
- `Assets/_Scripts/StateAsm` - reusable state-machine support.

## Academic Context

This repository supports the official graduation project submission (January 2026) for Egypt-Japan University of Science and Technology (E-JUST) and follows academic best practices for reproducibility, documentation, and extensibility.

## Credits

### Team Members and Contributions

- [@37743](https://github.com/37743) - **Yousef Ibrahim Gomaa Mahmoud** - [yousef.gomaa@ejust.edu.eg](mailto:yousef.gomaa@ejust.edu.eg)
- [@YahyaAzab](https://github.com/YahyaAzab) - **Yahya Refaat Mohamed Azab** - [yahya.azab@ejust.edu.eg](mailto:yahya.azab@ejust.edu.eg)
- **Mazen Hassan Ahmed Hassan** - [mazen.hassan@ejust.edu.eg](mailto:mazen.hassan@ejust.edu.eg)

### Supervisor

Dr. Ahmed B. Zaky - [ahmed.zaky@ejust.edu.eg](mailto:ahmed.zaky@ejust.edu.eg)

Egypt-Japan University of Science and Technology<br>
Faculty of Computer Science and Information Technology<br>
New Borg El-Arab City, 21934, Alexandria, Egypt

## License

This project is licensed under the terms of the [MIT License](LICENSE).
