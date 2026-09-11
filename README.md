# Golem

*The automaton that comes to life by written instructions — here, the ones written in its journal.*

Golem is a spike: [Puppeteer 2](GolemAPI/localfeed) actors ("golems") driving robot bodies in a
ROS 2 world simulated by **Gazebo Fortress**. One golem per body, one journal per golem, one shared
world with real physics and real collisions. The point under test: that the actor + journal + membrane
pattern travels to a domain of autonomous bodies that run missions, journal their outcomes, answer
queries and talk to each other — and that what the golem *knows* (its map) can differ from what is
*real* (the simulator), with the difference showing up in the journal.

Team discussion lives in [PLAN-Golem.md](PLAN-Golem.md) (Spanish). Working rules for AI assistants
in [CLAUDE.md](CLAUDE.md).

## The picture

```
                 tells over HTTP (PointVisited / PlaceVisited, acks back)
        ┌─────────────────────────────────────────────────────────────────┐
        │                                                                 │
┌───────┴────────┐  panel :8081                          panel :8082  ┌───┴────────────┐
│  blue-golem    │                                                    │   red-golem    │
│  actor + journal ./journal/blue                                     │   ./journal/red│
│  DiffDriveNavigator                                                 │                │
└───────┬────────┘                                                    └───────┬────────┘
        │  rosbridge (JSON over websocket) :9090                              │
        └────────────────────────────┬────────────────────────────────────────┘
                                     │
                          ┌──────────┴───────────┐
                          │  sim (one container)  │   noVNC :6080 — the kiosk
                          │  Gazebo Fortress      │   physics · walls · bodies with contact
                          │  ros_gz_bridge        │   sensors · a crate the map does not know
                          │  rosbridge · teleport │   the world is GENERATED from sim/world/plan.json
                          └──────────────────────┘
```

- **The world is real.** Walls, doors, solid blocks and the bodies are physical. A body that touches
  something is told so by the simulator's contact sensor. The golem reckons where the touch happened
  (its pose plus its own radius) and holds that point against its map: a wall it knows is its own
  execution error, so it backs off and tries the leg again; anything else is a touch it journals and
  tells (`{ touch = Pose(@x, @y, @heading); g.Bump(@id, touch); }`), presuming a thing: the mark is made in
  the same row, and the peers that hear it mark it too. Then it listens: a peer that says it bumped or was
  touched there and then is a body, not a thing — the golem concludes `g.Met(who, at)`, the mark comes back
  here and in every peer (`MetPeer` → `LearnMet`), and the two coordinate (the name that sorts first has the
  way, the other backs down its lane and steps aside); nobody? the mark stands, and the golem feels for a way
  past, right then left, before deciding its road again around the marks. Only when no road fits its body does
  the mission fail.
- **The map is knowledge, and a module of its own.** Each golem carries the warehouse map in its journal
  (release `warehouse_v1`: a `MapLayout`, the concrete map — each area found once and told where it stands, how
  big it is, its doors and what it opens to) and plans the shortest road through doors and openings. The map
  does not know about the crate standing in the middle of the center hall — that is the point; what the bodies
  learn by touching lives in another module, `collisions`.
- **The journal is the only truth.** Pose and contacts are ephemeral telemetry. Transitions are journaled:
  `Visit`, `Cover`, `Follow` (the entrusting) together with `Route` and its legs (the whole plan, in the same
  entry), `Reach` (a stop reached: the legs before it were walked; the last one completes the mission), `Bump`
  and `Graze` (a touch interrupts the plan; another `Route` replaces what was left), `Fail` and `Abandon` (the
  ending). Walking a door or a point is not journaled: the plan said it, the body did it. Kill a golem
  mid-mission and it rehydrates, decides its road again from where its body stands, and goes on.
- **Speech is a reaction.** Red tells blue every stop it reaches; blue takes each told point as a mission of
  its own, follows, stops a body's length short of the leader, and abandons stale told points when newer
  ones arrive (catching up, not retracing).

## Run it

Requirements: Docker Desktop (WSL 2 backend on Windows), about 9 GB of disk for the images, and
patience for the first build (the sim image is built on `tiryoh/ros2-desktop-vnc:humble`). The
Puppeteer package is pinned in `GolemAPI/localfeed`; nothing else needs installing.

```bash
docker compose up -d --build
```

Then open:

| Where | What |
|---|---|
| http://localhost:6080/vnc.html?autoconnect=true&resize=scale | The kiosk: Gazebo's picture, camera straight above the floor |
| http://localhost:8081 | Blue's panel (the follower) |
| http://localhost:8082 | Red's panel (the leader: it tells blue every visited place) |
| http://localhost:8083 | Green's panel (lives on its wheels' reckoning, like a real robot; the panel draws where the world says it really is) |
| ws://localhost:9090 | rosbridge — the bodies' topics as JSON |

A first tour, from a shell:

```bash
curl -X POST "localhost:8082/move?place=kitchen"
```

Red plans its road, journals it, crosses two doors and reaches the kitchen; blue is told and follows.
Send red on through several stops in one mission, in the order you give or in the order it finds shortest:

```bash
curl -X POST localhost:8082/move  -H "Content-Type: application/json" -d "{\"stops\": [\"storage\", \"garage\"]}"
curl -X POST localhost:8082/cover -H "Content-Type: application/json" -d "{\"stops\": [\"garage\", \"kitchen\", \"storage\"]}"
```

Its shortest road from the kitchen to the garage cuts through the center hall, where a crate the map never
heard of stands — the journal will say so: a mark, a few steps aside to feel for a way past it, and either
the way found or another road. The panels do the same with a stop composer: click rooms or press places
to collect stops, then *Visit* or *Cover*.

To watch the physics without the picture (the GUI's software rendering costs five or six CPU cores),
set `KIOSK=false` on the `sim` service in `docker-compose.yml`.

## Layout

| Path | Role |
|---|---|
| `sim/` | The world. `world/plan.json` is the floor plan (places, doors, open boundaries, bodies, obstacles); `world/build_world.py` turns it into the Gazebo world and the bridge's topic mappings at image build; `kiosk/kiosk.sh` starts physics, bridges, rosbridge and the GUI; `bridge/teleport.py` is the lab lever that puts a body back on its mark. |
| `GolemDomain/` | The pure domain, no framework references: `Golem` (the subject: missions, decisions, orders), `Body`, `Maps` (`Map`, `Area`, `Door`, `Opening`: information only), `Layouts` (`Layout`, `Zone`, `Wall`, the `Catalog`: the map on the plane), `Touches` (`Collisions`, `Mark`, `Obstacle`: what the bodies learned), `Routes` (`RoutePlanner`: Dijkstra over doors, openings and detours; doors are crossed straight, openings away from their corners). |
| `GolemTest/` | Acceptance tests that enter through the actor's perform, against an in-memory journal, with the same release chain the host runs. `dotnet test GolemTest` |
| `GolemAPI/` | The generic golem program (ASP.NET). One image, N golems by environment. `Membrane/` (rosbridge, the tell wire), `Navigation/` (the seam to the body's locomotion), `Choreography/` (reactions, the ops saga, the mission loop), `Panel/` (the page and the journal tap), `Controllers/`. |
| `journal/` | The golems' journals (FileSystem backend), one folder per golem. Git-ignored; disposable in this spike. |
| `PLAN-Golem.md` | The team's plan and decision log (Spanish): what was tried, what was retired, what the engine taught us. |

## The golem's surface

Every write goes through the actor's DSL and lands in the journal. The verbs:

| Verb | Meaning |
|---|---|
| `Visit(id, area)` · `Visit(id, point)` | The operator sends the golem to a place, a point, or through several stops **in that order** (`{'kitchen', '9,8', 'garage'}`). Handles are minted by the actor and never reused. |
| `Cover(id, stops)` | Several stops, and the golem **chooses the order** that makes the whole road shortest. |
| `Follow(x, y)` | The golem follows its leader to a point a peer says it reached (handle minted inside). |
| `route = Route(id)` · `route.Via(passage, at)` · `route.Around(at)` · `route.Aside(at)` · `route.Stop(at)` | The plan, one act per leg in the same entry as the errand: passages to cross (a door or an opening of the map, found), points to pass (around a mark, aside from a peer) and stops to reach, in order. Between stops it is always the shortest road. An errand one segment away has no road to decide and gets none. |
| `Reach(id, at)` | A stop reached: the legs before it were walked, whatever they were. Reaching the last one completes the mission — there is no separate "complete". Told to the follower. |
| `Bump(id, touch)` · `Bump(touch)` | The body touched something the map does not hold, heading that way — on a mission's road, or while standing still. A fact, told to every peer with the golem's name; what it was is concluded afterwards, by the domain. |
| `HearBump(who, touch, peerAt)` · `HearTouch(who, at, peerAt)` | A peer told it bumped (heading which way, standing where) or was touched while standing. A touch of my own there and then was that peer: a body, not a thing. A bump heard is learned as a mark, as the peer presumed; a touch heard is not (what touches a standing body is a body). |
| `Graze(id, at)` | The body grazed a wall the map KNOWS: its own execution error, no discovery. Journaled so the golem's patience on the leg (`MayRetryLeg`) decides whether to try again or give the mission up. |
| `Met(who, at)` | The domain concluded a peer: the body met that peer there. The mark its bump presumed comes back, and so does the one learned from that peer's bump there; the encounter stays among the obstacles as history (a `Peer`), never geometry. Told (`MetPeer`). |
| `LearnMet(at)` | A peer said its touch there was a body: the golem takes back the mark it learned from that bump. |
| `Fail(id, reason)` | The world said no — in the navigator's words (`no road … that fits a body of radius 0.25 past 2 marks`, `blocked by blue after yielding 4 times`, `stalled`, `timeout`). |
| `Abandon(id, reason)` | The golem let the mission go: a newer told point made it stale, or the operator let go of everything (one command, every pending mission). |

Releases (versioned initialization inside the actor, applied once and journaled) build the golem's modules
as globals of the actor and hand them to it: `body_v1` (`body = Body(0.25, 2.0, 6.0);`), `warehouse_v1` — the
concrete map, each area found once and told what it is in one train (`map = MapLayout('warehouse');
map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));
map.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0).DoorAt('storage', Position(7.0, 9.5)).OpenTo('center'); …`; `Map` is the
abstract maquette, `MapLayout : Map` adds the positions) and `init` (`collisions = Collisions(map); g = Golem(body, map, collisions);`). Values are objects in the journal — `g.Visit(1, Position(9.0, 8.0))`,
the plan one act per leg in the errand's own entry, on the road object the golem opens (`route = g.Route(1); door1 = map.FindDoor('kitchen', 'north'); at1 = Position(4.0, 9.5); route.Via(door1, at1); … route.Stop(stop3);`)
— except what is told to the peers, which travels flat. Evolve the golem by appending a release, never by
editing an applied one.

Endpoints, per golem:

| Endpoint | What it does |
|---|---|
| `POST /move?place=` · `POST /move?x=&y=` · `POST /move` `{"stops": [...]}` | Send the golem to a place, a point, or through several stops in that order (409 when a stop is off the map) |
| `POST /cover` `{"stops": [...]}` | Send it through several stops in the order it finds shortest |
| `GET /state` · `GET /progress` · `GET /map` | The mission board; road left and ETA from where the body stands; the map as the golem knows it — one query that walks the golem's `Place` objects and prints their properties (`foreach (places in g.Places()) { print places.Name 'name', places.Center.X 'cx'; foreach (doors in places.Doors()) { print doors.To 'to', doors.At.X 'x'; } }`), rendered by the engine as `{places: [{name, x, y, w, h, cx, cy, doors: [...], opens: [...], marks: [...]}]}` |
| `GET /body` | Host telemetry: body, believed pose, the world's pose, the last thing the body touched |
| `POST /query` | Ad-hoc read-only query in the DSL, e.g. `g.Distance('kitchen', 'garage')` |
| `POST /reset` · `POST /reset-everything` | Abandon every pending mission (journaled, one command) and put the body back; wipe the journals of this golem and its peers and reboot them reborn |
| `GET /events` | The panel's feed: the whole journal replayed, then every record as it lands, plus runtime events |
| `POST /tell` | Where a peer's tells arrive |

Environment of a golem container: `GOLEM` (identity, names the journal), `BODY` (the model it drives),
`HOME_AT` (its mark), `ROSBRIDGE_URL`, `JOURNAL_PATH`, `PANEL_PORT`, `TELL_ROUTES` (tell topic → peer
URL), `TELL_DONE_TO` (the peer to tell every visited place), `TELL_RETRY_SECONDS`. Only one direction of
`TELL_DONE_TO` between two golems, or the told missions echo back forever. `POSE_SOURCE` is where a
golem's idea of its position comes from: `world` (blue and red) hands it the simulator's true pose, a
gift no real robot gets; `wheels` (green) makes it live on dead reckoning from its own wheels, anchored
once at start-up, and the panel draws a ghost where the world says the body really is. Blocked against
a crate for half a second, a wheel-reckoning golem is half a metre wrong about itself, and misreads what
it touches next.

## Rules of the house

- One writer per journal: never point two processes at the same `GOLEM`.
- Journals are disposable here, but renaming a verb or an applied release makes the existing ones
  incompatible — the PLAN records every time it happened.
- Reset the two golems together (*Reset everything* does): the hearer deduplicates the teller's tell ids,
  so resetting one side alone makes the other swallow the next tells as repeats.
- Telemetry never touches the journal. A query can take the pose as a parameter and let the domain
  compute with what it knows; that is how `/progress` answers "how far, how long".

## Where this stands

Verified live on 8 September 2026: red sent through the kitchen to the garage meets blue head-on in a
doorway — both journal the touch, each hears the other's, nobody marks anything, blue passes by name and
red gives way, then re-decides its road and crosses; in the center hall red bumps the crate three times
with nobody else bumping, marks its north face as a three-vertex figure, told to blue and green, and
feels past it on the right; blue follows to the garage around the freshly learned marks. Next, in the
PLAN: marks that expire, the obstacle's outline as planning geometry, exploring space beyond the map
(with a lidar rather than by bumping). Open decisions: the follower's pause versus catching up, GPU
rendering for the kiosk, Nav2 as the navigator on a real robot.
