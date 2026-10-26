# Golem — working rules for Claude

Golem is a spike: Puppeteer actors ("golems") driving robot bodies in a ROS 2 world simulated by Gazebo
Fortress. One golem per body, one journal per golem, a shared world with real physics and real collisions.

This file says what is IN FORCE today, as rules, in the present tense. The history — every proposal, every
adjustment, Juan's words and the labs that decided each rule — lives in `docs/` (local, never in git): the
chronicle `docs/CRONICA-CLAUDE-20261023.md` (this file as it grew from 8-sep to 23-oct-2026, frozen whole),
`docs/PLAN-Golem.md` (the proposals), `docs/NOTEBOOK-Golem.md` (the labs), `docs/PLAN-*.md` (one per big
change) and `docs/sistema/` (the system as it is, by concepts). When a rule changes, it changes HERE and its
history goes to the plan of its subject.

## Scope

- Work ONLY inside this repo (`C:\Users\Juan\source\repos\Golem`). Do not read, cite or imitate code from
  ExchangeEngine, LottoAPI, VeladaApp or any other sibling repo unless Juan explicitly asks for a specific file.
  The only sibling repos to read: `../Skills` (the guides) and `../puppeteer` (the engine's source, read-only).
- `docs/` is LOCAL (ignored by `.gitignore`; never `git add` it), as are `lab/` and `GolemTest/appsettings.json`.
  Only `README.md` and this file are versioned markdown, at the root.
- Chat and `docs/` in Spanish; code, comments, logs and UI in English.
- **At the start of every session, after this file, read `docs/GUIA-orquestador.md`** (the orchestrator's guide:
  the chat decides and documents, agents do the bulk work; its §2 says what to catch up on — `docs/CAMBIOS.md`,
  the `.md` newer than it, the framework's guides — and its §11 is this project's profile). `docs/INDICE.md`
  says what every document in `docs/` is for.

## Research discipline (Juan, 8-sep-2026)

We are scientists. A finding without a note did not happen.

- **Every lab session produces an entry in `docs/NOTEBOOK-Golem.md`** — dated, with *Observación*, *Conclusión*,
  *Ajuste al dominio* (what changes in `GolemDomain/`, or "none, why") — written as the lab happens.
- **Every change to the domain traces back to a notebook entry** that names the classes it touched. The domain
  never grows from a chat alone.
- **Documentation Juan hands over is saved first, verbatim in substance**, in the notebook, before any code.
- New journaled VERBS are proposed in the PLAN first, never added on the fly; READS (queries) may grow as the
  labs need them.
- The domain is a durable, general asset (robots, floor plans, routes) that outlives this spike: concepts are
  named by Juan's canon (the glossary below), in English.

## Doctrine (in this order of authority)

1. Juan's words in the conversation.
2. The theory: the Puppeteer papers (`https://github.com/alvaroNCubo/puppeteer-papers`) — `01` operation, not
   structure (variants per outcome, no status flags); `02` program–value separability (verbs with `@params`,
   never literals in a script); `03` reactions and partition (deferred work is a Reaction); `04` cross-actor
   continuity (a tell asserts a LIVED FACT, past tense, inside a Reaction; never an order); `05` the journal IS the
   program, nondeterminism frozen on the write path; `07` the author writes the domain, the substrate does the
   rest; `08` inference without authority (the domain knows what exists, facts are journaled testimony,
   hypotheses are derived, no `ToString`/JSON in the domain); `09` the domain declares no ports, no interfaces;
   `0A` the domain is a REPERTOIRE of operations the actor assembles into verbs. Robotics has its own vocabulary
   (occupancy grids, costmaps, waypoints); we do NOT copy it.
3. The guides: `C:\Users\Juan\source\repos\Skills\puppeteer\training-lab\guides\puppeteer-*\SKILL.md` (start with
   `puppeteer-actor-basics`). When a guide covers the case, follow it literally — no V1 surfaces, no home-made
   idioms.
4. The engine's source and tests as the ground truth of behaviour: `C:\Users\Juan\source\repos\puppeteer`
   (read-only; we consume `Ncubo.Puppeteer 3.0.0-local.586d2c9` from `GolemAPI/localfeed`).

## The domain is the brain

"El único que toma las decisiones es el dominio, no el host." The domain decides everything the automaton does
and writes it in its journal; the host carries it out (drives the body, waits, listens) and reports whether it
could. A decision found in host C# (a classification, a retry rule, who yields) is a defect to migrate into the
domain. The host owns only what the papers give it: the clock, the wire, the body.

### Rules of the domain code (`GolemDomain/`)

- The engine binds classes by SIMPLE name: every class name is unique in the assembly and no class is named like a
  namespace (`DomainLibrary`, not `GolemDomain`; `Collisions` in `Touches`; `Choreographies`, not
  `Choreography`). Known violation to fix: `Part` (the enum `Plays.Part` and the class `Formations.Part`).
- **Methods take instances, never names or keys.** A string enters only where an object is created (`Area('kitchen')`,
  constructors) or found (`Find`, `Knows`); everything else takes the object. State one verb opens and another finds
  by a key is an object the first verb returns (`route = g.Visit(…)`, later `route = g.Find(@id)`).
- **Every non-private method or constructor that receives an object checks it for null FIRST, with an explicit `if`,
  and refuses with `GolemDomainException`** — the domain's message when it has one, else
  `"Type.Method: 'param' was not given"`. Never an inline `?? throw` (except meaning "not found"), never a
  `NullReferenceException` from inside.
- **Two objects of one kind given to one method must be two different objects**: after the null guards,
  `if (ReferenceEquals(a, b)) throw new GolemDomainException("…'a' and 'b' are the same area")` — except where the
  same point is a legitimate answer (`Road(from, to)`, a `Leg` that is no door).
- Magnitudes are never negative (`Angle` excepted); a body with radius or cruise zero is refused; sizes must be
  positive. No verb takes a bare `double` for a physical quantity from the journal.
- **The domain DECIDES; the calculations are a library's**: distance, angle, projection, intersection, envelope,
  buffer, point along a line — NetTopologySuite; shortest path — QuikGraph's Dijkstra. Never write a formula the
  library has; name the operation in the domain's class and call the library inside. What stays hand-written is a
  decision (which nodes see each other, the walk zone by zone, joining marks by closeness).
- Nothing derivable is stored: obstacles, figures, disputes, cells, the deduced map, a route's way are computed
  from journaled facts every time. No `ToString` overrides, no JSON, no rendering in the domain (paper 08).
- No delegates in the domain: objects know each other by name (`Choreographies(golem)`, `Route(…, golem)`).
- Internals are visible to `GolemTest`.
- Restore with `dotnet restore Golem.sln --configfile GolemAPI/nuget.config` (the Puppeteer feed asks for
  credentials and hangs a plain restore).

### Rules of the journal (the scripts)

- **The journal speaks in objects.** Values enter as `@params` and the braced template builds or finds the object,
  named after what it is, then hands it to the act: `{ from = g.Destination; point = Position(@x, @y); route =
  g.Visit(from, point); … }`. Literals are written with a decimal point (`Position(4.0, 9.5)`): the engine does not
  coerce an integer literal into a double. Reads speak in objects too (`g.FitsAt(Position(@x, @y))`,
  `g.Underway().NextLeg.Target.X`, `g.Current.Map.ZoneNameOf(…)`).
- **Templates are FIXED**: written once, parameters only, never assembled with a StringBuilder. Never a literal
  value in a template (paper 02). The braces matter: an assignment at the top level becomes a global of the actor;
  inside `{ }` the name dies with the block. Build every auxiliary object inside the braces, one variable per object.
- **Every script is spoken WHOLE inside its `robot.Actor.Using(check, script)`**, in the role that owns the act
  (`Choreography/Roles/*Rol.cs`): no template constants, no helper, no lambda; a Check repeated by two verbs is
  written in both; only the uptakes are `public const string` (GolemSpeech binds them). The method is the same
  shape everywhere: the validated parameters, one `Using`, its answer returned to the caller.
- **The Check is a guard**: it only decides whether the command runs (`Check(g.Choreography.Current.Reached)`); it
  never computes inputs for it; `Error` (or `Warning`) refuses, nothing is journaled.
- **A command's print is the body's next order, in the robot's words**, asked of the route the act was on, inside
  the braces: `print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
  if (route.IsWalkable) { print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name', route.Target.X 'x',
  route.Target.Y 'y', route.Target.Heading 'heading', route.Following 'following', route.StopsLeft 'stopsLeft'; }`
  — `Order` is `advance` | `back` | `turnLeft` | `turnRight` | `stop`, or how the route ended; `Amount` metres or
  radians measured from where the body stands. Every report of the body and every decision on the route underway
  writes `route = g.Underway()` first (no id enters the act). The errand starts from the domain's own answer,
  `from = g.Destination;` — a pose from the body enters only as its report of where it really stood (the bump, the
  hold, the resume, the waking, the halt, the retake, the run that follows an arrival).
- The `if`/`else` of a script keep their line breaks and braces (`if (g.Strategy.OnTheWay.IsActive) {` on three
  lines); the strategy is applied by the script with the route in hand: `route = g.Visit(from, point); if
  (g.Strategy.OnTheWay.IsActive) { route = g.Dash(route); } print …`.
- **A parameter is never named like a print label** (`px py ptheta`, not `x y`): the canonical render substitutes a
  parameter wherever its name appears. **A local is never named like a parameter**: it resolves to the parameter
  (`side = Meters(@sideLength)`, never `@side`). A hyphenated name in a release becomes camelCase
  (`north-aisle` → `northAisle`); a hyphen before a DIGIT stays an underscore (`cell-1-19` → `cell_1_19`). A block
  local named like a global of another upgrade refuses the whole boot (`floor`).
- The DSL reads `.Count` on any list a method returns, not on a bare `IEnumerable` property.
- **A script's local is never named like a GLOBAL of a release**: the maps (`warehouse`, `openFloor`, `maze`, `crossCorridors`, `road`), the scenarios, `g`, `body` — the engine binds the global's type and refuses ("Type Traffic does not inherit from MapLayout", 23-oct-2026).
- **What the domain would refuse INSIDE a command is refused in the Check, or by the host before the act**: a verb that passes its Check and throws inside is journaled whole, and replays as a logged error (`--link ask`, a `use map` with a route pending — 23-oct-2026).
- **An enum travels as its MEMBER'S NAME**: the host passes `p["sense", typeof(string)] = "Clockwise"`, the engine
  resolves it at the verb; `typeof(Enum)` cannot be exposed. The domain's small fixed sets are enums (`Sense`,
  `Policy`, `Link`, `Part`, `Direction`, `PlayState`), never strings.
- **Nobody dispatches the print: the engine pushes it.** A command's print is PULL (returned to the caller — a
  refused Check returns `{"EWI":[{"Error":"…"}]}`, recognised BY ITS SHAPE, the top-level `EWI` property, never a
  substring nor emptiness; `Answer.Of`); only a Reaction's emit is PUSHED: `RobotMechanics` declares ONE reaction per
  act shape (`next-order-underway` on `[_:Golem].Underway()`, `-bump`, `-visit`, `-convene`, …), each emitting the
  same print, written in full wherever it is spoken, the route found ONCE in a local inside the guarded block. Many
  reactions on one act shape made the push flaky: one per shape, no more.
- **A reaction captures what a tell needs FROM THE ACT**: literals, `@params` in argument positions and `expose`
  labels — never a local. A constructor pattern BESIDE the act casts the object built the line before
  (`Pose($bodyX, $bodyY, $bodyHeading) [_:Golem].Bump(_, $bearing)`); a constructor nested inside an argument does
  not parse. An `expose` exists ONLY for a value the act does not contain; ONE `expose` per script (a second one
  unbinds the first's parameters); `expose` takes any primitive expression inside the block.
- **A reaction with an expose must never observe an act an uptake also writes** (the engine cannot read a
  `Told` action's arguments): what an uptake concludes is announced by THE CLOCK, which asks a read every 2 s and
  writes the act (`Placed`, `Announce`, `Cue`, `Finish`…). A reaction's pattern names the type the SCRIPT DECLARES
  the variable with, not the instance's (`[_:Play].Announce()`, not `[_:Pacman]`).
- The engine's `remember (@firstAt, @everyTicks, @timesAtMost) { … } while (cond)`: the count is ALWAYS given (omitted
  it is one); the start travels as a `DateTime` parameter; the activations replay and the lapsed ones come as a
  burst bounded by the count; the host's `AttentionClock` only knocks.
- Engine race to know (2.0.1 and 3.0): a command and a tell's uptake written in the same instant can take one
  action id (`TakeAndIncrementActionId` under two locks); a show whose tabs fire several lines a second while the
  fleet's words arrive is where it shows. Reported to Alvaro; nothing of ours to fix.

### Journals and compatibility

- The journal (`./journal/<golem>/`, FileSystem) is the only truth; pose and contacts are ephemeral telemetry.
- **A change in what the domain decides makes the existing journals non-replayable** — the planner, the
  corrections, a verb's signature, who waits for whom in a round, a tell's uptake removed or changed: an act
  accepted live is refused on replay. ALWAYS say so, and archive the journals in the same deploy: rename
  `journal/` to `journal-legacy-<date>-<topic>/` (ignored by git). A new act or a new read keeps them compatible.
- A replayed journal that no longer fits is refused at that entry; the golem goes on with what it accepted up to
  there, which is not the whole history. Archive, do not hope.

## Domain glossary (Juan's canon → `GolemDomain/`, in force)

| Juan says | Class / namespace | What it is today |
|---|---|---|
| Robot / golem | `Golem` (the subject) + `Robots.Body` | The golem is the MIND, journaled as `g`; born with its body and its name (`g = Golem(body, @golemName)`; `Golem(body)` in the tests, a nameless golem is in no fleet). `Body(radius, speed, linger, retreat)` (`body_v1`: 0.25 m, 2 m/s, 6 s, 0.6 m), its turn rate 1.5 rad/s; `body.CruiseAt(pace)`, `body.TurnAt(rate)` (zero refused) — the line `speed <m/s> [--turn <rad/s>]`. The golem knows where its body stands (`g.Standing`, `g.Destination` — where its next errand starts, `PlannedEnd()` while busy), wakes with `g.Wake(me)`, is held with `g.Pause(me)` / `g.Resume(me)` (the hold is the golem's, not the route's), is touched with `g.Bump(me, bearing)`, stalls with `g.Stall(me, why)`. It wraps nothing: what a module answers alone is asked of the module. |
| Posición / pose | `Geometry.Position`, `Pose : Position`, `Location`, `Segment`, `Rectangle` | Pure geometry, no map concept. `Position(x, y[, z])`; `Pose` adds the heading (radians, counter-clockwise). |
| Magnitud | `Units.Length`, `Duration`, `Speed`, `AngularSpeed`, `Angle`, `Acceleration` (abstract) → `Meters`, `Centimeters`, `Seconds`, `Minutes`, `MetersPerSecond`, `RadiansPerSecond`, `Degrees`, `MetersPerSecondSquared` | The abstract class says WHAT, the concrete one is the UNIT the journal writes; read in the SI base (`InMeters`, `InSeconds`, `InRadians`). |
| Maqueta / mapa | `Maps.Map` (abstract), `Area`, `Passage` → `Door` ("a/b"), `Opening` ("a~b") | What a map DISPOSES, not one coordinate: areas, passages, connectivity. Never instantiated. |
| Plano | `Layouts.MapLayout : Map`, `Zone : Area`, `Wall`, `PlacedDoor`, `Catalog` | The concrete map, built in TWO MOVEMENTS — the areas as variables with their rectangle, then the passages between OBJECTS (`kitchen.DoorAt(north, Position(4.0, 9.5))`, `north.OpenTo(center)`); `map.Find('kitchen')`. Answers as a maquette and as a layout: `ZoneOf`, `IsOnMap`, `HasRoom`, `IsWallAt`, `Zone.Walls()`, `Crossings(from, to, radius[, throughDoors])`, `Pivots`, `IsNarrow`/`Middles`, `AsRelease([staged])` (the release text, verbatim; `MazeReleaseTests` holds the maze's equal). Catalog: `warehouse` (home, 11 × 11), `cross-corridors`, `ring-corridor`, `open-floor` (one area `floor`, the choreographies'), `maze` (drawn in text, `Catalog.MazeDrawing`, 19 × 21 cells of 1.5 m, `cell-<col>-<row>` open to its free neighbours, `P` the cradle, `G` the den; the world is built from the same drawing); `Blind(w, h)` for the tests. Constants: `DoorClearance` 0.6, `OpeningMargin` 0.5, `BodyMargin` 0.1, `DoorGap` = half the width − 0.15 (a touch on a jamb is a wall), `WallThickness` 0.15. |
| Escenario | `Scenarios.Scenario` | A map WITH what the bodies learned touching in it — its own `Collisions`, born empty — read in a `Frame`, `Exported` or a release's. `g.Stage(map)` makes it, `g.Enter(scenario)` enters it (refused with a route pending; a scenario built outside is learned as entered), `g.Current` (`.Map`, `.Collisions`, `.Name`, `.Frame`), `g.Scenarios()`, `g.Scenario(name)`, `g.Export(map)` (an exported scenario, droppable with `g.Drop`), `g.IsIn(name)`. NO global `map` or `collisions`: every script reads `g.Current`. Release chain: `init (g = Golem(body, @golemName)) → body_v1 → warehouse_v1 (staged and ENTERED: home) → open-floor_v1 → maze_v1 → cast_v1`; every map's release ends with the golem staging it. `use map X` CARRIES THE BODY to the same point of X only when X is read in ANOTHER frame than the one left and that point is on X (off it, the golem says "place it"; the same frame — the scenario it is in already, an exported map of it — carries nothing); a body carried keeps its heading and the host waits for it to settle. The golem WAKES IN THE SCENARIO WHOSE REGION HOLDS ITS BODY (reborn, let go, carried, `anchor`), unless a route is pending. `reset` lets the routes go, carries the body to `HOME_AT` and wakes it there (the journal stays); `reset --all` wipes the journal and reboots reborn. Lines: `use map <scenario>`, `scenarios`, `forget map <name>` (exported only). |
| Colisiones / marca / obstáculo | `Touches.Collisions`, `Mark`, `HeardBump`, `Obstacle` → `Thing`, `Peer` | The module keeps the FACTS (marks: a point and its normal; peers met: a body that touched me, in the way while the route it was met on lasts; bumps heard) and DERIVES the rest every time: obstacles (marks joined within 1.0 m: a point, a line, a polygon), `Present()`, `PeersInTheWay()`, `Figures(radius)` (a thing's box grown by a mark's reach 0.25, a margin 0.1 and the body: where the body's centre may not go — what blocks a way is the figure, not the vertex). A formation's berths are the ROUTE'S COMPANY (`Route.Company`), planned around, never obstacles. `forget x,y` drops a thing with all its marks or a peer met, and tells the peers (`ObstacleGone`). |
| Ruta / tramo / trayectoria | `Routes.Route`, `Leg`, `Trajectory`, `RouteStatus`, `Courtesy` | ONE object for the errand and its way, decided INSIDE at birth (`Visit(from, point)` the stops in order, `Cover` the golem's order, `Follow(point)` a peer's point; `Then(point)` one more stop; `g.Dash(route)` the same route taking the doors on the way, idempotent, applied by the script when `g.Strategy.OnTheWay.IsActive`). The cursor: `Turn(me)`/`Reach(me)` per leg, the arrival says THE LEG (`leg = route.NextLeg; route.Arrive(leg);`), the last stop completes. `Order`/`Amount`/`Target`; a turn under `TurnTolerance` 0.02 is not asked; a stop within `WithinAHair` 0.05 with a leg after it is reached at birth. **The touches correct the way inside**: the golem reckons the touch ONE RADIUS of its body from the centre in the direction heading + bearing, facing into the thing (`Golem.TouchOn`; the body reports only where it stood and where on its shell it was pressed), then `Touched` → `Graze` (`map.IsWallAt(touch, 0.3)`: a wall the map knows, own error, the third on a leg fails) or `Bump` (a thing: marked, the seventh fails); the first correction is `back` (touched on the front half) or `away` (from behind), then the courtesy step `aside` to the body's own right (left if it does not fit), then the planner's way around the figure; no way from the retreat → `PlanAgainFrom`; none → fails. `Unbump` annuls the last bump when a peer's word says it was its body. Legs: the plan's (`door` with approach and exit, `opening`, `via`, `middle`, `stop`), the corrections (`back`, `away`, `around`, `aside`), `face` (the turn to face a formation's centre). States the host reads: `IsPending()`, `IsWalkable`, `Waiting`, `Yielding` (a `stop` WITH a ticket, `Order.IsHalt`), `Paused`, `Status`, `Why`, `EndedShort`, `EndedByTouch`, `IsProbe`. Endings `Fail(why)`, `Abandon(why)`. A follower's last stop is met `FollowerStandoff` short. |
| Estrategia | `Routes.Navigation` → `DoorByDoor`, `OnTheWay`; `Strategies` (`g.Strategy`) | HOW a route takes the doors: every door a leg with its approach and exit (active at birth) or the doors' clearance points as nodes and a straight run crossing them on the way. Two objects born in the golem, a property each (`g.Strategy.OnTheWay.Activate()`, one active at a time; the direct one cannot be switched off); never constructed or found by a string from the journal. Every route is BORN door by door and the SCRIPT improves it with `g.Dash` under the `if`, so that the decision SHOWS IN THE JOURNAL (an `optimize` with a route underway improves it at its next arrival). Line `optimize on-the-way|door-by-door`. |
| Planificador | `Routes.RoutePlanner`, `EdgeCost` → `DistanceCost` | Dijkstra over the nodes that see each other — the doors as the strategy offers them (the point, or the clearance points 0.6 m into each area), the pivots of the openings, the MIDDLES of narrow zones (a zone is narrow when `min(W, H) − 2·OpeningMargin ≤ 2·radius`: the way goes SQUARE through it, never a diagonal that cuts a bend), the corners of the figures, start and goal; an edge is a straight run that stays on the map (`Crossings`: openings crossed away from the corners, doors within their clear window) and enters no figure; at equal length fewer stops. Built at query time, consults the layout and the collisions, owns neither. `BestOrder` tries the permutations up to seven stops, nearest neighbour after. |
| Formación / figura / flota | `Formations.Formation` (`Holder`), `Figure` → `Polygon` (`Triangle`, `Square`, `Pentagon`), `Circle`, `Propeller`; `Fleet`, `Member`, `Move`, `Dispute`, `Throws`; enums `Sense`, `Policy`; the module `Choreographies` (`g.Choreography`) | A figure: a centre, an orientation (`Degrees`), a measure (a side; a radius) and N PLACES in a fixed order (`Places(n)`) so every golem computes the same alone: the vertices first (the square's first corner north-east, the triangle's and pentagon's apex north, counter-clockwise), the rest on the sides; the propeller has one body at the centre and blades with rungs. A formation has a NAME (its identity across the fleet), a stamp (the once of its call, the host's), its fleet (names sorted = rank), its policy — `Rank`, `Distance` (who stood nearest, the law of pairs, ties by name then place), `RockPaperScissors` (places wanted by several are played with hands drawn from stamp and name, `Throws`) — and who holds which place (`Holders()`). **The warden writes the same `form …` and `take <name>` on EVERY member's tab** (no relay by tell); the ROUND is words between members: `formation.Convene(from, me)` (my word: where I stand; the word that completes the round opens every route, the others' berths as company), `Stood(peer, at[, nonce])`, `Placed`/`Heard`, `Aligned`/`HeardAligned` (every route to a place ends FACING THE CENTRE and STARTS TOGETHER: it waits lined up until every mover of the round said so, then `Go`), `Queue(move, stepId)`/`Step()` (a step moves everybody one place in the sense; free places move with them), `Halted(me)` (a route that yielded is replaced), `Retake(me)` ONCE for a route that ended short, then `Stranded`; `TakeAgain(me)` on the warden's `take` again; `Finish(me)`/`HeardFinished` (my part is done, counted by the clock when it is); a word heard AHEAD of its formation is kept (`Overheard`) and taken in by the `Form` of that name; every word is kept to the millimetre so every copy reckons the same law; a take refused inside a word is a route BORN FAILED (`FailToTake`), never a refused uptake. Reads: `formations`, `disputes` (derived: who yielded what to whom; `forget disputes` clears). Lines: `form <name> square|pentagon|triangle --center x,y --side s [--angle a] [--fleet a,b] [--by rank|distance|rps] [--places n]`, `form <name> circle … --radius r --places n`, `form <name> propeller --center x,y --radius R [--inner-radius hub] [--blades n]`, `take <name>`, `rotate <name> clockwise|counterclockwise`, `finish <name>`, `dissolve <name>`; `--with` refused on all of them. The double ring and the timed `Rotation` are gone. |
| Seña y pregunta (entre formaciones sueltas) | `Formation.Cues/StartsOnCue/Cue/TakeCue`, `Follows/ArmInquiry/Ask/HeardNotYet`, `Choreographies.Answer` (`Response`); the engine's `remember`; `Membrane/AttentionClock` | Two formations dance one after the other with NO coordinator. PUSH: the first is told `--cue <formation>`, the second `--on-cue` (born waiting, no take); when every member of the first counted its `finish`, the copy that counts the last gives the cue (`echo-cue` → `YourTurn(to)` with the FORMATION'S name) and every member of the cued one takes it by itself (its own `StoodFor`). PULL: the second is told `--after <formation> [--every 5s]` (born waiting like `--on-cue`: no take; its take is the answer); its ASKER (the first by rank; of an open formation, the first MEMBER once resolved) arms the engine's `remember` from the clock (`ArmInquiry`) and asks `DidYouFinish` every N s; the first by rank of the formation asked answers `FinishedState` from its own copy; `true` is the cue. `Formation.StillAsks(name, stamp)` is what the `remember`'s `while` reads. A finished formation stays, and always moves. Guide: `docs/GUIA-coreografias-push-pull.md`. |
| Sindicato / partitura | `Formations.Union` (`Share`), `Score` (`Part`), `Move.Rest`, enum `Link`; `Choreographies.Union/Open/Program/Unions/DissolveUnion` | Several OPEN formations, in order, that SHARE OUT the candidates by the law of the policy (distance or rps; by rank nobody chooses) in ONE round of words with the union's name (`union.Convene(from, me)`; `take <union>`), the same law in every copy (`Union.Resolve`: pairs candidate–place of any figure from the shortest; a full figure sends the next one on; the farthest left out; a figure that gets nobody leaves it unresolved and says why). The union ADMINISTERS the sequence: every formation is taken at once; the k-th step queued in each formation is the k-th BEAT, danced together (`Union.CanStep/Step`, a formation without a step rests), the start of a beat waits for every mover of every formation (`LetGoIfAligned`), every copy keeps the whole score (`Choreographies.Hears`), `IsDone` when every member finished; HOW the beats are agreed is the union's `Link` (`Cue` today; `Ask` refused "not built yet"). An open formation admits no `--cue`, `--on-cue`, `--after` of its own. ONE line makes everything — `program <union> [--fleet a,b] [--by distance|rps] [--link cue|ask [--every 5s]] --form <f> triangle|square|pentagon|circle <cx,cy> side|radius <m> [angle <g>] [places <n>] … --score <f>=cw,ccw,-,…` — the definition read by the domain (`Score.Read`, drawn in text like the maze), one act `g.Choreography.Program(…)`: the union, each formation formed with the union's stamp and opened in it, its steps queued (`-` a rest), its finish implied, and this golem's word. The loose lines (`union <name> <f1,f2> [--fleet] [--by] [--link] [--every]`, `form … --open [--places n]`, `take <union>`, `rotate`, `finish`) stay for composing by hand; `dissolve <union>`; reads `unions` (`GET /unions`). A member stranded is taken again with `take <union>`. Plan: `docs/PLAN-sindicato.md`. |
| Dinámicas / obra | `Plays.Dynamics` (`g.Dynamics`), `Play` → `Pacman`, `Cast`; enums `Part`, `Direction`, `PlayState` | Not a choreography: a PLAY, intentions that meet in space and a law that resolves them. `play <name> pacman --pacman <golem> --ghosts a,b --as pacman|ghost` on EVERY member's tab (`g.Dynamics.Stage(@playName, @kind, cast, @part, @stamp)`, the part checked against the cast), `start <name>` on every tab (each member's own start, no barrier), `play over`, `forget play <name>`; reads `play`, `plays`. Pacman is AUTONOMOUS: the rule alone — straight on, else the right of the heading, else the left, else back — to the next cell where straight on is closed (`NextRun`, `g.Dynamics.Run(me, play)` through `Golem.RunTo`: square, one advance, no facing); every run is told (`RanTo`); a ghost chases where pacman was last seen when idle (`Chase`), leaving the den staggered by pacman's runs (the k-th of the cast at run 1 + 2k). THE LAW: the domain concludes the end where it hears the encounter — `HearBump` → `Dynamics.Met(who)`: a ghost and pacman touched, the play is OVER on both sides; the clock announces it (`Announce`, `GameOver`). The run after an arrival opens from the pose the arrival brings (the drift in a corridor). The pacman verbs are written on `g.Dynamics.Pacman` (the engine binds the declared type). **THE TRAFFIC PLAY** (propuesta 125, 23-oct-2026; `docs/PLAN-semaforo.md`): `Traffic : Play` — a LIGHT (the cast's lead) and CARS that know their journey (`Journeys`: `red:straight,green:left`, a `Turn` each) on a ROAD OF ONE LANE, `cross-corridors` (the `Crossing`: derived from the map, the one zone open on four sides to aisles; its arms by compass point; the stop line 0.75 m from the crossing's edge on the arm's axis; a car exits by the arm its turn says and ends at that arm's far end). `play <name> traffic --light <golem> --cars a:straight,b:left,c:right --as light|car` on every member's tab, `start <name>` on every tab: a car's drive is ONE route with three stops and a GATE at the stop line (`Route.Gate`: reached, the route waits — `stop`, no ticket — until `Go`; a boot keeps it waiting; touched there, it backs off and waits at the line again); the WORDS are facts (paper 04): the arrival that leaves the route waiting writes `g.Dynamics.Stopped(me, play)` (→ `StoppedAt`), the light's clock writes the permit `g.Dynamics.Grant(play)` (→ `GreenFor`; the car named writes `g.Dynamics.Go(play)`, its gate opens), the arrival past the exit point writes `g.Dynamics.Cleared(me, play)` (→ `Cleared`). THE LAW is the light's, on its own journal (the one part that observes the order of arrival — a coordinator, legitimate here): one car at a time, the first that stopped WHOSE EXIT ARM IS FREE (`IsBlocked`: another waits at that arm's line, holds the crossing coming from it, or parked at its far end); every car cleared, the play is over in every copy (the end abandons no drive: a car finishes its journey). THE QUEUE AND THE TWO LANES (§12 of the plan): the road is **`road`** (four arms of 3 m, two lanes each, 6 m long, a `junction` of 3 × 3, a corner room at each end; its zone names differ from cross-corridors' on purpose — a release's block locals come from them and two upgrades sharing a local refuse the boot); a car's journey says the ARM it comes from (`--cars blue:south:left,red:south:straight`), and the cars of one arm QUEUE by name — the first at the stop line on the inbound lane (the right of a car coming to the junction), the next a metre behind each; EVERY car says `StoppedAt` with its place (`slot`) when it reaches it — the light queues the line's words alone; a car behind is born lined up and WAITING TO SET OUT (`Route.StartTogether`) until the one ahead said it stands at its place (`MaySetOut` → `g.Dynamics.SetOut(play)`; asked in the uptake, at the arrival that lined it up and from the clock); when the car ahead CLEARED the junction (never at its green: it is still turning to leave), the next advances by itself (`MayAdvance` → `g.Dynamics.Advance(me, play)`, the old route abandoned, a new one with its gate at the nearer place; `next-order-advance`); at its green the car leaves the route that waited at the line and opens THE WAY THROUGH (`Traffic.Through` → `Golem.DriveThrough`: ALONG ITS LANES, square — the TURN POINT where its inbound lane meets the outbound lane of the arm it leaves by (`Crossing.TurnPoint`; none going straight; the centre with one lane), the exit point, its parking place; no gate — never the planner's pivots, whose run between two openings' pivots passes 2 cm from the corner they share: orange touched the wall at every right turn), parking at its arm's far end on the outbound lane the k-th GRANTED among those leaving by that arm k metres short (the greens' order, every copy alike). The Traffic dialog writes `@sync` between the places and the play. On a two-lane road nothing blocks by exit arm; cross-corridors stays the one-lane road. The light is a golem with a body parked in a corner (no bodiless actor yet). |
| Exploración | `Trails.Explorations` (`g.Exploration`), `Trail`, `Stride`, `Cell`; the probing `Route`; `ExplorerRol` | The blind one who touches everything. FACTS: the trail (one per scenario, every stride the body walked, fed from `Golem.Stood`, anchored where the body is carried) and the scenario's marks. HYPOTHESIS, derived: `Cells()` (grain 1.5 m: free where the trail crossed, wall the first cell along a mark's normal), `Frontier()`, `Deduced(name)` (a `MapLayout`, a zone per free cell), `Drawing()`. POLICY: the nearest frontier. `explore` is ALWAYS BLIND: `if (g.Exploration.Sighted) { blind = g.Exploration.Blindfold(); g.Enter(blind); } g.Exploration.Start();` (a scenario with NO map, read in the frame of the one left); then, whenever the golem stands free, `route = g.Exploration.Probe(me)` — a route the golem lays itself, cell centre to cell centre and one step into the unknown; a touch ends it and brings the body back to the centre; a stall on a probe is a touch (`g.Stall`); `Concluded` when nothing unknown is left next to the known, the clock writes `Finish()`; `explore stop`, `explore clear`, `export map <name>` (`g.Export(explored)`: a scenario of the golem's own, `use map` enters it). `BumpedAt` carries its scenario: a touch lived elsewhere leaves no mark here. Plan: `docs/PLAN-exploracion.md`. |

## The host (`GolemAPI/`)

- **Names**: `GolemEmbodiment` (the golem given a body: the mind's own acts — wake, let go, anchor, enter —, the
  clock, the wire; it builds the roles the operator declared), `Choreography/Roles/*Rol.cs` (ONE ROLE PER MODULE
  OF THE DOMAIN, named with the suffix `Rol`, each owning EVERY `Using + script` of its acts and the uptakes of its
  words: `DisplacerRol` (motors), `BumperRol` (touches), `ChoreographerRol` (formations), `DynamicsRol` (plays),
  `ExplorerRol`), `RobotMechanics` (the actor's OUTPUT TARGET: the next-order reactions, the dispatch to ROS),
  `GolemSpeech` (the tell reactions `echo-*` and the `Told` uptakes), `Membrane/Rosbridge` (the WIRE: websocket,
  topics, telemetry; knows nothing of orders), `Membrane/HttpBroker` (the tells: `POST /tell`, answered on receipt,
  one consumer per topic, an ack once), `Membrane/WorldLayout` (`MAP_ORIGINS`: where each map lies in the world is
  the WORLD'S fact, translated by the host at its edges — the pose is read in the frame of the scenario the golem
  is in), `Membrane/ClockSource` (the clock as an INPUT SOURCE: a tick every 2 s through the actor's Dispatch; the
  tick is not journaled; `OnTick` runs the roles' questions — Check-then-command, nothing written unless the domain
  says yes — and `AskOrder`), `Membrane/AttentionClock` (the `remember`'s clock: it only knocks).
- **Capabilities** (`ROLES` in the compose: `displacer,bumper,choreographer,dynamics,explorer`): a role on the motors
  needs `displacer`, `explorer` needs `bumper` too; a verb of a role the golem lacks answers 409 naming it.
- **The body gets its own words alone** (`/golem/<body>/order`: `{"order", "action", "amount", "speed", "rate"}` —
  a TICKET — a counter the mechanics stamps —, `advance|back|turnLeft|turnRight|stop|continue`, metres or radians; nothing of the route; `continue` is the mechanics' own word for the same order held and back again, never a route's `Order`) **and says only
  what came of them** (`/golem/<body>/result`: `done`, `bumped` with `{x, y, heading, bearing}`, `stuck` with a
  reason); the ticket tells a stale word. The same order twice is not resent; a different one replaces it. The
  bumper is a switch: the body backs off NOTHING on its own. After a teleport, `stop` with `anchor`.
- **The controllers validate JSON and call the role** (`Controllers/Requests.cs`: every request says what is wrong
  with it, 400; points only, never places). Every verb has its `POST`, every read its `GET`, the levers theirs;
  `/command` runs a LINE of the shell-like language (`Commanding/CommandLine.cs` reads it, `Commander.cs` turns it
  into the same request and the same role call, decides nothing; 200 text or JSON with `Accept: application/json`,
  400 no command, 409 refused; `GET /commands` the help). The language is NEVER the actor's DSL (`/query` stays
  apart, for the lab). `--with a,b | all` carries the same line to those peers (`CommandPeerAsync`: host plumbing,
  not a tell); refused on the formation, union, play and exploration verbs, which the warden writes on every tab.
- The fleet: six golems — blue (:8081), red (:8082), green (:8083), yellow (:8084), orange (:8085), cyan (:8086) —
  every one with tell routes to the other five; the body nodes' words are in the kiosk session's logs inside the
  container (`docker exec golem-sim grep body_green /home/ubuntu/.vnc/*.log`), not in `docker logs golem-sim`:
  read them before blaming the domain for a `stuck`.
- Known host debts: the membrane does NOT reconnect after rosbridge drops (restart the golem containers after a
  simulator rebuild); the peers of a stranded member keep waiting (a `StrandedAt` word would free them).

## The world (`sim/`)

- `sim/world/plan.json` is composite — `maps: [{name, at, …}]`: `open-floor` at x 0–11, `warehouse` at 13–24,
  `maze` at 26–54.5 — and `build_world.py` lays each map at its `at` on one floor at image build (walls 0.15 m,
  doors as gaps of 1.4 m, blocks spanning each map's WHOLE region, bodies with contact sensors; physics 4 ms;
  `obstacles` empty on purpose) and writes `maps.json`; `MAP_ORIGINS=open-floor=0,0,11,11;warehouse=13,0,11,11;
  maze=26,0,28.5,31.5` (set per golem in `docker-compose.yml`) tells the golems. Two published ports: `:6080` (the kiosk, noVNC's index with the lab's
  levers: crates, the camera over one map) and `ws :9090` (rosbridge). The levers publish through rosbridge
  (`crates.py`, `teleport.py` — `set_pose` up to three times, asking the odometry first).
- The bodies are ROS nodes (`sim/bridge/body.py`, one per body, launched by `kiosk.sh` with `python3 -u`): they do
  ONE thing at a time, measured on their own odometry, never against a point on the plane.
- Deploy = `docker compose up -d --build`; verify with the console and the kiosk before claiming anything works.

## The console (`WardenCli/`)

The operator's desktop program (WPF, .NET 9, DARK theme always), a thin CLIENT of the golems' HTTP surface that
decides nothing for the golem. Rules in force:

- The ribbon holds the CONSOLE'S OWN operations alone (workspace, clipboard, clear tabs, Formation / Unionize /
  Pacman dialogs); the GOLEM'S commands live in the centre, above the script they compose on; the golem's
  ENVIRONMENT (scenario, navigation, speed, anchor, reset, *apply to all golems*) is acted AT ONCE, at the foot of
  the golems' column, by the endpoint (`GolemClient.Endpoint(line)`; a 404 falls back to `/command`).
- The golems' boxes ARE the tabs; a tab's script IS its queue: SEND runs the CHECKED golems' queues (which golems go
  is said by the checks alone), one line at a time, waiting for each route to end, and at the boundary of a
  formation or a union until the whole group is done (three minutes without words: say so and KEEP waiting);
  `@sync` is the console's own barrier. The console writes COMMANDS ALONE on the tabs: no comments; what each line
  did is said in the log.
- The READS are not buttons: `where`, `state`, `route`, `obstacles`, `formations`, `unions`, `disputes`, `play`,
  `explored` are asked of the SELECTED golem on demand (*refresh*, or *auto* every 3 s) and shown as a card and
  on the map; the map draws the selected golem's SCENARIO as the golem tells it (`GET /map`), its obstacles, the
  formations as the golem's read answers them (the console never shows a step of its own), the explored cells only
  while the exploration tab is open; every click on the floor adds a point to a `visit`/`cover`; *⌖ place* arms
  one teleport.
- **The console has no formations of its own**: `WardenCli/Formations` keeps the GEOMETRY to draw and compose
  lines; the formations tab is the golem's `formations` READ plus the `form` lines on tabs not sent yet; ↻ ↺ write
  `rotate` on every member's tab; ✕ asks the golem to dissolve (after asking); the grips make a DRAFT and *shot*
  writes the `form` again; the Formation dialog writes `form` + `take` (+ `finish`) on EVERY member's tab, the
  Unionize dialog ONE `program` line per tab, the Pacman dialog `play … --as` on every member's tab.
- A WORKSPACE (`.golemws`, version 3) is the golems and what the operator WROTE on each tab (never what the queue
  left); a SHOW is the same format loaded into the tabs without becoming the workspace (`shows/by-rank`,
  `by-distance`, `by-rps`, `pacman.golemws`). Pane sizes and the checkboxes are remembered in the machine's
  settings (`%AppData%\WardenCli`).
- Build and run: `dotnet run --project WardenCli`; kill `WardenCli.exe` before building, relaunch it after.

## Testing (Juan, 21-sep-2026)

- **The domain is tested as OBJECTS, in C#** (`GolemTest/EndToEnd/`, one file per class under test): every test
  INSTANTIATES ITS OWN OBJECTS in its first lines, as the releases build them, and GOES THROUGH THE GOLEM — a mark
  is born of `g.Bump(new Pose(…), 0.0)` or a peer's `g.HearBump`, never of `collisions.Mark`; a way of
  `g.Visit(from, stop).AsPlan()`, never of `planner.Road` — and reads where the journal reads. Every value is
  written where it is used; a refusal is `StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(…).Message, …)`.
  The one helper a class keeps is the body's walk (`Step`, `Reach`, `WalkToTheEnd`), private to it.
- `GolemTest/Integration/` runs the actor with the assembly: `JournalAcceptanceTests` (what only the engine can
  show: an act accepted or refused at write time, a reaction firing, a release applied) and the labs (`*LabTests`,
  what the engine does). The scenarios' asserts are what the EMULATOR tells, one leg at a time, with the pose the
  body REPORTED (`World/BodyReports`), never the golem's plan.
- The host's own unit tests (`AnswerTests`, `CapabilitiesTests`, `RequestValidationTests`, `CommandLineTests`…) at
  the project's root; the console's in `WardenCliTests`.
- **Always `GOLEM_LAB_WORLD=mock`** for the suite: the local `appsettings.json` points at gazebo and the scenarios
  move the live fleet.

## Habits

- Never commit on your own; leave the working tree and offer the commit command. Commit only when Juan says
  "commit y push", on `master`; never `git add` `docs/`, `lab/`, `GolemTest/appsettings.json`.
- Every change in what the domain decides archives the journals in the same deploy, and says so.
- Line endings: `core.autocrlf=true` (`.cs`/`.md` CRLF in the working copy, `sim/kiosk/kiosk.sh` `eol=lf`). A
  Python `write_text` on Windows writes CRLF: never let it touch a `.sh` (a CRLF kiosk script means no world).
  Never mass-convert line endings.
- Long Python edits go to a script in the scratchpad run with `python -I`; files written CRLF.
