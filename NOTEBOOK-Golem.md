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
