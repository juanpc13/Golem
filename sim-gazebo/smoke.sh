#!/usr/bin/env bash
# Smoke test: physics + two bodies + the simulator reporting a collision, headless, on this machine.
# Usage: sim-gazebo/smoke.sh            (needs the image: docker build -t golem-gazebo:smoke sim-gazebo/)
set -u
IMG=golem-gazebo:smoke
NAME=golem-gazebo-smoke
SRC='source /opt/ros/humble/setup.bash'
IGN="$SRC && ign"

# Ground truth from the scene broadcaster (odometry integrates the wheels even when the body is blocked).
poses() {
  docker exec $NAME bash -lc "$SRC && timeout 4 ign topic -e -t /world/arena/dynamic_pose/info -n 1 2>/dev/null" \
    | awk '/name: "(blue|red)"/ { n = $2 } /position \{/ { p = 1 } p && /x:/ { x = $2 } p && /y:/ { y = $2; if (n != "") printf "  %s x=%.2f y=%.2f\n", n, x, y; n = ""; p = 0 }'
}

docker rm -f $NAME >/dev/null 2>&1 || true
docker run -d --name $NAME $IMG >/dev/null
echo "== waiting for the physics server =="
for i in $(seq 1 30); do
  if docker exec $NAME bash -lc "$IGN topic -l 2>/dev/null | grep -q /world/arena/stats"; then echo "server up after ${i}s"; break; fi
  sleep 1
done
echo "== topics =="; docker exec $NAME bash -lc "$IGN topic -l" | grep -E "cmd_vel|odometry|contact|stats"

echo "== idle cost =="; docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' $NAME
echo "== ground-truth poses before =="; poses

echo "== both drive toward each other at 0.6 m/s =="
docker exec $NAME bash -lc "$IGN topic -t /model/blue/cmd_vel -m ignition.msgs.Twist -p 'linear: {x: 0.6}'"
docker exec $NAME bash -lc "$IGN topic -t /model/red/cmd_vel  -m ignition.msgs.Twist -p 'linear: {x: 0.6}'"

echo "== blue's contact sensor (the simulator itself reporting the hit) =="
docker exec $NAME bash -lc "$SRC && timeout 25 ign topic -e -t /world/arena/model/blue/link/chassis/sensor/bumper/contact 2>/dev/null | grep -m 1 -A 14 'contact {'" \
  || echo "(no contact reported within 25 s)"

echo "== ground-truth poses after =="; poses

echo "== real-time factor while running =="
docker exec $NAME bash -lc "$SRC && timeout 3 ign topic -e -t /world/arena/stats -n 1 2>/dev/null | grep -E 'real_time_factor|iterations'"

echo "== cost while simulating =="; docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' $NAME

echo "== server log (warnings, sensors) =="; docker logs $NAME 2>&1 | grep -iE "warn|err|contact|sensor|plugin" | head -12

echo "== stop =="
docker exec $NAME bash -lc "$IGN topic -t /model/blue/cmd_vel -m ignition.msgs.Twist -p 'linear: {x: 0.0}'; $IGN topic -t /model/red/cmd_vel -m ignition.msgs.Twist -p 'linear: {x: 0.0}'"
docker rm -f $NAME >/dev/null
echo "done"
