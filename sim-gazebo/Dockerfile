# Smoke test: can this machine run a physics simulator for the golems?
# Gazebo Fortress is the simulator officially paired with ROS 2 Humble (ros-humble-ros-gz
# brings the simulator, the message types and the ROS <-> Gazebo bridge).
# Headless by design: the physics server only, no GUI — what we measure is physics + robots.
FROM ros:humble-ros-base

RUN apt-get update \
 && apt-get install -y --no-install-recommends ros-humble-ros-gz \
 && rm -rf /var/lib/apt/lists/*

COPY worlds/ /worlds/

# -s = server only (no GUI), -r = run at start, -v 2 = warnings and errors.
CMD ["bash", "-lc", "source /opt/ros/humble/setup.bash && ign gazebo -s -r -v 2 /worlds/arena.sdf"]
