# Virtual Reality–Based Industrial Machinery Training Using Agentic AI for Decision Support for Industry 4.0
Virtual Reality–Based Industrial Machinery Training Using Agentic AI (VR IMTAI) is a Unity-based VR training system integrating agentic AI decision loops with LLM reasoning and Retrieval-Augmented Generation (RAG). It performs real-time perception of user actions, adaptive guidance, safety validation, and performance evaluation via modular agents, vector search, STT/TTS, and XR interaction pipelines.

# Overview:
Modern Industry 4.0 environments demand highly skilled operators capable of handling complex machinery safely and efficiently. Traditional training methods are often costly, hazardous, and limited in adaptability. By combining VR, Large Language Models (LLMs), and RAG, the project introduces an immersive Virtual Reality (VR) training system powered by Agentic AI, capable of:
- Perceiving trainee actions in real time
- Reasoning over task context and industrial knowledge
- Providing adaptive, knowledge-grounded guidance
- Supporting objective performance evaluation

# Stack
- Unity 6
- XR Interaction Toolkit
- Universal Render Pipeline (URP)
- Meta SDK
- Unity Sentis (on-device AI inference)
- LangChain framework
- Kimi-K2-Instruct Large Language Model
- Vector databases: FAISS
- Whisper (Speech-to-Text)
- Piper (Text-to-Speech)

# Installation & Setup:
## Hardware Requirements:
- Meta Quest 2/3/3S with ≥1 GB free storage.
- A good USB-C cable (USB 3.0 preferred) or Air Link (optional). (for development build)

## Setup Method:
### APK File
- Install the Meta Quest app on your phone and log in with your Meta account.
- Pair your headset in the app.
- In the phone app: Menu → Devices → your Quest → Developer Mode → On.
- Reboot the headset, then enable "Allow USB Debugging" when connecting the cable with your preferred device of choice.
- Install SideQuest (desktop).
- Plug in the Quest, ensure the top-left dot in SideQuest is green (authorized).
- Download the latest APK release from the [Releases](https://github.com/37743/IMTAI-VR/releases) sidepanel. 
- On SideQuest, select Install APK from folder (down-arrow icon) → pick your .apk → wait for "Success".
- Now you can run the application locally through: Apps → filter Unknown Sources → launch your app.

# Academic Context:
This repository supports the official graduation project submission (January 2026) for Egypt-Japan University of Science and Technology (E-JUST) and follows academic best practices for reproducibility, documentation, and extensibility.

# Credits
## Team Members & Contributions
- [@37743](https://github.com/37743) - **Yousef Ibrahim Gomaa Mahmoud** - [yousef.gomaa@ejust.edu.eg](mailto:yousef.gomaa@ejust.edu.eg)
- [@]() - **Yahya Refaat Mohamed Azab** - [yahya.azab@ejust.edu.eg](mailto:yahya.azab@ejust.edu.eg)
- [@]() - **Mazen Hassan Ahmed Hassan** - [mazen.hassan@ejust.edu.eg](mailto:mazen.hassan@ejust.edu.eg)

## Supervisor
Dr. Ahmed B. Zaky - [ahmed.zaky@ejust.edu.eg](mailto:ahmed.zaky@ejust.edu.eg)

Egypt-Japan University of Science and Technology,
Faculty of Computer Science and Information Technology,
New Borg El-Arab City, 21934, Alexandria, Egypt
