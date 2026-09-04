#!/usr/bin/env python3
"""Builds the Gazebo world from the floor plan: plan.json -> arena.sdf (+ bridge.args).

Reality is generated here, once, when the sim image is built. Places become a floor with walls
along their edges — minus the boundaries declared open, minus a gap at every door (a flat
threshold marks it on the floor). Whatever is not a place becomes a solid block. Each body is a
differential-drive robot (round chassis, two wheels, two casters) with a contact sensor on its
chassis, a DiffDrive plugin listening on /model/<name>/cmd_vel and an OdometryPublisher giving
its REAL pose on /model/<name>/odometry (ground truth, not integrated wheels). Obstacles are
placed as declared.

The golems' map (release map_v1 in their journal) mirrors places, doors and openings — and knows
nothing of the obstacles. That is the point: when the simulator reports a body touching a crate,
the golem has met a real collision its map never contemplated.

Usage: build_world.py plan.json out_dir   -> writes out_dir/arena.sdf and out_dir/bridge.args
"""
import json
import os
import sys

WALL_T, WALL_H = 0.15, 0.5   # wall thickness and height (m)
DOOR_GAP = 1.4               # a door is a gap this wide (walls overlap half a thickness: ~1.25 clear), centered on the door point
BODY_R = 0.25                # chassis radius: the body's footprint (0.5 m across)
EPS = 1e-6

WALL_COLOR = "0.82 0.84 0.90"
BLOCK_COLOR = "0.20 0.21 0.30"
FLOOR_COLOR = "0.80 0.80 0.76"
THRESHOLD_COLOR = "0.87 0.65 0.39"


def num(v):
    s = "%.3f" % v
    return s.rstrip("0").rstrip(".") or "0"


def overlaps(a0, a1, b0, b1):
    return min(a1, b1) - max(a0, b0) > EPS


def edges(p):
    x0, y0, x1, y1 = p["x"], p["y"], p["x"] + p["w"], p["y"] + p["h"]
    return {"s": (x0, y0, x1, y0), "n": (x0, y1, x1, y1), "w": (x0, y0, x0, y1), "e": (x1, y0, x1, y1)}


def is_vertical(edge):
    return abs(edge[0] - edge[2]) < EPS


def is_open(edge, place, places, opens):
    """An edge is open when a neighbour joined by an opening shares that very edge."""
    x0, y0, x1, y1 = edge
    vertical = is_vertical(edge)
    for o in opens:
        if place not in (o["a"], o["b"]):
            continue
        other = o["b"] if o["a"] == place else o["a"]
        q = next((q for q in places if q["name"] == other), None)
        if q is None:
            continue
        if vertical and (abs(q["x"] - x0) < EPS or abs(q["x"] + q["w"] - x0) < EPS) \
                and overlaps(y0, y1, q["y"], q["y"] + q["h"]):
            return True
        if not vertical and (abs(q["y"] - y0) < EPS or abs(q["y"] + q["h"] - y0) < EPS) \
                and overlaps(x0, x1, q["x"], q["x"] + q["w"]):
            return True
    return False


def cut_doors(edge, doors):
    """The pieces of an edge that remain wall once every door on it is cut out."""
    x0, y0, x1, y1 = edge
    vertical = is_vertical(edge)
    lo, hi = (y0, y1) if vertical else (x0, x1)

    def on_edge(d):
        if vertical:
            return abs(d["x"] - x0) < EPS and y0 + EPS < d["y"] < y1 - EPS
        return abs(d["y"] - y0) < EPS and x0 + EPS < d["x"] < x1 - EPS

    gaps = sorted((d["y"] if vertical else d["x"]) for d in doors if on_edge(d))
    cursor = lo
    for g in gaps:
        a = max(cursor, g - DOOR_GAP / 2)
        if a > cursor + EPS:
            yield (x0, cursor, x0, a) if vertical else (cursor, y0, a, y0)
        cursor = min(hi, g + DOOR_GAP / 2)
    if hi > cursor + EPS:
        yield (x0, cursor, x0, hi) if vertical else (cursor, y0, hi, y0)


def walls(places, doors, opens):
    """Every wall piece, named after the place and side it belongs to; a wall shared by two
    places is emitted once (the first place to claim it names it)."""
    seen = set()
    for p in places:
        for side, edge in edges(p).items():
            if is_open(edge, p["name"], places, opens):
                continue
            pieces = list(cut_doors(edge, doors))
            for k, s in enumerate(pieces):
                key = tuple(round(v, 3) for v in s)
                if key in seen:
                    continue
                seen.add(key)
                name = "wall_%s_%s" % (p["name"], side) + ("_%d" % (k + 1) if len(pieces) > 1 else "")
                yield name, s


def blocks(places):
    """The cells of the plan's grid that lie in no place: solid."""
    xs = sorted({v for p in places for v in (p["x"], p["x"] + p["w"])})
    ys = sorted({v for p in places for v in (p["y"], p["y"] + p["h"])})
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            cx, cy = (xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2
            if not any(p["x"] <= cx <= p["x"] + p["w"] and p["y"] <= cy <= p["y"] + p["h"] for p in places):
                yield (xs[i], ys[j], xs[i + 1], ys[j + 1])


def box(name, cx, cy, sx, sy, h, color, collide=True, z=None):
    z = h / 2 if z is None else z
    geometry = "<geometry><box><size>%s %s %s</size></box></geometry>" % (num(sx), num(sy), num(h))
    collision = '<collision name="collision">%s</collision>' % geometry if collide else ""
    return """    <model name="%s"><static>true</static><pose>%s %s %s 0 0 0</pose>
      <link name="link">%s
        <visual name="visual">%s<material><ambient>%s 1</ambient><diffuse>%s 1</diffuse></material></visual>
      </link>
    </model>""" % (name, num(cx), num(cy), num(z), collision, geometry, color, color)


def wall(name, seg):
    x0, y0, x1, y1 = seg
    if is_vertical(seg):
        return box(name, x0, (y0 + y1) / 2, WALL_T, abs(y1 - y0) + WALL_T, WALL_H, WALL_COLOR)
    return box(name, (x0 + x1) / 2, y0, abs(x1 - x0) + WALL_T, WALL_T, WALL_H, WALL_COLOR)


def threshold(i, d, places):
    vertical = any(abs(p["x"] - d["x"]) < EPS or abs(p["x"] + p["w"] - d["x"]) < EPS for p in places)
    sx, sy = (0.2, DOOR_GAP) if vertical else (DOOR_GAP, 0.2)
    return box("threshold_%d" % i, d["x"], d["y"], sx, sy, 0.01, THRESHOLD_COLOR, collide=False, z=0.005)


def floor(max_x, max_y):
    return """    <model name="ground_plane"><static>true</static>
      <link name="link">
        <collision name="collision"><geometry><plane><normal>0 0 1</normal><size>60 60</size></plane></geometry></collision>
        <visual name="visual"><pose>%s %s 0 0 0 0</pose><geometry><plane><normal>0 0 1</normal><size>%s %s</size></plane></geometry>
          <material><ambient>%s 1</ambient><diffuse>%s 1</diffuse></material></visual>
      </link>
    </model>""" % (num(max_x / 2), num(max_y / 2), num(max_x), num(max_y), FLOOR_COLOR, FLOOR_COLOR)


def wheel(side, y):
    return """      <link name="%s_wheel">
        <pose>0 %s 0.10 -1.5707 0 0</pose>
        <inertial><mass>0.3</mass><inertia><ixx>0.00084</ixx><iyy>0.00084</iyy><izz>0.0015</izz><ixy>0</ixy><ixz>0</ixz><iyz>0</iyz></inertia></inertial>
        <collision name="collision"><geometry><cylinder><radius>0.10</radius><length>0.06</length></cylinder></geometry>
          <surface><friction><ode><mu>1.5</mu><mu2>1.5</mu2></ode></friction></surface></collision>
        <visual name="visual"><geometry><cylinder><radius>0.10</radius><length>0.06</length></cylinder></geometry>
          <material><ambient>0.15 0.15 0.15 1</ambient><diffuse>0.15 0.15 0.15 1</diffuse></material></visual>
      </link>""" % (side, num(y))


def caster(where, x):
    return """      <link name="%s_caster">
        <pose>%s 0 0.052 0 0 0</pose>
        <inertial><mass>0.1</mass><inertia><ixx>0.0001</ixx><iyy>0.0001</iyy><izz>0.0001</izz><ixy>0</ixy><ixz>0</ixz><iyz>0</iyz></inertia></inertial>
        <collision name="collision"><geometry><sphere><radius>0.05</radius></sphere></geometry>
          <surface><friction><ode><mu>0</mu><mu2>0</mu2></ode></friction></surface></collision>
        <visual name="visual"><geometry><sphere><radius>0.05</radius></sphere></geometry></visual>
      </link>""" % (where, num(x))


def body(b):
    n, c = b["name"], b["color"]
    return """
    <!-- %s: a round differential-drive body; the chassis carries the contact sensor -->
    <model name="%s">
      <pose>%s %s 0 0 0 %s</pose>
      <link name="chassis">
        <pose>0 0 0.20 0 0 0</pose>
        <inertial><mass>3</mass><inertia><ixx>0.053</ixx><iyy>0.053</iyy><izz>0.094</izz><ixy>0</ixy><ixz>0</ixz><iyz>0</iyz></inertia></inertial>
        <collision name="chassis_collision"><geometry><cylinder><radius>%s</radius><length>0.16</length></cylinder></geometry></collision>
        <visual name="chassis_visual"><geometry><cylinder><radius>%s</radius><length>0.16</length></cylinder></geometry>
          <material><ambient>%s 1</ambient><diffuse>%s 1</diffuse></material></visual>
        <visual name="nose"><pose>0.16 0 0.10 0 0 0</pose><geometry><box><size>0.14 0.10 0.04</size></box></geometry>
          <material><ambient>0.95 0.95 0.95 1</ambient><diffuse>0.95 0.95 0.95 1</diffuse></material></visual>
        <sensor name="bumper" type="contact">
          <contact><collision>chassis_collision</collision><topic>/model/%s/contacts</topic></contact>
          <always_on>1</always_on>
          <update_rate>20</update_rate>
        </sensor>
      </link>
%s
%s
%s
%s
      <joint name="left_wheel_joint" type="revolute"><parent>chassis</parent><child>left_wheel</child>
        <axis><xyz>0 0 1</xyz><limit><lower>-1.79769e+308</lower><upper>1.79769e+308</upper></limit></axis></joint>
      <joint name="right_wheel_joint" type="revolute"><parent>chassis</parent><child>right_wheel</child>
        <axis><xyz>0 0 1</xyz><limit><lower>-1.79769e+308</lower><upper>1.79769e+308</upper></limit></axis></joint>
      <joint name="front_caster_joint" type="ball"><parent>chassis</parent><child>front_caster</child></joint>
      <joint name="rear_caster_joint" type="ball"><parent>chassis</parent><child>rear_caster</child></joint>
      <plugin filename="ignition-gazebo-diff-drive-system" name="ignition::gazebo::systems::DiffDrive">
        <left_joint>left_wheel_joint</left_joint>
        <right_joint>right_wheel_joint</right_joint>
        <wheel_separation>0.40</wheel_separation>
        <wheel_radius>0.10</wheel_radius>
        <topic>/model/%s/cmd_vel</topic>
        <odom_topic>/model/%s/wheel_odometry</odom_topic>
        <odom_publish_frequency>2</odom_publish_frequency>
        <max_linear_acceleration>4</max_linear_acceleration>
        <min_linear_acceleration>-4</min_linear_acceleration>
        <max_angular_acceleration>12</max_angular_acceleration>
        <min_angular_acceleration>-12</min_angular_acceleration>
      </plugin>
      <plugin filename="ignition-gazebo-odometry-publisher-system" name="ignition::gazebo::systems::OdometryPublisher">
        <odom_frame>world</odom_frame>
        <robot_base_frame>%s</robot_base_frame>
        <odom_topic>/model/%s/odometry</odom_topic>
        <odom_publish_frequency>30</odom_publish_frequency>
        <dimensions>2</dimensions>
      </plugin>
    </model>""" % (n, n, num(b["x"]), num(b["y"]), num(b.get("yaw", 0)), num(BODY_R), num(BODY_R), c, c, n,
                   wheel("left", 0.20), wheel("right", -0.20), caster("front", 0.17), caster("rear", -0.17),
                   n, n, n, n)


WORLD = """<?xml version="1.0" ?>
<!-- GENERATED by build_world.py from plan.json: edit the plan, not this file. -->
<sdf version="1.8">
  <world name="%(world)s">
    <physics name="1ms" type="ignored">
      <max_step_size>0.001</max_step_size>
      <real_time_factor>1.0</real_time_factor>
    </physics>
    <plugin filename="ignition-gazebo-physics-system" name="ignition::gazebo::systems::Physics"/>
    <plugin filename="ignition-gazebo-user-commands-system" name="ignition::gazebo::systems::UserCommands"/>
    <plugin filename="ignition-gazebo-scene-broadcaster-system" name="ignition::gazebo::systems::SceneBroadcaster"/>
    <plugin filename="ignition-gazebo-contact-system" name="ignition::gazebo::systems::Contact"/>
    <scene>
      <ambient>0.7 0.7 0.7 1</ambient>
      <background>0.08 0.09 0.13 1</background>
      <shadows>false</shadows>
    </scene>

    <!-- The kiosk's picture: one camera straight above the floor, +x to the right, +y up -->
    <gui fullscreen="0">
      <plugin filename="GzScene3D" name="3D View">
        <ignition-gui>
          <title>3D View</title>
          <property type="bool" key="showTitleBar">false</property>
          <property type="string" key="state">docked</property>
        </ignition-gui>
        <engine>ogre2</engine>
        <scene>scene</scene>
        <ambient_light>0.7 0.7 0.7</ambient_light>
        <background_color>0.08 0.09 0.13</background_color>
        <camera_pose>%(cx)s %(cy)s %(cam_z)s 0 1.5707 1.5707</camera_pose>
      </plugin>
      <plugin filename="WorldStats" name="World stats">
        <ignition-gui>
          <title>World stats</title>
          <property type="bool" key="showTitleBar">false</property>
          <property type="bool" key="resizable">false</property>
          <property type="double" key="height">110</property>
          <property type="double" key="width">290</property>
          <property type="double" key="z">1</property>
          <property type="string" key="state">floating</property>
          <anchors target="3D View">
            <line own="right" target="right"/>
            <line own="bottom" target="bottom"/>
          </anchors>
        </ignition-gui>
        <sim_time>true</sim_time>
        <real_time>true</real_time>
        <real_time_factor>true</real_time_factor>
        <iterations>false</iterations>
        <topic>/world/%(world)s/stats</topic>
      </plugin>
    </gui>

    <light type="directional" name="sun">
      <cast_shadows>false</cast_shadows>
      <pose>0 0 10 0 0 0</pose>
      <diffuse>0.9 0.9 0.9 1</diffuse>
      <specular>0.1 0.1 0.1 1</specular>
      <direction>-0.3 0.2 -0.9</direction>
    </light>

%(models)s
  </world>
</sdf>
"""


def bridge_args(bodies):
    """One ros_gz_bridge mapping per body topic: cmd_vel in, odometry and contacts out."""
    args = []
    for b in bodies:
        n = b["name"]
        args.append("/model/%s/cmd_vel@geometry_msgs/msg/Twist]ignition.msgs.Twist" % n)
        args.append("/model/%s/odometry@nav_msgs/msg/Odometry[ignition.msgs.Odometry" % n)
        args.append("/model/%s/contacts@ros_gz_interfaces/msg/Contacts[ignition.msgs.Contacts" % n)
    return " ".join(args)


def main(plan_path, out_dir):
    with open(plan_path) as f:
        plan = json.load(f)
    places, doors, opens = plan["places"], plan["doors"], plan["opens"]
    max_x = max(p["x"] + p["w"] for p in places)
    max_y = max(p["y"] + p["h"] for p in places)

    models = [floor(max_x, max_y)]
    for i, cell in enumerate(blocks(places)):
        x0, y0, x1, y1 = cell
        models.append(box("block_%d" % (i + 1), (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0, WALL_H, BLOCK_COLOR))
    for name, seg in walls(places, doors, opens):
        models.append(wall(name, seg))
    for i, d in enumerate(doors):
        models.append(threshold(i + 1, d, places))
    for o in plan.get("obstacles", []):
        models.append(box(o["name"], o["x"], o["y"], o["w"], o["d"], o["h"], o["color"]))
    for b in plan["bodies"]:
        models.append(body(b))

    sdf = WORLD % {
        "world": plan.get("world", "arena"),
        "cx": num(max_x / 2), "cy": num(max_y / 2),
        "cam_z": num(max(max_x, max_y) * 0.66),   # the GUI's lens on a square screen: the whole floor plus a margin
        "models": "\n".join(models),
    }
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "arena.sdf"), "w") as f:
        f.write(sdf)
    with open(os.path.join(out_dir, "bridge.args"), "w") as f:
        f.write(bridge_args(plan["bodies"]) + "\n")
    print("world %s: %d places, %d walls, %d obstacles, %d bodies -> %s" % (
        plan.get("world", "arena"), len(places), sum(1 for _ in walls(places, doors, opens)),
        len(plan.get("obstacles", [])), len(plan["bodies"]), os.path.join(out_dir, "arena.sdf")))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
