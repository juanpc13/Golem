#!/usr/bin/env python3
"""The robot: the body's own node, one per body. It knows nothing of maps, routes or peers — it takes ONE order at a
time from its golem and reports what came of it (Juan, 16-sep-2026: "el robot solo es el cuerpo; nosotros le decimos
qué hacer, y cuando termina le dice al actor por un endpoint que ya terminó, para pedir el siguiente print").

Its vocabulary is the robot's BASE ACTIONS (Juan: "avanzar / retroceder / girar a la derecha / girar a la izquierda /
detener / continuar; el choque es lo que reporta"). Orders arrive on /golem/<body>/order (std_msgs/String: the JSON the
golem's journal printed, plus the action and what the body needs), switched on "action":
  {"action": "advance",   "route": 3, "x": .., "y": .., "ax": .., "ay": .., "ex": .., "ey": .., "within": 0.25, ...}  — move to a point
  {"action": "back",      "route": 3, "x": .., "y": .., "within": 0.15, ...}                          — move to a point IN REVERSE
  {"action": "turnLeft",  "route": 3, "heading": -1.57, ...}   — turn in place, counter-clockwise, until facing the heading
  {"action": "turnRight", "route": 3, "heading": -1.57, ...}   — the same, clockwise
  {"action": "stop"}                    — stand, remembering what it was doing (the operator held the route, or nothing is pending)
  {"action": "stop", "anchor": true}    — stand, and take the world's word for where you are (after a teleport)
  {"action": "continue"}                — take up what it was doing when it was stopped
A new action replaces whatever the body was doing. Reports go back to the golem's endpoints as JSON bodies:
  POST /robot/arrived {"route"}                                                         — the turn made, the point reached
  POST /robot/bump    {"route", "with", "x", "y", "heading", "px", "py", "ptheta"}      — route 0 when touched while standing
  POST /robot/stuck   {"route", "reason"}
The body drives /model/<body>/cmd_vel and watches its odometry — the world's truth, or its own wheels' reckoning
anchored once to the truth (a real robot's lot) — and its contact sensor, its bumper. THE BUMPER IS A SWITCH: the moment
it fires the motors stop and the bump is reported — where the touch landed on the plane (one radius from the body's
centre, in the direction the shell was pressed) and where the body stood, facing which way. What to do about it (back
off, go around) is the golem's to say: it comes as the next order. The body decides nothing.

Usage: body.py <body> <golem-url> [world|wheels]
"""
import json
import math
import sys
import threading
import time
import urllib.request

import rclpy
from geometry_msgs.msg import Twist
from nav_msgs.msg import Odometry
from rclpy.node import Node
from ros_gz_interfaces.msg import Contacts
from std_msgs.msg import String

TICK = 0.05                 # s, the servo's beat
RUN_TIMEOUT = 90.0          # s, a run that takes longer is stuck
TURN_TIMEOUT = 15.0         # s
STALL_AFTER = 3.0           # s without the distance improving = stuck
PROGRESS = 0.05             # m, an improvement smaller than this is noise
LINE_UP_WITHIN = 0.15       # m, a door's approach is lined up tighter than a stop
FACING_WITHIN = 0.05        # rad, close enough to call a turn made (~3 deg)
BACK_OFF_SPEED = 0.4        # m/s, in reverse, after a touch
STANDING_TOUCH_EVERY = 1.0  # s, a standing body reports a touch at most this often


def normalize(a):
    while a > math.pi:
        a -= 2 * math.pi
    while a < -math.pi:
        a += 2 * math.pi
    return a


def yaw_of(q):
    return math.atan2(2 * (q.w * q.z + q.x * q.y), 1 - 2 * (q.y * q.y + q.z * q.z))


class Body(Node):
    def __init__(self, body, golem_url, source):
        super().__init__("body_" + body)
        self.body = body
        self.golem_url = golem_url.rstrip("/")
        self.source = source
        self.truth = None            # (x, y, theta) the world's
        self.pose = None             # what the body believes: the truth, or dead reckoning
        self.anchor = None           # dead reckoning's anchor: (wheels pose, truth pose) at calibration
        self.calibrate = True
        self.contact = None          # (model, time, bearing) the last touch
        self.order = None            # the order being carried out (dict), None while standing
        self.held = None             # the order it was carrying when told to stop: "continue" takes it up again
        self.phase = None            # run: "approach" | "exit"
        self.began = 0.0
        self.best = float("inf")
        self.improved = 0.0
        self.closest = float("inf")  # a reverse move: the nearest it got to the point (moving away again = arrived)
        self.last_standing_touch = 0.0
        self.pressed = None          # the model the bumper is still pressed against since it was reported: not a new bump until released
        self.released_since = None   # when the bumper last went quiet (no contact message for a moment)
        self.lock = threading.Lock()

        self.cmd = self.create_publisher(Twist, "/model/%s/cmd_vel" % body, 10)
        self.create_subscription(String, "/golem/%s/order" % body, self.on_order, 10)
        self.create_subscription(Odometry, "/model/%s/odometry" % body, self.on_truth, 20)
        if source == "wheels":
            self.create_subscription(Odometry, "/model/%s/wheel_odometry" % body, self.on_wheels, 20)
        self.create_subscription(Contacts, "/model/%s/contacts" % body, self.on_contacts, 20)
        self.create_timer(TICK, self.tick)
        self.get_logger().info("body %s: orders on /golem/%s/order, reporting to %s, pose from %s" % (body, body, self.golem_url, source))

    # ---- telemetry ----
    def on_truth(self, m):
        p, q = m.pose.pose.position, m.pose.pose.orientation
        self.truth = (p.x, p.y, yaw_of(q))
        if self.source != "wheels":
            self.pose = self.truth

    def on_wheels(self, m):
        if self.truth is None:
            return
        p, q = m.pose.pose.position, m.pose.pose.orientation
        odom = (p.x, p.y, yaw_of(q))
        if self.calibrate:
            self.anchor = (odom, self.truth)
            self.calibrate = False
            self.get_logger().info("dead reckoning anchored: told it stands at (%.2f, %.2f)" % (self.truth[0], self.truth[1]))
        (ox, oy, ot), (tx, ty, tt) = self.anchor
        dx, dy = odom[0] - ox, odom[1] - oy
        turn = tt - ot
        self.pose = (tx + dx * math.cos(turn) - dy * math.sin(turn),
                     ty + dx * math.sin(turn) + dy * math.cos(turn),
                     normalize(tt + (odom[2] - ot)))

    def on_contacts(self, m):
        for c in m.contacts:
            a, b = c.collision1.name, c.collision2.name
            other = b if a.startswith(self.body + "::") else a
            model = other.split("::")[0]
            if model in ("ground_plane", self.body, ""):
                continue
            bearing = 0.0
            if self.truth is not None and len(c.positions) > 0:
                sx = sum(p.x for p in c.positions) / len(c.positions)
                sy = sum(p.y for p in c.positions) / len(c.positions)
                bearing = normalize(math.atan2(sy - self.truth[1], sx - self.truth[0]) - self.truth[2])
            self.contact = (model, time.time(), bearing)
            return

    # ---- orders ----
    def on_order(self, m):
        try:
            order = json.loads(m.data)
        except ValueError:
            self.get_logger().warning("an order I cannot read: %s" % m.data[:120])
            return
        with self.lock:
            action = order.get("action")
            if action == "stop":
                self.held = self.order
                self.order = None
                if order.get("anchor"):
                    self.calibrate = True
                self.drive(0.0, 0.0)
                return
            if action == "continue":
                if self.held is None:
                    return
                order, self.held = self.held, None
                action = order.get("action")
                self.get_logger().info("route %s: continuing" % order.get("route"))
            if action not in ("advance", "back", "turnLeft", "turnRight"):
                self.get_logger().warning("an action I do not know: %s" % action)
                return
            self.order = order
            self.held = None
            self.phase = "approach" if action == "advance" and (order.get("ax") != order.get("ex") or order.get("ay") != order.get("ey")) else "exit"
            self.closest = float("inf")
            self.began = time.time()
            self.best = float("inf")
            self.improved = self.began
            self.get_logger().info("route %s: %s %s" % (order.get("route"), action,
                                   ("to heading %.2f" % order.get("heading", 0.0)) if action.startswith("turn") else ("to (%.2f, %.2f)" % (order.get("x", 0.0), order.get("y", 0.0)))))

    # ---- the servo's beat ----
    def tick(self):
        with self.lock:
            order = self.order
            now = time.time()
            touch = self.contact
            # A bump is reported ONCE per contact: while the bumper stays pressed against the same thing (the golem's
            # order — back off, step aside — is on its way, or being carried out), it is not a new bump. It is released
            # when the sensor goes quiet for a moment; only then can that thing be bumped again.
            in_contact = touch is not None and now - touch[1] < 0.3
            if not in_contact:
                if self.released_since is None:
                    self.released_since = now
                elif now - self.released_since > 0.4:
                    self.pressed = None
            else:
                self.released_since = None
            fresh = in_contact and (self.pressed is None or touch[0] != self.pressed)
            if order is None:
                if fresh and now - self.last_standing_touch > STANDING_TOUCH_EVERY:
                    self.last_standing_touch = now
                    self.pressed = touch[0]
                    self.report_bump(0, touch, None)
                return
            if fresh and touch[1] > self.began:
                self.drive(0.0, 0.0)          # the bumper fired: the motors stop at once
                self.order = None
                self.pressed = touch[0]
                self.report_bump(order.get("route", 0), touch, order)
                return
            if self.pose is None:
                return
            x, y, theta = self.pose
            action = order["action"]
            if action in ("turnLeft", "turnRight"):
                # in place, the way the golem said (left: counter-clockwise), until facing the heading
                deviation = normalize(order.get("heading", 0.0) - theta)
                if abs(deviation) < FACING_WITHIN:
                    self.drive(0.0, 0.0)
                    self.done(order, "arrived", {"route": order.get("route", 0)})
                    return
                if now - self.began > TURN_TIMEOUT:
                    self.drive(0.0, 0.0)
                    self.done(order, "stuck", {"route": order.get("route", 0), "reason": "turn timeout"})
                    return
                sign = 1.0 if action == "turnLeft" else -1.0
                left = deviation if deviation >= 0 else deviation + 2 * math.pi   # the angle still to turn, going the way told
                if action == "turnRight":
                    left = 2 * math.pi - left if deviation > 0 else -deviation
                angular = sign * max(0.3, min(1.5, 3.0 * left))
                self.drive(0.0, angular)
                return
            if action == "back":
                self.reverse_to(order, now)
                return
            # an advance: line up at the approach, then run to the exit (one leg, when the two coincide)
            if self.phase == "approach":
                tx, ty, within = order.get("ax", order["x"]), order.get("ay", order["y"]), LINE_UP_WITHIN
            else:
                tx, ty, within = order.get("ex", order["x"]), order.get("ey", order["y"]), order.get("within", 0.25)
            dx, dy = tx - x, ty - y
            distance = math.hypot(dx, dy)
            if distance < within:
                if self.phase == "approach":
                    self.phase = "exit"
                    self.best = float("inf")
                    self.improved = now
                    return
                self.drive(0.0, 0.0)
                self.done(order, "arrived", {"route": order.get("route", 0)})
                return
            if distance < self.best - PROGRESS:
                self.best = distance
                self.improved = now
            elif now - self.improved > STALL_AFTER:
                self.drive(0.0, 0.0)
                self.done(order, "stuck", {"route": order.get("route", 0), "reason": "stalled: no progress for 3 s"})
                return
            if now - self.began > RUN_TIMEOUT:
                self.drive(0.0, 0.0)
                self.done(order, "stuck", {"route": order.get("route", 0), "reason": "timeout"})
                return
            # turn toward the target first (in place: doors are narrow and runs through them must be straight);
            # move once aligned, slower the further off — and slower the closer to the arrival circle
            cruise = order.get("body", {}).get("speed", 1.0)
            heading = math.atan2(dy, dx)
            deviation = normalize(heading - theta)
            angular = max(-3.0, min(3.0, 3.0 * deviation))
            linear = min(cruise, 1.5 * (distance - within) + 0.15) * math.cos(deviation) if abs(deviation) < 0.35 else 0.0
            self.drive(linear, angular)

    # A move in reverse, straight back to the point the golem chose (its own retreat behind where it stood): the body
    # reverses, gently, until it is within reach of the point or starts moving away from it.
    def reverse_to(self, order, now):
        x, y, theta = self.pose
        tx, ty, within = order["x"], order["y"], order.get("within", LINE_UP_WITHIN)
        distance = math.hypot(tx - x, ty - y)
        if distance < within or distance > self.closest + PROGRESS or now - self.began > 8.0:
            self.drive(0.0, 0.0)
            self.done(order, "arrived", {"route": order.get("route", 0)})
            return
        self.closest = min(self.closest, distance)
        self.drive(-min(BACK_OFF_SPEED, 1.0 * distance + 0.1), 0.0)

    # Where the touch landed on the plane, as the body reckons it: one radius from the centre of the body it believes,
    # in the direction the shell was pressed. The heading points into what was touched. And where the body stands,
    # facing which way — the golem's route backs it off from there.
    def report_bump(self, route, touch, order):
        pose = self.pose
        if pose is None:
            return
        radius = (order or {}).get("body", {}).get("radius", 0.25)
        heading = normalize(pose[2] + touch[2])
        self.post("bump", {"route": route, "with": touch[0],
                           "x": pose[0] + radius * math.cos(heading), "y": pose[1] + radius * math.sin(heading),
                           "heading": heading, "px": pose[0], "py": pose[1], "ptheta": pose[2]})

    def done(self, order, what, report):
        self.order = None
        self.post(what, report)

    def drive(self, linear, angular):
        t = Twist()
        t.linear.x = float(linear)
        t.angular.z = float(angular)
        self.cmd.publish(t)

    # The report travels to the golem's endpoint as a JSON body, off the servo's beat.
    def post(self, what, report):
        def send():
            data = json.dumps(report).encode("utf-8")
            req = urllib.request.Request(self.golem_url + "/robot/" + what, data=data, headers={"Content-Type": "application/json"}, method="POST")
            try:
                with urllib.request.urlopen(req, timeout=5) as r:
                    r.read()
            except Exception as e:  # noqa: BLE001 — the golem may be rebooting; the next order will come
                self.get_logger().warning("could not report %s to the golem: %s" % (what, e))
        threading.Thread(target=send, daemon=True).start()


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    rclpy.init()
    node = Body(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "world")
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    node.destroy_node()
    rclpy.shutdown()


if __name__ == "__main__":
    main()
