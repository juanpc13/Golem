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
- **F3** — fallo definido (timeout ya journalea `Fallar`) + ruta alterna + segunda tortuga con su golem y journal propios + tell entre golems (LoopbackBroker → transporte real).
- **F4** ✅ (adelantada) — `docker kill` a media misión 2: rehidrató en entry 8, despertó con 3 pendientes y retomó la misión desde donde la tortuga quedó. El journal es el cerebro.
- **Panel de depuración** ✅ — el brain expone :8081: encargar misiones (clic en el minimapa / Assign / patrol) y feed SSE en vivo del journal — cada commit con su entry id, distinguiendo lo journaleado (sólido) de lo runtime que nunca toca el journal (punteado). Emitido por el único escritor en el momento del commit.

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
