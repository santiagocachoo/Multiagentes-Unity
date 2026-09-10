# Multiagentes-Unity

Simulación multiagente de logística agrícola desarrollada en colaboración con **John Deere**, como proyecto académico de sistemas multiagente.

El sistema modela una flota de cosechadoras y tractores que coordinan la recolección de grano en un campo de cultivo, demostrando coordinación autónoma mediante arquitectura de pizarrón (_blackboard_).

---

## Arquitectura

```
Servidor Python  ──── WebSocket (ws://localhost:8765) ────  Unity (visualización)
  · lógica de agentes                                          · render 3D
  · pathfinding A*                                             · gráficas en tiempo real
  · blackboard                                                 · cámaras ISO / Top / POV
  · motor de simulación                                        · panel de control
```

**Python es el motor del sistema.** Unity actúa exclusivamente como cliente de visualización: recibe estados por WebSocket y envía comandos de control. No hay lógica de simulación en C#.

---

## Requisitos

| Herramienta             | Versión mínima   |
| ----------------------- | ---------------- |
| Unity                   | 6000.x (URP)     |
| Python                  | 3.10+            |
| Paquete NativeWebSocket | incluido vía UPM |

Dependencias Python:

```
websockets
```

---

## Cómo ejecutar

> **Orden importante:** primero el servidor Python, luego Unity.

**1. Iniciar el servidor Python**

```bash
python server.py
```

El servidor escucha en `ws://localhost:8765` y espera conexión de Unity antes de arrancar la simulación.

**2. Abrir Unity y darle Play**

Abrir la escena `Assets/Scenes/SampleScene.unity` y presionar Play. Unity se conecta automáticamente al WebSocket.

**3. Iniciar la simulación**

En el panel de control (esquina derecha de la pantalla), ajusta los parámetros y pulsa **INICIAR**.

---

## Panel de control

| Campo         | Descripción                         | Default |
| ------------- | ----------------------------------- | ------- |
| Size          | Tamaño del grid (N×N)               | 25      |
| Cosechadoras  | Número de cosechadoras              | 3       |
| Tractores     | Número de tractores                 | 2       |
| Obstáculos    | Densidad de obstáculos (0.0–1.0)    | 0.05    |
| Cap. gasolina | Capacidad de combustible por agente | 500     |
| Cap. grano    | Capacidad de grano por cosechadora  | 100     |

Botones disponibles: **INICIAR / REINICIAR**, **PAUSAR / CONTINUAR**.

---

## Vistas de cámara

- **ISO** — vista isométrica general del campo
- **Top** — vista cenital
- **POV Cosechadora** — cámara en primera persona siguiendo a una cosechadora (botón _next_ para cambiar de agente)
- **POV Tractor** — ídem para tractores

---

## Gráficas en tiempo real

- **Barras de combustible** — nivel actual de cada agente (verde > 60%, amarillo > 30%, rojo ≤ 30%)
- **Líneas de grano** — porcentaje de capacidad de grano por cosechadora a lo largo del tiempo

---

## Protocolo WebSocket

Ver [`WEBSOCKET_PROTOCOL.md`](./WEBSOCKET_PROTOCOL.md) para la especificación completa del contrato de comunicación entre Python y Unity.

---

## Estructura del repositorio

```
Assets/
  Scripts/
    WebSocketManager.cs       # cliente WebSocket + visualización (activo)
    FuelBarChartManager.cs    # gráfica de barras de combustible
    GrainFillChartManager.cs  # gráfica de líneas de grano
    GrainFillLineChartUI.cs   # renderer de la gráfica de líneas
    AgentFuelRowUI.cs         # fila individual de combustible
    PageManager.cs            # navegación entre páginas del dashboard
  Prefabs/                    # modelos de agentes y elementos de terreno
  OBJS/                       # assets 3D por autor del equipo
  Scenes/SampleScene.unity    # escena principal
server/
  server.py                   # servidor de simulación Python
  requirements.txt
```

---
