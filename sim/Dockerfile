# Golem sim in kiosk mode: turtlesim + rosbridge, no desktop.
#
# The base image's /entrypoint.sh REGENERATES ~/.vnc/xstartup on every boot
# (heredoc with mate-session), so copying our own xstartup is useless: it gets
# clobbered. Instead we patch the entrypoint at build time: the xstartup it
# generates now launches /golem/kiosk.sh, and the VNC geometry drops to 500x500.
FROM tiryoh/ros2-desktop-vnc:humble

RUN apt-get update && apt-get install -y --no-install-recommends \
      ros-humble-rosbridge-suite \
      openbox \
      wmctrl \
      x11-xserver-utils \
    && rm -rf /var/lib/apt/lists/*

COPY --chown=ubuntu:ubuntu kiosk/kiosk.sh /golem/kiosk.sh
# The entrypoint copies /root/.config into the user's home on every boot,
# so the openbox config is planted in /root and arrives on its own.
COPY kiosk/rc.xml /root/.config/openbox/rc.xml
# Demo panel: websockify serves /usr/lib/novnc as the web root
# => http://localhost:6080/control.html
COPY kiosk/control.html /usr/lib/novnc/control.html

# The turtlesim window has a fixed size (500x500): the VNC screen measures
# exactly that, and openbox strips the decorations => the window IS the screen.
# -AcceptSetDesktopSize=0 stops noVNC from resizing the remote desktop on connect.
RUN chmod +x /golem/kiosk.sh \
    && sed -i 's|^mate-session$|exec bash /golem/kiosk.sh|' /entrypoint.sh \
    && sed -i 's/-geometry 1920x1080/-geometry 500x500 -AcceptSetDesktopSize=0/g' /entrypoint.sh \
    && grep -q '/golem/kiosk.sh' /entrypoint.sh \
    && grep -q '500x500' /entrypoint.sh
