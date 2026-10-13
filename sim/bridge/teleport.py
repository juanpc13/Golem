#!/usr/bin/env python3
"""The lab's teleport lever. ros_gz_bridge (Humble/Fortress) bridges topics, not the world's
services, so putting a body back on its mark goes through this node: a geometry_msgs/PoseStamped
on /sim/teleport whose frame_id names the model becomes a call to Gazebo's /world/<world>/set_pose.
Only a lab lever — a real robot is not teleported, it is carried."""
import math
import re
import subprocess
import sys

import rclpy
from geometry_msgs.msg import PoseStamped
from nav_msgs.msg import Odometry
from rclpy.node import Node
from rclpy.wait_for_message import wait_for_message


class Teleport(Node):
    def __init__(self, world):
        super().__init__("sim_teleport")
        self.world = world
        self.create_subscription(PoseStamped, "/sim/teleport", self.on_pose, 10)
        self.get_logger().info("teleporting models of world '%s' on /sim/teleport" % world)

    def on_pose(self, m):
        name = m.header.frame_id
        if not re.fullmatch(r"[A-Za-z0-9_]+", name):
            self.get_logger().warning("refusing to teleport '%s': not a model name" % name)
            return
        p, q = m.pose.position, m.pose.orientation
        req = 'name: "%s", position: {x: %f, y: %f, z: %f}, orientation: {x: %f, y: %f, z: %f, w: %f}' % (
            name, p.x, p.y, p.z, q.x, q.y, q.z, q.w)
        # tried until Gazebo answers (13-oct-2026: six bodies carried at once after a reset --all timed out at 3 s, Gazebo moved them LATE,
        # and every golem had already woken where its body stood before the jump — metres off, and the formations collided)
        # — but a call that timed out may still have been DONE (13-oct-2026 afternoon: blue was carried onto its place, the first call timed
        # out, Gazebo moved it late, the golem woke there and told it to turn — and the RETRY put it back facing as before, the turn wiped
        # while the golem believed it done): before trying again the body's own odometry is asked, and a body already standing where it
        # was sent, facing as told, is left alone
        answer = ""
        yaw = math.atan2(2.0 * (q.w * q.z + q.x * q.y), 1.0 - 2.0 * (q.y * q.y + q.z * q.z))
        for attempt in range(1, 4):
            r = subprocess.run(
                ["ign", "service", "-s", "/world/%s/set_pose" % self.world,
                 "--reqtype", "ignition.msgs.Pose", "--reptype", "ignition.msgs.Boolean",
                 "--timeout", "6000", "--req", req],
                capture_output=True, text=True)
            answer = (r.stdout or r.stderr).strip()
            if "true" in answer:
                break
            if self.landed(name, p.x, p.y, yaw):
                answer = "landed late (the call timed out, the body stands where it was sent)"
                break
            self.get_logger().warning("%s -> (%.2f, %.2f): try %d: %s" % (name, p.x, p.y, attempt, answer))
        self.get_logger().info("%s -> (%.2f, %.2f): %s" % (name, p.x, p.y, answer))

    def landed(self, name, x, y, yaw):
        """Whether the body's own odometry (the world's truth, bridged) already says it stands where it was sent, facing as told."""
        got, m = wait_for_message(Odometry, self, "/model/%s/odometry" % name, time_to_wait=2.0)
        if not got:
            return False
        o, r = m.pose.pose.position, m.pose.pose.orientation
        heading = math.atan2(2.0 * (r.w * r.z + r.x * r.y), 1.0 - 2.0 * (r.y * r.y + r.z * r.z))
        turn = math.atan2(math.sin(heading - yaw), math.cos(heading - yaw))
        return math.hypot(o.x - x, o.y - y) <= 0.05 and abs(turn) <= 0.05


def main():
    rclpy.init()
    node = Teleport(sys.argv[1] if len(sys.argv) > 1 else "arena")
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
