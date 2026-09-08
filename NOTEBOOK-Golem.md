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
