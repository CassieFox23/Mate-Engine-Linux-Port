#!/bin/bash

export GDK_BACKEND=x11

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

visual_id=$(glxinfo 2>/dev/null | grep -i "32 tc  0  32  0 r  y .   8  8  8  8 .  .   0 24  8" | head -n1 | awk '{print $1}')

if [ -z "$visual_id" ]; then
    visual_id=$(glxinfo 2>/dev/null | grep -i "32 tc  0  32  0 r  y" | head -n1 | awk '{print $1}')
fi

if [ -z "$visual_id" ]; then
    visual_id=$(xdpyinfo 2>/dev/null | grep -A 2 "visual id" | grep -B 5 "depth:.* .*32 planes" | grep "visual id" | awk '{print $3}' | head -n1)
fi

echo "Visual ARGB: $visual_id"

echo "\`\`\`"

export SDL_VIDEO_X11_VISUALID=$visual_id

SDL_VIDEO_X11_VISUALID=$visual_id "$(dirname "$(realpath "${BASH_SOURCE[0]}")")/MateEngineX.$(uname -m)" "$@" | grep -v "[Vulkan init] extensions: "

echo "\`\`\`"
