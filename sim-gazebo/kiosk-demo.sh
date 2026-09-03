#!/usr/bin/env bash
# Watch the smoke test: Gazebo's GUI in the browser (kiosk), then drive blue and red into each other.
# Build first:  docker build -f sim-gazebo/Dockerfile.kiosk -t golem-gazebo:kiosk sim-gazebo/
# Watch at:     http://localhost:6081/vnc.html?autoconnect=true&resize=scale
# Usage:        sim-gazebo/kiosk-demo.sh            (starts the kiosk if needed, then runs the crash)
#               sim-gazebo/kiosk-demo.sh reset      (puts both bodies back on their start marks)
set -u
NAME=golem-gazebo-kiosk
SRC='source /opt/ros/humble/setup.bash'

if ! docker ps --format '{{.Names}}' | grep -q "^$NAME\$"; then
  docker rm -f $NAME >/dev/null 2>&1 || true
  docker run -d --name $NAME -p 6081:80 --shm-size 512m --security-opt seccomp=unconfined golem-gazebo:kiosk >/dev/null
  echo "kiosk starting — open http://localhost:6081/vnc.html?autoconnect=true&resize=scale"
  for i in $(seq 1 60); do
    if docker exec -u ubuntu $NAME bash -lc "$SRC && ign topic -l 2>/dev/null | grep -q /world/arena/stats"; then echo "physics up after ${i}s"; break; fi
    sleep 1
  done
fi

ign() { docker exec -u ubuntu $NAME bash -lc "$SRC && ign $*"; }

if [ "${1:-}" = "reset" ]; then
  ign "service -s /world/arena/set_pose --reqtype ignition.msgs.Pose --reptype ignition.msgs.Boolean --timeout 2000 --req 'name: \"blue\", position: {x: 2, y: 5.54, z: 0.3}, orientation: {w: 1}'"
  ign "service -s /world/arena/set_pose --reqtype ignition.msgs.Pose --reptype ignition.msgs.Boolean --timeout 2000 --req 'name: \"red\", position: {x: 9, y: 5.54, z: 0.3}, orientation: {z: 1, w: 0}'"
  exit 0
fi

echo "== both drive toward each other at 0.6 m/s =="
ign "topic -t /model/blue/cmd_vel -m ignition.msgs.Twist -p 'linear: {x: 0.6}'"
ign "topic -t /model/red/cmd_vel  -m ignition.msgs.Twist -p 'linear: {x: 0.6}'"
echo "== waiting for the simulator to report the contact =="
docker exec -u ubuntu $NAME bash -lc "$SRC && timeout 30 ign topic -e -t /world/arena/model/blue/link/chassis/sensor/bumper/contact 2>/dev/null | grep -m 1 -A 9 'collision1'" \
  || echo "(no contact within 30 s)"
echo "== stop =="
ign "topic -t /model/blue/cmd_vel -m ignition.msgs.Twist -p 'linear: {x: 0.0}'"
ign "topic -t /model/red/cmd_vel  -m ignition.msgs.Twist -p 'linear: {x: 0.0}'"
docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' $NAME
