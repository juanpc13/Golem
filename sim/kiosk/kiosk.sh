#!/bin/bash
# Golem kiosk: no desktop. Just openbox (minimal WM), rosbridge and turtlesim.
# The TurtleSim window is the only thing visible in the browser.
unset DBUS_SESSION_BUS_ADDRESS
source /opt/ros/humble/setup.bash

xsetroot -solid "#1a1a2e"
# openbox reads ~/.config/openbox/rc.xml: no decorations, window at (0,0)
openbox &

# The membrane: all of ROS exposed as JSON over websocket on :9090
ros2 launch rosbridge_server rosbridge_websocket_launch.xml &

# turtlesim is the process that holds the session: if it dies, the session restarts clean
exec ros2 run turtlesim turtlesim_node
