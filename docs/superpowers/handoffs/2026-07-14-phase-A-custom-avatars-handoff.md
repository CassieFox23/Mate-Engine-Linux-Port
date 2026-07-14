# Handoff — Start Phase A (Custom Avatars)

Paste the block below into a fresh Claude Code session started in
`~/Projects/Mate-Engine-Linux-Port` to kick off Phase A.

---

Start Phase A of my Mate-Engine fork — **custom avatars (no Unity build)**.

Repo: ~/Projects/Mate-Engine-Linux-Port (start the session here).
Box: Nobara Linux, KDE Wayland, AMD RX 6700 XT.

Phase 0 is DONE and shipped — the pet already runs transparent on KDE Wayland
via `./run-local.sh` (prebuilt binary in gitignored `Payload/`). Phase A runs
against that SAME prebuilt binary — do NOT install Unity or build anything.

Context to read first (do not re-plan Phase 0):
  - Roadmap:   docs/superpowers/specs/2026-07-12-mate-engine-fork-roadmap-design.md
    (see "Phase A — Custom avatars", marked ⏭ NEXT)

Goal: load one of my own `.vrm` avatars into the running pet and confirm it
persists across a relaunch (`./run-local.sh` → close → `./run-local.sh`).

This is creative/generative feature work with real unknowns, so **brainstorm
before planning**:
  1. Use superpowers:brainstorming FIRST. Answer these open sub-questions by
     reading the source + running the app (don't guess):
       - Where is the in-app "load / import avatar" control? (README mentions
         "custom VRM support" via `Plugins/StandaloneFileBrowser`.)
       - Where do imported VRMs get stored, and how is the "current avatar"
         persisted across relaunch? (Check SaveLoadHandler / the save data —
         same handler Phase 0's autostart edit touched.)
       - Does a source `.vrm` need any conversion, or is it drop-in at runtime?
       - Where do I get / author an avatar `.vrm`? (I may supply one.)
  2. Then superpowers:writing-plans for the task-by-task plan.
  3. Then hand it back to me — some steps (picking/loading MY avatar, eyeballing
     it on the desktop) are hands-on like Phase 0's Task 5 was.

Constraints (carry over from the roadmap Global Constraints):
  - Portable run-in-place only. NO /opt, NO install.sh, NO sudo, NO Unity build.
  - Keep the 284 MB binary + any large `.vrm` assets OUT of git (extend
    .gitignore if I drop avatar files into the repo tree).
  - Keep LICENSE + NOTICE.txt intact (copyleft).
  - Repo pushes over SSH (origin is git@github.com:...). Commit patched/tracked
    source; never commit Payload/ or big binaries.

When Phase A is done (or stopped for me), log it to the crew ledger per
~/.claude/crew/README.md (ledger line always; job file if it surfaces detail
worth keeping, e.g. how VRM persistence is keyed).

---

## Why these sub-questions (recon crumbs from Phase 0)

- `SaveLoadHandler` is the app's save system — Phase 0's autostart edit
  (`Assets/MATE ENGINE - Scripts/Tools/SystemStartHandler.cs`) read/wrote
  `SaveLoadHandler.Instance.data`. Avatar persistence very likely lives in the
  same `data` object — start there.
- Avatar loading is a **runtime** path (`Plugins/StandaloneFileBrowser` + VRM
  importer under `Assets/MATE ENGINE - Packages/VRM10/`), which is exactly why
  Phase A needs no build.
- The running binary is upstream's prebuilt release, so any *source* change to
  the load flow won't take effect until Phase 1's rebuild — Phase A should aim
  to work through the **existing** in-app importer, not by editing source.
