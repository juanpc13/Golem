# Golem — working rules for Claude

Golem is a spike: Puppeteer 2 actors ("golems") driving robot bodies in a ROS 2 world
simulated by Gazebo Fortress (turtlesim until 4-sep-2026). One golem per body, one
journal per golem, a shared world with real physics and real collisions.

## Scope

- Work ONLY inside this repo (`C:\Users\Juan\source\repos\Golem`). Do not read,
  cite, or imitate code from ExchangeEngine, LottoAPI, VeladaApp or any other
  sibling repo unless Juan explicitly asks for a specific file. Those are other
  eras and other conventions; they confuse the design here.
- Plan for the team: `PLAN-Golem.md` (Spanish, discussion doc). Research notebook:
  `NOTEBOOK-Golem.md` (Spanish, lab entries — see *Research discipline*). Code,
  comments, logs and UI are in English.

## Research discipline (Juan, 8-sep-2026)

We are scientists. Chatting without recording results wastes Juan's time and ours.
Every observation must end as a note that lets us conclude and improve the domain:

- **Every lab session produces an entry in `NOTEBOOK-Golem.md`** — dated, with
  *Observación* (what the world/simulator/journal did), *Conclusión* (what it
  means), *Ajuste al dominio* (what changes in `GolemDomain/`, or "none, why").
  Write the entry as the lab happens, not at the end; a finding without a note
  did not happen.
- **Every change to the domain traces back to a notebook entry** (the entry names
  the classes it touched). The notebook feeds the domain; the domain never grows
  from a chat alone.
- **Documentation Juan hands over is saved first, verbatim in substance**, in the
  notebook (as an entry) and summarized in this file's glossary — before any code.
- The domain is a durable, general asset (robots, floor plans, routes) that will
  outlive this spike: name concepts by Juan's canon (glossary below), in English.

## Domain glossary (Juan's canon, 8-sep-2026 → `GolemDomain/`)

| Juan says (ES) | Class / namespace (EN) | Notes |
|---|---|---|
| Robot | `Golem` (subject) + `Robots.Body` | The golem is the mind (journaled); it RECEIVES its modules — `g = Golem(body, layout, collisions)` — and builds none. `Body(radius, speed, linger)` is a module of its own (`body_v1`), built from magnitudes that say what they are: `radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); body = Body(radius, speed, linger);` (11-sep-2026). Its *name* is the journal's identity (env `GOLEM`), not domain state. Its *estimated position* is telemetry: it enters queries as `@x, @y`, never the journal. |
| Posición | `Geometry.Position` | The coordinate (x, y, z) in space (14-sep-2026): `Position(4.0, 9.5)` is the point on the floor (z = 0), `Position(4.0, 9.5, 1.2)` keeps the third dimension for the day a body climbs or touches above the floor. Distance is measured in space; the layout reads x and y only. Was `Waypoint`. |
| Ubicación | `Geometry.Location : Position` | A position that means something on the map (a corner, a jamb, a mark). |
| Segmento | `Geometry.Segment` | A straight run from position i to j. A `Wall` is one. |
| Mapa / maqueta | `Maps.Map` (abstract) | The ABSTRACT contract of what a map DISPOSES, and nothing else (10-sep-2026): areas, passages, connectivity (`Connects(a, b)`), attributes (door width, wall height). Not one coordinate. Never instantiated: the concrete map is a `MapLayout`. |
| Espacio / área | `Maps.Area` | A named part of the map; chains its passages (`DoorTo`, `OpenTo`), knows its neighbours through the map. That it is a rectangle is its zone's business. |
| Puerta | `Maps.Door : Passage` | A gap in the wall two areas share; as wide as the map says. WHERE it stands is the layout's. |
| Frontera abierta | `Maps.Opening : Passage` | The whole boundary two areas share is free. |
| Mapa con su distribución | `Layouts.MapLayout : Map` | The CONCRETE map, the one class that builds everything: the same ways of creating areas and passages as the maquette, extended with dimensions and positions — its areas are `Zone : Area` (an area that also occupies a rectangle), every door has a point. Built by finding each object once and telling it in one train: `map = MapLayout('warehouse'); map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));` then `map.Find('kitchen')`. Answers as a maquette (`Connects`, `Neighbours`) and as a layout (`Touches`, `ZoneOf`, walls, corners, room for a body). Global `map`, release `warehouse_v1`. `Layouts.Catalog` holds the named maps (`warehouse`, `cross-corridors`, `ring-corridor`) and renders their release. Inheritance here is domain truth (Juan, 10-sep: "la abstracta es la de mapa y la clase concreta la del MapLayout… el POO tiene que hablar por sí solo"). Zone views: `Doorways()`, `OpenSides()` (`Doors()` is the area's plain list). |
| Pared | `Layouts.Wall` | A boundary of a zone not freed by an opening. HAS a `Segment` (its line), is not one; knows its `PlacedDoor`s. |
| Colisiones | `Touches.Collisions` | The module that keeps what the bodies LEARNED by touching (marks, peers met, bumps heard) and INTERPRETS it (obstacles, suspicions, who was near). Global `collisions`, built in `init` over the layout. |
| Marca (hecho) | `Touches.Mark` | A body touched something uncharted: HAS a `Position` and a heading (its normal). A fact. |
| Obstáculo (hipótesis) | `Touches.Obstacle` (`Thing`, `Peer`) | Marks joined by closeness: a point, a line, a polygon. A hypothesis, refined by each bump; never stored, always derived. |
| Ruta (el encargo y su camino) | `Routes.Route` | Juan, 14-sep-2026: "visit, luego route devuelve un objeto con todos los puntos a visitar y eso es lo que se está siguiendo". ONE object for what was `Mission` + the road: the stops (`Then`), the way written as POINTS (`Via`, `Stop` — the route names each point against the map: a door where a door stands, an opening on a shared edge, a plain `via` elsewhere), the walk (`Reach`, `Bump`, `Graze`), the hold (`Pause`, `Resume`), the ending (`Fail`, `Abandon`, `Announce`). Handed out by `g.Visit(point)`, `g.Cover(point)`, `g.Follow(point)` — the handle minted inside, a deterministic function of the routes the golem holds — and found again by `g.Find(@id)`, the only place the id enters. The last `Stop` decides the way and a `Via` on a decided route opens a new decision (the recalculation after a bump). |
| Trayectoria | `Routes.Trajectory` | Ordered legs, born whole: the planner's answer (`g.Preview`, `g.Road`), what a route holds once decided, an evasion maneuver. Never in the journal by itself. |
| Planificador | `Routes.RoutePlanner` | Dijkstra over doors, openings and detours; consults the layout AND the collisions, owns neither; `EdgeCost` generic, `DistanceCost` today. Built at query time (`RoutePlanner(layout, collisions, radius)`), never a global. |
| Magnitud / unidad de medida | `Units.Length`, `Speed`, `Acceleration`, `Duration` (abstract) — `Meters`, `Centimeters`, `MetersPerSecond`, `MetersPerSecondSquared`, `Seconds`, `Minutes` (concrete) | Juan, 11-sep-2026: "una clase que nos especifique qué son esos números, tipo las unidades de medida y velocidad, aceleraciones, segundos". The magnitude is the abstract class, the UNIT is the concrete one the journal constructs, so a value says what it is where it is written; every magnitude reads in its SI base unit (`InMeters`, `InMetersPerSecond`, `InMetersPerSecondSquared`, `InSeconds`) whatever unit wrote it, and is never negative. A duration where a length goes is refused by the engine's static type check before anything runs ("a value of type 'Length' is expected"). Nothing in the domain takes a bare double for a physical quantity from the journal any more; `Acceleration` waits for the body that declares one. |
| Posición / pose / rectángulo | `Geometry.Position`, `Pose : Position`, `Segment`, `Rectangle`, `Location : Position` | Pure geometry, no map concepts; the layout composes them. Constructors are DSL-visible: `Position(4.0, 9.5)` — literals with a decimal point (the engine does not coerce an integer literal into a double parameter). |

## The domain is the brain (Juan, 8-sep-2026)

"El único que toma las decisiones es el dominio, no el host." The domain decides
everything the automaton does — the way, how a route must end, the stages to
continue, what a touch was, what to do about it — and writes those decisions in its
journal. The host only carries them out (drives the body, waits, listens) and reports
whether it could or not, so the domain resolves what follows. Consequences:
- A decision found in host C# (a classification, a retry rule, who yields) is a
  defect to migrate: it becomes a repertoire operation (a read the host asks, or a
  verb the domain concludes with), never the other way round.
- The host owns only what the papers give it: the clock, the wire, the body.
- **The journal speaks in objects (10-sep-2026)**: values enter as `@params` and the template builds or
  finds the object — `route = g.Visit(map.Find(@area))`, `route.Via(Position(@x, @y))` — several acts per command
  when an errand has several stops or a way several legs. **Methods take instances, never names or keys**: a
  string enters only where an object is created (`Area('kitchen')`, `DoorTo('north')`, constructors) or
  found (`Find`, `FindDoor`, `FindOpening`, `FindPassage`, `Knows`); everything else takes the object
  (`Connects(a, b)`, `Touches(a, b)`, `Distance(from, to)`, `PointOf(door)`, `StepInto(door, side)`)
  (Juan, 10-sep: "evitemos parámetros primitivos si tenemos las instancias reales"). **State a verb opens and
  another verb finds by a key is an object the first verb must return** (Juan, 11/14-sep — `map`, then `route`):
  the errand is the route, `g.Visit(point)` hands it out with its handle minted inside (like `Follow` always did),
  later acts find it (`route = g.Find(@id)`), and no golem act takes a mission id any more.
  **The reads speak in objects too** (Juan, 14-sep: "hacer de alto nivel, ya no interactuar con primitivos… tanto
  en escritura y lectura"): telemetry enters a query as `@x, @y` and the query builds the object —
  `g.FitsAt(Position(@x, @y))`, `g.DistanceLeft(Position(@x, @y))`, `g.Suspect(Pose(@x, @y, @heading), @since)`,
  `g.Road(g.Find(@id), Position(@x, @y))`, `g.RoadPast(g.Find(@id), @who, Pose(…))`; a per-route question is the
  route's own property (`g.Find(@id).StopsLeft`, `.IsPending()`, `.Paused`, `.NextLeg.Name`, `.LegsAhead`, `.Status`),
  the route underway is `g.Next()` (`g.Next().Id`, `g.Next().NextLeg.At.X`), the pending ones `g.PendingRoutes()`;
  what a module answers alone is asked of the module (`map.IsOnMap(Position(…))`, `map.ZoneAt(…)`, `map.Knows(@area)`,
  `collisions.KnowsAt(…)`, `collisions.HeardNear(…)`, `collisions.MarkCount`); and the way a NEW errand would take is
  an object told its stops one by one — `{ preview = g.Preview(Position(@fx, @fy), @cover); preview.Then(map.Find(@area1));
  preview.Then(Position(@x2, @y2)); foreach (legs in preview.Legs()) { … } }` — no arrays of numbers. **The golem
  wraps nothing** (14-sep, second pass): no `g.Radius()`, `g.MarkCount()`, `g.KnowsWallAt` — the journal reads the
  module (`body.Radius.InMeters`, `collisions.MarkCount`, `collisions.All().Count`, `map.IsWallAt(Position(…), 0.3)`),
  and the DSL reads `.Count` on any list a method returns (`g.PendingRoutes().Count`, `g.Routes().Count`; not on a
  bare `IEnumerable` property such as `map.Zones` — use `map.ZoneCount`). The golem keeps only the reads where the BODY
  enters the answer (`FitsAt`, `HasRoomAt`, `Distance`, `DistanceLeft`, `SecondsLeft`, `RouteLength`, `RouteSeconds`,
  `Road`, `RoadPast`, `Preview`, `Suspect`) or the fleet of routes does (`Find`, `Knows`, `Next`, `HasPendingMission`,
  `Routes`, `PendingRoutes`, `PlannedEnd`, `NewestFollowingId`, `HasNewerFollowing`). A way in one line is
  `g.Road(route, from).AsPlan()`, a lab reading — no `Plan` read.
  **Every non-private method or constructor that receives an object checks it for null FIRST, with an explicit
  `if`, and refuses with a `GolemDomainException`** (Juan, 14-sep-2026: "hay que dar una excepción del dominio…
  con un if antes de procesarlo, en todos los métodos que reciban un objeto por parámetro") — never an inline
  `?? throw`, never a `NullReferenceException` from inside. The message is the domain's when it has one to say
  ("a golem needs a body to drive"), else the uniform `"Type.Method: 'param' was not given"`. A `?? throw` stays
  only where it means "not found" (`Doors.FirstOrDefault(…) ?? throw`), which is not a null parameter.
  **Two objects of one kind given to one method must be two different objects** (Juan, 14-sep: "que a y b no
  pueden ser el mismo objeto"): after the null guards, `if (ReferenceEquals(a, b)) throw new GolemDomainException
  ("Type.Method: 'a' and 'b' are the same area")` — every pair of areas (`Connects`, `Door`, `Open`, `DoorBetween`,
  `Touches`, `Distance`, `Joins`…), the two ends of a `Segment`, the touch and the peer's place (`Hear`, `HearTouch`),
  the two points of a crossing; the creators by name refuse it too ("area 'kitchen' has no door to itself"). NOT
  where the same point is a legitimate answer: `Road(from, to)` and `RoadLength` (already there), `EdgeCost.Between`
  (cost zero), `Leg(at, approach, exit)` (a leg that is no door has one point three times). Engine note: a method's
  domain refusal inside a QUERY surfaces as "Exception has been thrown by the target of an invocation" — the reason is
  kept only when it reaches the actor as a command (write-time error) or a C# test; the panel's ad-hoc console loses it.
- **A command template is a braced block, step by step, values as `@params`** (Juan, 10-sep): the object is
  found or built from the parameters, named after what it is, then handed to the act — the errand and its whole
  way, one entry, ONLY POINTS (Juan, 14-sep: "el listado de puntos en el script"; the route names each point
  against the map, so no `map.FindDoor` in the plan):
  `{ point1 = map.Find(@area1); route = g.Visit(point1); point2 = Position(@x2, @y2); route.Then(point2); via1 = Position(@lx1, @ly1); route.Via(via1); route.Stop(point1); via3 = Position(@lx3, @ly3); route.Via(via3); route.Stop(point2); }`
  — a stop the errand named is written back as that very variable; a way decided again (after a bump, or at wake)
  is `{ route = g.Find(@id); via1 = …; route.Via(via1); stop2 = Position(@lx2, @ly2); route.Stop(stop2); }`. The
  legs stay written (not computed in the body from the pose): if the planner changed, a rehydration would decide
  another way and history would change (paper 05). `Parameter.Eval` cannot pull the points off the route in the
  same entry — it resolves before the body, when the route does not exist yet, and freezes scalars, not objects.
  The host walks the plan in memory (the cursor is telemetry, like the pose) and journals only what fulfils it
  (`route.Reach(point)`) or interrupts it (`route.Bump(touch)`, `route.Graze(at)`), the hold (`route.Pause()`,
  `route.Resume()`) and the ending (`route.Fail(@reason)`, `route.Abandon(@reason)`) — each `{ route = g.Find(@id); … }`.
  Names: one stop is `point`, several are `point1`, `point2`…; the points of the way are `via{n}`, a stop not named
  by the errand `stop{n}` (legs and their `@params` count from 1, as a person counts them) — Juan, 10-sep.
  The braces matter: an assignment at the top level of a command becomes a global of the actor (Fase 0,
  P2a); inside `{ }` the name dies with the block (P2b). Never a literal value in a template: every value
  is an `@param` (program–value separability, paper 02).
  **The touches take their objects too, and what is told rides beside them as `expose`** (Juan, 10-sep:
  "el g.Bump aún no maneja posición… hay que corregirlo, y el g.Mark también"): `{ touch = Pose(@x, @y,
  @heading); route = g.Find(@id); route.Bump(touch); } expose @x x, @y y, @heading heading, @me who, @px px, @py py;` — the
  reaction that tells the peers captures the `expose` labels, because the matcher captures literals, `@params`
  and `expose` labels only, never an object variable (Fase 0, P3). Same for the idle `Bump(touch)` (`tx, ty,
  twho, tpx, tpy` → `TouchedAt`), `Met` (`ex, ey` → `MetPeer`), `Reach` (`rid, rx, ry`) and `Forget` (`gx, gy`);
  `Graze`, `HearBump`, `HearTouch`, `LearnMet`, `LearnForget` need no expose (nothing captures them). Labels are
  distinct per act so no two reactions match one shape.
  **One row per touch** (Juan, 10-sep: "mantengamos una sola fila… el mismo g.Bump internamente se lo setea"):
  there is no `Mark` verb. `route.Bump(touch)` presumes a THING and marks it inside (`collisions.Mark`); the peers
  that hear `BumpedAt` mark it too (`HearBump`). If a peer says it bumped or was touched there and then, the
  conclusion is `Met(who, at)`: the mark comes back (mine and the one heard from `who`), and `MetPeer` makes every
  peer take back what it learned (`LearnMet`). A standing body's touch is `Bump(touch)` without a mark (things
  do not move) and travels as `TouchedAt` → `HearTouch`. The host owns the clock: it listens 2.5 s, then keeps
  reconsidering for 12 s more, because a peer's word may arrive after the window (a ghost mark lived in three
  journals on 10-sep before that).
- **Modules are globals of the actor** (`body`, `map`, `collisions`), built in their own releases and handed
  to the golem (`g = Golem(body, map, collisions)`): a query may calculate with a module alone
  (`map.ZoneOf(Position(5.5, 5.5))`, `map.Connects('north', 'center')`, `collisions.All()`,
  `RoutePlanner(map, collisions, r)`). Auxiliary variables inside a command template become globals too:
  build the object inline or inside `{ }`.
- A Reaction may conclude for the domain (`Causation.Continue("g.Met(...)")`), and
  the engine can judge ABSENCE in a window (`None().Within(span)`, journal-clock): the
  host's timers are candidates to disappear (NOTEBOOK, *protocolo de toques v2*).

## Doctrine (in this order of authority)

1. Juan's words in the conversation.
2. **The theory: the Puppeteer papers** (Juan, 8-sep-2026: "son el marco teórico de lo
   que utilizamos; es importante: los repertorios del dominio"). Robotics has its own
   terminology (occupancy grids, costmaps, waypoints); we do NOT copy it, because it is
   not talking about the puppet. Papers, `https://github.com/alvaroNCubo/puppeteer-papers`:
   `01-anti-porosity` (operation, not structure, is the primary: variants per outcome, no
   status flags with fields populated by case), `02-program-value-separability` (verbs with
   `@params`, never literals in the body), `03-reactions-and-partition` (the domain closes to
   operational tools; deferred work is a Reaction), `04-cross-actor-continuity` (a tell asserts a
   lived fact, past tense, inside a Reaction), `05-substrate-operations` (the journal IS the
   program; nondeterminism frozen on the write path), `06-infrastructural-symptom`,
   `07-after-the-substrate` (the author writes the domain, the substrate does the rest),
   `08-inference-without-authority` (the domain knows what exists, the actor what becomes
   observable, the assembler where it lands: no `ToString`/JSON rendering in the domain; facts
   are journaled testimony, hypotheses are derived), `09-identity-precedes-staging` (the domain
   declares no ports, no interfaces, no reconstitution surface), `0A-assembled-verb` (the domain
   is a REPERTOIRE of operations that need not anticipate the verbs the actor assembles from
   them; `Robots`, `Plans`, `Routes` are our repertoires, `Golem` the subject that assembles).
   Digest and the audit of our domain against them: `NOTEBOOK-Golem.md`, *Marco teórico*.
3. The patterns live in `C:\Users\Juan\source\repos\Skills` (Juan, 8-sep-2026: "es el
   lugar donde se siguen los patrones") — that repo is the reference for HOW we work,
   and the only sibling repo we may read. Its Puppeteer training-lab guides:
   `C:\Users\Juan\source\repos\Skills\puppeteer\training-lab\guides\puppeteer-*\SKILL.md`
   (entry point: `/puppeteer-guides`; read `puppeteer-actor-basics` first).
   When a guide covers the case, follow it literally — no V1 surfaces
   (`PerformCmd(string)` on a Performance), no home-made idioms.
4. The Puppeteer source and tests as the ground truth of behavior:
   `C:\Users\Juan\source\repos\puppeteer` (read-only reference, we consume the
   `Ncubo.Puppeteer` nupkg pinned in `GolemAPI/localfeed`).

## Architecture in one breath

- `sim/` — the world: Gazebo Fortress in kiosk mode. **Two published ports, no more**:
  :6080 (the kiosk) and ws :9090 (rosbridge, the membrane). **The lab's levers ARE the kiosk
  page** (`sim/kiosk/kiosk.html`, which is noVNC's index, so `http://localhost:6080` shows
  the buttons with the picture; the bare viewer stays at `/vnc.html`): four buttons put a
  crate in the RUNNING world (west corridor, central hall, one big crate that shuts that
  hall wall to wall, east corridor), one clears them, one puts the camera back above the
  floor. The buttons need no port of their own — they publish through rosbridge and
  `sim/bridge/crates.py` (an rclpy node, brother of `teleport.py`) turns the topics into
  Gazebo's services: `/sim/crate` and `/sim/view` in, `/sim/crates` out (what stands there,
  so a button never lies), `/world/arena/create` · `/remove` and the GUI's
  `/gui/move_to/pose` (the pose read from the world's own `<camera_pose>`). Reality is
  GENERATED from `sim/world/plan.json` at image build (walls, doors, solid blocks, bodies
  with contact sensors); its `obstacles` are empty on purpose since 10-sep, so every test
  starts from a clean floor and the crates are pressed in as needed — they live only in the
  running world and a restart clears them. A lab lever, never domain: the golems' map still
  knows nothing of what stands there.
- `GolemDomain/` — the pure domain, one assembly, namespaces `GolemDomain` (`Golem`, the
  subject), `.Geometry`, `.Robots`, `.Maps` (information), `.Layouts` (the map on the plane),
  `.Touches` (what was learned by touching), `.Routes` (see glossary). The engine binds
  classes by SIMPLE name: every class name in the assembly must be unique, and a class may
  not share its name with a namespace (`DomainLibrary`, not `GolemDomain`; `Collisions`
  lives in `Touches`). Tests see internals (`InternalsVisibleTo`).
- `GolemAPI/` — the generic golem program (ASP.NET controllers). One image, N
  golems via environment: `GOLEM` (identity, names the journal), `BODY` (the model
  it drives), `HOME_AT` (its mark), `TELL_ROUTES`/`TELL_DONE_TO` (speech).
- The journal (`./journal/<golem>/`, FileSystem backend) is the only truth: pose and
  contacts are ephemeral telemetry, transitions are journaled — entrusting (`route = g.Visit(point)` /
  `g.Cover` / `g.Follow`, more stops with `route.Then`) WITH its whole way in the same entry (a `route.Via(via{n})`
  per point, a `route.Stop(point)` per stop — asked of the golem with `g.Preview` before the command), progress
  (`route.Reach(point)` only: a stop reached implies the legs before it were walked; the last Reach completes —
  walking a door or a point is NOT journaled, Juan 10-sep: "no estar diciéndole cada cosa que va haciendo"), the
  interruption (`route.Bump`/`route.Graze`: the plan stops, another way is written on the same route; also when the
  golem wakes with a plan underway), the hold (`route.Pause()`/`route.Resume()`, 14-sep-2026: the operator holds the
  route underway — the body stands, plan and cursor keep, nothing is decided again; the host cancels the leg's
  drive with a linked token and takes the leg up again on Resume),
  touches (Bump: the fact, told, and the mark at once; HearBump/HearTouch: a peer's bump or touch; Met/LearnMet:
  it was a body, the mark comes back; Forget/LearnForget), the ending (Fail/Abandon) — every write goes through one serial
  Dispatch, tells are reaction-only. The releases build the modules (`body_v1`, `warehouse_v1`, `init`);
  what the simulator reports (a collision with a crate) is reality. The
  language was fixed on 7-sep-2026 and rewritten in objects on 10-sep-2026 (local plan
  *PLAN-objetos-en-el-journal.md*; journals before that day archived as `journal-legacy-20260910-*`):
  do not add verbs on the fly — propose them in the PLAN first. Reads (queries) may grow as the labs
  need them; writes (journaled verbs) only through the PLAN.

## Habits

- Never commit on your own; leave the working tree and offer the commit command.
- Deploy = `docker compose up -d --build`; verify with the panels (:8081 blue,
  :8082 red, :8083 green — the one on dead reckoning) and the kiosk before claiming
  anything works.
- Journals are disposable in this spike, but always say when a change makes the
  existing ones incompatible (renamed verbs, renamed upgrades).
