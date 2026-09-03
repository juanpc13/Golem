# Golem — Puppeteer 2 sobre turtlesim (ROS 2)

*Golem: el autómata que cobra vida por instrucciones escritas — aquí, las escritas en su journal.*

**Objetivo:** validar que el patrón actor + journal + membrana viaja a otro dominio: autómatas simulados que ejecutan tareas, journalean desenlaces y responden PerformQuery.

## La idea en una línea

Un golem por tortuga: actor .NET (estilo teller: dueño de su journal, un solo escritor), con membrana hacia ROS 2. El loop: percibir → decidir → journalear → actuar.

## Mapeo

| ROS 2 | Puppeteer |
|---|---|
| Acción (`rotate_absolute`: feedback + resultado) | Tarea journaleada con estado completado/fallido |
| Topic `pose` (60 Hz) | Telemetría efímera en memoria — **nunca al journal** |
| Servicios (`spawn`, `teleport`, ...) | Comandos puntuales |

Regla de oro: al journal van transiciones de tarea (asignada, tomada, completada, fallida, soltada), no el stream de sensores. Ya vimos reventar una rehidratación por journalear telemetría.

## Decisión a discutir: el puente .NET ↔ ROS 2

1. **rosbridge_suite** — todo ROS expuesto como WebSocket + JSON en :9090. Cero ROS en Windows, cero DDS entre contenedores. *(Es lo que usamos)*
2. **ros2_dotnet / rclcs** — cliente nativo .NET. Sin capa intermedia, pero inmaduro y requiere compilar contra ROS; doloroso en Windows.

## Diseño acordado

Un mundo compartido, N tortugas (vía `spawn`), un golem por tortuga. En el navegador una sola ventana kiosko donde se ve a todos actuar. La propiedad de los datos vive en el actor, no en la ventana: mundo compartido no compromete journals separados.

## Infraestructura (docker compose, nada instalado local) — ✅ hecha

- **`sim/`**: imagen propia `golem-sim:kiosk` (base `tiryoh/ros2-desktop-vnc:humble` + rosbridge + openbox). Arranca solo: turtlesim en modo kiosko visible en noVNC (:6080), rosbridge en :9090. Todo DDS vive dentro del contenedor. Gotcha documentado en el Dockerfile: el entrypoint de la base regenera `xstartup` en cada boot, por eso se parchea el entrypoint en build en vez de copiar el xstartup.
- **`golemdomain/`**: el dominio puro (`Golem`, `Mission`, `MissionStatus`, `Waypoint`, `DomainException`) — assembly propio sin referencia a Puppeteer ni a ASP.NET (regla E38 de la guía de dominio: los hosts lo ven, él no ve a nadie). Clases `internal sealed`; el DSL las invoca igual. Único público: `GolemDomain.Assembly`.
- **`golemdomain.tests/`**: tests MSTest de aceptación que entran por el perform (actor real, storage IN_MEMORY, misma cadena de releases que el host, aserciones tipadas vía `Parameter.Out`). `dotnet test golemdomain.tests`.
- **`golemhost/`**: el programa genérico del golem (ASP.NET, imagen `golemhost`). Una imagen, N golems por environment: `GOLEM` (identidad, nombra el journal), `TURTLE` (cuerpo), `TELL_ROUTES`/`TELL_DONE_TO`/`TELL_RETRY_SECONDS` (habla). El build context es la raíz del repo (el host referencia al dominio). Volumen `./journal` para los journals.

## Fases

- **F0** ✅ — compose arriba, turtlesim visible en el navegador (kiosko).
- **F1** ✅ — membrana validada: rosbridge por websocket JSON (advertise/publish/subscribe).
- **F2** ✅ — GolemHost (.NET 9): PerformanceV2 + journal FileSystem en `./journal/<golem>/`, misiones journaleadas, controlador ir-a-(x,y). El journal escrito en Windows rehidrata en el Ubuntu del contenedor sin cambios.
- **F3** ✅ — **segunda tortuga**: `red-golem` gobierna `turtle2` desde la misma imagen. El golem se asegura su propio cuerpo (`/spawn` idempotente por rechazo) y firma el mundo con su pluma (`PEN_RGB`). **Tell entre golems (sobre HTTP)**: al completar una misión, el golem con `TELL_DONE_TO=<peer>` le cuenta el punto visitado y el peer lo toma como misión propia. **Ruta alterna, roca y límites del mundo**: se implementaron y verificaron (commit `1f7a10e`) y se RETIRARON en el commit siguiente para pensarlos mejor (ver *Pendientes de diseño*).
- **F4** ✅ — `docker kill` a media misión: rehidrata, despierta con sus pendientes y retoma desde donde la tortuga quedó. El journal es el cerebro.
- **Panel de depuración** ✅ — :8081 (blue) / :8082 (red): misiones por clic en el minimapa, tablero, consola PerformQuery, carril *Journal — live* (proyecciones de las view reactions) y carril *Runtime* (efímero), botón *Let go of everything*.

## Cómo quedó el golem (3-sep-2026, tras la auditoría contra las guías)

**Verbos del dominio** (todos devuelven un escalar; un verbo observado por una Reaction DEBE devolver valor): `Assign(id, x, y)` (encargo con handle), `Take(x, y)` (toma un punto contado por un peer; acuña su handle adentro), `Complete(id)`, `Fail(id, reason)`, `Retire(reason)` (soltar todas las misiones — el olvido queda escrito, nada se borra). Lecturas totales: `HasPendingMission()`, `NextId/NextX/NextY` (consultar `HasPendingMission()` antes), `Knows(id)`, `IsPending(id)`, `StatusOf(id)`, `Pending()`, `Total()`, `NextHandle()`. **Los handles nunca se reutilizan** (ni después de `Retire`): las claves de idempotencia del Saga cuelgan de ellos — lo aprendimos cuando un `mission 1` post-Retire fue descartado en silencio por repetir la clave.

**Navigator (la costura con el cuerpo)**: el loop ya no maneja la tortuga. Le pide a `INavigator.GoToAsync(x, y)` que la lleve y recibe un `Outcome` (`Reached` + `Reason`); si llegó produce `MissionSucceeded`, si no `MissionFailed` con la razón que dio el navigator. `TurtlesimNavigator` (`golemhost/Navigation/`) es el controlador proporcional sobre rosbridge y quien DEFINE el fallo en un mundo que no falla solo: atasco (la distancia deja de mejorar 3 s mientras empuja → "stuck against a wall") o timeout (90 s). Un `Nav2Navigator` futuro traduciría el resultado de `NavigateToPose` al mismo `Outcome` sin tocar golem, journal, reactions ni tells. La evasión de colisiones entre cuerpos vivirá ahí adentro. El golem NO conoce su mundo todavía (ver *Pendientes de diseño*).

**Endpoints** (`GolemController`, el actor mismo): `POST /assign?x&y` performa directo — el handle se acuña en el actor con `p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()"` y queda congelado en los argumentos journaleados, por eso la Reaction que cuenta el punto puede correlacionar por él; `GET /state` = UNA PerformQuery con varios `print` (el tablero), su salida ES la respuesta. `OperatorController` (la cara del operador): `/` panel, `GET /body` (telemetría del host: golem, cuerpo, entry, pose), `POST /query` (consola ad-hoc del laboratorio; 400 si el script no compila), `POST /reset` (→ `Retire`), `GET /events` (SSE). `TellController.HearFromPeer` (`POST /tell`): 200 si un consumidor local tomó el record, 503 si nadie lo escucha aún.

**Ops = Saga por misión**: el loop produce `MissionSucceeded/Failed` al topic `<golem>-ops` (InProcessBroker) con clave de idempotencia derivada del dominio (`red:mission:7:succeeded`); `DefineSaga("Mission").On<T>(m => m.Id).Task(step, handler)` serializa los pasos por misión y cada paso performa con `Using(check, comando).PerformCheckThenCommand()` — `Check(g.Knows(@id) && g.IsPending(@id)) Error '...'` es la guardia real contra desenlaces repetidos (un rechazo no deja entrada). `GolemRetired` es un comando independiente (`On<T>`). El `InputRouting` en la frontera convierte el header `kind` en el tag del mensaje — el layout de wire se decide en un solo lugar.

**Habla = Reactions** (definidas ANTES de `perf.Start()`, que es quien las arma): `echo-visited-point` = `.Seek("Ordered").One().OnMatch("[_:Golem].Assign($missionId, $x, $y)")` → `.ThenSeek("Done").One().OnMatch("[_:Golem].Complete($missionId)")` → `.Causation.Continue("tell PointVisited with @x, @y to <peer> once 'visited-' + @missionId;")` — el cuerpo es SOLO el tell. El peer hace uptake declarativo `ListenAs(golem, bindings, wire).Told("PointVisited").With<double>("x").With<double>("y").Command("g.Take(@x, @y);")`.

**Panel = proyecciones, no lectura del journal**: view reactions `.Cue()` por hecho (`Assigned`, `Taken`, `Completed`, `Failed`, `Retired`, `Acked`) con `.Program.Emit("print @mission 'mission', @x 'x', …")`, empujadas a `PanelSink : IOutputSink` (registrado con `perf.OutputTarget`) → SSE. Cada fila trae el entry que la disparó, el nombre del hecho y el documento impreso. Se fue el decodificador binario del journal (`JournalPeek`/`StageHook.OnRecordWritten`).

**Transporte**: `HttpBroker : IMessageBroker` (tabla `TELL_ROUTES` topic→peer, POST `/tell`). Contrato honrado: una entrega imposible (sin ruta, o `TELL_RETRY_SECONDS` agotados) hace FALLAR la tarea — entonces el transporte journalea el veredicto (`tell 'visited-3' unacknowledged by blue;`, verificado con blue apagado) en vez de fingir un envío, y al volver el receptor el tell NO resucita; los records de una misma clave guardan orden (una compuerta por clave); log retenido por topic y replay al suscriptor tardío; `CanRoute` valida en el boot que `tell-<peer>` tenga ruta. Los acks viajan en `<topic>.acks` y necesitan ruta de VUELTA. Dev-grade: sin outbox durable — producción = broker real.

**Cultura**: el DSL renderiza números al journal con la cultura del hilo. En una máquina en español `11.08` se densifica `11,08` y la rehidratación revienta ("wrong number of arguments"). El host y los tests fijan `CultureInfo.InvariantCulture`.

## Patrones del training-lab aplicados (guías de `repos/Skills/puppeteer/training-lab`)

- **Hidratación versionada** (`puppeteer-hosting-environments`): `OnHydrated()` + `upgrade('x') { … }` encadenados; los aplicados se saltan, un no-op no journalea; `OnFirstHydration` solo como bandera de nacimiento.
- **Parámetros V2** (`puppeteer-actor-basics`): siempre `@x` en el cuerpo, indexador `p["x", typeof(double)] = x;` alineado en columnas; `Parameter.Eval` para el handle; `Parameter.Out` con `RentedParameters()` para leer valores tipados (nunca parsear el `print`).
- **Check antes de comando** (`puppeteer-control-flow`): `Using(check, cmd).PerformCheckThenCommand()`; vacío = aceptado; guardias en el DSL, no `throw` ni `if` en C# sobre estado del actor.
- **Consume & Dispatch + Saga** (`puppeteer-consume-and-dispatch`): `BrokerInputSource` + `InputRouting` en la frontera; Saga por clave de misión.
- **Reactions** (`puppeteer-reactions`): cadena correlacionada para el tell; views `.Cue()` + `Emit` para las proyecciones; definidas antes de `Start()`.
- **Told uptake y transporte** (`puppeteer-told-uptake`, `puppeteer-transport`): `ListenAs…Told…Command`; `BrokerTellTransport` sobre un `IMessageBroker` que honra el contrato (fallo = veredicto).
- **Output y controllers** (`puppeteer-output-and-controllers`): el `print` es la respuesta; C# solo valida entradas; operador y actor en controllers distintos.
- **Dominio** (`puppeteer-domain-modeling`): assembly propio, `internal sealed`, tipo cerrado `MissionStatus`, `DomainException`, sin centinelas (-1/null) — `HasPendingMission()` antes de `NextId()`.

**GAPs declarados (no inventamos idioma)**: observabilidad (guía en borrador; hoy la telemetría del host va por consola y el carril runtime), followers/handover (no aplica al spike), PlainText vs FileSystem para lectura (seguimos en FileSystem; las proyecciones reemplazan la lectura del journal).

## Reglas del motor que aprendimos a golpes

1. **Las Reactions observan solo Actions V2** (define + invocación). Un comando sin `@params` (`g.Retire();`, un upgrade con literales) se journalea como script literal y NINGUNA reaction lo ve — por eso `Retire` lleva razón.
2. **Solo `@params` planos en un comando observado**: `g.Assign(g.NextHandle(), @x, @y)` hace fallar el matcher ("push event faulted during matching") — de ahí `Take(x, y)`.
3. Un verbo observado por una Reaction debe **devolver valor**.
4. Una @captura anidada dentro de una llamada en el `with` del tell pierde el binding — solo capturas directas.
5. El store IN_MEMORY se comparte por nombre: un actor y un nombre de storage por test.
6. Suscribirse a `/turtleX/pose` antes del spawn deja la pose en null; el pen azul puro es invisible sobre el lienzo azul.

## Pendientes de diseño (hay que pensarlo mejor antes de codificar)

- **Colisiones (siguiente paso acordado)**: hitbox del cuerpo (radio ≈ 0.5: el sprite mide ~1 unidad) como conocimiento del cuerpo; la pose de los otros cuerpos como percepción (suscribir sus `/pose`); evasión en runtime dentro de `TurtlesimNavigator` con regla de prioridad determinista; al journal solo desenlaces (`Fail` "blocked by red", o un hecho de choque). Decisiones de Juan pendientes: hitbox como release vs env; prioridad por identidad vs distancia; choque como `Fail` vs hecho aparte.
- **Colisiones, límites del mundo, roca y ruta alterna.** La versión retirada (commit `1f7a10e`) hacía al golem dueño del mundo: `World`/`Rock` como releases (`upgrade('world_v1') { g.Inhabit(11.08, 0.6); }`), `Reroute(id, x, y, reason)` journaleado, desvío táctico alrededor de la roca, y el panel pintando zona de fallo, margen y roca. Preguntas abiertas: ¿quién define el fallo — el golem, o ROS 2/Nav2 cuando haya robot real? ¿El mundo es conocimiento del golem (release) o del entorno (percepción)? ¿Colisiones entre tortugas (hoy se atraviesan: turtlesim no tiene física)?
- Sin registro nombre→instancia: disciplina de un actor por tortuga, un escritor por journal.
- Websocket sirve para tareas, no para control fino en tiempo real (no aplica al spike).
- Los journals viejos (`journal-legacy-*`) son incompatibles con las firmas actuales; son disposables en el spike.
