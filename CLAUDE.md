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
| Robot | `Golem` (root) + `Robots.Body` | The golem is the mind (journaled); `Body` its size/speed/linger. Its *name* is the journal's identity (env `GOLEM`), not domain state. Its *estimated position* is telemetry: it enters queries as `@x, @y`, never the journal. |
| Posición | `Geometry.Position` | The coordinate (x, y) on the Euclidean plane. Was `Waypoint`. |
| Ubicación | `Geometry.Location : Position` | A position that means something on the map (a corner, a jamb, a mark). |
| Segmento | `Geometry.Segment` | A straight run from position i to j. A `Wall` is one. |
| Mapa / plano | `Plans.FloorPlan` | One plane, its places, passages and marks. Was `Atlas`. Levels and layers: future. |
| Espacio / habitación | `Plans.Place` | A rectangle (polygon later), ≥ 4 corner locations, its walls. |
| Pared | `Plans.Wall : Segment` | A boundary of a place that is not open; thickness 0 today; knows its doors. |
| Puerta | `Plans.Door : Passage` | Two jamb locations on a wall: a point, a width (1.4) and the plan's height. |
| Frontera abierta | `Plans.OpenBoundary : Passage` | The whole shared edge is free. |
| Marca (hecho) | `Plans.Mark : Location` | Where a body touched something uncharted. A fact. |
| Obstáculo (hipótesis) | `Plans.Obstacle` | Marks joined by closeness: a point, a line, a polygon. A hypothesis, refined by each bump. |
| Ruta / trayectoria | `Routes.Trajectory` | Ordered legs; `Leg` names what the journal writes (`kitchen/north@4,9.5`). |
| Planificador | `Routes.RoutePlanner` | Dijkstra over doors, openings and detours; `EdgeCost` generic, `DistanceCost` today. |
| Maniobra de evasión | `Routes.Maneuver : Trajectory`, `Routes.EvasionStrategy` | `BackOff`, `StepAside(Side)`; the host's runtime probe still owns execution. |
| Planos con nombre | `Plans.FloorPlans` | Hardcoded catalog (`arena`, `cross-corridors`, `ring-corridor`) rendered as the `map_v1` release text: the journal stays the truth. |

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

- `sim/` — the world: Gazebo Fortress in kiosk mode (noVNC :6080, rosbridge ws :9090).
  Reality is GENERATED from `sim/world/plan.json` at image build (walls, doors, solid
  blocks, bodies with contact sensors, obstacles the golems' map does not know).
- `GolemDomain/` — the pure domain, one assembly, namespaces `GolemHost.Domain`
  (`Golem`, the aggregate the DSL instantiates), `.Geometry`, `.Robots`, `.Plans`,
  `.Routes` (see glossary). The engine binds classes by SIMPLE name: every class
  name in the assembly must be unique. Tests see internals (`InternalsVisibleTo`).
- `GolemAPI/` — the generic golem program (ASP.NET controllers). One image, N
  golems via environment: `GOLEM` (identity, names the journal), `BODY` (the model
  it drives), `HOME_AT` (its mark), `TELL_ROUTES`/`TELL_DONE_TO` (speech).
- The journal (`./journal/<golem>/`, FileSystem backend) is the only truth: pose and
  contacts are ephemeral telemetry, transitions are journaled — entrusting (MoveTo/
  Cover/Follow), the road (Route, again after bumps), progress (Cross/Reach: the last
  Reach completes), touches (Bump: the fact, told; HearBump: a peer's bump; Mark/LearnMark: the map
  of marks — a touch nobody else reported), the ending (Fail/Abandon) —
  every write goes through one serial Dispatch, tells are reaction-only. The golem's map (release `map_v1`: Chart/DoorTo/OpenTo) is its
  knowledge; what the simulator reports (a collision with a crate) is reality. The
  language was fixed on 7-sep-2026 (PLAN, *El lenguaje del golem*): do not add verbs
  on the fly — propose them there first. Reads (queries) may grow as the labs need
  them; writes (journaled verbs) only through the PLAN.

## Habits

- Never commit on your own; leave the working tree and offer the commit command.
- Deploy = `docker compose up -d --build`; verify with the panels (:8081 blue,
  :8082 red, :8083 green — the one on dead reckoning) and the kiosk before claiming
  anything works.
- Journals are disposable in this spike, but always say when a change makes the
  existing ones incompatible (renamed verbs, renamed upgrades).
