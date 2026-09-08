# Golem

*The automaton that comes to life by written instructions — here, the ones written in its journal.*

Golem is a spike: [Puppeteer 2](golemhost/localfeed) actors ("golems") driving robot bodies in a
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
  execution error, so it backs off and tries the leg again; a peer is a body that moves, so it yields
  and waits; anything else becomes a mark on its map (`g.Bump(1, 10.2, 5.8)`), told to every peer
  (`g.Learn`), and the golem feels for a way past, right then left, before deciding its road again
  around the marks. Only when no road fits its body does the mission fail.
- **The map is knowledge.** Each golem carries a floor plan in its journal (release `map_v1`) and plans
  the shortest road through doors and open boundaries. The map does not know about the crate in the east
  corridor — that is the point.
- **The journal is the only truth.** Pose and contacts are ephemeral telemetry. Transitions are journaled:
  `MoveTo`, `Cover`, `Follow` (the entrusting), `Route` (the road decided), `Cross` and `Reach` (progress; the
  last stop reached completes the mission), `Fail` and `Abandon` (the ending). Kill a golem mid-mission and
  it rehydrates and resumes from where its body stands.
- **Speech is a reaction.** Red tells blue every stop it reaches; blue takes each told point as a mission of
  its own, follows, stops a body's length short of the leader, and abandons stale told points when newer
  ones arrive (catching up, not retracing).

## Run it

Requirements: Docker Desktop (WSL 2 backend on Windows), about 9 GB of disk for the images, and
patience for the first build (the sim image is built on `tiryoh/ros2-desktop-vnc:humble`). The
Puppeteer package is pinned in `golemhost/localfeed`; nothing else needs installing.

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

Its shortest road from the storage to the garage is the east corridor, where a crate the map never heard
of stands — the journal will say so. The panels do the same with a stop composer: click rooms or press
places to collect stops, then *MoveTo* or *Cover*.

To watch the physics without the picture (the GUI's software rendering costs five or six CPU cores),
set `KIOSK=false` on the `sim` service in `docker-compose.yml`.

## Layout

| Path | Role |
|---|---|
| `sim/` | The world. `world/plan.json` is the floor plan (places, doors, open boundaries, bodies, obstacles); `world/build_world.py` turns it into the Gazebo world and the bridge's topic mappings at image build; `kiosk/kiosk.sh` starts physics, bridges, rosbridge and the GUI; `bridge/teleport.py` is the lab lever that puts a body back on its mark. |
| `golemdomain/` | The pure domain, no framework references: `Golem` (the aggregate the DSL drives), `Mission`, `Place`, `Passage`, `Atlas` (Dijkstra over doors and openings; doors are crossed straight, openings away from their corners). |
| `golemdomain.tests/` | Acceptance tests that enter through the actor's perform, against an in-memory journal, with the same release chain the host runs. `dotnet test golemdomain.tests` |
| `golemhost/` | The generic golem program (ASP.NET). One image, N golems by environment. `Membrane/` (rosbridge, the tell wire), `Navigation/` (the seam to the body's locomotion), `Choreography/` (reactions, the ops saga, the mission loop), `Panel/` (the page and the journal tap), `Controllers/`. |
| `journal/` | The golems' journals (FileSystem backend), one folder per golem. Git-ignored; disposable in this spike. |
| `PLAN-Golem.md` | The team's plan and decision log (Spanish): what was tried, what was retired, what the engine taught us. |

## The golem's surface

Every write goes through the actor's DSL and lands in the journal. The verbs:

| Verb | Meaning |
|---|---|
| `MoveTo(id, place)` · `MoveTo(id, x, y)` · `MoveTo(id, stops)` | The operator sends the golem to a place, a point, or through several stops **in that order** (`{'kitchen', '9,8', 'garage'}`). Handles are minted by the actor and never reused. |
| `Cover(id, stops)` | Several stops, and the golem **chooses the order** that makes the whole road shortest. |
| `Follow(x, y)` | The golem follows its leader to a point a peer says it reached (handle minted inside). |
| `Route(id, plan)` | The road decided from where the body stands, passages and stops in order: `kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5`. Between stops it is always the shortest road. |
| `Cross(id, passage)` | A door or open boundary crossed. |
| `Reach(id, x, y)` | A stop reached. Reaching the last one completes the mission — there is no separate "complete". |
| `Bump(id, x, y)` | The body touched something the map does not hold: a **mark** on the map of touches. The golem then feels for a way past (a step right, then left, a body's width at a time) and, failing that, decides its road again around the marks. |
| `Learn(x, y)` | A peer told of a mark: the golem learns it without the bruise, and plans around it too. |
| `Fail(id, reason)` | The world said no — in the navigator's words (`no road … that fits a body of radius 0.25 past 2 marks`, `blocked by blue after yielding 4 times`, `stalled`, `timeout`). |
| `Abandon(id, reason)` | The golem let the mission go: a newer told point made it stale, or the operator let go of everything (one command, every pending mission). |

Releases (versioned initialization inside the actor, applied once and journaled): `init` gives the golem
its body — size, cruise speed and how long it lingers at a told stop (`g.Embody(0.25); g.Cruise(2.0);
g.Linger(6);`); `map_v1` charts its floor plan, one fluent chain per place
(`g.Chart('kitchen', 0, 8, 4, 3).DoorTo('north', 4, 9.5).DoorTo('west', 0.75, 8);`). Evolve the golem by
appending a release, never by editing an applied one.

Endpoints, per golem:

| Endpoint | What it does |
|---|---|
| `POST /move?place=` · `POST /move?x=&y=` · `POST /move` `{"stops": [...]}` | Send the golem to a place, a point, or through several stops in that order (409 when a stop is off the map) |
| `POST /cover` `{"stops": [...]}` | Send it through several stops in the order it finds shortest |
| `GET /state` · `GET /progress` · `GET /map` | The mission board; road left and ETA from where the body stands; the map as the golem knows it |
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

Verified live on 7 September 2026: two golems, real doors crossed straight, the follower stopping short
of the leader, a bump against a known wall recovered and retried, a real collision with an obstacle the
map does not know, journaled with its cause and its place. Next, in the PLAN: exploring space beyond the
map (with a lidar rather than by bumping), then learning discovered obstacles, telling peers about them
and replanning. Open decisions: the follower's pause versus catching up, GPU rendering for the kiosk,
Nav2 as the navigator on a real robot.
