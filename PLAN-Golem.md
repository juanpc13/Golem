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

Regla de oro: al journal van transiciones de tarea (asignada, completada, fallida, ruta alterna elegida), no el stream de sensores. Ya vimos reventar una rehidratación por journalear telemetría.

## Decisión a discutir: el puente .NET ↔ ROS 2

1. **rosbridge_suite** — todo ROS expuesto como WebSocket + JSON en :9090. Cero ROS en Windows, cero DDS entre contenedores, encaja con `Ncubo.Puppeteer.Adapters.Sockets`. *(Propuesta)*
2. **ros2_dotnet / rclcs** — cliente nativo .NET. Sin capa intermedia, pero inmaduro y requiere compilar contra ROS; doloroso en Windows.

## Diseño acordado

Un mundo compartido, N tortugas (vía `spawn`), un golem por tortuga. En el navegador una sola ventana kiosko donde se ve a todos actuar. La propiedad de los datos vive en el actor, no en la ventana: mundo compartido no compromete journals separados — y da fallos/estorbos naturales para F3.

## Infraestructura (docker compose, nada instalado local) — ✅ hecha

- **`sim`**: imagen propia `golem-sim:kiosco` (base `tiryoh/ros2-desktop-vnc:humble` + rosbridge + openbox). Arranca solo: turtlesim en modo kiosko visible en noVNC (:6080), rosbridge en :9090. Todo DDS vive dentro del contenedor. Gotcha documentado en el Dockerfile: el entrypoint de la base regenera `xstartup` en cada boot, por eso se parchea el entrypoint en build en vez de copiar el xstartup.
- **`cerebro`**: host Puppeteer (.NET 9) con volumen para journals. En desarrollo puede correr fuera del compose (depurar con VS); el puente es el mismo websocket.

## Fases

- **F0** ✅ — compose arriba, turtlesim visible en el navegador (kiosko + panel `/control.html`).
- **F1** ✅ — membrana validada: rosbridge por websocket JSON (advertise/publish/subscribe).
- **F2** ✅ — `cerebro/` (GolemHost, .NET 9): PerformanceV2 + journal FileSystem en `./journal`, misiones journaleadas, controlador ir-a-(x,y). Verificado local (Windows) y en contenedor; el journal escrito en Windows rehidrata en el Ubuntu del contenedor sin cambios.
- **F3** 🔶 — **segunda tortuga ✅**: `red-golem` gobierna `turtle2` desde la misma imagen (solo cambia el environment). El golem se asegura su propio cuerpo (`/spawn` idempotente por rechazo) y firma el mundo con su pluma (`PEN_RGB`); journal propio en `journal/red/`, panel en :8082. **Tell entre golems ✅ (sobre HTTP, estilo bingo)**: al completar una misión, el golem con `TELL_DONE_TO=<peer>` le cuenta el punto visitado y el peer va a visitarlo. La cadena canónica FINAL (cadena correlacionada, como `AddItem($orderId)`→`Checkout($orderId)` de la guía): el `Assign` journalea el HANDLE de la misión (`g.Assign(@id, @x, @y)`, id acuñado por el handler serial vía `g.NextHandle()`), el `Complete` journalea solo el hecho (`g.Complete(@id)`), y la Reaction es `.Seek("Ordered").One().OnMatch("[_:Golem].Assign($missionId, $x, $y)")` → `.ThenSeek("Done").One().OnMatch("[_:Golem].Complete($missionId)")` → `.Causation.Continue("tell PointVisited with @x, @y to blue once 'visited-' + @missionId;")` — el cuerpo es SOLO el tell ("reaction > tell"), el punto viaja como capturas del Assign correlacionado por `$missionId`. Las misiones que llegan por tell usan el overload `Assign(x, y)` (toma el siguiente handle) y por eso NO se re-cuentan (sin ping-pong). Gotchas del motor: (1) un verbo que una Reaction observa DEBE devolver un valor (con `void Complete` el resolutor fallaba "Unknown method Complete"); (2) una @captura solo puede ir DIRECTA en el `with` — anidada en una llamada pierde el binding en write-time → `.Causation.Continue("tell PointVisited with @x, @y to <peer> once 'visited-' + @missionId")` → el peer hace uptake declarativo `ListenAs(golem).Told("PointVisited").With<double>×2.Command("g.Assign(@x, @y);")` — un perform journaleado por tell.

**Transporte**: `HttpBroker : IMessageBroker` propio (espejo de `PhoneToPhone` del bingo en VeladaApp): tabla de rutas topic→peer por DNS de contenedores (`TELL_ROUTES`), POST al endpoint `/tell` del peer (TellController), y `BrokerTellTransport`/`ListenAs` encima sin cambios — cambiar el medio jamás toca la capa tell. OJO: los acks viajan en `<topic>.acks` y necesitan ruta de VUELTA al que habló. Dev-grade: retry corto sin outbox durable (PhoneToPhone sí lo tiene); producción = broker real o crecer el outbox. Gotcha: `Reactions.Execute()` con `.Cue()` retiene el hilo llamador (push loop) — correrlo en background.

**Controllers ✅**: el actor se maneja por endpoints ASP.NET (estilo LottoAPI): `GolemController` (`/` panel, `POST /assign`, `GET /state`, `POST /query`, `POST /reset`, `GET /events` SSE) y `TellController` (`POST /tell`, el lado receptor del wire). Pendiente de F3: ruta alterna ante fallo.

**Panel = cola del journal ✅**: el feed ya no depende solo de nuestros call sites — `GolemPerformance.WatchJournal` expone el hook `StageHook.OnRecordWritten` (alcanzable porque `hook` es `protected`), y `JournalPeek` decodifica el frame binario (mirror debug-grade del BinaryEventCodec: type byte, entryId, actionId, payload UTF-8, expose, CRC32) para que las escrituras del motor (define/script, action con args, tell, ack, uptake) salgan como filas legibles: el define muestra el template tipado, la action muestra `template ← args`. Los performs propios reclaman su rango completo (define + action) para no duplicar; las escrituras del motor esperan 600 ms de gracia y se descargan ordenadas por entryId. Límite: solo frames sin compresión/cifrado. **Regla de parámetros (V2)**: en el cuerpo del comando los parámetros se referencian SIEMPRE como `@x`, `@id` (`g.Assign(@x, @y);`) — con nombres desnudos (`g.Assign(x, y)`) el comando no densifica en define + action y queda como script nuevo cada vez. El carril Journal — live ahora lo alimenta SOLO el journal (define/action/script tal como se escribieron); nuestros textos van al carril runtime.
- **F4** ✅ (adelantada) — `docker kill` a media misión 2: rehidrató en entry 8, despertó con 3 pendientes y retomó la misión desde donde la tortuga quedó. El journal es el cerebro.
- **Panel de depuración** ✅ — el brain expone :8081: encargar misiones (clic en el minimapa / Assign / patrol) y feed SSE en vivo del journal — cada commit con su entry id, distinguiendo lo journaleado (sólido) de lo runtime que nunca toca el journal (punteado). Emitido por el único escritor en el momento del commit.

## Patrones del training-lab aplicados (guías de repos/Skills)

- **Upgrade/Hidratación** (estilo LottoPerformance, versión moderna): `GolemPerformance : PerformanceV2` con `OnHydrated()` que performa la cadena de upgrades (`upgrade('init') { g = Golem(); };`) en cada arranque — los ya aplicados se saltan en silencio, los nuevos corren y journalean. El propio framework lo documenta: `OnFirstHydration` es el hook legacy (LottoAPI); código nuevo = `OnHydrated` + sentencias `upgrade`. Para evolucionar al golem se agregan `Upgrade_From_X_To_Y()` — nunca se edita un upgrade ya publicado (la firma del cuerpo se valida).
- **Consume & Dispatch** (guía `puppeteer-consume-and-dispatch`): toda escritura fluye por UN Dispatch serial (`CreateDispatch(MaxParallelism=1)` + `On<MissionOrdered/Succeeded/Failed>` + `ConsumeFrom(BrokerInputSource(InProcessBroker, "<golem>-ops"))`). El panel y el mission loop son PRODUCTORES al topic; los handlers tipados son el único lugar donde se performa. Encolado ≠ commiteado: el journal sigue siendo la única verdad. Cuando llegue el tell entre golems, el mismo Dispatch consume del broker compartido — solo se agrega otro ConsumeFrom (el merge).
- **Namespace Choreography** (`GolemHost.Choreography`): la orquestación salió de Program (que quedó en puro bootstrap) a `GolemChoreography` — upgrade de nacimiento, wiring del dispatch, mission loop, queries del panel.

## Bootstrap mínimo que quedó probado

```csharp
var perf = new PerformanceV2("turtle1", typeof(Golem).Assembly);
perf.ConfigureStorage(DatabaseType.FileSystem, $"path={journalPath}");
perf.Start();                       // rehidratación
perf.PerformCmd("g = Golem();");    // comandos journaleados
perf.PerformQry("{ print g.SiguienteId() 'valor'; }"); // queries, no journalean
```
Solo el paquete `Ncubo.Puppeteer` (2.0.1-beta.10017-portable.2, trae Choreography adentro). Sin SQL, sin transporte, sin Adapters.Sockets — esos entran en F3 para el tell entre golems.

## Riesgos conocidos

- Turtlesim no falla solo: el fallo hay que definirlo (timeout, bordes 0–11.08).
- Bug de params rehidratados post-restart → `AlwaysInterpreted` desde el día uno.
- Sin registro nombre→instancia: disciplina de un actor por tortuga, un escritor por journal.
- Websocket sirve para tareas, no para control fino en tiempo real (no aplica al spike).
