#!/usr/bin/env python3
"""The lab's teleport lever. ros_gz_bridge (Humble/Fortress) bridges topics, not the world's
services, so putting a body back on its mark goes through this node: a geometry_msgs/PoseStamped
on /sim/teleport whose frame_id names the model becomes a call to Gazebo's /world/<world>/set_pose.
Only a lab lever — a real robot is not teleported, it is carried."""
import re
import subprocess
import sys

import rclpy
from geometry_msgs.msg import PoseStamped
from rclpy.node import Node


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
        r = subprocess.run(
            ["ign", "service", "-s", "/world/%s/set_pose" % self.world,
             "--reqtype", "ignition.msgs.Pose", "--reptype", "ignition.msgs.Boolean",
             "--timeout", "3000", "--req", req],
            capture_output=True, text=True)
        self.get_logger().info("%s -> (%.2f, %.2f): %s" % (name, p.x, p.y, (r.stdout or r.stderr).strip()))


def main():
    rclpy.init()
    node = Teleport(sys.argv[1] if len(sys.argv) > 1 else "arena")
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
