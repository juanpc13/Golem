#!/bin/bash
# The sim's session (the base image's xstartup execs this). Reality first, then the picture:
#   1. Gazebo's physics server, running from the start (the world built from the floor plan);
#   2. ros_gz_bridge — every body's cmd_vel (in), odometry and contacts (out) as ROS topics;
#   3. the teleport helper (world services are not bridged in this release) and the crate lever
#      (the four buttons' page on :6081: an obstacle into the running world, or away);
#   4. rosbridge — all of ROS as JSON over websocket on :9090, the golems' membrane;
#   5. Gazebo's GUI, unless KIOSK=false.
# The GUI does NOT run on the base image's Xvnc (:1): Qt Quick + OGRE die there with "XIO fatal
# IO error". It runs on an Xvfb display (:2) with Mesa's software OpenGL and x11vnc mirrors that
# display to noVNC (port 5902). Server and GUI are SEPARATE processes: launched together, the
# launcher shuts the server down with a SIGINT half a second after start.
unset DBUS_SESSION_BUS_ADDRESS
source /opt/ros/humble/setup.bash
export LIBGL_ALWAYS_SOFTWARE=1
export QT_X11_NO_MITSHM=1

ign gazebo -s -r -v 1 /world/arena.sdf &
sleep 3

set -f   # the bridge mappings carry [ and ]: no globbing
ros2 run ros_gz_bridge parameter_bridge $(cat /world/bridge.args) &
set +f
python3 /golem/teleport.py arena &
python3 /golem/crates.py arena 6081 &
ros2 launch rosbridge_server rosbridge_websocket_launch.xml &

if [ "${KIOSK:-true}" != "true" ]; then
  echo "[sim] KIOSK=${KIOSK}: headless, no picture"
  wait
  exit 0
fi

Xvfb :2 -screen 0 720x720x24 +extension GLX +render -noreset &
sleep 2
export DISPLAY=:2
xsetroot -solid "#14161f"
openbox &
x11vnc -display :2 -rfbport 5902 -forever -shared -nopw -noxdamage -quiet &

# the GUI window is born larger than the screen: maximize it once it shows up
( for i in $(seq 1 90); do sleep 1; wmctrl -r "Gazebo" -b add,maximized_vert,maximized_horz && break; done ) &

# Software rendering eats every core it is given: the picture yields to the physics and the bridges.
exec nice -n 10 ign gazebo -g -v 1
