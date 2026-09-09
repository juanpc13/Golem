# Plan — la ruta como cola de posiciones, y el host reducido a mensajero

**ESTADO: implementado el 9-sep-2026 y verificado en vivo. CORREGIDO el mismo día (opción C): la orden la escribe el host leyendo al dominio, no una reacción — el `expose` y las guardas ensuciaban el diario, que ahora es solo actos. Las dos reacciones de bombeo desaparecieron; las tres de habla siguen.**

**ESTADO original:** Las cinco fases están hechas salvo el `docker kill` a media ruta. Decisiones tomadas: (1) `Cross` sobrevive; (2) `Learn` pasó a `LearnMark`; (3) las lecturas `Next*` pasaron a `Order*`. Lo que sigue siendo runtime del host, y el plan nunca prometió mover: el tanteo y la cesión ante un compañero. Evidencia en `NOTEBOOK-Golem.md`, entradas del 9-sep.

*Propuesta de Juan, 9-sep-2026. Documento de trabajo. El cuaderno con la evidencia del motor: `NOTEBOOK-Golem.md`, entrada «Laboratorio: la ruta como cola de posiciones que bombea una reacción».*

## El principio

> "El MoveTo es casi que solo delegarle esa tarea al host; el host debe ser lo más básico posible, él no toma decisiones, solo comunica lo que pudo o no hacer, luego el dominio es quien decide por dónde ir." — Juan
>
> "Al final la Route es una serie de MoveTo y el robot retroalimenta diciendo que ya llegó; si ya llegó Reach, entonces el reaction le da el siguiente punto de la ruta, y así hasta completarlo. Cada Reaction le da retroalimentación al dominio para ver qué es lo siguiente que debería hacer el robot." — Juan

Hoy el lazo del host recorre el camino: lee el cursor del dominio, elige el tramo y conduce. Con este cambio la conducción entera la cuenta el journal, y al host le quedan dos deberes: **cumplir la orden vigente** y **decir si pudo o no**.

Lo que el motor ya demostró (4 pruebas en verde, scratch `touchlab`): una reacción escribe el primer paso al ver el `Route` y el siguiente al ver cada `Reach`; dos reacciones distintas observan el mismo reporte; la cadena sobrevive un reinicio a media cola sin repetir pasos ya dados.

## Cómo clasificamos cada método

Ocho clases. Las cinco primeras se journalean (son actos); las tres últimas no (son preguntas).

| Clase | Quién la escribe | Qué significa en el diario |
|---|---|---|
| **Release** | la cadena de arranque | lo que el golem sabe de sí y de su plano |
| **Encargo** | el operador | lo que se le pide |
| **Uptake** | una reacción, con lo que un compañero contó | lo que oí de otro |
| **Decisión** | el host, con lo que el dominio calculó | mi camino |
| **Orden** | una reacción | host, lleva el cuerpo aquí |
| **Reporte** | el host | pude, o no pude |
| **Conclusión** | una reacción, tras un reporte | qué fue lo que pasó |
| **Cierre** | el host o una reacción | cómo terminó |
| **Lectura total** | nadie: es consulta | nunca falla, aunque no haya nada |
| **Lectura guardada** | nadie: es consulta | exige consultar antes lo que indica |

## Diccionario: cómo está hoy y en qué queda

### Escrituras (hoy 22, contando sobrecargas; 18 nombres)

| Hoy | Clase hoy | Mañana | Cambio |
|---|---|---|---|
| `Embody(r)` `Cruise(v)` `Linger(s)` | release | igual | — |
| `Chart(name,x,y,w,h)` `.DoorTo()` `.OpenTo()` | release | igual | — |
| `MoveTo(id, x, y)` | encargo | **`Visit(id, x, y)`** | renombre: el nombre `MoveTo` queda libre para la orden |
| `MoveTo(id, place)` | encargo | **`Visit(id, place)`** | renombre |
| `MoveTo(id, stops[])` | encargo | **`Visit(id, stops[])`** | renombre |
| `Cover(id, stops[])` | encargo | igual | — |
| `Follow(x, y)` | encargo por uptake | igual | — |
| `Route(id, plan)` | decisión | igual | ya no es obligatoria: si el camino es un solo segmento, no hay `Route` |
| — | — | **`MoveTo(id, x, y)`** nueva | la orden al host, escrita por una reacción; guarda: debe ser el punto que sigue en la cola |
| `Cross(id, passage)` | reporte | igual, si sobrevive (decisión 1) | además dispara la bomba |
| `Reach(id, x, y)` | reporte | igual | acepta misión sin `Route` (camino de un solo segmento) |
| `Bump(id, x, y, heading)` | reporte | igual | — |
| `Bump(x, y, heading)` | reporte | igual | el cuerpo parado al que tocan |
| `Graze(id, x, y)` | reporte | igual | — |
| `HearBump(who, x, y)` | uptake | igual | — |
| `LearnMark(x, y, heading)` | uptake | igual, o **`LearnMark`** (decisión 2) | — |
| `Mark(x, y, heading)` | conclusión | igual | — |
| `Met(who, x, y)` | conclusión | igual | — |
| `Fail(id, reason)` | cierre | igual | ahora puede escribirlo una reacción (paciencia agotada, sin camino) |
| `Abandon(id, reason)` | cierre | igual | — |
| `Announce(id)` | rodeo del motor | igual | mientras el bug de elisión siga |

### Lecturas (hoy 56)

| Grupo | Hoy | Clase | Mañana |
|---|---|---|---|
| Cuerpo | `Radius` `LingerAfterTold` | total | igual |
| | `Speed` | guardada: init aplicado | igual |
| Mapa | `PlaceCount` `PassageCount` `MarkCount` `ObstacleCount` `ThingCount` `MetCount` | total | igual |
| | `KnowsPlace` `IsOnMap` `KnowsWallAt` `FitsAt` `HasRoomAt` `AreStops` | total | igual |
| | `PlaceAt` | guardada: `IsOnMap` | igual |
| | `Distance(from, to)` | guardada: lugares y camino | igual |
| | `Places()` | objetos | igual |
| Camino | `Plan(id, x, y)` | guardada | igual: sigue siendo quien calcula la cola |
| | `Evasion(x, y, heading, strategy)` | guardada | igual |
| | — | — | **`HasNextPoint(id)`** nueva: la guarda de la bomba |
| | — | — | **`NeedsRoad(id, x, y)`** nueva: ¿el camino es más de un segmento? |
| Toques | `Suspect(x, y, heading, since)` | total | igual |
| | `HeardBumpCount` `HeardBumpNear` | total | igual |
| Misiones | `NextHandle` `Knows` `HasPendingMission` `Pending` `PendingIds` `Total` `FollowingCount` `NewestFollowingId` | total | igual |
| | `IsPending` `IsFollowing` `WasAnnounced` `IsRouted` `HasNewerFollowing` `OrderIsStop` `HasBumpedSinceRoute` `MayRetryLeg` | guardada: `Knows(id)` | igual |
| | `LegsLeft` `StopsLeft` `Bumps` `Grazes` `StatusOf` `OrderPassage` | guardada: `Knows(id)` | igual |
| Progreso | `RouteLength` `DistanceLeft` | total | igual |
| | `RouteSeconds` `SecondsLeft` | guardada: `Speed` | igual |
| Orden vigente | `NextId` `OrderX` `OrderY` `OrderApproachX/Y` `OrderExitX/Y` | guardada: `HasPendingMission` | **cambian de sentido**: hoy dicen "el siguiente tramo del camino", mañana "la orden que estoy cumpliendo". Candidatos a renombre (decisión 3): `OrderX`, `OrderY`, `OrderApproachX`… |

### Lo que muere

Nada del dominio. Lo que se borra es del host: el trozo del lazo que elige tramo y avanza el cursor. Ese código es el que hoy decide, y por eso se va.

## Fases

**Fase 0 — una prueba de motor. HECHA (9-sep), ver el cuaderno «Fase 0: cómo viaja el punto hasta la orden».** Resultado: la orden **sí puede llevar el punto**. El comando de llegada asigna a parámetros y los expone bajo una guarda:

```
g.Reach(@id, @x, @y);
if (g.HasNextPoint(@id)) {
    @nx = g.OrderX(@id);
    @ny = g.OrderY(@id);
    expose @id id, @nx nx, @ny ny;
}
```

y la reacción casa por la forma del `expose` y escribe `g.MoveTo(@id, @nx, @ny)`. Dos consecuencias: el diario dice el punto (`g.MoveTo(1, 6.19, 3)`), y **la entrada no-op del final desaparece** — con la cola agotada no se expone nada, la reacción no dispara y el último reporte cuesta una sola entrada. Queda descartada la forma `expose g.OrderX(@id) nx` (llamada dentro del expose): funciona aislada pero no siempre, y no se entendió por qué.

**Fase 1 — el dominio.** `Visit` toma el lugar de `MoveTo` como encargo (mismo cuerpo, `Entrust`). Nace `MoveTo(id, x, y)` como orden, con su guarda. El cursor de la misión pasa a avanzar con la **orden**, y `Cross`/`Reach` la confirman; si el reporte es `Bump`, el punto vigente no se mueve. `Reach` acepta misión sin camino, que es lo que permite que un encargo de un solo segmento no tenga `Route`. Tests de aceptación para cada regla nueva.

**Fase 2 — las reacciones.** Dos bombas, porque una reacción no hace OR de patrones: una sobre `Route` (la primera orden) y otra sobre el reporte de llegada. Si `Cross` sobrevive, son tres.

**Fase 3 — el host se encoge.** El lazo deja de elegir tramo: lee la orden vigente, conduce, reporta. Es la fase que más código borra y la que hay que revisar con cuidado, porque las claves de idempotencia cuelgan hoy de los tramos restantes.

**Fase 4 — las otras dos cadenas, con la misma forma.** `Bump` concluye `Mark` o `Met` y después repite la orden o decide otra ruta. `Graze` repite la orden o cierra con `Fail` cuando se agota la paciencia.

**Fase 5 — journals nuevos, despliegue y laboratorio en vivo.** Las tres cadenas, y un `docker kill` a media ruta para ver que la bomba se reanuda sola en el mundo real, no solo en el scratch. Entrada en el cuaderno con lo observado.

## Decisiones que faltan

1. **¿Sobrevive `Cross`?** Si sí, el diario sigue diciendo "crucé la puerta" y cuesta una reacción más. Si no, todo es `Reach` y la puerta se lee del nombre del punto en el `Route`. *Recomiendo conservarlo.*
2. **¿`LearnMark` pasa a `LearnMark`?** Los journals renacen igual con este cambio, así que sale gratis. Sin decidir.
3. **¿Se renombran `OrderX`/`OrderY` a `OrderX`/`OrderY`?** Cambian de sentido: pasan a ser la orden vigente. *Recomiendo renombrarlos, para que el host no crea que sigue leyendo un cursor.*

## Riesgos

- **La bomba es el latido de la conducción.** Si una reacción falla, el cuerpo se queda quieto; hoy el lazo se recupera solo releyendo el estado. A favor: el cursor de la reacción solo avanza después de que su acción corrió, así que un fallo se reintenta y un reinicio lo reanuda. Probado en el scratch; **falta probarlo en vivo**.
- **El journal crece**: una entrada más por tramo. La entrada final que no hacía nada **ya no existe**: la fase 0 movió la guarda al comando de llegada, así que con la cola agotada la bomba no dispara.
- **Journals incompatibles**: cambian nombres y firmas de verbos journaleados. Los tres golems renacen. Juan ya autorizó limpiarlos.

## Lo que no toca este plan

El mapa y sus releases, el planificador y su costo, las maniobras de evasión, el protocolo de toques (que quedó cerrado ayer), el habla entre golems y el panel, salvo el botón que nombre un verbo renombrado.
