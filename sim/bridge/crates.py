#!/usr/bin/env python3
"""The lab's crate lever: put an obstacle in the world while it runs, and take it away.

Reality is generated when the image is built (world/build_world.py), so an obstacle used to mean a
rebuild. Gazebo's world services accept a model at runtime, so the testing spots are pressed into
place instead: the west corridor and the east corridor (1.5 m wide: a 0.7 crate closes them to a
0.5 body), the middle of the central hall (3 m wide: the body can feel its way past), and the big
one, as wide as that hall (nothing gets through: the shortcut is shut).

The golems' map knows nothing of these crates — that is the point (the same as the plan's obstacles):
what the simulator reports as a touch is a real collision their map never contemplated.

This serves the kiosk page too: the buttons, with Gazebo's picture (noVNC) below them. A lab lever,
like teleport.py — nothing here decides anything about the golems.

Usage: crates.py [world] [port]
"""
import json
import re
import subprocess
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HEIGHT = 0.5                    # as tall as the plan's crates and its walls
COLOR = "0.72 0.5 0.25"
BIG_COLOR = "0.66 0.36 0.16"    # the big one reads darker: it is the one that shuts the hall
NAME = "crate_%s"               # the model's name in the world: what a collision is reported as

# The spots, named. Nothing outside this table can ever be placed: a request selects a key, it never
# carries a coordinate. A spot says where its crate stands, how wide and deep it is, and which other
# spot it cannot share the floor with — two crates on the same point would sit inside each other.
SPOTS = {
    #          x       y     w    d    colour     excludes
    "west":   (0.75,  5.5,  0.7, 0.7, COLOR,     None),       # the west corridor: closed to a body
    "center": (5.5,   5.5,  0.7, 0.7, COLOR,     "big"),      # the middle of the hall: passable on either side
    "east":   (10.25, 5.5,  0.7, 0.7, COLOR,     None),       # the east corridor: closed to a body
    "big":    (5.5,   5.5,  2.8, 0.7, BIG_COLOR, "center"),   # the hall wall to wall, 0.1 spare a side: nothing gets through
}

world = sys.argv[1] if len(sys.argv) > 1 else "arena"
port = int(sys.argv[2]) if len(sys.argv) > 2 else 6081


def call(service, reqtype, req):
    """One of Gazebo's world services. The argument list never goes through a shell."""
    r = subprocess.run(
        ["ign", "service", "-s", "/world/%s/%s" % (world, service),
         "--reqtype", "ignition.msgs." + reqtype, "--reptype", "ignition.msgs.Boolean",
         "--timeout", "3000", "--req", req],
        capture_output=True, text=True)
    return "data: true" in (r.stdout or "")


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


def remove(name):
    return call("remove", "Entity", 'name: "%s", type: MODEL' % name)


def place(spot):
    """Put the crate of a spot in the world. Pressed twice, it is put back where it belongs (a body
    may have shoved it): the old one goes first, so the name stays free. The spot it excludes goes
    too — the big crate and the small one share a point, so only one of them stands there."""
    x, y, w, d, color, excludes = SPOTS[spot]
    name = NAME % spot
    remove(name)
    if excludes:
        remove(NAME % excludes)
    return call("create", "EntityFactory",
                'sdf: "%s", name: "%s", allow_renaming: false' % (model_sdf(name, x, y, w, d, color), name))


def placed():
    """Which spots hold a crate right now, asked of the world itself — the server keeps no state of
    its own, so it tells the truth after a restart."""
    r = subprocess.run(["ign", "model", "--list"], capture_output=True, text=True)
    names = set(re.findall(r"crate_(\w+)", r.stdout or ""))
    return sorted(s for s in SPOTS if s in names)


PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Golem sim — crates</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body { margin: 0; height: 100vh; display: flex; flex-direction: column;
         background: #14161f; color: #e7e9f3; font: 14px/1.4 system-ui, sans-serif; }
  header { display: flex; gap: 8px; align-items: center; padding: 8px 10px;
           border-bottom: 1px solid #2a2e3f; flex: 0 0 auto; }
  h1 { font-size: 13px; font-weight: 600; margin: 0 10px 0 0; color: #9aa0bb; letter-spacing: .04em; }
  button { font: inherit; padding: 6px 12px; border-radius: 6px; cursor: pointer;
           border: 1px solid #3a3f57; background: #1e2131; color: #e7e9f3; }
  button:hover { background: #272b3f; }
  button[aria-pressed="true"] { border-color: #b8834a; background: #3a2b18; color: #f0c894; }
  button.big[aria-pressed="true"] { border-color: #a8622c; background: #4a2c12; color: #f6b980; }
  button.clear { margin-left: auto; border-color: #4a3a3a; }
  #note { color: #9aa0bb; font-size: 12px; padding: 0 10px 6px; flex: 0 0 auto; }
  iframe { flex: 1 1 auto; width: 100%; border: 0; background: #14161f; }
</style></head><body>
<header>
  <h1>CRATES IN THE WORLD</h1>
  <button data-spot="west" title="A crate in the west corridor: 1.5 m wide, so it is shut to a body">West corridor</button>
  <button data-spot="center" title="A crate in the middle of the central hall: 3 m wide, so the body can feel its way past">Central hall</button>
  <button data-spot="east" title="A crate in the east corridor: shut to a body">East corridor</button>
  <button class="big" data-spot="big" title="One big crate, the hall from wall to wall: no body gets through, the shortcut is shut">Central hall · wall to wall</button>
  <button class="clear" data-clear="1">Clear all</button>
</header>
<div id="note">A crate the golems' map does not hold: whatever touches it meets a real obstacle.</div>
<iframe src="__VNC__" title="Gazebo"></iframe>
<script>
  const buttons = [...document.querySelectorAll('button[data-spot]')];
  const note = document.getElementById('note');
  const paint = placed => buttons.forEach(b =>
      b.setAttribute('aria-pressed', placed.includes(b.dataset.spot) ? 'true' : 'false'));
  const ask = async (url) => {
      note.textContent = 'working…';
      try {
          const r = await fetch(url, { method: 'POST' });
          const s = await r.json();
          paint(s.placed);
          note.textContent = s.placed.length
              ? 'crates in the world: ' + s.placed.join(', ')
              : 'no crates: the world is as its plan built it.';
      } catch (e) { note.textContent = 'the world did not answer: ' + e; }
  };
  buttons.forEach(b => b.onclick = () => ask('/crate/' + b.dataset.spot));
  document.querySelector('button[data-clear]').onclick = () => ask('/crates/clear');
  fetch('/state').then(r => r.json()).then(s => paint(s.placed));
</script>
</body></html>
"""


class Lever(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        if self.path.startswith("/state"):
            return self.reply_state()
        if self.path in ("/", "/index.html"):
            host = self.headers.get("Host", "localhost").split(":")[0]
            return self.send(PAGE.replace("__VNC__", "http://%s:6080/vnc.html?autoconnect=true&resize=scale" % host),
                             "text/html; charset=utf-8")
        self.send_error(404)

    def do_POST(self):
        spot = self.path[len("/crate/"):] if self.path.startswith("/crate/") else None
        if spot in SPOTS:
            place(spot)
            return self.reply_state()
        if self.path == "/crates/clear":
            for s in SPOTS:
                remove(NAME % s)
            return self.reply_state()
        self.send_error(404)

    def reply_state(self):
        self.send(json.dumps({"placed": placed()}), "application/json")

    def send(self, body, kind):
        raw = body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(raw)

    def log_message(self, fmt, *args):
        print("[crates] " + fmt % args, flush=True)


if __name__ == "__main__":
    print("[crates] levers for world '%s' on :%d — spots: %s" % (world, port, ", ".join(SPOTS)), flush=True)
    ThreadingHTTPServer(("0.0.0.0", port), Lever).serve_forever()
