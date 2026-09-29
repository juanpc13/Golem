#!/usr/bin/env python3
"""The lab's levers over the running world: put an obstacle in it, take it away, put the camera back.

Reality is generated when the image is built (world/build_world.py), so an obstacle used to mean a
rebuild. Gazebo's world services accept a model at runtime, so the testing spots are pressed into
place instead: the west corridor and the east corridor (1.5 m wide: a 0.7 crate closes them to a
0.5 body), the middle of the central hall (3 m wide: the body can feel its way past), and the big
one, as wide as that hall (nothing gets through: the shortcut is shut). The GUI's camera answers a
service too, so a view dragged out of place goes back to the one the kiosk opens with.

The golems' map knows nothing of these crates — that is the point (the same as the plan's obstacles):
what the simulator reports as a touch is a real collision their map never contemplated.

The orders arrive as ROS topics, like teleport.py's do, so the kiosk needs no port of its own: the
buttons' page (noVNC's index, on :6080) speaks to rosbridge (:9090), which the golems already use.
  /sim/crate  (std_msgs/String)  west | center | east | big | clear
  /sim/floor  (std_msgs/String)  open | close — every inner wall, block and door mark taken out for the choreographies, and put back
  /sim/view   (std_msgs/String)  top
  /sim/crates (std_msgs/String)  what stands there now, told every couple of seconds: "west,big,floor:closed"
A lab lever, like teleport.py — nothing here decides anything about the golems.

Usage: crates.py [world]
"""
import math
import re
import subprocess
import sys

import rclpy
from rclpy.node import Node
from std_msgs.msg import String

HEIGHT = 0.5                    # as tall as the plan's crates and its walls
COLOR = "0.72 0.5 0.25"
BIG_COLOR = "0.66 0.36 0.16"    # the big one reads darker: it is the one that shuts the hall
NAME = "crate_%s"               # the model's name in the world: what a collision is reported as
WORLD_SDF = "/world/arena.sdf"

# THE OPEN FLOOR (29-sep-2026; Juan: "remover todo lo que hay al centro y solo dejar un espacio libre para que los robots puedan
# hacer estas coreografías… aún veo muros de las paredes de los cuartos, limpiarlos también"): EVERY inner piece of the plan —
# the walls between the rooms, the corridors and the halls, the solid blocks, the door marks on the floor — read from the world
# file itself, by position: a wall standing on the arena's outermost lines is the perimeter and stays. Opened, the floor is one
# clear space inside the perimeter; closed, every piece comes back exactly as the world file built it. A LAB lever, never domain:
# the golems' map still knows its walls and blocks.
PIECE = re.compile(r'<model name="((?:wall|block|threshold)_[^"]+)">(.*?)</model>', re.S)
FLOOR_SENTINEL = "wall_center_w"   # standing = the floor is closed"

# The spots, named. Nothing outside this table can ever be placed: an order names a key, it never
# carries a coordinate. A spot says where its crate stands, how wide and deep it is, and which other
# spot it cannot share the floor with — two crates on the same point would sit inside each other.
SPOTS = {
    #          x       y     w    d    colour     excludes
    "west":   (0.75,  5.5,  0.7, 0.7, COLOR,     None),       # the west corridor: closed to a body
    "center": (5.5,   5.5,  0.7, 0.7, COLOR,     "big"),      # the middle of the hall: passable on either side
    "east":   (10.25, 5.5,  0.7, 0.7, COLOR,     None),       # the east corridor: closed to a body
    "big":    (5.5,   5.5,  2.8, 0.7, BIG_COLOR, "center"),   # the hall wall to wall, 0.1 spare a side: nothing gets through
}


def model_sdf(name, x, y, w, d, color):
    """A crate as SDF. XML attributes are single-quoted: a double quote would close the protobuf
    text-format string the service takes its request in (verified in the running world, 10-sep)."""
    size = "%s %s %s" % (w, d, HEIGHT)
    return (
        "<sdf version='1.6'><model name='%s'><static>true</static>"
        "<pose>%s %s %s 0 0 0</pose><link name='link'>"
        "<collision name='collision'><geometry><box><size>%s</size></box></geometry></collision>"
        "<visual name='visual'><geometry><box><size>%s</size></box></geometry>"
        "<material><ambient>%s 1</ambient><diffuse>%s 1</diffuse></material></visual>"
        "</link></model></sdf>" % (name, x, y, HEIGHT / 2, size, size, color, color))


def kiosk_camera():
    """The view the kiosk opens with, read from the world it is looking at: the <camera_pose> the plan
    wrote (x y z roll pitch yaw). Read from the world, not hardcoded, so a floor of another size
    still gets its own view back."""
    try:
        with open(WORLD_SDF) as f:
            m = re.search(r"<camera_pose>([^<]+)</camera_pose>", f.read())
        if m:
            n = [float(v) for v in m.group(1).split()]
            if len(n) == 6:
                return n
    except OSError:
        pass
    return [5.5, 5.5, 7.26, 0.0, math.pi / 2, math.pi / 2]   # the arena's, should the world not say


class Levers(Node):
    def __init__(self, world):
        super().__init__("sim_levers")
        self.world = world
        self.create_subscription(String, "/sim/crate", self.on_crate, 10)
        self.create_subscription(String, "/sim/floor", self.on_floor, 10)
        self.create_subscription(String, "/sim/view", self.on_view, 10)
        self.telling = self.create_publisher(String, "/sim/crates", 10)
        # Asking the world what stands there spawns `ign model --list` — a whole ruby process, ~1 s of CPU
        # and 600 MB — so it is asked seldom: a slow heartbeat, plus at once after every order and whenever
        # a page asks ("tell"), which is how a freshly opened kiosk learns the floor without waiting.
        self.create_timer(10.0, self.tell_what_stands_there)
        self.floor = self.inner_pieces()
        self.get_logger().info("the open floor takes out %d inner piece(s), the perimeter stays" % len(self.floor))
        self.get_logger().info(
            "levers over world '%s': /sim/crate (%s | clear), /sim/floor (open | close), /sim/view (top); telling /sim/crates"
            % (world, " | ".join(SPOTS)))

    # ---- the world's services ----

    def call(self, service, reqtype, req):
        """One of the running world's (or the GUI's) services. The argument list never goes through a shell."""
        r = subprocess.run(
            ["ign", "service", "-s", service,
             "--reqtype", "ignition.msgs." + reqtype, "--reptype", "ignition.msgs.Boolean",
             "--timeout", "3000", "--req", req],
            capture_output=True, text=True)
        return "data: true" in (r.stdout or "")

    def remove(self, name):
        return self.call("/world/%s/remove" % self.world, "Entity", 'name: "%s", type: MODEL' % name)

    def place(self, spot):
        """Put the crate of a spot in the world. Ordered twice, it is put back where it belongs (a body
        may have shoved it): the old one goes first, so the name stays free. The spot it excludes goes
        too — the big crate and the small one share a point, so only one of them stands there."""
        x, y, w, d, color, excludes = SPOTS[spot]
        name = NAME % spot
        self.remove(name)
        if excludes:
            self.remove(NAME % excludes)
        return self.call("/world/%s/create" % self.world, "EntityFactory",
                         'sdf: "%s", name: "%s", allow_renaming: false'
                         % (model_sdf(name, x, y, w, d, color), name))

    def placed(self):
        """Which spots hold a crate right now, and whether the floor at the centre is open or closed — asked of the
        world itself: this node keeps no state of its own, so what it tells is the truth even after a restart."""
        r = subprocess.run(["ign", "model", "--list"], capture_output=True, text=True)
        out = r.stdout or ""
        names = set(re.findall(r"crate_(\w+)", out))
        floor = "closed" if re.search(r"\b%s\b" % FLOOR_SENTINEL, out) else "open"
        return sorted(s for s in SPOTS if s in names) + ["floor:" + floor]

    # ---- the floor at the centre ----

    def inner_pieces(self):
        """Every wall, block and door mark of the plan that is NOT the perimeter, by name, from the world file: a
        vertical wall on the outermost x, or a horizontal one on the outermost y, is the perimeter."""
        with open(WORLD_SDF, encoding="utf-8") as f:
            world = f.read()
        pieces = []
        for m in PIECE.finditer(world):
            name, body = m.groups()
            pose = re.search(r"<pose>([-\d.]+) ([-\d.]+) ", body)
            size = re.search(r"<size>([-\d.]+) ([-\d.]+) ", body)
            if not pose or not size:
                continue
            pieces.append((name, float(pose.group(1)), float(pose.group(2)), float(size.group(1)), float(size.group(2))))
        xs = [x for name, x, y, sx, sy in pieces if name.startswith("wall_") and sy > sx]   # the vertical walls' x
        ys = [y for name, x, y, sx, sy in pieces if name.startswith("wall_") and sx > sy]   # the horizontal walls' y

        def perimeter(piece):
            name, x, y, sx, sy = piece
            if not name.startswith("wall_"):
                return False
            if sy > sx:
                return abs(x - min(xs)) < 1e-6 or abs(x - max(xs)) < 1e-6
            return abs(y - min(ys)) < 1e-6 or abs(y - max(ys)) < 1e-6

        return [piece[0] for piece in pieces if not perimeter(piece)]

    def piece_sdf(self, name):
        """A piece of the plan as the world file built it, wrapped as its own SDF; the attributes single-quoted
        for the service's text-format request (see model_sdf)."""
        with open(WORLD_SDF, encoding="utf-8") as f:
            world = f.read()
        m = re.search(r'<model name="%s">.*?</model>' % re.escape(name), world, re.S)
        if not m:
            return None
        # one line: a newline inside the request's quoted string cuts it short (29-sep-2026: "Unable to read SDF string")
        return "<sdf version='1.6'>%s</sdf>" % re.sub(r"\s+", " ", m.group(0)).replace('"', "'")

    def open_floor(self):
        """Every inner piece taken out of the world: one clear floor inside the perimeter."""
        gone = [name for name in self.floor if self.remove(name)]
        return len(gone)

    def close_floor(self):
        """Every piece put back as the plan built it: a piece still standing is left alone."""
        back = 0
        for name in self.floor:
            sdf = self.piece_sdf(name)
            if sdf is None:
                self.get_logger().warning("the world file has no piece named '%s'" % name)
                continue
            self.remove(name)   # a piece still standing keeps its name free
            if self.call("/world/%s/create" % self.world, "EntityFactory",
                         'sdf: "%s", name: "%s", allow_renaming: false' % (sdf, name)):
                back += 1
        return back

    def top_view(self):
        """Put the GUI's camera back above the floor. The service takes a quaternion, the plan writes
        roll-pitch-yaw: converted here."""
        x, y, z, roll, pitch, yaw = kiosk_camera()
        cr, sr = math.cos(roll / 2), math.sin(roll / 2)
        cp, sp = math.cos(pitch / 2), math.sin(pitch / 2)
        cy, sy = math.cos(yaw / 2), math.sin(yaw / 2)
        q = (sr * cp * cy - cr * sp * sy,   # x
             cr * sp * cy + sr * cp * sy,   # y
             cr * cp * sy - sr * sp * cy,   # z
             cr * cp * cy + sr * sp * sy)   # w
        return self.call("/gui/move_to/pose", "GUICamera",
                         "pose: {position: {x: %f, y: %f, z: %f}, orientation: {x: %f, y: %f, z: %f, w: %f}}"
                         % (x, y, z, q[0], q[1], q[2], q[3]))

    # ---- what the buttons ask for ----

    def on_crate(self, m):
        order = (m.data or "").strip()
        if order == "tell":
            pass   # a page just opened and wants to know the floor: told below
        elif order == "clear":
            for s in SPOTS:
                self.remove(NAME % s)
            self.get_logger().info("cleared every crate")
        elif order in SPOTS:
            self.get_logger().info("crate at the %s: %s" % (order, "in" if self.place(order) else "REFUSED"))
        else:
            self.get_logger().warning("no spot named '%s'" % order)
        self.tell_what_stands_there()

    def on_floor(self, m):
        order = (m.data or "").strip()
        if order == "open":
            self.get_logger().info("the floor opened: %d piece(s) taken out" % self.open_floor())
        elif order == "close":
            self.get_logger().info("the floor closed: %d piece(s) back" % self.close_floor())
        elif order != "tell":
            self.get_logger().warning("no floor order named '%s'" % order)
        self.tell_what_stands_there()

    def on_view(self, m):
        if (m.data or "").strip() == "top":
            self.get_logger().info("camera back above the floor: %s" % self.top_view())
        else:
            self.get_logger().warning("no view named '%s'" % m.data)

    def tell_what_stands_there(self):
        self.telling.publish(String(data=",".join(self.placed())))


def main():
    rclpy.init()
    node = Levers(sys.argv[1] if len(sys.argv) > 1 else "arena")
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
