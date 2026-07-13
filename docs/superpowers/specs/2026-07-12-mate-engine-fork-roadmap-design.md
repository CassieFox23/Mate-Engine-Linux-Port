# Mate-Engine Fork — Roadmap Design

**Date:** 2026-07-12
**Owner:** Cassie (CassieFox23)
**Repo:** CassieFox23/Mate-Engine-Linux-Port (own-direction fork)
**Upstream:** Marksonthegamer/Mate-Engine-Linux-Port (Linux port of shinyflvre's MateEngine)

## What this is

A vision/roadmap spec, not a single implementation spec. The fork pursues four
goals with a natural dependency order. Each phase below gets its own
spec → plan → build cycle when it is started. This document records the
decomposition, the sequencing decision, and the key technical facts that shaped
them.

## Context / environment

- Machine: Cassie-Pc, Nobara Linux (`ID_LIKE` includes `fedora`), KDE **Wayland**.
- Local copy: `~/Projects/Mate-Engine-Linux-Port`. Remotes: `origin` (SSH, own
  repo), `upstream` (fetch-only, push disabled).
- The repo is **source-only** (Unity project). No prebuilt binary is tracked.
  A runnable binary comes from either the upstream release tarball (Phase 0) or
  a local Unity build (Phase 1).
- Latest upstream release: `Public-Release-X3.2.0_5` (2026-05-05), one asset
  `MateEngineX_3.2.0_5.tar.gz` (284 MB). Repo source (2026-07-10) is newer.
- Build requires Unity Editor `6000.2.6f2` (Unity 6.2) via Unity Hub — not
  currently installed.

## Key technical findings

- **launch.sh KDE-Wayland gap.** launch.sh only special-cases Hyprland for the
  X11 transparency exports (`XDG_BACKEND=x11`, `SDL_VIDEODRIVER=x11`). A KDE
  Wayland session falls through to "Unknown windowmanager" and never sets them,
  risking a broken / black transparent window under XWayland. This is the
  primary Phase 0 fix.
- **kdotool bundled.** `Plugins/kdotool-main` (a KWin/KDE Wayland window tool)
  is already in the port — the author reached for KDE-Wayland window control.
  Worth reading how it is invoked for window pin/position.
- **Two LLM brain paths ship in the tree:**
  - `Assets/LLMUnity/` — undreamai / llama.cpp **in-process** inference
    (`StreamingAssets/undreamai-v1.2.5-llamacpp`). Runs a local gguf. llama.cpp
    is an engine, not a company — a Qwen gguf here is **cave-legal**.
  - `Assets/ollama-unity/` — HTTP client to an **Ollama** server. Ollama is on
    Cassie's model deny list. A `LLAMA 3.2 COMMUNITY LICENSE` in StreamingAssets
    implies a Meta-Llama gguf may also ship — also deny-listed.
- **Avatars load at runtime.** `Plugins/StandaloneFileBrowser` + README "custom
  VRM support" = drop-in `.vrm` at runtime, no Unity build required.

## Phases

### Phase 0 — Run on KDE Wayland (portable, no build)

- Download upstream release `X3.2.0_5` tarball → extract to a **gitignored**
  `Payload/` in the repo (install.sh already references `./Payload`; gitignore
  keeps the 284 MB blob out of the source repo).
- Install runtime deps from install.sh's Fedora branch:
  `dnf install pulseaudio-libs gtk3 glib2 libX11 libXext libXrender libXrandr
  libXdamage libXcursor libXcomposite libayatana-appindicator-gtk3`.
- Patch launch.sh: add a KDE-Wayland branch mirroring the Hyprland exports.
  Patch the **tracked** repo launch.sh (source of truth) **and** copy it into
  the extracted Payload to test the running build.
- Investigate `Plugins/kdotool-main` usage for window pin/position on KWin.
- **Done when:** transparent floating VRM shows on the KDE Wayland desktop, no
  black box, drag/interactions work, survives relaunch.

### Phase A — Custom avatars (no build)

- Runtime `.vrm` import via the in-app file browser (StandaloneFileBrowser).
  Load a custom model; confirm it persists across relaunch.
- **Done when:** Cassie's own avatar loads on launch.

### Phase 1 — Source-build toolchain

- Install Unity Hub + Editor `6000.2.6f2`; open the project; resolve packages;
  `build.sh <output>` → own `MateEngineX.x86_64` (invokes `CliBuilder.Build`).
- **Done when:** a locally built binary runs and matches release behavior.

### Phase 2 — Brain → local LLM gate (own deep brainstorm before building)

- Disable / remove the `ollama-unity` path and any Meta-Llama gguf (cave-law
  deny). Keep the llama.cpp path optional/legal only if it runs a Qwen/DeepSeek
  gguf.
- Add an OpenAI-compatible chat client into the ChatBot flow, pointed at
  Cassie's local gate `:4000` (Qwen3.5 roster; OpenAI-compatible wire format is
  cave-legal).
- Many unknowns (how `ChatBot.cs` invokes the LLM, streaming, prompt template,
  persona). **Re-brainstorm this phase before implementing.**
- **Done when:** the pet chats through the local gate roster.

### Cross-cutting

- **Learn the Unity codebase** happens naturally through Phases 1 and 2.
- gitignore the Payload/run dir.
- Keep `LICENSE` + `NOTICE.txt` (MateEngine Pro License v2.0 is copyleft).
- Sync upstream when wanted: `git fetch upstream && git merge upstream/main`
  (or cherry-pick specific commits). `upstream` push is disabled.

## Sequencing decision

Order: **Phase 0 → Phase A → Phase 1 → Phase 2**, learning throughout.
Rationale: front-load the no-build quick wins (confirm the hardware runs it and
get a custom avatar on screen) before committing to the multi-GB Unity toolchain
and the heaviest goal (LLM wiring).

Install style: **portable, run-in-place** from the repo tree — no `/opt` system
install — because the fork is a tinkering target that should be easy to nuke and
rebuild.

## Next step

Hand **Phase 0** to the writing-plans skill for a detailed implementation plan.
