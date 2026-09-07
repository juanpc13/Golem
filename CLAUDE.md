# Golem — working rules for Claude

Golem is a spike: Puppeteer 2 actors ("golems") driving robot bodies in a ROS 2 world
simulated by Gazebo Fortress (turtlesim until 4-sep-2026). One golem per body, one
journal per golem, a shared world with real physics and real collisions.

## Scope

- Work ONLY inside this repo (`C:\Users\Juan\source\repos\Golem`). Do not read,
  cite, or imitate code from ExchangeEngine, LottoAPI, VeladaApp or any other
  sibling repo unless Juan explicitly asks for a specific file. Those are other
  eras and other conventions; they confuse the design here.
- Plan for the team: `PLAN-Golem.md` (Spanish, discussion doc). Code, comments,
  logs and UI are in English.

## Doctrine (in this order of authority)

1. Juan's words in the conversation.
2. The Puppeteer training-lab guides:
   `C:\Users\Juan\source\repos\Skills\puppeteer\training-lab\guides\puppeteer-*\SKILL.md`
   (entry point: `/puppeteer-guides`; read `puppeteer-actor-basics` first).
   When a guide covers the case, follow it literally — no V1 surfaces
   (`PerformCmd(string)` on a Performance), no home-made idioms.
3. The Puppeteer source and tests as the ground truth of behavior:
   `C:\Users\Juan\source\repos\puppeteer` (read-only reference, we consume the
   `Ncubo.Puppeteer` nupkg pinned in `golemhost/localfeed`).

## Architecture in one breath

- `sim/` — the world: Gazebo Fortress in kiosk mode (noVNC :6080, rosbridge ws :9090).
  Reality is GENERATED from `sim/world/plan.json` at image build (walls, doors, solid
  blocks, bodies with contact sensors, obstacles the golems' map does not know).
- `golemhost/` — the generic golem program (ASP.NET controllers). One image, N
  golems via environment: `GOLEM` (identity, names the journal), `BODY` (the model
  it drives), `HOME_AT` (its mark), `TELL_ROUTES`/`TELL_DONE_TO` (speech).
- The journal (`./journal/<golem>/`, FileSystem backend) is the only truth: pose and
  contacts are ephemeral telemetry, transitions (Assign/Route/Pass/Complete/Fail/
  Supersede/Retire) are journaled, every write goes through one serial Dispatch,
  tells are reaction-only. The golem's map (release `map_v1`) is its knowledge;
  what the simulator reports (a collision with a crate) is reality.

## Habits

- Never commit on your own; leave the working tree and offer the commit command.
- Deploy = `docker compose up -d --build`; verify with the panels (:8081 blue,
  :8082 red, :8083 green — the one on dead reckoning) and the kiosk before claiming
  anything works.
- Journals are disposable in this spike, but always say when a change makes the
  existing ones incompatible (renamed verbs, renamed upgrades).
