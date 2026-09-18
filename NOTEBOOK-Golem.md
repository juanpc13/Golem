# Cuaderno de investigación — Golem

*Somos científicos: una observación que no queda anotada no ocurrió. Cada laboratorio deja aquí una entrada con **Observación** (lo que hizo el mundo, el simulador o el journal), **Conclusión** (qué significa) y **Ajuste al dominio** (qué cambia en `golemdomain/`, o "nada, y por qué"). El dominio solo crece desde estas entradas; el `PLAN-Golem.md` sigue siendo el documento de diseño y discusión; `CLAUDE.md` guarda las reglas y el glosario.*

Formato de una entrada:

```
## AAAA-MM-DD · título corto
**Contexto**: qué se probó y con qué (compose, golems, plano, journals nuevos o no).
**Observación**: hechos, con números y entradas del journal cuando los haya.
**Conclusión**: qué aprendimos; qué hipótesis se confirma o se cae.
**Ajuste al dominio**: clases tocadas / verbos propuestos al PLAN / nada y por qué.
**Pendiente**: lo que abre.
```

---

## 2026-09-08 · Entrada 0 — La documentación del dominio (Juan)

**Contexto**: Juan entrega la especificación del dominio que este proyecto debe cristalizar: un dominio inicial para desplazar uno o varios robots sobre espacios, donde el robot solo puede moverse y su único sensor es detectar una colisión. La documentación se guarda aquí en sustancia (ordenada, no parafraseada en su intención) y el glosario resumido va en `CLAUDE.md`.

### El robot, sus capacidades y las bibliotecas de dominio

- Un nodo robot en ROS 2. Puede tener más capacidades según sus sensores. El más básico, el de los laboratorios actuales, se desplaza de un sitio a otro usando un sistema de coordenadas.
- A ese sistema de coordenadas el dominio lo llama **POSICIÓN**.
- Intención: un dominio rico para hacer cálculos sobre la posición que finalmente se le alimenten al robot para que se desplace. Además, eventos generados por el robot alimentan al dominio con información valiosa que afecta capacidades, en particular la de desplazamiento.

**El robot**: tiene un nombre; tiene una posición estimada; se desplaza en un mapa.

### El mapa

- Constituye un plano. A veces el plano está acotado por un **borde exterior** conocido.
- En el plano (o dentro del borde exterior) hay **espacios o habitaciones** que no se superponen. Entre el espacio "cuarto" y el espacio "pasillo", adyacentes, lo que se dibuja es una **pared**. Lo que definimos es semejante al plano de distribución de una casa, una bodega, un campo de fútbol. Por ahora un solo plano, sin niveles; luego se agregará el concepto de **niveles** para edificios de varias plantas.
- Entre una habitación y otra hay **puertas**, con dimensión: ancho y alto. A la pared se le define ancho y alto; no se podrá acceder por la pared, solo por la intersección declarada de los dos espacios.
- El plano se considera bidimensional o con una altura **H**; inicialmente todas las puertas tienen altura H.
- Las paredes pueden tener grosor, pero inicialmente se tratan de ancho mínimo, como una línea, en el plano euclídeo / cartesiano XY.

### La ubicación

- Es una posición donde se encuentra una **marca**. El robot está ubicado en una posición; dicha posición es una ubicación.
- Un espacio está definido por un **polígono**, usualmente rectangular. Los límites de un espacio están definidos por al menos cuatro ubicaciones.
- Una puerta está definida por **dos ubicaciones contenidas en una pared**.
- La pared es normalmente una línea recta (o polígono de ancho usualmente 0) que limita con el borde, si existe.

### El desplazamiento del robot

- El desplazamiento básico es de la posición actual a la próxima ubicación.
- Una **ruta o trayectoria** conecta varios **segmentos**. Cada segmento es una línea de la trayectoria.
- Un segmento es el desplazamiento lineal del robot de una posición *i* a una posición *j* (por ejemplo de (2,2) a (3,3)).

### Planificador de rutas

- Para ir de la posición actual a una ubicación objetivo el robot cuenta con uno o varios **mapas**.
- Desplazarse sin rumbo puede retroalimentar los mapas creando paredes o puertas, lo que indirectamente define los límites del plano conocido.
- El robot avanza por el interior de los espacios hasta la ubicación objetivo; a veces el objetivo está en otra habitación y hay que cruzar espacios y puertas para conectar la trayectoria.

### Obstáculos

- Los mapas tienen asociadas colecciones de **obstáculos**: algunos son **hechos** y otros **especulaciones o hipótesis**.
- Cuando el robot colisiona con algo desconocido, se anota la posición de ese obstáculo.
- Al cambiar de ruta puede volver a colisionar con otro obstáculo junto a los anteriores en el mismo espacio, y se genera la hipótesis de que varios en línea forman una pared de ancho/largo desconocido. Un obstáculo puede ser otro robot o mobiliario (polígono, normalmente rectangular) que impide atravesar la trayectoria trazada.

### El robot ante la colisión

- Mientras se desplaza, puede llegar el mensaje de que el robot colisionó: eso debe generar la hipótesis de si se trata de una pared o de un obstáculo. El robot establece una **maniobra de evasión**, que es un tipo especial de trayectoria; al generar más colisiones ayuda a hacer hipótesis sobre la forma de los obstáculos o a determinar que era una pared en posición conocida.
- Una colisión también puede deberse a imprecisión de la posición del robot respecto a una pared, una esquina o dos paredes: la maniobra debe ser distinta y requiere información del espacio (no del plano entero, que puede tener muchos espacios).
- Estrategias de evasión posibles: echar para atrás; retroceder en ángulo; girar y tomar a la derecha; buscar una ruta en forma *greedy* o aleatoria.

### Generalidades

- Cada entidad anterior debe ser una **clase** del dominio, con **herencia** donde hay verdad de dominio: el segmento se relaciona con la estrategia de evasión; el desplazamiento del punto actual al objetivo también es un segmento.
- Trayectoria por **Dijkstra / ruta más corta**; el costo de la arista es genérico pero en los primeros diseños es la distancia.
- A futuro, varios mapas para un mismo espacio: **capas**; algunas con más información según la capacidad del modelo.
- El dominio es un **activo general** que se irá enriqueciendo con más robots, más características de espacios y más problemas de desplazamiento.
- Clases en **inglés**, separadas por **namespaces**: robots, planos, rutas.
- Planos **hardcoded con nombre**, con el mismo código: (a) cuatro habitaciones separadas por dos pasadizos en cruz; (b) cuatro habitaciones juntas en el centro rodeadas por un pasadizo exterior.

### Conclusión: cómo cae esto sobre lo que ya había

Lo que veníamos manejando (PLAN, *El lenguaje del golem*, *El mapa*, *El mapa de choques*) ya contiene casi todos los conceptos, pero con nombres de la conversación y una sola clase gorda (`Atlas`) haciendo de plano, catálogo de marcas y planificador. Decisiones tomadas para el refactor (ver la entrada siguiente):

1. **El robot = `Golem` + `Body`.** El golem es la mente (lo journaleado: encargos, camino, toques); el cuerpo (`Robots.Body`) son sus propiedades físicas (radio, velocidad, linger). El **nombre** del robot es la identidad del journal (env `GOLEM`); no se repite como estado del dominio. La **posición estimada** es telemetría y **no se journalea** (regla de oro del PLAN): entra a las consultas como parámetros (`Plan(id, x, y)`, `DistanceLeft(x, y)`). Tensión registrada: la especificación dice "el robot tiene una posición estimada"; el dominio la recibe, no la guarda.
2. **POSICIÓN = `Position`** (era `Waypoint`). **Ubicación = `Location : Position`**: una posición con significado en el mapa (esquina de un espacio, jamba de una puerta, marca). **Marca = `Mark : Location`** (un hecho).
3. **Plano = `FloorPlan`** (era `Atlas`); el nombre *Atlas* queda reservado para "los mapas del robot" (varios planos, capas), que aún no existe.
4. **Espacio = `Place`** (se conserva: es la palabra del journal, `Chart`/`Places()`); hoy rectángulo, con `Corners()` (cuatro ubicaciones) y `Walls()`.
5. **Pared = `Wall : Segment`**: un borde del lugar que no es frontera abierta, grosor 0, conoce sus puertas. **Puerta = `Door : Passage`** con punto, ancho (1.4, el hueco físico del mundo) y las dos **jambas** (`Jambs()`, dos ubicaciones sobre la pared); altura = H del plano (0.5, la de las paredes de la arena). **Frontera abierta = `OpenBoundary : Passage`**. La herencia lleva verdad de dominio: una puerta se cruza de frente (aproximación y salida), una frontera abierta en cualquier punto lejos de las esquinas.
6. **Obstáculo = `Obstacle`**, la **hipótesis** que agrupa marcas cercanas (punto, línea, polígono). Las marcas son los hechos.
7. **Trayectoria = `Trajectory`** (lista ordenada de `Leg`); **planificador = `RoutePlanner`** (Dijkstra) con costo `EdgeCost` genérico y `DistanceCost` como única implementación por ahora.
8. **Maniobra de evasión = `Maneuver : Trajectory`** producida por una `EvasionStrategy` (`BackOff`, `StepAside(Side)`). El host sigue ejecutando su tanteo en runtime (verificado en vivo el 8-sep, no se toca hoy); el dominio expone la lectura `g.Evasion(x, y, heading, side)` como costura para migrarlo.
9. **Planos con nombre = `FloorPlans`** (`arena`, `cross-corridors`, `ring-corridor`) construidos con las mismas clases. Para que **el journal siga siendo la única verdad**, el catálogo no se carga con un verbo (`Chart('arena')` dejaría el mapa en el código, no en el diario): el catálogo **renderiza el texto del release** (`AsRelease('map_v1')`) y ese texto es lo que se journalea, literal, como hoy.
10. **Ancho/alto de pared, borde exterior, polígonos no rectangulares, niveles y capas**: quedan anotados como pendientes, no se modelan a medias.

**Pendiente**: borde exterior del plano; espacios poligonales; grosor real de pared (hoy lo absorbe `WallTolerance` 0.3); niveles; capas; costos distintos de la distancia; estrategias de evasión *greedy*/aleatoria; que el tanteo del host use `Maneuver`.

---

## 2026-09-08 · Refactor del dominio a partir de la entrada 0

**Contexto**: `golemdomain/` reorganizado en namespaces (`GolemHost.Domain` raíz con `Golem`; `.Geometry`, `.Robots`, `.Plans`, `.Routes`). El motor de Puppeteer registra las clases por **nombre simple** (`DomainLibraries.cs`: `typesByName[type.Name]`), así que los namespaces son libres pero **cada nombre de clase debe ser único en el assembly**. El host solo usa `GolemDomain.Assembly` y texto DSL: no requirió cambios.

**Observación** (lo que cambió en el código; el detalle se lee en los `///` de cada clase):

| Antes | Ahora | Namespace |
|---|---|---|
| `Waypoint` | `Position`; `Location : Position` (nueva); `Segment` (nueva) | `Geometry` |
| campos `radius/speed/linger` en `Golem` | `Body` (`Embody`, `Cruise`, `Linger`) | `Robots` |
| `Mission`, `MissionStatus` | iguales, movidas; `HeardBump` (era una tupla) | `Robots` |
| `Atlas` | `FloorPlan` (plano + marcas + `WithDoorCrossings` + `AsRelease`) | `Plans` |
| `Passage` con `IsDoor` | `Passage` abstracta; `Door` (con `Width`, `Height`, `Jambs()`) y `OpenBoundary` (con `Edge`, `Midpoint`, `CrossingPoint`) | `Plans` |
| — | `Wall : Segment` (bordes no abiertos de un lugar, con sus puertas); `Place.Corners()`, `Place.Walls()` | `Plans` |
| `Mark` (X, Y, Reach) | `Mark : Location` con `Reach` | `Plans` |
| `Obstacle` | igual, más `Shape` (point / line / polygon) | `Plans` |
| — | `FloorPlans` (catálogo: `arena`, `cross-corridors`, `ring-corridor`; `Named(name)`) | `Plans` |
| `Leg`, `List<Leg>` | `Leg`, `Trajectory` (`Parse`, `AsPlan`, `Segments(from)`, `Length(from)`) | `Routes` |
| Dijkstra dentro de `Atlas` | `RoutePlanner(plan, radius, cost)`; `EdgeCost` / `DistanceCost` | `Routes` |
| — | `Maneuver : Trajectory`; `EvasionStrategy` → `BackOff`, `StepAside`; `Side` (right / left) | `Routes` |

**Lenguaje del journal**: **sin cambios** en los verbos de escritura (`Embody/Cruise/Linger`, `Chart/DoorTo/OpenTo`, `MoveTo/Cover/Follow`, `Route`, `Cross/Reach`, `Bump/Hear/Mark/Learn`, `Fail/Abandon`, `Announce`); **los journals existentes siguen valiendo**. Lecturas nuevas: `g.Evasion(x, y, heading, side)` (devuelve la maniobra; se recorre con `foreach`), `places.Corners()`, `places.Walls()` (cada pared con `From`, `To`, `Length` y `Doors()`; cada puerta con `At`, `Width`, `Name` y `Jambs()`), `doors.Width` en la vista `Doorway`, `obstacles.Shape`.

**Conclusión**: el dominio dice ahora las palabras de la especificación sin romper el diario. Un cambio de comportamiento deliberado y pequeño: `KnowsWallAt` pregunta pared por pared (una puerta solo abre hueco **en su propia pared**); antes cualquier puerta a ≤ 0.8 borraba también las paredes perpendiculares cercanas (p. ej. la pared exterior a 0.75 de la puerta cocina/oeste). Lo nuevo es lo correcto; vigilar en el próximo laboratorio si cambia la clasificación de algún roce.

**Ajuste al dominio**: el de la tabla. Tests: los 33 de aceptación siguen en verde **sin tocar sus scripts DSL**, más 6 nuevos (paredes/esquinas/jambas como objetos, maniobra de evasión, catálogo: arena, cruz, anillo, nombres): **39/39**. El host (`golemhost`) compila sin cambios.

**Observaciones del motor y del planificador durante los tests** (valen como hallazgos):
- El DSL encadena una llamada sobre el resultado de otra dentro de un `foreach` (`foreach (legs in g.Evasion(@x, @y, @heading, 'step-right').Legs())`) y liga propiedades **heredadas** (`corners.X` de `Location : Position`, `walls.Length` de `Wall : Segment`): la herencia del dominio no le cuesta nada al motor (reflexión `GetProperties(NonPublic | Instance)`, que incluye lo heredado).
- En el plano en cruz, del cuarto noroeste al sureste hay dos caminos de igual longitud (norte→cruce→este y oeste→cruce→sur): Dijkstra devuelve uno u otro según el orden de los nodos. **No asumir un empate resuelto**; si algún día importa (p. ej. preferir la derecha), es una regla de desempate a escribir en el dominio, no un accidente.
- En el anillo, del rincón noroeste del pasillo al pasillo este, el planificador **corta por la habitación noreste** (dos puertas) porque es más corto que rodear por el pasillo norte: correcto por definición de "más corto", pero un operador humano quizá no quiera que el robot atraviese habitaciones de paso. Candidato a costo distinto de la distancia (`EdgeCost`: penalizar puertas o habitaciones), que es exactamente para lo que el costo quedó genérico.
- La diagonal cuarto-a-cuarto en la cruz mide ≈ 10.72 (puerta, pasillo, cruce, pasillo, puerta), no los 12–14 que estimé de cabeza: **estimar antes, medir después, y anotar la diferencia**.

**Pendiente**: los del final de la entrada 0; y que el host adopte `FloorPlans.Arena().AsRelease("map_v1")` **solo** cuando los journals renazcan (el motor valida la firma de un release aplicado: el texto literal de `map_v1` en `GolemPerformance.cs` no se toca mientras haya journals vivos).

---

## 2026-09-08 · Marco teórico: los papers de Puppeteer, y el dominio auditado contra ellos

**Contexto**: Juan: "ya debe haber bastante terminología y teoría respecto a estos espacios; yo no quiero hacer las cosas tal cual ellos las hacen, porque ellos no están hablando del puppet". El marco teórico son los diez papers de `github.com/alvaroNCubo/puppeteer-papers`, y lo importante son **los repertorios del dominio**. Se leyeron los diez (8-sep) y aquí queda lo que cada uno le exige a `golemdomain/`, más la auditoría del refactor de hoy.

### Digesto por paper (qué dice → qué nos exige)

| # | Paper | Tesis en una línea | Lo que le exige a nuestro dominio |
|---|---|---|---|
| 01 | Anti-porosity | La *porosidad* es codificar la estructura (el espacio de tipos) en vez de la operación que determinó el caso: columnas NULL por estado, DTOs con campos opcionales, eventos que guardan estado. Anti-porosidad = la operación como primario: el *script* en persistencia, la *variante* (sum type) en el dominio, el *caso de uso* en el endpoint. | Una clase por rol/desenlace, no una clase ancha con `status` y campos que solo algunos estados llenan. Los actores nombran responsabilidades acotadas (un golem por cuerpo ✓), no agregadores sin fin. |
| 02 | Program–value separability | Un script es `F(x1..xn)`: el programa declara sus parámetros y los valores viven fuera. Eso da identidad estable → compilación, caché, journal denso (definición una vez, referencias después). | Comandos siempre con `@params`; literales solo para lo efímero. Verbos ricos (profundidad) mejor que setters. Ya era la regla 1 del PLAN. |
| 03 | Reactions and the partition | El desarrollador parte el trabajo en *ahora* (lo que promete el verbo) y *diferido* (Reactions: patrones sobre la trayectoria del journal). Consecuencia: el dominio **cierra** a herramientas operativas (correo, telemetría, webhooks) y sigue abierto a primitivas conceptuales. | Nada operativo en `golemdomain/` ✓. Los tells van en `Causation.Continue` ✓. `expose` para dar datos a una Reaction sin rehidratar ✓ (así viaja `@me who`). |
| 04 | Cross-actor continuity | La causación entre actores es *programática*, no operativa: el `tell` es una sentencia del journal del emisor que afirma un **hecho vivido, en pasado**, nunca una orden; sin coordinador externo; el ack vuelve al emisor. | Nuestros tells son `PointVisited`, `BumpedAt`, `ObstacleFound` ✓ (afirmaciones). El uptake del receptor es SU decisión (`Follow`, `Hear`, `Learn`) ✓. |
| 05 | Substrate operations | El journal *es* el programa escrito en el tiempo: desplegar, replicar, respaldar y operar sin conexión son cuatro caras del replay. Lo no determinista se resuelve **una vez en el camino de escritura** y se congela en la entrada. | Handles acuñados con `Parameter.Eval` ✓; nada en el dominio lee reloj ni azar. Un release aplicado no se edita ✓. |
| 06 | Infrastructural symptom | Muchas capas (caché, ORM, colas, locks, app servers) compensan defectos del modelo de persistencia y se disuelven cuando el modelo cambia. | Diagnóstico para el host, no para el dominio. Nota: el panel "peek" del journal es un ojo de laboratorio, no una capa. |
| 07 | After the substrate | Con el sustrato, la nube es biblioteca, no hábitat; el autor escribe **el dominio** (clases, verbos, invariantes, determinista) y nada más. | Todo lo externo (ROS, rosbridge, Gazebo) vive en el host; el dominio no sabe que existe un robot físico ✓. |
| 08 | Inference without authority | Tres autoridades: el **dominio** sabe *qué existe*; el **actor** sabe *qué se vuelve observable* (la proyección, el `print`); el **ensamblador** sabe *dónde cae*. Un objeto que se renderiza (`ToString`, JSON) se pasó de autoridad. *Instantánea* vs *narración*: solo la narración conserva la causa; el observador que recibe el journal **conoce por testimonio**, el que recibe instantáneas **infiere** a golpe de diffs. | Fue exactamente la razón de retirar `DescribeMap()` el 8-sep (el dominio armaba JSON) ✓: hoy `/map` es una query que recorre objetos y hace `print`. **Ley que fija para nosotros: los hechos se journalean como testimonio (Bump, Mark, Reach…); las hipótesis se derivan y jamás se journalean (`Obstacle`, la ruta más corta, `KnowsWallAt`).** |
| 09 | Identity precedes staging | Un dominio tiene identidad cuando no declara obligaciones hacia ningún montaje: cero puertos, cero interfaces, cero superficie de reconstitución (el replay reentra por las operaciones). Solo se registran **actos**; un registro incompleto responde plausible pero mal. | `golemdomain/` no referencia nada ni declara interfaces ✓ (`GolemDomain.Assembly` es la única puerta pública). Todo hecho emitido debe derivarse dentro de UN actor ✓ (las marcas ajenas llegan por `Learn`, no por lectura del otro journal). |
| 0A | The assembled verb | Un **repertorio** es el conjunto de operaciones disponibles a un sujeto; **no necesita anticipar los verbos** que luego se ensamblen con ellas. El verbo ensamblado es una capacidad del sujeto, autorada en el plano de ensamblaje (el actor), y su definición **es una entrada del journal** (`define action … as …; end;`). Comando o consulta se atribuye al performar, no es intrínseco a la operación. El ensamblador ve *ancho* (compone repertorios), no *profundidad* (no reescribe la lógica de uno). | **Nuestros repertorios son `Robots`, `Plans`, `Routes`; `Golem` es el sujeto que los ensambla.** No agregar al dominio métodos que solo existen para comodidad de una orquestación: eso es un verbo ensamblado en el actor (p. ej. "soltar todo" = `foreach (id in g.PendingIds()) { g.Abandon(id, @reason); }`, una definición y N ejercicios, no un `AbandonAll`). |

### Auditoría del refactor de hoy contra los papers

**Lo que ya cumple**: dominio puro sin puertos (09), verbos con `@params` (02), tells como hechos vividos en Reactions (03, 04), hechos journaleados e hipótesis derivadas (08: `Mark` es hecho, `Obstacle` es inferencia recomputada), catálogo `FloorPlans` que renderiza el **programa** (el texto del release) y no una proyección: lo que viaja es la sentencia que el journal ejecutará, no un documento para un observador (05, 0A). `Trajectory.AsPlan()` igual: es el argumento de `Route`, programa, no proyección.

**Porosidad que quedó (01) — candidatos para una siguiente pasada, con Juan**:
1. `Mission` es una clase ancha: `MissionStatus` + `reason` (vacía salvo en `failed`/`abandoned`), `Following` y `ChoosesOrder` como banderas. Anti-poroso sería una variante por desenlace/origen: p. ej. `Entrusted`/`Followed` como orígenes y `Completed`/`Failed(reason)`/`Abandoned(reason)` como cierres. Antes de mover nada: medir si el DSL liga bien cuando el tipo en tiempo de ejecución cambia (el panel imprime `StatusOf`, un escalar, así que el riesgo es bajo).
2. `Leg.IsStop` deduce el tipo de tramo **parseando el nombre** (`/`, `~`, `around`): un código de tipo escondido en un string. Variantes `DoorLeg`, `OpeningLeg`, `DetourLeg`, `StopLeg` dirían la verdad de dominio y `Mission.Cross/Reach` dejarían de mirar el texto. El texto del plan en el journal no cambiaría (es el programa; las variantes se reconstruyen al releerlo, como hoy `WithDoorCrossings`).
3. `Passage.IsDoor` ya desapareció en variantes (`Door`/`OpenBoundary`) ✓: ese es el modelo a seguir para 1 y 2.

**Autoridad (08) — vigilar**: `Obstacle.Shape` ("point/line/polygon"), `Side.Name`, `EvasionStrategy.Name` son vocabulario del dominio (qué existe), no proyección: aceptables. La línea roja sigue siendo que ninguna clase del dominio produzca JSON, tablas o texto para un observador.

**Repertorios (0A) — decisión de vocabulario**: de aquí en adelante el cuaderno y el PLAN llaman **repertorio** a cada namespace del dominio y **verbo ensamblado** a lo que el actor compone con ellos (releases, reacciones, el `foreach` de abandono, la query de `/map`). Cuando aparezca una necesidad nueva, la primera pregunta es: ¿le falta una **operación** a un repertorio (entonces sí toca el dominio) o le falta un **verbo** al sujeto (entonces se ensambla en el actor, sin tocar el dominio)?

**Sobre la terminología ajena**: la robótica diría *occupancy grid*, *costmap*, *waypoint*, *pose graph*. No se adoptan: describen estructuras (instantáneas), y el puppet describe operaciones (narración). Cuando haga falta un puente (p. ej. importar un plano estándar), será un **controller** del host que traduzca al lenguaje del golem, como quedó anotado en el PLAN el 7-sep.

**Pendiente**: proponer en el PLAN las variantes de `Mission` y `Leg` (puntos 1 y 2) y decidir con Juan; releer los papers 01 y 0A antes de la próxima ampliación del dominio (niveles, capas, borde exterior), porque son los que deciden si algo es operación de un repertorio o verbo del sujeto.

---

## 2026-09-08 · Estado del arte en robótica vs nuestro repertorio

**Contexto**: Juan pide buscar prácticas y papers de robótica que enriquezcan el dominio, anotar en el código el origen o autor de cada técnica, y respetar el marco (repertorio, papers del puppet) y los escenarios cubiertos: ruta más corta por puertas y fronteras (3–4 sep), cruce de puertas de frente (4 sep), líder/seguidor (3–4 sep), roce con pared conocida (escenario 1, 7 sep), mapa de choques y tanteo (escenario 2, 7–8 sep), protocolo de toques entre cuerpos (8 sep), odometría de ruedas (7 sep). Escenario 3 (exploración) sigue pendiente y Juan lo prefiere con lidar.

**Observación** — qué hace la robótica para cada cosa que ya hacemos, dónde nace, y cómo cae sobre el repertorio (las citas quedaron como notas `///` en las clases indicadas):

| Nuestro repertorio | Técnica / origen (verificado) | Coincide en | Difiere por el puppet | Enriquecimiento posible (propuesta, no hecho) |
|---|---|---|---|---|
| `Routes.RoutePlanner` | Dijkstra (1959). Grafo de visibilidad **VGRAPH**: Lozano-Pérez & Wesley, *CACM* 22(10), 1979. Espacio de configuraciones (cuerpo → punto, mundo crece el radio): Lozano-Pérez, *IEEE Trans. Computers* 1983. | Nodos = puertas, puntos medios de fronteras, rodeos; aristas = quien se ve; radio como `clearance`/inset. | El grafo es **topológico** (lugares y pasajes), no una malla; el camino se journalea como programa (`Route`) y se redecide solo tras un `Bump`. | Costo ≠ distancia (`EdgeCost`): penalizar puertas o habitaciones de paso (hallazgo del anillo: el planificador corta por una habitación). Desempate determinista (hallazgo de la cruz). |
| `Plans.FloorPlan`, `Place`, `Passage` | Mapas **topológicos**: Kuipers & Byun, *Robotics and Autonomous Systems* 8, 1991 (*Spatial Semantic Hierarchy*: lugares distintivos + caminos; capas métrica/topológica). | Lugares nombrados unidos por pasajes; la métrica dentro de cada lugar. | La SSH construye el mapa explorando con sensores; el nuestro es **conocimiento releaseado** en el journal y crece solo por hechos (`Mark/Learn`). | Las **capas** que pide Juan tienen aquí su fundamento: capa topológica (hoy) + capa de marcas (hoy, sin nombre) + capa métrica fina (futuro). |
| `Plans.Mark` | **Occupancy grid**: Moravec & Elfes, ICRA 1985 (celdas con probabilidad de ocupación). | Ambos acumulan evidencia de "algo hay aquí". | Guardamos el **toque** (hecho ubicado con alcance), no una celda de creencia: la rejilla sería una proyección que alguien dibuja desde las marcas (paper 08). | Nada en el dominio. Una vista del panel podría rasterizar marcas si algún día hace falta comparar con ROS. |
| `Plans.Obstacle` | *Collision Hypothesis Sets*: Saund & Berenson, ISER 2018 (creencia de ocupación desde contactos, en manipuladores); continuado en *The Blindfolded Robot: a Bayesian approach to planning with contact feedback*, Saund, Choudhury, Srinivasa & Berenson, ISRR 2019, y *The blindfolded traveler's problem*, IJRR 2023 — **el robot con los ojos vendados que planifica solo con contactos: nuestro caso exacto, a leer completo antes del escenario 3.** Agrupación por cercanía = *single-linkage* (unión-búsqueda). | Un contacto no localiza el objeto: define un conjunto de hipótesis que se estrecha con cada contacto. | La hipótesis **no se journalea**; se recomputa desde las marcas. | Estrechar la hipótesis con la **dirección del golpe** (hoy solo el punto): el navigator ya conoce el rumbo; sería un dato más del `Bump` (verbo al PLAN, no ahora). |
| `Routes.Maneuver`, `EvasionStrategy` | Familia **Bug**: Lumelsky & Stepanov, *Algorithmica* 2, 1987 (Bug1, Bug2: autómata puntual con sensor táctil, ir-al-objetivo + seguir-el-borde, con garantía de terminación); comparación de 11 variantes: Ng & Bräunl, *J. Intell. Robot. Syst.* 50, 2007 (DistBug, TangentBug…). | Exactamente nuestro caso: solo tacto. Nuestro paso lateral + avance es un seguimiento de borde **acotado**. | Cada toque es un hecho contado a la flota; tras un tanteo acotado se **redecide la ruta** sobre el mapa de marcas en vez de seguir el borde indefinidamente. Bug asume punto; nosotros radio (C-space). | La condición de terminación de Bug ("si vuelvo al punto de contacto, el objetivo es inalcanzable") es la que hoy nos falta formalmente: `Fail` cuando ningún camino cabe es el análogo, pero sin la prueba de haber rodeado. Candidata a ley del dominio. |
| Campos de potencial, VFH | Khatib, *IJRR* 5(1), 1986; Borenstein & Koren, *IEEE T-RA* 7(3), 1991. | Evitación reactiva local. | Requieren **rango** (distancias), no tacto: no aplican al sensor que tenemos. Se descartan, anotado para no volver a mirarlos hasta tener lidar. | — |
| Protocolo de toques, quién cede | **RVO/ORCA**: van den Berg, Lin & Manocha, ICRA 2008; van den Berg, Guy, Lin & Manocha, *Springer STAR* 2011 (cada cuerpo asume la mitad de la evitación, sin comunicarse). | Mismo problema: dos cuerpos se encuentran. | Ellos evitan **sensando**; nosotros resolvemos **hablando** (`Bump`/`Hear`, dos tells) y con una regla determinista de prioridad. Sin comunicación no tendríamos nada, porque no vemos al otro. | Reglas de tránsito (cada cuerpo se orilla a un lado fijo) ya están; falta que la prioridad sea una **operación del repertorio** (`Yields(who)` como lectura) y no una regla del host. |
| `Robots.Body`, pose como parámetro | Odometría: Borenstein & Feng, *IEEE T-RA* 12(6), 1996 (**UMBmark**: errores sistemáticos vs no sistemáticos; los sistemáticos se calibran). | El experimento del 7-sep (0.03–0.11 m de deriva conduciendo, 0.48 m en un golpe) es el "no sistemático" de Borenstein. | La pose no es estado del dominio; el dominio no corrige la pose, la recibe. | Un `Bump` contra **pared conocida** es una observación que un robot real usaría para corregir pose (contacto como baliza). Hoy es reintento del tramo. Podría ser una lectura: "dónde debería estar el cuerpo si lo que tocó es esa pared" — inferencia, no verdad. |
| Escenario 3 (pendiente) | **Frontier-based exploration**: Yamauchi, CIRA 1997 (ir a la frontera entre lo conocido y lo desconocido). | La idea de frontera sirve aunque sea con tacto: "qué borde de mi plano no he tocado". | Yamauchi la define sobre una rejilla de evidencia; la nuestra sería sobre lugares/paredes sin marcas ni toques. | Cuando llegue el lidar. Anotado para no inventar otra palabra. |
| `Cover` (orden de paradas) | TSP abierto: fuerza bruta ≤ 7 paradas, vecino más cercano después (heurística clásica). | Igual. | — | 2-opt si alguna vez hay más de 7 paradas en serio. |

**Conclusión**:
1. Casi todo lo que hicimos a ciegas tiene nombre y fecha en robótica, y la fecha es vieja (1979–1997): el problema "robot ciego con tacto" es la familia Bug. Saberlo nos da dos cosas: la **condición de terminación** de Bug como candidata a ley del dominio, y la certeza de que campos de potencial y VFH no aplican sin sensor de rango.
2. La diferencia constante con la robótica no es el algoritmo sino **dónde vive la verdad**: ellos acumulan creencia en estructuras (rejillas, campos); nosotros journaleamos hechos (toques, decisiones) y derivamos lo demás. Esa es la línea del paper 08 y se mantuvo en todas las anotaciones del código: "misma idea, resuelta con los medios del puppet".
3. Las **capas** que Juan anticipó tienen fundamento en Kuipers (topológico sobre métrico); podremos nombrarlas con ese respaldo sin copiar su implementación.

**Ajuste al dominio**: solo notas `///` con el origen (RoutePlanner, Maneuver/EvasionStrategy, Obstacle, Mark, Body, FloorPlan, Golem.Follow y HeardNear). Ninguna clase ni verbo nuevo: los enriquecimientos de la tabla son **propuestas** para el PLAN.

**Pendiente (estado del arte)**: llevar al PLAN, para decidir con Juan, (a) la condición de terminación tipo Bug como ley de `Fail`; (b) `EdgeCost` distinto de la distancia y desempate determinista; (c) la dirección del golpe en `Bump`; (d) la prioridad de paso como lectura del repertorio en vez de regla del host; (e) el contacto contra pared conocida como corrección de pose (inferencia, nunca verdad).

---

## 2026-09-08 · Laboratorio: el dominio refactorizado desplegado sobre los journals vivos

**Contexto**: `docker compose up -d --build` con el dominio en namespaces (sin cambios en `golemhost` ni en `sim`, que no se reconstruyó: el mundo es el mismo). Los tres golems arrancaron **sin reiniciar journals**: red en la entrada 64, blue en la 37, green en la 14, con las tres marcas de la cara norte de la caja (protocolo de toques de la mañana). Panel de red en el navegador; `/query` con las lecturas nuevas; dos misiones.

**Observación**:
1. **Rehidratación**: `rehydrated at entry N` y `release chain no-op — awake` en los tres, cero errores. Los journals escritos por el dominio anterior replayan idénticos contra `Position/FloorPlan/Trajectory/Body`: la compatibilidad prometida se cumplió en vivo. El único ruido es el DEBUG conocido de `echo-marked` saltándose el script literal de la entrada 1.
2. **Lecturas nuevas contra el golem real** (`POST /query` en red): `places.Corners()` (cuatro ubicaciones etiquetadas), `places.Walls()` con `walls.Doors()` y `doors.Jambs()` (la cocina: cuatro paredes, la puerta kitchen/north con jambas en (4, 8.8) y (4, 10.2), la kitchen/west con jambas en (0.05, 8) y (1.45, 8)), y `g.Evasion(5.5, 4.0, 1.5708, 'step-right')` → `aside@6,4` y `ahead@6,5.2`. La herencia (`Location : Position`, `Wall : Segment`) liga en el motor de producción igual que en los tests.
3. **Escenario "el mapa aprendido sirve"**: red `MoveTo(2, 'kitchen')` desde el garaje: `Route` con dos tramos `around` (6.46, 5.92) y (6.27, 6.4) que rodean la caja por el este usando las marcas de la mañana; cruzó south/garage, center~south, around ×2, north~center, kitchen/north y `Reach` en la entrada 74, **21 s, cero choques**. Blue tomó el punto contado (`Follow`, misión 3, entrada 39) y recorrió el mismo camino con los mismos `around`. Green no hizo nada (nadie le habla), como debe.
4. **Escenario "una cara que nadie tocó"**: red `MoveTo(3, {'5.5,4.8', '4.8,6.6'})` desde la cocina (la primera parada está al sur de la caja; las marcas solo cubrían la cara norte). Tres toques, cada uno con el protocolo completo: `Bump` (entradas 84, 92, 103) → 2.5 s de escucha, nadie → `Mark` (88, 96, 107) → `tell ObstacleFound` a blue y green, acks de vuelta (visibles en el panel: `tell ack 'obstacle-red-…' from blue/green`) → `Learn` en ambos. Tanteo: paso a la derecha, toca de nuevo (la cosa es más ancha), paso a la izquierda, pasa; `Reach (5.5, 4.8)` (entrada 100); tercer toque camino al segundo punto, paso a la derecha, `Cross around` ×2, `Reach (4.8, 6.6)` (entrada 113). Misión completada en ~60 s. Al final los **tres** golems responden `marks: 6, obstacles: 1`: las seis marcas (tres de la cara norte, tres de la esquina sureste) caen a ≤ 1.0 entre sí y forman **una sola figura**, un polígono rojo alrededor de la caja en el panel.
5. **Hallazgo — blue falló su `Follow`**: cuando red alcanzó (5.5, 4.8), blue (parado en la cocina, a la derecha de su carril, en (2.97, 10.16)) tomó el punto y su planificador respondió `Fail(4, 'no road from (2.97, 10.16) to (5.5, 4.8) that fits a body of radius 0.25 past 5 marks')` (entrada 53). Geometría: el hall central mide 3 m (x 4–7) y la caja 0.7 (x 5.15–5.85): a cada lado quedan **1.15 m reales**, y el cuerpo mide 0.5. Pero el planificador exige 0.6 desde cada marca (`MarkReach` 0.25 + radio 0.25 + `MarkMargin` 0.1) y 0.35 desde la pared, y las marcas están sobre las caras (x 5.0 y 6.0): la ventana para el centro del cuerpo queda en 0.05 m por el oeste y 0.05 por el este, y ningún nodo del anillo de rodeo cabe. **El disco de la marca infla la caja 0.35 m hacia afuera más de lo necesario**: el alcance 0.25 es verdad a lo largo de la superficie tocada (la cosa sigue por ahí), pero hacia afuera, por donde vino el cuerpo, la marca YA es el borde. Red sí completó porque venía del otro lado y sus tramos `around` fueron decididos antes de las marcas nuevas.

**Conclusión**:
- El refactor no cambió el comportamiento: rehidratación, rutas, tanteo, habla y figura funcionan igual que en la verificación de la mañana, ahora sobre las clases nuevas. Verificado, no supuesto.
- El hallazgo 5 es el más valioso del día y conecta con dos cosas ya anotadas: la **dirección del golpe** (propuesta (c) de *Estado del arte*: una marca con normal sabe de qué lado está el vacío) y el planteo de Saund et al.: un contacto no localiza un volumen, restringe una hipótesis; tratar el punto como un disco lleno es la hipótesis más pesimista posible. Con la dirección, la marca sería un semidisco (o un punto con normal) y ambos laterales del hall se abrirían.
- La figura de seis vértices confirma que `Obstacle` como **hipótesis derivada** (nunca journaleada) se recompone igual en los tres mapas a partir de los mismos hechos: tres journals distintos, una sola geometría.

**Ajuste al dominio**: ninguno hoy. Propuesta concreta al PLAN, para Juan: `Bump(id, x, y, heading)` / `Mark(x, y, heading)` (o el rumbo como `expose`) para que la marca conozca la normal del toque; `FloorPlan.Fits` y el anillo de rodeo usarían el alcance solo hacia los lados y hacia adentro. Cambio de verbo → journals incompatibles: se hace cuando Juan lo apruebe y los golems renazcan.

**Pendiente**: (1) el hallazgo 5 al PLAN como decisión; (2) repetir el escenario 2 con green (odometría de ruedas) para medir cuánto corrompe la pose cada golpe del tanteo, que es la pregunta abierta del 7-sep; (3) el seguidor que falla por "no road" hoy se queda quieto con la misión fallida: ¿debería reintentar el plan cuando lleguen marcas nuevas o cuando el líder se mueva? Es una regla del sujeto (verbo ensamblado en el host), no del repertorio.

---

## 2026-09-08 · Inventario del repertorio (commit c22ac22)

**Contexto**: Juan pide ver todos los verbos y métodos actuales del dominio con su clasificación. Levantado con `grep` de los miembros `internal` de cada clase, no de memoria. Clasificación por plano (paper 0A): lo que el sujeto **journalea** (escrituras), lo que **consulta** (lecturas, nunca journaleadas), los **objetos que recorre** con `foreach` (sus propiedades cruzan como escalares por `print`) y las **operaciones internas** de cada repertorio con las que se ensambla todo. Cifras: 17 verbos de escritura (21 con sobrecargas), 51 lecturas en `Golem`, 12 tipos recorribles, ~60 operaciones internas, 17 constantes.

### A. Verbos de escritura (journaleados; en voz del golem; todos devuelven escalar)

| Grupo | Verbo (sobre `g`) | Devuelve | Guardia (DomainException) | Quién lo observa |
|---|---|---|---|---|
| Cuerpo (release `init`) | `Embody(radius)` `Cruise(speed)` `Linger(seconds)` | double | radio y velocidad > 0; linger ≥ 0 | — |
| Mapa (release `map_v1`) | `Chart(name, x, y, w, h)` → `Place`; encadena `.DoorTo(place, x, y)` `.OpenTo(place)` | `Place` (encadenable) | nombre único, w/h > 0; pasaje entre dos lugares distintos; duplicados se ignoran | — |
| Encargos | `MoveTo(id, x, y)` `MoveTo(id, place)` `MoveTo(id, stops[])` `Cover(id, stops[])` `Follow(x, y)` | int (id) | id nuevo y mayor al último; paradas en el mapa; `Follow` acuña su propio handle | `Follow` es el uptake de `PointVisited` |
| Decisión | `Route(id, plan)` | int (tramos) | misión pendiente; una sola vez salvo tras `Bump`; termina en parada; cubre las paradas que faltan | Saga `MissionRouted` |
| Avance | `Cross(id, passage)` `Reach(id, x, y)` | int (id) | el tramo siguiente debe ser ese pasaje / esa parada exacta; la última parada completa | `Reach` → `echo` → `tell PointVisited` |
| Toques | `Bump(id, x, y)` `Bump(x, y)` `Hear(who, x, y)` `Mark(x, y)` `Learn(x, y)` | int (id / conteo) | `Bump(id)` exige misión pendiente; `Hear` exige quién; marcas a < 0.1 son una | `Bump` → `echo-bumped` (+`expose @me`) → `tell BumpedAt`; `Mark` → `echo-marked` → `tell ObstacleFound`; `Hear`/`Learn` son uptakes |
| Cierre | `Fail(id, reason)` `Abandon(id, reason)` | int (id) | pendiente; razón no vacía | Saga |
| Habla | `Announce(id)` | int (id) | alguna parada alcanzada | rodeo del bug 7 del motor |

### B. Lecturas (consultas; nunca journalean; **T** = total, nunca falla; **G** = guardada, consultar antes lo indicado)

| Grupo | Lectura | Devuelve | T/G |
|---|---|---|---|
| Cuerpo | `Radius()` `LingerAfterTold()` | double | T (0 antes del init) |
| | `Speed()` | double | G: init aplicado |
| Mapa | `PlaceCount()` `PassageCount()` `MarkCount()` `ObstacleCount()` | int | T |
| | `KnowsPlace(name)` `IsOnMap(x, y)` `KnowsWallAt(x, y)` `FitsAt(x, y)` `HasRoomAt(x, y)` `AreStops(stops[])` | bool | T |
| | `PlaceAt(x, y)` | string | G: `IsOnMap` |
| | `Distance(from, to)` | double | G: lugares conocidos y algún camino |
| | `Places()` | objetos `Place` | T (se recorre) |
| Camino | `Plan(id, x, y)` | string (texto del plan) | G: `Knows(id)`, punto en el mapa, algún camino que quepa |
| | `Evasion(x, y, heading, strategy)` | objeto `Maneuver` | G: estrategia `back-off` / `step-right` / `step-left` |
| Toques | `HeardCount()` | int | T |
| | `HeardNear(x, y, since)` | string ("" si nadie) | T |
| Misiones | `NextHandle()` `Pending()` `Total()` `FollowingCount()` `NewestFollowingId()` | int | T (0 si nada) |
| | `Knows(id)` `HasPendingMission()` | bool | T |
| | `PendingIds()` | int[] | T |
| | `IsPending(id)` `IsFollowing(id)` `WasAnnounced(id)` `IsRouted(id)` `HasBumpedSinceRoute(id)` `NextIsStop(id)` `HasNewerFollowing(id)` | bool | G: `Knows(id)` |
| | `LegsLeft(id)` `StopsLeft(id)` `Bumps(id)` | int | G: `Knows(id)` |
| | `StatusOf(id)` `NextPassage(id)` | string | G: `Knows(id)` |
| Progreso | `RouteLength()` `DistanceLeft(x, y)` | double | T (sin camino: línea recta) |
| | `RouteSeconds()` `SecondsLeft(x, y)` | double | G: `Speed()` |
| Siguiente tramo | `NextId()` `NextX()` `NextY()` `NextApproachX()` `NextApproachY()` `NextExitX()` `NextExitY()` | int / double | G: `HasPendingMission()` |

### C. Objetos que el sujeto recorre (`foreach`) y lo que exponen

| Objeto (repertorio) | Propiedades (sin paréntesis) | Métodos (con paréntesis) |
|---|---|---|
| `Place` (Plans) | `Name X Y Width Height Center` | `Corners()` `Walls()` `Doors()` `Openings()` `Marks()` `Obstacles()`; `DoorTo()` `OpenTo()` (charting) |
| `Doorway` (vista de puerta desde un lugar) | `To At Width` | — |
| `Opening` (vista de frontera) | `To` | — |
| `Wall : Segment` | `From To Length Midpoint Heading IsVertical IsHorizontal Thickness Height Place` | `Doors()` |
| `Door : Passage` | `A B Name At Width Height` | `Jambs()` |
| `OpenBoundary : Passage` | `A B Name Touches Edge Midpoint` | — |
| `Location : Position` | `Label X Y` | — |
| `Mark : Location` | `Label X Y Reach` | — |
| `Obstacle` | `Center Size Shape` | `Vertices()` |
| `Maneuver : Trajectory` | `Strategy Count IsEmpty StopCount` | `Legs()` |
| `Leg` | `At Name Approach Exit IsStop` | — |
| `Position` | `X Y` | — |

### D. Operaciones internas de cada repertorio (no cuelgan de `g`; son con lo que se ensamblan los verbos)

| Repertorio | Clase | Operaciones |
|---|---|---|
| Geometry | `Position` | `DistanceTo` `HeadingTo` `Along(heading, d)` `Moved(dx, dy)` |
| | `Location` | ctor con `Label` (guardia: no vacío) |
| | `Segment` | `Length` `Midpoint` `IsVertical` `IsHorizontal` `Heading` `DistanceTo(p)` |
| Robots | `Body` | `Embody` `Cruise` `Linger` `Radius` `Speed()` `LingerAfterTold` |
| | `Mission` | escrituras `Route(Trajectory)` `Bump()` `Cross(passage)` `Reach(x, y)` `Fail(why)` `Abandon(why)` `Announce()`; lecturas `IsPending()` `ReadStatus()` `ReadReason()` `IsRouted` `LegsLeft` `NextLeg` `StopsAhead` `StopsLeft` `Bumps` `BumpedSinceRoute`; datos `Id Stops Following ChoosesOrder Announced` |
| | `MissionStatus` | conjunto cerrado `Pending Completed Failed Abandoned` |
| | `HeardBump` | `Who At` |
| Plans | `FloorPlan` | charting `AddPlace` `AddDoor` `AddOpening` `AddMark`; lecturas `PlaceCount` `PassageCount` `MarkCount` `Knows` `Places` `Doors` `Openings` `Marks` `DoorsJoining` `OpeningsJoining` `DoorsOf` `OpeningsOf` `MarksIn` `Obstacles()` `ObstaclesIn` `PlaceNamed` `HasOpeningBetween` `OpeningBetween` `IsOnMap` `PlaceAt` `PlacesOf`; geometría del cuerpo `Fits` `HasRoom` `IsWallAt`; camino `WithDoorCrossings`; programa `AsRelease(upgrade)` |
| | `Place` | `Contains` `ContainsInset` `Touches` `SharedEdgeWith` `StepInto` (+ lo de la tabla C) |
| | `Passage` → `Door` / `OpenBoundary` | `Joins(a)` `Joins(a, b)` `OtherSide` `BothCharted` `PlaceA` `PlaceB`; `Door.Jambs()` `Door.StepInto(side)`; `OpenBoundary.IsCrossedBy(u, v)` `CrossingPoint(u, v)` |
| | `Wall` | `Holds(at, tolerance)` |
| | `FloorPlans` (catálogo estático) | `Names()` `Named(name)` `Arena()` `CrossCorridors()` `RingCorridor()` |
| Routes | `Trajectory` | `Legs()` `Count` `IsEmpty` `StopCount` `LegAt(i)` `Last` `StopsFrom(i)` `Segments(from)` `Length(from)` `AsPlan()` `Parse(text)` |
| | `RoutePlanner(plan, radius[, cost])` | `Road(from, to)` `Road(from, stops)` `RoadLength` `BestOrder` (Dijkstra adentro) |
| | `EdgeCost` → `DistanceCost` | `Name` `Between(a, b)` |
| | `EvasionStrategy` → `BackOff` / `StepAside(Side)` | `Named(name)` `Names()` `From(here, heading)` → `Maneuver` |
| | `Side` | conjunto cerrado `Right Left`; `Named` `Turn` |

### E. Los números que el dominio tiene (constantes y literales)

| Dónde | Nombre | Valor | Qué es |
|---|---|---|---|
| `FloorPlan` | `Height` | 0.5 | H del plano (las paredes de la arena) |
| | `WallThickness` | 0 | la pared como línea |
| | `DoorWidth` / `DoorGap` | 1.4 / 0.8 | hueco físico; a ≤ 0.8 del punto de puerta no hay pared |
| | `DoorClearance` | 0.6 | aproximación y salida perpendiculares a la puerta |
| | `OpeningMargin` | 0.5 | distancia a las esquinas al cruzar una frontera |
| | `WallTolerance` | 0.3 | un toque a ≤ 0.3 de una pared es esa pared |
| | `MarkReach` / `MarkMargin` | 0.25 / 0.1 | disco de la marca; clearance = reach + radio + margen (0.6 con r = 0.25) |
| | `JoinWithin` | 1.0 | marcas a ≤ 1.0 son un obstáculo |
| | `AddMark` | 0.1 | dos toques a < 0.1 son una marca |
| `RoutePlanner` | anillo de rodeo | clearance + 0.08, 8 nodos | puntos por donde el cuerpo rodea una marca |
| | salida desde marcas | radio − 0.05 | el arranque puede rozar una marca, nunca atravesarla |
| | `BestOrder` | ≤ 7 paradas | fuerza bruta; después vecino más cercano |
| `Golem.HeardNear` | (literal) | 1.2 | dos radios y el error del morro: "ese choque era él" |
| `Leg.Detour` | `"around"` | — | nombre del tramo de rodeo |
| `BackOff.Distance` | 0.9 | — | retroceso por el carril |
| `StepAside.Step` / `Run` | 0.5 / 1.2 | — | paso lateral y avance del tanteo |

**Conclusión**: la superficie del sujeto está donde debe (verbos pocos y en su voz; lecturas muchas y totales cuando se puede); los repertorios cargan la profundidad (paper 02: profundidad sobre superficie). Dos cosas saltan del inventario: (a) el `1.2` de `HeardNear` es el único número del dominio que vive como literal dentro de un método en vez de como constante con nombre: debe subir a constante (`FloorPlan`? no: es del cuerpo, `Body.MeetingReach`) en la próxima pasada; (b) `Golem` tiene 51 lecturas frente a 17 verbos: sano según los papers, pero varias lecturas guardadas por `Knows(id)` (`IsPending`, `IsRouted`, `StopsLeft`…) podrían vivir en un objeto `Mission` recorrible (`g.Missions()`), como ya se hizo con el mapa (`g.Places()`); es la misma decisión de Juan del 8-sep ("iterar sobre los objetos y printear sus propiedades") aplicada a las misiones.

**Ajuste al dominio**: ninguno; inventario. Propuestas: (a) y (b) al PLAN.

---

## 2026-09-08 · Discusión de diseño: ¿renombrar por los papers? ¿fachada `Golem` o clases al journal?

**Contexto**: dos preguntas de Juan tras ver el inventario. (1) Los papers de robótica resuelven problemas y proponen algoritmos: ¿qué proponen frente a lo nuestro, y hace falta renombrar métodos? (2) Todo son clases y módulos que se manejan aislados: ¿seguimos manteniendo una interfaz general del golem y nos especializamos del lado de las clases? ¿Esas clases deberían salir al journal o todo se maneja a lo interno?

### 1. Nombres: lo que ellos dicen, lo que decimos, veredicto

| Ellos (robótica) | Nosotros | Veredicto |
|---|---|---|
| *pose* = posición + rumbo (universal, ROS `geometry_msgs/Pose`) | `Position` (x, y); el rumbo viaja suelto como `heading` en `Evasion` y en el host | **Candidato real**: `Pose : Position` con `Heading`, en el repertorio Geometry. Lo pide el hallazgo del disco de la marca (la marca necesita la normal del toque). No es copiar estructura: es una palabra que dice lo que ya cargamos por separado. |
| *waypoint* | `Position` / `Leg.At` | No: era nuestro nombre viejo (`Waypoint`) y Juan pidió POSICIÓN. |
| *path* (geométrico) vs *trajectory* (con tiempo) | `Trajectory` es geométrica; el tiempo entra en el sujeto (`RouteSeconds`, `Speed`) | Divergencia consciente: el canon de Juan es "ruta o trayectoria". Se anota; no se renombra. Si algún día los tramos llevan velocidad, el nombre será exacto. |
| *distinctive place* (Kuipers) | `Place` | Coincide. |
| *landmark* | `Location` (posición con etiqueta) | Canon: ubicación → `Location`. Se mantiene. |
| *contact point* / *collision* | `Mark` / `Bump` | Los nuestros son mejores para un cuerpo con bumper: `Bump` es el verbo en su voz. |
| *footprint* (la forma del robot para planificar) | `Body.Radius` | Coincide en sustancia; no hace falta la palabra. |
| *recovery behaviors* (Nav2: back up, spin, wait) | `EvasionStrategy` → `BackOff`, `StepAside` | Coincidencia exacta de concepto. Canon: "maniobra de evasión". Se mantiene `Evasion`; se anota `recovery` como sinónimo para quien venga de Nav2. |
| *cost function* / *costmap* | `EdgeCost` / nada | `EdgeCost` es más honesto: costo por arista, sin mapa de costos (estructura). |
| *boundary following* (Bug) | el tanteo (`StepAside` + avance) | El nuestro es acotado y redecide; no se renombra. |
| *frontier* (Yamauchi) | — | Futuro (escenario 3). Adoptar la palabra cuando exista la cosa. |
| *occupancy grid* | — | No: es la estructura que el paper 08 nos pide no journalear. |

**Conclusión 1**: no hay renombres forzados. Un solo nombre nuevo se justifica, `Pose`, y solo cuando `Bump`/`Mark` lleven el rumbo (propuesta ya abierta). Los verbos siguen en voz del golem (decisión del 7-sep); los nombres de clase siguen el canon de Juan; donde la robótica tiene la misma idea con otra palabra, la nota `///` la cita y no la copia.

### 2. Fachada `Golem` vs clases al journal

**Hechos que mandan**:
- El motor exige un **global raíz** para el estado (guía E14): `g = Golem()`. Ese es el sujeto del paper 0A: el que ensambla verbos con los repertorios.
- Las **Reactions** y la **Saga** del host correlacionan por `[_:Golem].Reach($missionId, $x, $y)`, `[_:Golem].Mark($x, $y)` y por `expose` dentro de un comando con `@params` planos (reglas 1 y 2 del motor: un comando sin `@params` es invisible; un argumento anidado rompe el matcher). Todo el habla del golem cuelga de que el verbo esté en `g` con el **handle** como parámetro.
- El motor **sí** encadena sobre objetos devueltos y **sí** liga propiedades heredadas (verificado hoy en producción): `g.Chart(...).DoorTo(...)` ya escribe en el journal operaciones de un objeto del repertorio (`Place`), pero solo dentro de un **release** (script literal, que ninguna reacción observa).
- **No sabemos** si una reacción puede observar `g.Mission(@id).Cross(@passage)` (receptor encadenado) o `m = g.Mission(@id); m.Cross(@passage);` (variable). Es una prueba de motor, no una opinión.

**Lo que dicen los papers**:
- 0A: el repertorio no anticipa los verbos; el sujeto los ensambla y la definición vive en el journal. Una fachada que replica una a una las operaciones de los repertorios (`Bump` → `Mission.Bump`) es "superficie sin profundidad"; una que compone (`Route` = parsear + cruces de puerta + `Mission.Route`) es exactamente lo que 02 premia (β/α alto).
- 08: la autoridad no cambia por dónde cuelgue el método; cambia por quién decide qué se observa. Da igual fachada u objeto.
- 01: un actor-agregador sin fin es el god class; `Golem` no lo es (un golem por cuerpo, misiones que terminan). Pero una fachada de 70 miembros SÍ es ancha: la porosidad no está en que exista `g`, sino en que `g` repita lo que sus objetos ya saben.

**Recomendación** (para decidir con Juan):
1. **Mantener `Golem` como único sujeto y única superficie de escritura en runtime.** Los verbos journaleados siguen siendo `g.Verbo(@id, …)` con el handle: es lo que las reacciones y la Saga correlacionan, y es la identidad estable del paper 09. No pasar `Cross`/`Reach`/`Bump` a `Mission` mientras el motor no demuestre que observa receptores encadenados.
2. **Sin interfaz C#.** "Interfaz general" = fachada concreta. El paper 09 y la guía piden cero interfaces declaradas; `Golem` es una clase.
3. **Especializarse del lado de las clases, sí, pero por la vía de las lecturas**: como con `g.Places()`, exponer `g.Missions()` y que `Mission` sea recorrible (`Id`, `Status`, `StopsLeft`, `Legs()`, …), retirando de `g` las doce lecturas guardadas por `Knows(id)`. Las lecturas no journalean y no las observa nadie: mover ahí la especialización no arriesga nada y adelgaza la fachada donde está gorda.
4. **Al journal salen solo los verbos del sujeto y los releases.** Las operaciones de los repertorios (`FloorPlan.AddMark`, `Mission.Cross`, `RoutePlanner.Road`) se manejan a lo interno, compuestas por `g`. Excepción vigente y sana: la cadena de charting dentro del release (`Chart(...).DoorTo(...)`), porque un release es programa literal sin observadores. Regla que propongo escribir en el PLAN: **"escritura encadenada sobre objetos solo en releases; en runtime, verbos sobre `g` con handle"**.
5. **Probar antes de mover nada**: un scratch con el motor real que defina una reacción sobre `[_:Mission].Cross($passage)` y ejecute (a) `g.Mission(@id).Cross(@p)` y (b) `m = g.Mission(@id); m.Cross(@p);`. Si el motor observa alguna de las dos, la regla 4 se puede relajar; si no, queda como ley del motor (regla 10 de *Reglas del motor que aprendimos a golpes*). Es el laboratorio siguiente si Juan quiere abrir esta puerta.

**Ajuste al dominio**: ninguno hoy. Propuestas al PLAN: `Pose`, `g.Missions()` con `Mission` recorrible, la regla de escritura encadenada, y la prueba del motor.

---

## 2026-09-08 · Diseño: la colisión como cadena hecho → reacción → hipótesis → conclusión, con el razonamiento en el dominio

**Contexto**: Juan precisa la pregunta anterior. No es dónde cuelgan los métodos sino **quién razona**: "COLISIÓN > comando al journal > Reaction > se dispara el proceso para tomar una decisión en el módulo (quizá con una query para saber qué sigue) > la conclusión se guarda en el journal; esa conclusión también es una clase del dominio, algo que hereda de obstáculo… o descubrir que era otro robot y solo queda como historia… la idea es modelar la realidad con el único sensor que tenemos, el de colisión".

**Dónde está hoy cada pieza** (leído del host, `GolemChoreography.BumpAndListenAsync`):

| Paso de Juan | Hoy | Quién razona |
|---|---|---|
| Colisión | el navigator devuelve `Collided(with, x, y)`; el host estima el punto (pose + radio en el rumbo) | host |
| ¿Pared conocida? | `g.KnowsWallAt(x, y)` (lectura del dominio) → reintento del tramo, **nada al journal** | host pregunta, dominio responde |
| Comando al journal | `g.Bump(@id, @x, @y)` con `expose @x, @y, @me` | dominio (hecho) |
| Reaction | `echo-bumped` → `tell BumpedAt` a cada peer; el peer hace `g.Hear(who, x, y)` | motor |
| Decisión | el host **espera 2.5 s con su reloj**, pregunta `g.HeardNear(x, y, since)` cada 250 ms y decide en C#: alguien → "era un cuerpo", nadie → `g.Mark(x, y)` | **host** |
| Conclusión al journal | `Mark(x, y)` solo si fue cosa; si fue un cuerpo **no se journalea nada** (la cesión es runtime) | dominio, a medias |
| La clase de la conclusión | `Obstacle` derivada de las marcas: una sola clase, sin distinguir cosa de cuerpo ni de pared | dominio, sin jerarquía |

**Lo que dicen los papers de esta cadena**: 03 (la partición): el verbo promete lo inmediato — que el toque quede registrado y expuesto — y lo diferido va a una Reaction; pero **el reloj es del host** (guía E31: orquestación, tiempo y azar son del host). 08: el hecho es testimonio (Bump, Hear), la hipótesis se deriva (Obstacle), y una **decisión** tomada sobre la hipótesis es un acto vivido que sí se journalea (como `Route`). 0A: el razonamiento es una **operación del repertorio** (profundidad) que el sujeto invoca; la cadena es un verbo ensamblado. 01: las conclusiones son **variantes**, no un `Kind` string.

### La cadena, en términos del puppet

```
mundo: contacto ──► host estima el punto (pose + radio en el rumbo)
  1. hecho        g.Bump(@id, @x, @y, @heading)     journal; expose x, y, heading, me
  2. reacción     echo-bumped → tell BumpedAt        a cada peer (existe)
                  peer: g.Hear(@who, @x, @y)         hecho en el journal del otro (existe)
  3. hipótesis    g.Suspect(@x, @y, @heading, @since) LECTURA que devuelve una Suspicion:
                    KnownWall  (a ≤ 0.3 de una pared del plano, fuera de sus puertas)
                    MetPeer    (un Hear a ≤ 1.2 desde que empezó el tramo: who)
                    Thing      (nadie: la marca, con su normal)
                  el host solo aporta el tiempo: espera 2.5 s y pregunta una vez
  4. conclusión   el verbo que la Suspicion nombra, en voz del golem:
                    g.Mark(@x, @y, @heading)   "era una cosa"  → echo-marked → Learn en los peers (existe)
                    g.Met(@who, @x, @y)        "era blue"      → NUEVO: hoy no queda historia
                    (pared conocida)           reintento; ¿merece g.Graze(@x, @y)? pendiente del 7-sep
  5. el obstáculo  Obstacle (hipótesis derivada de las marcas, nunca journaleada) se vuelve jerarquía:
                    Thing  : figura de marcas (punto / línea / polígono), la que se planifica
                    Peer   : el encuentro con un cuerpo, transitorio, solo historia: no se planifica
```

**Qué cambia respecto a hoy**: (a) la clasificación deja el C# del host y pasa al repertorio `Plans` (una operación `Suspect` que compone `IsWallAt`, `HeardNear` y las marcas); (b) la conclusión "era un robot" queda escrita (`Met`), porque hoy la historia se corta ahí; (c) `Obstacle` se abre en variantes por origen; (d) `Bump`/`Mark` llevan el rumbo, con lo que la marca conoce la normal y el hallazgo del disco se resuelve de paso. Lo que **no** cambia: el reloj de los 2.5 s sigue en el host (el dominio no espera); las Reactions siguen siendo las que hablan; los hechos siguen siendo `Bump`/`Hear`.

**Variante sin reloj (para discutir)**: concluir `Mark` de inmediato en la reacción sobre `Bump` (`Causation.Continue("g.Mark(@x, @y, @heading)")`, el motor lo permite: `echo-reached` ya escribe `g.Announce` desde una reacción) y **retractar** por reacción sobre `Hear`: si llega un `Hear` a ≤ 1.2 de una marca propia reciente, `g.Met(@who, @x, @y)` reclasifica la marca como encuentro. El journal contaría "choqué, lo tomé por una cosa, oí a blue, era blue". Más fiel al paper 08 (la historia se resuelve en el camino, como dice Juan) y sin timer, pero propaga `Learn` a los peers antes de la retractación: haría falta `Unlearn` o que los peers también deriven. Se anota; la variante con reloj es la que cabe en el host de hoy.

**Lo que hay que probar antes** (laboratorio, no opinión): (1) que una lectura del dominio pueda devolver un objeto de una jerarquía (`Suspicion` → `KnownWall`/`MetPeer`/`Thing`) y que el DSL imprima `suspicion.Kind` y `suspicion.Who` sin importar la variante en tiempo de ejecución (`DotAccess` resuelve polimórfico, dice el código del motor; falta verlo); (2) si una reacción puede encadenar `Seek Bump` → `ThenSeek Hear` con `.Within(2.5 s)` para detectar la **ausencia** de un `Hear` (si el motor lo da, el reloj también podría salir del host).

**Ajuste al dominio**: ninguno hoy: `Bump`/`Mark` con rumbo y `Met` son verbos nuevos → al PLAN primero (regla del 7-sep). Propuesta al PLAN, *El protocolo de toques, segunda versión*: los verbos de arriba, la jerarquía `Obstacle` → `Thing`/`Peer`, la lectura `Suspect`, y las dos pruebas del motor. Journals incompatibles cuando se haga (firmas de `Bump`/`Mark`).

**Decisión de Juan (misma tarde)**: "sí, es correcto: el único que toma las decisiones es el dominio, no el host; este reescribe en su journal la ruta, cómo debe terminar y las etapas para continuar; el host solo las sigue y dice si pudo o no". Fijado en `CLAUDE.md` (*The domain is the brain*) y en el PLAN (*El protocolo de toques, segunda versión*).

---

## 2026-09-08 · Laboratorio: el motor concluye por el golem (pruebas del protocolo de toques v2)

**Contexto**: scratch `touchlab` (fuera del repo, en el scratchpad de la sesión; MSTest + `Ncubo.Puppeteer 2.0.1-beta.10017-portable.2` del localfeed; reproducible en minutos). Un repertorio de juguete `Lab` con `Bump(id, x, y)`, `Hear(who, x, y)`, `Mark(x, y)`, `Ping(n)` y una jerarquía `Suspicion` → `KnownWall` / `MetPeer(who)` / `Thing`. Actor real, storage IN_MEMORY, reacción `.Cue()` definida antes de `Start()`.

**Observación** (5 pruebas, todas en verde al final):
1. **Lectura polimórfica**: `print lab.Suspect('peer').Kind 'kind', lab.Suspect('peer').Who 'who'` → `peer`, `blue`; `foreach (s in lab.Suspicions()) { print s.Kind, s.Who }` sobre una lista mixta → `wall`/`peer`/`thing` con `green` en el peer. El DSL liga por el tipo en tiempo de ejecución; `Who` virtual con base `""` funciona. **La `Suspicion` como jerarquía es viable.**
2. **Ausencia en ventana**: reacción `Seek("Bumped").One().OnMatch("[_:Lab].Bump($id, $x, $y)") .ThenFinalSeek("Heard").None().Within(2.5 s).OnMatch("[_:Lab].Hear($who, $hx, $hy)") .Causation.Continue("lab.Mark(@x, @y);")`. Tras `Bump`, 0.5 s: nada; **3.5 s de silencio: nada** (la ventana no se cierra con el reloj de pared); un `Ping` posterior → `Mark` escrito en ≤ 1 s. **La reacción concluyó y escribió un comando del dominio sin tell**: `Causation.Continue` con solo `lab.Mark(...)` vale.
3. **Un `Hear` dentro de la ventana** → ningún `Mark`: el `None` muere con el primer evento contado. "Era un cuerpo".
4. **`Where` de distancia**: con `"($hx - $x) * ($hx - $x) + ($hy - $y) * ($hy - $y) <= 1.44"` la prueba del `Hear` lejano FALLÓ: el `Hear` a 6 m contó como cercano y no hubo `Mark`. Sin excepción, sin aviso. Con `@hx`, `@x`… **pasa**: el `Hear` lejano no cuenta (hay `Mark`), el cercano sí (no hay `Mark`). Regla del motor: en `Where` las capturas van con `@`; con `$` el filtro no filtra y no avisa.
5. Tiempos: cada prueba de ventana ≈ 4–5 s (la espera); la lectura polimórfica 59 ms.

**Conclusión**:
- Las dos piezas que faltaban para que **el dominio razone** existen en el motor: la hipótesis como objeto con variantes (lectura `Suspect`) y la conclusión escrita por una **reacción** sobre la ausencia o presencia de un `Hear` cercano. El C# de `BumpAndListenAsync` puede desaparecer como razonador.
- El reloj sigue siendo del host en un sentido preciso: la ventana la cierra **la siguiente entrada del journal**. Un cuerpo que se queda quieto tras el toque no cierra nada; el host debe escribir su siguiente acto (o esperar la ventana con su reloj y escribirlo). Eso está bien: es exactamente "el host espera y reporta"; lo que ya no hace es decidir.
- Un fallo silencioso (`$` en `Where`) es el tipo de cosa que solo se descubre midiendo: la prueba negativa (el `Hear` lejano) fue la que lo reveló. Mantener siempre el caso negativo en los laboratorios del motor.

**Ajuste al dominio**: ninguno todavía; el PLAN ya tiene la sección con los verbos (`Bump`/`Mark` con rumbo, `Met`), la lectura `Suspect`, la jerarquía `Obstacle` → `Thing`/`Peer` y `Pose`. Reglas 10 y 11 añadidas a *Reglas del motor que aprendimos a golpes*.

**Pendiente**: (1) implementar el protocolo v2 cuando Juan confirme los nombres (`Met`, `Suspect`, `Thing`/`Peer`) — journals renacen; (2) decidir quién escribe la entrada que cierra la ventana cuando el cuerpo se queda quieto (el siguiente acto del host es lo natural; un verbo solo para cerrar ventanas sería un síntoma infraestructural, paper 06); (3) mover el scratch `touchlab` al repo si queremos que las pruebas del motor vivan con el proyecto.

**Juan (misma tarde)**: journals desechables, `Suspect` provisional (quizá se renombre), `Graze` sí y journaleado "para saber qué decisión tomará si continúa". → Implementado (entrada siguiente).

---

## 2026-09-08 · Implementación del protocolo de toques v2: el dominio razona, el host escribe lo que el dominio nombra

**Contexto**: con el sí de Juan. Variante elegida: **reloj del host, razón del dominio** (variante A). La reacción que concluye por ausencia (`None().Within`) queda probada pero no se usa todavía, porque la ventana la cierra la siguiente entrada del journal y un cuerpo quieto tras el toque no escribe ninguna: habría que inventar un verbo solo para cerrar ventanas (síntoma, paper 06). El host espera 2.5 s, pregunta `g.Suspect(...)` y journalea el verbo que la sospecha nombra. Nada de la clasificación queda en C#.

**Ajuste al dominio** (`golemdomain/`, 43 tests en verde):
- `Geometry.Pose : Position` con `Heading`, `Forward`, `Ahead(p)`.
- `Plans.Mark` ahora lleva `Heading` (la normal del toque) y decide `Blocks(center, radius)`: bloquea dentro de `Reach + radio + margen` **salvo** del lado por donde vino el cuerpo, libre a `radio + margen` (0.35 en vez de 0.6). `FloorPlan.Fits` y `RoutePlanner.Sees` (en el punto más cercano del tramo a la marca) usan `Blocks`. Test nuevo: a 0.4 por detrás de la marca el cuerpo cabe; a 0.25 no; lateral a 0.5 no, a 0.65 sí.
- `Plans.Obstacle` abstracta → `Thing` (figura de marcas; la que se planifica) y `Peer` (encuentro con un cuerpo: `Who`; historia). `FloorPlan.Obstacles()` devuelve ambas; `Things()` solo las cosas. `Kind` es propiedad de la variante, no un campo.
- `Plans.Suspicion` abstracta → `WallTouched` (Graze), `PeerMet(who)` (Met), `ThingFound` (Mark); cada variante nombra su `Conclusion`.
- `Golem`: `Bump(id, x, y, heading)`, `Bump(x, y, heading)`, `Graze(id, x, y)`, `Mark(x, y, heading)`, `Learn(x, y, heading)`, `Met(who, x, y)`; lecturas `Suspect(x, y, heading, since)`, `Grazes(id)`, `MayRetryLeg(id)`, `ThingCount()`, `MetCount()`. `HeardNear` usa `Body.MeetingReach` (1.2, ya con nombre).
- `Robots.Mission`: `Graze()` cuenta roces por misión y por tramo; `PatienceWithWalls = 3`; `MayRetryLeg`; el contador del tramo se reinicia en `Cross`/`Reach`/`Route`. **La paciencia con las paredes dejó de ser una constante del host.**

**Ajuste al host** (`golemhost/`, compila): `Collision` lleva `Heading` (la pose al tocar; `DiffDriveNavigator`); mensajes `MissionBumped`/`ObstacleMarked` con rumbo, nuevos `PeerMet` ('E') y `MissionGrazed` ('G'); handlers `g.Met`, `g.Graze` (con Check pendiente); la reacción `echo-marked` casa `Mark($x, $y, $heading)` y el tell `ObstacleFound` viaja con rumbo → uptake `Learn(@x, @y, @heading)`. El lazo de colisión: `Suspect` primero; `wall` → `Graze` journaleado → `MayRetryLeg` decide reintento o `Fail("…patience spent")`; si no → `BumpAndListenAsync`, que journalea el `Bump`, espera la ventana consultando `Suspect`, y escribe **la conclusión que la sospecha nombra**: `Met` (→ coordinación) o `Mark` (→ tanteo). El tanteo pregunta `Suspect(...).Kind == "wall"` en vez de `KnowsWallAt`. Eliminados del host: `RetriesAfterBump`, `wallBumps`, `KnowsWallAt`.

**Journals**: incompatibles (firmas de `Bump`/`Mark`/`Learn`); los tres archivados en `journal-legacy-2026-09-08-touch-v1/`. README actualizado. Verificación en vivo: entrada siguiente.

---

## 2026-09-08 · Laboratorio: el protocolo de toques v2 en vivo

**Contexto**: `docker compose up -d --build` con journals nuevos (los tres nacen en la entrada 2). Docker Desktop estaba apagado y hubo que arrancarlo. Escenarios: red de la sala a la cocina (sin cruzar el centro), de la cocina al garaje (por el centro, donde está la caja sin marcar), otra vez al garaje desde junto a la caja, y contra la pared este del garaje; blue siguiendo a red; green solo escuchando.

**Observación**:
1. **Cocina desde la sala**: por el corredor oeste, sin toques; blue siguió y se orilló a la derecha. Cero marcas: el protocolo no inventa nada cuando nada se toca.
2. **Garaje por el centro (misión 2 de red)**: `Bump(2, 4.9, 5.8, -1.37)` (entrada 20) → 2.5 s → `Suspect` = thing → `Mark(4.9, 5.8, -1.37)` (26) → `ObstacleFound` con rumbo → `Learn` en blue y green: **los tres mapas con la misma marca y la misma normal**. Tanteo: derecha bloqueada por la pared oeste del hall (el dominio, `HasRoomAt`), paso a la izquierda → segundo toque `(5.1, 5.7, 0.07)` (normal hacia el este: la cara oeste de la caja) → marca 2. Después **`Fail: no road from (4.8, 5.63) … past 1 marks`**. Dos defectos:
   - *Dominio*: el cuerpo quedó a **0.19 m** del punto de su propia marca (la estimación "morro + radio" supone toque frontal; fue un roce de costado en la esquina noroeste de la caja) y la regla de arranque exigía 0.20: el planificador creía al cuerpo dentro de la cosa. **Regla nueva en `RoutePlanner.Sees`**: una marca dentro del propio radio del cuerpo es una estimación fallida; no le prohíbe salir mientras la carrera no se acerque a la marca más de lo que ya está (el punto más cercano es el arranque). El caso vivo quedó como test (`ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt`, con las coordenadas exactas) y el test viejo que esperaba `Refuses` en la situación análoga cambió: ahora sale por donde entró y no atraviesa la marca.
   - *Host*: "deciding the road again with 1 marks" se imprimió antes de que la segunda marca existiera: `WaitUntilAsync(() => Marks() >= marksBefore)` era siempre verdadero. Ahora `>` (con el timeout de 5 s del propio `WaitUntilAsync` para el caso de una marca duplicada que no suma).
3. **Garaje desde junto a la caja (misión 3, tras redesplegar; journals compatibles, rehidratación limpia en 41/17/9)**: el planificador encontró la salida (`center~south@5.82,3 …`), tocó otra vez la cara oeste `(5.0, 5.3, -1.09)` → marca 3 → paso a la derecha → cruzó `center~south`, `south/garage`, redecidió (`another road garage@9,1.5`) y **llegó** (entrada 55). 27 s.
4. **Pared conocida (misión 5, a (11.0, 1.5), sobre la línea de la pared este del garaje)**: `Suspect` = wall → **`g.Graze(5, 10.925, 1.500)`** ×3 (entradas 75, 76, 77: define de la acción 14 y tres invocaciones), `MayRetryLeg` verdadero dos veces, falso a la tercera → `Fail(5, 'still grazing wall_garage_e at (10.9, 1.5) after 3 grazes: patience spent')` (78). **La paciencia con las paredes la decidió el dominio**; en el host no queda constante. (Con (10.9, 1.5) no hubo roce: la llegada con tolerancia 0.25 frena antes de la pared.)
5. **Blue, siguiendo a red al garaje**: cinco toques a la caja desde el norte y el este, cinco `Mark` con normales `-1.26, -2.88, -1.08, -2.10, -2.53` (las caras norte y este, apuntando hacia dentro), "no way past by feel, 1 right and 3 left — deciding the road again with 8 marks" → `another road center~south@6.33,3 …` → llegó. **Los tres mapas terminan con 8 marcas y UNA figura, un polígono de 8 vértices**, green por puro `Learn` (heard 9).
6. Ningún encuentro entre cuerpos ocurrió (met 0 en los tres): `Met` queda verificado solo en tests.

**Conclusión**:
- La cadena de Juan está viva: colisión → hecho con rumbo → reacción (tell) → el dominio sospecha (`Suspect`) → la conclusión que la sospecha nombra, en el journal (`Mark`/`Graze`; `Met` pendiente de ocurrir) → el obstáculo como clase (`Thing` polígono de 8). El host ya no clasifica ni cuenta reintentos: espera, pregunta y escribe.
- La normal cumplió: blue rodeó la caja con 8 marcas y encontró camino; con el disco de la mañana 5 marcas habían sellado el hall.
- El error de estimación del punto de contacto (toque de costado tomado como frontal) es la fuente de marcas "dentro del cuerpo"; la regla de arranque lo absorbe, pero la cura de fondo sería estimar el punto con la dirección del movimiento y no con el morro, o un bumper con sectores (izquierda / frente / derecha), como en los robots aspiradora.

**Ajuste al dominio**: `RoutePlanner.Sees` (regla de arranque para marcas dentro del radio) + test del caso vivo + test ajustado. Host: la espera por la marca aplicada. 44 tests en verde.

**Pendiente**: (1) provocar un encuentro entre cuerpos para ver `Met` en vivo (dos misiones cruzadas en una puerta, como el 8-sep por la mañana); (2) el punto de contacto por dirección de movimiento o bumper por sectores → PLAN; (3) commit de todo esto cuando Juan lo pida.

---

## 2026-09-09 · `Hear` pasa a llamarse `HearBump` (y lo que el renombre destapó)

**Contexto**: Juan pide renombrar "el método de learn o hear dependiendo cuál es el que habla del bump". De los dos uptakes, el que recibe un **bump** contado por un compañero es `Hear`; `Learn` recibe una **marca**. Solo se renombra el primero.

**Ajuste al dominio**: `Hear(who, x, y)` → **`HearBump(who, x, y)`**, y con él las dos lecturas que le siguen (regla del 7-sep: las lecturas siguen a su verbo): `HeardCount()` → `HeardBumpCount()`, `HeardNear(x, y, since)` → `HeardBumpNear(x, y, since)`. `Learn(x, y, heading)` se queda como está: habla de una marca, no de un toque. El uptake del host pasa a `g.HearBump(@who, @x, @y);`; el botón del panel y las tablas de verbos de README, PLAN y `CLAUDE.md` quedan al día.

**Observación** (lo que el renombre destapó, que es lo que justifica la nota): el host aún tenía su propio helper `HeardNear(x, y, since)` — una consulta a `g.HeardNear` — **muerto desde la migración a `Suspect`** de ayer: nadie lo llamaba. Era el último resto del host preguntando "¿quién chocó cerca?" para clasificar él mismo. Eliminado. El host solo pregunta ya `g.Suspect(...)`, que es la hipótesis del dominio.

**Verificación en vivo** (journals archivados en `journal-legacy-2026-09-09-hear/`, los tres golems nacen en la entrada 1): red al garaje por el sur, sin toques. Blue, siguiéndolo, cruza el hall central y choca tres veces con la caja: cada `Bump` viaja como `tell BumpedAt` y en los journals de red y green aparece **`g.HearBump('blue', 5.41…, 5.85…)`** (leído crudo del carril *Journal — live*), seguido de `g.Learn(5.41…, 5.85…, -1.68)` cuando blue concluye la marca. Cierre: los tres con 3 marcas y 1 `Thing`; `HeardBumpCount()` 3 en red y green, 0 en blue (quien choca no se oye a sí mismo). 44 tests en verde, host compila.

**Conclusión**: el par de uptakes queda legible en el journal — se oye un **bump** (`HearBump`), se aprende una **marca** (`Learn`). Un renombre pequeño sirvió de barrido: la migración de ayer había dejado código del host que ya no decidía nada pero seguía preguntando.

**Pendiente**: si Juan quiere la simetría completa, `Learn(x, y, heading)` → `LearnMark(x, y, heading)`; es otro renombre de verbo journaleado (journals renacen otra vez). Sin decidir.

---

## 2026-09-09 · Laboratorio: la ruta como cola de posiciones que bombea una reacción

**Contexto**: Juan plantea el diseño: "todo esto es como una cola de posiciones; si defino un Route, una reaction se da cuenta y tendría que escribir su MoveTo para llegar a la primera posición… al final la Route es una serie de MoveTo y el robot retroalimenta diciendo que ya llegó; si ya llegó Reach, entonces la reaction le da el siguiente punto". Y lo cierra: "el MoveTo es casi que solo delegarle esa tarea al host; el host debe ser lo más básico posible, no toma decisiones, solo comunica lo que pudo o no hacer" y "cada Reaction le da retroalimentación al dominio para ver qué es lo siguiente que debería hacer el robot acorde al dominio". Antes de tocar el dominio, se mide la mecánica en el scratch `touchlab` con un repertorio de juguete (`Convoy`: `Errand`, `Route` como cola, `Head` como desencolado, `Reach`, `Announce`).

**Observación** (4 pruebas, todas en verde; `dotnet test --filter ClassName~RouteQueueProbe`):

| # | Lo que se midió | Resultado |
|---|---|---|
| P1 | Una reacción sobre `Route` escribe el primer paso en el journal del propio actor | Sí: `Causation.Continue("convoy.Head(@id);")` sin tell. Tras el `Route`, el punto vigente es el primero de la cola |
| P2 | Una reacción sobre `Reach` escribe el paso siguiente, y la cadena recorre la cola entera | Sí. Historia journaleada: `errand:1 route:3 head:1 reach:1 head:2 reach:2 head:3 reach:3`. El host solo leyó el punto vigente y reportó llegada |
| P3 | Dos reacciones distintas observan el MISMO `Reach` | Sí: la bomba y el eco dispararon las dos (`announce` intercalado en la historia). No hay exclusión entre reacciones sobre el mismo patrón |
| P4 | La cadena sobrevive un reinicio a media cola | Sí, y **sin re-disparar**: al rehidratar, entradas 13 → 13 (el cursor durable no repitió la bomba sobre los `Reach` viejos), estado intacto (2 pasos, 1 llegada), y el `Reach` siguiente volvió a bombear. La conducción terminó cruzando el reinicio |
| P5 | Qué cuesta el último `Reach`, cuando ya no queda cola | La guarda `if (convoy.HasNext(@id)) { … }` dentro del script impide que el dominio "vaya a ninguna parte" (0 pasos vacíos), **pero la entrada del script se escribe igual**: el último `Reach` cuesta 2 entradas (11 → 13), una de ellas un no-op |

Costo medido: una conducción de 3 puntos con 2 reacciones = 19 entradas (incluye defines, el encargo y los ecos). Frente al esquema de hoy, **una entrada más por tramo** (el paso), más la del no-op final.

**Conclusión**:
- El diseño de Juan es realizable con este motor, tal cual, y la pieza que más preocupaba (rehidratar a media cola) es la que mejor salió: el checkpoint durable no repite pasos ya dados y la cadena se reanuda sola. Eso hace la conducción **reanudable por construcción**, algo que hoy depende del lazo del host.
- Se cae la duda de la regla 7: no hace falta que nadie observe lo que la reacción escribe. La cadena alterna — el host escribe el reporte (`Reach`, observable), la reacción escribe la orden (`MoveTo`, que nadie necesita observar).
- **Consecuencia de nombres** (de las dos aclaraciones de Juan): el paso de la cola **es** el `MoveTo` — la orden que se le delega al host, "lleva el cuerpo exactamente a este punto". Entonces `MoveTo` deja de ser el encargo del operador y ese encargo necesita nombre propio. El reparto queda: encargo del operador (nombre por decidir) → `Route` (la decisión: la cola) → `MoveTo` (la orden al host, escrita por la reacción) → `Reach`/`Cross`/`Bump`/`Graze` (lo que el host pudo o no pudo).
- La regla general que Juan enuncia — "cada reacción le pregunta al dominio qué sigue" — cierra la arquitectura: **el host tiene exactamente dos deberes, cumplir el `MoveTo` vigente y reportar el desenlace**; todo lo demás es reacción más dominio. Las tres cadenas quedan simétricas: `Reach` → siguiente `MoveTo` (o misión completa); `Bump` → `Mark`/`Met` y después `Route` de nuevo o el paso siguiente del tanteo; `Graze` → reintento del mismo `MoveTo` o `Fail` cuando se agota la paciencia.

**Ajuste al dominio**: ninguno todavía. El laboratorio vive en el scratchpad (`touchlab/RouteQueueProbe.cs`), fuera del repo.

---

## 2026-09-09 · Fase 0: cómo viaja el punto hasta la orden (`expose`)

**Contexto**: para que la orden se journalee como `MoveTo(id, x, y)` y no como `MoveTo(id)`, la reacción tiene que **capturar** el punto siguiente, y de un reporte de llegada solo captura el punto ya alcanzado. El único canal posible es el `expose` que escribe el comando de llegada. Cuatro preguntas al motor (`touchlab/ExposeProbe.cs`, repertorio de juguete `Relay`).

| # | Pregunta | Resultado |
|---|---|---|
| Q1 | ¿`expose` acepta un valor **calculado** (`expose relay.NextX() nx`)? | El comando se acepta y **aislado funciona** (dos corridas, 263 y 308 ms), pero corriendo junto a las otras pruebas la reacción no disparó en 5 s. No se entiende; **se descarta esta forma** |
| Q2 | ¿Puede el comando asignar a un parámetro y exponerlo? | **Sí, siempre**: `@nx = relay.NextX(); expose @id id, @nx nx, @ny ny;` La reacción captura y escribe `moveto:6.19,3` — el punto SIGUIENTE, no el alcanzado |
| Q3 | ¿Puede el `expose` ir dentro de un `if`? | **Sí** |
| Q4 | Con la cola agotada, ¿qué cuesta el último reporte? | **Nada**: si no hay siguiente, no se expone, la reacción **no dispara** y no se escribe entrada. Medido: la última llegada movió el journal 11 → 12, solo el reporte |

**Conclusión**:
- La orden **puede llevar el punto**: `g.MoveTo(1, 6.19, 3)` en el diario, no un `MoveTo(1)` mudo. Se usa la forma de Q2 (parámetros) con la guarda de Q3, que es la que pasó en todas las configuraciones.
- **Q4 borra el costo que el plan daba por hecho**: la entrada no-op del final no existe si la guarda vive en el comando en vez de en el script de la reacción. El diseño mejora y el plan se corrige.
- La reacción casa por la **forma del `expose`** (`expose $id id, $nx nx, $ny ny;`), como ya hace `echo-bumped` en el host. Los nombres expuestos deben ser únicos por cadena para que dos reacciones no se pisen.
- Anomalía anotada, no resuelta: Q1 depende de con quién corra. Sospecha (sin probar): la espera activa consultando cada 100 ms compite con el hilo de la reacción. Evitar la forma y, en los laboratorios, no encuestar tan seguido.

**Ajuste al dominio**: ninguno; es la medición que habilita la fase 1.

**Pendiente**: (1) el nombre del encargo del operador, que es lo único que bloquea (candidatos: `Visit(id, …)` para el encargo ordenado, dejando `Cover` y `Follow` como están); (2) decidir si `Cross` sobrevive como "este paso era una puerta" o se deduce del nombre del punto; (3) el no-op del final: vivir con él, o que el último paso lo escriba el propio `Reach` cuando completa; (4) implementar, con journals nuevos.

---

## 2026-09-09 · Auditoría del dominio contra la guía `puppeteer-domain-modeling` (E1–E45)

**Contexto**: Juan pide validar si el dominio respeta la guía. Se leyó la guía completa (`repos/Skills/puppeteer/training-lab/guides/puppeteer-domain-modeling/SKILL.md`, 1401 líneas, entradas E1–E45) y se auditó `golemdomain/` regla por regla, con verificación mecánica (grep) donde la regla es mecánica y juicio donde es de diseño. El host se revisó solo para las reglas que lo tocan (E20, E31, E38).

**Observación — lo que cumple, con evidencia**:

| Entradas | Regla | Evidencia |
|---|---|---|
| E2, E15, E42 | Todo `internal`, sin base marcadora ni atributo; un solo público | único `public` en todo el assembly: `GolemDomain.Assembly`; cero `: Objeto` / `[Puppet]` |
| E23 | Sin igualdad de valor, sin `record`/`struct`, sin colecciones con clave por valor | cero `Equals`/`GetHashCode`/`IEquatable`/`==`/`record`/`readonly struct`. Los únicos `Dictionary`/`HashSet` (`RoutePlanner` 142–143) llevan como clave el `Node` privado **sin** igualdad: clave por identidad, que E23 permite explícitamente |
| E6, E31, E44 | Ni E/S, ni reloj, ni azar en el dominio | cero `File.`/`Console.`/`DateTime`/`Random`/`Environment.`. Pose, rumbo y `who` se **reciben** |
| E7, E8 | Sin hilos ni asincronía | cero `lock`/`Task`/`async`/`Interlocked`/`Concurrent` |
| E9 | Sin `out`; resultado explícito | los dos `out` son de `double.TryParse` dentro de un privado. Portadores: `Suspicion`, `Trajectory` |
| E37, E38 | La persistencia ES el journal; sinks fuera | cero repositorio/`Save`/`Insert`. `Golem.Domain.csproj` no tiene **ninguna** referencia |
| E20 | El endpoint es contrato, no host con campo | el host usa el namespace del dominio **una vez** (`Program.cs:4`, para `GolemDomain.Assembly`); nunca instancia un `Golem` |
| E45 | Sin `null`/`?` en la superficie | cero anotaciones nullable; ausencia = `""` (`HeardBumpNear`, `Suspicion.Who`, `Obstacle.Who`) |
| E21 (cobertura) | Todo tipo con `///` de rol | cero clases sin resumen |
| E11, E43 | La herencia lleva verdad; hojas selladas, bases abiertas | `Location : Position`, `Mark : Location`, `Wall : Segment`, `Pose : Position`, `Maneuver : Trajectory`; bases abiertas (`Passage`, `Obstacle`, `Suspicion`, `EdgeCost`, `EvasionStrategy`, `Trajectory`), hojas `sealed` |
| E16 | Conjuntos cerrados con ctor privado; los seleccionados desde el borde con resolver | `MissionStatus` (interno, sin resolver: el borde solo ve `StatusOf()`), `Side.Named`, `EvasionStrategy.Named` (resolver total-con-throw) |
| E41 | Polaridad de lecturas | registro por id lanza + consulta de reconocimiento (`Find` ↔ `Knows`); accesor de opcional lanza + consulta de presencia (`NextId/NextX…` ↔ `HasPendingMission`); rangos densos totales (`IsOnMap`, `KnowsWallAt`, `FitsAt`, `HasRoomAt`) |
| E19, E22, E14 | Extensión con su predicado; conjunto validado antes de mutar; estado como global raíz | `Place.Contains`/`ContainsInset`; `Mission.Route` y `Golem.Entrust` validan todo antes de tocar nada; `g = Golem()` |

**Sobre E3 (forma de retorno) y `g.Places()` / `g.Evasion()` / `g.Suspect()`**: la regla dice que un retorno de consulta que **cruza el perform** debe ser de forma de cable (escalar, arreglo primitivo o proyección), nunca un portador de colección del dominio. No lo violamos: esos objetos **no cruzan** el perform, los recorre el script con `foreach` y lo único que cruza son los `print` escalares (verificado en el motor el 8-sep y en producción). La guía contempla justo esa distinción, "interno vs cruza el cable"; nuestra variante es el `foreach` sobre objetos del dominio dentro de la query.

**Observación — desviaciones, ordenadas por severidad**:

1. **E40/E16 — un tipo decidido parseando un string.** `Routes/Leg.cs:21`: `IsStop => Name != Detour && !Name.Contains('/') && !Name.Contains('~')`. La clase de tramo (puerta, frontera, rodeo, parada) es un conjunto cerrado escondido en el nombre, y `Mission.Cross`/`Reach` ramifican sobre él. La guía nombra esta forma como anti-patrón: "dentro del dominio se trafica en símbolos, nunca en strings; un string de tipo vive solo en el borde". Arreglo: variantes `DoorLeg`/`OpeningLeg`/`DetourLeg`/`StopLeg`. El texto del plan en el journal no cambia (es el programa; las variantes se reconstruyen al releerlo). Ya estaba propuesto en el PLAN.
2. **E01/E39 — `Mission` es una clase ancha.** `reason` vacío salvo en `failed`/`abandoned`; `Following` y `ChoosesOrder` como banderas. La guía pide separar definición, ocurrencia e identidad y usar subtipo solo donde hay comportamiento; el paper 01 pide variante por desenlace. Ya estaba propuesto.
3. **E41 — `Golem.PlaceAt(x, y)` lanza sobre una clave de rango denso.** La guía: rango denso → total (respuesta vacía); lanzar es para registros por id. Evidencia de que ya lo tratamos como guardado: el host lo envuelve en `if (g.IsOnMap(@x, @y))`. Arreglo barato: que la lectura del sujeto devuelva `""` fuera del mapa (el `PlaceAt` interno del plano sigue lanzando, que ahí sí protege al planificador).
4. **E17/E45 — un centinela sin nombre.** `Routes/RoutePlanner.cs:172`: `OpeningCrossed(...)?.Name ?? "?"`. Un tramo llamado `"?"` cuando no se puede decir qué frontera se cruzó: string mágico más camino de `null` interno. Arreglo: nombrarlo o hacer imposible el caso.
5. **E24/E25 — sin copias defensivas.** `FloorPlan.Places`/`Marks`, `Obstacle.Vertices()`, `Wall.Doors()`, `Trajectory.Legs()` entregan la lista de respaldo como `IReadOnlyList`. La guía pide clon defensivo, nunca el arreglo de respaldo. Riesgo real bajo (el DSL solo recorre), pero un llamador interno podría castear de vuelta.
6. **E9 — `Suspicion.Conclusion` devuelve el nombre de un verbo como string** ("Graze"/"Met"/"Mark"). Es una pista que el host puede ignorar; nada obliga la correspondencia. Alternativa: dejar solo `Kind` y que el host mapee, o hacer de la conclusión un conjunto cerrado.

**Excepción deliberada, no defecto**: E21 acota los comentarios en archivo al `///` de rol más notas breves propias del repo, y manda el resto (justificar una decisión, re-explicar doctrina) a la nota de diseño. Nuestros bloques `<para>Origins: …</para>` citan papers de robótica y explican en qué diferimos: eso excede el límite. **Los pidió Juan explícitamente** el 8-sep ("agrega notas en el código del origen de la técnica o el autor"), y su palabra es la autoridad 1 sobre la guía. Queda como excepción registrada.

**Conclusión**: el dominio respeta la guía en todo lo que es mecánicamente verificable (visibilidad, identidad sin igualdad, pureza, ausencia de repositorio, nulos fuera de la superficie, polaridad de lecturas, sellado, conjuntos cerrados) y en las reglas de diseño que la guía marca como columna (extensión con su predicado, validar-antes-de-mutar, valores recibidos, estado como global raíz, herencia con verdad de dominio). Las seis desviaciones son de dos clases: dos de deuda estructural que ya estaban en el PLAN (tramo y misión como variantes) y cuatro chicas y locales. Ninguna toca el journal salvo la 1 y la 2.

**Ajuste al dominio**: ninguno en esta entrada; es auditoría. Pendientes de decisión de Juan: (a) las cuatro chicas (3–6), que no rompen journals y se pueden hacer de una; (b) las dos estructurales (1–2), que sí renuevan journals.

---

## 2026-09-09 · Fases 1 a 3: la cola de posiciones, en el dominio y en el host

**Contexto**: Juan: "arranca primero con la parte de navegación y luego vemos mejor lo de colisiones". Se implementan las fases 1 (dominio), 2 (la bomba) y 3 (el host) del plan `PLAN-ruta-como-cola.md`. La cadena de colisiones se deja como está.

**Ajuste al dominio**:
- `MoveTo(id, …)` como encargo pasa a **`Visit(id, …)`**, tres sobrecargas, mismo cuerpo.
- Nace **`MoveTo(id, x, y)`**: la orden. Guarda: debe ser el punto que la cola tiene por delante; repetirla se permite (tras un toque el mismo punto puede volver a ordenarse), saltarse un punto no. Queda como **orden vigente** en la misión, que `Cross`/`Reach` limpian al cumplirse y `Route` anula al cambiar la cola.
- `Reach` **ya no exige camino**: un encargo a un punto a un solo segmento se camina derecho y la última parada completa igual.
- Lecturas nuevas: `HasNextPoint(id)` (la guarda de la bomba), `IsOrdered(id)` (¿hay orden vigente?), `NeedsRoad(id, x, y)` (¿hay camino que decidir, o es un solo segmento?), y `NextX(id)`/`NextY(id)` por misión — las sin argumento responden por la PRIMERA misión pendiente, que no es la misma cuando hay varias en cola.
- 47 tests (tres nuevos: la orden y su guarda, el encargo de un solo segmento, varias paradas sin camino).

**Ajuste al host**:
- **Una sola bomba**, no dos ni tres: los tres comandos que entregan un punto siguiente (`Route`, `Cross`, `Reach`) exponen la misma forma, `expose @id id, @nx nx, @ny ny`, y una única reacción `pump-order` la casa y escribe `g.MoveTo(@id, @nx, @ny)`. Mejor de lo planeado.
- El lazo **no conduce sin orden**: si no hay orden vigente, espera. Y solo decide `Route` cuando el dominio dice que hay camino que decidir.
- El encargo directo se resuelve en el controlador, que es quien tiene la pose: `g.Visit(...)` seguido de `if (g.NeedsRoad(...) == false) { … expose … }`. Si no hay pose, no expone y el lazo decide un `Route`: los dos caminos convergen.

**Observación en vivo** (journals archivados en `journal-legacy-2026-09-09-visit/`, los tres nacen en la entrada 1). Red de la sala al garaje, tres puntos, leído crudo del journal:

```
 5  g.Route(1, 'living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5');  + expose
 6..8  (define de la orden) g.MoveTo(1, 4, 1.5);        ← la bomba
 9  g.Cross(1, 'living/south'); If (g.HasNextPoint(1)) { … Expose … }
10  g.MoveTo(1, 7, 1.5);                                 ← la bomba
11  g.Cross(1, 'south/garage'); …
12  g.MoveTo(1, 9, 1.5);                                 ← la bomba
14  g.Reach(1, 9, 1.5); If (g.HasNextPoint(1)) { … }     ← cola agotada: no expone, no hay orden
```

Y el encargo de un solo segmento, red ya en el garaje enviado a otro punto del garaje:

```
27  g.Visit(2, 10, 2.4); If (g.NeedsRoad(2, 8.76, 1.5) == false) { … Expose … }
28  g.MoveTo(2, 10, 2.4);                                ← la bomba, sin Route
29  g.Reach(2, 10, 2.4);
```

`g.IsRouted(2)` responde `false`: **no hubo `Route`**, que era la objeción de Juan del día anterior. Y el último reporte de cada misión no dejó entrada inútil, como predijo la fase 0. Blue siguió a red las dos veces, chocó con la caja del hall en el camino, la marcó y red y green la aprendieron: **la cadena de colisiones sigue intacta** con el modelo nuevo.

**Conclusión**: la conducción entera se lee en el diario, y el host dejó de elegir a dónde ir: espera la orden, conduce, reporta. Lo que queda del lazo son las decisiones que aún no migran, todas de la cadena de colisiones (tanteo, cesión, reintento), que es justo lo que Juan dejó para después.

**Pendiente**: fase 4, las cadenas de `Bump` y `Graze` con la misma forma; el `docker kill` a media ruta para ver la bomba reanudarse en el mundo real; y las decisiones 2 y 3 del plan (`LearnMark`, renombrar `Next*` a `Order*`), sin urgencia.

---

## 2026-09-09 · Fase 4 y cierre del plan: las dos cadenas de la colisión también las decide el dominio

**Contexto**: Juan pide implementar el plan entero para poder ir a probar. Se completa la fase 4 (las cadenas de `Graze` y de la conclusión de un toque) y se toman las tres decisiones abiertas del plan.

**Ajuste al dominio**:
- **Un toque anula la orden vigente**: `Bump()` y `Graze()` ponen la orden en nada. El cuerpo no vuelve a conducir por su cuenta; espera a que el golem diga otra vez a dónde va. Es lo que cierra el círculo: sin esto el host seguía re-conduciendo la orden vieja después de chocar.
- Decisión 2 tomada: `Learn(x, y, heading)` → **`LearnMark(x, y, heading)`**. El par de uptakes queda `HearBump` / `LearnMark`: se oye un choque, se aprende una marca.
- Decisión 3 tomada: las lecturas que dicen a dónde va el cuerpo pasan de `Next*` a **`Order*`** (`OrderX`, `OrderY`, `OrderApproachX/Y`, `OrderExitX/Y`, `OrderIsStop`, `OrderPassage`), porque ya no significan "el siguiente tramo" sino "la orden que estoy cumpliendo". `NextId`, `NextHandle` y `HasNextPoint` se quedan: hablan de la misión y de la cola, no de la orden.
- 48 tests.

**Ajuste al host**:
- El comando `Graze` pregunta al dominio qué sigue: si hay paciencia, expone el punto y **la bomba devuelve la orden**; si se agotó, expone otra forma (`spent`, `why`) y una reacción nueva, `give-up-on-wall`, escribe el `Fail`. El host ya no cuenta reintentos ni decide el final: reporta y espera.
- Los comandos `Mark` y `Met` exponen el punto vigente, así que **la conclusión de un toque devuelve la orden**. Para eso los mensajes `ObstacleMarked` y `PeerMet` llevan ahora la misión.
- Se borró del host la lectura `MayRetryLeg`: la paciencia es asunto del dominio.

**Observación en vivo** (journals nuevos, los tres nacen en la entrada 1). Journal de red contra la pared este del garaje, leído crudo:

```
20  g.MoveTo(2, 11, 1.5);                                   ← la orden
22  g.Graze(2, 10.918…, 1.5); If (g.MayRetryLeg(2)) { … }
23  g.MoveTo(2, 11, 1.5);                                   ← la reacción devuelve la orden
24  g.Graze(2, 10.916…, 1.5); …
25  g.MoveTo(2, 11, 1.5);                                   ← otra vez
28  g.Graze(2, 10.903…, 1.5); …                             ← la tercera: no expone la orden
30  g.Fail(2, 'still grazing wall_garage_e at (10.9, 1.5): patience spent');   ← give-up-on-wall
```

Y el journal de blue chocando con la caja del hall, en el mismo modelo:

```
g.Bump(1, 5.027…, 5.839…, -1.73); Expose …
tell BumpedAt with 5.027…, 5.839…, 'blue' to red …
g.Mark(5.027…, 5.839…, -1.73); If (g.Knows(1) && g.HasNextPoint(1)) { … Expose … }
g.MoveTo(1, 6.19, 3);                                        ← la conclusión devuelve la orden
g.Cross(1, 'center~south'); …
g.MoveTo(1, 7, 1.5);
g.Route(1, 'garage@9,1.5'); …                                ← otra ruta tras el tanteo, y vuelve a bombear
g.MoveTo(1, 9, 1.5);
g.Reach(1, 9, 1.5);
```

Cierre: los tres golems con 3 marcas y una figura, nada pendiente, `Fail` por paciencia agotada escrito por una reacción, y ni un reintento decidido por el host.

**Conclusión**: el plan queda implementado salvo dos cosas que siguen siendo runtime del host y que el plan nunca prometió mover: el **tanteo** (paso lateral y avance) y la **cesión** ante un compañero con su tope de cuatro cesiones (`MaxYields`). Son maniobras del cuerpo, no decisiones de camino; migrarlas sería el mismo patrón (paciencia en el dominio, orden devuelta por reacción) y queda anotado como el siguiente candidato.

**Pendiente**: (1) `MaxYields` y el tanteo, si Juan quiere el mismo tratamiento; (2) el `docker kill` a media ruta, que es la única prueba de la fase 5 que falta.

---

## 2026-09-09 · El diario vuelve a ser actos: la orden la escribe el host (opción C)

**Contexto**: Juan lee su journal y pregunta por qué aparecen `If (g.NeedsRoad(…))` y `If (g.HasNextPoint(…))` dentro de los comandos. La respuesta honesta: es maquinaria. Una reacción solo usa lo que captura, y del reporte de llegada captura el punto ya alcanzado, no el siguiente; `expose` era el único canal, y la guarda evitaba una entrada vacía al final. El costo: una entrada que debería ser un acto quedaba con un aparato pegado atrás.

Se le presentaron tres formas: (A) la de entonces; (B) actos limpios con la maquinaria en la bomba y la orden sin coordenadas; (C) el host lee al dominio a dónde ir y escribe la orden, sin reacción. **Juan eligió C.**

**Ajuste**:
- Fuera el `expose` y el `if` de `Visit`, `Route`, `Cross`, `Reach`, `Mark`, `Met` y `Graze`. Los dos `expose` que quedan son los del `Bump`, que alimentan el habla con los compañeros: esos sí son su propósito.
- Fuera las dos reacciones de bombeo (`pump-order`, `give-up-on-wall`). Quedan las tres de habla (`echo-reached`, `echo-bumped`, `echo-marked`).
- Nace el mensaje `MissionOrdered` y su handler: el host **lee** `g.OrderX/OrderY` y journalea `g.MoveTo(id, x, y)`. Un solo punto del lazo lo hace, y por ahí pasan todas las cadenas: arranque, tras cruzar, tras llegar, tras un roce y tras un toque, porque `Bump` y `Graze` anulan la orden.
- El veredicto de la pared vuelve a leerse con `g.MayRetryLeg(id)`: el dominio decide, el host escribe el `Fail`.

**Observación en vivo** (journals nuevos). Encargo largo, red a la cocina:

```
 3  g.Visit(1, 'kitchen');
 5  g.Route(1, 'west/living@0.75,3 > kitchen/west@0.75,8 > kitchen@2,9.5');
 7  g.MoveTo(1, 0.75, 3);
 9  g.Cross(1, 'west/living');
10  g.MoveTo(1, 0.75, 8);
11  g.Cross(1, 'kitchen/west');
12  g.MoveTo(1, 2, 9.5);
14  g.Reach(1, 2, 9.5);
16  g.Announce(1); tell PointVisited …
```

Roce contra la pared oeste de la cocina:

```
87  g.MoveTo(3, 0.02, 9.5);
89  g.Graze(3, 0.099, 9.475);
90  g.MoveTo(3, 0.02, 9.5);      ← la orden, otra vez
91  g.Graze(3, 0.100, 9.476);
92  g.MoveTo(3, 0.02, 9.5);
93  g.Graze(3, 0.089, 9.480);    ← la tercera
95  g.Fail(3, 'still grazing wall_kitchen_w at (0.1, 9.5) after 3 grazes: patience spent');
```

Y el encargo a un punto del mismo cuarto arrancó con `Visit` y `MoveTo`, sin `Route` (entrada 45). Susto propio: vi después un `Route` de un solo punto para esa misión y lo tomé por defecto; era correcto — venía de ceder el paso a blue, y quien se aparta de su línea decide de nuevo desde donde quedó.

**Conclusión**: cada línea del diario es un acto. Lo que se pierde frente al diseño de la mañana es que la secuencia ya no la lleva una reacción sino el lazo del host, que **no decide**: lee el punto que el dominio nombra y lo escribe. El principio de Juan se sostiene, la maquinaria sale del diario, y el modelo es más simple (dos reacciones menos).

**También**: nace `Golem.sln` con los tres proyectos (`golemdomain`, `golemdomain.tests`, `golemhost`), así `dotnet build Golem.sln` y `dotnet test Golem.sln` cubren todo de una vez. 48 tests.

---

## 2026-09-09 · El rodeo también se journalea: la marca dispara el recálculo, no un baile a ciegas

**Contexto**: Juan mira el diario tras un choque y nota que el cuerpo se mueve a la derecha sin que ninguna línea lo explique: "quién lo está manejando, no veo los comandos de MoveTo". Y dice qué esperaría: "un `g.Bump`, luego el tell, y luego otro `MoveTo`, como si se agregó una coordenada de emergencia o como un recálculo para evitar el obstáculo".

**Observación (el defecto)**: cuatro trozos del host llamaban al navegador **directo**, sin journalear: el paso lateral del tanteo (0.5 m), el avance en el carril nuevo (1.2 m), el retroceso al ceder (0.9 m) y el hacerse a un lado (0.8 m). Las distancias y los topes eran constantes del host. Y el dato que lo delataba: el dominio **ya tenía** la maniobra modelada (`Maneuver`, `EvasionStrategy`, la lectura `g.Evasion`) y el host **no la usaba ni una vez**. Con la conducción normal ya contada en el diario, el contraste era evidente: rehidratando ese journal no se podía reconstruir por dónde anduvo el cuerpo.

**Ajuste (host, ninguna firma del dominio cambia; journals compatibles)**: no hacía falta un verbo nuevo. El planificador **ya** mete la coordenada de emergencia — los tramos `around@x,y` que rodean una marca — solo que el host no lo dejaba recalcular hasta haber tanteado a ciegas los dos lados. Se quitó ese gatillo:
- Una marca nueva **recalcula el camino de inmediato** (`reRoute` cuando hay toque y marcas nuevas, sin esperar al tanteo).
- El tanteo por tacto pasa a ser **el último recurso**, y solo donde corresponde: cuando el planificador dice que ningún camino cabe. Si el tacto encuentra hueco, el golem decide otra vez desde donde quedó; si no, la misión falla como antes.

**Verificado en vivo** (journals nuevos, red al hall norte cruzando el centro donde está la caja):

```
 3  g.Visit(1, 'north');
 5  g.Route(1, 'living/south@4,1.5 > center~south@4.5,3 > north~center@5.5,8 > north@5.5,9.5');
 7  g.MoveTo(1, 4, 1.5);
 9  g.Cross(1, 'living/south');
10  g.MoveTo(1, 4.5, 3);
11  g.Cross(1, 'center~south');
12  g.MoveTo(1, 5.5, 8);
14  g.Bump(1, 4.943, 5.266, 1.372);          ← toqué algo
16  tell BumpedAt … to blue      (+ acks de blue y green)
20  g.Mark(4.943, 5.266, 1.372);             ← era una cosa
22  tell ObstacleFound …         (+ acks)
25  g.Route(1, 'north~center@5.26,8 > north@5.5,9.5');    ← el RECÁLCULO
26  g.MoveTo(1, 5.26, 8);                    ← la coordenada nueva, ya evitando la marca
27  g.Bump(1, 4.961, 5.853, 1.320);          ← la caja es más ancha que un paso: otra vez
31  g.Mark(4.961, 5.853, 1.320);
…
84  g.MoveTo(1, 5.5, 9.5);
86  g.Reach(1, 5.5, 9.5);                    ← llegó, rodeando por el corredor este
```

Cinco toques, cinco marcas, cinco recálculos, y la misión completada. Los tres golems terminan con las cinco marcas y **una figura**; blue y green la aprendieron sin golpearse.

**Conclusión**: la cadena que Juan esperaba es la que ahora ocurre, y no hizo falta inventar nada — bastó dejar que el planificador hiciera su trabajo en el momento en que aprende algo nuevo. El diario explica cada metro que recorre el cuerpo. Lo que queda sin journalear es solo el tacto del último recurso y las cortesías entre cuerpos (retroceder, orillarse), que son maniobras del cuerpo y no decisiones de camino; el candidato natural es que usen `g.Evasion` y se escriban como órdenes.

**Pendiente**: (1) que el tanteo y las cortesías pasen por `g.Evasion` y se journaleen; (2) el `docker kill` a media ruta.

**Aparte, un incidente de herramientas**: al abrir `Golem.sln`, Visual Studio 18 reescribió la solución y **le borró al proyecto de tests la referencia al dominio**, con lo que la solución dejó de compilar. Se restauró la referencia. Vigilar si vuelve a pasar al reabrir.

---

## 2026-09-09 · El encuentro entre cuerpos: comparten posición y cada uno se aparta, journaleado

**Contexto**: Juan, sobre la rama del `Met`: "cuando hace `g.Bump` y resulta ser otro robot, necesito que se compartan su posición actual para poder hacer otro `g.MoveTo` a la par, para que eviten golpearse entre ellos cuando retomen su camino". Era la última cortesía que el host hacía a escondidas: retroceder 0.9 y hacerse 0.8 a un lado, sin escribir nada.

**Ajuste al dominio**:
- `HeardBump` guarda además **dónde estaba el compañero** cuando lo contó (`PeerAt`), y el verbo pasa a `HearBump(who, x, y, px, py)`: el punto del toque y la posición de quien lo cuenta. Firma nueva → journals renacen.
- `Leg` aprende un nombre más: **`aside`** (`Leg.Courtesy`), el tramo que se aparta de un cuerpo. No es parada, así que cumplirlo no completa el encargo: se reporta con `Cross(id, 'aside')`.
- Lectura nueva **`PlanPast(id, who, x, y, heading)`**: el camino para pasar a ese compañero — el paso de cortesía primero y después el encargo. El paso es de un ancho de cuerpo hacia **su propia derecha** (y si no cabe, a la izquierda), elegido para **alejarse** de donde el compañero dijo estar y donde el propio cuerpo quepa (`Fits`). Que los dos vayan a su derecha es lo que hace que se cruzen en vez de empujarse. Si no hay lado que sirva, devuelve el camino normal y el encuentro se resuelve esperando.
- 50 tests (dos nuevos: el camino que se aparta con su geometría exacta, y el caso sin lado disponible).

**Ajuste al host**: el toque expone también la pose, el tell lleva cinco valores y el uptake es `g.HearBump(@who, @x, @y, @px, @py)`. Al concluir `Met`, el golem **decide un camino** con `PlanPast` y lo journalea como `Route`; el paso se ordena y se reporta como cualquier tramo. Se borraron `CoordinateWithAsync` y `BackAwayAsync`: la cesión silenciosa ya no existe.

**Observación en vivo** (journals nuevos; blue y red cruzándose en la cocina). Diario de blue:

```
g.Bump(6, 3.069, 10.233, 0.548); Expose …
tell BumpedAt with 3.069, 10.233, 'blue', 2.895, 10.137 to red     ← con MI posición
g.HearBump('red', 3.632, 10.523, 3.426, 10.382);                   ← red dice dónde está
g.Met('red', 3.069, 10.233);                                       ← era red
g.Route(6, 'aside@3.12,9.69 > kitchen@3.5,10.5');                  ← el recálculo que se aparta
g.MoveTo(6, 3.12, 9.69);                                           ← el paso, ordenado
g.Cross(6, 'aside');                                               ← cumplido
g.MoveTo(6, 3.5, 10.5);                                            ← retoma su camino
g.Reach(6, 3.5, 10.5);                                             ← llega
```

Y en el diario de red, la otra mitad: `HearBump('blue', …, 2.895, 10.137)` y su propio `Bump` contado con su posición.

**Defecto encontrado y corregido en el mismo laboratorio**: la primera corrida journaleó el `Met` pero **no** el camino de cortesía. Causa: la clave de idempotencia del `Route` se armaba con el conteo de toques leído al principio del turno, ya viejo, así que coincidía con la del primer `Route` y el motor la descartó **en silencio** (regla 7a del motor, otra vez). Ahora la clave lleva un conteo fresco y el número de encuentro. Lección repetida: una clave repetida no falla, desaparece.

**Conclusión**: la cadena que Juan pidió está viva y entera en el diario — toqué, lo conté con mi posición, oí la suya, era un cuerpo, decido apartarme, me aparto, retomo. Ya no hay metros que el diario no explique, salvo dos cortesías que quedan: el seguidor orillándose al llegar y el cuerpo parado que se hace a un lado cuando lo tocan. Ambas son del mismo patrón y se cierran igual.

**Pendiente**: (1) esas dos cortesías; (2) el `docker kill` a media ruta.

---

## 2026-09-09 · El módulo de obstáculos, aparte del módulo del mapa

**Contexto**: Juan pide dos cosas encadenadas. Primero: "algo como `g.Obstacles()` y que retorne la lista de cuáles son, y cada obstáculo con cada vértice… que si uno hace GET al controller, en la UI salga como una tabla, y así poder ver un segundo elemento de otro obstáculo pero con otros vértices en otra zona". Después, el fondo: "dentro de `class Golem` la idea es ir creando el módulo de obstáculos, el módulo, y así cada uno de cada uno, para encontrar la ruta más corta entre todos los puntos acorde al módulo del mapa".

**Observación (el defecto)**: los obstáculos solo se alcanzaban lugar por lugar (`places.Obstacles()`), y peor: `FloorPlan` guardaba a la vez **el plano** (lo que al golem le contaron: lugares, puertas, paredes) y **lo aprendido** (las marcas de los toques y los encuentros). Dos cosas de naturaleza distinta en una sola clase: el plano no cambia, lo aprendido cambia con cada golpe. Es la porosidad estructural que la guía llama aplanar dos colaboradores en uno (E26).

**Ajuste al dominio**:
- Nace **`Plans.ObstacleMap`**, el módulo de obstáculos: guarda los **hechos** (marcas con su normal, encuentros) y **deriva** las hipótesis (`All()`, `Things()`, `In(place)`), responde si algo aprendido estorba (`Blocks`) y se apoya en el plano solo para nombrar la zona. Sus constantes viajan con él: `MarkReach`, `MarkMargin`, `JoinWithin`, `SameTouch`.
- **`FloorPlan` queda como el módulo del mapa**, y solo eso: lugares, pasajes, paredes, `HasRoom` (lo que las paredes dejan), `IsWallAt`, `ZoneOf`, `WithDoorCrossings`, `AsRelease`. Ya no sabe qué se tocó. Su margen propio se llama `BodyMargin`.
- **`Golem` sostiene los dos módulos** y los compone: `FitsAt` es "las paredes dejan espacio Y lo aprendido no estorba", y el planificador se construye con ambos — `new RoutePlanner(plan, learned, radius)` —, que es literalmente lo que Juan pidió: la ruta más corta se busca consultando el módulo del mapa y el de obstáculos.
- **`Place` deja de responder por lo tocado**: se retiraron `Place.Marks()` y `Place.Obstacles()`. Lo aprendido se lee plano y con su zona.
- Lectura nueva **`g.Obstacles()`**: todos los obstáculos, cada uno con `Kind`, `Where` (la zona), `Shape`, `Size`, `Who`, `Center` y `Vertices()`, y cada vértice con su normal y su alcance. `Obstacle.Where` es propiedad de la variante, la rellena el módulo al derivarla.
- 51 tests: tres se reescribieron para leer plano en vez de por lugar, y uno nuevo comprueba la tabla entera (dos cosas en zonas distintas con sus vértices, y el peer sin ninguno).

**Ajuste al host**: `GET /obstacles` con una sola query que recorre `g.Obstacles()` — una fila por obstáculo y una por vértice. El panel tiene su **tabla** y, de la misma lista, pinta las figuras rojas del minimapa; `/map` volvió a ser solo el plano.

**Verificado en vivo**, tabla del panel de red:

```
thing · line     | kitchen | 2 vertexes | centre (0.87, 8.58)
  • vertex       |         | (0.96, 8.57) | normal -2.32 rad · reach 0.25
  • vertex       |         | (0.79, 8.58) | normal  1.45 rad · reach 0.25
thing · polygon  | center  | 5 vertexes | centre (5.58, 5.74)
  • vertex ×5    |         | …           | cada uno con su normal
peer · blue      | kitchen | 1 vertex   | centre (0.75, 8.54)
```

**Aclaración que quedó fijada (Juan preguntó si `Bump` alimenta el módulo)**: no. `Bump` es el hecho del toque — cuenta, anula la orden y expone el punto para el habla —, y quienes alimentan el módulo son `Mark` (mi conclusión) y `LearnMark` (la de un compañero). Es deliberado: en el instante del toque no se sabe si fue un robot, y si `Bump` marcara, cada encuentro dejaría un obstáculo fantasma permanente en los tres mapas — exactamente el error del 8-sep, tres marcas fantasma en una puerta. Lo que sí es cierto, y ahora explícito en el código, es que **el módulo guía al robot entero**: el planificador lo consulta en cada camino. La cadena es `Bump` → `Mark`/`LearnMark` → módulo de obstáculos → planificador.

**Pendiente**: (1) las dos cortesías que faltan por journalear (el seguidor orillándose, el parado que se hace a un lado); (2) el `docker kill` a media ruta; (3) cuando lleguen las capas del mapa, el módulo de obstáculos ya es una de ellas — la capa de lo aprendido sobre la capa de lo contado.

---

## 2026-09-09 · Olvidar un obstáculo: lo quitaron, y con él se van todos sus vértices

**Contexto**: Juan: "quiero que los Obstacles me permitan deshabilitar/eliminar uno con todos sus vértices asociados, porque ya lo quitaron el obstáculo y ahora ya puede pasar; los que estaban marcados ahora ya no están disponibles; si vuelve a chocar con algo eso ya es otro Obstacle nuevo".

**Ajuste al dominio**:
- `ObstacleMap.Forget(at)`: encuentra el obstáculo que ese punto nombra — el más cercano por su centro **o por cualquiera de sus vértices**, dentro de `JoinWithin` — y suelta **todos** sus hechos de una vez: las marcas que lo dibujaban, porque eran vértices de una sola cosa y la cosa ya no está. Si lo que hay ahí es un encuentro con un compañero, suelta ese encuentro. Devuelve cuántos hechos soltó; cero cuando no hay nada, que no es un rechazo.
- `ObstacleMap.KnowsAt(at)`: la lectura que se consulta antes de decir que ya no está.
- Verbos del golem: **`Forget(x, y)`** (el operador dice que lo quitaron) y **`LearnForget(x, y)`** (un compañero me lo contó), más la lectura `KnowsObstacleAt(x, y)`. El par queda simétrico con `Mark`/`LearnMark`.
- **El mapa olvida, el journal no**: el acto de olvidar se journalea como cualquier otro, así que la historia sigue diciendo qué se creyó y cuándo se dejó de creer. Es la misma regla de `Abandon`.
- Lo nuevo que pasa gratis: un toque posterior en ese sitio forma **otro** obstáculo, con marcas nuevas, porque los grupos se derivan de los hechos que quedan.
- 53 tests (dos nuevos: olvidar una cosa con sus tres vértices y que un toque después sea otra cosa; olvidar un encuentro sin tocar las cosas).

**Ajuste al host**: `POST /forget?x=&y=` con `Check(g.KnowsObstacleAt(...))` antes del comando — 409 si no hay nada ahí, 400 sin coordenadas, las dos verificadas en vivo. Reacción **`echo-forgotten`** sobre `Forget` → `tell ObstacleGone` a cada compañero → uptake `g.LearnForget`. Sin eso la flota quedaría inconsistente: uno pasando por donde otro sigue rodeando. Y en el panel, cada fila de obstáculo tiene su botón **gone**.

**Aparte, la UI**: la sección de obstáculos rompía la rejilla de dos columnas del panel — se metía en la segunda celda y empujaba la consola a la columna angosta de 300 px. La tabla vive ahora dentro de la columna ancha; la rejilla se centra (`margin:0 auto`, 1280 px), la barra lateral pasó a 340 px y las dos explicaciones largas se pliegan (`<details>`), así ninguna columna es un muro de texto de 1400 px. Tres tropiezos propios en el camino, todos de escritura de archivos y ninguno del diseño: un ancla de CSS que no existía, un escape `B8` que Python leyó como octal y dejó un carácter de control en el archivo, y un `
` que se partió dentro de un literal de C#. Lección: verificar el archivo escrito, no solo que el script no falle.

**Pendiente**: (1) que el olvido caduque solo (una marca vieja que nadie confirma), si algún día hace falta; (2) las dos cortesías sin journalear; (3) el `docker kill` a media ruta.

---

## 2026-09-10 · Los proyectos se llaman como sus carpetas: GolemAPI, GolemDomain, GolemTest

**Contexto**: Juan: "vamos a renombrar los proyectos: para golemhost que se llame GolemAPI, el otro GolemDomain, GolemTest; las carpetas de los proyectos".

**Ajuste**: carpetas y `.csproj` renombrados a la vez, para que proyecto, carpeta y assembly digan lo mismo: `golemhost/GolemHost.csproj` → `GolemAPI/GolemAPI.csproj`, `golemdomain/Golem.Domain.csproj` → `GolemDomain/GolemDomain.csproj`, `golemdomain.tests/Golem.Domain.Tests.csproj` → `GolemTest/GolemTest.csproj`. Con ellos: `Golem.sln`, las dos `ProjectReference`, el `InternalsVisibleTo` (ahora `GolemTest`, el nombre del assembly que ve los internals), el `nuget.config` de los tests (apunta al `localfeed` de `GolemAPI`), el `Dockerfile` (publica `GolemAPI.csproj`, arranca `GolemAPI.dll`), `docker-compose.yml` (imagen `golemapi`), `launchSettings.json`, y la documentación viva (`CLAUDE.md`, `README.md`, `PLAN-Golem.md`). Este cuaderno se deja como está: es historia, y en su momento las carpetas se llamaban así. Windows no distingue mayúsculas, así que `golemdomain` → `GolemDomain` se hizo en dos pasos con `git mv` por un nombre temporal.

**Lo que NO cambió, a propósito**: los namespaces (`GolemHost.Domain`, `GolemHost.Choreography`, …). Renombrarlos es otra decisión, con más superficie (todos los `using`, la clase pública `GolemDomain` chocaría con un namespace del mismo nombre), y Juan pidió proyectos y carpetas. Queda anotado como opción, no como deuda.

**Observación**: `dotnet test Golem.sln` → 53 en verde en `GolemTest.dll`. Desplegado con la imagen nueva: los tres paneles responden y los journals rehidrataron intactos con el assembly renombrado (blue en la entrada 82, red con sus 5 marcas). **Conclusión**: el motor liga las clases por nombre simple, no por assembly — el journal no sabe cómo se llama el `.dll` que lo interpreta, que es exactamente lo que el paper 09 (*identity precedes staging*) pide del dominio.

---

## 2026-09-10 · Los namespaces también: GolemDomain.* y GolemAPI.*

**Contexto**: Juan, tras el renombre de proyectos: "renombra también los namespaces a GolemAPI y GolemDomain".

**Ajuste**: `GolemHost.Domain` → `GolemDomain` (y `.Geometry`, `.Plans`, `.Robots`, `.Routes` debajo), `GolemHost.Domain.Tests` → `GolemTest`, `GolemHost` y `GolemHost.{Choreography, Controllers, Membrane, Navigation, Panel}` → `GolemAPI.*`. `RootNamespace` del dominio a `GolemDomain`. Documentación viva (`CLAUDE.md`, `PLAN-Golem.md`) al día.

**El choque previsto y su salida**: la clase pública `GolemDomain` (el asa del assembly para el actor) quedaría dentro de un namespace con su mismo nombre, y en C# eso hace que `GolemDomain.Assembly` se resuelva contra el namespace y no contra la clase. Pasa a llamarse **`DomainLibrary`** (`DomainLibrary.Assembly`), en `GolemDomain/DomainLibrary.cs`; es lo único que el host y los tests toman del assembly, tres usos.

**Observación**: 53 tests en verde; desplegado, los tres paneles responden y los journals rehidrataron intactos (red con sus 5 marcas). **Conclusión**: el journal tampoco sabe de namespaces — el DSL escribe `Golem()`, `[_:Golem]`, y el motor liga por nombre simple. Renombrar proyectos, assemblies y namespaces no tocó una sola entrada: la identidad del dominio está en sus verbos, no en su montaje (paper 09).

---

## 2026-09-10 · Lo que el DSL permite: objetos, globales, capturas (estudio previo al plan de objetos en el journal)

**Contexto**: Juan propone dos cosas: (1) que el journal cuente la historia con objetos — "a partir de primitivos vamos a ir creando objetos; se guarda en una variable y esa variable ya se pasa por parámetro al otro; que lo que se escriba sea verboso, que se expanda" —, y (2) que los módulos (el plano, el mapa de obstáculos, el cuerpo) puedan ser globales del actor, armados aparte y brindados al golem, "para poder reusar esas clases fuera del contexto del golem algún día". Antes de planear, se revisó qué permite el motor: las 25 guías del training-lab de Skills y, donde callan, los laboratorios de `repos/puppeteer`.

**Observación** (nueve hallazgos; la evidencia con archivo y línea está en `PLAN-objetos-en-el-journal.md`):
1. Un objeto construido en el DSL puede guardarse en una variable y pasarse como argumento, también en línea. Lo prueba un laboratorio del motor, no una guía.
2. Puede haber varias globales; las asignaciones al nivel superior de un `upgrade` **o de un comando** son globales del actor; dentro de `{ }` son locales.
3. Un objeto puede ir a un constructor (`Order(…, address, …)`); los constructores `internal` valen.
4. El cuerpo de una acción admite varias sentencias, pero sus **parámetros son solo primitivos**: ningún tipo del dominio puede aparecer en la cabecera. El objeto se construye dentro de la plantilla.
5. No hay literales de lista de objetos (tipan `List<object>` y no ligan). Las cadenas fluidas sí, incluso sobre un constructor.
6. Se puede guardar lo que devuelve un método y usarlo en el mismo comando; el journal guarda el texto de la llamada, nunca el valor.
7. **Las reacciones no capturan objetos**: una captura solo cae sobre un literal, un `@param` o una etiqueta de `expose`; sobre una variable lanza `PatternCaptureException`. Los `tell` llevan solo primitivos.
8. Los literales en comandos están permitidos pero cada uno es una identidad de acción; los `upgrade` son los portadores sancionados de literales.
9. Varias raíces globales existen y rehidratan (laboratorio `AssembledVerbOverTwoDomainsLab`, dos dominios); la guía de modelado prefiere una raíz que compone por dentro (E14, E26) y pide pocas globales.

**Conclusión**: la idea 1 es viable tal como Juan la describe y encaja con el paper 02 (los valores siguen entrando por `@params`; la plantilla construye el objeto). La idea 2 es mecánicamente segura y tiene respaldo directo en el paper 0A y en el laboratorio de dos dominios del motor, pero va contra la preferencia de la guía de modelado: es una decisión de Juan que hay que dejar documentada como tal. El riesgo real del plan es el hallazgo 7: los verbos que las reacciones capturan (`Mark`, `Bump`, `Forget`…) podrían tener que quedarse con primitivos, o capturarse en la construcción del objeto, cosa que ninguna guía muestra y hay que probar. Dos avisos más van al laboratorio: el «cross-PerformCmd global-read bug» de `actor-basics:341` y la rehidratación PlainText de un `define action` multilínea (`parameters:286`; nosotros usamos FileSystem).

**Ajuste al dominio**: ninguno todavía. Nace `PLAN-objetos-en-el-journal.md` con el ejemplo del journal de mañana, el diccionario de verbos, seis fases (la 0 es el laboratorio con seis pruebas P1–P6 en `touchlab`) y seis decisiones para Juan. Lección de método que repetimos: **primero qué permite el motor, después el plan**; sin la revisión habríamos planificado capturas de objetos que el motor rechaza en tiempo de autoría.

---

## 2026-09-10 · El mapa dispone, la geometría realiza (ajuste del plan de objetos con las observaciones de Juan)

**Contexto**: Juan revisó el plan y devolvió cuatro cosas: (1) los `.md` de planes se quedan locales, fuera de git; (2) `body` como global suena bien; (3) `plan` no: "algo como un módulo de mapas, y el mapa que le vamos a cargar será el mapa de bodega con todas esas distribuciones"; y una crítica de POO: "las herencias no hacen frente a estructuras sólidas, más bien parece que lo amarraste a un plano cartesiano; la idea es generalizar hasta que sean cosas que no tengan que ver con planos cartesianos o segmentos: otro módulo toma esas propiedades y ya lo trabaja como plano cartesiano; este módulo solo dispone la información para que la ocupen otros"; (4) `learned` no es nombre para un módulo que interpreta o construye colisiones.

**Observación (diagnóstico de las herencias, 10-sep-2026)**: los ocho archivos de `Plans` importan `Geometry` y hablan en coordenadas. `Wall : Segment` dice que una pared ES un segmento; `Mark : Location : Position` que una marca ES una posición; `Door : Passage` carga `Position at`, `Jambs()`, `StepInto()`; `OpenBoundary` carga `Edge`, `Midpoint`, `IsCrossedBy()`; `Place` ES un rectángulo (`X, Y, Width, Height, Corners(), Walls()`); `Obstacle` ES una figura (`Center`, `Vertices()`). La crítica es exacta: el mapa y su realización cartesiana están fundidos, y la herencia es el pegamento. Las herencias que sí son verdad del dominio (paper 01, variantes por resultado) son otras y se quedan: `Door`/`Opening : Passage`, `Thing`/`Peer : Obstacle`, las `Suspicion`, las `EvasionStrategy`, `DistanceCost : EdgeCost`.

**Conclusión**: la separación que Juan pide ya tenía nombre en nuestras propias notas — la tradición topológico/métrica de Kuipers & Byun citada en `FloorPlan` — pero la habíamos anotado como "capas futuras del mismo mapa" en vez de construirla como dos módulos. Mañana: **Maps** dispone información (áreas, pasajes, límites, atributos; ni una coordenada), **Layouts** la realiza en el plano (rectángulos, posiciones de puertas, y de ahí paredes, esquinas, aristas), **Collisions** (el `ObstacleMap` de hoy, con el nombre de lo que interpreta) mide sobre la distribución, **Routes** consulta distribución y colisiones, y `Golem` recibe `body`, `layout`, `collisions`. Lo cartesiano se compone desde su capa; ya no se hereda.

**Ajuste al plan** (`PLAN-objetos-en-el-journal.md`, local): tres ideas en vez de dos (la tercera: los módulos por capas); una sección de diagnóstico con la tabla de herencias; la tabla de módulos con namespaces y globales (`body`, `map`, `layout`, `collisions`, `g`); el journal de mañana con dos releases para el mapa (`warehouse_v1`: información; `warehouse_layout_v1`: distribución); una **Fase 1 nueva y solo de C#** — el refactor por capas con `Golem` todavía fabricando sus módulos por dentro, para que los 53 tests sigan siendo el arnés sin tocar el DSL — antes de mover nada al journal; y siete decisiones para Juan (nombres, qué recibe el golem, `Area` vs `Place`, rectángulo vs polígono…). **Ajuste al dominio**: ninguno todavía.

**Método**: la doctrina del repo dice "los .md de planes son locales": `PLAN-objetos-en-el-journal.md` queda excluido con `.git/info/exclude` (exclusión local, no toca el repo). El cuaderno y `PLAN-Golem.md` ya estaban versionados y no se decidió sobre ellos: se dejan como están hasta que Juan diga.

---

## 2026-09-10 · Laboratorio Fase 0 del plan de objetos: siete preguntas al motor (scratch `objlab`)

**Contexto**: Juan aprobó el plan ("arranca, procura hacer todas las fases juntas"). Antes de tocar el repo, las pruebas P1–P6 del plan más una P7 que salió al correrlas. Dominio de juguete con la forma de mañana: `Body`, `Map`/`Area`/`Passage` (información), `Layout` (realización cartesiana, recibe el mapa), `Collisions` (recibe la distribución), `Planner` (se construye al consultar), `Robot(body, layout, collisions)`.

**Observación**:
| Prueba | Resultado |
|---|---|
| **P1** cinco globales en cuatro releases, objetos en constructores (`Layout(map)`, `Collisions(layout)`, `Robot(body, layout, collisions)`), cada módulo leído desde un `PerformQuery` aparte, y un segundo actor que rehidrata el mismo journal | **Verde.** El «cross-PerformCmd global-read bug» de la guía no apareció. |
| **P2a** `stop = Position(@x, @y); robot.Visit(@id, stop);` al nivel superior de una plantilla | `stop` **se filtra como global**: se lee después con `stop.Text()`. |
| **P2b** lo mismo entre llaves `{ … }` | **Local**: después no existe. La plantilla sigue siendo una acción válida. |
| **P2c** constructor en línea `robot.Visit(@id, Position(@x, @y))` | Verde. |
| **P3a** capturar en la construcción `[_:Pose]($x, $y, $heading)` | **Rechazado al definir** la reacción (sintaxis del patrón). |
| **P3b** capturar con constructor en línea `[_:Robot].Mark(Pose($x, $y, $heading))` | **Rechazado al definir**. |
| **P3c** `expose @x x, @y y, @heading heading;` junto al acto | **Verde**: la reacción captura y concluye. |
| **P3d** capturar la variable `[_:Robot].Mark($touch)` | La reacción **nunca dispara**; en el log, `PatternCaptureException: the observed argument is the global/local variable 'touch', not a literal … nor a parameter`. |
| **P4a** varios actos en un comando (`Route` + dos `Via`) | Verde como comando. Con dos `expose` en el mismo comando la reacción no capturó: un `expose` por comando, o ninguno. |
| **P4b** una llamada sobre una global como argumento: `robot.Cross(@id, map.DoorBetween(kitchen, north))` | **Verde**: el pasaje llega como objeto. |
| **P5** FileSystem rehidrata una plantilla de varias sentencias | Verde (tres actos, segundo actor, historia intacta). |
| **P6** consulta que construye `Planner(layout, collisions, body.Radius)` y calcula sin `robot`; `map.AreaCount()`; `layout.AreaAt(layout.Door(kitchen, north))` | Verde. La asignación de una consulta **no** se vuelve global. |
| **P7** literales enteros en parámetros `double` de un constructor: `Position(5, 9)` | **Rechazado** (`InvalidCastException Int32→Double`) en comandos y en consultas; `Position(5.0, 9.0)` pasa. En el `upgrade` sí pasó (`Position(0, 8)` dentro de `layout.Rectangle(…)`). |

**Conclusión y decisiones que fijan las fases**:
1. Los módulos como globales y el golem que los recibe por constructor: **viable y rehidrata**. Fase 2 va como está en el plan.
2. La forma de una plantilla que construye un objeto: **entre llaves** `{ stop = Position(x, y); g.Visit(id, stop); }` — es la verbosidad paso a paso que pide Juan sin dejar globales huérfanas en la raíz (P2a las deja). El constructor en línea queda para argumentos simples.
3. **Lo que se cuenta viaja plano.** Un acto que una reacción debe capturar para contárselo a los compañeros (`Bump`, `Mark`, `Reach`, `Forget`) conserva sus `@params` primitivos, porque el matcher solo captura literales, `@params` o etiquetas de `expose` (P3), y el cable solo lleva primitivos (guía). La alternativa, un `expose` junto a cada acto, mete en el journal una línea que no es un acto — lo mismo que Juan rechazó con los `If (…)` el 9-sep. La familia de toques se queda en primitivos completa (`Bump`, `Graze`, `Mark`, `LearnMark`, `Met`, `HearBump`, `Forget`, `LearnForget`) para que se lea uniforme; `Reach` también, porque se cuenta. El resto de la navegación (`Visit`, `Cover`, `Follow`, `MoveTo`, `Route`/`Via`, `Cross`) pasa a objetos.
4. Un pasaje del mapa puede viajar como objeto (`map.Door(a, b)`) en un argumento (P4b): `Cross` y `Via` lo usan.
5. El catálogo emite los literales de constructores **con punto decimal** (P7); el host nunca escribe literales: siempre `@params` tipados.

**Ajuste al dominio**: empieza la Fase 1 (capas: `Maps` dispone, `Layouts` realiza, `Collisions` interpreta), en C# puro, con los 53 tests como arnés.

---

## 2026-09-10 · Fase 1: el mapa dispone, la distribución realiza, las colisiones interpretan (C# puro, 53 tests como arnés)

**Ajuste al dominio**: muere `Plans`. Nacen **`Maps`** (`Map`, `Area`, `Passage` → `Door`/`Opening`: información pura, con los atributos que el mapa conoce — `DoorWidth`, `Height`, `WallThickness` — y ni una coordenada), **`Layouts`** (`Layout(map)`: un `Zone` por área = `Area` + `Rectangle`; `PlacedDoor` = `Door` + `Position`; `Wall` que TIENE un `Segment`; `Doorway`/`OpenSide` como vistas; el `Catalog` con los planos con nombre) y **`Touches`** (`Collisions(layout)`: marcas, encuentros y bumps oídos, con `Suspect`, `HeardNear`, `Forget`; `Mark` que TIENE un `Position`; `Obstacle`, `Suspicion`, `HeardBump`). `Geometry` gana `Rectangle`. `RoutePlanner(layout, collisions, radius)` consulta los dos módulos y no posee ninguno. `Golem` seguía fabricando sus módulos por dentro y `Chart/DoorTo/OpenTo` vivían de forma transitoria en `Zone`, para que los 53 tests corrieran con el mismo DSL. **Observación**: 53 en verde a la primera compilación salvo tres textos de mensajes ("area 'attic' is not laid out", "no layout named", "neither an area nor a point"). El refactor de POO se validó solo, sin tocar el journal. El namespace de las colisiones se llama `Touches` porque una clase no puede llamarse como su namespace (el mismo choque de `DomainLibrary`).

---

## 2026-09-10 · Fases 2–6: el journal habla en objetos, y los módulos son globales

**Ajuste al dominio**: `Body(radius, speed, linger)` es un objeto (mueren `Embody/Cruise/Linger`); `Golem(body, layout, collisions)` recibe sus módulos (muere `Golem()` y muere `Chart`); `Visit(id, Position)`/`Visit(id, area)`/`Cover(…)` abren un encargo con un handle nuevo o le añaden una parada con el suyo (`Mission.AddStop`, antes de decidir el camino); `Follow(Position)`; `MoveTo(id, Position)`; la decisión es una secuencia de actos: `Route(id)` abre el borrador, `Via(id, pasaje, Position)`, `Around`, `Aside` y `Stop` lo llenan, y al último `Stop` (tantos como paradas por delante) el golem toma el camino con sus cruces de puerta; `Cross(id, Passage)` recibe el objeto del mapa (`map.DoorBetween(a, b)` / `map.OpeningBetween(a, b)`); nace **`Pass(id, Position)`** para los puntos de rodeo y de cortesía, que no son pasajes ni paradas (no estaba en el diccionario del plan: se anota aquí y en el PLAN). Lecturas nuevas `Road(id, x, y)` y `RoadPast(…)` devuelven la `Trajectory` como objetos; `Plan`/`PlanPast` siguen dando la línea de texto para humanos y tests. Muere `Trajectory.Parse`. `Layout.AsReleases()` emite los dos releases del mapa con literales con punto decimal. El catálogo renombra `arena` → **`warehouse`** ("el mapa de bodega").

**Ajuste a los tests**: los helpers traducen — `Route(id, planText)` parte la línea de `g.Plan` y escribe los actos; `Cross(id, "a/b")` escribe `map.DoorBetween`; `Pass(id, plan, "around")` toma el punto del texto. 53 en verde. Un hallazgo del motor: **un constructor que rechaza llega al DSL como `Error while instantiating class 'Body'`; la razón del dominio se pierde** en el envoltorio (`TargetInvocationException`). Los tests de `Body` comprueban la razón en C# y en el DSL solo que se rechazó.

**Ajuste al host**: `RoadLeg` (record: kind, a, b, x, y) lleva la decisión desde la lectura `g.Road(…)` (una consulta que recorre `Legs()` e imprime `Kind`, `A`, `B`, `At.X`, `At.Y`) hasta el handler que escribe `g.Route(@id); g.Via(@id, map.DoorBetween(@a0, @b0), Position(@x0, @y0)); … g.Stop(@id, Position(@xN, @yN));` en un solo comando; `PassageCrossed` viaja con el punto y el handler elige `Cross` (puerta/abertura) o `Pass` (punto); `MoveTo(@id, Position(@x, @y))`; el uptake `Follow(Position(@x, @y))`; el controller escribe un acto por parada (`g.Visit(@id, @p0); g.Visit(@id, Position(@x1, @y1));`) con un `Check` por parada; `/map` recorre `layout.Zones` y `/obstacles` recorre `collisions.All()` — los módulos, sin el golem; el panel gana botones `map.AreaCount`, `layout.ZoneOf(Position(5.5, 5.5))`, `collisions.MarkCount`, `body.Radius`. Los releases del host llevan el texto de `Catalog.Warehouse().AsReleases()`.

**Laboratorio en vivo** (journals archivados en `journal-legacy-20260910-objetos/`; flota nueva; blue a `kitchen` y `9,8`, red a `living`). El journal de blue, tal cual:
```
1  upgrade('body_v1') { body = Body(0.25,2.0,6.0); } upgrade('warehouse_v1') { map = Map('warehouse'); map.Area('kitchen'); … }
3  g.Visit(1, 'kitchen'); g.Visit(1, Position(9,8));
5  g.Route(1); g.Via(1, map.DoorBetween('kitchen', 'north'), Position(4,9.5)); g.Stop(1, Position(2,9.5)); g.Via(1, map.DoorBetween('kitchen', 'north'), Position(4,9.5)); …
7  g.MoveTo(1, Position(4,9.5));
9  g.Follow(Position(2,1.5));
11 g.Cross(1, map.DoorBetween('kitchen', 'north'));
14 g.Reach(1, 2, 9.5);
34 g.Cross(2, map.OpeningBetween('north', 'center'));
37 g.Bump(2, 5.2076, 5.8825, -1.714); Expose …           ← lo que se cuenta, plano
43 g.Mark(5.2076, 5.8825, -1.714);
49 g.Route(2); g.Around(2, Position(4.5276,5.8825)); g.Via(2, map.OpeningBetween('center', 'south'), Position(4.5752,3)); …
52 g.Pass(2, Position(4.5276,5.8825));
58 g.Reach(2, 2, 1.5);
```
Y red: `g.Visit(1, 'living'); g.MoveTo(1, Position(2,1.5)); g.Reach(1, 2, 1.5); … g.HearBump('blue', …); g.LearnMark(…)`. Tres actores, tres journals con cuatro releases y cinco globales cada uno, rehidratando; `/obstacles` de red muestra la marca que aprendió de blue (una cosa en `center`); `/map` dice `"map":"warehouse"`.

**Observaciones**: (1) la misión 1 de blue falló con "still grazing wall_storage_s_1 at (8.9, 8.1)": la parada `9,8` que pedí está SOBRE la pared sur de `storage` (y = 8) — un encargo mal dado, no una regresión; el golem lo trató como siempre (tres roces, paciencia agotada, `Fail`). (2) Blue chocó de verdad con la caja del centro en (5.21, 5.88), la marcó, se lo contó a red y green, y recalculó con un `Around` y un `Pass`. (3) Cada `define action` nuevo tiene los parámetros por posición (`x0, y0, a0, b0, …`): una ruta de N tramos es una acción por forma, como anticipaba el hallazgo 8.

**Conclusión**: el journal se lee como Juan lo pidió — objetos construidos a partir de primitivos, cada acto por separado, los módulos como globales que el golem recibe —, sin una sola línea que no sea un acto, y sin que el motor haya dicho no: los límites (captura, coerción, constructor envuelto) se conocieron en Fase 0 y el lenguaje se diseñó alrededor.

---

## 2026-09-10 · La distribución hereda del mapa; el objeto se busca una vez y se le habla en tren

**Contexto**: Juan, al ver el release del mapa: "la clase de mapas es la maqueta; la de layout es la otra clase que hereda de mapa, para que tome todos los métodos y el funcionamiento de la padre pero a su vez lo ponga en perspectiva de usar posiciones… el POO tiene que hablar por sí solo, como si los objetos hablaran de lo que son capaces… buscar el objeto con un find para modificar sus propiedades, o un builder en tren, en vez de estar seteando a cada rato el identificador tipo 'kitchen'".

**Observación (lo que estaba mal)**: `Layout` TENÍA un `Map` (composición) y sus métodos repetían el nombre del área en cada llamada (`layout.Rectangle('kitchen', …); layout.DoorAt('kitchen', 'north', …)`). Es correcto pero no habla: una distribución no "tiene" una maqueta, ES la maqueta con posiciones. Y el identificador repetido es ruido en el journal.

**Ajuste al dominio**: **`Layout : Map`** (`Map` deja de ser sealed; `areas`/`passages` protegidos; fábrica virtual `NewArea`) y **`Zone : Area`**. `Layout(map)` copia áreas y pasajes de la maqueta y sus áreas nacen como zonas sin rectángulo; `Layout('name')` también sirve para construir desde cero. `Find(name)` en ambos (`override Zone Find` con retorno covariante). Trenes: en el área `DoorTo(other)`/`OpenTo(other)` (virtuales, la zona los sobreescribe para que el tren siga siendo suyo); en la zona `At(Position)`, `Size(w, h)`, `DoorAt(other, Position)`. El rectángulo de la zona es mutable hasta que se le dice (`IsLaidOut`); las lecturas geométricas lo exigen. Las vistas de la zona pasan a `Doorways()`/`OpenSides()` porque `Doors()` es la lista llana heredada del área (dos `Doors()` en la misma clase serían un `AmbiguousMatch` para el motor). `RoutePlanner` y `Golem` ya no pasan por `layout.Map`: la distribución responde como mapa. El catálogo se escribe con los mismos trenes. `AsReleases()` emite una línea por área en cada release.

**Observación (motor)**: el DSL liga por el tipo en tiempo de ejecución: `layout.Find('kitchen').Width` responde 4.0 (la zona), y `map.Find('kitchen').Neighbours().Count` responde 2 (el área de la maqueta). El retorno covariante de C# 9 no hizo falta para el DSL, pero sí para que el catálogo en C# encadene sin casts.

**Verificado**: 53 tests; flota redesplegada (journals archivados en `journal-legacy-20260910-objetos-b/`, porque un release aplicado no se edita); blue completó `kitchen` → `garage` con el journal de siempre, y la entrada 1 dice ahora:
```
upgrade('warehouse_v1') { map = Map('warehouse'); map.Area('kitchen').DoorTo('north').DoorTo('west'); map.Area('north').DoorTo('storage').OpenTo('center'); … }
upgrade('warehouse_layout_v1') { layout = Layout(map); layout.Find('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0)); … }
```

---

## 2026-09-10 · `Map` abstracta, `MapLayout` concreta: una sola clase construye todo

**Contexto**: Juan, sobre la herencia recién hecha: "lo que está mal es que ahora solo sería una clase la que vamos a usar, la de layout, para crear todo; la abstracta es la de mapa y la clase concreta ya sería la del MapLayout: las mismas formas para crear áreas, pero extiende las funciones para definirles dimensiones y otras cosas, sobre si un área conecta con otra área o no necesariamente están alineadas".

**Observación (lo que aún no calzaba)**: tenía dos objetos vivos, `map` (la maqueta) y `layout = Layout(map)` (una copia con posiciones), y por tanto dos releases y dos globales para una sola cosa. La herencia estaba, pero la maqueta seguía existiendo como instancia aparte.

**Ajuste al dominio**: **`Map` es abstracta** — el contrato de lo que un mapa dispone: áreas, pasajes, `Connects(a, b)` (información: hay un pasaje entre dos áreas, estén como estén en el plano), atributos; `NewArea` abstracto. **`MapLayout : Map` es la única clase concreta**: las mismas formas de crear áreas y pasajes, extendidas con dimensiones y posiciones (`Zone : Area` con `At`, `Size`, `DoorAt`; `Touches(a, b)`: dos áreas comparten arista en el plano — pueden conectar sin tocarse, si no están alineadas). Desaparecen `Layout(Map)`, el release `warehouse_layout_v1` y la global `layout`. El release del mapa es uno, un tren por área. El golem recibe `map`: `g = Golem(body, map, collisions)`. El catálogo construye con la misma clase y `AsRelease()` renderiza el tren.

**Método (lección propia)**: el renombre `Layout → MapLayout` con una expresión regular sobre diez archivos convirtió también las propiedades llamadas `Layout` en accesos estáticos, y un script de parches encadenado abortaba a la mitad y dejaba duplicados al reintentar. Costó cuatro vueltas. Regla: un renombre de clase se hace con el compilador como juez, archivo por archivo, y cada script de parche debe ser idempotente o no se reintenta.

**Verificado**: 53 tests (el del catálogo comprueba el tren completo, `Connects` vs `Touches`, `map.Find('kitchen').Width`); flota redesplegada (journals en `journal-legacy-20260910-objetos-c/`); blue completó `kitchen` → `garage`. Entrada 1 del journal:
```
upgrade('body_v1') { body = Body(0.25, 2.0, 6.0); }
upgrade('warehouse_v1') { map = MapLayout('warehouse'); map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0)); … }
upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }
```
Y desde el panel, con el mapa solo: `map.Connects('north', 'center')` → true, `map.Touches('north', 'center')` → true, `map.Connects('kitchen', 'garage')` → false, `map.Find('kitchen').Neighbours().Count` → 2, `map.ZoneOf(Position(5.5, 5.5))` → center.

---

## 2026-09-10 · Los métodos reciben instancias; el nombre entra solo al crear o al buscar

**Contexto**: Juan: "la gran mayoría de métodos debería pasar el objeto entero en vez de pasar el string del nombre del área o del mapa… evitemos usar parámetros con tipos primitivos si tenemos las instancias reales; casi que los que sí podrían buscar con primitivos son los find, los métodos create y los constructores".

**Observación (lo que había)**: `Connects(string, string)`, `Neighbours(string)`, `DoorBetween(string, string)`, `Touches(string, string)`, `StepInto(Door, string side)`, `g.Visit(id, string area)`, `g.Distance(string, string)`; `Passage` con `Joins(string)` y `OtherSide(string) → string`. Nombres viajando como identidad por todas partes, aunque el objeto ya existiera.

**Ajuste al dominio**: regla — un `string` entra solo al **crear** (`Area(name)`, `DoorTo(name)`, `OpenTo(name)`, `DoorAt(name, Position)`, `Door(a, b)`/`Open(a, b)` por nombre porque el vecino puede no existir aún; constructores) o al **buscar** (`Find(name)`, `Knows(name)`, `FindDoor(a, b)`, `FindOpening(a, b)`, `FindPassage(name)`, `KnowsPassage(name)`). Todo lo demás recibe objetos: `Map.PassagesOf/DoorsOf/OpeningsOf/Neighbours/Connects/HasDoorBetween/DoorBetween/OpeningBetween(Area…)`, `Neighbours` devuelve áreas; `Area.DoorTo(Area)/OpenTo(Area)/Connects(Area)`; `Passage` guarda los nombres con que se declaró y entrega `AreaA`, `AreaB`, `Joins(Area)`, `OtherSide(Area) → Area` (el `Map` que la creó resuelve); `MapLayout.IsLaidOut(Area)`, `Touches(Area, Area)`, `Of(Area) → Zone`, `DoorAt(Area, Area, Position)`, `Placed(Door)`, `StepInto(Door, Area)`, `ZoneOf(Position) → Zone` (null si ninguna), `ZonesOf(Position) → zonas`; `Zone.DoorAt(Area, Position)`, `Doorways()` con `Across` como área; `PlacedDoor.A/B` como áreas, `StepInto(Area)`; `RoutePlanner.Node` guarda zonas, no nombres; `Golem.Visit(id, Area)`, `Cover(id, Area)`, `Distance(Area, Area)`, `PlaceAt(x, y) → Zone`. Las sobrecargas por nombre se conservan solo donde crean.

**Ajuste al host y a los tests**: `g.Visit(@id, map.Find(@area))` con `Check(map.Knows(@area))`; `g.Cross(@id, map.FindDoor(@a, @b))` / `map.FindOpening`; `RoadLeg` escribe `Via` con `FindDoor`/`FindOpening`; el panel pregunta `g.Distance(map.Find('kitchen'), map.Find('garage'))`, `map.Connects(map.Find('north'), map.Find('center'))`, `map.ZoneOf(Position(5.5, 5.5)).Name`; `/progress` imprime `g.PlaceAt(@x, @y).Name`.

**Verificado**: 53 tests; flota redesplegada (journals en `journal-legacy-20260910-objetos-d/`); blue completó `kitchen` → `garage`. El journal:
```
3:  g.Visit(1, map.Find('kitchen')); g.Visit(1, map.Find('garage'));
5:  g.Route(1); g.Via(1, map.FindDoor('kitchen', 'north'), Position(4,9.5)); g.Stop(1, Position(2,9.5)); …
9:  g.Cross(1, map.FindDoor('kitchen', 'north'));
17: g.Cross(1, map.FindOpening('north', 'center'));
```
Y desde el panel: `map.Touches(map.Find('kitchen'), map.Find('north'))` → true; `map.PointOf(map.FindDoor('kitchen', 'north')).X` → 4.0; `g.Distance(map.Find('kitchen'), map.Find('garage'))` → 12.87.

**Conclusión**: el POO habla: se busca una vez y se le pregunta al objeto. El motor liga por tipo en tiempo de ejecución, así que una expresión que devuelve un objeto (`map.Find(…)`, `map.FindDoor(…)`) sirve como argumento de cualquier verbo — es lo que P4b había mostrado en el laboratorio.

---

## 2026-09-10 · Plantillas entre llaves, paso a paso, con `@parameter` en cada valor

**Contexto**: Juan pregunta si un comando puede tener saltos de línea — `area = map.Find('kitchen'); g.Visit(1, area);` — y, ante la respuesta (sí; y entre llaves para que el nombre intermedio no quede como global), decide: "entre llaves me parece correcto… ojo, siempre ponle parámetros con @parameter a donde deben de ir acorde a las guías".

**Ajuste al host y a los tests**: todas las plantillas que construyen o buscan objetos pasan a un bloque entre llaves: `{ point = Position(@x, @y); g.MoveTo(@id, point); }`, `{ door = map.FindDoor(@a, @b); g.Cross(@id, door); }`, `{ opening = map.FindOpening(@a, @b); g.Cross(@id, opening); }`, `{ point = Position(@x, @y); g.Pass(@id, point); }`, `{ point = Position(@x, @y); g.Follow(point); }`; el encargo `{ stop0 = map.Find(@p0); g.Visit(@id, stop0); stop1 = Position(@x1, @y1); g.Visit(@id, stop1); }`; la ruta `{ g.Route(@id); door0 = map.FindDoor(@a0, @b0); at0 = Position(@x0, @y0); g.Via(@id, door0, at0); stop1 = Position(@x1, @y1); g.Stop(@id, stop1); … }`. Los valores siempre son `@params`; los nombres intermedios dicen qué es el objeto (`door`, `opening`, `point`, `stop`, `around`, `aside`, `at`).

**Observación (en vivo, blue de `kitchen` a `garage`, journals en `journal-legacy-20260910-objetos-e/`)**:
```
2: define action 1 (id:int, p0:string, p1:string) as { stop0 = map.Find(p0); g.Visit(id, stop0); stop1 = map.Find(p1); g.Visit(id, stop1); } end;
3: { stop0 = map.Find('kitchen'); g.Visit(1, stop0); stop1 = map.Find('garage'); g.Visit(1, stop1); }
5: { g.Route(1); door0 = map.FindDoor('kitchen', 'north'); at0 = Position(4,9.5); g.Via(1, door0, at0); stop1 = Position(2,9.5); g.Stop(1, stop1); … }
7: { point = Position(4,9.5); g.MoveTo(1, point); }
9: { door = map.FindDoor('kitchen', 'north'); g.Cross(1, door); }
17: { opening = map.FindOpening('north', 'center'); g.Cross(1, opening); }
32: { g.Route(1); around0 = Position(6.48,5.88); g.Around(1, around0); opening1 = map.FindOpening('center', 'south'); … }
35: { point = Position(6.48,5.88); g.Pass(1, point); }
```
Blue chocó otra vez con la caja del centro (5.80, 5.88), la marcó, se lo contó y recalculó con un rodeo — el protocolo intacto bajo la forma nueva. Y ninguna variable intermedia quedó en la raíz: `stop0`, `point`, `door0`, `area` responden "has not been defined" desde el panel — la promesa de P2b, cumplida en producción. El motor guarda la plantilla normalizada en una línea; los saltos de línea del host son para quien la escribe.

**Conclusión**: el journal cuenta la historia paso a paso — busca, nombra, actúa — sin dejar huella en la raíz y sin un literal fuera de sus `@params`. Regla anotada en `CLAUDE.md`.

---

## 2026-09-10 · Los nombres intermedios: `point`, o `point1`, `point2`… cuando son varios

**Contexto**: Juan, sobre `{ stop0 = Position(5.6,1.2); g.Visit(1, stop0); }`: "podríamos dejarlo como… no ponerle ese numeral si solo es uno; si son más de uno pues sí tocaría ponerle 1, 2 y así… más bien usar `point`".

**Ajuste al host y a los tests**: el encargo nombra `point` a su única parada y `point1`, `point2`… cuando hay varias, y los `@params` siguen la misma cuenta (`@area`, `@x`/`@y` o `@area1`, `@x2`/`@y2`); la ruta numera sus tramos desde 1 (`door1`, `at1`, `stop2`, `around3`…). `point` es también la orden (`MoveTo`), el paso (`Pass`) y el punto seguido (`Follow`).

**Verificado en vivo** (journals en `journal-legacy-20260910-objetos-f/`; red a `garage`, blue a `kitchen` y `9,8.5`):
```
red   3: { point = map.Find('garage'); g.Visit(1, point); }
      5: { g.Route(1); door1 = map.FindDoor('living', 'south'); at1 = Position(4,1.5); g.Via(1, door1, at1); door2 = map.FindDoor('south', 'garage'); at2 = Position(7,1.5); … }
      7: { point = Position(4,1.5); g.MoveTo(1, point); }
      9: { door = map.FindDoor('living', 'south'); g.Cross(1, door); }
blue  3: { point1 = map.Find('kitchen'); g.Visit(1, point1); point2 = Position(9,8.5); g.Visit(1, point2); }
      5: { g.Route(1); door1 = map.FindDoor('kitchen', 'north'); at1 = Position(4,9.5); g.Via(1, door1, at1); stop2 = Position(2,9.5); g.Stop(1, stop2); … }
     15: { point = Position(9,1.5); g.Follow(point); }
```
Las dos misiones completaron; 53 tests.

---

## 2026-09-10 · El plan entero en una entrada; el journal calla mientras se camina

**Contexto**: Juan: "la idea es justo no crear demasiados rows en el journal; con un solo [comando] podemos tener toda la ruta y seguir ese plan de punto, pero no estar diciéndole cada cosa que va haciendo el robot; solo si hizo bump, ese sí interrumpe el flujo de todos los puntos que mandó el primer comando con el plan entero". Aprobó los cinco puntos propuestos: `Reach` por parada se queda; el avance vive en el host mientras ejecuta; reiniciar a media ruta decide la ruta de nuevo; mueren las lecturas de la orden; `Graze` interrumpe igual que `Bump`.

**Ajuste al dominio**: `Mission.Reach(x, y)` acepta cualquier parada por delante y avanza el cursor hasta ella (los tramos anteriores se caminaron); `MayRoute` mientras esté pendiente (una ruta nueva reemplaza lo que quedaba); mueren `MoveTo`, `Cross`, `Pass`, la orden y sus lecturas. Nacen `Golem.Preview(fromX, fromY, xs[], ys[], cover)` (la ruta de un encargo que aún no existe — arrays de primitivos como parámetros, que el motor sí acepta), `RoadAhead(id)` (los tramos por delante con su aproximación y su salida), `IsStopAhead(id, x, y)` (la guarda de `Reach`), `HeadingTo/HeadsToAStop/HeadingX/HeadingY` (lecturas del panel) y `PlannedEndX/Y` (dónde termina el último plan pendiente: el punto de partida de un encargo encolado).

**Ajuste al host**: el controller pregunta `g.Preview` y escribe encargo y plan en una entrada; el lazo toma el plan con `RoadAhead`, lo recorre en memoria tramo a tramo (aproximación y salida en cada puerta), y escribe solo `Reach` (guarda `IsStopAhead`), `Bump`/`Graze` y, tras ellos, otra ruta. Al despertar con una misión pendiente decide la ruta de nuevo desde su pose, una vez. Mueren los mensajes `MissionOrdered` y `PassageCrossed`. Los `@params` de los tramos se llaman `la`, `lb`, `lx`, `ly` para no chocar con los de las paradas.

**Observación en vivo** (journals en `journal-legacy-20260910-plan/`; red a `garage`, blue a `kitchen` y `garage` con `storage` encolada detrás):
```
red  3: { point = map.Find('garage'); g.Visit(1, point); g.Route(1); door1 = map.FindDoor('living', 'south'); at1 = Position(4,1.5); g.Via(1, door1, at1); door2 = … }
     7: g.Reach(1, 9, 1.5);
blue 3: { point1 = map.Find('kitchen'); g.Visit(1, point1); point2 = map.Find('garage'); g.Visit(1, point2); g.Route(1); door1 = map.FindDoor('kitchen', 'north'); … }
     5: { point = map.Find('storage'); g.Visit(2, point); g.Route(2); door1 = map.FindDoor('east', 'garage'); at1 = Position(10.25,3); … }   ← planeada desde el garage, donde acabará la misión 1
     9: g.Reach(1, 2, 9.5);
    13: g.Bump(1, 5.80, 5.88, -1.43);   19: g.Mark(…);
    25: { g.Route(1); around1 = Position(6.48,5.88); g.Around(1, around1); opening2 = map.FindOpening('center', 'south'); … }
    28: g.Bump(1, 8.51, 1.50, …);  33: g.Met('red', …);  35: { g.Route(1); aside1 = Position(8.25,1.0); g.Aside(1, aside1); stop2 = Position(9,1.5); g.Stop(1, stop2); }
    43: g.Reach(1, 9, 1.5);
    53: { g.Route(2); … }   54: g.Reach(2, 9, 9.5);
```
Ni un `MoveTo`, `Cross` o `Pass`. Cuatro misiones completadas entre los dos, una de ellas encolada; un choque con la caja del centro recalculado con `Around`; un encuentro con red en el garage resuelto con `Met` y `Aside` (dos veces); ninguna variable intermedia en la raíz (`point`, `point1`, `door1`, `at1`, `stop2`: "has not been defined"). El único defecto: la regla del despertar disparó una ruta redundante en la primera misión tras el arranque (`woke` valía true desde el boot); corregido: solo despierta con plan en curso si ya había una misión pendiente al arrancar.

**Conclusión**: de unas cuarenta filas por misión a cuatro o cinco: la decisión entera, las paradas cumplidas, y los toques con su nueva decisión. La doctrina se sostiene: el dominio decide el plan y lo escribe completo; el host lo obedece en silencio y reporta lo que lo cumple o lo interrumpe. Lo que se perdió, a sabiendas: el journal ya no dice en qué tramo iba el cuerpo entre dos paradas — al despertar, el golem decide la ruta de nuevo desde donde está.

---

## 2026-09-10 · La palanca de cajas: botones en la ventana del kiosko

**Contexto**: Juan pide botones en la ventana de noVNC para probar mejor sobre el simulador: una caja al oeste, otra al centro, otra al este, una que limpia, y —al ver que funcionaba— una quinta caja **grande** que bloquee del todo el hall central. Las cajas deben aparecer en tiempo real, sin reconstruir la imagen.

**Observación**:
1. **El servidor de noVNC no ejecuta nada.** `websockify --web=/usr/lib/novnc 80 localhost:5902` sirve archivos estáticos y el websocket de la pantalla: un botón en esa página necesita a alguien que reciba la orden. Descartados el menú de openbox (es clic derecho, no botones) y una barra flotante con `yad` (pelearía el z-order con la ventana de Gazebo, que nace maximizada).
2. **Gazebo Fortress acepta modelos en caliente.** Los servicios del mundo existen en la corrida: `/world/arena/create`, `/remove`, `/set_pose`. Probados en el contenedor vivo: `create` con un SDF en línea devolvió `data: true` y `ign model --list` mostró el modelo; `remove` por nombre lo quitó.
3. **Gotcha del formato**: el SDF viaja dentro de una cadena del text-format de protobuf, así que **una comilla doble la cierra**. Con `<?xml version="1.0"?>` la petición falló (`Expected identifier, got: 1.0`); con los atributos XML entre **comillas simples** (`<sdf version='1.6'>`) y sin encabezado XML, pasa limpio.
4. **Los servicios se llaman como `ubuntu`**, no como root: el mismo gotcha de memoria compartida de Fast DDS anotado el 3-sep. El servidor corre dentro de la sesión del kiosko, que ya es de ese usuario.
5. **Verificado en vivo (tres puntos)**: cada botón pone su caja y el de limpiar las quita (`{"placed": ["center","east","west"]}` → `{"placed": []}`, cero modelos `crate_*`). La página pinta en ámbar los botones cuyas cajas están puestas, y el estado se pregunta **al mundo** (`ign model --list`), no a una variable del servidor: dice la verdad aunque el servidor se reinicie.
6. **La prueba que cerró el círculo**: caja en el hall central y red enviado a la cocina cruzándolo. Chocó en (5.2, 5.0), el dominio sospechó una cosa, la marcó con su normal, la contó a los peers, decidió **otro camino** con dos tramos de rodeo y llegó a la parada. La caja puesta con un botón produce el escenario que antes exigía reconstruir la imagen.
7. **La caja grande** (pedido de Juan, sin volver a correr robots): 2.8 × 0.7 centrada en (5.5, 5.5). El hall va de x=4.0 a 7.0, así que la caja ocupa de 4.10 a 6.90 y deja **0.10 m por lado**; el cuerpo mide 0.5 de ancho, de modo que no pasa por ninguno de los dos costados: el atajo del centro queda cerrado. Es la única grande; se pinta más oscura. **Se excluye con la caja chica del centro** (comparten el punto: poner una quita la otra, para que no queden una dentro de la otra). Verificada por servicio y a la vista: `{"placed": ["big"]}`, modelo `crate_big`, y el hall atravesado de pared a pared en la imagen. Ningún robot se movió para esta prueba.

**Conclusión**: el obstáculo deja de ser propiedad del build y pasa a ser palanca del laboratorio, que es lo que hacía falta para provocar el protocolo de toques a voluntad. La página del kiosko es ahora la de la palanca (:6081) con la imagen de Gazebo embebida; la imagen desnuda sigue en :6080. Las cajas viven solo en el mundo corriendo: al recrear el contenedor, el piso vuelve a estar limpio (el plano ya no trae ninguna).

**Ajuste al dominio**: **ninguno, a propósito.** Esto es infraestructura del operador (paper 06: síntoma infraestructural, no dominio): el golem no sabe que existen los botones, y las cajas siguen siendo realidad que su mapa no contempla. Archivos: `sim/bridge/crates.py` (servidor de la palanca y su página, tabla `SPOTS` de cuatro puntos), `sim/kiosk/kiosk.sh` (lo arranca junto al teleport, también en modo headless), `sim/Dockerfile` (lo copia), `docker-compose.yml` (publica :6081), `sim/world/plan.json` (la caja fija SALE: `obstacles: []`, para que cada prueba empiece con el piso limpio y el botón del centro tenga dónde poner la suya).

**Pendiente**: (1) el contenedor corriendo lleva la versión de cuatro puntos aplicada en caliente (`docker cp` + reinicio de ese solo proceso, para no reiniciar el mundo mientras Juan trabajaba); **la imagen se queda con tres hasta el próximo `docker compose up -d --build`**, que la hornea; (2) los journals vivos conservan marcas de la caja fija que ya no existe — creencia obsoleta, se vio en que red planificó un rodeo antes de chocar; limpiarlos cuando Juan quiera; (3) si hacen falta más puntos, `SPOTS` crece sola: el nombre del punto es lo único que viaja en la petición, nunca una coordenada; (4) un botón que EMPUJE una caja sería `set_pose` sobre el mismo nombre, si alguna vez se quiere el obstáculo móvil.

---

## 2026-09-10 · Los toques reciben objetos; lo que se cuenta viaja al lado como `expose`; el panel oculta los tells

**Contexto**: Juan, mirando el panel: "el g.Bump aún no maneja posición, recibe los primitivos; hay que corregirlo, y el g.Mark también por lo que veo". Antes: "ponme un checkbox para omitir los tells del Journal — live", y "mueve la parte del PerformQuery, lo que más me importa ver es la tabla de journal live".

**Ajuste al dominio**: la familia de toques recibe sus objetos: `Bump(id, Pose)`, `Bump(Pose)`, `Graze(id, Position)`, `Mark(Pose)`, `LearnMark(Pose)`, `Met(who, Position)`, `HearBump(who, Position, Position)`, `Forget(Position)`, `LearnForget(Position)` y `Reach(id, Position)`. La regla "lo que se cuenta viaja plano" se reformula: el acto lleva el objeto, y lo que la reacción debe capturar para contárselo a los compañeros va **al lado** como `expose` — la forma P3c del laboratorio — con etiquetas propias por acto para que dos reacciones nunca casen la misma forma: `Bump` ya exponía `x, y, who, px, py`; `Mark` expone `mx, my, mh`; `Reach`, `rid, rx, ry`; `Forget`, `gx, gy`. Las reacciones `echo-marked`, `echo-reached` y `echo-forgotten` pasan de casar `[_:Golem].Verbo($…)` a casar el `expose`.

**Observación en vivo** (journals en `journal-legacy-20260910-toques/`):
```
blue 11: { touch = Pose(8.5046,1.4970,-0.0014); g.Bump(1, touch); } Expose 8.5046 '8.5046'; …
     13: tell BumpedAt with 8.5046, 1.4970, 'blue', 8.3160, 1.4972 to red once …      ← la reacción capturó el expose
     16: { at = Position(8.5046,1.4970); g.Met('red', at); }
     20: { point = Position(9,1.5); g.Reach(1, point); } Expose 1 'rid'; Expose 9 'rx'; Expose 1.5 'ry';
red   5: { point = Position(9,1.5); g.Reach(1, point); } Expose 1 'rid'; …
      7: g.Announce(1); tell PointVisited with 9, 1.5 to blue once …                   ← echo-reached capturó el expose
     16: { at = Position(8.5046,1.4970); peer = Position(8.3160,1.4972); g.HearBump('blue', at, peer); }
```
El motor imprime los `expose` como `Expose <valor> '<valor>'` en la fila: el valor y su etiqueta. No hubo `Mark` en esta corrida (los dos choques fueron encuentros); su forma es la misma que la de `Reach`, verificada.

**Ajuste al panel**: casilla «hide tells» junto al título del journal en vivo: oculta las filas cuyo script empieza por `tell` (tells y acks) y las plantillas `define action … as tell …`; preferencia guardada por navegador; el journal no se toca. Verificado: 29 filas, 7 ocultas. La consola `PerformQuery` bajó al final; el journal en vivo subió al primer lugar de la columna ancha, con más alto. Un tropiezo de método: el regex se escribió con `\b` desde Python y llegó al archivo como un carácter de retroceso invisible (`grep` no lo muestra); el navegador lo delató (`"tell\b"` en la fuente servida). Lección: verificar con `cat -A` lo que se escribe por script.

**Pendiente (pregunta abierta de Juan)**: "el bump y mark deberían ir en el mismo script". El protocolo de encuentro lo impide tal cual: el `Bump` se cuenta de inmediato para que el compañero que chocó a la vez lo oiga en su ventana de escucha; la conclusión (`Mark` o `Met`) llega después de escuchar. Si los dos demoraran su `Bump` hasta concluir, ninguno oiría al otro y ambos marcarían fantasmas (8-sep). Alternativas propuestas: dejar dos filas (hecho y conclusión), o marcar de inmediato en la misma fila y retractar con un verbo nuevo cuando un compañero hable.

---

## 2026-09-10 · La palanca, en el puerto de siempre, y el botón que devuelve la vista

**Contexto**: Juan no veía los botones porque miraba `:6080/vnc.html`, su URL de costumbre, y la palanca vivía en `:6081`. Pide dos cosas: que los botones salgan también en 6080, y un botón más que **recupere la vista de arriba** cuando la cámara se quedó girada.

**Observación**:
1. **Una sola página, dos puertos.** La página pasa a ser un archivo del repo (`sim/kiosk/kiosk.html`), y la imagen la deja en dos sitios: `/golem/kiosk.html`, que sirve la palanca en 6081, y `/usr/lib/novnc/index.html`, con lo que `http://localhost:6080` ya muestra los botones. El visor desnudo sigue en `/vnc.html`. La página calcula los dos destinos desde `location.hostname`, así que sirve igual por localhost o por una dirección de la red.
2. **Gotcha que costó**: en la imagen base, `/usr/lib/novnc/index.html` **es un symlink a `vnc.html`**. Copiar la página encima escribió A TRAVÉS del enlace y reemplazó el visor: la página quedó dentro de un iframe de sí misma. Reparado a mano (el visor volvió de una copia) y en el Dockerfile: primero `rm -f index.html`, después copiar. Anotado ahí mismo para que nadie lo repita.
3. **El GUI de Gazebo tiene servicios de cámara**: `/gui/move_to/pose` (`ignition.msgs.GUICamera`) y `/gui/view_angle` (`Vector3d`). Se eligió el primero porque **restituye la vista exacta del kiosko**, no una parecida: el botón lee `<camera_pose>` del propio `arena.sdf` (hoy `5.5 5.5 7.26 0 1.5707 1.5707`) y convierte esos roll-pitch-yaw a cuaternión (0 π/2 π/2 → x -0.5, y 0.5, z 0.5, w 0.5). Al leerla del mundo, un piso de otro tamaño recupera su propia vista sin tocar código.
4. **Verificado en vivo**, con la cámara inclinada a propósito antes de cada prueba: el botón la devuelve arriba desde 6081 (mismo origen) y desde 6080 (origen cruzado), y el aviso de la página dice "the camera is back above the floor". Los encabezados CORS se comprobaron con `curl` mandando `Origin: http://localhost:6080`: `Access-Control-Allow-Origin: *`.
5. **Un falso negativo que conviene recordar**: la primera pulsación desde 6080 dio `TypeError: Failed to fetch`. No era CORS: el contenedor se había recreado segundos antes (Juan levantó el compose) y la palanca aún arrancaba. Repetida con todo asentado, pasa. **Antes de culpar al diseño, mirar la hora de arranque del contenedor.**
6. **Los botones ya no mienten**: la página relee el estado del mundo cada tres segundos, así que si otra pestaña —o un `curl`— pone o quita una caja, los botones se pintan solos. El estado sale de `ign model --list`, nunca de una variable del servidor.

**Conclusión**: la ventana del kiosko es ahora el tablero del laboratorio: cuatro cajas, limpiar y la vista. Todo en el puerto que Juan ya tenía en el navegador, sin perder el visor desnudo. La cámara vuelve a la pose que el plano escribió, no a una aproximada, porque la lee del mundo.

**Ajuste al dominio**: **ninguno**, sigue siendo infraestructura del operador (paper 06). Archivos: `sim/kiosk/kiosk.html` (nuevo, la página compartida), `sim/bridge/crates.py` (sirve esa página, añade `POST /view/top`, CORS y la lectura de `<camera_pose>`), `sim/Dockerfile` (copia la página a los dos sitios, con el aviso del symlink).

**Pendiente**: (1) la imagen ya quedó horneada con todo esto en el rebuild de las 21:52; (2) ese mismo reinicio se llevó las cajas que Juan había puesto — inherente: las cajas de la palanca viven solo en el mundo corriendo; (3) si algún día el kiosko se abre en un navegador ajeno a la máquina, la página apunta al `hostname` que usó el navegador, así que funciona sin cambios.

---

## 2026-09-10 · Un solo puerto: las órdenes de la palanca viajan por la membrana

**Contexto**: Juan pide desplegar dejando **solo el 6080**. La palanca vivía en un segundo servidor HTTP en 6081, y la página lo llamaba desde el navegador: al cerrar ese puerto, los botones se quedaban sin quien los atendiera.

**Observación**:
1. **Lo que se descartó, y por qué.** Un proxy en el 80 (nginx, o el propio Python) repartiendo visor y comandos obliga a atravesar el **stream de vídeo** por nuestro código y a instalar un paquete más, además de operar sobre el `supervisord.conf` que el entrypoint **regenera en cada arranque** (gotcha ya conocido del 4-sep). Se descartó.
2. **La solución era el puerto que ya estaba abierto.** El simulador ya habla ROS y rosbridge ya está publicado en 9090, porque es la membrana de los golems. Los botones publican en tópicos y `crates.py` deja de ser servidor HTTP para volverse **nodo rclpy**, igual que `teleport.py`. Nada de CORS, nada de proxy, ningún puerto nuevo:
   - `/sim/crate` (String, entra): `west | center | east | big | clear`
   - `/sim/view` (String, entra): `top`
   - `/sim/crates` (String, sale, cada dos segundos): lo que hay en el piso, `"west,big"`
   La página es el `index.html` de noVNC, así que el 6080 sirve los botones **y** la imagen; el visor desnudo sigue en `/vnc.html` y el iframe lo carga con ruta relativa, mismo origen.
3. **El fallo que me costó, y cómo se delató.** Escribí `say('… golems\' map …')` dentro de una cadena de comillas simples: el escape roto tiró **todo** el script con `SyntaxError: missing ) after argument list`. Síntoma engañoso: la página se veía bien y el aviso decía lo correcto, **porque el texto estático del `<div>` era el mismo que pone el `onopen`**. Los botones simplemente no hacían nada. Se diagnosticó (a) leyendo la consola del navegador, y (b) abriendo un segundo WebSocket desde la propia página, que demostró que rosbridge **sí** entregaba `/sim/crates`: el problema estaba de este lado. **Lección doble**: mirar la consola antes de sospechar del transporte, y no repetir en el HTML estático el mensaje que confirma la conexión — ese duplicado fue el que tapó el error.
4. **Verificado en vivo, por las dos vías**: `ros2 topic pub --once /sim/crate` desde la CLI pone la caja; el clic del navegador puso `crate_west` (el nodo lo registró: "crate at the west: in"); "Top view" devolvió la cámara ("camera back above the floor: True") y la captura tomada **dentro** del contenedor (`scrot` en el display :2) muestra el piso desde arriba con la caja del corredor oeste. Los botones se pintan solos desde `/sim/crates`. El 6081 responde `http 000`: cerrado, como se pidió.
5. **Dos reconstrucciones**: la primera imagen se horneó con el script roto; la segunda lleva el arreglo. Comprobado sobre la imagen final: la página trae el arreglo, los cuatro tópicos `/sim/*` existen y solo 6080 y 9090 están publicados.
6. **Juan ya está usando la palanca**: en el registro apareció un "cleared every crate" que no fue mío.

**Conclusión**: el kiosko queda con un solo puerto propio y sin servidor auxiliar. La palanca es ahora un nodo ROS más del simulador, hermano de `teleport.py`, y la página es el índice de noVNC. Que el camino correcto fuera "usa la membrana que ya existe" es la misma lección del paper 06 en pequeño: el segundo puerto era un síntoma, no una necesidad.

**Ajuste al dominio**: **ninguno**, sigue siendo infraestructura del operador. Archivos: `sim/bridge/crates.py` (nodo rclpy: dos suscripciones, un publicador, las llamadas a los servicios del mundo y del GUI), `sim/kiosk/kiosk.html` (habla rosbridge por WebSocket, sin librerías), `sim/kiosk/kiosk.sh` (lo arranca sin argumento de puerto), `sim/Dockerfile` (la página va solo al índice de noVNC), `docker-compose.yml` (se retira la publicación de 6081).

**Pendiente**: (1) las cajas siguen viviendo solo en el mundo corriendo: cada reinicio deja el piso limpio; (2) si algún día el kiosko se abre desde otra máquina, la página apunta al `hostname` que usó el navegador para rosbridge, así que funciona sin cambios mientras 9090 esté alcanzable.

---

## 2026-09-10 · Una sola fila por toque: `Bump` marca por dentro, `Met` retracta; la marca fantasma y la reconsideración

**Contexto**: Juan: "el bump y mark deberían ir en el mismo script" y luego "mantengamos una sola fila; además el script no sé si debería llamar a la global del journal de obstáculos o el mismo `g.Bump` internamente se lo setea". Recomendación aceptada ("sí, apliquémoslo"): que `g.Bump` marque por dentro — el script no debe tocar `collisions` porque el sujeto asambla los repertorios (paper 0A) y la marca es la consecuencia del hecho, no otro hecho.

**Ajuste al dominio** (`Golem`, `Touches.Collisions`): `Bump(id, Pose)` presume una cosa y marca (`collisions.Mark(touch)`); `Bump(Pose)` (parado) no marca; `HearBump(who, Pose, Position)` oye y marca como presumió el compañero; `HearTouch(who, Position, Position)` oye sin marcar; `Met(who, Position)` = `Meet` + `Unmark(at)` (mi marca, a `SameTouch`) + `UnmarkHeardFrom(who, at)` (la que aprendí de ese compañero a `MeetingReach`); `LearnMet(Position)` = `Unmark`. Se retiran `Mark`, `LearnMark`, el tell `ObstacleMarked` y la reacción `echo-marked`. Tells: `BumpedAt` lleva ahora `heading`; nuevos `TouchedAt` (expose `tx, ty, twho, tpx, tpy`) y `MetPeer` (expose `ex, ey`). Tests: 53 verdes (`ABump_IsATouchAndAMarkAtOnce_UntilAPeerSpeaks`, `APeersBumpHeardThereAndThen_NamesWhoWasMet`: 2 marcas tras el choque de ambos, 0 tras `Met`).

**Observación en vivo, corrida 1** (`journal-legacy-20260910-unafila/`): red → garage, blue → kitchen + garage. El journal de blue: `{ touch = Pose(…); g.Bump(1, touch); } Expose … 'heading' …` → `tell BumpedAt …`; `{ at = Position(…); g.Met('red', at); } Expose … 'ex' … 'ey'` → `tell MetPeer`; red: `g.Bump(touch)` parado con `tx/ty` → `tell TouchedAt`, y `HearBump('blue', touch, peer)`, `LearnMet(at)`. Pero `/obstacles` terminó con **una cosa fantasma en el garage en los tres golems**: uno de los cuatro choques de blue contra red (≈ 8.08, 1.49) nunca concluyó `Met` porque el `TouchedAt` de red llegó después de la ventana de escucha (2.5 s): la fila del compañero pasa por su journal, su reacción y el cable, y un cuerpo parado cuenta su toque a lo sumo una vez por segundo. La marca presumida quedó y la flota la aprendió por `HearBump`. Es exactamente el costo que se advirtió al proponer la fila única.

**Conclusión**: la conclusión no puede depender de una sola ventana. El dominio ya sabe decidir tarde (`Suspect(x, y, heading, since)` mira lo oído desde el conteo del arranque del tramo); lo que faltaba era que el host, dueño del reloj, siguiera preguntando.

**Ajuste al host** (`GolemChoreography.ReconsiderAsync`): tras concluir "cosa" en la ventana, el host sigue preguntando `Suspect` cada 0.5 s durante 12 s; si el dominio pasa a sospechar un compañero, escribe el mismo `Met` (con nota en el panel: "spoke after the window") y la marca se retira aquí y, por `MetPeer`, en todos. **Ninguna decisión nueva en C#**: el host solo alarga su reloj; quién fue lo dice el dominio.

**Observación en vivo, corrida 2** (journal actual; anteriores en `journal-legacy-20260910-unafila-b/`): misma coreografía dos veces. Un solo choque de blue contra red en (8.5, 1.5), concluido `Met` dentro de la ventana; `/obstacles`: blue `{things 0, met 1, marks 0}` (el `Peer` red en garage como historia), red y green `{total 0}`. Sin fantasma. La rama tardía no se ejercitó en estas dos corridas (el encuentro fue rápido): queda verificada por construcción, no en vivo. Nota: la primera misión de blue en la corrida fue un `Follow` al garage porque red le contó `PointVisited` — comportamiento del seguidor ya conocido, no del protocolo de toques.

**Pendiente**: (1) provocar la rama tardía en vivo (dos cuerpos en movimiento chocando en un pasillo); (2) si la palabra llega después de los 12 s, la marca queda: el operador tiene `/forget`; (3) `Bump(Pose)` parado devuelve un contador que nadie lee — candidato a desaparecer si el journal no lo necesita.

---

## 2026-09-11 · La marca caía fuera de la cosa: el toque se ponía en la nariz, no donde el casco fue presionado

**Contexto**: Juan, mirando el panel con la caja del centro (0.7 × 0.7 en (5.5, 5.5), pasable por ambos lados): "el robot hizo varios intentos por pasar, se encontró con el obstáculo del centro y registró los puntos, pero aún tenía forma de pasar en los laterales; decidió irse por el lado west, ¿por qué no recalculó para irse por un lado del obstáculo?"

**Observación** (journal en `journal-legacy-20260911-marcas/`): sí recalculó — dos rodeos dentro del centro (`around@4.98,6.46 > around@4.78,5.98` por la izquierda, `around@5.8,5.18` por la derecha), los dos terminaron en choque, y al tercer recálculo ningún rodeo dentro del centro le cupo al cuerpo: el pasillo west fue la ruta más corta que sí cabía. Las cinco marcas iban de x 4.94 a 6.08 cuando la caja ocupa 5.15–5.85: las marcas extremas caían 0.2 m FUERA de la cosa a cada lado. Con el alcance de cada marca (0.25) el dominio presumía una cosa de 4.69 a 6.33; contra las paredes (x 4 y 7) quedaban 0.69 y 0.67 m, y el cuerpo pide 0.5 + 0.1 (`MarkMargin`) + 0.1 (`BodyMargin`) = 0.7. Faltaron 1 y 3 cm. En la realidad sobra 1.15 m por lado.

**Causa**: el host ponía el toque un radio ADELANTE de la nariz, en el rumbo del cuerpo (`DiffDriveNavigator`: "the run is taken as head-on"). Cuando el cuerpo pega con el flanco contra la esquina de la caja, el contacto real está a un lado; la marca quedaba corrida hacia afuera. El mensaje `ros_gz_interfaces/Contacts` trae `positions[]` (los puntos de contacto, en el marco del mundo) y la membrana leía solo `collision1/2.name`.

**Ajuste a la membrana** (`Rosbridge`, `DiffDriveNavigator`, `GolemChoreography.TellTouchedStanding`, `OperatorController./body`): `Contact(With, At, Bearing)` — el rumbo del toque SOBRE EL CASCO relativo a la proa (0 = la nariz, +90° = flanco izquierdo). Se calcula con la pose VERDADERA del cuerpo (un parachoques sabe qué parte del casco fue presionada, crea lo que crea sobre dónde está), y las coordenadas del mundo no salen del método: el punto en el plano es `Contact.On(pose creída, radio)` — un radio desde el centro creído, en la dirección del bearing; el heading de la marca apunta del cuerpo hacia la cosa. Sin `positions` o antes de la primera verdad, bearing 0 (como antes). El toque parado usa lo mismo. `/body` muestra `bearingDeg`. **Ajuste al dominio: ninguno** — la marca es un hecho y se estaba journaleando en el lugar equivocado; el dominio razonaba bien sobre un dato malo.

**Verificación en vivo** (caja `center` puesta con `/sim/crate`, blue de north a south):
```
[navigator] touched crate_center at bearing 57° off the nose — the touch lands at (5.28, 5.86)
[navigator] touched crate_center at bearing 60° … (5.21, 5.85)
[navigator] touched crate_center at bearing 65° … (5.16, 5.85)
[navigator] touched crate_center at bearing 71° … (5.10, 5.83)
marks: (5.28, 5.86) (5.16, 5.85) (5.46, 5.85) (5.58, 5.86) (5.83, 5.94), normales ≈ -90°
```
Las cinco marcas caen sobre la cara norte de la caja (y = 5.85) y dentro de su ancho (5.15–5.85); antes: 4.94–6.08. Blue rodeó por la izquierda (`around@4.48,5.85`) y llegó a south sin ir por west.

**Conclusión y pendiente**: el dato ya es honesto; ahora se ve el siguiente defecto. Necesitó cuatro choques para rodear una caja de 0.7 m porque la primera corrida tras retroceder se juzga con una relajación (`RoutePlanner.Sees`, nodo `Start`: "una marca dentro del propio radio es un estimado que salió mal… puede rozarla a `radius - 0.05`") que existía para compensar marcas corridas. Con las marcas en su sitio, esa relajación deja que el primer tramo pase a 0.2 m de una marca que está en la esquina de una cosa que sigue 0.7 m más: choca otra vez. Candidato: quitar o estrechar la relajación y probar de nuevo con la caja del centro (medir cuántos choques hacen falta). Segundo candidato, después: el alcance de las marcas exteriores de un polígono ya dibujado (0.25 a cada lado).

---

## 2026-09-11 · La ruta es un objeto: `route = g.Route(@id)` y los tramos son actos suyos

**Contexto**: Juan: "los scripts de `g.Route(1)` ¿no deberían retornar un objeto tipo `route = g.Route(1)` y empezar a llamar los métodos del objeto `route.METODO`?"

**Diagnóstico**: el objeto existía pero escondido. `Golem` guardaba `drafting[id]` (una lista de tramos por misión) y `Via`, `Around`, `Aside`, `Stop` recibían el `@id` para encontrarla: estado de un objeto manejado con una clave primitiva. `Routes.Trajectory` ya es la ruta del glosario, pero nacía terminada (la construía el planificador o el `Stop` final).

**Ajuste al dominio** (`Routes.Trajectory`, `Golem`): `Trajectory` gana un segundo constructor, `Trajectory(layout, mission)`, la ruta en decisión; sus actos `Via(passage, at)`, `Around(at)`, `Aside(at)`, `Stop(at)` agregan el tramo y devuelven la misma ruta (tren posible). `Stop` cierra cuando la ruta tiene tantas paradas como la misión tiene por delante: `mission.Route(layout.WithDoorCrossings(this))`. Después de decidida, o si nació entera, no admite tramos (`IsDecided`). `Golem.Route(id)` devuelve esa ruta y pierde `Via/Around/Aside/Stop` y el diccionario. La validación de la puerta ("stands at (x, y), not at…") se mudó a `Trajectory.Via`, con el layout que la ruta recibe al nacer. Tests: 53 verdes sin tocar ninguna aserción; solo el helper `Route` escribe los actos nuevos.

**Ajuste al host** (`RoadLeg.Acts`): `route = g.Route(@id);` y luego `route.Via(door1, at1); … route.Stop(stop4);`.

**Observación en vivo** (blue → garage con la caja del centro puesta; journals anteriores en `journal-legacy-20260911-route/`):
```
define action 1 (id:int, area:string, lx1:double, ly1:double, la1:string, lb1:string, …) as { point = map.Find(area); g.Visit(id, point);
    route = g.Route(id); opening1 = map.FindOpening(la1, lb1); at1 = Position(lx1,ly1); route.Via(opening1, at1);
    opening2 = map.FindOpening(la2, lb2); at2 = Position(lx2,ly2); route.Via(opening2, at2);
    door3 = map.FindDoor(la3, lb3); at3 = Position(lx3,ly3); route.Via(door3, at3); stop4 = Position(lx4,ly4); route.Stop(stop4); } end;
 3: { point = map.Find('garage'); g.Visit(1, point); route = g.Route(1); opening1 = map.FindOpening('north', 'center'); at1 = Position(5.5,8); route.Via(opening1, at1); … stop4 = Position(9,1.5); route.Stop(stop4); }
17: { route = g.Route(1); around1 = Position(6.28,5.37); route.Around(around1); opening2 = map.FindOpening('center', 'south'); … route.Stop(stop4); }   ← el recálculo tras el choque
29: { point = Position(9,1.5); g.Reach(1, point); } Expose 1 'rid'; Expose 9 'rx'; Expose 1.5 'ry';
```
El motor asigna a una variable local (entre llaves) lo que devuelve un método del sujeto y despacha los métodos del objeto devuelto; en la rehidratación la ruta se reconstruye al reejecutar las mismas sentencias (los tres golems arrancaron y blue caminó su plan). Blue chocó dos veces con la caja (marcas (5.9, 5.9) y (5.8, 5.8), sobre la esquina noreste de la caja) y llegó al garage por la derecha.

**Conclusión**: regla general, ya vista dos veces (`map`, `route`): cuando un verbo del sujeto recibe una clave para encontrar un estado que otro verbo abrió, ese estado es un objeto que el primer verbo debe devolver. Candidatos a revisar con la misma lupa: `g.Visit(@id, point)` repetido por parada (¿`errand = g.Visit(point); errand.Then(point2)`?) — se propone antes en el PLAN, no ahora.

---

## 2026-09-11 · Magnitudes con unidad: el cuerpo se construye de `Meters`, `MetersPerSecond` y `Seconds`

**Contexto**: Juan, ante `body = Body(0.25, 2.0, 6.0)`: "vamos a crear una clase, no sé si del sistema internacional, que nos especifique qué son esos números, tipo las unidades de medida y velocidad, aceleraciones, segundos, y definirlos en variables para setearlas a los parámetros del body".

**Ajuste al dominio** (`Units.Length/Speed/Acceleration/Duration` + unidades, `Robots.Body`, `Golem`): la magnitud es la clase abstracta y la unidad la concreta que el journal construye — el mismo dibujo que `Map`/`MapLayout`: el POO habla solo y el valor dice qué es donde se escribe. Cada magnitud se lee en su unidad base del SI (`InMeters`, `InMetersPerSecond`, `InMetersPerSecondSquared`, `InSeconds`) y rechaza negativos; `Speed.TimeFor(Length)` devuelve una `Duration`. `Body(Length, Speed, Duration)` guarda las magnitudes; el golem las lee en unidad base para el host (`g.Radius()`…), que no cambia. `Acceleration` queda definida sin que nadie la tome todavía: es el repertorio, no el verbo (paper 0A).

**Observación (motor)**: una magnitud del tipo equivocado la rechaza el motor ANTES de ejecutar, en la validación estática, con un mensaje que nombra el problema: `Body(Seconds(0.25), MetersPerSecond(2.0), Seconds(6.0))` → *"You are trying to call the constructor of 'Body' with a value of type 'Seconds' at parameter #1, but a value of type 'Length' is expected"*. Compárese con el rechazo de un constructor por su propia regla (`Seconds(-1.0)`), que sigue llegando como *"Error while instantiating class 'Seconds'"* sin la razón del dominio. Lección: **el tipo es la validación que el motor sí sabe explicar**; un número crudo nunca la tendrá. Los tests cubren ambos caminos y las conversiones (`Centimeters(25.0).InMeters == 0.25`, `Minutes(1.5).InSeconds == 90`).

**Observación en vivo** (journals anteriores en `journal-legacy-20260911-units/`): los tres golems rehidrataron con la release nueva —
```
upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); body = Body(radius,speed,linger); }
```
`/progress` de red: `speed 2.0, lingerAfterTold 6.0`; red fue a storage y llegó. `radius`, `speed` y `linger` son globales del actor (nivel del upgrade, P2a), como Juan pidió; el panel consulta `body.Radius.InMeters`.

**Pendiente**: las lecturas del golem al host siguen siendo dobles en unidad base (`g.Radius()`); si algún día el host quiere la magnitud, la lee del módulo (`body.Radius.InMeters`). Otros números crudos del dominio que merecen la misma mirada: las constantes de `Collisions` (`MarkReach`, `MeetingReach`…) y de `MapLayout` (`DoorWidth`, `WallThickness`) — son metros y podrían declararse como tales.

---

## 2026-09-14 · Tras un reinicio en frío la realidad nació seis veces: la VM se ahogó y Docker dejó de contestar

**Contexto**: Juan reinició la máquina y "no conectaba nada": los paneles no respondían, el kiosko decía *Failed to connect to server* y se quedaba en "reaching the world…".

**Observación**: `docker ps` contestaba pero `docker logs` y `docker info` se colgaban y la API devolvía 500. Dentro de la VM de WSL (tope 4 GB en `~/.wslconfig` desde marzo): 3.7 GB usados, 1.9 GB de swap, carga 338, y en `golem-sim` SEIS servidores de Gazebo, seis `parameter_bridge`, seis `crates.py` y varios `ign model --list` de 600 MB cada uno, muchos en estado D. El registro del contenedor lo explicaba: `exited: vnc (exit status 255; not expected)` → `spawned: 'vnc'`. El supervisor de la imagen base reinicia su Xvnc cuando muere, y su `xstartup` es nuestro `kiosk.sh`: cada reinicio del VNC ejecutaba el guion completo y paría otro mundo encima del anterior, cuyos procesos sobreviven porque no son hijos de Xvnc. En un arranque en frío con la máquina cargada el VNC muere una vez, el segundo mundo agota la memoria, el swap hace morir el VNC otra vez, y la espiral se cierra sola. Reiniciar Docker Desktop no ayuda: los contenedores vuelven por `restart: unless-stopped` y repiten el ciclo.

**Segundo factor**: `crates.py` preguntaba al mundo qué cajas hay cada 2 s con `ign model --list`, un proceso ruby de ~1 s de CPU y 600 MB por llamada: carga constante aun en reposo, y bajo presión se apilan.

**Conclusión**: la infraestructura tenía dos fugas latentes que solo la memoria justa reveló. Es el paper 06 otra vez: el síntoma ("Docker no conecta") estaba dos capas por debajo de donde apareció.

**Ajuste a la infraestructura** (`sim/kiosk/kiosk.sh`, `sim/bridge/crates.py`, `sim/kiosk/kiosk.html`): (1) `kiosk.sh` es idempotente: si ya corre `ign gazebo -s`, la sesión nueva solo espera (`exec sleep infinity`), o levanta únicamente la GUI si fue la imagen lo que murió; (2) el latido de `/sim/crates` pasa de 2 s a 10 s, y la página pide el piso al conectar publicando `tell` en `/sim/crate`, así una pestaña recién abierta no espera al latido; cada orden sigue contando el piso de inmediato. **Ajuste al dominio: ninguno.**

**Verificación**: flota arriba con un solo servidor, un puente, un `crates.py`, una GUI; VM en 2.0 GB usados y 1 MB de swap (antes 3.7 GB y 1.9 GB), carga 6 (antes 338); los tres paneles contestan, el kiosko muestra la imagen con los tres cuerpos y la lista de cajas. Journals intactos (no se archivó nada: no cambió el lenguaje).

**Receta si vuelve a pasar** (síntoma: `docker ps` sí, `docker logs` no): `docker compose stop` si el motor aún contesta; si no, `wsl --shutdown` y abrir Docker Desktop; con la imagen nueva el mundo ya no se duplica. **Pendiente (decisión de Juan)**: el tope de 4 GB en `~/.wslconfig` fue suficiente con el mundo nacido una vez, pero el margen es corto (2 GB libres con la flota quieta); subirlo a 6 GB daría aire en la máquina de 16 GB.

---

## 2026-09-14 · Pausa y reanudación del trayecto: la retención en el journal, y un reinicio que se hacía pasar por pausa

**Contexto**: Juan: "apliquemos una forma de poner pausa/continuar el trayecto actual en ejecución y que se registre en el journal".

**Ajuste al dominio** (`Robots.Mission`, `Golem`): `Mission.Paused`; `Pause()` exige misión pendiente y no pausada; `Resume()` exige pausada. Ni el plan ni el cursor ni las paradas cambian: la pausa es una retención, distinta de la interrupción (`Bump`/`Graze`, que obligan a decidir otra ruta) y del final (`Fail`/`Abandon`). `Golem.Pause(id)`, `Golem.Resume(id)`, lectura `IsPaused(id)`. Test `APausedMission_KeepsItsPlanAndItsPlace_UntilResumed` (54 verdes): pausada sigue pendiente y enrutada con su parada por delante; doble pausa y reanudación sin pausa se rechazan; una misión completada no se pausa ("already completed").

**Ajuste al host** (`GolemChoreography`, `GolemController`, panel): `ReadPlan` lee `paused`; en la cabeza del bucle, si la misión está pausada, `HoldWhilePausedAsync` detiene el cuerpo, anota en el panel y espera al journal contando los toques como los de un cuerpo parado; a mitad de tramo, un vigilante consulta `IsPaused` cada 300 ms y cancela la conducción del tramo con un token enlazado; al reanudar, el mismo tramo se retoma desde donde está el cuerpo (cursor intacto, sin recálculo). `POST /pause` y `POST /resume` con `Check` (pendiente; no pausada / pausada) y `g.Pause(@id)` / `g.Resume(@id)`; `/state` trae `paused`; botones Pause/Resume en el panel.

**Observación 1 — la pausa que "funcionaba"**: en las dos primeras corridas el cuerpo se detuvo, el journal mostró `g.Pause(1)` y `g.Resume(1)` y la misión llegó… pero la nota del panel decía "paused at (0.0, 0.0)", luego "(NaN, NaN)": la pose del cuerpo era nula en ese instante, cuando `/body` la mostraba. `docker inspect`: **`RestartCount=2`**, y el registro: `clean shutdown at entry 76` → `rehydrated at entry 77`. La cancelación del tramo (`Task.Delay(Tick, ct)` dentro del navegador) escapaba como `OperationCanceledException`, subía hasta `Program.cs`, que la toma por el apagado ordenado, el proceso terminaba, Docker lo reiniciaba (`restart: unless-stopped`) y el golem rehidratado veía la pausa al despertar antes de recibir su primera pose. Se veía como una pausa y era una muerte y una resurrección.

**Conclusión**: la rehidratación es tan buena que esconde caídas. Dos señales que hay que mirar siempre al verificar una función del host: `RestartCount` del contenedor y el par `clean shutdown`/`rehydrated` en el registro. Una función que "funciona" con la pose nula no funciona.

**Corrección**: `catch (OperationCanceledException) when (pausedMidLeg.IsCancellationRequested && !ct.IsCancellationRequested)` alrededor de la conducción del tramo: la cancelación por pausa se distingue de la del apagado.

**Observación 2 — verificado**: blue → kitchen, pausa a los 5 s: `paused by the operator at (0.9, 2.2)`, pose idéntica a los 3 y a los 8 s, `resumed — taking up the plan from where the body stands`, llega a (2, 9.5); `RestartCount` 0 antes y después; journal: `g.Pause(5);` … `g.Resume(5);` … `{ point = Position(2,9.5); g.Reach(5, point); }`. Segunda pausa con la misión ya pausada: `{"EWI":[{"Error":"the mission is already paused"}]}`.

**De paso**: blue no encontraba ruta ("no road … past 9 marks") porque guardaba las marcas de las cajas del viernes (west, center y east) y el mundo regenerado ya no las tiene: el operador las olvidó con `/forget` (tres cosas), como manda la doctrina — la marca es un hecho, quien sabe que la cosa ya no está es el operador.

**Pendiente**: una pausa que llega durante el protocolo de un choque (escucha y reconsideración) surte efecto al tramo siguiente, no en el acto; documentado, no corregido.

---

## 2026-09-14 · La ruta es el encargo: `route = g.Visit(point)`, el camino en puntos, los actos sobre la ruta

**Contexto**: tres observaciones de Juan seguidas. Sobre `g.Fail(@id, …)`: "uno le setea el parámetro primero para buscar la misión, pero debería sacar la Route con un find y a la variable ponerle un fail". Sobre la separación misión/ruta: "yo todo este tiempo pensé que la Route era el encargado de devolver un objeto con la ruta completa… la idea es decir visit, luego llamar al route y devuelve un objeto con todos los puntos a visitar y eso es lo que se está siguiendo". Sobre los tramos: "esperaría que quizás algún Eval del puppeteer le fuéramos sacando todos esos puntos que conoce el objeto route… creando el listado de puntos en el script". Y sobre `g.Visit(@id, …)`: "el cálculo del siguiente id se puede hacer dentro del método para quitar ese primitivo".

**Diagnóstico**: la separación `Mission` (encargo, estado) / `Trajectory` (tramos) era mía, no del dominio: lo que Juan llama ruta es el objeto que nace con el `Visit`, guarda las paradas, recibe el camino y es lo que el cuerpo sigue. Y los pasajes en el script (`opening1 = map.FindOpening(…)`) eran ruido: la ruta sabe qué es cada punto porque tiene el mapa.

**Sobre el `Eval`** (guía `puppeteer-parameters`): los `Parameter.Eval` se resuelven al cargar argumentos, ANTES del cuerpo, y congelan un valor escalar en la cabecera de la entrada. La ruta no existe en ese momento (la crea el `g.Visit` del cuerpo), y su resultado no es una lista de objetos. Lo que Juan describe ya es lo que hace el host con `g.Preview` antes de escribir: los puntos que la ruta conoce, congelados como valores en el script. Y no se calculan dentro del cuerpo a propósito: si cambiara el planificador, la rehidratación decidiría otra ruta y la historia cambiaría (paper 05).

**Ajuste al dominio** (`Routes.Route` nuevo; `Robots.Mission` y `MissionStatus` desaparecen → `RouteStatus`; `Golem`, `Trajectory`, `Leg`): `Route(id, stop, following, choosesOrder, layout, collisions)`. Paradas: `Then(Position|Area)`. Camino en puntos: `Via(Position)` — la ruta lo nombra contra el mapa: puerta si coincide con `PointOf(door)`, frontera si está sobre `EdgeOf(opening)`, `via` (nuevo `Leg.Waypoint`) si no; `Stop(Position|Area)` exige que sea una parada por delante y, cuando todas tienen su tramo, toma la trayectoria (`WithDoorCrossings`); un `Via` tras una ruta decidida abre la decisión siguiente. Marcha: `Reach(Position)`, `Bump(Pose)` (marca en `collisions`), `Graze(Position)`; retención `Pause()`/`Resume()`; final `Fail`, `Abandon`, `Announce`. `Golem`: `Visit/Cover/Follow` devuelven la ruta con el handle acuñado dentro (`lastHandle + 1`, determinista en la rehidratación, nunca reutilizado); `Find(id)` pasa a ser público y es el único lugar donde entra el id; desaparecen `Route(id)`, `Reach`, `Bump(id, …)`, `Graze`, `Pause`, `Resume`, `Fail`, `Abandon`, `Announce` del golem (queda `g.Bump(touch)` del cuerpo parado, sin ruta). `Trajectory` vuelve a nacer entera. Tests: 55 verdes; el helper `Route` escribe solo puntos y dos aserciones que esperaban `around`/`aside` como rumbo esperan ahora `via`.

**Ajuste al host** (`RoadLeg.Acts/Bind/Script`, `GolemController.Errand`, handlers, `echo-reached`, panel): el encargo escribe `route = g.Visit(point1); route.Then(point2); via{n} = Position(@lx{n}, @ly{n}); route.Via(via{n}); route.Stop(point1)…` (una parada nombrada por el encargo se escribe con su misma variable; en un recálculo, `stop{n}`); desaparece el `Parameter.Eval` de `g.NextHandle()`. Handlers: `{ route = g.Find(@id); … }` para Reach, Bump, Graze, Fail, Abandon, Pause, Resume; `foreach (id in g.PendingIds()) { route = g.Find(id); route.Abandon(@reason); }`; la reacción `echo-reached` continúa con `{ route = g.Find(@missionId); route.Announce(); } tell PointVisited …` (un bloque entre llaves seguido de un `tell` en un `Causation.Continue` funciona).

**Observación 1 — un tropiezo del motor**: la primera corrida falló al escribir: *"Unknown property or method 'Stop' on type 'Route'"*. `route.Stop(point1)` con `point1 = map.Find('kitchen')` entrega una `Zone`, y el motor liga por el tipo en tiempo de ejecución: sin sobrecarga `Stop(Area)`, el método "no existe". El mensaje engaña (dice que falta el método, no que falta la sobrecarga). Corregido con `Stop(Area)` y un test que escribe el encargo exactamente como el host. Lección: **todo acto que reciba lo que un `map.Find` devuelve necesita la sobrecarga con `Area`**.

**Observación 2 — en vivo** (journals anteriores en `journal-legacy-20260914-route/` y `-route-b/`; cero reinicios, cero errores de escritura):
```
blue 3: { point1 = map.Find('kitchen'); route = g.Visit(point1); point2 = map.Find('garage'); route.Then(point2);
          via1 = Position(4,9.5); route.Via(via1); route.Stop(point1); via3 = Position(4,9.5); route.Via(via3);
          via4 = Position(5.5,8); route.Via(via4); via5 = Position(6.19,3); route.Via(via5); via6 = Position(7,1.5); route.Via(via6); route.Stop(point2); }
red  3: { point1 = map.Find('living'); route = g.Cover(point1); point2 = map.Find('storage'); route.Then(point2); route.Stop(point1); via2 = …; … route.Stop(point2); }
red    : { route = g.Find(1); point = Position(2,1.5); route.Reach(point); } Expose 1 'rid' …
         { route = g.Find(1); route.Announce(); } tell PointVisited with 2, 1.5 to blue once …
blue   : { point = Position(2,1.5); g.Follow(point); }
         { route = g.Find(2); route.Abandon('superseded by mission 3'); }
         { route = g.Find(3); via1 = Position(10.25,3); route.Via(via1); via2 = Position(10.25,8); route.Via(via2); stop3 = Position(9,9.5); route.Stop(stop3); }
blue 4 : { point = map.Find('south'); route = g.Visit(point); via1 = …; route.Via(via1); via2 = …; via3 = …; route.Stop(point); }
         { route = g.Find(4); route.Pause(); }
         { touch = Pose(8.77,9.48,3.03); g.Bump(touch); } Expose … 'tx' …         ← red lo tocó mientras estaba pausado: toque de cuerpo parado
         { route = g.Find(4); route.Resume(); }
         { route = g.Find(4); touch = Pose(8.53,9.49,3.09); route.Bump(touch); } Expose … → tell BumpedAt …
         { route = g.Find(4); via1 = Position(8.75,9.98); route.Via(via1); via2 = Position(7,9.5); … stop5 = Position(5.5,1.5); route.Stop(stop5); }   ← el recálculo
         { route = g.Find(4); point = Position(5.5,1.5); route.Reach(point); }
```
La numeración de `via{n}` cuenta los tramos (una parada nombrada ocupa su número sin variable: `via1`, `Stop(point1)`, `via3`…). El host camina el plan leído de `g.RoadAhead` con los tramos ya nombrados por la ruta (las puertas conservan sus cruces rectos).

**Conclusión**: regla general confirmada por tercera vez (`map`, `route` como trayectoria, `route` como encargo): cuando un verbo del sujeto recibe una clave para encontrar un estado que otro verbo abrió, ese estado es un objeto que el primero devuelve. El sujeto queda con lo que crea (`Visit`, `Cover`, `Follow`), lo que busca (`Find`), lo que oye (`HearBump`, `HearTouch`, `LearnMet`, `LearnForget`), lo que concluye (`Met`, `Forget`) y el toque del cuerpo parado (`Bump(touch)`).

**Pendiente**: (1) el `Bump(touch)` del cuerpo parado sigue en el golem porque no hay ruta: candidato a ser del cuerpo (`body.Bump`?) si algún día el cuerpo journalea; (2) los nombres del host (`MissionRouted`, `MissionBumped`, `mission {id}` en el panel) hablan todavía de misiones: son del host, no del journal, y se renombran cuando toque el host.

---

## 2026-09-14 · Ningún método del dominio procesa un objeto que no le dieron

**Contexto**: Juan: "hay varios métodos del dominio que reciben objetos, pero no validamos en cada uno si viene null; hay que dar una excepción del dominio con un `if` antes de procesarlo, en todos los métodos que reciban un objeto por parámetro".

**Inventario**: un guion recorrió `GolemDomain/` buscando todo método o constructor no privado con parámetros de tipo objeto (`Position`, `Pose`, `Area`, `Zone`, `Door`, `Opening`, `Segment`, `Rectangle`, `Trajectory`, `Leg`, `Mark`, los módulos, listas) sin `== null` en su cuerpo: 95 miembros sin ninguna guarda y otros tantos con la guarda escrita como `?? throw` en línea. Los helpers privados se dejaron como están: reciben lo que el método público ya validó.

**Ajuste al dominio** (20 archivos, 176 guardas): cada miembro abre con `if (x == null) throw new GolemDomainException(…)`, una línea por parámetro, antes de tocar nada; los miembros de expresión (`=>`) pasaron a bloque. Los `?? throw` que validaban un parámetro se convirtieron en `if` conservando su mensaje ("a golem needs a body to drive", "a wall bounds a zone"…); los que significan "no encontrado" (`Doors.FirstOrDefault(…) ?? throw`) se quedan, porque no hablan de un parámetro nulo. Donde el dominio no tenía frase propia, el mensaje es uniforme y buscable: `"Tipo.Método: 'parámetro' was not given"` (`"Map.Connects: 'a' was not given"`). Test nuevo `AnObjectNotGiven_IsRefusedByTheDomain_BeforeAnythingRuns` (56 verdes): el mensaje llega como excepción del dominio, no como `NullReferenceException` desde adentro.

**Observación (método)**: la transformación se hizo con un guion idempotente sobre el árbol limpio (`git checkout -- GolemDomain` y volver a correr), no a mano: 176 ediciones a mano habrían dejado huecos, y el guion imprime el conteo por archivo para comparar contra el inventario. Lección de la semana repetida: cambios mecánicos anchos, por guion y verificados por conteo.

**Conclusión**: una guarda en la puerta convierte un `NullReferenceException` anónimo en una frase del dominio que el motor devuelve al comando ("Error while instantiating…" o el mensaje mismo en un método) y que el journal no registra. Sin cambio en el lenguaje del journal; los journals siguen válidos. La verificación en vivo quedó pendiente porque Docker Desktop estaba cerrado al terminar: el cambio no toca ningún camino que la rehidratación ejecute con nulos, y los 56 tests rehidratan al golem entero.

---

## 2026-09-14 · El simulador no arrancaba: el motor de Docker estaba apagado

**Contexto**: Juan reporta que el simulador no arranca. Antes de tocar `sim/` conviene separar dos cosas que se parecen desde afuera: que el simulador falle, o que no haya dónde correrlo.

**Observación**:
1. `docker compose ps` no respondió con contenedores sino con el error del socket: `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`. Ese mensaje **no habla del simulador**: dice que el motor no está.
2. Confirmado desde Windows: el proceso de Docker Desktop no existía, el servicio `com.docker.service` estaba **Stopped**, y la distro `docker-desktop` de WSL, **Stopped**.
3. Arrancado Docker Desktop, el motor respondió en unos tres segundos y **los cuatro contenedores se levantaron solos**, porque el compose los declara `restart: unless-stopped`. No hizo falta `compose up`.
4. Comprobado que el simulador sí arranca por dentro, no solo que el contenedor está "Up": `ign gazebo -s -r` (física) y `ign gazebo -g` (imagen), el `parameter_bridge`, `teleport.py`, `crates.py` y rosbridge, que registró a los tres golems conectándose y a la página del kiosko suscribiéndose a `/sim/crates`. El mundo contesta a `ign model --list` y la captura tomada dentro del contenedor (display :2) muestra la arena desde arriba con los tres cuerpos y el factor de tiempo real al 80%.
5. **Gotcha de diagnóstico propio**: el primer `pgrep -a "ign|rosbridge"` no encontró nada y casi me hace concluir que Gazebo no corría. `pgrep -a` casa contra el **nombre** del proceso; hace falta `pgrep -af` para casar la línea de comando completa, que es donde viven `ign gazebo -s -r` y el rosbridge lanzado por python.

**Conclusión**: no había avería. La causa era que el motor de Docker estaba apagado, y el síntoma se confunde con "el simulador no arranca" porque todos los comandos de docker fallan igual. La señal que los distingue es el error del *npipe*: si aparece, nada de lo que se pruebe dentro del contenedor tiene sentido todavía.

**Ajuste al dominio**: ninguno; es operación, no dominio.

**Pendiente**: nada. Queda como receta: ante "no arranca", primero `docker info`; si da el error del npipe, arrancar Docker Desktop y esperar a que el motor responda; recién entonces mirar el log de la sesión del kiosko (`~ubuntu/.vnc/*.log`), que es donde escribe `kiosk.sh`.

---

## 2026-09-14 · Inventario de la superficie del journal: qué recibe objetos, qué valida, qué sobra

**Contexto**: Juan pidió "una lista de los métodos y los parámetros que reciben: cuáles ya tienen el ajuste de objetos, cuáles las validaciones, cuáles aún reciben primitivos y si se pueden sustituir, y cuáles ya no se usan". Un guion contó, por miembro de `Golem` y `Route`, el tipo de parámetros, las guardas y los usos en host, panel y tests.

**Observación**: todos los actos del journal reciben objetos y validan (`Visit/Cover/Follow`, `Then`, `Via`, `Stop`, `Reach`, `Bump`, `Graze`, `Pause/Resume/Announce`, `LearnMet/Forget/LearnForget`). Quedan primitivos legítimos: `g.Find(id)` (búsqueda), `Fail/Abandon(why)` (texto; candidato a un `Reason` con variantes, paper 01) y `who` en `HearBump/HearTouch/Met` (la identidad del journal ajeno no es objeto del dominio). Las lecturas reciben `id` y `x, y` porque la pose es telemetría de la consulta y el id la clave del host. Sin uso en el host: `g.LearnMark`, `g.Evasion` (+ `Maneuver`, `EvasionStrategy`, `Side`) y `Route.ReadReason`; solo en tests: `Plan`, `PlanPast`, `NextHandle`, `KnowsPlace`, `PlaceCount`, `PassageCount`, `HeardBumpNear`, `WasAnnounced`, `HeadingKind`, `StatusOf`, `Places` (lecturas de laboratorio, se quedan).

**Ajuste al dominio** (`Golem`, `Routes.Route`, `Touches.Suspicion`; `Routes/Maneuver.cs` eliminado): fuera `LearnMark`, `Evasion` y la familia de maniobras (el paso de cortesía de `RoadPast` se calcula en el golem: `CourtesyStep = 0.5` a la derecha y luego a la izquierda de la proa), fuera `ReadReason` y el campo `reason` (la razón es del acto y la guarda el journal). `Suspect(Pose touch, int since)` con guarda; `ThingFound.Conclusion` pasa de `"Mark"` a `"Bump"`: desde la fila única no hay verbo `Mark`, la conclusión de una cosa es que la marca del choque se queda. Host: `g.Suspect(Pose(@x, @y, @heading), @since)`. Tests: 55 verdes; el helper del laboratorio planta marcas con `collisions.Mark(touch)` sobre el módulo global, que es lo que un laboratorio hace: calcular con un módulo solo.

**Conclusión**: el inventario por guion (parámetros, guardas, usos) es barato y vale repetirlo tras cada cambio de lenguaje; deja ver muertos que la lectura del código no delata. **Pendiente**: el tanteo alrededor de una marca sigue siendo pasos del host (`StepAsideAsync`, `FeelForAWayPastAsync`): la doctrina pide que el dominio decida la maniobra y el host la ejecute; se propone en el PLAN antes de tocarlo.

---

## 2026-09-14 · Las lecturas también hablan en objetos: la ruta responde por sí misma, el módulo por lo suyo, la vista previa cuenta paradas

**Contexto**: tras el inventario de lecturas, Juan: "corrige estos otros casos; la idea es hacer de alto nivel, ya no interactuar con primitivos y llamar a los objetos usando los primitivos o instanciarlos con primitivos, tanto en escritura y lectura".

**Ajuste al dominio** (`Golem`, `Routes.Route`, `Routes.Preview` nuevo):
- Diecisiete lecturas por `id` del golem eran duplicados de propiedades de `Route`: fuera. El host y el panel preguntan a la ruta encontrada (`g.Find(@id).StopsLeft`), a la ruta en curso (`g.Next()`, que reemplaza `NextId`, `HeadingX`, `HeadingY`) o a las pendientes (`g.PendingRoutes()` en vez de `PendingIds`, así el `foreach` de "let go" es `foreach (route in g.PendingRoutes()) { route.Abandon(@reason); }`). `Route.ReadStatus()` es `Status`; `LegsAhead` devuelve una lista; `IsStopAhead(Position)`.
- La telemetría entra como objeto construido en la consulta: `FitsAt(Position)`, `HasRoomAt(Position)`, `KnowsWallAt(Position)`, `DistanceLeft(Position)`, `SecondsLeft(Position)`, `Road(Route, Position)`, `Plan(Route, Position)`, `RoadPast(Route, who, Pose)`, `PlanPast`, `HasNewerFollowing(Route)`, `PlannedEnd()` devuelve la `Position`.
- Lo que responde un módulo se le pregunta al módulo, que ya es global: fuera del golem `IsOnMap`, `PlaceAt`, `KnowsPlace`, `PlaceCount`, `PassageCount`, `Places`, `KnowsObstacleAt`, `HeardBumpNear`, `NextHandle`. En su lugar `map.IsOnMap(Position(…))`, `map.ZoneAt(…)`, `map.Knows(@area)`, `map.ZoneCount`, `collisions.KnowsAt(…)`, `collisions.HeardNear(…, since)`. Se quedan en el golem las lecturas donde entra el CUERPO (`FitsAt`, `HasRoomAt`, `KnowsWallAt`, `Distance`, `DistanceLeft`…) o la flota de rutas (`Knows(id)`, `HasPendingMission`, `Pending`, `Total`, `NewestFollowingId`).
- `Preview` es un objeto: `g.Preview(Position from, bool choosesOrder)` devuelve una vista previa a la que se le cuentan las paradas una a una (`Then(Position|Area)`) y que responde `Legs()`. Antes recibía dos arreglos `double[] xs, ys`; ahora el controller escribe la consulta con la misma forma que el encargo:
```
{ preview = g.Preview(Position(@fx, @fy), @cover);
  preview.Then(map.Find(@area1));
  preview.Then(Position(@x2, @y2));
  foreach (legs in preview.Legs()) { print legs.Kind 'kind', … } }
```
El bloque entre llaves también en la consulta: la variable `preview` muere con él.

**Ajuste al host y al panel**: `ReadPlan` lee `g.Next().Id/.IsRouted/.StopsLeft/.Following/.Bumps/.BumpedSinceRoute/.Paused`; los `Check` dicen `g.Knows(@id) && g.Find(@id).IsPending()`; `/progress` usa `Position(@x, @y)` y `map.ZoneAt`; `/state` `g.Next().NextLeg.At.X`; `/forget` `collisions.KnowsAt(Position(@x, @y))`; los botones de consulta del panel preguntan al módulo. Tests: 55 verdes, reescritos por expresiones regulares (`g.Plan(1, 2.0, 9.5)` → `g.Plan(g.Find(1), Position(2.0, 9.5))`, `g.IsRouted(1)` → `g.Find(1).IsRouted`, …).

**Observación en vivo** (journals actuales, compatibles; cero reinicios, cero errores de escritura): encargo de dos paradas por la vista previa nueva (`http 200`, `stopsLeft 2`), `/progress` con `distanceLeft 11.19` y `here west`, pausa y reanudación por `g.Find(@id).Paused` (segunda pausa rechazada: "the route is already paused"), llegada a las dos paradas. Red no encontró camino al norte "past 17 marks": la caja del centro sigue puesta y tres golems han chocado con ella toda la tarde; es la realidad, no un defecto.

**Conclusión**: la superficie del golem quedó en lo que solo el golem sabe: su cuerpo y su flota de rutas. Todo lo demás lo responde el objeto al que pertenece. Regla escrita en CLAUDE.md. **Pendiente**: `Plan`/`PlanPast` son texto para humanos (los lee el laboratorio en 31 tests); el operador podría verlos en el panel en vez de la lista de puntos cruda.

---

## 2026-09-14 · El golem no envuelve nada: las últimas lecturas redundantes se van

**Contexto**: Juan, tras el paso anterior: "de estos query o lecturas me estoy preguntando, ¿son necesarios o también podemos reducirlos?"

**Observación previa (motor)**: sondeado con `/query` en blue antes de tocar código — `collisions.All().Count` → 3, `g.PendingRoutes().Count` → 0, `body.Radius.InMeters` → 0.25, `map.IsWallAt(Position(4.0, 8.5), 0.3)` → true; pero `map.Zones.Count` → *"Unknown property or method 'Count'"*: la DSL lee `.Count` de una lista que devuelve un método (`IReadOnlyList`), no de una propiedad `IEnumerable`. Regla anotada en CLAUDE.md.

**Ajuste al dominio** (`Golem`): fuera once envolturas — `Radius`, `Speed`, `LingerAfterTold` quedan privadas para las sumas del propio golem (el planificador, el reloj), y el journal lee `body.Radius.InMeters`; fuera `MarkCount`, `ObstacleCount`, `Obstacles`, `ThingCount`, `MetCount`, `HeardBumpCount` (el módulo `collisions` responde), `KnowsWallAt` (`map.IsWallAt(Position, tolerancia)`), `Plan` y `PlanPast` (`g.Road(…).AsPlan()`); `Pending()` y `Total()` se vuelven `g.PendingRoutes().Count` y `g.Routes().Count` (lectura nueva `Routes()`, la lista entera). Host, panel (`/state`, `/progress`, `/obstacles`, botones de consulta) y tests reescritos; 55 verdes.

**Observación en vivo** (journals compatibles; cero reinicios, cero errores): `/state` `{"pending":0,"total":9}`, `/progress` con velocidad y espera leídas del cuerpo, `/obstacles` con `collisions.All().Count`; blue → storage llegó (`mission 10 reached the stop (9.0, 9.5)`). Un encargo a living fue rechazado con "no road … past 17 marks": la flota guarda una cosa de un solo vértice en el pasillo west y otra en el east, además de la caja del centro (15 vértices); son marcas de toques entre cuerpos de la tarde que no se retractaron, y cierran los dos pasillos. Es el estado del mundo y del journal, no de las lecturas; el operador las olvida con `/forget` si la caja no está.

**Conclusión**: la superficie de lectura del golem quedó en veinte miembros, cada uno con el cuerpo o la flota de rutas dentro de la respuesta. Lo demás lo responde el objeto dueño del dato. Con esto se cierra el inventario del 14-sep: escrituras en objetos y validadas, lecturas en objetos y sin envolturas. **Pendiente**: las dos marcas sueltas de los pasillos merecen mirarse — un toque entre cuerpos que no concluyó `Met` deja una marca que nadie retracta (el caso de la palabra tardía más allá de los 12 s).

---

## 2026-09-14 · Dos objetos del mismo tipo no pueden ser el mismo; la posición gana la altura

**Contexto**: Juan: "si recibimos dos objetos ya validamos que no sean nulos, pero también que no sean el mismo objeto, al menos en los que reciben dos del mismo tipo: `Area a, Area b`, que a y b no puedan ser el mismo". Y aparte: "el constructor de Position debería manejar Z también; cuando sean dos parámetros Z es cero, pero poder manejar la tercera dimensión para un futuro".

**Inventario**: 21 miembros del dominio reciben dos o más objetos del mismo tipo. En 17 el mismo objeto dos veces es un error del que llama y ahora se rechaza con `ReferenceEquals` tras las guardas de null: los pares de áreas, los dos extremos de un segmento, el punto del toque y el del compañero, los dos puntos de un cruce. En 4 el mismo punto es legítimo y se deja: `RoutePlanner.Road/RoadLength(from, to)` (el test "already there: the stop alone" pide una ruta de una parada desde la parada misma), `EdgeCost.Between` (costo cero) y `Leg(at, approach, exit)` (`Leg(at, name)` pasa el mismo punto tres veces a propósito: un tramo que no es puerta se acerca y sale por su punto). Identidad, no igualdad de valor: dos `Position` con las mismas coordenadas son dos objetos y `Segment` los acepta; lo que se rechaza es el mismo objeto pasado dos veces.

**Ajuste al dominio** (`Golem`, `Map`, `MapLayout`, `Passage`, `Collisions`, `HeardBump`, `Segment`, `Position`): las 17 guardas con mensaje uniforme `"Tipo.Método: 'a' and 'b' are the same area|point"`; los creadores por nombre `Map.Door(string, string)` y `Open` rechazan el área consigo misma ("area 'kitchen' has no door to itself"); `Passage.Joins` pierde una comprobación de null redundante. `Position(x, y, z)`: la forma de dos coordenadas delega en `z = 0`; `DistanceTo` mide en el espacio (en el piso, la del plano); `Along` y `Moved` conservan la altura; el layout, que tiende paredes y zonas sobre el plano, sigue leyendo x e y. Tests: 57 verdes (`TwoObjectsOfOneKind_MustBeTwoDifferentObjects`, `APosition_LivesOnTheFloorUnlessToldItsHeight`).

**Observación (motor)**: por `/query`, `Position(4.0, 9.5).Z` → 0.0 y `Position(4.0, 9.5, 1.2).Z` → 1.2 (dos constructores del mismo nombre, elegidos por aridad). Pero `map.Connects(map.Find('kitchen'), map.Find('kitchen'))` responde *"Exception has been thrown by the target of an invocation"*: dentro de una consulta, el rechazo de un método pierde su razón igual que el de un constructor en un comando. La frase del dominio llega entera solo como error de escritura de un comando o en un test de C#. Anotado en CLAUDE.md.

**Verificación en vivo**: flota rehidratada sobre los journals existentes (`Position(x, y)` sigue siendo válido), blue → north llegó; cero reinicios, cero errores.

**Conclusión**: la regla es de identidad de objetos y va donde el mismo objeto dos veces no puede significar nada; donde sí significa (llegar a donde ya se está) no va. La tercera coordenada entra sin costo hoy y evita reescribir la geometría el día que un cuerpo suba.

---

## 2026-09-14 · Retroceder más y rodear la figura: la caja del centro se pasa con dos choques, no con cinco

**Contexto**: Juan, mirando el mapa del panel: "cuando choca debería poder retroceder un poco más y girar más a la derecha o izquierda para poder pasar el obstáculo que tiene enfrente, porque genera muy continuos los vértices de cada bump en el mapa, muy juntos". Los registros de la tarde lo confirmaban: marcas a 0.1–0.3 m unas de otras sobre la misma cara de la caja (5.16, 5.28, 5.46, 5.58, 5.83).

**Diagnóstico**: dos causas. (1) El navegador retrocedía solo hasta soltar el contacto; el cuerpo quedaba pegado a su marca, y para que el planificador pudiera arrancar desde ahí existía la relajación del primer tramo ("puede rozar una marca a 0.2 m"): ese roce era el siguiente choque. (2) El planificador rodeaba cada MARCA (anillo a 0.68 m), no la COSA: una marca "sabe" 0.25 m a cada lado y la caja mide 0.7; el rodeo pasaba junto a la marca conocida y encontraba la caja 0.3 m más allá. Las colisiones ya derivaban la figura (marcas unidas a menos de 1 m), pero el planificador no la usaba.

**Ajuste al dominio** (`Robots.Body`, `Touches.Thing`, `Touches.Collisions`, `Routes.RoutePlanner`, `Geometry.Rectangle`, `Geometry.Segment`):
- `Body(radius, speed, linger, retreat)`: cuánto retrocede tras un toque es del cuerpo, no del host; `retreat = Meters(0.6)` (dos radios y una mano: fuera del margen que el planificador guarda alrededor de una marca).
- `Thing.Extent(margin)`: la caja alrededor de todas las marcas de la cosa, crecida por `MarkReach + margin`; `Collisions.Figures(radius)` la infla por el radio del cuerpo — donde el centro del cuerpo no puede ir. `Collisions.Blocks(Position|Segment)` miran figuras (`Rectangle.Contains`, `Rectangle.IsCrossedBy(run)`, nuevo `Segment.Crosses`). Se probó crecer la figura además con la separación media entre marcas ("lo que la cosa mostró"): dos marcas a 0.6 m cerraban una puerta a 0.6 m del borde real — demasiado; se descartó: la caja ya toma el ancho entre las marcas extremas.
- `RoutePlanner`: los rodeos son las esquinas de cada figura (+0.08), no ocho puntos por marca; `Sees` rechaza un tramo que entra en una figura; si el ARRANQUE está dentro de una figura, ese primer tramo se juzga marca por marca con las reglas viejas (la normal de la marca, salir por donde vino, nunca atravesar). La relajación "rozar a `radius - 0.05`" sobrevive solo ahí.
- Tests: 57 verdes. `AMarkKnowsItsNormal_TheSideTheBodyCameFromIsFree` se reescribió como `AMarkIsAFigure_TheBodyKeepsABerthAroundIt_AndTheRetreatStandsClear`: 0.4 m detrás de la marca ya NO es libre (de ahí venía el siguiente choque), 0.85 m (toque + retirada) sí; con una segunda marca a 0.3 m, una sola figura. `ABodyStandingAmongMarks…` movió sus dos marcas a 1.2 m de la puerta: a 0.6 m el modelo de discos pasaba por 3 cm y el de caja no; el hueco real (0.6 m para un cuerpo de 0.5) no cumple los márgenes.

**Ajuste al host** (`DiffDriveNavigator.BackOffAsync`): marcha atrás hasta soltar el contacto y luego hasta haber retrocedido `body.Retreat.InMeters` desde la pose del toque (6 s como máximo; para si toca otra cosa). `Program.cs` le pasa `flow.Retreat()`.

**Observación en vivo** (journals anteriores en `journal-legacy-20260914-retreat/`; caja del centro puesta desde el kiosko):
```
pass 1  blue north → south:
  touched crate_center at bearing -0° — the touch lands at (5.50, 5.87)
  takes the road around@4.82,6.55 > around@4.82,5.19 > center~south@5.22,3 > stop@5.5,1.5
  touched crate_center at bearing 74° — the touch lands at (5.15, 5.85)      ← la esquina NW: la figura de una marca (±0.6) era 0.08 corta
  takes the road around@4.47,6.55 > around@4.47,5.17 > center~south@5.08,3 > stop@5.5,1.5
  reached the stop (5.5, 1.5)                                                 → 2 choques, marcas (5.50, 5.87) (5.15, 5.85), una figura
pass 2  blue south → north: 0 choques, pasó por el este de la figura conocida
```
Antes de hoy: 4–5 marcas seguidas para pasar la misma caja. Con la retirada, ningún choque volvió a caer a 0.1 m del anterior: el segundo cayó 0.35 m al lado, en la esquina que la primera figura no cubría, y la segunda figura ya tomó el ancho.

**Tropiezos del laboratorio (infraestructura, anotados)**: (1) Gazebo dejó de atender servicios dentro del contenedor ("NodeShared::RecvSrvRequest() error sending response: Host unreachable"): el nodo de palancas decía "crate in" y el mundo no creaba nada; `ign model --list` respondía vacío. Se resolvió reiniciando el contenedor del simulador. (2) `ros2 topic pub` desde `docker exec` no llega al nodo de palancas (descubrimiento DDS de un proceso nuevo); el botón del kiosko por rosbridge sí. (3) Al reiniciar el simulador, el golem que estaba conduciendo murió con `WebSocketException ('Aborted')` sin atrapar en `DriveAsync` y Docker lo revivió (`RestartCount 1`, rehidratado en la entrada 11): la membrana debe sobrevivir a la pérdida del cable sin matar el proceso. Pendiente del host.

**Conclusión**: el cuerpo declara su retirada y el planificador respeta la figura; la primera pasada cuesta dos choques porque la primera figura es la de una marca sola (0.6 m de holgura) y la caja la excede por 8 cm en la esquina. Si se quiere una sola, la figura de una marca sola podría presumir un poco más de ancho (0.35 en vez de 0.25 de alcance), a costa de rodeos más amplios alrededor de cosas pequeñas: decisión de Juan.

---

## 2026-09-16 · El panel muestra cada acto en su línea

**Contexto**: Juan: "podrías al panel ajustarle los saltos de línea de los scripts del journal para que se vea mejor".

**Ajuste al panel** (`panel.html`, solo presentación; el journal guarda la plantilla plana, `JournalPeek.OneLine` no cambia): una función `pretty` tiende cada sentencia en su propia línea — la llave abre y cierra su línea, dos espacios por nivel, lo expuesto junto al acto (`Expose 7 'rid';`) en líneas propias, los `tell` encadenados uno por línea, las comillas respetadas para no partir un literal. El texto se escapa antes de insertarlo (antes iba crudo al `innerHTML`). De paso, las filas del carril de eventos del host, que no tienen sentencia, muestran su nota a todo el ancho en vez de comprimida en la columna de la etiqueta. **Ajuste al dominio: ninguno.**

**Verificación**: panel de blue tras un encargo al south —
```
#35  {
       point = map.Find('south');
       route = g.Visit(point);
       via1 = Position(5.8459257087703,8);
       route.Via(via1);
       …
       route.Stop(point);
     }                                          action 7
#36  {
       route = g.Find(7);
       point = Position(5.5,1.5);
       route.Reach(point);
     }
     Expose 7 'rid';
     Expose 5.5 'rx';
     Expose 1.5 'ry';                            action 2
```

---

## 2026-09-16 · Una cosa a la vez: el journal ordena, el cuerpo reporta, y la orden es un `print` empujado

**Contexto**: Juan, revisando `RunAsync`: "debería ser un comando ejecutado que produce un print del siguiente punto que debe alcanzar y una vez alcanzado pide el siguiente… al `GolemPerformance` hay que ponerle el `perf.OutputTarget` apuntando al websocket… ya solo queda responsabilidad del robot decir si logré llegar o girar al punto indicado; una cosa a la vez, no una cola de acciones acumuladas". Y la corrección previa: la ruta debe traer también el ángulo al que girar; esos cálculos no estaban en el dominio.

**Lo que dice el motor** (guías `puppeteer-materialized-views`, `puppeteer-reactions`; `IOutputSink.cs`): el `print` de un comando vuelve a quien lo ejecutó (pull). Lo que se empuja a un `OutputTarget` es el `print` del `.Program.Emit` de una Reaction — "pull vs push es propiedad del destino, el script es el mismo". El sink recibe un `PushDocument` (documento renderizado, nombre de la reacción, `EntryId`, las capturas). El empuje es efímero: sin outbox, sin reintento. El formateador por defecto del push es Toon; se pasa `JsonFormatter` para parsear.

**Ajuste al dominio** (`Routes.Leg`, `Trajectory`, `Route`, `Golem`): `Leg.Heading`/`HasHeading` — la trayectoria da a cada tramo, salvo el primero, el rumbo desde la salida del anterior (`Leg.WalkedFrom`); el primero se camina desde donde esté el cuerpo, cuya pose es telemetría. `Route.Order` es lo que la ruta pide ahora: `hold` si está pausada, `decide` si no tiene camino o un choque lo interrumpió, `leg` si no; `IsWalkable`. `Route.Reach(point)` acepta cualquier punto del camino por delante (una puerta, una frontera, un punto, una parada), mueve el cursor y solo cuenta parada si lo es; `IsLegAhead`. `g.Aside(Pose)`: el paso de cortesía (derecha si cabe, si no izquierda, la misma pose si ninguna).

**Ajuste al host** (`Orders.cs` nuevo, `GolemChoreography`, `Messages`, `RoadLeg`, `GolemController`, `Program`): ocho reacciones `next-order-*`, una por forma de acto que cambia la orden (`order` del encargo y del recálculo, `rid/rx/ry` de la parada, `vid/vx/vy` del punto pasado, la forma del `Bump`, `held`, `resumed`, `ended`, `letgo`), todas con el mismo `Emit`:
```
if (g.HasPendingMission()) { print g.Next().Id 'route', g.Next().Order 'order'; }
if (g.HasPendingMission() && g.Next().IsWalkable) { print g.Next().NextLeg.Kind 'kind', … .At.X 'x', … .Heading 'heading', g.Next().Following 'following', g.Next().StopsLeft 'stopsLeft'; }
```
`perf.OutputTarget(orders, new JsonFormatter())`. `OrderSink` guarda la ÚLTIMA orden (una nueva sustituye a una no tomada), despierta al conductor y avisa para soltar la conducción en curso; se arma después de hidratar (lo que las reacciones repasan al arrancar es historia). El conductor (`RunAsync`, ya no un bucle de misión): toma una orden — `hold` → detiene el cuerpo y espera; `decide` → pregunta `g.Road(route, Position(pose))` y escribe el camino; `leg` → camina el tramo (aproximación y salida si es puerta) y reporta: `passed` (`route.Reach(via)` con `vid vx vy`) o `reached` (parada, `rid rx ry`); un toque sigue el protocolo de antes (pared → `Graze` y la misma orden otra vez; cosa → `Bump`, que hace la orden `decide`; compañero → `Met` y `RoadPast`). Si nada llega en 2 s, pregunta la misma proyección como consulta (`AskOrder`). Fuera: los tramos en memoria, el cursor, `lane`, `StepAsideAsync` con seno y coseno, `FeelForAWayPastAsync`, `HoldWhilePausedAsync`, `WatchForPauseAsync`, `ReadPlan`, `RoadAhead`. La coreografía pasó de 1420 a 1170 líneas quitando 560 e insertando 390 (Orders.cs aparte).

**Observación en vivo** (journals anteriores en `journal-legacy-20260916-orders/`; la caja del centro seguía en el mundo):
```
blue → garage
  { point = map.Find('garage'); route = g.Visit(point); via1 = …; route.Via(via1); … route.Stop(point); }   expose 1 'order'
  route 1: heading to the passage north~center (5.5, 8.0)                         ← primera orden, sin rumbo: desde donde esté
  { route = g.Find(1); via = Position(5.5,8); route.Reach(via); } Expose 1 'vid' …   ← el reporte
  route 1: heading to the passage center~south on heading -1.43 (6.2, 3.0)         ← la siguiente, con el rumbo del dominio
  { route = g.Find(1); touch = Pose(5.79,5.85,-1.57); route.Bump(touch); } …       ← la caja
  route 1: another road around@6.47,6.53 > center~south@6.42,3 > south/garage@7,1.5 > stop@9,1.5   ← 'decide' cumplida
  route 1: heading to a point (6.5, 6.5) / passed / heading … on heading -1.58 / passed / … / reached the stop (9.0, 1.5)
pausa y reanudación: 'hold' detuvo el cuerpo (pose idéntica a los 3 y 8 s); Resume empujó el tramo y llegó
encuentro: blue entró al garage donde red estaba parado — red "touched while standing", "making room: stepping to (8.6, 1.8)"
  (el paso lo eligió g.Aside), blue concluyó Met cuatro veces y llegó; cero reinicios, cero errores en los tres golems.
```
Con la caja: un solo choque para pasarla en esta corrida (la retirada y la figura del lunes, más el rumbo del dominio).

**Conclusión**: el diálogo es exactamente el de Juan: el journal dice "ve a este punto con este rumbo", el cuerpo va y dice "llegué" o "toqué", y esa palabra trae la siguiente orden. El host ya no sabe el plan. Dos consecuencias asumidas: el journal escribe cada punto del camino (cinco filas por encargo típico) — el "camina en silencio" del 10-sep queda superado por el "una cosa a la vez" del 16-sep —, y el empuje efímero se cubre preguntando la misma proyección cuando nada llega.

**Pendiente**: (1) `decide` tras un choque la cumple el host preguntando `g.Road` desde la pose: es telemetría, se queda; (2) el primer tramo de un camino no trae rumbo — si se quisiera, el encargo tendría que escribir el punto de partida; (3) la caída por `WebSocketException` al reiniciar el simulador sigue sin atrapar.

## 2026-09-16 · El script entero en el controller: la orden es el `print` del propio comando, y el `expose` solo donde un tell lo necesita

**Contexto**: dos observaciones de Juan sobre la versión de la mañana. Primera: "hay que quitar los `expose @id rid, @x rx, @y ry;`, tengo entendido que no aportan nada de valor; ¿se pueden quitar todos?". Segunda: "el código no está bien, uno esperaría que todo esté ordenado en el `GolemController`, ahí tenemos acceso al actor ya configurado y ahí se ve el script entero relacionado a la acción, con los `print` del punto al que deberá moverse; pero todo lo tenemos metido en una clase de coreografía; refactoricemos".

**Laboratorio 1 — ¿qué captura una Reaction de un comando entre llaves?** (`GolemTest/ReactionPatternLabTests.cs`, resultados en `bin/…/lab-patterns.txt`). Sobre un actor con los releases reales, definir una reacción por patrón y ejecutar un encargo, una parada, una pausa, una reanudación, un choque y un fallo:
```
definen: [_:Route].Reach(_)  [_:Route].Reach(_:Position)  [_:Route].Stop(_)  [_:Route].Via(_)  [_:Route].Then(_)
         [_:Route].Bump(_)  [_:Route].Pause()  [_:Route].Pause  [_:Route].Resume()  [_:Route].Fail($why)
         [_:Golem].Visit(_)  [_:Golem].Visit($p)  [_:Golem].Visit(_:Position)  [_:Golem].Find($id)
rechaza: [_:Golem].Visit(_, _)  ("no overload accepts that number of arguments") y todo patrón anidado
         (constructor dentro de la llamada, asignación dentro del patrón).
disparan en una corrida: Stop(_)  Resume()  Then(_)  Visit(_:Position)      — con las capturas vacías (bindings[])
callan en la misma:      Reach(_)  Via(_)  Pause()  Bump(_)  Fail($why)  Find($id)  Visit(_)
```
*Observación*: el acto mismo, sobre una variable local del bloque, ES un patrón válido: un `expose` que solo servía de gatillo (`vid vx vy` en `Reach`, `order`, `held`, `resumed`, `ended`, `letgo`) no aporta nada. Pero el disparo fue inconsistente entre corridas idénticas, y una reacción sobre el acto captura VACÍO: para llevar un valor a un tell (`PointVisited` necesita `x, y`; `BumpedAt` la pose y el nombre) el `expose` sigue siendo el único canal, porque el matcher no captura objetos (Fase 0, P3).

**Laboratorio 2 — ¿a quién vuelve el `print` de un comando?** (`GolemTest/CommandPrintLabTests.cs` → `lab-print.txt`):
```
1 encargo + print (PerformCommand):              {"route":1,"order":"leg","kind":"door","x":0.75,"y":8.0,"hasHeading":false,…}
2 Reach + expose + print (CheckThenCommand):     {"route":1,"order":"leg","kind":"stop","x":2.0,"y":1.5,"hasHeading":true,"heading":-1.38}
3 Check rechazado:                               {"EWI":[{"Error":"not paused"}]}
4 Pause + print:                                 {"route":1,"order":"hold"}
5 última parada alcanzada (nada pendiente):      ''
```
*Observación*: el comando devuelve su `print` a quien lo ejecutó, en el mismo `PerformCheckThenCommand`, en el momento de escribir; el rechazo vuelve con las palabras del dominio. No hace falta que una Reaction lo empuje a un `OutputTarget` (la mañana lo hacía así, y el empuje era efímero y sin garantía entrada a entrada).

**Conclusión**: la orden siguiente es el `print` con el que termina el propio comando que la cambió. Eso deja el script ENTERO — el acto y su `print` — en un solo lugar, el controller, como pidió Juan; las reacciones quedan solo para hablar (paper 04: el tell es una Reaction) y los `expose` solo donde un tell captura un valor.

**Ajuste al host** (sin cambio de dominio): `GolemController` tiene todo script del journal — `NextOrder` (`g.Next().Id 'route', g.Next().Order 'order'` + el tramo si `IsWalkable`), el encargo (`/move`, `/cover`: primero los centros de las áreas, la vista previa `Preview`, luego `{ point1 = map.Find(@area1); route = g.Visit(point1); … via1 = Position(@lx1, @ly1); route.Via(via1); route.Stop(point1); }` + `NextOrder`), `/pause`, `/resume`, `/forget`, y los que ejecuta el conductor: `Passed`/`Reached` (`{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }` + `NextOrder`; `Reached` conserva `rid rx ry` para `echo-reached`), `Bumped`, `TouchedStanding`, `Grazed`, `Met`, `Decided` (`RoadLeg.Script(legs)` + `NextOrder`), `Failed`, `Abandoned`, `LetGo`, y las captaciones `Uptake*`. `Choreography/Orders.cs`: `Order` (route, what, kind, name, punto, aproximación, salida, rumbo, following, stopsLeft), `Answer` (la orden o el rechazo del Check, nunca ambos) y el buzón `Orders` (gana la última; `Arrived` suelta la conducción en curso; `Drop` para el let-go). `GolemDriver`: toma UNA orden — `hold` detiene, `decide` pregunta `g.Road(route, Position(pose))` y escribe `Decided`, `leg` camina el tramo con su rumbo — y reporta; si nada llega en 2 s pregunta `NextOrder` como query (`AskOrder`: el punto contado que se vuelve `Follow` cambia la orden sin script mío). `GolemSpeech`: `echo-bumped/touched/met/forgotten/reached` y `Listen()`. Se van `GolemChoreography.cs`, `Messages.cs`, `OrderSink`, `perf.OutputTarget`, las ocho `next-order-*`.

**Observación en vivo** (journals conservados: solo cambian reacciones y exposes; tres golems, cero reinicios, cero excepciones):
```
blue → living con la caja del centro puesta desde el kiosco (la marca previa olvidada con /forget):
  route 8: heading to the passage north~center (5.5, 8.0)                       ← la orden que devolvió el encargo
  route 8 passed the point (5.5, 8.0) (entry 137)                                ← Passed; su print = la siguiente
  route 8: heading to the passage center~south on heading -1.71 (4.8, 3.0)
  [navigator] touched crate_center at bearing 7° off the nose — the touch lands at (5.24, 5.85)
  route 8 bumped into something at (5.2, 5.8) (entry 139)                        ← Bumped; su print = 'decide'
  nobody else bumped there and then — the mark stands; reconsidering for 12s
  route 8 takes the road around@4.56,6.53 > center~south@4.59,3 > living/south@4,1.5 > stop@2,1.5 (entry 144)
  passed (4.6, 6.5) · passed (4.6, 3.0) on heading -1.56 · passed (4.0, 1.5) · reached the stop (2.0, 1.5) (entry 148)
pausa: 'hold', pose idéntica a los 3 y 7 s; segunda pausa rechazada "the route is already paused"; resume: siguió.
let go a mitad de tramo (route 9): "let go of every pending route (entry 151)" y nada más — antes salían dos
  "refused: route is not pending" porque la conducción en curso seguía y reportaba sobre la ruta abandonada;
  ahora el let-go suelta la conducción (`Orders.Drop`).
```
Un choque para pasar la caja, cuatro tramos después, llegada. Igual que la versión de la mañana, con la mitad de las piezas.

**Ajuste al dominio**: ninguno — el laboratorio fue del motor y del host. **Pendiente**: la caída por `WebSocketException` al reiniciar el simulador; el primer tramo sin rumbo; las marcas viejas de los corredores oeste/este.

## 2026-09-16 · La ruta guarda sus puntos: el journal solo dice lo siguiente — gira, corre — y las plantillas son fijas

**Contexto** (Juan, sobre los scripts del encargo): "el problema es la construcción de los scripts del journal: si resulta que necesitamos especificar cada uno de los `pointN`, pero eso ya no es así; ahora uno crea el visit, crea el objeto en memoria con todos los puntos, pero nunca los imprime, es algo que él tiene internamente y solo imprime el punto target… tampoco los `viaN`: la route tiene la lista de los puntos, y cuando `route.Reach` internamente se mueve al siguiente punto, el print dice todo lo necesario, que si tiene que girar entonces gira, luego ros le dice ya giré, luego se escribe en el journal que alcanzó a girar y esa misma escritura hace print de lo siguiente que hará, y así hasta completar todas las rutas que proponía la ruta… los scripts del controller deberían seguir una estructura bien redactada con parámetros, pero no armarse a punta de StringBuilder, eso terminaría creando demasiados actions; la idea es concentrar las acciones principales en unas cuantas".

**Lo que cambia de doctrina**: el 10/14-sep se escribía el camino entero como puntos en la entrada del encargo (`via1… route.Stop(point)`), por el paper 05 ("si el planificador cambiara, una rehidratación decidiría otro camino"). Hoy la ruta decide y GUARDA su camino por dentro. Sigue siendo determinista en la reproducción: el planificador lee el layout y las colisiones, ambos estado del journal, y el origen del encargo se escribe (`from = Position(@fx, @fy)`); lo que queda expuesto es el cambio de código del planificador, un canje aceptado en el spike. A cambio: el journal no lista puntos, el encargo es UNA plantilla fija, y la primera pierna tiene rumbo (antes no, porque el origen no se escribía).

**Ajuste al dominio** (`Routes.Route`, `Golem`, `Routes.Trajectory`, `Routes.Courtesy` nuevo; se van `Routes.Preview` y `Golem.RoadPast`):
- `Route(id, stop, following, choosesOrder, layout, collisions, radius)`: `Decide(from)` planifica desde un punto por las paradas que faltan (`Ordered`: un Cover deja al planificador elegir el orden y lo adopta como propio, así lo alcanzado y lo que falta se leen de una sola lista) y toma la trayectoria `WalkedFrom(from)` (la primera pierna gana su rumbo); `DecidePast(who, me)` antepone el paso de cortesía (`Courtesy.StepOutOfTheWayOf`, compartido con `g.Aside`); `Then(point)` solo antes de arrancar y vuelve a decidir desde el origen.
- El cursor: `Order` = `hold` | `decide` | `turn` (la pierna tiene rumbo y el cuerpo no ha girado) | `run`; `Turn()` marca el giro hecho y se rechaza cuando no toca ("asked no turn now"); `Reach(point)` avanza hasta esa pierna y **no salta paradas** (`AheadAt`: los pasos y puertas intermedios pueden quedar sin reportar, una parada no) — la prueba `SeveralStopsWithoutARoad_AreReachedInOrder` lo exigía y tenía razón.
- `g.Visit(from, point|area)`, `g.Cover(from, point|area)` (rechazan `from` y `stop` como el mismo objeto, como toda pareja del mismo tipo), `g.Follow(point)` sin origen → `decide`; `g.Newest()`; `Road` queda como lectura; `route.AsPlan()`.
- Pruebas: 59 verdes; las que escribían el camino a mano (`Route(id, plan)`) ahora `Decide(id, x, y)`; la de "el camino se escribe en puntos" pasó a ser `TheErrand_DecidesItsWholeWayInside_AndPrintsOnlyTheNextThing` (turn → run → Reach → turn).

**Ajuste al host** (`GolemController`, `GolemDriver`, `Orders`, `INavigator`/`DiffDriveNavigator`; se va `RoadLeg.cs`): cada endpoint escribe su script completo, sin métodos que lo generalicen (Juan, misma tarde: "el controller en el endpoint debe explicar el script como tal") y en bloques verbatim de una sentencia por línea: `/move` y `/cover` su `{ from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }` + `NextOrder` (el `from` es la pose, o el fin de la última ruta pendiente si el golem está ocupado) y, por cada parada adicional, su `{ route = g.Find(@id); point = …; route.Then(point); }` (una entrada, con el handle leído de `g.Newest()`); `/pause` y `/resume` cada uno con sus dos `Check` y su acto; la entrada del operador es un cuerpo JSON tipado (`Requests.cs`: `ErrandRequest`, `PointRequest`, `QueryRequest`, `ResetRequest`) que se valida antes de ejecutar nada — sin `[FromQuery]`; `Turned` (`{ route = g.Find(@id); route.Turn(); }`), `Passed`/`Reached`, `Decided` (`{ route = g.Find(@id); from = Position(@x, @y); route.Decide(from); }`), `DecidedPast`. El conductor: `turn` → `TurnToAsync(heading)` (giro en el sitio hasta 3°, un toque lo termina como colisión igual que a una carrera), `run` → como antes; `decide` → `Decided` desde la pose, y si el dominio rechaza dentro del comando (no hay camino) la ruta falla con la razón del planificador (`Safely`).

**Observación en vivo** (journals reiniciados en `journal-legacy-20260916-puntos/`; tres golems, cero reinicios, cero excepciones; la caja del centro seguía en el mundo):
```
blue → living desde north (route 2), con pausa a mitad:
  { from = Position(5.5, 9.5); point = map.Find('living'); route = g.Visit(from, point); }   → {"order":"turn","heading":-1.67,…}
  route 2: turning to heading -1.67 for (5.5, 8.0) · turned (entry 9) · heading to the passage north~center · passed (entry 11)
  turning -1.71 · turned (12) · heading to center~south (4.8, 3.0)
  [navigator] touched crate_center at bearing 8° — the touch lands at (5.24, 5.86)
  bumped (entry 14) · nobody else bumped there · another road — deciding the way from (5.2, 6.7) · decided (entry 20)   ← route.Decide(from)
  turning -2.85 · turned (21) · heading to the passage around (4.6, 6.5) · passed (22)
  turning -1.56 for (4.6, 3.0) · paused by the operator at (4.8, 6.6) [pose idéntica a los 3 y 8 s] · resumed · turning -1.56 · turned (27)
  passed (4.6, 3.0) (28) · turning · turned (29) · passed living/south (4.0, 1.5) (30) · turning 3.14 · turned (31) · reached the stop (2.0, 1.5) (32)
red → cover {kitchen, garage} desde living (route 1, dos entradas: Visit(from, kitchen) + Then(garage); eligió garage primero):
  living/south · south/garage · reached garage (9.0, 1.5) (entry 16) · vuelta por south/garage · toque en wall_south_e_2 (7.05, 2.09)
  tratado como cosa (bumped, entry 24) · decidió otra vía (30) · tocó a blue (7.0, 1.3) · Met blue · DecidedPast (43): aside (7.8, 1.1)
  · east/garage · … · kitchen/north (4.0, 9.5) · reached the stop (2.0, 9.5) (entry 61) · pending 0
```

**Conclusión**: el diálogo es literalmente el de Juan — la escritura del acto imprime lo siguiente, el cuerpo hace UNA cosa y la reporta — y el journal quedó más corto y legible: un encargo es una plantilla de una línea, cada parada más una entrada, y el camino vive en el objeto. El giro como acción propia costó un método del navegador y nada del dominio que no fuera un bit (`turned`).

**Observación posterior (misma tarde, tras el formato)**: blue (`/move` kitchen → storage, dos entradas) y red (`/cover` living, north) se cruzaron en la puerta kitchen/north desde lados opuestos: cada choque concluyó `Met`, cada uno escribió `DecidePast` con su paso a un lado (blue a (4.4, 9.9), red a (2.8, 9.0)), y volvieron a encontrarse en la misma jamba cuatro veces; blue falló "blocked by red after meeting it 4 times". El protocolo hizo lo suyo entrada a entrada; lo que falta es una regla de paso en una puerta (quién espera). Los dos `pause` seguidos: el segundo rechazado "the route is already paused"; `?place=attic` → "unknown area"; `?x=3&y=5` → "that point is nowhere on the map".

**Pendiente**: (0) dos cuerpos que entran por la misma puerta desde lados opuestos se ceden el paso en vano hasta agotar `MaxYields` — hace falta que uno espere (el dominio decide quién, p. ej. el de menor handle); (1) red trató un roce con `wall_south_e_2` como cosa (`Suspect` no lo reconoció como pared a 0.05 m de la puerta south/garage): revisar la tolerancia de `map.IsWallAt` junto a las jambas; (2) `Then` tras arrancar se rechaza — si el operador quiere añadir una parada a una ruta en marcha, será un encargo nuevo; (3) la caída por `WebSocketException` al reiniciar el simulador.

## 2026-09-16 · El `OutputTarget` es el robot: el `print` viaja por el websocket y el cuerpo reporta por un endpoint

**Contexto** (Juan): "veo en el código que aún no hacemos la implementación de que el `OutputTarget` sea el ROS que lo envíe por el websocket que tenemos hacia el robot… el print termina retornando los datos al controller en su return, pero a su vez ese objeto de formato también analiza el JSON y tiene un switch case con las acciones disponibles basado en el tipo de JSON, y dentro de esos case están los llamados que viajarán hasta el robot para cumplir lo que sea que tenga que hacer… nosotros por lo general queremos abstraernos del robot como tal: el robot solo es el cuerpo y nosotros le vamos diciendo qué es lo que debe hacer, y si terminó le dice al actor por medio de algún endpoint que ya terminó, para pedir el siguiente print con lo siguiente que debe realizar si aún tiene pendientes". De las dos formas propuestas (A: el robot es un nodo ROS; B: el servo sigue en C# detrás de los tópicos) eligió la A.

**Ajuste al host** (`Choreography/Robot.cs` nuevo, `Order.cs`, `GolemController`, `Requests.cs`, `Rosbridge`, `Program`; se van `GolemDriver.cs`, `Orders.cs`, `Navigation/`): el `Robot` es el `IOutputSink` del actor (`performance.OutputTarget(robot, new JsonFormatter())`) y a la vez a donde el controller entrega el `print` que devuelve cada comando (`robot.Obey(print)`). `Obey` analiza el JSON y hace `switch`: `hold` → `stop` al cuerpo; `decide` → `route.Decide(from)` desde la pose y se obedece el print resultante; `turn`/`run` → publica el mismo JSON en `/golem/<cuerpo>/order` con `within` (0.25 m; 1.0 m si es la última parada de un seguidor) y el cuerpo del journal (`speed`, `radius`, `retreat`). El cuerpo reporta en `POST /robot/turned|reached|touched|stuck` (cuerpos JSON validados); el endpoint escribe `route.Turn()` o `route.Reach(point)` con su script completo y devuelve la respuesta al `Robot`, que obedece el print. El protocolo de toques se mudó al `Robot` sin cambiar (2.5 s de escucha, 12 s de reconsideración, `Met`, `DecidedPast`, `MaxYields`); `deliberating` impide que el reloj de 2 s decida mientras se pondera un toque (la primera corrida decidió dos veces: una el reloj, otra el protocolo). El paso de cortesía y el "hacer sitio" son un `run` con `route` 0: el cuerpo reporta `reached` y no se escribe nada.

**Ajuste al simulador** (`sim/bridge/body.py` nuevo, `kiosk.sh`, `Dockerfile`, `docker-compose.yml`): un nodo rclpy por cuerpo, `body.py <cuerpo> <url del golem> [world|wheels]`, lanzado por `kiosk.sh` desde `GOLEMS=blue=http://blue-golem:8080;…` y `POSE_SOURCES=green=wheels`. Es el port del navegador en C#: controlador proporcional, giro en el sitio (±1.5 rad/s, mínimo 0.3, hecho a 3°), aproximación y salida de una puerta, atasco a los 3 s sin progreso, timeout a los 90 s; tras un toque retrocede a 0.4 m/s hasta 0.4 s sin contacto más la retirada declarada (máximo 6 s), calcula el punto del toque a un radio del centro creído en la dirección del bearing (media de las posiciones del contacto contra la pose verdadera, como la membrana) y reporta `touched` con la pose; parado, reporta un toque por segundo con `route` 0. La odometría de ruedas se ancla a la verdad una vez, y de nuevo cuando la orden `stop` trae `anchor` (tras un teletransporte).

**Observación en vivo** (tres golems, cero reinicios; los nodos del cuerpo arrancaron con el simulador: `body.py blue http://blue-golem:8080 world`, `red … world`, `green … wheels`; tópicos `/golem/{blue,red,green}/order` presentes):
```
blue → living (route 9): turning -1.89 · turned (entry 210) · heading to north~center · passed (211) · … · reached the stop (2.0, 1.5) (218)
  [body_blue] route 9: turn to heading -1.71 / run to (4.00, 1.50) / … — el nodo recibe cada orden y la ejecuta
red → garage (route 3): siete órdenes, siete reportes, llegada (entry 160); blue la siguió (route 10, PointVisited) y dio el paso
  de cortesía: [body_blue] route 0: run to (8.00, 1.00)
blue → north con la caja del centro puesta (route 11): bumped (7.0, 2.1) [la pared junto a south/garage, otra vez tomada por cosa]
  · decide DOS veces (el reloj y el protocolo) → corregido con `deliberating`
blue → living con la caja (route 12, tras la corrección): bumped (5.2, 5.9) (entry 266) · nobody else · deciding from (5.2, 6.8) (270)
  · turning -2.82 · skirting to (4.6, 6.6) · passed · center~south · living/south · reached (2.0, 1.5) (278). Un choque, un Decide.
```

**Conclusión**: el diálogo que Juan describió existe ahora físicamente: el `print` del comando vuelve al controller Y viaja al cuerpo como el mismo JSON; el cuerpo hace una sola cosa y avisa por un endpoint; el acto se escribe y su `print` es la siguiente orden. El host ya no tiene servo: un robot real solo tiene que hablar estos dos tópicos y estos cuatro endpoints. El `Robot` como `IOutputSink` deja además el camino abierto a que una Reaction emita el mismo `print` y llegue al cuerpo por la misma puerta.

**Ajuste al dominio**: ninguno. **Pendiente**: (1) la pared junto a las jambas sigue tomándose por cosa (`Suspect`); (2) la regla de paso en una puerta cuando dos cuerpos entran por lados opuestos; (3) green ancla su odometría en el nodo y en la membrana por separado: dos relojes, una misma verdad al arrancar — vigilar que no diverjan tras un teletransporte.

## 2026-09-16 · El choque es un interruptor: los motores paran, el cuerpo avisa, y la ruta mete la retirada y el rodeo como puntos de corrección

**Contexto** (Juan, noche): "desde el momento exacto en que el robot choca con algo, en su chasis es como un botón que se acciona: los motores se detienen inmediatamente y el robot envía el bump al endpoint de choques, y este termina registrando el bump en el journal, y lo mejor es que eso desencadena otro print con el retroceso que debe hacer, porque el recálculo de la ruta termina metiendo más puntos para llegar al punto al que iba: retroceder un poco y pasar al lado; esos son los nuevos puntos insertados en el trayecto… posiciones legacy vs posiciones de corrección; si las de corrección no resuelven de A a B se recalcula hasta encontrar camino". Y dos precisiones: "la interfaz del robot es extremadamente sencilla, solo es mover / girar / choque / llegué" y "el que decide todo debe ser el dominio".

**Ajuste al dominio** (`Routes.Route`, `Routes.Leg`, `Golem`, `Layouts.MapLayout`): `Bump(touch, me)` — el toque y dónde estaba el cuerpo mirando hacia dónde — marca y llama a `Correct(me, replan: true)`: `back = me.Along(me.Heading + π, retreat)` (la retirada del cuerpo, `Body.Retreat`, ahora en la ruta), una pierna `Leg.Retreat` ("back") y detrás el `Road` del planificador desde `back` por las paradas que faltan; si el planificador rechaza, queda solo la retirada y `Order` pide `decide` al acabarla (camino agotado sin completar). `Graze(at, me)` → `Correct(me, replan: false)`: retirada y las mismas piernas otra vez (sus rumbos se recalculan desde la retirada; `grazesOnLeg` se conserva). `Order` = `hold` | `decide` | `back` | `turn` | `run`; `IsWalkable` cubre `back`. `Reach` completa cuando `reached == stops.Count`. `Leg.IsCorrection`/`IsReverse`. `DoorGap` = `DoorWidth/2 − 0.15`. Pruebas: 63 verdes; `ABump_IsATouchAndAMarkAtOnce…` y `ATouch_InterruptsThePlan…` ahora esperan `Order == back` y `AsPlan()` empezando por `back@`; la paciencia con las paredes sobrevive a la retirada.

**Ajuste al robot y al host**: `body.py` para los motores al primer contacto y reporta `bump` con `ptheta`; no retrocede; una sola vez por contacto (`pressed`/`released_since`: pegado a lo mismo no es otro choque hasta que el sensor calla 0.4 s — sin esto, la orden que llegaba mientras seguía pegado disparaba otro choque en el acto, y un cuerpo parado contra la caja pedía un paso de cortesía cada segundo); orden `back` = reversa suave hasta el punto o hasta empezar a alejarse; `arrived {route}` para giro y punto. Endpoints `/robot/arrived`, `/robot/bump`, `/robot/stuck`. `Robot.BumpedAsync`: `Suspect` → `Grazed(at, me)` o `Bumped(touch, me)`; el print (`back`) se obedece en el acto y la ventana de 2.5 s corre mientras el cuerpo retrocede; `Met` → `DecidedPast`. `Robot.RunAsync` espera la pose antes de decidir al despertar.

**Observación en vivo** (journals archivados en `journal-legacy-20260916-toques/`; tres golems, cero reinicios):
```
1ª corrida — un error mío: el script de Bumped usaba @me para el nombre del golem y `me` para la pose → "Id me is already set as String":
  el choque se rechazaba, el reloj reenviaba `run`, el cuerpo volvía a chocar… (renombrado a @name).
2ª corrida — blue living → north (route 2): chocó en (3.9, 2.1) y, retrocediendo, en (3.9, 0.9): las JAMBAS de living/south,
  tomadas por cosas ("nothing on my map there") porque DoorGap = 0.8 m las cubría; dos marcas falsas, la ruta se fue por west
  y llegó a north. → DoorGap = 0.55 m; marcas olvidadas con /forget (red y green las olvidaron al oír a blue: 409).
3ª corrida — blue north → living con la caja del centro (route 3):
  heading to center~south (4.8, 3.0) · bumped into crate_center at (5.2, 5.9) heading -1.59 · the way corrected, backing off first (entry 256)
  backing off to (5.3, 6.7) · [body_blue] route 3: back to (5.28, 6.71) · backed off (entry 260)
  turning -2.93 · skirting to (4.5, 6.5) · passed (262) · turning -1.56 · center~south (4.6, 3.0) · passed (264)
  living/south (4.0, 1.5) · passed (266) · turning 3.14 · reached the stop (2.0, 1.5) (entry 268). Una marca: la caja.
```

**Conclusión**: el cuerpo ya no decide nada — ni retrocede: para y avisa. La corrección es un acto del dominio (`route.Bump(touch, me)`) cuyo print es la primera orden nueva, `back`, y el rodeo son piernas de corrección en la misma lista que las del plan. El hallazgo colateral (las jambas) muestra por qué importaba: cada clasificación errada ahora inserta correcciones reales.

**Pendiente**: (1) regla de paso en una puerta cuando dos cuerpos entran por lados opuestos; (2) el `within` de llegada y el `standoff` del seguidor siguen siendo del host (`Robot`): candidatos a decidirse en el dominio; (3) green ancla su odometría en el nodo y en la membrana por separado.

## 2026-09-16 · La salida se llama `RobotToRos`: el `print` se vuelve una acción base del robot

**Contexto** (Juan): "actualmente tengo `Robot : IOutputSink`, pero mejor separemos esa parte: una nueva clase que sirva de salida, `RobotToRos`, que herede de `IOutputSink`, que se ocupe del `Obey` y haga el `switch case` para enviar al robot; las acciones base son avanzar / retroceder / girar a la derecha / girar a la izquierda / detener / continuar / choque".

**Ajuste al host** (`Choreography/RobotToRos.cs` nuevo, `Robot.cs`, `GolemController`, `Program`, `sim/bridge/body.py`): la salida y los reportes quedan en dos clases. `RobotToRos` es el `IOutputSink` del actor y el destino del print que devuelve cada comando; su `switch` traduce la palabra del journal a la acción del robot — `turn` → `turnLeft`/`turnRight` (el lado, del menor giro desde la pose del cuerpo; el rumbo sigue siendo del dominio), `run` → `advance`, `back` → `back`, `hold` → `stop` (recordando la acción), reanudación → `continue`, `decide` → de vuelta al golem (`robot.Decide`). El JSON que viaja es el del print más `action`, `within` y el cuerpo declarado. `Robot` se queda con lo que el cuerpo reporta y con el reloj y las palancas, y expone `Actor`, `Pose` y `ToRos`. `body.py` conmuta por `action`; el giro a la izquierda/derecha avanza en el sentido dicho hasta encarar el rumbo; `stop` guarda la acción y `continue` la retoma.

**Ajuste al dominio**: ninguno (`Order` sigue diciendo `turn`/`run`/`back`/`hold`/`decide`; la traducción es del host, como el papel 08 pide: el dominio no anticipa el vocabulario del cuerpo).

**Observación en vivo** (tres golems, cero reinicios; nodos del cuerpo relanzados con el vocabulario nuevo):
```
blue home → living (route 11): turnRight -2.00 · advance (5.19, 8.0) · turnLeft -2.00 · advance (4.53, 6.55) · turnLeft -1.56 · advance (4.58, 3.0)
  /pause a mitad: la pose no cambió en 7 s; /resume: el clock preguntaba 'hold' cada 2 s y cada vez volvía a "retener" borrando lo
  guardado → corregido (Hold solo si hay algo en curso); la segunda vez: "resumed — the body continues what it was doing" → continue.
blue living → north con la caja (route 12): stop/continue en living/south · dos roces en las jambas de living/south, AHORA como
  pared ("grazed wall_living_e_2 / _e_1 … backing off to try again", entradas 368-375: back, giro, otra vez) · pasó center~south ·
  bumped into crate_center (5.2, 5.2) → back (4.8, 4.5) → al girar en el sitio junto a la caja volvió a tocarla (5.2, 5.4) → back →
  al girar otra vez, tercer toque (5.2, 5.2) → back (4.5, 4.9) → turnLeft 1.59 · skirting (4.5, 6.1) · north~center · reached
  the stop (5.5, 9.5) (entry 398). Dos marcas, una figura en línea.
```

**Conclusión**: la separación deja cada cara con su trabajo: `RobotToRos` traduce y envía, `Robot` recibe y pondera. El `continue` funciona y las jambas ya no siembran marcas. Lo que sale a la luz es físico: tras un `back` de 0.6 m, girar en el sitio pegado a una esquina de la caja vuelve a tocarla; cada toque es una corrección legítima del dominio, pero son tres donde bastaría una.

**Pendiente**: (1) la corrección tras un choque podría retroceder más cuando el siguiente giro es amplio, o elegir el sentido de giro que se aleja de la marca — decisión del dominio (`Route.Correct`), no del servo; (2) regla de paso en una puerta; (3) `within` y `standoff` del seguidor siguen en el host.

## 2026-09-17 · Los scripts viven en el `Robot` y el `Push` lo dispara el motor: una reacción por forma de acto

**Contexto** (Juan): "los scripts vuelven a la clase `Robot`, ahí estarán los métodos-acción; el controller recibe y valida los parámetros y llama a robot; el método en el robot tiene los scripts, los valida una segunda vez, corre los scripts y hace los respectivos prints, y esos prints terminan llamando al `Push` de `RobotToRos` automáticamente al terminar el script, sin necesidad de que nosotros lo disparemos".

**Laboratorio** (`GolemTest/OrderPushLabTests.cs`): el print de un comando es PULL (vuelve a quien lo ejecuta); solo el emit de una Reaction se EMPUJA al `OutputTarget`. Pregunta: ¿qué reacción dispara fiel en cada acto tal como el host lo escribe?
```
lab-push.txt — una sola reacción por actor, los once actos del host, pushes por acto:
  [_:Golem].Find($id)      7 pushes: then, turn, reach, pause, resume, bump, decide (todo acto que empieza por route = g.Find(@id)); visit/visit-b 0
  [_:Golem].Visit(_, _)    2: visit, visit-b            [_:Route].Then(_) 1: then          [_:Route].Turn() 1: turn
  [_:Route].Reach(_)       1: reach                      [_:Route].Bump(_, _) 1: bump       [_:Route].Decide(_) 1: decide
  [_:Route].Pause()        1: pause                      [_:Route].Abandon($why) 0 (ni solo)   [_:Golem].PendingRoutes() 0 (un foreach no casa)
  abandon/letgo: 0 pushes también con Find — el print quedaba VACÍO (nada pendiente) y un emit vacío no se empuja
lab-push-real.txt — un actor con las reacciones de habla (expose) + next-order-find/visit/cover/follow, print que siempre dice algo:
  visit 1 (visit) · then 1 (find) · turn 1 · reach 1 · bump 2 (find + echo-bumped) · pause 1 · resume 1 · follow 1 (follow) · fail 1
  · decide 1 · cover 1 (cover) · reach-stop 2 (find + echo-reached): cada acto, exactamente una orden empujada.
```
*Conclusión del laboratorio*: la inconsistencia de la mañana (`lab-patterns.txt`) venía de definir MUCHAS reacciones sobre la misma forma de acto en un actor; una por forma dispara fiel. El emit debe imprimir siempre algo (`pending`) o el motor no empuja cuando la ruta terminó y el cuerpo debe pararse.

**Ajuste al host** (`Robot.cs`, `RobotToRos.cs`, `GolemController.cs`, `Program.cs`, `GolemSpeech.cs`): los scripts son métodos-acción del `Robot` (operador: `Move`, `Cover`, `Pause`, `Resume`, `Forget`; cuerpo: `Arrived`, `Stuck`, `BumpedAsync`; protocolo: `Bumped`, `Grazed`, `Met`, `Decided`, `DecidedPast`, `Failed`, `LetGo`; `Uptake*`), cada uno con su `Check` y terminando en `Robot.NextOrder`. `RobotToRos.DefineReactions(performance)` declara `next-order-find|visit|cover|follow` con `.Program.Emit(Robot.NextOrder)` antes de `Start`; `Push` filtra por nombre y hace el `switch`; el sink se registra tras `Start` y la membrana. El controller valida el JSON y llama al `Robot`; ni él ni el `Robot` despachan la orden. `Answer.Refusal` para los rechazos del propio host. El reloj de 2 s queda de red de seguridad.

**Observación en vivo** (tres golems, cero reinicios; journals conservados — las reacciones nuevas se arman con el sink apagado, así la puesta al día no empuja historia):
```
blue north → living con la caja (route 13), todo por empuje: turning right -1.51 · turned (407) · advancing north~center · passed (409)
  · turning right -1.71 · turned (410) · advancing center~south · /pause → "the body stands" · /resume → "continues what it was doing"
  · bumped into crate_center (5.2, 5.9) · the way corrected, backing off first (416) · backing off to (5.3, 6.7) · backed off (420)
  · turning right -2.93 · skirting to (4.5, 6.5) · passed (422) · … · reached the stop (2.0, 1.5) (429)
red → garage (route 2) y blue la siguió (route 14, PointVisited → Follow → next-order-follow empujó 'decide' → Decided → turn…)
```

**Ajuste al dominio**: ninguno. **Pendiente**: (1) `Abandon($why)` no casa como patrón — el let-go detiene el cuerpo por la palanca; si algún día un `Abandon` cambiara la orden sin palanca, haría falta otra forma; (2) los pendientes anteriores (giro tras el retroceso, regla de paso en puertas, `within`/`standoff` del seguidor).

## 2026-09-17 · La pausa guarda dónde estaba el cuerpo, y la reanudación vuelve a apuntar desde ahí

**Contexto** (Juan): "los scripts de pausa deberían guardar la posición actual… quizá preguntarle cuál es la posición que él piensa que lleva, persistirla, y cuando le den resume, desde su posición donde se había quedado hacia la siguiente que tenía en ruta, que creo que coincide con `route.NextLeg`; y `RouteUnderway()` lo preguntan por query: ¿eso no lo tenemos ya disponible en el script?"

**Ajuste al dominio** (`Routes.Route`): `Pause(Pose me)` guarda `HeldAt`; `Resume()` rehace el rumbo de la pierna siguiente desde `HeldAt` y pone `turned = false`. La prueba `APausedMission_KeepsItsPlanAndItsPlace_UntilResumed` lo fija: retenida en (2.6, 9.5) mirando al norte con la parada en (2.0, 9.5), tras `Resume` la orden es `turn` y el rumbo 3.14 (oeste).

**Ajuste al host** (`Robot.Pause/Resume`): `{ route = g.Next(); me = Pose(@x, @y, @theta); route.Pause(me); print … route.HeldAt.X 'heldX', route.HeldAt.Y 'heldY'; }` y `{ route = g.Next(); route.Resume(); print … }`; se va la consulta `RouteUnderway()`. Los laboratorios pasan la pose como literal (son laboratorios).

**Observación en vivo** (journals archivados en `journal-legacy-20260917-pausa/`; blue home → living con la caja en el centro):
```
turning left to heading -1.71 for (4.8, 3.0) · /pause → paused=true, "the body stands until resumed" · segundo /pause → 409 "already paused"
/resume → turning left to heading -1.72 for (4.8, 3.0)   ← el rumbo recalculado desde HeldAt (antes -1.71)
bumped crate_center (5.3, 5.8) · back · skirting (4.6, 6.5) · center~south · living/south · reached the stop (2.0, 1.5) (entry 28)
```
Un incidente ajeno: la shell de Windows quedó sin recursos para hacer `fork` a mitad de la prueba y hubo que reanudar en un segundo comando; nada del golem.

**Segunda vuelta (Juan, misma tarde): "¿por qué `route.Pause(me)` y no `g.Pause(me)`? ¿por qué pausamos la ruta y no al cerebro?"** Tenía razón: la retención es del robot entero. Si solo se retiene la ruta y esa ruta termina o la abandona un tell más nuevo, la siguiente arrancaría con el robot supuestamente parado. *Ajuste al dominio* (`Golem`, `Route`): `route = g.Pause(me)` retiene al golem (`g.Held`, `g.HeldAt`), retiene la ruta en curso (que conserva su `HeldAt`) y la devuelve; `route = g.Resume(me)` la suelta re-orientando la pierna siguiente desde donde el cuerpo está AHORA (pudo ser empujado parado). *Host*: `NextOrder` imprime `g.Held` y una orden con el golem retenido es `hold` diga lo que diga la ruta; reacciones `next-order-pause` (`[_:Golem].Pause(_)`) y `next-order-resume`. *Hallazgo*: `[_:Golem].Resume()` sin argumentos NO disparó en vivo (la reanudación llegó por el reloj de 2 s); con la pose, `[_:Golem].Resume(_)` disparó: `[output] next-order-resume pushed: {"pending":true,"held":false,"route":1,"order":"turn",…}`. Los patrones que casan fiel llevan un argumento o un `$param`. Journals archivados en `journal-legacy-20260917-pausa2/` y `-pausa3/`. Pendiente: la nota "paused by the operator" salió dos veces en una pausa (cosmético: revisar quién vuelve a obedecer `hold`).

## 2026-09-17 · La retirada crece hasta que el cuerpo tenga sitio para girar

**Contexto** (Juan): "aplica eso en `Correct`: retroceder más y girar alejándose de la marca, para rodearla si aún cabe su cuerpo o ir por otra ruta si ya no cabe". Ayer, tras una retirada de 0.6 m, el cuerpo giró en el sitio pegado a la esquina de la caja y la tocó dos veces más.

**Ajuste al dominio** (`Routes.Route.RetreatFrom`): la retirada va recto hacia atrás y crece — desde la retirada del cuerpo hasta tres retiradas, medio radio cada vez — hasta un punto libre de las paredes (`layout.HasRoom`) y a un radio ENTERO de toda figura (`collisions.Blocks(back, 2·radius)`): el casco que barre al girar no toca lo que acaba de chocar, gire hacia donde gire. Pared detrás: no se pasa de ella. Nada libre: la retirada simple. Desde ese punto el planificador ya decide rodear (`around` por las esquinas de la figura) o ir por otra puerta. Prueba nueva `AfterABump_TheRetreatLeavesRoomToTurn_AndTheRoadGoesAroundIfTheBodyFits` (66 verdes).

**Observación en vivo** (blue north → living con la caja en el centro): `bumped into crate_center (5.2, 5.9) heading -1.59 · backing off to (5.3, 6.8) · backed off (entry 43) · turning right to heading -2.79 · skirting to (4.5, 6.5) · … · reached the stop (2.0, 1.5) (entry 51)`. Un solo choque, una sola retirada (0.1 m más larga que ayer), el giro en el sitio sin volver a tocar, una marca.

**Conclusión**: la corrección completa es del dominio — cuánto retroceder, hacia dónde queda libre, y si se rodea o se cambia de camino — y el robot solo recibe poses. **Pendiente**: el sentido del giro sigue siendo el más corto desde la pose (cosa del servo); con la holgura de un radio no hizo falta forzarlo.

## 2026-09-17 · Nombres: `GolemEmbodiment` y `RobotMechanics`; la salida no es la membrana

**Contexto** (Juan): "renombrar `Robot` a algo más relacionado al robot en general, el embody o el conjunto del robot; `RobotToRos` a algo del sistema nervioso o la cinética; ¿no es esa clase lo mismo que `Rosbridge`?" — y tras varias rondas de opciones (biológicas, de robótica, compuestas): "tal vez no cinemática sino algo como mecánicas… aplica `GolemEmbodiment` y `RobotMechanics`".

**Observación**: `RobotToRos` usa de `Rosbridge` una sola cosa, `PublishAsync(OrderTopic, json)`; `Rosbridge` (275 líneas) es websocket, advertise/subscribe, parseo de odometría y contactos, el anclaje del reckoning, el teleport — nada de órdenes. `DriveAsync` (cmd_vel) ya no lo llama nadie desde que el cuerpo es `body.py`: el host no conduce, ordena. Del lado contrario, lo que vuelve del cuerpo no pasa por la salida: la pose y los contactos por la membrana, los reportes por `/robot/*`.

**Conclusión**: son dos capas distintas — la salida conoce la orden (qué lleva el cuerpo, qué sostuvo, la demora) y la membrana conoce el cable —, así que se quedan separadas y los nombres lo dicen: `GolemEmbodiment` (el golem con cuerpo, mente y carne juntas; el sujeto delante) y `RobotMechanics` (cómo la orden se vuelve movimiento del robot; sólo de ida — lo que vuelve son la membrana y los endpoints). Un robot real cambia el cable y conserva las mecánicas.

**Ajuste al dominio**: ninguno — es host. **Ajuste al host**: `Choreography/Robot.cs` → `GolemEmbodiment.cs`, `RobotToRos.cs` → `RobotMechanics.cs`, `robot.ToRos` → `golemEmbodiment.Mechanics`, log `[output]` → `[mechanics]`. Pendiente menor: quitar `Rosbridge.DriveAsync` y el advertise de `cmd_vel`, código muerto.

## 2026-09-17 · `Dispatch` y las seis acciones base; el `continue` ya no viaja

**Contexto** (Juan): "el `Obey` es una especie de dispatch para ROS: renómbralo; y las acciones del switch no parecen las que hemos venido comentando: mover adelante, mover atrás, girar derecha, girar izquierda".

**Observación en vivo** (blue, north → cocina, con pausa y reanudación a mitad del avance a la puerta): `next-order-visit` → `turning right to heading -2.65` → `advancing to the passage kitchen/north` → `/pause` → `paused by the operator — the body stands` (`stop`) → `next-order-pause` con `held:true, order:hold` → `/resume` → `next-order-resume` con `order:turn` y rumbo `-2.71` → `turning right` → `advancing` → giro a la parada → `reached the stop (3.0, 9.5)` → `pending:false`. La reanudación NO salió como `continue`: `g.Resume(me)` vuelve a dar rumbo a la pierna desde donde está el cuerpo y pone `turned=false`, así que la orden que vuelve es un `turn` con un rumbo distinto al que se sostenía, y `Continue(order)` (que exige la misma orden) no se dispara.

**Conclusión**: el despacho queda con los seis métodos que dicen su nombre (`Advance`, `Back`, `TurnLeft`, `TurnRight`, `Stop`, `Continue`) y el caso `turn` elige lado con la pose; pero desde que la reanudación es del golem (17-sep) el `continue` es letra muerta: el dominio prefiere re-orientar a reanudar. Es coherente con "el que decide es el dominio" — un cuerpo que se detuvo puede haber derivado — pero deja una acción base del robot sin uso.

**Ajuste al dominio**: ninguno todavía. **Pendiente**: decidir si `Resume` conserva la pierna tal cual cuando el cuerpo no se movió desde `HeldAt` (y entonces `continue` viaja), o si se retira `continue` del vocabulario del robot.

## 2026-09-17 · El dominio habla en el vocabulario del robot: acción y cantidad (ajuste 35)

**Contexto** (Juan): "no entiendo muy bien ese switch del `Dispatch`: los `case` no representan lo básico que he dicho de mover adelante, mover atrás, girar derecha, girar izquierda; al robot se le dice muy sencillamente lo que debe moverse hacia adelante, qué tanto debe rotar… escríbelo en el PLAN y aplícalo completo".

**Ajuste al dominio** (`Routes.Route`, `Layouts.MapLayout`, `Golem`): `route.Order` pasa a ser la acción del robot — `advance` | `back` | `turnLeft` | `turnRight` | `stop`, o `decide` — y nace `route.Amount`, la cantidad en metros o radianes, medida desde `route.Standing`: la pose que traen los actos del cursor, `route.Turn(me)` y `route.Reach(me)` (antes `Turn()` y `Reach(point)`), y también `Bump`, `Graze`, `Pause`, `Resume`, `Decide`. `route.Target` es la pose a la que va ahora: el punto de la pierna, o en una puerta primero su approach y luego su exit — dos avances, no uno. `Route.TurnTolerance` (0.05 rad): un giro menor no se pide. `Route.FollowerStandoff` (1.0 m) sustituye al `LeaderStandoff` del host: el respeto al líder es del dominio. `g.PlannedEnd()` es una `Pose` (el fin del camino, mirando como la última pierna): de ahí parte el siguiente encargo cuando el golem está ocupado, y el encargo parte siempre de una pose (`from = Pose(@fx, @fy, @ftheta)`). `Reach` ya no compara puntos: el cuerpo dice que hizo el movimiento y dónde quedó; el cursor avanza una cosa. Se fueron `IsLegAhead` y `AheadAt`.

**Ajuste al host** (`RobotMechanics`, `GolemEmbodiment`, `Roles/Displacer`, `Roles/CollisionCaptor`, `Order`, `body.py`): el print es `action`, `amount`, `route`, `pending` y, si camina, `kind name x y heading following stopsLeft` (fuera `order ax ay ex ey hasHeading within`). `Dispatch` conmuta por acción sin aritmética; desaparecen `LeftIsShorter`, `ArriveWithin`, `LineUpWithin`, `LeaderStandoff`. `Arrived` lleva la pose del cuerpo al acto (`Turned(route, px, py, ptheta)`, `ReachedStop`, `ReachedPoint`). `body.py` ejecuta cantidades con su odometría: avanza o retrocede hasta acumular los metros manteniendo el rumbo con que empezó, gira hasta acumular los radianes; `stop` guarda lo que falta y `continue` lo termina. La única aritmética que queda en la mecánica es el paso de cortesía (`StepAside`, ruta 0): el golem elige el punto y la mecánica lo dice como un giro y un avance desde la pose.

**Observación en vivo** (blue, diarios nuevos; `journal-legacy-20260917-cantidades/` archiva los anteriores):
- norte (6.3, 10.4) → cocina (3.0, 9.5): `turnRight 2.65 rad` → `advance 1.91 m` al approach (4.6, 9.5) → `turnRight 0.53` → `advance 1.22 m` al exit (3.4, 9.5), la puerta cruzada recta → `advance 0.42 m` a la parada → `pending:false`. Cada llegada dice dónde quedó el cuerpo (`standing at (4.6, 9.4)`) y la cantidad siguiente sale de ahí.
- cocina → sur con la caja del centro: `bumped into crate_center at (5.6, 5.8)` → `back 0.73 m` (la retirada que crece) → `turnRight 1.18` → `advance 0.71 m` a un punto de rodeo (5.0, 6.5) → `turnLeft 1.15` → `advance 1.37 m` → segundo toque en (5.1, 5.8): la figura de una sola marca era estrecha y el rodeo pasó a 0.15 m del canto → `back 0.73` → rodeo más ancho por (4.5, 6.5) y (4.5, 5.2) → `center~south` → parada. Dos toques, como en los rodeos de una marca de días anteriores: la figura se ensancha con el segundo.
- green (ruedas, reckoning): `turnRight 2.36 rad` → `advance 1.41 m` → parada; 0.05 m entre lo que cree y la verdad. Las cantidades no le pesan al reckoning más que las poses.
- **Un roce que enseñó algo**: al salir de la cocina hacia el sur (`kitchen/north` seguida de la frontera abierta `north~center`), la puerta salió SIN approach ni exit: el cuerpo fue al punto mismo de la puerta, giró 1.27 rad dentro del vano y rozó `wall_kitchen_e_1`. El roce se corrigió solo (`back 0.60 m`, `turnRight 0.12`, `advance 1.71`, pasó), pero la causa es del dominio: `MapLayout.WithDoorCrossings` elegía el lado de cruce por el punto de la pierna SIGUIENTE, y cuando ése cae en una tercera zona (la parada del sur, detrás de una frontera abierta) ningún lado de la puerta lo contiene y no se decidía cruce.

**Conclusión**: el robot ya recibe exactamente lo que Juan describió — una acción base y una cantidad — y el switch de la mecánica son esas acciones; el error de un mandato relativo no pasa de una pierna porque la siguiente se mide desde la pose real que trae el acto. El costo de las puertas (dos avances) es visible en el log y aceptable. El `continue` sigue sin viajar: tras la reanudación la cantidad que falta es otra, así que la orden nunca es la misma que se sostenía (pendiente del ajuste 34).

**Ajuste al dominio, segundo** (`MapLayout.WithDoorCrossings(from, road)`, `SideAwayFrom`): el lado al que se cruza una puerta es el que NO es el de donde viene el camino — el punto anterior, o `from` para la primera pierna —; sólo si eso no dice nada se consulta el siguiente (y ahora también cuando ninguno de los dos lados lo contiene, por la pasada de la pierna siguiente). Prueba: `ADoor_FollowedByAnOpening_IsStillCrossedStraight` (cocina → sur: approach 3.4, exit 4.6). 67 pruebas verdes.

## 2026-09-17 · Los reportes del robot son un script; el protocolo de toques vuelve al dominio (ajuste 36)

**Contexto** (Juan): "`Move`, `Cover`, `Pause` y `Resume` los veo bien, pero los demás están muy sospechosos: no siguen el patrón de script, manejan lógica del dominio y concurrencia, cosa que no debe ser".

**Observación** (el código antes del ajuste): `Arrived` elegía entre tres scripts según la orden que el host recordaba; `BumpedAsync` preguntaba `g.Suspect`, elegía `Grazed` o `Bumped`, dormía 250 ms en bucle 2.5 s esperando a los compañeros, escribía `Met` y `DecidedPast` o `Failed` según `MaxYields`, y lanzaba `ReconsiderAsync` 12 s más; `StepAside` pedía `g.Aside` y la mecánica lo convertía en giro y avance con la pose. Decisiones del dominio y relojes en el host, exactamente lo que el 8-sep quedó prohibido.

**Conclusión**: cada reporte del cuerpo es UN acto con la pose, y quien concluye es la ruta: `route.Arrive(me)`, `route.Touched(touch, me)`, `g.Bump(touch, me)`. Lo que el host esperaba con relojes (la palabra tardía de un compañero) se concluye donde esa palabra cae en el diario (`HearBump`/`HearTouch`), por geometría: un cuerpo MÁS ALLÁ de la superficie que presioné es a quien toqué; uno DETRÁS, como yo, presiona la misma cosa. Las paciencias (paredes, cosas, compañeros) son de la ruta. El seguidor se aparta con una pierna propia. Ya no hay ruta 0 ni aritmética de pose en la mecánica.

**Ajuste al dominio** (`Routes.Route`: `Arrive`, `Touched`, `Met` privado, `PullOver`, `PatienceWithThings`, `PatienceWithPeers`, `heardWhenLegBegan`; `Touches.Collisions`: `PeerBeyond`, `MarkFacing`, fuera `Suspect`/`Suspicion`; `Golem`: `Bump(touch, me)`, `HearBump`/`HearTouch` concluyen el encuentro). **Ajuste al host**: `GolemEmbodiment` sin `BumpedAsync`, `ReconsiderAsync`, `Suspect`, `StepAside`, `yields`, `lock`; `Roles/Displacer.Arrived`, `Roles/CollisionCaptor.Touched` y `TouchedStanding`; `RobotMechanics` sin `StepAside`/`heardBefore`, una reacción más (`next-order-standing` sobre `[_:Golem].Bump(_, _)`); `GolemSpeech.echo-reached` casa `true reached`; sin `MetPeer`. Pruebas nuevas: pared, cosa, compañero más allá, compañero oído antes de la pierna, palabra tardía, misma cosa desde el mismo lado, `Arrive`, el seguidor se aparta, el golem de pie hace sitio.

**Observación en vivo, primer encuentro de dos cuerpos con el protocolo en el dominio** (blue cocina → sala, red sala → cocina, por el pasillo oeste): se tocaron de frente en el vano de `west/living` en el mismo instante — blue `touched red at (0.7, 3.8) heading -1.60`, red `touched blue at (0.7, 3.8) heading 1.54`. Ninguno había oído todavía al otro (los tells viajan después del acto), así que cada uno concluyó COSA al escribir: marca, retirada, camino nuevo. Luego cada uno oyó el `BumpedAt` del otro y `HearBump` concluyó el encuentro por geometría (`MarkFacing`: el cuerpo del otro estaba más allá de mi marca): `met:1` en los dos, la marca propia retirada. Tres cosas salieron mal y se corrigieron en el acto:
- **El mismo tell llegó DOS veces** a cada diario (dos `Heard` idénticos): la segunda vez ya no había marca propia que retirar y el toque del otro quedó marcado — una marca fantasma en el vano, en los dos diarios. Ajuste: `Collisions.HeardAlready` — una palabra se oye una vez; y `Collisions.MetNear` — un toque oído donde ya concluí un encuentro es ese encuentro contado desde el otro lado, no una cosa.
- **Los roces de red contra la pared de la sala se contaron a blue como choques** (el `expose` acompaña a todo `Touched`) y blue marcó `(1.38, 2.93)` sobre una pared que conoce. Ajuste: `HearBump` no marca un toque sobre una pared del mapa.
- **Red se rindió**: retrocedió 0.6 m hasta quedar dentro del vano, el planificador trazó desde ahí una diagonal a `living/south` que rozó la jamba, y la repitió tres veces desde casi el mismo punto — `PatienceWithWalls` la falló sola, como debe. Blue sí llegó, dando la vuelta por el hall. **Pendiente**: una retirada que termina dentro de un vano debería salir por el eje de la puerta antes de replanificar; y un encuentro concluido tarde no vuelve a decidir el camino (queda la corrección de "cosa"), lo que en un pasillo estrecho manda al cuerpo por el camino largo.
Pruebas: 77 verdes con `APeersGrazeOnAWallWeBothKnow_TeachesNothing` y `TheSameWordHeardTwice_IsHeardOnce`.

**Segunda vuelta y un error de rehidratación**: al redesplegar sobre los diarios de la tarde, blue falló al rehidratar la entrada 21 — `route 2 asked no move now: it asks 'turnLeft'` — un acto aceptado en vivo y rechazado en la repetición. Causa: entre la escritura y la repetición cambió lo que el dominio decide — el cruce de puertas (`SideAwayFrom`) da approach y exit donde antes no los daba, así que la misma `Reach(me)` cae hoy en otro estado del cursor. Es el trato del paper 05 tal como lo aceptamos el 16-sep: el camino decidido dentro es determinista mientras el código del planificador no cambie; cuando cambia, los diarios anteriores son incompatibles y se archivan. Archivados en `journal-legacy-20260918-protocolo/`; la flota arranca en la entrada 1. Lección para la disciplina: **todo ajuste que cambie una decisión del dominio (planificador, cruces, correcciones) archiva los diarios en el mismo despliegue**, no sólo los que cambian firmas de verbos. Con diarios sucios, el segundo encuentro dejó además marcas dudosas junto a la frontera `north~center` que no se pueden atribuir con confianza; la observación válida es la tercera vuelta, sobre diarios nuevos, que sigue abajo.

**Tercera vuelta, diarios nuevos — la danza en la frontera**: blue (cocina → sala) y red (sala → cocina) eligieron ambos el hall y se toparon de frente en la frontera `north~center`, en (4.5, 8.2), en el mismo instante. Ninguno había oído al otro: los dos concluyeron COSA, retrocedieron 0.73 m, y el planificador — el mismo código en los dos, imagen especular — los mandó a rodear la "cosa" por el MISMO lado (este): se tocaron en (5.2, 8.2), otra vez en (5.9, 8.2), y así hasta siete choques — `PatienceWithThings` falló las dos rutas. Mientras tanto la palabra de cada uno sí llegó al otro y `HearBump` concluyó el encuentro (`met` 5, marcas retiradas), pero la ruta ya había corregido como cosa y siguió rodeando el vacío.

**Conclusión**: concluir al escribir es correcto y rápido cuando el otro ya habló; cuando los dos tocan a la vez, la conclusión llega TARDE y tiene que cambiar el camino: no basta retirar la marca. Antes lo resolvía la espera de 2.5 s del host; ahora lo resuelve el dominio donde cae la palabra.

**Ajuste al dominio** (`Route.MetLate(who)`, llamado desde `Golem.HearBump`/`HearTouch` cuando concluyen el encuentro): la ruta en curso que tomó el toque por cosa decide su camino para pasar al compañero desde donde estaba — el paso de cortesía primero, cada uno a SU derecha, que es como dos cuerpos se cruzan —, contando su paciencia con cuerpos. **Ajuste al host**: dos reacciones más, `next-order-heard` sobre `[_:Golem].HearBump(_, _, _)` y `HearTouch`, para que el camino cambiado se empuje al cuerpo sin esperar al reloj. Los diarios de esta vuelta cambian de decisión al repetirse: archivados en `journal-legacy-20260918-protocolo2/`.

**Cuarta vuelta, diarios nuevos, con `MetLate`** (blue cocina → sala, red sala → cocina; se toparon de frente en el pasillo oeste en (0.7, 3.8), a la vez): los dos concluyeron cosa al escribir, retrocedieron, y en cuanto cayó la palabra del otro `HearBump` concluyó el encuentro — `marks: 0`, `met: 1` en ambos, ninguna marca fantasma, ningún roce contado como choque — y `MetLate` cambió el camino, empujado al cuerpo por `next-order-heard` sin esperar al reloj. Hasta ahí, lo que se buscaba. Pero el camino nuevo fue "recto" en los dos: en un pasillo de 1.5 m un cuerpo de 0.5 m no tiene sitio para el paso de cortesía a ningún lado (`Courtesy.StepOutOfTheWayOf` devolvió null y `DecidePast` planificó el camino llano, ya sin marca que rodear), así que red avanzó 3.86 m a través de blue, lo empujó dos metros pasillo arriba, y los dos acabaron `stalled` y fallados.

**Conclusión**: el protocolo de conclusión ya es del dominio y acierta — qué fue el toque, quién estaba, sin ventanas ni contadores en el host —; lo que falta es una regla de CEDER cuando no hay sitio para apartarse: uno de los dos retrocede hasta el cuarto de donde venía y deja pasar. Eso es el pendiente ya anotado de la puerta con dos cuerpos entrando de lados opuestos (desempate determinista por nombre, retroceso a la pierna anterior, espera hasta que el otro pase); va al PLAN como propuesta, no se decide aquí. **Ajuste al dominio**: ninguno más hoy.

**Remate de forma** (Juan, 18-sep: "nada de `Safely`; el método llama a un script de `golemActor.Using`, registra el acto en `g` y hace el print, y ese print llega a `RobotMechanics` por ser el OutputTarget: Push > Dispatch > switch > ros > robot"): `Arrived`, `Stuck` y `Bumped` quedan con su script inline en `GolemEmbodiment`, la misma forma que `Move`; `Safely` y la indirección por los roles se van. Sin cambio de comportamiento: la suite completa sigue verde y el choque con la caja se verificó en vivo tras redesplegar.

## 2026-09-18 · Todo toque es un bump, de momento (ajuste 37)

**Contexto** (Juan): "¿por qué tenemos dos scripts para el bump? … quitemos eso de que si choca con otro robot tenga que decidir que fue un touch; más adelante lo retomamos; todo se considera un bump de momento".

**Conclusión**: el protocolo de encuentro entre cuerpos (concluir al escribir por `PeerBeyond`, concluir tarde por `MarkFacing` + `MetLate`, la ruta de cortesía del cuerpo quieto, `TouchedAt`/`HearTouch`) se construyó, se probó en vivo cuatro veces y se retira el mismo día: hoy el dominio concluye pared o cosa, y nada más. Lo que enseñaron las cuatro vueltas queda en las entradas anteriores para cuando se retome: dos cuerpos que se tocan a la vez concluyen cosa los dos; la palabra tardía debe cambiar el camino; en un pasillo de 1.5 m no hay sitio para apartarse y hace falta una regla de ceder; una palabra puede llegar dos veces.

**Ajuste al dominio**: `Route.Touched` = pared → `Graze`, si no → `Bump`; fuera `Met`, `MetLate`, `PatienceWithPeers`, `heardWhenLegBegan`; `Collisions` sin `PeerBeyond`, `MarkFacing`, `MetNear` (queda `HeardAlready`); `Golem` sin `Bump(touch, me)` ni `HearTouch`; `HearBump` oye y marca salvo sobre pared. Quedan como repertorio probado y sin uso desde el host `g.Met`, `g.LearnMet`, `route.DecidePast`, `Courtesy`. **Ajuste al host**: `Bumped` reconoce y no escribe un toque de pie; sin `TouchedAt`; tres reacciones menos. 72 pruebas verdes. Diarios en `journal-legacy-20260918-protocolo3/`.

## 2026-09-18 · Laboratorio: ¿dispara una reacción sobre `[_:Golem].Underway()`? (ajuste 38)

**Contexto** (Juan): "¿por qué nos pasan el id de la ruta? ¿no sería la ruta en curso la que terminamos encontrando?".

**Observación** (`OrderPushLabTests.DoesAReactionOnTheZeroArgumentUnderway_PushTheNextOrder`, `lab-underway.txt`): tres actores, una reacción cada uno, el mismo acto `{ route = g.Underway(); me = Pose(…); route.Arrive(me); }` tras un `g.Visit`: `[_:Golem].Underway()` → 1 push; `[_:Golem].Underway` (sin paréntesis) → 0; `[_:Route].Arrive(_)` → 1 push. El 17-sep `[_:Golem].Resume()` no había disparado y `Resume(_)` sí; hoy la forma vacía sobre `Underway()` sí dispara. Diferencia posible: `Resume()` se probó entre muchas reacciones sobre el mismo actor (la flojera del lab de patrones); aquí es una reacción por actor.

**Conclusión**: el id de la ruta no tiene que entrar a los actos de los reportes; el golem sabe cuál va. Queda en el `expose` del aviso al seguidor, que sí lo necesita (anuncia y dedupe por ruta, y la ruta puede haberse completado cuando la reacción corre), y como token del host en el JSON que el cuerpo devuelve.

**Ajuste al dominio**: ninguno. **Ajuste al host**: `route = g.Underway()` en `Arrived`, `Bumped`, `Decided`, `Failed`; `Check(g.HasPendingMission())`; reacción `next-order-underway`.

## 2026-09-18 · El choque es del golem: `route = g.Bump(touch, me)` (ajuste 39)

**Contexto** (Juan): "tenía que ser `g.Bump(…)`, el que da el siguiente punto es el recálculo interno del `g.Bump`; no se toman decisiones en el controller; remueve el descarte del reporte viejo".

**Conclusión**: el toque llega al golem, que sabe cuál ruta lleva; la ruta concluye pared o cosa y corrige su camino; el print sale de ella y `next-order-bump` lo empuja. El host ya no mira qué orden llevaba el cuerpo al escribir un choque: si nada está en curso, el `Check` del dominio lo rechaza. De paso, el único `decide` que nacía de un choque (retirada sin camino) se resuelve dentro: al alcanzar la retirada la ruta planifica de nuevo desde la pose real (`PlanAgainFrom`), y si no hay camino se falla sola. `decide` queda para el punto contado sin pose conocida y para el despertar.

**Ajuste al dominio** (`Golem.Bump(touch, me)` devuelve la ruta; `Route.Reach` planifica de nuevo cuando el camino se agota antes de una parada; `Order` sin la rama `BumpedSinceRoute`). **Ajuste al host**: `Bumped(with, x, y, heading, px, py, ptheta)` con un solo script; reacción `next-order-bump`. Prueba nueva: `TheBump_IsTheGolems_ItFindsItsRouteUnderway_AndTheRouteCorrectsInside`. 74 verdes.

## 2026-09-18 · El choque en las palabras del cuerpo: pose y rumbo del golpe (ajuste 40)

**Contexto** (Juan): "los nombres de los parámetros no se entienden; y el dominio debe calcular la coordenada de la colisión con el cuerpo del robot: la posición del golpe más el radio, así sabemos con más certeza dónde está el obstáculo".

**Observación**: `Bumped` recibía dos pares `x y`: el toque en el plano, que `body.py` calculaba sumando el radio, y la pose del cuerpo. El robot no sabe dónde tocó en el plano; sabe dónde está y en qué parte de su casco se apretó el sensor. La suma era aritmética del dominio hecha en el cuerpo.

**Conclusión**: el reporte dice lo que un parachoques sabe — `bodyX bodyY bodyHeading bearing` — y el golem reconstruye el toque con el cuerpo que declaró en su diario. El mismo cálculo sirve para lo que se oye de los compañeros, y el tell viaja en esas mismas palabras.

**Ajuste al dominio** (`Golem.Bump(me, bearing)`, `Golem.HearBump(who, peer, bearing)`, `Golem.TouchOn`). **Ajuste al host**: `Bumped(bodyX, bodyY, bodyHeading, bearing)`, `BumpReport`, `UptakeBumpedAt`, el tell `BumpedAt`, `body.py`. 74 pruebas verdes; la del `g.Bump` comprueba que el toque queda un radio por delante del cuerpo, mirando hacia la cosa. Diarios en `journal-legacy-20260918-rumbo/`.

## 2026-09-18 · El golem sabe dónde está su cuerpo: `Follow` planifica al recibir el punto y `decide` desaparece (ajuste 41)

**Contexto** (Juan): "desde el momento en que blue recibió ese `g.Follow(point)` ese mismo comando crea la route para poder llegar al punto… veo innecesario el `decide`, porque al final el `g.Follow` va a terminar creando la route e imprimiendo a dónde tiene que llegar; ojo que esto debe considerar el radio de red, eso no lo veo que viaje".

**Observación**: `Golem.Follow` entraba con `from = null` a propósito ("no start known here: the route asks 'decide'") porque el uptake de `PointVisited` corre dentro de una Reaction y una Reaction no tiene telemetría: no hay `@x` de blue que inyectar. La ruta nacía sin camino, `Order` decía `decide`, `Dispatch` llamaba a `golemEmbodiment.Decide` y éste escribía `route.Decide(from)` con la pose de la membrana. Dos vueltas por el host para algo que el diario ya sabía: cada acto trae la pose del cuerpo (`Visit(from…)`, `Arrive(me)`, `Bump(me…)`, `Pause(me)`, `Resume(me)`), pero sólo la ruta la guardaba (`route.Standing`), no el golem. El otro `decide`, el despertar con plan en curso, era el mismo rodeo: el reloj del host pedía la pose y escribía `Decide`.

**Conclusión**: el golem conserva dónde está su cuerpo como estado del diario (`g.Standing`, determinista en replay porque toda pose entró como `@param`), y con eso `Follow` planifica dentro como `Visit`: desde donde termina la ruta en curso si está ocupado, desde `Standing` si está libre. El único momento sin pose es el nacimiento: el arranque escribe un acto `g.Wake(me)` con la pose de la membrana — el golem despierta donde está su cuerpo — y ahí mismo, si hay un plan en curso y el golem no está retenido, la ruta lo decide de nuevo DENTRO (`Route.Awake`); el `Decide("awake…")` del reloj sobra. Con eso `decide` deja de ser una orden: una ruta nace con camino y lo rehace sola (retirada sin camino, despertar); una ruta pendiente sin camino es una invariante rota y `Order` lo dice con excepción. Sobre el radio del compañero: no viaja; hoy la flota comparte `body_v1` y el seguidor asume que el líder mide como él; `FollowerStandoff` pasa de constante suelta (1.0) a dos radios más un margen de 0.5 (mismo valor hoy). Que el tell lleve el radio de quien habla queda pendiente para una flota heterogénea.

**Ajuste al dominio** (`Golem.Standing`, `KnowsWhereItStands`, `Wake(me)`, `Follow` planifica desde `Whereabouts()`; la ruta avisa al golem en `Turn`/`Reach` (`stood`); `Route.Awake(me)`; `Order` sin `decide`; `FollowerStandoff` derivado, `FollowerClearance`). **Ajuste al host**: `GolemEmbodiment.Wake()` en el arranque (`RunAsync`), fuera `Decide`/`Decided`; `RobotMechanics` sin `case "decide"`, reacción `next-order-wake` sobre `[_:Golem].Wake(_)`; `Order.cs` sin la palabra. Pruebas nuevas (8): el golem aprende la pose de cada acto; punto contado planificado al recibirlo (libre / ocupado desde `PlannedEnd` / sin pose → rechazo); despertar con plan (decide dentro), sin plan, retenido (nada se decide de nuevo); standoff derivado. 82 verdes. Diarios en `journal-legacy-20260918-standing/`.

**Verificación en vivo (ajuste 41)**, diarios nuevos: los tres golems despertaron con `g.Wake(me)` en la entrada 3 (blue en (6.3, 10.4), red en (2.5, 2.5), green en (10.0, 10.5)) y `next-order-wake` empujó `{pending:false}`. `POST /move` a red hacia la cocina (2.0, 9.5): sala → puerta west/living → pasillo oeste → puerta kitchen/west → parada, ocho actos, 0 choques. Al llegar, red contó `PointVisited`; en blue el uptake `g.Follow(point)` nació CON camino desde donde blue sabía que estaba su cuerpo (la pose del despertar) y `next-order-follow` empujó de inmediato `turnRight 2.65 rad` hacia la puerta kitchen/north — la primera orden fue a los motores, ninguna vuelta por el host. Blue giró, avanzó 1.91 m al approach, cruzó (1.24 m), y su último avance fue de 0.42 m: se detuvo en (3.0, 9.5), exactamente el standoff de 1.0 m antes del punto del líder (2.0, 9.5), esperó sus 6 s y se apartó (`aside`). La palabra `decide` no aparece en el log de ningún golem.

## 2026-09-18 · Laboratorio: ¿puede una reacción capturar sin `expose` lo que hoy exponemos? (`lab-nested.txt`)

**Contexto** (Juan): "tengo entendido que podemos omitir los `expose` de los scripts de Puppeteer, ¿será que hay una nueva versión? nos comentan que no son necesarios".

**Observación 1, la versión**: el paquete fijado es `Ncubo.Puppeteer 2.0.1-beta.10017-portable.2`, construido el 21-ago-2026; la fuente en `repos/puppeteer` está en el commit del 1-sep-2026 y desde el 21-ago no hay cambios en el matcher (`Follower/`). No hay versión nueva que cambie esto. El paquete YA trae `NestedCallParameterNode` (12-jun: "casar argumento-llamada anidado y capturar sus `$vars` internos"), `ScriptConstructorCall.ArgumentValues` (11-jul: "constructor positions correlate `$x` like method positions") y el contrato de captura (9-jul: `$x` sobre un `@param` declarado captura; sobre una variable local es error de autoría). Las guías del training-lab (`puppeteer-reactions`, escenarios 4 y 5) siguen enseñando `expose` como el tercer canal — "el ÚNICO que un patrón captura directamente" — y a la vez la guía de parámetros dice que un valor también se captura "por un verbo que la reacción casa": `RecordFooter(@n)` → `[Ledger].RecordFooter($n)`. Eso ya lo usamos: `[_:Golem].Find($id)`.

**Observación 2, el laboratorio** (`GolemTest/NestedCaptureLabTests.cs`, un actor por patrón, mismos actos, hallazgos en `lab-nested.txt`):
- `[_:Golem].Bump(Pose($bx, $by, $bh), $bearing)`: la DEFINICIÓN se rechaza (`Expected 'rParen'… found '('`): un constructor anidado como argumento no se parsea; el parser sólo admite como argumento anidado una llamada con receptor entre corchetes, `foo([_:T].goo($x))`. Lo mismo para `Forget(Position($x, $y))` y `Arrive(Pose(…))`.
- `[_:Golem].Bump(_, $bearing)`: dispara y captura `bearing=0.1` — un `@param` en posición de argumento se captura sin `expose`.
- `[_:Golem].Bump($me, $bearing)` sobre `me = Pose(…); route = g.Bump(me, @bearing)`: silencio (0 pushes) — una variable local no se captura.
- **`Pose($bx, $by, $bh) [_:Golem].Bump(_, $bearing)`** sobre el acto tal como lo escribimos hoy (la pose en `me` en la línea anterior): DISPARA UNA VEZ con las cuatro capturas, `bx=3, by=9.5, bh=0, bearing=0.1`. Un patrón de constructor suelto al lado del acto casa el `Pose(@…)` de la misma entrada y captura sus `@params`.
- `Pose($bx, $by, $bh)` solo: dispara en TODA entrada que construya una `Pose` (también el `from` del `Visit`): sin el acto al lado no distingue el choque.

**Conclusión**: no hay versión nueva; lo que hay es más capacidad de la que usamos. De nuestros tres `expose`, dos llevan sólo `@params` del propio acto y se pueden quitar con el patrón "constructor al lado del acto": el de `Bumped` (`bodyX bodyY bodyHeading bearing` ← `Pose($bx, $by, $bh) [_:Golem].Bump(_, $bearing)`) y el de `Forget` (`gx gy` ← `Position($x, $y) [_:Golem].Forget(_)`). Falta `who`, el nombre del golem, que no está en el acto ni en el dominio (el nombre es identidad del diario): hoy viaja por el `expose @name who`; sin él tendría que decirlo el cable — nuestro `HttpBroker` ya sella cada trama con `golem-origin` y `TELL_ROUTES` conoce a cada compañero por nombre, así que el receptor puede saber quién habló sin que viaje como parámetro (cambio de host, no de dominio). El tercero, el de `Arrived` (`rid`, `reached`), lleva hechos que el acto NO contiene — si la llegada fue una parada, y a qué ruta — y que el dominio concluye dentro de `Arrive(me)`; por la regla de la propia guía (un predicado de reacción nunca lee estado vivo: se congela en la entrada con un `$param` o un `expose`), ahí el `expose` ES la herramienta correcta, salvo que el acto lo diga en sus palabras (un verbo del dominio que nombre la parada alcanzada). **Ajuste al dominio**: ninguno hoy; propuesta al PLAN pendiente de Juan: quitar los `expose` de `Bumped` y `Forget` con el patrón de constructor al lado, `who` por el cable, y decidir qué hacer con el de `Arrived`.

## 2026-09-18 · Un `expose` sólo para lo que el acto no dice (ajuste 42)

**Contexto** (Juan, tras el laboratorio anterior): "escríbelo en el PLAN como ajuste 42 y aplícalo".

**Observación** (segunda vuelta del laboratorio, `lab-nested.txt`, casos N10–N13): el patrón mixto — constructor al lado del acto más un `expose` reducido — dispara y captura todo junto: `Pose($bx, $by, $bh) [_:Golem].Bump(_, $bearing) expose $who who;` → `bx=3, by=9.5, bh=0, bearing=0.1, who=lab`; `Pose($x, $y, _) [_:Route].Arrive(_) expose $rid rid, true reached;` dispara cuando el `expose` dice `true` y calla cuando dice `false` (la pierna que no es parada); `Position($x, $y) [_:Golem].Forget(_)` captura el punto sin ningún `expose`.

**Conclusión**: lo que es un `@param` del acto — directo o dentro del objeto construido al lado — lo captura el patrón; el `expose` queda para lo que el acto no contiene: el nombre del golem (identidad del diario, no estado; el `Told` del motor no entrega quién habló) y la conclusión del dominio de que la llegada fue una parada (congelada en la entrada, porque un predicado de reacción no lee estado vivo). El punto que se cuenta al seguidor pasa a ser la pose donde el cuerpo quedó, un hecho vivido, y no el punto de la orden.

**Ajuste al dominio**: ninguno. **Ajuste al host**: `Bumped` expone `@name who` solo; `Arrived` expone `@id rid, @stop reached` (fuera `rx ry` y sus parámetros); `Forget` no expone; `echo-bumped`, `echo-reached`, `echo-forgotten` casan el constructor al lado del acto; el lab del conjunto real (`OrderPushLabTests`) con los mismos patrones. Los actos no cambian de firma: los diarios se conservan.

**Verificación en vivo (ajuste 42)**, redespliegue sin archivar diarios (los actos no cambian; la flota había sido reiniciada por el operador a las 13:49 y rehidrató en la entrada 4): caja en el hall central por el kiosko; `POST /move` a red del living al hall norte (5.5, 9.5) por el centro. Red chocó con la caja (entrada 16), `next-order-bump` empujó `back 0.73 m`, y `echo-bumped` — con el patrón `Pose($bodyX, $bodyY, $bodyHeading) [_:Golem].Bump(_, $bearing) expose $who who;` — contó `BumpedAt` a blue y a green: blue quedó con `marks 1, heard 1`. Red rodeó y llegó a la parada (entrada 28); `echo-reached` — `Pose($x, $y, _) [_:Route].Arrive(_) expose $missionId rid, true reached;` — contó a blue la pose donde quedó red, (5.47, 9.49): blue planificó al recibirla, giró 2.31 rad, avanzó 0.21 m hasta el standoff, esperó 6 s y se apartó. En las siete piernas anteriores de red (giros y avances que no eran parada) el eco calló. `POST /forget` en red sobre la marca (5.14, 5.10): `echo-forgotten` — `Position($x, $y) [_:Golem].Forget(_)`, sin ningún `expose` — contó `ObstacleGone`: `marks 0` en red, blue y green (`heard` sigue en 1: el oído no se olvida). Tres tells, tres patrones, los `expose` reducidos a `who` y a `rid reached`.
