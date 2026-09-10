using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NativeWebSocket;

[Serializable]
public class AgentData
{
    public string id;
    public string tipo;          // "cosechadora" o "tractor"
    public int x;
    public int z;
    public int grano;
    public int capacidad;
    public float combustible;
    public float combustible_maximo;
    public int recolectado;      // solo cosechadoras
    public bool terminado;       // solo cosechadoras
    public int entregado; 
    public float gasolina;       // solo tractores
}

[Serializable]
public class CeldaCambiada
{
    public int x;
    public int z;
    public int tipo;             // siempre 0 (vacio) por ahora
}

[Serializable]
public class SiloData
{
    public int x;
    public int z;
}

[Serializable]
public class SimulationData
{
    public bool paused;
    public bool iniciado;
    public int step;
    public bool terminado;
    public float tick;
    public int size;
    public SiloData silo;
    public int total_cultivo;
    public int[] terreno;                    // SOLO viene lleno en el primer
                                              // mensaje o tras un reset
    public CeldaCambiada[] celdas_cambiadas;
    public AgentData[] agentes;
}

// ============================================================
// INTERPOLACION DE UN AGENTE ENTRE DOS ESTADOS
// ============================================================

public class AgenteVisual
{
    public GameObject objeto;
    public Vector3 posInicial;
    public Vector3 posObjetivo;
    public Quaternion rotInicial;
    public Quaternion rotObjetivo;
    public float tiempoInicio;
}

public class WebSocketManager : MonoBehaviour
{
    private WebSocket websocket;

    [Header("Prefabs de agentes")]
    public GameObject cosechadoraPrefab;
    public GameObject tractorPrefab;

    [Header("Terreno")]
    public GameObject siloPrefab;            // opcional

    // Prefab especifico para las celdas con cultivo (por
    // ejemplo un modelo de trigo o una planta).
    public GameObject cultivoPrefab;

    // Varios prefabs posibles para obstaculos (rocas, arboles,
    // etc): en cada celda con obstaculo se elige uno al azar,
    // para que no se vea todo repetido.
    public GameObject[] obstaculoPrefabs;

    [Tooltip("Prefab de tierra que se coloca debajo de cada celda del terreno al cargarlo")]
    public GameObject tierraPrefab;

    [Tooltip("Altura (Y) a la que se coloca la tierra. Si queda igual que el Plane del fondo, se ven mezclados (z-fighting)")]
    public float alturaTierra = 0.02f;

    [Header("Posicion de los agentes")]
    [Tooltip("Altura (Y) a la que se colocan los agentes. Si vuelan, bajala a 0")]
    public float alturaAgentes = 0f;

    [Header("Rotacion de los agentes al moverse (grados en Y)")]
    [Tooltip("Hacia donde debe mirar el prefab cuando se mueve hacia X positivo (la fila aumenta)")]
    public float anguloMovXPositivo = 0f;
    [Tooltip("Hacia donde debe mirar el prefab cuando se mueve hacia X negativo (la fila disminuye)")]
    public float anguloMovXNegativo = 180f;
    [Tooltip("Hacia donde debe mirar el prefab cuando se mueve hacia Z positivo (la columna aumenta)")]
    public float anguloMovZPositivo = 90f;
    [Tooltip("Hacia donde debe mirar el prefab cuando se mueve hacia Z negativo (la columna disminuye)")]
    public float anguloMovZNegativo = 270f;

    [Header("Interfaz")]
    public TMP_Text granoRecolectadoText;
    public TMP_Text combustibleConsumidoText;
    public TMP_Text pauseButtonText;
    public TMP_Text iniciarReiniciarButtonText;
    public TMP_InputField inputSize;
    public TMP_InputField inputCosechadoras;
    public TMP_InputField inputTractores;
    public TMP_InputField inputObstaculo;
    public TMP_InputField inputCapacidadGasolina;
    public TMP_InputField inputCapacidadGrano; 

    [Header("Interfaz POV")]
    [Tooltip("Texto que muestra a que cosechadora esta siguiendo la camara POV")]
    public TMP_Text povCosechadoraText;
    [Tooltip("Texto que muestra a que tractor esta siguiendo la camara POV")]
    public TMP_Text povTractorText;

    [Header("Escenario / Fondo")]
    [Tooltip("El objeto vacío que contiene todos los assets de fondo")]
    public Transform escenarioFondo;

    [Tooltip("El tamaño de grid (size) para el cual el fondo fue diseñado originalmente")]
    public int tamanoDisenoFondo = 25;

    [Tooltip("Si el pivote del fondo está en una esquina en vez de en el centro, actívalo")]
    public bool pivoteEnEsquina = false;

    [Header("Camaras")]
    public Transform camaraISO;
    public Transform camaraTop;

    [Tooltip("Tamaño de grid para el cual estan calibrados los valores de esta seccion. Al cambiar el tamaño del terreno, las camaras se reescalan tomando esto como referencia")]
    public int tamanoDisenoCamaras = 25;

    [Tooltip("Offset (X,Y,Z) de la camara ISO respecto al centro del grid, para cuando el grid mide tamanoDisenoCamaras. Dejalo en (0,0,0) para calcularlo automaticamente desde la posicion de la camara en la escena")]
    public Vector3 offsetCamaraISODiseno = Vector3.zero;

    [Tooltip("Altura (Y) de la camara Top, para cuando el grid mide tamanoDisenoCamaras. Dejalo en 0 para calcularla automaticamente desde la posicion de la camara en la escena")]
    public float alturaCamaraTopDiseno = 0f;

    private float alturaCamaraTopBase = 30f;
    private Vector3 offsetCamaraISO;

    [Header("Camaras POV")]
    public Transform camaraPOVCosechadora;
    public Transform camaraPOVTractor;

    [Tooltip("Posicion local de la camara relativa a la cosechadora (arriba y atras, por ejemplo)")]
    public Vector3 offsetPOV = new Vector3(0f, 1.2f, -1.5f);

    [Tooltip("Rotacion local de la camara relativa a la cosechadora")]
    public Vector3 rotacionOffsetPOV = Vector3.zero;

    [Header("Graficos")]
    public FuelBarChartManager fuelBarChartManager;
    public GrainFillChartManager grainFillChartManager;

    [Header("Panel de inicio")]
    [Tooltip("Panel que se muestra antes de iniciar la simulacion, con un mensaje como 'Inicie la simulacion para visualizar datos'")]
    public GameObject panelInicio;

    [Tooltip("Referencia al PageManager que controla las paginas de datos")]
    public PageManager pageManager;

    private string idAgentePOVCosechadora = null;   // id de la cosechadora que está siguiendo
    private string idAgentePOVTractor = null;   // id del tractor que está siguiendo

    // ------------------------------------------------------
    // Estado interno
    // ------------------------------------------------------

    private Dictionary<string, AgenteVisual> agentes =
        new Dictionary<string, AgenteVisual>();

    private GameObject[] decoraciones;        // decoracion actual de cada celda (o null)
    private GameObject[] sueloDecoraciones;   // parche de tierra bajo cada celda, uno por celda al cargar el terreno
    private int tamanoActual = -1;
    private float tickActual = 0.25f;
    private float tiempoUltimoMensaje = -1f;   // Time.time del ultimo estado recibido, para medir el intervalo real
    private GameObject siloInstancia;
    private AgentData[] ultimosAgentesRecibidos;   // ultima lista de agentes recibida del servidor

    private bool simulacionIniciada = false;

    // ------------------------------------------------------
    // Conexion
    // ------------------------------------------------------

    async void Start()
    {
        if (camaraISO != null)
        {
            if (offsetCamaraISODiseno != Vector3.zero)
            {
                offsetCamaraISO = offsetCamaraISODiseno;
            }
            else
            {
                float centroInicial = (tamanoDisenoCamaras - 1) / 2f;
                Vector3 centroGridInicial = new Vector3(centroInicial, 0f, centroInicial);
                offsetCamaraISO = camaraISO.position - centroGridInicial;
            }
        }

        if (camaraTop != null)
        {
            alturaCamaraTopBase = alturaCamaraTopDiseno > 0f
                ? alturaCamaraTopDiseno
                : camaraTop.position.y;
        }

        granoRecolectadoText.text = "0";
        combustibleConsumidoText.text = "0";
        ActualizarTextoIniciarReiniciar();

        MostrarPanelInicio();

        websocket = new WebSocket("ws://localhost:8765");

        websocket.OnOpen += () =>
        {
            Debug.Log("Conectado al servidor");
        };

        websocket.OnError += (error) =>
        {
            Debug.LogError("Error WebSocket: " + error);
        };

        websocket.OnClose += (closeCode) =>
        {
            Debug.Log("Conexion cerrada");
        };

        websocket.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            SimulationData data = JsonUtility.FromJson<SimulationData>(message);
            UpdateSimulation(data);
        };

        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        InterpolarAgentes();
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }

    // ------------------------------------------------------
    // Procesar un estado recibido
    // ------------------------------------------------------

    void UpdateSimulation(SimulationData data)
    {
        if (data == null || data.agentes == null)
        {
            return;
        }

        // Usamos el tiempo real transcurrido desde el mensaje anterior
        // para la interpolacion, en vez de confiar ciegamente en
        // "data.tick": si el servidor manda estados mas seguido de lo
        // que dice su propio tick, el visual se va atrasando cada vez
        // mas respecto a la posicion real que reporta el servidor.
        float ahora = Time.time;

        if (tiempoUltimoMensaje > 0f)
        {
            float intervaloReal = ahora - tiempoUltimoMensaje;

            if (intervaloReal > 0.01f)
            {
                tickActual = intervaloReal;
            }
        }
        else if (data.tick > 0f)
        {
            tickActual = data.tick;
        }

        tiempoUltimoMensaje = ahora;

        if (data.terreno != null && data.terreno.Length > 0)
        {
            ReconstruirTerreno(data);
        }

        if (data.celdas_cambiadas != null)
        {
            AplicarCeldasCambiadas(data.celdas_cambiadas);
        }

        UpdateAgents(data);
        fuelBarChartManager?.ActualizarDesdeEstado(data);
        grainFillChartManager?.ActualizarDesdeEstado(data); 
        RemoveMissingAgents(data);
        ultimosAgentesRecibidos = data.agentes;
        AsignarCamaraPOVCosechadora(data);
        AsignarCamaraPOVTractor(data);
        UpdateInterface(data);
    }

    // ------------------------------------------------------
    // Terreno
    // ------------------------------------------------------

    void ReconstruirTerreno(SimulationData data)
    {
        Debug.Log(
            $"[DIAGNOSTICO] ReconstruirTerreno llamado. size={data.size} " +
            $"terreno.Length={data.terreno.Length} " +
            $"decoraciones previas={(decoraciones == null ? "null" : decoraciones.Length.ToString())}"
        );

        // Si ya habia decoraciones puestas, las destruimos
        // primero (esto pasa cuando cambia el tamaño en un reset).
        if (decoraciones != null)
        {
            foreach (GameObject decoracion in decoraciones)
            {
                if (decoracion != null)
                {
                    Destroy(decoracion);
                }
            }
        }

        if (sueloDecoraciones != null)
        {
            foreach (GameObject suelo in sueloDecoraciones)
            {
                if (suelo != null)
                {
                    Destroy(suelo);
                }
            }
        }

        int size = data.size;
        tamanoActual = size;
        decoraciones = new GameObject[size * size];
        sueloDecoraciones = new GameObject[size * size];

        AjustarEscenario(size);
        AjustarCamaras(size);    

        int contadorCultivo = 0;
        int contadorObstaculo = 0;
        int contadorDecoracionesCreadas = 0;

        for (int fila = 0; fila < size; fila++)
        {
            for (int columna = 0; columna < size; columna++)
            {
                int indice = fila * size + columna;
                Vector3 pos = new Vector3(fila, 0.001f, columna);
                int tipo = data.terreno[indice];

                if (tipo == 1) contadorCultivo++;
                if (tipo == 2) contadorObstaculo++;

                ActualizarDecoracion(indice, pos, tipo);

                if (tierraPrefab != null)
                {
                    Vector3 posSuelo = new Vector3(fila, alturaTierra, columna);
                    sueloDecoraciones[indice] = Instantiate(
                        tierraPrefab, posSuelo, Quaternion.identity, transform
                    );
                }

                if (decoraciones[indice] != null) contadorDecoracionesCreadas++;
            }
        }

        Debug.Log(
            $"[DIAGNOSTICO] Terreno reconstruido: cultivo={contadorCultivo} " +
            $"obstaculo={contadorObstaculo} decoracionesCreadas={contadorDecoracionesCreadas} " +
            $"(cultivoPrefab asignado={cultivoPrefab != null}, " +
            $"obstaculoPrefabs asignados={obstaculoPrefabs != null && obstaculoPrefabs.Length > 0})"
        );

        // Silo
        if (siloPrefab != null)
        {
            Vector3 posSilo = new Vector3(data.silo.x, 0.1f, data.silo.z);

            if (siloInstancia == null)
            {
                siloInstancia = Instantiate(siloPrefab, posSilo, Quaternion.identity);
            }
            else
            {
                siloInstancia.transform.position = posSilo;
            }
        }
    }

    void AplicarCeldasCambiadas(CeldaCambiada[] celdas)
    {
        if (decoraciones == null || tamanoActual <= 0)
        {
            return;
        }

        foreach (CeldaCambiada celda in celdas)
        {
            int indice = celda.x * tamanoActual + celda.z;

            if (indice >= 0 && indice < decoraciones.Length)
            {
                Vector3 pos = new Vector3(celda.x, 0f, celda.z);
                ActualizarDecoracion(indice, pos, celda.tipo);
            }
        }
    }

    // Coloca (o quita) la decoracion de una celda: un prefab
    // especifico para cultivo, uno elegido al azar de la lista
    // para obstaculo, o nada si esta vacia (queda solo el Plane
    // de la escena, sin ningun objeto encima).
    void ActualizarDecoracion(int indice, Vector3 pos, int tipo)
    {
        if (decoraciones[indice] != null)
        {
            Destroy(decoraciones[indice]);
            decoraciones[indice] = null;
        }

        // 0 = vacio, 1 = cultivo, 2 = obstaculo
        if (tipo == 1 && cultivoPrefab != null)
        {
            decoraciones[indice] = Instantiate(
                cultivoPrefab, pos, Quaternion.identity, transform
            );
        }
        else if (tipo == 2 && obstaculoPrefabs != null && obstaculoPrefabs.Length > 0)
        {
            GameObject elegido = obstaculoPrefabs[
                UnityEngine.Random.Range(0, obstaculoPrefabs.Length)
            ];
            decoraciones[indice] = Instantiate(
                elegido, pos, Quaternion.identity, transform
            );
        }
    }

    void AjustarEscenario(int size)
    {
        if (escenarioFondo == null || tamanoDisenoFondo <= 0)
        {
            return;
        }

        float factor = (float)size / tamanoDisenoFondo;
        escenarioFondo.localScale = new Vector3(factor, 1f, factor);

        if (pivoteEnEsquina)
        {
            // Si el fondo crece desde una esquina, no hace falta
            // reposicionar nada extra.
            escenarioFondo.position = Vector3.zero;
        }
        else
        {
            // Si el pivote está en el centro del fondo, hay que
            // recentrarlo para que siga alineado con el grid,
            // que va de (0,0) a (size-1, size-1).
            float centro = (size - 1) / 2f;
            escenarioFondo.position = new Vector3(centro, 0f, centro);
        }
    }

    void AjustarCamaras(int size)
    {
        if (tamanoDisenoCamaras <= 0)
        {
            return;
        }

        float factor = (float)size / tamanoDisenoCamaras;
        // Centro del grid: las celdas van de (0,0) a (size-1, size-1),
        // igual que en AjustarEscenario.
        float centro = (size - 1) / 2f;

        if (camaraISO != null)
        {
            // Escala el offset completo (X, Y, Z) para mantener el
            // mismo angulo isometrico, solo que mas lejos/mas alto.
            Vector3 centroGrid = new Vector3(centro, 0f, centro);
            camaraISO.position = centroGrid + offsetCamaraISO * factor;
        }

        if (camaraTop != null)
        {
            Vector3 pos = camaraTop.position;
            pos.x = centro;
            pos.y = alturaCamaraTopBase * factor;
            pos.z = centro;
            camaraTop.position = pos;
        }
    }

    void AsignarCamaraPOVCosechadora(SimulationData data)
    {
        if (camaraPOVCosechadora == null)
        {
            return;
        }

        // Si ya tenemos un agente asignado, revisamos que siga existiendo
        bool sigueExistiendo = idAgentePOVCosechadora != null && agentes.ContainsKey(idAgentePOVCosechadora);

        if (sigueExistiendo)
        {
            return; // todo bien, sigue pegada a la misma cosechadora
        }

        // Buscar la primera cosechadora disponible en el estado actual
        foreach (AgentData agentData in data.agentes)
        {
            if (agentData.tipo == "cosechadora")
            {
                idAgentePOVCosechadora = agentData.id;
                PegarCamaraA(agentes[agentData.id].objeto, camaraPOVCosechadora, povCosechadoraText, agentData.id);
                Debug.Log("Camara POV asignada a: " + idAgentePOVCosechadora);
                return;
            }
        }

        // No hay ninguna cosechadora disponible
        idAgentePOVCosechadora = null;
        ActualizarTextoPOV(povCosechadoraText, null);
    }

    void AsignarCamaraPOVTractor(SimulationData data)
    {
        if (camaraPOVTractor == null)
        {
            return;
        }

        // Si ya tenemos un agente asignado, revisamos que siga existiendo
        bool sigueExistiendo = idAgentePOVTractor != null && agentes.ContainsKey(idAgentePOVTractor);

        if (sigueExistiendo)
        {
            return; // todo bien, sigue pegada al mismo tractor
        }

        // Buscar el primer tractor disponible en el estado actual
        foreach (AgentData agentData in data.agentes)
        {
            if (agentData.tipo == "tractor")
            {
                idAgentePOVTractor = agentData.id;
                PegarCamaraA(agentes[agentData.id].objeto, camaraPOVTractor, povTractorText, agentData.id);
                Debug.Log("Camara POV asignada a: " + idAgentePOVTractor);
                return;
            }
        }

        // No hay ningun tractor disponible
        idAgentePOVTractor = null;
        ActualizarTextoPOV(povTractorText, null);
    }

    void PegarCamaraA(GameObject objetoAgente, Transform camara, TMP_Text texto, string idAgente)
    {
        camara.SetParent(objetoAgente.transform);
        camara.localPosition = offsetPOV;
        camara.localRotation = Quaternion.Euler(rotacionOffsetPOV);
        ActualizarTextoPOV(texto, idAgente);
    }

    // Muestra en el texto de la UI a que agente esta pegada la
    // camara POV (o lo deja vacio si no hay ninguno).
    void ActualizarTextoPOV(TMP_Text texto, string idAgente)
    {
        if (texto == null)
        {
            return;
        }

        texto.text = idAgente != null ? ("POV: " + idAgente) : "POV: -";
    }

    // Conectar estos metodos al OnClick() de los botones en el
    // Inspector para pasar al siguiente agente de cada tipo.
    public void SiguientePOVCosechadora()
    {
        SiguienteAgentePOV("cosechadora", ref idAgentePOVCosechadora, camaraPOVCosechadora, povCosechadoraText);
    }

    public void SiguientePOVTractor()
    {
        SiguienteAgentePOV("tractor", ref idAgentePOVTractor, camaraPOVTractor, povTractorText);
    }

    // Busca, dentro del ultimo estado recibido, todos los agentes
    // del tipo pedido y pasa al siguiente (en orden, dando la
    // vuelta al llegar al final). Si no hay ninguno, no hace nada.
    void SiguienteAgentePOV(string tipo, ref string idActual, Transform camara, TMP_Text texto)
    {
        if (camara == null || ultimosAgentesRecibidos == null)
        {
            return;
        }

        List<string> idsDelTipo = new List<string>();

        foreach (AgentData agentData in ultimosAgentesRecibidos)
        {
            if (agentData.tipo == tipo && agentes.ContainsKey(agentData.id))
            {
                idsDelTipo.Add(agentData.id);
            }
        }

        if (idsDelTipo.Count == 0)
        {
            return;
        }

        int indiceActual = idActual != null ? idsDelTipo.IndexOf(idActual) : -1;
        int siguienteIndice = (indiceActual + 1) % idsDelTipo.Count;
        string siguienteId = idsDelTipo[siguienteIndice];

        idActual = siguienteId;
        PegarCamaraA(agentes[siguienteId].objeto, camara, texto, siguienteId);
        Debug.Log("Camara POV cambiada a: " + siguienteId);
    }

    // ------------------------------------------------------
    // Agentes
    // ------------------------------------------------------

    void UpdateAgents(SimulationData data)
    {
        foreach (AgentData agentData in data.agentes)
        {
            Vector3 nuevaPos = new Vector3(agentData.x, alturaAgentes, agentData.z);

            if (!agentes.ContainsKey(agentData.id))
            {
                GameObject prefab = agentData.tipo == "tractor"
                    ? tractorPrefab
                    : cosechadoraPrefab;

                GameObject nuevoAgente = Instantiate(prefab, nuevaPos, Quaternion.identity);
                nuevoAgente.name = "Agent_" + agentData.id;

                AgenteVisual visual = new AgenteVisual
                {
                    objeto = nuevoAgente,
                    posInicial = nuevaPos,
                    posObjetivo = nuevaPos,
                    rotInicial = nuevoAgente.transform.rotation,
                    rotObjetivo = nuevoAgente.transform.rotation,
                    tiempoInicio = Time.time
                };

                agentes.Add(agentData.id, visual);
                Debug.Log("Nuevo agente creado: " + agentData.id);
            }

            AgenteVisual av = agentes[agentData.id];

            // Partimos desde donde esta ahora visualmente (no
            // desde el ultimo objetivo) para que la interpolacion
            // no de un salto si el mensaje anterior no termino.
            av.posInicial = av.objeto.transform.position;
            av.rotInicial = av.objeto.transform.rotation;

            // Si de verdad se movio, giramos el prefab al angulo
            // que configuraste para esa direccion (arriba, en el
            // Inspector). Si no se movio (esta esperando, cargando
            // gasolina, etc), se queda mirando hacia donde ya
            // estaba mirando.
            Vector3 direccion = nuevaPos - av.posInicial;

            if (direccion.sqrMagnitude > 0.0001f)
            {
                float angulo;

                // Se mueve mas en X que en Z: fue un paso
                // arriba/abajo (fila). Si no, fue izquierda/
                // derecha (columna).
                if (Mathf.Abs(direccion.x) > Mathf.Abs(direccion.z))
                {
                    angulo = direccion.x > 0f ? anguloMovXPositivo : anguloMovXNegativo;
                }
                else
                {
                    angulo = direccion.z > 0f ? anguloMovZPositivo : anguloMovZNegativo;
                }

                av.rotObjetivo = Quaternion.Euler(0f, angulo, 0f);
            }

            av.posObjetivo = nuevaPos;
            av.tiempoInicio = Time.time;

            ActualizarInfoAgente(av.objeto, agentData);
        }
    }

    void InterpolarAgentes()
    {
        foreach (AgenteVisual av in agentes.Values)
        {
            if (av.objeto == null)
            {
                continue;
            }

            float t = tickActual > 0f
                ? (Time.time - av.tiempoInicio) / tickActual
                : 1f;

            t = Mathf.Clamp01(t);

            av.objeto.transform.position = Vector3.Lerp(
                av.posInicial, av.posObjetivo, t
            );

            av.objeto.transform.rotation = Quaternion.Slerp(
                av.rotInicial, av.rotObjetivo, t
            );
        }
    }

    void ActualizarInfoAgente(GameObject objeto, AgentData agentData)
    {
        // Si el prefab tiene un TextMeshPro hijo (por ejemplo
        // llamado "InfoText"), le actualizamos el texto con el
        // grano y el combustible. Si no existe, no pasa nada.
        TextMeshPro info = objeto.GetComponentInChildren<TextMeshPro>();

        if (info == null)
        {
            return;
        }

        if (agentData.tipo == "cosechadora")
        {
            info.text = $"{agentData.id}\n" +
                        $"grano {agentData.grano}/{agentData.capacidad}\n" +
                        $"comb {agentData.combustible:0}/{agentData.combustible_maximo:0}";
        }
        else
        {
            info.text = $"{agentData.id}\n" +
                        $"grano {agentData.grano}/{agentData.capacidad}\n" +
                        $"comb {agentData.combustible:0}/{agentData.combustible_maximo:0}\n" +
                        $"entregado {agentData.entregado}";
        }
    }

    void RemoveMissingAgents(SimulationData data)
    {
        HashSet<string> idsRecibidos = new HashSet<string>();

        foreach (AgentData agentData in data.agentes)
        {
            idsRecibidos.Add(agentData.id);
        }

        List<string> idsAEliminar = new List<string>();

        foreach (string id in agentes.Keys)
        {
            if (!idsRecibidos.Contains(id))
            {
                idsAEliminar.Add(id);
            }
        }

        foreach (string id in idsAEliminar)
        {
            // Si la camara POV esta pegada a este agente, la
            // desprendemos antes de destruirlo. Si no, al ser
            // hija de su transform, Unity la destruiria junto
            // con el agente y se perderia para siempre.
            if (id == idAgentePOVCosechadora)
            {
                DespegarCamara(camaraPOVCosechadora);
                idAgentePOVCosechadora = null;
                ActualizarTextoPOV(povCosechadoraText, null);
            }

            if (id == idAgentePOVTractor)
            {
                DespegarCamara(camaraPOVTractor);
                idAgentePOVTractor = null;
                ActualizarTextoPOV(povTractorText, null);
            }

            Destroy(agentes[id].objeto);
            agentes.Remove(id);
            Debug.Log("Agente eliminado: " + id);
        }
    }

    void DespegarCamara(Transform camara)
    {
        if (camara == null)
        {
            return;
        }

        camara.SetParent(null);
    }

    // ------------------------------------------------------
    // Interfaz
    // ------------------------------------------------------

    void UpdateInterface(SimulationData data)
    {
        if (data.iniciado && !simulacionIniciada)
        {
            simulacionIniciada = true;

            // Si nos conectamos a una simulacion que el servidor ya
            // tenia corriendo (en vez de arrancarla nosotros con el
            // boton), igual hay que quitar el panel de inicio.
            MostrarPrimeraPagina();
        }

        ActualizarTextoIniciarReiniciar();

        if (!data.terminado)
        {
            pauseButtonText.text = data.paused ? "CONTINUAR" : "PAUSAR";
        }

        int totalGranoRecolectado = 0;
        float totalCombustibleConsumido = 0f;

        foreach (AgentData agentData in data.agentes)
        {
            // Solo las cosechadoras tienen "recolectado"
            if (agentData.tipo == "cosechadora")
            {
                totalGranoRecolectado += agentData.recolectado;
            }

            // El combustible consumido suma cosechadoras Y tractores
            totalCombustibleConsumido += agentData.gasolina;
        }

        granoRecolectadoText.text = totalGranoRecolectado.ToString();
        combustibleConsumidoText.text = totalCombustibleConsumido.ToString("0.0");
    }

    // ------------------------------------------------------
    // Comandos hacia Python
    // ------------------------------------------------------

    async void EnviarComando(string json)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            return;
        }

        await websocket.SendText(json);
    }

    public void TogglePause()
    {
        string command = pauseButtonText.text == "PAUSAR"
            ? "{\"command\":\"pause\"}"
            : "{\"command\":\"resume\"}";

        EnviarComando(command);
    }

    void ActualizarTextoIniciarReiniciar()
    {
        if (iniciarReiniciarButtonText == null)
        {
            return;
        }

        iniciarReiniciarButtonText.text = simulacionIniciada ? "REINICIAR" : "INICIAR";
    }

    public void OnClickIniciarReiniciar()
    {
        if (!simulacionIniciada)
        {
            string json = ConstruirComandoConConfig("iniciar");
            EnviarComando(json);
            simulacionIniciada = true;
            ActualizarTextoIniciarReiniciar();
            MostrarPrimeraPagina();
        }
        else
        {
            ResetSimulation();
        }
    }

    public void ResetSimulation()
    {
        // Primero limpiamos el terreno que se ve ahorita, para
        // que desaparezca de inmediato al picarle al boton (sin
        // esperar a que llegue el campo nuevo). Cuando llegue el
        // siguiente mensaje con el terreno fresco, ReconstruirTerreno
        // lo vuelve a construir desde cero.
        LimpiarTerrenoVisual();
        fuelBarChartManager?.LimpiarTodo();
        grainFillChartManager?.LimpiarTodo();

        string json = ConstruirComandoConConfig("reset");
        EnviarComando(json);
    }

    // Arma el JSON de comando ("iniciar" o "reset") leyendo los
    // valores actuales de los inputs de la UI, para no repetir
    // esta lectura en los dos lugares donde se necesita.
    string ConstruirComandoConConfig(string comando)
    {
        int size = LeerEntero(inputSize, 25);
        int cosechadoras = LeerEntero(inputCosechadoras, 3);
        int tractores = LeerEntero(inputTractores, 2);
        float obstaculo = LeerFlotante(inputObstaculo, 0.05f);
        float capacidadGasolina = LeerFlotante(inputCapacidadGasolina, 500f);
        int capacidadGrano = LeerEntero(inputCapacidadGrano, 100);

        return
            "{\"command\":\"" + comando + "\",\"config\":{" +
            $"\"size\":{size}," +
            $"\"n_cosechadoras\":{cosechadoras}," +
            $"\"n_tractores\":{tractores}," +
            $"\"densidad_obstaculo\":{obstaculo.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"capacidad_gasolina\":{capacidadGasolina.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"\"capacidad_grano\":{capacidadGrano}" +
            "}}";
    }

    void LimpiarTerrenoVisual()
    {
        if (decoraciones != null)
        {
            foreach (GameObject decoracion in decoraciones)
            {
                if (decoracion != null)
                {
                    Destroy(decoracion);
                }
            }
        }

        if (sueloDecoraciones != null)
        {
            foreach (GameObject suelo in sueloDecoraciones)
            {
                if (suelo != null)
                {
                    Destroy(suelo);
                }
            }
        }

        decoraciones = null;
        sueloDecoraciones = null;
        tamanoActual = -1;

        Debug.Log("[DIAGNOSTICO] Terreno visual limpiado por el boton de Reset");
    }

    int LeerEntero(TMP_InputField campo, int porDefecto)
    {
        if (campo == null || string.IsNullOrEmpty(campo.text))
        {
            return porDefecto;
        }

        return int.TryParse(campo.text, out int valor) ? valor : porDefecto;
    }

    float LeerFlotante(TMP_InputField campo, float porDefecto)
    {
        if (campo == null || string.IsNullOrEmpty(campo.text))
        {
            return porDefecto;
        }

        return float.TryParse(
            campo.text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float valor
        ) ? valor : porDefecto;
    }

    // Estado antes de arrancar: panel de inicio visible, paginas
    // de datos apagadas (todavia no hay nada que mostrar).
    void MostrarPanelInicio()
    {
        if (panelInicio != null)
        {
            panelInicio.SetActive(true);
        }
    }

    // Se llama al iniciar la simulacion: apaga el panel de inicio,
    // prende el contenedor de paginas y lo deja en la primera pagina.
    void MostrarPrimeraPagina()
    {
        if (panelInicio != null)
        {
            panelInicio.SetActive(false);
        }

        pageManager?.MostrarPrimeraPagina();
    }
}