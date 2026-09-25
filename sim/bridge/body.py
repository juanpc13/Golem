#!/usr/bin/env python3
"""The robot: the body's own node, one per body. It knows nothing of maps, routes or peers — it takes ONE order at a
time from its golem and reports what came of it (Juan, 16-sep-2026: "el robot solo es el cuerpo; nosotros le decimos
qué hacer, y cuando termina le dice al actor por un endpoint que ya terminó, para pedir el siguiente print").

Its vocabulary is the robot's BASE ACTIONS, each with an AMOUNT (Juan, 17-sep-2026: "al robot se le dice muy
sencillamente lo que debe moverse hacia adelante, qué tanto debe rotar"). Orders arrive on /golem/<body>/order
(std_msgs/String — the body's words alone, ajuste 55: {"order": 17, "action": "advance", "amount": 1.35, "speed": 2.0};
the order is a TICKET the golem stamped, the only thing the body echoes back), switched on "action":
  {"action": "advance",   "amount": 2.35, "route": 3, ...}   — move forward that many metres
  {"action": "back",      "amount": 0.60, "route": 3, ...}   — move that many metres IN REVERSE
  {"action": "turnLeft",  "amount": 1.57, "route": 3, ...}   — turn in place, counter-clockwise, that many radians
  {"action": "turnRight", "amount": 0.80, "route": 3, ...}   — the same, clockwise
  {"action": "stop"}                    — stand, remembering what it was doing and how much was left (the operator held the golem, or nothing is pending)
  {"action": "stop", "anchor": true}    — stand, and take the world's word for where you are (after a teleport)
  {"action": "continue"}                — take up what was left of what it was doing when it was stopped
A new action replaces whatever the body was doing. The amounts are measured on the body's own odometry — how far it
travelled since the order began, how much it turned — never against a point on the plane: the golem decides the points,
the body only moves. What came of the order goes back on /golem/<body>/result (std_msgs/String), and nothing else — the
body knows nothing of routes, golems or endpoints (Juan, 24-sep-2026: "sólo decir si logré lo que me dijiste que hiciera"):
  {"order": 17, "result": "done"}                                     — the amount done
  {"order": 17, "result": "bumped", "x", "y", "heading", "bearing"}   — where it stood and where on its shell it was pressed
  {"order": 17, "result": "stuck", "reason"}
The body drives /model/<body>/cmd_vel and watches its odometry — the world's truth, or its own wheels' reckoning
anchored once to the truth (a real robot's lot) — and its contact sensor, its bumper. THE BUMPER IS A SWITCH: the moment
it fires the motors stop and the bump is reported — where the body stood, facing which way, and where on its shell it was
pressed. Where that lands on the plane, what it was and what to do about it (back off, go around) are the golem's to say:
it comes as the next order. The body decides nothing.

Usage: body.py <body> <golem-url: unused since ajuste 55, kept for kiosk.sh> [world|wheels]
"""
import json
import math
import sys
import threading
import time

import rclpy
from geometry_msgs.msg import Twist
from nav_msgs.msg import Odometry
from rclpy.node import Node
from ros_gz_interfaces.msg import Contacts
from std_msgs.msg import String

TICK = 0.05                 # s, the servo's beat
MOVE_TIMEOUT = 90.0         # s, a move that takes longer is stuck
TURN_TIMEOUT = 15.0         # s
STALL_AFTER = 3.0           # s without the amount progressing = stuck
PROGRESS = 0.02             # m or rad, progress smaller than this is noise
DONE_WITHIN = 0.02          # m or rad, what is left of the amount when the action counts as done
BACK_OFF_SPEED = 0.4        # m/s, in reverse
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
        self.source = source
        self.truth = None            # (x, y, theta) the world's
        self.pose = None             # what the body believes: the truth, or dead reckoning
        self.anchor = None           # dead reckoning's anchor: (wheels pose, truth pose) at calibration
        self.calibrate = True
        self.contact = None          # (model, time, bearing) the last touch
        self.order = None            # the order being carried out (dict), None while standing
        self.held = None             # what was left of the order it was carrying when told to stop: "continue" takes it up
        self.start = None            # the pose when the order began: the amount is measured from it
        self.turned = 0.0            # rad turned since the order began
        self.prev_theta = 0.0
        self.began = 0.0
        self.best = 0.0              # the most of the amount done so far (stall detection)
        self.improved = 0.0
        self.last_standing_touch = 0.0
        self.pressed = None          # the model the bumper is still pressed against since it was reported: not a new bump until released
        self.released_since = None   # when the bumper last went quiet (no contact message for a moment)
        self.lock = threading.Lock()

        self.cmd = self.create_publisher(Twist, "/model/%s/cmd_vel" % body, 10)
        self.result = self.create_publisher(String, "/golem/%s/result" % body, 10)
        self.create_subscription(String, "/golem/%s/order" % body, self.on_order, 10)
        self.create_subscription(Odometry, "/model/%s/odometry" % body, self.on_truth, 20)
        if source == "wheels":
            self.create_subscription(Odometry, "/model/%s/wheel_odometry" % body, self.on_wheels, 20)
        self.create_subscription(Contacts, "/model/%s/contacts" % body, self.on_contacts, 20)
        self.create_timer(TICK, self.tick)
        self.get_logger().info("body %s: orders on /golem/%s/order, results on /golem/%s/result, pose from %s" % (body, body, body, source))

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
                if self.order is not None:
                    left = dict(self.order)
                    left["amount"] = max(0.0, float(self.order.get("amount", 0.0)) - self.done_so_far())
                    self.held = left
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
                self.get_logger().info("order %s: continuing — %s %.2f left" % (order.get("order"), action, float(order.get("amount", 0.0))))
            if action not in ("advance", "back", "turnLeft", "turnRight"):
                self.get_logger().warning("an action I do not know: %s" % action)
                return
            self.order = order
            self.held = None
            self.begin()
            self.get_logger().info("order %s: %s %.2f %s" % (order.get("order"), action, float(order.get("amount", 0.0)),
                                   "rad" if action.startswith("turn") else "m"))

    # The amount is measured from the pose the order began at; a body that has said nothing yet begins when it does.
    def begin(self):
        self.start = self.pose
        self.turned = 0.0
        self.prev_theta = self.pose[2] if self.pose is not None else 0.0
        self.began = time.time()
        self.best = 0.0
        self.improved = self.began

    # How much of the current order is done: metres travelled from where it began, or radians turned.
    def done_so_far(self):
        if self.order is None or self.start is None or self.pose is None:
            return 0.0
        if self.order["action"] in ("turnLeft", "turnRight"):
            return self.turned
        return self.moved(self.order["action"], self.pose[0], self.pose[1])

    # The metres of a move done so far: the displacement since the order began PROJECTED on the direction it asked — ahead
    # for an advance, behind for a back — never below zero. Not the plain distance from the start: a body that bumped at
    # speed is still sliding forward when the `back` order arrives (22-sep-2026 live: began at (6.11, 5.46), slid to 0.46 m
    # ahead, then backed 0.36 m past the start and was declared stalled, the slide having set the mark to beat).
    def moved(self, action, x, y):
        dx, dy = x - self.start[0], y - self.start[1]
        along = dx * math.cos(self.start[2]) + dy * math.sin(self.start[2])
        return max(0.0, -along if action == "back" else along)

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
                    self.report_bump(0, touch)   # ticket 0: no order carried (25-sep-2026: a third argument here killed the node)
                return
            if fresh and touch[1] > self.began:
                self.drive(0.0, 0.0)          # the bumper fired: the motors stop at once
                self.order = None
                self.pressed = touch[0]
                self.report_bump(order.get("order", 0), touch)
                return
            if self.pose is None:
                return
            if self.start is None:
                self.begin()                  # the first pose since the order came: the amount is measured from here
            x, y, theta = self.pose
            action = order["action"]
            amount = float(order.get("amount", 0.0))
            ticket = order.get("order", 0)
            if action in ("turnLeft", "turnRight"):
                # in place, the way the golem said (left: counter-clockwise), until the radians told are turned
                self.turned += abs(normalize(theta - self.prev_theta))
                self.prev_theta = theta
                left = amount - self.turned
                if left <= DONE_WITHIN:
                    self.drive(0.0, 0.0)
                    self.done({"order": ticket, "result": "done"})
                    return
                if now - self.began > TURN_TIMEOUT:
                    self.drive(0.0, 0.0)
                    self.done({"order": ticket, "result": "stuck", "reason": "turn timeout"})
                    return
                sign = 1.0 if action == "turnLeft" else -1.0
                self.drive(0.0, sign * max(0.25, min(1.5, 3.0 * left)))
                return
            # a move, forward or in reverse: straight, holding the heading it began with, until the metres told are travelled
            travelled = self.moved(action, x, y)
            left = amount - travelled
            if left <= DONE_WITHIN:
                self.drive(0.0, 0.0)
                self.done({"order": ticket, "result": "done"})
                return
            if travelled > self.best + PROGRESS:
                self.best = travelled
                self.improved = now
            elif now - self.improved > STALL_AFTER:
                self.drive(0.0, 0.0)
                self.get_logger().warning("order %s: %s stalled — travelled %.3f of %.3f (best %.3f), began at (%.2f, %.2f), now at (%.2f, %.2f)"
                                          % (ticket, action, travelled, amount, self.best, self.start[0], self.start[1], x, y))
                self.done({"order": ticket, "result": "stuck", "reason": "stalled: no progress for 3 s"})
                return
            if now - self.began > MOVE_TIMEOUT:
                self.drive(0.0, 0.0)
                self.done({"order": ticket, "result": "stuck", "reason": "timeout"})
                return
            angular = max(-1.0, min(1.0, 2.0 * normalize(self.start[2] - theta)))   # hold the heading the move began with
            if action == "back":
                self.drive(-min(BACK_OFF_SPEED, 1.0 * left + 0.1), angular)
                return
            cruise = order.get("speed", 1.0)
            self.drive(min(cruise, 1.5 * left + 0.15), angular)

    # What a bumper knows, and no more: where the body stands, facing which way, and where on its shell it was pressed —
    # the bearing, radians from the direction it faces (0 the nose, +pi/2 the left flank). Where the touch landed on the
    # plane is the golem's to reckon from the body it declared (18-sep-2026). What the world calls the thing touched is
    # logged here and told to nobody.
    def report_bump(self, ticket, touch):
        pose = self.pose
        if pose is None:
            return
        self.get_logger().info("order %s: bumped %s, bearing %.2f" % (ticket, touch[0], touch[2]))
        self.say({"order": ticket, "result": "bumped", "x": pose[0], "y": pose[1], "heading": pose[2], "bearing": touch[2]})

    def done(self, report):
        self.order = None
        self.say(report)

    def drive(self, linear, angular):
        t = Twist()
        t.linear.x = float(linear)
        t.angular.z = float(angular)
        self.cmd.publish(t)

    # The body's only word back: the result of the order, on its own topic. Whoever listens, listens.
    def say(self, report):
        m = String()
        m.data = json.dumps(report)
        self.result.publish(m)


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
