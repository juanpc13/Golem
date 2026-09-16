#!/usr/bin/env python3
"""The robot: the body's own node, one per body. It knows nothing of maps, routes or peers — it takes ONE order at a
time from its golem and reports what came of it (Juan, 16-sep-2026: "el robot solo es el cuerpo; nosotros le decimos
qué hacer, y cuando termina le dice al actor por un endpoint que ya terminó, para pedir el siguiente print").

Orders arrive on /golem/<body>/order (std_msgs/String, the JSON the golem's journal printed, plus what the body needs):
  {"order": "turn", "route": 3, "heading": -1.57, "body": {"speed": 2.0, "radius": 0.25, "retreat": 0.6}, ...}
  {"order": "run",  "route": 3, "x": .., "y": .., "ax": .., "ay": .., "ex": .., "ey": .., "within": 0.25, "body": {...}, ...}
  {"order": "stop"}                       — stand (the operator held the route, or nothing is pending)
  {"order": "stop", "anchor": true}       — stand, and take the world's word for where you are (after a teleport)
A new order replaces whatever the body was doing. Reports go back to the golem's endpoints as JSON bodies:
  POST /robot/turned  {"route", "heading"}
  POST /robot/reached {"route", "x", "y"}                       — the point the order named, verbatim
  POST /robot/touched {"route", "with", "x", "y", "heading", "px", "py"}   — route 0 when touched while standing
  POST /robot/stuck   {"route", "reason"}
The body drives /model/<body>/cmd_vel and watches its odometry — the world's truth, or its own wheels' reckoning
anchored once to the truth (a real robot's lot) — and its contact sensor. A touch ends the order: the body backs off
until it is free again and the body's own retreat further, then reports where the touch landed on the plane (one
radius from its centre, in the direction the shell was pressed) and where it believes it stands.

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
FREE_FOR = 0.4              # s without a touch reported = free
BACK_OFF_AT_MOST = 6.0      # s
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
        self.phase = None            # run: "approach" | "exit"; touch: "backing"
        self.began = 0.0
        self.best = float("inf")
        self.improved = 0.0
        self.backing = None          # (touch, began, start pose, retreat, free_since)
        self.last_standing_touch = 0.0
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
            what = order.get("order")
            if what == "stop":
                self.order = None
                self.backing = None
                if order.get("anchor"):
                    self.calibrate = True
                self.drive(0.0, 0.0)
                return
            if what not in ("turn", "run"):
                self.get_logger().warning("an order I do not know: %s" % what)
                return
            self.order = order
            self.phase = "approach" if what == "run" and (order.get("ax") != order.get("ex") or order.get("ay") != order.get("ey")) else "exit"
            self.began = time.time()
            self.best = float("inf")
            self.improved = self.began
            self.backing = None
            self.get_logger().info("route %s: %s %s" % (order.get("route"), what,
                                   ("to heading %.2f" % order.get("heading", 0.0)) if what == "turn" else ("to (%.2f, %.2f)" % (order.get("x", 0.0), order.get("y", 0.0)))))

    # ---- the servo's beat ----
    def tick(self):
        with self.lock:
            if self.backing is not None:
                self.back_off()
                return
            order = self.order
            now = time.time()
            touch = self.contact
            if order is None:
                if touch is not None and now - touch[1] < 0.3 and now - self.last_standing_touch > STANDING_TOUCH_EVERY:
                    self.last_standing_touch = now
                    self.report_touch(0, touch)
                return
            if touch is not None and touch[1] > self.began:
                self.begin_back_off(touch, order)
                return
            if self.pose is None:
                return
            x, y, theta = self.pose
            if order["order"] == "turn":
                deviation = normalize(order.get("heading", 0.0) - theta)
                if abs(deviation) < FACING_WITHIN:
                    self.drive(0.0, 0.0)
                    self.done(order, "turned", {"route": order.get("route", 0), "heading": order.get("heading", 0.0)})
                    return
                if now - self.began > TURN_TIMEOUT:
                    self.drive(0.0, 0.0)
                    self.done(order, "stuck", {"route": order.get("route", 0), "reason": "turn timeout"})
                    return
                angular = max(-1.5, min(1.5, 3.0 * deviation))
                if abs(angular) < 0.3:
                    angular = math.copysign(0.3, angular)
                self.drive(0.0, angular)
                return
            # a run: line up at the approach, then run to the exit (one leg, when the two coincide)
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
                self.done(order, "reached", {"route": order.get("route", 0), "x": order["x"], "y": order["y"]})
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

    # After a touch the body is pressed against what it hit: it reverses until the world stops reporting the touch
    # (plus a moment) and the body's own retreat further, so it stands clear before the golem decides again. It gives
    # up after a while, or if it backs into something else.
    def begin_back_off(self, touch, order):
        self.backing = (touch, time.time(), self.pose, order.get("body", {}).get("retreat", 0.5), None, order)
        self.drive(-BACK_OFF_SPEED, 0.0)

    def back_off(self):
        touch, began, start, retreat, free_since, order = self.backing
        now = time.time()
        latest = self.contact
        stop = False
        if latest is not None and latest[0] != touch[0] and latest[1] > began:
            stop = True                                       # backed into something else
        elif free_since is None and latest is not None and now - latest[1] > FREE_FOR:
            free_since = now
        if free_since is not None and (start is None or self.pose is None or math.hypot(self.pose[0] - start[0], self.pose[1] - start[1]) >= retreat):
            stop = True
        if now - began > BACK_OFF_AT_MOST:
            stop = True
        if not stop:
            self.backing = (touch, began, start, retreat, free_since, order)
            return
        self.drive(0.0, 0.0)
        self.backing = None
        self.order = None
        self.report_touch(order.get("route", 0), touch, at=start)

    # Where the touch landed on the plane, as the body reckons it: one radius from the centre of the body it believed
    # at the touch, in the direction the shell was pressed. The heading points into what was touched.
    def report_touch(self, route, touch, at=None):
        pose = at or self.pose
        if pose is None:
            return
        radius = (self.order or {}).get("body", {}).get("radius", 0.25) if self.order else 0.25
        heading = normalize(pose[2] + touch[2])
        self.post("touched", {"route": route, "with": touch[0],
                              "x": pose[0] + radius * math.cos(heading), "y": pose[1] + radius * math.sin(heading),
                              "heading": heading, "px": pose[0], "py": pose[1]})

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
