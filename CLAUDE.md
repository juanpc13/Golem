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
| Robot | `Golem` (subject) + `Robots.Body` | The golem is the mind (journaled); it RECEIVES its modules — `g = Golem(body, layout, collisions)` — and builds none. `Body(radius, speed, linger)` is a module of its own (`body_v1`). Its *name* is the journal's identity (env `GOLEM`), not domain state. Its *estimated position* is telemetry: it enters queries as `@x, @y`, never the journal. |
| Posición | `Geometry.Position` | The coordinate (x, y) on the Euclidean plane. Was `Waypoint`. |
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
| Ruta / trayectoria | `Routes.Trajectory` | Ordered legs; `Leg` names what the journal writes (`kitchen/north@4,9.5`). |
| Planificador | `Routes.RoutePlanner` | Dijkstra over doors, openings and detours; consults the layout AND the collisions, owns neither; `EdgeCost` generic, `DistanceCost` today. Built at query time (`RoutePlanner(layout, collisions, radius)`), never a global. |
| Maniobra de evasión | `Routes.Maneuver : Trajectory`, `Routes.EvasionStrategy` | `BackOff`, `StepAside(Side)`; the host's runtime probe still owns execution. |
| Posición / pose / rectángulo | `Geometry.Position`, `Pose : Position`, `Segment`, `Rectangle`, `Location : Position` | Pure geometry, no map concepts; the layout composes them. Constructors are DSL-visible: `Position(4.0, 9.5)` — literals with a decimal point (the engine does not coerce an integer literal into a double parameter). |

## The domain is the brain (Juan, 8-sep-2026)

"El único que toma las decisiones es el dominio, no el host." The domain decides
everything the automaton does — the road, how a mission must end, the stages to
continue, what a touch was, what to do about it — and writes those decisions in its
journal. The host only carries them out (drives the body, waits, listens) and reports
whether it could or not, so the domain resolves what follows. Consequences:
- A decision found in host C# (a classification, a retry rule, who yields) is a
  defect to migrate: it becomes a repertoire operation (a read the host asks, or a
  verb the domain concludes with), never the other way round.
- The host owns only what the papers give it: the clock, the wire, the body.
- **The journal speaks in objects (10-sep-2026)**: values enter as `@params` and the template builds or
  finds the object — `g.Visit(@id, Position(@x, @y))`, `g.Visit(@id, map.Find(@area))`,
  `g.Cross(@id, map.FindDoor(@a, @b))` — several acts per command when an errand has several stops or a
  road several legs (`g.Route(@id); g.Via(…); g.Stop(…)`). **Methods take instances, never names**: a
  string enters only where an object is created (`Area('kitchen')`, `DoorTo('north')`, constructors) or
  found (`Find`, `FindDoor`, `FindOpening`, `FindPassage`, `Knows`); everything else takes the object
  (`Connects(a, b)`, `Touches(a, b)`, `Distance(from, to)`, `PointOf(door)`, `StepInto(door, side)`)
  (Juan, 10-sep: "evitemos parámetros primitivos si tenemos las instancias reales").
- **A command template is a braced block, step by step, values as `@params`** (Juan, 10-sep): the object is
  found or built from the parameters, named after what it is, then handed to the act —
  `{ point = map.Find(@area); g.Visit(@id, point); g.Route(@id); door1 = map.FindDoor(@la1, @lb1); at1 = Position(@lx1, @ly1); g.Via(@id, door1, at1); … stop3 = Position(@lx3, @ly3); g.Stop(@id, stop3); }`
  — the errand and its whole plan, one entry. The host walks the plan in memory (the cursor is telemetry,
  like the pose) and journals only what fulfils it (`Reach`) or interrupts it (`Bump`, `Graze`).
  Names: one stop is `point`, several are `point1`, `point2`… (no numeral when there is only one; legs and
  their `@params` count from 1, as a person counts them) — Juan, 10-sep.
  The braces matter: an assignment at the top level of a command becomes a global of the actor (Fase 0,
  P2a); inside `{ }` the name dies with the block (P2b). Never a literal value in a template: every value
  is an `@param` (program–value separability, paper 02). The flat, told acts (`Reach`, `Bump`, `Mark`,
  `Forget`…) stay one-line, because a reaction captures their `@params`.
  **What is told travels flat**: an act a reaction must capture (`Bump`, `Mark`, `Reach`, `Forget`, and the
  whole touch family for uniformity) keeps primitive `@params`, because the matcher captures literals,
  `@params` and `expose` labels only — never an object variable (Fase 0 lab, NOTEBOOK 10-sep).
- **Modules are globals of the actor** (`body`, `map`, `collisions`), built in their own releases and handed
  to the golem (`g = Golem(body, map, collisions)`): a query may calculate with a module alone
  (`map.ZoneOf(Position(5.5, 5.5))`, `map.Connects('north', 'center')`, `collisions.All()`,
  `RoutePlanner(map, collisions, r)`). Auxiliary variables inside a command template become globals too:
  build the object inline or inside `{ }`.
- A Reaction may conclude for the domain (`Causation.Continue("g.Mark(...)")`), and
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

- `sim/` — the world: Gazebo Fortress in kiosk mode (noVNC :6080; the crate lever's
  buttons WITH that picture on :6081; rosbridge ws :9090). Reality is GENERATED from
  `sim/world/plan.json` at image build (walls, doors, solid blocks, bodies with contact
  sensors); its `obstacles` are empty on purpose since 10-sep — a crate is pressed into
  the RUNNING world by `sim/bridge/crates.py` (west corridor, central hall, east corridor,
  one big crate that shuts the hall wall to wall, clear all) through Gazebo's
  `/world/arena/create` and `/remove`, so every test starts from a clean floor. A lab
  lever, never domain: the golems' map still knows nothing of what stands there.
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
  contacts are ephemeral telemetry, transitions are journaled — entrusting (MoveTo/
  Cover/Follow with a `Position` or an area found, one act per stop) WITH its whole plan in the same
  entry (Route, then Via / Around / Aside / Stop, one act per leg — asked of the golem with `g.Preview`
  before the command), progress (Reach only: a stop reached implies the legs before it were walked; the
  last Reach completes — walking a door or a point is NOT journaled, Juan 10-sep: "no estar diciéndole
  cada cosa que va haciendo"), the interruption (Bump/Graze: the plan stops, another Route replaces what
  was left; also when the golem wakes with a plan underway),
  touches (Bump: the fact, told; HearBump: a peer's bump; Mark/LearnMark: the marks — a touch nobody
  else reported; Forget/LearnForget), the ending (Fail/Abandon) — every write goes through one serial
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
