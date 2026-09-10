# Protocolo WebSocket — Multiagentes-Unity

Especificación del contrato de comunicación entre el servidor Python y Unity.

- **Endpoint:** `ws://localhost:8765`
- **Formato:** JSON en UTF-8
- **Sentido:** Python → Unity (estados), Unity → Python (comandos)

---

## Python → Unity: estado de simulación

Python envía un mensaje por cada _step_ de simulación. Unity lo procesa y actualiza la visualización.

### Estructura completa

```json
{
  "paused":          false,
  "iniciado":        true,
  "terminado":       false,
  "step":            42,
  "tick":            0.25,
  "size":            25,
  "total_cultivo":   580,
  "silo": {
    "x": 0,
    "z": 0
  },
  "terreno":         [1, 1, 2, 1, 0, ...],
  "celdas_cambiadas": [
    { "x": 3, "z": 7, "tipo": 0 }
  ],
  "agentes": [
    {
      "id":                 "C1",
      "tipo":               "cosechadora",
      "x":                  12,
      "z":                  8,
      "grano":              45,
      "capacidad":          100,
      "combustible":        380.0,
      "combustible_maximo": 500.0,
      "recolectado":        45,
      "terminado":          false,
      "entregado":          0,
      "gasolina":           120.0
    },
    {
      "id":                 "T1",
      "tipo":               "tractor",
      "x":                  10,
      "z":                  9,
      "grano":              20,
      "capacidad":          60,
      "combustible":        450.0,
      "combustible_maximo": 500.0,
      "recolectado":        0,
      "terminado":          false,
      "entregado":          80,
      "gasolina":           50.0
    }
  ]
}
```

### Campos del mensaje raíz

| Campo              | Tipo   | Descripción                                                                                                                      |
| ------------------ | ------ | -------------------------------------------------------------------------------------------------------------------------------- |
| `paused`           | bool   | La simulación está pausada                                                                                                       |
| `iniciado`         | bool   | La simulación fue iniciada al menos una vez                                                                                      |
| `terminado`        | bool   | Toda la cosecha fue completada y entregada                                                                                       |
| `step`             | int    | Número de paso actual                                                                                                            |
| `tick`             | float  | Intervalo de tiempo entre pasos (segundos). Unity lo usa para interpolar el movimiento suavemente                                |
| `size`             | int    | Lado del grid (el campo mide `size × size` celdas)                                                                               |
| `total_cultivo`    | int    | Total de celdas de cultivo al inicio                                                                                             |
| `silo`             | objeto | Posición del silo de entrega                                                                                                     |
| `terreno`          | int[]  | Array aplanado del estado de cada celda. **Solo se envía en el primer mensaje o tras un reset.** Índice: `fila * size + columna` |
| `celdas_cambiadas` | array  | Celdas que cambiaron de estado en este step. Se usa en lugar de reenviar el terreno completo                                     |
| `agentes`          | array  | Lista de todos los agentes activos con su estado actual                                                                          |

### Codificación del terreno

El array `terreno` y el campo `tipo` de `celdas_cambiadas` usan estos valores:

| Valor | Tipo de celda                              |
| ----- | ------------------------------------------ |
| `0`   | Vacío (cultivo ya cosechado o celda libre) |
| `1`   | Cultivo (maíz disponible)                  |
| `2`   | Obstáculo (roca, árbol, etc.)              |

La posición 3D de la celda `(fila, columna)` en Unity es `Vector3(fila, 0, columna)`, es decir: **fila → eje X, columna → eje Z**.

### Campos por agente

| Campo                | Tipo   | Aplica a    | Descripción                                                                                            |
| -------------------- | ------ | ----------- | ------------------------------------------------------------------------------------------------------ |
| `id`                 | string | ambos       | Identificador único, ej. `"C1"`, `"T2"`                                                                |
| `tipo`               | string | ambos       | `"cosechadora"` o `"tractor"`                                                                          |
| `x`                  | int    | ambos       | Posición actual, fila del grid                                                                         |
| `z`                  | int    | ambos       | Posición actual, columna del grid                                                                      |
| `grano`              | int    | ambos       | Grano que carga actualmente                                                                            |
| `capacidad`          | int    | ambos       | Capacidad máxima de grano                                                                              |
| `combustible`        | float  | ambos       | Combustible restante                                                                                   |
| `combustible_maximo` | float  | ambos       | Capacidad total del tanque                                                                             |
| `recolectado`        | int    | cosechadora | Total de celdas cosechadas por este agente                                                             |
| `terminado`          | bool   | cosechadora | Esta cosechadora terminó su tarea                                                                      |
| `entregado`          | int    | tractor     | Total de grano entregado al silo                                                                       |
| `gasolina`           | float  | ambos       | Combustible **consumido** (acumulado desde el inicio). Unity lo suma para mostrar el total de la flota |

> **Nota:** `combustible` es el nivel actual (decrece). `gasolina` es el consumo acumulado (crece). Son campos distintos.

---

## Unity → Python: comandos de control

Unity envía comandos como respuesta a la interacción del usuario con el panel de control.

### Iniciar simulación

Se envía al pulsar **INICIAR** por primera vez.

```json
{
  "command": "iniciar",
  "config": {
    "size": 25,
    "n_cosechadoras": 3,
    "n_tractores": 2,
    "densidad_obstaculo": 0.05,
    "capacidad_gasolina": 500.0,
    "capacidad_grano": 100
  }
}
```

### Reiniciar simulación

Se envía al pulsar **REINICIAR**. Misma estructura que `iniciar`.

```json
{
  "command": "reset",
  "config": {
    "size": 25,
    "n_cosechadoras": 3,
    "n_tractores": 2,
    "densidad_obstaculo": 0.05,
    "capacidad_gasolina": 500.0,
    "capacidad_grano": 100
  }
}
```

### Pausar

```json
{ "command": "pause" }
```

### Reanudar

```json
{ "command": "resume" }
```

### Parámetros de configuración

| Parámetro            | Tipo  | Default | Descripción                                        |
| -------------------- | ----- | ------- | -------------------------------------------------- |
| `size`               | int   | 25      | Lado del grid. El campo tiene `size²` celdas       |
| `n_cosechadoras`     | int   | 3       | Número de cosechadoras                             |
| `n_tractores`        | int   | 2       | Número de tractores                                |
| `densidad_obstaculo` | float | 0.05    | Fracción de celdas que son obstáculos (0.0–1.0)    |
| `capacidad_gasolina` | float | 500.0   | Capacidad del tanque de combustible de cada agente |
| `capacidad_grano`    | int   | 100     | Capacidad de grano de cada cosechadora             |

---

## Flujo de mensajes típico

```
Python                              Unity
  │                                   │
  │ ←── conexión WebSocket ──────────  │
  │                                   │
  │ ←── {"command":"iniciar",...} ───  │  (usuario pulsa INICIAR)
  │                                   │
  │ ──── estado step 1 ─────────────→ │  terreno completo + agentes
  │ ──── estado step 2 ─────────────→ │  celdas_cambiadas + agentes
  │ ──── estado step N ─────────────→ │
  │                                   │
  │ ←── {"command":"pause"} ─────────  │  (usuario pulsa PAUSAR)
  │                                   │
  │ ──── estado (paused: true) ──────→ │
  │                                   │
  │ ←── {"command":"resume"} ────────  │
  │                                   │
  │ ──── estados step N+1... ────────→ │
  │                                   │
  │ ──── estado (terminado: true) ───→ │  simulación completa
```

---

## Notas de implementación

**Optimización de ancho de banda:** el array `terreno` completo (25×25 = 625 enteros) solo viaja una vez al inicio o tras un reset. En cada step posterior Python envía únicamente las celdas que cambiaron (`celdas_cambiadas`), que normalmente son las celdas cosechadas ese step.

**Interpolación de movimiento:** Unity usa el campo `tick` para interpolar visualmente el movimiento de los agentes entre pasos discretos. Si `tick = 0.25`, cada agente tarda 250ms en animarse de su posición anterior a la nueva, independientemente del framerate.

**Sincronización real:** a partir del segundo mensaje, Unity mide el tiempo real entre mensajes (`Time.time`) y usa ese intervalo para la interpolación, en lugar de confiar en `tick`. Esto previene desincronización si el servidor varía su ritmo.
