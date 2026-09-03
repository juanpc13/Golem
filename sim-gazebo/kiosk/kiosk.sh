#!/bin/bash
# Gazebo kiosk. The GUI does NOT run on the base image's Xvnc (:1): Qt Quick + OGRE die there
# with "XIO fatal IO error". It runs on an Xvfb display (:2) with Mesa's software OpenGL — the
# very setup Gazebo's own CI uses — and x11vnc mirrors that display to noVNC (port 5902).
unset DBUS_SESSION_BUS_ADDRESS
source /opt/ros/humble/setup.bash
export LIBGL_ALWAYS_SOFTWARE=1
export QT_X11_NO_MITSHM=1

Xvfb :2 -screen 0 1024x768x24 +extension GLX +render -noreset &
sleep 2
export DISPLAY=:2
xsetroot -solid "#1a1a2e"
openbox &
x11vnc -display :2 -rfbport 5902 -forever -shared -nopw -noxdamage -quiet &

# the GUI window is born larger than the screen: maximize it once it shows up
( for i in $(seq 1 60); do sleep 1; wmctrl -r "Gazebo" -b add,maximized_vert,maximized_horz && break; done ) &

# Server and GUI as SEPARATE processes: launched together, the launcher shut the server
# down with a SIGINT half a second after start (the GUI survived, showing "N/A").
ign gazebo -s -r -v 1 /worlds/arena.sdf &
sleep 3
exec ign gazebo -g -v 1
