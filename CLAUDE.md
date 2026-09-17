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
| Robot | `Golem` (subject) + `Robots.Body` | The golem is the mind (journaled); it RECEIVES its modules — `g = Golem(body, layout, collisions)` — and builds none. `Body(radius, speed, linger, retreat)` is a module of its own (`body_v1`), built from magnitudes that say what they are: `radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat);` (11-sep-2026; the retreat — how far it backs off after a touch, once released — 14-sep). Its *name* is the journal's identity (env `GOLEM`), not domain state. Its *estimated position* is telemetry: it enters queries as `@x, @y`, never the journal. |
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
| Obstáculo (hipótesis) | `Touches.Obstacle` (`Thing`, `Peer`) | Marks joined by closeness: a point, a line, a polygon. A hypothesis, refined by each bump; never stored, always derived. A thing's `Extent(margin)` is its FIGURE (14-sep-2026): the box around its marks grown by a mark's reach and a margin — one figure per thing, so the second way around a crate takes the width the crate showed between the touches; `Collisions.Figures(radius)` inflates it by the body: where the body's centre may not go. What blocks a way is the figure, not the vertex. |
| Ruta (el encargo y su camino) | `Routes.Route` | Juan, 14-sep-2026: "visit, luego route devuelve un objeto con todos los puntos a visitar y eso es lo que se está siguiendo". 16-sep-2026: "la route tiene la lista de los puntos… `route.Reach` internamente se mueve al siguiente punto y el print dice todo lo necesario: si tiene que girar entonces gira". ONE object for what was `Mission` + the road, DECIDED INSIDE: the stops (`Then`), the way it plans itself from where it starts (held as legs, never written point by point — no `Via`, no `Stop` in the journal since 16-sep), the cursor (`Turn`: the body turned to the next leg's heading; `Reach`: it reached the next point, a stop never skipped), the touches — which CORRECT THE WAY INSIDE (Juan, 16-sep, evening: "cuando choca queriendo llegar de A a B mete entre A y B otros puntos: retroceder un poco, pasar al lado… posiciones de corrección"): `Bump(touch, me)` marks the thing and inserts a `back` leg (in reverse, the body's own retreat behind where it stood) followed by the planner's road from there around the figure; `Graze(at, me)` inserts the `back` leg and the same legs again (the patience on the leg stays spent); when no road fits from the retreat the retreat alone stays and the route asks `decide` after it — the way decided again (`Decide(from)` at wake or when stranded, `DecidePast(who, me)` out of a peer's way — the courtesy step is its first leg), the hold — the GOLEM's, not the route's (Juan, 17-sep: "pausamos el cerebro"): `route = g.Pause(me)` holds the golem where its body stands (`g.Held`, `g.HeldAt`), holds the route underway with it (`route.HeldAt`) and hands it back; `route = g.Resume(me)` lets it go on with the next leg's heading given again from where the body stands now; a route that becomes underway while the golem is held stays standing (`NextOrder` prints `g.Held`) — the ending (`Fail`, `Abandon`, `Announce`). A leg is a correction (`Leg.IsCorrection`: back, around, aside) or the plan's own (door, opening, via, stop). Handed out by `g.Visit(from, point)`, `g.Cover(from, point)` (the way decided from `from` at once; refused when no way fits) and `g.Follow(point)` (no start known: it asks `decide`) — the handle minted inside, a deterministic function of the routes the golem holds — and found again by `g.Find(@id)`, the only place the id enters. What it asks NOW is `Order`: `back` | `turn` | `run` | `hold` | `decide`. |
| Trayectoria | `Routes.Trajectory` | Ordered legs, born whole: the planner's answer (`g.Road`, a read), what a route holds once it decided (`route.AsPlan()` reads it), what a route holds once decided, an evasion maneuver. Never in the journal by itself. |
| Planificador | `Routes.RoutePlanner` | Dijkstra over doors, openings and detours; consults the layout AND the collisions, owns neither; `EdgeCost` generic, `DistanceCost` today. Built at query time (`RoutePlanner(layout, collisions, radius)`), never a global. Detours are the corners of every thing's figure (grown by the body and a hair), not a ring per mark; a run is refused when it enters a figure; the start may still stand inside one (the touch estimated short, the thing wider since) — that first run out is judged mark by mark, on the mark's normal, and may leave the way the body came but never run through a mark (14-sep-2026). |
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
  finds the object — `route = g.Visit(from, map.Find(@area))`, `route.Reach(Position(@x, @y))` — several acts per command
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
  the route underway is `g.Underway()` (`g.Underway().Id`, `g.Underway().NextLeg.At.X`), the pending ones `g.PendingRoutes()`;
  what a module answers alone is asked of the module (`map.IsOnMap(Position(…))`, `map.ZoneAt(…)`, `map.Knows(@area)`,
  `collisions.KnowsAt(…)`, `collisions.HeardNear(…)`, `collisions.MarkCount`); and the way a route holds is its own to tell (`g.Find(@id).AsPlan()`, `.LegsAhead`) — no arrays of numbers, no preview: the errand decides inside. **The golem
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
  found or built from the parameters, named after what it is, then handed to the act. **The templates are FIXED —
  written once, parameters only, never assembled with a StringBuilder** (Juan, 16-sep: "una estructura bien redactada
  con parámetros, no armarse a punta de StringBuilder… concentrar las acciones principales en unas cuantas"). The errand
  is where it starts and its stop: `{ from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }`
  (or `point = Position(@x, @y)`; `g.Cover` the same) — the route decides its WHOLE way inside and holds the points;
  the journal never lists them (16-sep supersedes the 10/14-sep "way written as points": no `via{n}`, no `route.Stop`).
  One more stop is its own entry, `{ route = g.Find(@id); point = map.Find(@area); route.Then(point); }` (the id read
  back with `g.Newest().Id`), and the route decides again through them all. A way decided again is
  `{ route = g.Find(@id); from = Position(@x, @y); route.Decide(from); }` — the pose is the only thing the host adds.
  The way decided inside is deterministic on replay (the planner reads the layout and the collisions module, both
  journaled state; paper 05 holds while the planner's code holds — a spike's trade, accepted 16-sep).
  **The body does ONE thing at a time; the journal says what, and every script lives in the Robot's action methods**
  (Juan, 16-sep-2026: "un comando ejecutado produce un print del siguiente punto que debe alcanzar y una vez alcanzado
  pide el siguiente… una cosa a la vez, no una cola de acciones acumuladas"; then: "uno esperaría que todo esté
  ordenado en el `GolemController`… ahí se ve el script entero relacionado a la acción, con los `print` del punto al
  que deberá moverse"; 17-sep: "el controller recibe y valida los parámetros y llama a robot; el método en el robot
  tiene los scripts, los valida una segunda vez, corre los scripts y hace los prints, y esos prints terminan llamando
  al Push de RobotToRos automáticamente al terminar el script"). The host holds no plan and no cursor. EVERY journal
  script — the errand with its way, a point reached, a bump, a graze, a hold, a resume, a way decided again, an ending,
  the uptake of a tell — is an action method or const of `Choreography/Robot.cs` (`Move`, `Cover`, `Pause`, `Resume`,
  `Forget`, `Arrived`, `Stuck`, `BumpedAsync` and the touch protocol's `Bumped`, `Grazed`, `Met`, `Decided`,
  `DecidedPast`, `Failed`, `LetGo`; `Uptake*`); the controller only validates the JSON and calls it. Every one that
  changes what the body must do ENDS WITH ITS PRINT INSIDE THE BRACES, asked of the route the act was on (Juan, 17-sep:
  "que lo que está afuera también esté dentro y le preguntes cosas al route"): `print route.Id 'route', route.Order 'order',
  route.IsPending() 'pending'; if (route.IsWalkable) { print route.NextLeg.Kind 'kind', … route.StopsLeft 'stopsLeft'; }`
  — `route.Order` is `hold` | `decide` | `back` | `turn` | `run`, or how the route ended (`completed`, `failed`,
  `abandoned`) once it is no longer pending. That print is what the command RETURNS to the caller. What the engine
  PUSHES to the body is the reaction's emit, `Robot.NextOrder` — the same fields asked of `g.Underway()`, the route underway
  (a reaction sees no local variable), starting with `g.HasPendingMission() 'pending'` (always something, so it pushes
  even when nothing is pending and the body must stop):
  and, when walkable, the leg (`kind name x y ax ay ex ey hasHeading heading following stopsLeft`); the order is
  `turn` (turn in place to the leg's heading — every leg has one now, the first from where the errand started), `run`
  (run to the leg's point), `hold` or `decide`. A command's print
  comes back to WHOEVER PERFORMED IT, at write time — `PerformCheckThenCommand` returns it; a refused Check returns
  `{"EWI":[{"Error":"…"}]}`, the domain's own words (lab 16-sep, `lab-print.txt`) — so the answer to a report IS the
  next order (`Choreography/Order.cs`: `Order`, `Answer`). **The print is handed to the ROBOT, the actor's OUTPUT
  TARGET** (Juan, 16-sep, evening: "el print retorna al controller, pero ese mismo objeto analiza el JSON y tiene un
  switch con las acciones disponibles; dentro de cada case están los llamados que viajan hasta el robot… el robot solo
  es el cuerpo; cuando termina le dice al actor por un endpoint que ya terminó, para pedir el siguiente print"):
  `Choreography/RobotToRos.cs` implements `IOutputSink` and is registered with `performance.OutputTarget(robot.ToRos,
  JsonFormatter)` (Juan, 16-sep: "una nueva clase que sirva de salida, RobotToRos, que herede de IOutputSink, que se
  ocupe del obey y haga el switch case para enviar al robot"). **Nobody dispatches the print: the engine pushes it.** A
  command's print is PULL (it returns to the caller); only a Reaction's emit is PUSHED — so `RobotToRos.DefineReactions`
  declares ONE reaction per act shape, `next-order-find` on `[_:Golem].Find($id)` (every act on a route in hand writes
  `route = g.Find(@id)` first), `next-order-visit` / `-cover` on `Visit(_, _)` / `Cover(_, _)`, `next-order-follow` on
  `Follow(_)`, each `.Program.Emit(Robot.NextOrder)`; when the act lands the engine calls `Push`, which parses the
  document and SWITCHES (17-sep lab, `lab-push-real.txt`: every act pushed exactly once with the speech reactions around;
  the morning's flakiness came from MANY reactions on the same act shape — one per shape, no more). The sink is armed
  after `performance.Start()` and the membrane (a replayed or early push would be lost or stale); the returned print is
  kept only to answer the caller (a refusal, 409). `Push` ignores anything not named `next-order*` and switches: `hold` → `stop` (the body remembers what it was doing);
  `decide` → `robot.Decide` writes `route.Decide(from)` from the pose and the new print comes back; `turn` → `turnLeft`
  or `turnRight` to the domain's heading (which way round is read off the body's pose: the servo's business); `run` →
  `advance`; `back` → `back`; the same order held and resumed → `continue`. **The robot's base actions are advance,
  back, turnLeft, turnRight, stop, continue; the bump is what it reports.** The SAME JSON the journal printed travels
  to the body over the websocket (rosbridge, `std_msgs/String` on `/golem/<body>/order`) with the action, `within` and
  the body the journal declared (speed, radius, retreat). The same order twice is not resent; a different one
  replaces what the body was doing. `Choreography/Robot.cs` keeps the other face: the action methods with the scripts, what the body reports (`Arrived`,
  `BumpedAsync`, `Stuck`), the touch protocol, the follower's linger, the clock (a push is ephemeral: the journal is
  still asked every 2 s and obeyed only when it differs from what the body carries), the operator's levers. **The body is a ROS node in the simulator**
  (`sim/bridge/body.py`, one per body, launched by `kiosk.sh` from `GOLEMS`/`POSE_SOURCES`): it drives `cmd_vel`, watches
  its odometry (the truth, or its wheels' reckoning anchored once) and its contact sensor, does that ONE thing and
  reports on the golem's endpoints. **The body's vocabulary is its base actions** (Juan, 16-sep: "la interfaz del
  robot es extremadamente sencilla… avanzar / retroceder / girar a la derecha / girar a la izquierda / detener /
  continuar / choque"): it is told `advance` (to a point), `back` (to a point, in reverse), `turnLeft` / `turnRight`
  (to a heading), `stop`, `continue` — and it says `POST /robot/arrived` (`{route}`: the turn made, the
  point reached — the endpoint knows which from what the Robot handed it) or `POST /robot/bump` (`{route, with, x, y,
  heading, px, py, ptheta}`: what, where the touch landed on the plane heading into it, where it stood facing which way;
  route 0 while standing) — plus `/robot/stuck` when it could not. **The bumper is a switch**: the moment it fires the
  motors stop and the bump is reported; the body backs off NOTHING on its own — what follows is the domain's
  (`route.Bump(touch, me)` inserts the retreat and the way around; the print is `back`) and comes as the next order.
  One bump per contact: while the bumper stays pressed against the same thing it is not a new bump. Each report is a
  validated JSON body whose endpoint writes the act whole (`route.Turn()`, `route.Reach(point)`) and hands the answer
  back to the Robot: its print is the next order — "gira, luego ros le dice ya giré, se escribe que giró y esa escritura
  imprime lo siguiente". The touch protocol lives in the Robot (`BumpedAsync`: `Suspect` → `Grazed` or `Bumped`, whose
  print — `back` — goes to the body AT ONCE; then the peers' window, `Met`, `DecidedPast`), performed with the
  controller's scripts. When nothing comes through a print (a told point taken up as a `Follow`, a boot) the Robot's
  clock ASKS the same print as a query every 2 s (`AskOrder`) and obeys it if it differs from what the body carries. Every point of
  the way is journaled (the 10-sep "walk in silence" is superseded by the 16-sep dialogue). The host keeps only the
  clock (listening after a bump, the follower's linger), the wire, and the body's servo (turning to the heading,
  running, backing off the retreat). The courtesy step is the golem's (`g.Aside(Pose)`); the host walks it. The acts
  stay `{ route = g.Find(@id); … }`: `Reach`, `Bump`, `Graze`, `Pause`, `Resume`, `Fail`, `Abandon`.
  Names: the errand's stop is `point`, its start `from`; the route names its own legs (a door, an opening, `around`, `aside`, a stop
  by its zone) — Juan, 10-sep, trimmed 16-sep.
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
  distinct per act so no two reactions match one shape. **An `expose` exists ONLY where a Reaction must capture a
  VALUE to tell it** (Juan, 16-sep: "estos tengo entendido que no aportan nada de valor"; lab `lab-patterns.txt`): a
  reaction can match the act itself — `[_:Route].Reach(_)`, `Stop(_)`, `Via(_)`, `Bump(_)`, `Pause()`, `Resume()`,
  `[_:Golem].Visit(_)`, `[_:Golem].Find($id)`, `[_:Route].Fail($why)` all define and fire on a braced command — so an
  expose used as a mere trigger adds nothing and was removed (`vid vx vy`, `order`, `held`, `resumed`, `ended`,
  `letgo` are gone; `Passed` carries none). What stays is what a tell needs: `rid rx ry` (`echo-reached` →
  `PointVisited`), the bump's, the touch's, `ex ey`, `gx gy`. Lab caveat: firing of act-pattern reactions was
  inconsistent across identical runs (`Stop`, `Resume`, `Then`, `Visit` fired, `Reach`, `Via`, `Pause`, `Bump`, `Fail`
  stayed silent in one run) — one more reason the next order is the command's own print, never a reaction's push.
  **A touch on a jamb is a wall** (16-sep lab): `MapLayout.DoorGap` — within it of a door's point there is no wall — is
  half the door's width MINUS the touch's estimation error (0.55 m), because the world's walls eat into the gap (~1.25 m
  clear: the jambs stand 0.62 m from the point); with the old `+ 0.1` two touches on the jambs of living/south were taken
  for things and marked.
  **One row per touch** (Juan, 10-sep: "mantengamos una sola fila… el mismo g.Bump internamente se lo setea"):
  there is no `Mark` verb. `route.Bump(touch, me)` presumes a THING and marks it inside (`collisions.Mark`); the peers
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
  `/gui/move_to/pose` (the pose read from the world's own `<camera_pose>`). **The bodies are ROS nodes here
  too** (16-sep-2026, `sim/bridge/body.py`, one per body): each takes its golem's order on `/golem/<body>/order`
  (turn, run, stop), drives `/model/<body>/cmd_vel`, backs off after a touch, and reports to its golem's
  `/robot/*` endpoints — the robot is only the body; the golem tells it what to do, one thing at a time. Reality is
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
  it drives), `HOME_AT` (its mark), `TELL_ROUTES`/`TELL_DONE_TO` (speech). Since 16-sep-2026 EVERY
  journal script lives in `Choreography/Robot.cs` as an action method (the operator's verbs, the robot's reports, the
  touch protocol's scripts, the tell uptakes — each ending in `NextOrder`); `Controllers/GolemController.cs` validates
  the JSON and calls the Robot; `Choreography/RobotToRos.cs` is the actor's OUTPUT TARGET (the next-order reactions it
  defines push the print; it switches and sends the body its action over rosbridge); `GolemSpeech` the tell reactions and
  uptakes — no mission loop, no legs in memory, no navigator: the body's servo is the ROS node `sim/bridge/body.py`. The controllers receive the `Robot` singleton alone and perform every
  script on `robot.Actor.Using(…)` (Juan, 16-sep); the Robot reads the golem's answers from a query's print, never from a
  rented Out lease (found empty under the panel's concurrent polls). **What the operator sends arrives as a
  JSON body, typed and validated before any script runs** (Juan, 16-sep: "debería ser por JSON… y validar que venga
  correcta"): `Controllers/Requests.cs` — `ErrandRequest` (`{"stops": [{"x": 2.0, "y": 9.5}, {"x": 9.0, "y": 8.0}]}` — points only, never places: Juan, 16-sep),
  `PointRequest`, `QueryRequest`, `ResetRequest` — each says what is wrong with it and the endpoint answers 400 with
  that; no `[FromQuery]` anywhere. Each endpoint writes its own script whole, in place (no `Held(verb)`, no templates
  with the verb interpolated): Juan, 16-sep, "el controller en el endpoint debe explicar el script como tal".
- The journal (`./journal/<golem>/`, FileSystem backend) is the only truth: pose and
  contacts are ephemeral telemetry, transitions are journaled — entrusting (`route = g.Visit(from, point)` /
  `g.Cover(from, point)` / `g.Follow(point)`, more stops with `route.Then(point)`, one entry each — the route decides
  and HOLDS its whole way, nothing of it is written), progress (`route.Turn()` and `route.Reach(point)` for EVERY leg,
  one at a time — the body turns, reports it, runs, reports it; the golem hands out the next; the last stop completes —
  Juan 16-sep, superseding 10-sep), the interruption (`route.Bump`/`route.Graze`: the plan stops; `route.Decide(from)`
  decides another way on the same route — also when the golem wakes with a plan underway; `route.DecidePast(who, me)`
  out of a peer's way), the hold (`route = g.Pause(me)` / `route = g.Resume(me)`, 14-sep-2026, the golem's own since 17-sep: the operator holds
  the golem where its body stands and that pose is kept; the route underway is held with it, plan and cursor keep,
  nothing is decided again; Resume gives the next leg its heading again from where it was held),
  touches (Bump: the fact, told, and the mark at once; HearBump/HearTouch: a peer's bump or touch; Met/LearnMet:
  it was a body, the mark comes back; Forget/LearnForget), the ending (Fail/Abandon) — every write goes through one serial
  Dispatch, tells are reaction-only. The releases build the modules (`body_v1`, `warehouse_v1`, `init`);
  what the simulator reports (a collision with a crate) is reality. The
  language was fixed on 7-sep-2026, rewritten in objects on 10-sep-2026 (local plan
  *PLAN-objetos-en-el-journal.md*; journals before that day archived as `journal-legacy-20260910-*`) and the way
  moved inside the route on 16-sep-2026 (journals archived as `journal-legacy-20260916-puntos/`):
  do not add verbs on the fly — propose them in the PLAN first. Reads (queries) may grow as the labs
  need them; writes (journaled verbs) only through the PLAN.

## Habits

- Never commit on your own; leave the working tree and offer the commit command.
- Deploy = `docker compose up -d --build`; verify with the panels (:8081 blue,
  :8082 red, :8083 green — the one on dead reckoning) and the kiosk before claiming
  anything works.
- Journals are disposable in this spike, but always say when a change makes the
  existing ones incompatible (renamed verbs, renamed upgrades).
