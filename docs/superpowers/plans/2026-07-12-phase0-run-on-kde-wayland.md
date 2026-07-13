# Phase 0 — Run on KDE Wayland (Portable) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Get the upstream MateEngine release running as a transparent floating VRM pet on Cassie's Nobara KDE **Wayland** desktop, portable (run-in-place, no system install), with the launcher's Wayland gap fixed in the tracked repo.

**Architecture:** The git repo is Unity source only — no binary is built here in Phase 0. Instead, extract the upstream prebuilt release (`Public-Release-X3.2.0_5`, 284 MB) into a gitignored `Payload/` and run it with the repo's own `launch.sh` (patched for Wayland) copied in beside the binary. A tracked `run-local.sh` automates extract + run. The one real code change is a KDE/Wayland branch in `launch.sh` that forces XWayland (`XDG_BACKEND=x11`, `SDL_VIDEODRIVER=x11`) for the transparent ARGB window — today only Hyprland gets this, and a KDE Wayland session falls through to a no-op.

**Tech Stack:** Bash, tar, gh CLI, dnf (Nobara/Fedora deps), Unity player runtime (prebuilt), KWin/XWayland.

## Global Constraints

- Target platform: Nobara Linux (fedora-family), KDE **Wayland** session.
- Install style: **portable, run-in-place** from the repo tree — NO `/opt`, NO `install.sh`, NO sudo symlinks.
- The 284 MB release binary must stay OUT of the git repo — it lives in gitignored `Payload/` (already ignored via `/Payload/` in `.gitignore`).
- Keep `LICENSE` + `NOTICE.txt` intact (MateEngine Pro License v2.0 is copyleft).
- Patch the **tracked** `launch.sh` (source of truth) — never edit only the extracted copy.
- Upstream release tag: `Public-Release-X3.2.0_5`, asset `MateEngineX_3.2.0_5.tar.gz`. Tarball layout: `MateEngineX/Payload/{MateEngineX.x86_64, UnityPlayer.so, MateEngineX_Data/, ...}`.

---

### Task 1: Patch `launch.sh` for Wayland (KDE/KWin)

**Files:**
- Modify: `launch.sh` (the window-manager detection block)

**Interfaces:**
- Consumes: nothing.
- Produces: a `launch.sh` that exports `XDG_BACKEND=x11` and `SDL_VIDEODRIVER=x11` on ANY Wayland session (not just Hyprland), leaving X11 sessions unchanged.

- [ ] **Step 1: Read the current detection block**

Run: `sed -n '1,14p' launch.sh`
Expected: shows the `if [[ $XDG_SESSION_DESKTOP == "Hyprland"* ]]` block whose `else` branch only prints `Unknown windowmanager`.

- [ ] **Step 2: Replace the detection block**

Replace this exact block in `launch.sh`:

```bash
if [[ $XDG_SESSION_DESKTOP == "Hyprland"* ]]; then
  echo "Hyprland detected"
  # hyprland seems to need these variables as well to create a transparent xwayland window
  export XDG_BACKEND=x11
  export SDL_VIDEODRIVER=x11 
else
  echo "Unknown windowmanager"
fi
```

with:

```bash
if [[ $XDG_SESSION_DESKTOP == "Hyprland"* ]]; then
  echo "Hyprland detected"
  # hyprland seems to need these variables as well to create a transparent xwayland window
  export XDG_BACKEND=x11
  export SDL_VIDEODRIVER=x11
elif [[ $XDG_SESSION_TYPE == "wayland" ]]; then
  # KDE/KWin (and other Wayland compositors) need the same XWayland forcing as
  # Hyprland to get a transparent ARGB window. Without this, a KDE Wayland
  # session fell through to a no-op and the pet window broke / went black.
  echo "Wayland session (${XDG_CURRENT_DESKTOP:-unknown}) detected — forcing XWayland"
  export XDG_BACKEND=x11
  export SDL_VIDEODRIVER=x11
else
  echo "X11 / unknown windowmanager"
fi
```

- [ ] **Step 3: Verify the script still parses**

Run: `bash -n launch.sh && echo PARSE_OK`
Expected: `PARSE_OK` (no syntax errors).

- [ ] **Step 4: Verify the Wayland branch triggers under a simulated KDE Wayland env**

Run:
```bash
XDG_SESSION_TYPE=wayland XDG_CURRENT_DESKTOP=KDE XDG_SESSION_DESKTOP=KDE \
  bash -c 'source <(sed -n "1,20p" launch.sh); echo "BACKEND=$XDG_BACKEND SDL=$SDL_VIDEODRIVER"' 2>/dev/null
```
Expected: output contains `BACKEND=x11 SDL=x11` (the Wayland branch exported both).

- [ ] **Step 5: Verify an X11 session is left untouched**

Run:
```bash
XDG_SESSION_TYPE=x11 XDG_CURRENT_DESKTOP=KDE XDG_SESSION_DESKTOP=KDE \
  bash -c 'source <(sed -n "1,20p" launch.sh); echo "BACKEND=${XDG_BACKEND:-unset} SDL=${SDL_VIDEODRIVER:-unset}"' 2>/dev/null
```
Expected: output contains `BACKEND=unset SDL=unset` (X11 path did not force XWayland).

- [ ] **Step 6: Commit**

```bash
git add launch.sh
git commit -m "fix(launch): force XWayland on any Wayland session (KDE/KWin), not just Hyprland

KDE Wayland previously fell through to the no-op 'Unknown windowmanager'
branch and never set XDG_BACKEND/SDL_VIDEODRIVER=x11, breaking the
transparent ARGB pet window. Generalize the Hyprland special-case to all
Wayland sessions; X11 sessions are unaffected.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Add `run-local.sh` portable dev runner

**Files:**
- Create: `run-local.sh` (repo root, tracked)

**Interfaces:**
- Consumes: the patched `launch.sh` from Task 1; a release tarball (arg `$1`, default `./MateEngineX_release.tar.gz`).
- Produces: a runner that, given the tarball, extracts `MateEngineX/Payload` into gitignored `./Payload/`, copies the repo's patched `launch.sh` beside the binary, and execs it. Later tasks run the app via `./run-local.sh`.

- [ ] **Step 1: Create `run-local.sh`**

```bash
#!/usr/bin/env bash
# Portable dev runner for Cassie's fork.
# Extracts the upstream release into a gitignored ./Payload and runs it with the
# repo's patched launch.sh (source of truth), NOT the release's bundled copy.
#
# Usage:
#   ./run-local.sh [path-to-release-tarball]
# If ./Payload already exists, it is reused (no re-extract).
set -euo pipefail

here="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)"
payload="$here/Payload"
tarball="${1:-$here/MateEngineX_release.tar.gz}"

if [[ ! -d "$payload" ]]; then
  if [[ ! -f "$tarball" ]]; then
    echo "No Payload/ and no tarball at: $tarball" >&2
    echo "Download it first:" >&2
    echo "  gh release download Public-Release-X3.2.0_5 \\" >&2
    echo "    -R Marksonthegamer/Mate-Engine-Linux-Port \\" >&2
    echo "    -p 'MateEngineX_*.tar.gz' -O '$tarball'" >&2
    exit 1
  fi
  echo "Extracting $(basename "$tarball") -> Payload/"
  mkdir -p "$payload"
  # Strip the leading MateEngineX/Payload/ so contents land directly in ./Payload
  tar xzf "$tarball" -C "$payload" --strip-components=2 MateEngineX/Payload
fi

# Always refresh launch.sh from the tracked, patched source of truth.
cp "$here/launch.sh" "$payload/launch.sh"
chmod +x "$payload/launch.sh"

exec "$payload/launch.sh" "$@"
```

- [ ] **Step 2: Make it executable**

Run: `chmod +x run-local.sh && echo CHMOD_OK`
Expected: `CHMOD_OK`.

- [ ] **Step 3: Verify it parses**

Run: `bash -n run-local.sh && echo PARSE_OK`
Expected: `PARSE_OK`.

- [ ] **Step 4: Verify the no-tarball guard**

Run (in a clean subdir with no Payload/tarball):
```bash
( tmp=$(mktemp -d); cp run-local.sh launch.sh "$tmp"/; cd "$tmp"; \
  ./run-local.sh ./nope.tar.gz; echo "exit=$?" )
```
Expected: prints the "No Payload/ and no tarball" message and the `gh release download` hint, then `exit=1`.

- [ ] **Step 5: Commit**

```bash
git add run-local.sh
git commit -m "feat: add run-local.sh portable dev runner

Extracts the upstream release Payload into gitignored ./Payload and runs it
with the repo's patched launch.sh. No /opt, no sudo — run-in-place for the
fork.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Install runtime dependencies (Nobara/Fedora)

**Files:** none (system state only — no commit).

**Interfaces:**
- Consumes: nothing.
- Produces: the shared libs the Unity player + tray/appindicator need at runtime.

- [ ] **Step 1: Install the deps from install.sh's Fedora branch**

Run:
```bash
sudo dnf install -y pulseaudio-libs gtk3 glib2 libX11 libXext libXrender \
  libXrandr libXdamage libXcursor libXcomposite libayatana-appindicator-gtk3
```
Expected: completes with the packages installed or already-present.

- [ ] **Step 2: Verify the key libs resolve**

Run:
```bash
for p in gtk3 libX11 libXcomposite libayatana-appindicator-gtk3 pulseaudio-libs; do
  rpm -q "$p" || echo "MISSING: $p"
done
```
Expected: a version line for each; no `MISSING:` lines.

- [ ] **Step 3: Verify the XWayland transparency helpers exist (used by launch.sh)**

Run: `command -v glxinfo xdpyinfo`
Expected: both resolve (`/usr/bin/glxinfo`, `/usr/bin/xdpyinfo`). If missing: `sudo dnf install -y glx-utils xorg-x11-utils`.

---

### Task 4: Download + extract the release into `Payload/`

**Files:**
- Create (gitignored, untracked): `Payload/` and `MateEngineX_release.tar.gz`.

**Interfaces:**
- Consumes: `run-local.sh` from Task 2.
- Produces: a populated `Payload/` containing `MateEngineX.x86_64` + `MateEngineX_Data/`, ready to run.

- [ ] **Step 1: Download the release tarball (284 MB)**

Run:
```bash
gh release download Public-Release-X3.2.0_5 \
  -R Marksonthegamer/Mate-Engine-Linux-Port \
  -p 'MateEngineX_*.tar.gz' -O MateEngineX_release.tar.gz
```
Expected: `MateEngineX_release.tar.gz` (~284 MB) in the repo root.

- [ ] **Step 2: Extract via the runner (extract-only, then confirm)**

Run: `./run-local.sh MateEngineX_release.tar.gz` — then immediately close the app window (it will try to launch; that's fine, Task 5 does the real verification). If it errors before extracting, re-run.

Then confirm the binary landed:
```bash
find Payload -maxdepth 1 -name 'MateEngineX.*' -type f
```
Expected: `Payload/MateEngineX.x86_64`.

- [ ] **Step 3: Verify Payload is NOT tracked by git**

Run: `git status --porcelain | grep -E 'Payload|MateEngineX_release' || echo CLEAN`
Expected: `CLEAN` (both are gitignored; nothing staged/untracked-reported).

- [ ] **Step 4: Verify launch.sh was copied into Payload**

Run: `diff -q launch.sh Payload/launch.sh && echo SAME`
Expected: `SAME` (runner copied the patched launcher beside the binary).

---

### Task 5: Launch and verify the transparent pet on KDE Wayland

**Files:** none (observational; may create `docs/superpowers/notes/phase0-run-notes.md` if kdotool work is needed).

**Interfaces:**
- Consumes: populated `Payload/` from Task 4; patched `launch.sh`.
- Produces: confirmation the pet renders transparent on KDE Wayland, or a documented follow-up if it doesn't.

- [ ] **Step 1: Confirm the live session is KDE Wayland**

Run: `echo "type=$XDG_SESSION_TYPE desktop=$XDG_CURRENT_DESKTOP"`
Expected: `type=wayland desktop=KDE` (the case this plan targets).

- [ ] **Step 2: Launch**

Run: `./run-local.sh`
Expected: launcher prints `Wayland session (KDE) detected — forcing XWayland` and a `Visual ARGB:` line with a non-empty id, then the app window appears.

- [ ] **Step 3: Verify transparency (manual observation)**

Look at the pet on the desktop.
Expected: the VRM avatar floats with a **transparent** background — you see the desktop/wallpaper around/behind it, NOT an opaque black or grey box.

- [ ] **Step 4: Verify basic interaction**

Drag the avatar with the mouse; right-click for its menu if it has one.
Expected: the avatar moves and responds; it stays on top of the desktop.

- [ ] **Step 5: Verify relaunch**

Close the app, run `./run-local.sh` again.
Expected: it reuses the existing `Payload/` (no re-extract) and the pet reappears the same way.

- [ ] **Step 6 (only if Step 3 fails — black/opaque box): investigate kdotool + visual id**

If the background is not transparent:
1. Check the ARGB visual was found:
   Run: `glxinfo 2>/dev/null | grep -i "32 tc  0  32  0 r  y" | head -1`
   Expected: a matching visual line. If empty, the KWin/XWayland GLX config lacks a 32-bit ARGB visual — note it.
2. Inspect how the port uses the bundled `Plugins/kdotool-main` (KWin window tool) — it may be the intended path for setting the window's compositing/transparency on KDE:
   Run: `ls Plugins/kdotool-main; grep -rniE "kdotool" Assets --include=*.cs | head`
3. Write findings to `docs/superpowers/notes/phase0-run-notes.md` (what was tried, what the visual id was, whether kdotool is invoked at runtime) so the follow-up fork edit has a starting point. Commit that note:
   ```bash
   git add docs/superpowers/notes/phase0-run-notes.md
   git commit -m "docs: phase 0 KDE Wayland transparency investigation notes

   Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
   ```

---

## Done criteria

- `launch.sh` forces XWayland on any Wayland session; committed. (Task 1)
- `run-local.sh` extracts + runs portably; committed. (Task 2)
- Runtime deps present on the box. (Task 3)
- `Payload/` populated and untracked; patched launcher copied in. (Task 4)
- Pet renders transparent + interactive on KDE Wayland, survives relaunch — OR the transparency failure is documented with kdotool/visual-id findings for the next edit. (Task 5)
