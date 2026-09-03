# Golem — working rules for Claude

Golem is a spike: Puppeteer 2 actors ("golems") driving ROS 2 turtlesim turtles.
One golem per turtle, one journal per golem, a shared world.

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

- `sim/` — the world: turtlesim + rosbridge in kiosk mode (noVNC :6080, ws :9090).
- `golemhost/` — the generic golem program (ASP.NET controllers). One image, N
  golems via environment: `GOLEM` (identity, names the journal), `TURTLE` (body),
  `TELL_ROUTES`/`TELL_DONE_TO` (speech), `ROCK` (shared world knowledge).
- The journal (`./journal/<golem>/`, FileSystem backend) is the only truth: pose
  is ephemeral telemetry, transitions (Assign/Complete/Fail/Reroute) are journaled,
  every write goes through one serial Dispatch, tells are reaction-only.

## Habits

- Never commit on your own; leave the working tree and offer the commit command.
- Deploy = `docker compose up -d --build`; verify with the panels (:8081 blue,
  :8082 red) and the kiosk before claiming anything works.
- Journals are disposable in this spike, but always say when a change makes the
  existing ones incompatible (renamed verbs, renamed upgrades).
