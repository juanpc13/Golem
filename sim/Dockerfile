# Golem sim: Gazebo Fortress (the simulator paired with ROS 2 Humble) in kiosk mode.
# Reality lives here — physics, walls, bodies, contact sensors — generated from the floor plan
# (world/plan.json) when the image is built. The picture (Gazebo's GUI) reaches the browser through
# noVNC on :80; ROS reaches the golems through rosbridge on :9090; ros_gz_bridge turns each body's
# Gazebo topics into ROS topics.
#
# The base image's /entrypoint.sh REGENERATES ~/.vnc/xstartup on every boot, so copying our own
# xstartup is useless: the entrypoint is patched at build time instead — its xstartup launches
# /golem/kiosk.sh, and noVNC's websockify points at x11vnc (5902), not at the base's Xvnc.
FROM tiryoh/ros2-desktop-vnc:humble

RUN apt-get update && apt-get install -y --no-install-recommends \
      ros-humble-ros-gz \
      openbox \
      wmctrl \
      x11-xserver-utils \
    && rm -rf /var/lib/apt/lists/*

RUN apt-get update && apt-get install -y --no-install-recommends xvfb x11vnc \
    && rm -rf /var/lib/apt/lists/*

RUN apt-get update && apt-get install -y --no-install-recommends ros-humble-rosbridge-suite scrot \
    && rm -rf /var/lib/apt/lists/*

# The world is generated from the plan: arena.sdf + the bridge's topic mappings.
COPY world/ /world/
RUN python3 /world/build_world.py /world/plan.json /world

COPY --chown=ubuntu:ubuntu kiosk/kiosk.sh bridge/teleport.py bridge/crates.py /golem/
# The entrypoint copies /root/.config into the user's home on every boot,
# so the openbox config is planted in /root and arrives on its own.
COPY kiosk/rc.xml /root/.config/openbox/rc.xml

RUN chmod +x /golem/kiosk.sh \
    && sed -i 's|^mate-session$|exec bash /golem/kiosk.sh|' /entrypoint.sh \
    && sed -i 's|websockify --web=/usr/lib/novnc 80 localhost:5901|websockify --web=/usr/lib/novnc 80 localhost:5902|' /entrypoint.sh \
    && grep -q '/golem/kiosk.sh' /entrypoint.sh \
    && grep -q 'localhost:5902' /entrypoint.sh
