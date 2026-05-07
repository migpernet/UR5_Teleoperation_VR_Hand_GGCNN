using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudReceiver : MonoBehaviour
{
    [Header("Configurações do ROS")]
    public string pointCloudTopic = "/points_fixed_to_base";
    
    // Variáveis da Malha
    private Mesh mesh;
    private Vector3[] vertices;
    private Color32[] colors;
    private int[] indices;
    
    // Referência para o ROS Connection
    private ROSConnection ros;

    void Start()
    {
        // Prepara a malha (Mesh) que vai receber os pontos
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = mesh;
        
        // Conecta ao ROS
        ros = ROSConnection.GetOrCreateInstance();
        
        // Inscreve no tópico da nuvem de pontos (Fica sempre ligado)
        ros.Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
        
        Debug.Log("[PointCloud] Receptor iniciado. Transmissão contínua ativada.");
    }

    void ReceivePointCloud(PointCloud2Msg msg)
    {
        int numPoints = (int)(msg.width * msg.height);
        int pointStep = (int)msg.point_step;

        // Inicializa ou redimensiona os arrays de vértices e cores se o tamanho da nuvem mudar
        if (vertices == null || vertices.Length != numPoints)
        {
            vertices = new Vector3[numPoints];
            colors = new Color32[numPoints];
            indices = new int[numPoints];
            for (int i = 0; i < numPoints; i++) indices[i] = i;
        }

        int xOffset = 0, yOffset = 0, zOffset = 0, rgbOffset = -1;
        
        // Procura onde estão as coordenadas X, Y, Z e as Cores (RGB) dentro do pacote de bytes
        foreach (var field in msg.fields)
        {
            if (field.name == "x") xOffset = (int)field.offset;
            if (field.name == "y") yOffset = (int)field.offset;
            if (field.name == "z") zOffset = (int)field.offset;
            if (field.name == "rgb" || field.name == "rgba") rgbOffset = (int)field.offset;
        }

        // Desempacota os bytes
        for (int i = 0; i < numPoints; i++)
        {
            int byteIndex = i * pointStep;
            float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
            float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
            float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

            // Verifica se o ponto é válido (não é Not-a-Number)
            if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
            {
                // Converte do padrão ROS (Right-Handed) para o padrão Unity (Left-Handed)
                vertices[i] = new Vector3(-rosY, rosZ, rosX);

                if (rgbOffset != -1)
                {
                    uint rgbCode = BitConverter.ToUInt32(msg.data, byteIndex + rgbOffset);
                    byte b = (byte)((rgbCode >> 16) & 0xFF); 
                    byte g = (byte)((rgbCode >> 8) & 0xFF);  
                    byte r = (byte)(rgbCode & 0xFF);         
                    colors[i] = new Color32(r, g, b, 255);
                }
                else
                {
                    // Se não tiver cor nativa, pinta os pontos de ciano
                    colors[i] = new Color32(0, 255, 255, 255);
                }
            }
            else
            {
                // Esconde os pontos corrompidos na origem com invisibilidade (alpha = 0)
                vertices[i] = Vector3.zero;
                colors[i] = new Color32(0, 0, 0, 0);
            }
        }

        // Limpa a malha antiga para não dar erro se vierem menos pontos
        mesh.Clear(); 
        
        // Envia os dados processados para a Placa de Vídeo renderizar
        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.SetIndices(indices, MeshTopology.Points, 0); 
        mesh.RecalculateBounds();
    }
}

















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Sensor;
// using System;
// using System.Collections.Generic;
// using UnityEngine.XR.Hands; // Adicionado para Hand Tracking
// using TMPro;

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// public class PointCloudReceiver : MonoBehaviour
// {
//     [Header("Configurações do ROS")]
//     public string pointCloudTopic = "/points_fixed_to_base";
    
//     [Header("Gesto de Congelamento (Mão Esquerda)")]
//     public bool isFrozen = false;
//     [Tooltip("Distância para considerar a pinça (2.5 cm)")]
//     public float pinchThreshold = 0.025f; 
//     [Tooltip("Tempo de espera entre detecções")]
//     public float gestureCooldown = 0.5f;
    
//     private float lastToggleTime = 0f;
//     private bool wasPinching = false; 

//     [Header("Feedback Visual (UI)")]
//     public TextMeshProUGUI statusText; 
    
//     // Variáveis da Malha
//     private Mesh mesh;
//     private Vector3[] vertices;
//     private Color32[] colors;
//     private int[] indices;
    
//     // Referência para o ROS e Subsystem de Mãos
//     private ROSConnection ros;
//     private XRHandSubsystem handSubsystem;

//     void Start()
//     {
//         mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//         GetComponent<MeshFilter>().mesh = mesh;
        
//         ros = ROSConnection.GetOrCreateInstance();
        
//         // Busca o sistema de mãos
//         var handSubsystems = new List<XRHandSubsystem>();
//         SubsystemManager.GetInstances(handSubsystems);
//         if (handSubsystems.Count > 0) handSubsystem = handSubsystems[0];

//         // Inicia o sistema ATIVO (Live)
//         ros.Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//         UpdateUI(false);
//     }

//     void Update()
//     {
//         HandleGesture();
//     }

//     private void HandleGesture()
//     {
//         if (handSubsystem == null) return;

//         // Verifica a MÃO ESQUERDA
//         XRHand leftHand = handSubsystem.leftHand;
//         if (!leftHand.isTracked) return;

//         var thumb = leftHand.GetJoint(XRHandJointID.ThumbTip);
//         var middle = leftHand.GetJoint(XRHandJointID.MiddleTip);

//         if (thumb.TryGetPose(out Pose tP) && middle.TryGetPose(out Pose mP))
//         {
//             float distance = Vector3.Distance(tP.position, mP.position);
//             bool isPinching = distance < pinchThreshold;

//             // Lógica de Trigger (aciona apenas no momento do aperto)
//             if (isPinching && !wasPinching && Time.time - lastToggleTime > gestureCooldown)
//             {
//                 ToggleFreeze();
//                 lastToggleTime = Time.time;
//                 wasPinching = true;
//             }
//             else if (!isPinching)
//             {
//                 wasPinching = false;
//             }
//         }
//     }

//     private void ToggleFreeze()
//     {
//         isFrozen = !isFrozen;
        
//         if (isFrozen)
//         {
//             ros.Unsubscribe(pointCloudTopic);
//             Debug.Log("<color=cyan>Nuvem de Pontos: CONGELADA</color>");
//         }
//         else
//         {
//             ros.Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//             Debug.Log("<color=green>Nuvem de Pontos: AO VIVO</color>");
//         }
        
//         UpdateUI(isFrozen);
//     }

//     void UpdateUI(bool frozen)
//     {
//         if (statusText == null) return;
//         statusText.gameObject.SetActive(frozen); 
//         if (frozen)
//         {
//             statusText.text = "NUVEM: CONGELADA (OFFLINE)";
//             statusText.color = Color.cyan;
//         }
//     }

//     void ReceivePointCloud(PointCloud2Msg msg)
//     {
//         if (isFrozen) return;

//         int numPoints = (int)(msg.width * msg.height);
//         int pointStep = (int)msg.point_step;

//         if (vertices == null || vertices.Length != numPoints)
//         {
//             vertices = new Vector3[numPoints];
//             colors = new Color32[numPoints];
//             indices = new int[numPoints];
//             for (int i = 0; i < numPoints; i++) indices[i] = i;
//         }

//         int xOffset = 0, yOffset = 0, zOffset = 0, rgbOffset = -1;
//         foreach (var field in msg.fields)
//         {
//             if (field.name == "x") xOffset = (int)field.offset;
//             if (field.name == "y") yOffset = (int)field.offset;
//             if (field.name == "z") zOffset = (int)field.offset;
//             if (field.name == "rgb" || field.name == "rgba") rgbOffset = (int)field.offset;
//         }

//         for (int i = 0; i < numPoints; i++)
//         {
//             int byteIndex = i * pointStep;
//             float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
//             float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
//             float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

//             if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
//             {
//                 vertices[i] = new Vector3(-rosY, rosZ, rosX);

//                 if (rgbOffset != -1)
//                 {
//                     uint rgbCode = BitConverter.ToUInt32(msg.data, byteIndex + rgbOffset);
//                     byte b = (byte)((rgbCode >> 16) & 0xFF); 
//                     byte g = (byte)((rgbCode >> 8) & 0xFF);  
//                     byte r = (byte)(rgbCode & 0xFF);         
//                     colors[i] = new Color32(r, g, b, 255);
//                 }
//                 else
//                 {
//                     colors[i] = new Color32(0, 255, 255, 255);
//                 }
//             }
//             else
//             {
//                 vertices[i] = Vector3.zero;
//                 colors[i] = new Color32(0, 0, 0, 0);
//             }
//         }

//         mesh.Clear(); 
//         mesh.vertices = vertices;
//         mesh.colors32 = colors;
//         mesh.SetIndices(indices, MeshTopology.Points, 0); 
//         mesh.RecalculateBounds();
//     }
// }













// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Sensor;
// using System;
// using UnityEngine.InputSystem;
// using TMPro; // BIBLIOTECA DE TEXTO ADICIONADA

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// public class PointCloudReceiver : MonoBehaviour
// {
//     [Header("Configurações do ROS")]
//     public string pointCloudTopic = "/points_fixed_to_base";
    
//     [Header("Controle de Snapshot (Duplo Clique)")]
//     public InputActionReference leftHandPinchAction; 
//     public bool isFrozen = false;
//     public float doubleClickTime = 0.4f;
//     private float lastPinchTime = 0f;

//     [Header("Feedback Visual (UI)")]
//     [Tooltip("Arraste aqui o texto do seu Canvas (TextMeshPro)")]
//     public TextMeshProUGUI statusText; 
    
//     // Variáveis da Malha
//     private Mesh mesh;
//     private Vector3[] vertices;
//     private Color32[] colors;
//     private int[] indices;
    
//     // Referência para o ROS Connection
//     private ROSConnection ros;

//     void Start()
//     {
//         mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//         GetComponent<MeshFilter>().mesh = mesh;
        
//         ros = ROSConnection.GetOrCreateInstance();
        
//         // Inicia o sistema ATIVO (Live)
//         ros.Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//         UpdateUI(false);
//     }

//     void Update()
//     {
//         if (leftHandPinchAction != null && leftHandPinchAction.action.WasPressedThisFrame())
//         {
//             float timeSinceLastPinch = Time.time - lastPinchTime;

//             if (timeSinceLastPinch <= doubleClickTime)
//             {
//                 // Inverte o estado
//                 isFrozen = !isFrozen;
                
//                 if (isFrozen)
//                 {
//                     // === CORTA A TRANSMISSÃO DE DADOS (ECONOMIZA REDE/BATERIA) ===
//                     ros.Unsubscribe(pointCloudTopic);
//                     UpdateUI(true);
//                 }
//                 else
//                 {
//                     // === RETOMA A TRANSMISSÃO DE DADOS ===
//                     ros.Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//                     UpdateUI(false);
//                 }

//                 lastPinchTime = 0f; 
//             }
//             else
//             {
//                 lastPinchTime = Time.time;
//             }
//         }
//     }

//     // Função para atualizar o "Relógio" do operador
//     void UpdateUI(bool frozen)
//     {
//         if (statusText == null) return;

//         if (frozen)
//         {
//             // LIGA o objeto de texto na tela e define a mensagem
//             statusText.gameObject.SetActive(true); 
//             statusText.text = "NUVEM: CONGELADA (OFFLINE)";
//             statusText.color = Color.cyan;
//         }
//         else
//         {
//             // DESLIGA o objeto de texto, limpando a visão do operador
//             statusText.gameObject.SetActive(false); 
//         }
//     }

//     void ReceivePointCloud(PointCloud2Msg msg)
//     {
//         // Esta trava extra garante que pacotes atrasados no momento do corte sejam ignorados
//         if (isFrozen) return;

//         int numPoints = (int)(msg.width * msg.height);
//         int pointStep = (int)msg.point_step;

//         if (vertices == null || vertices.Length != numPoints)
//         {
//             vertices = new Vector3[numPoints];
//             colors = new Color32[numPoints];
//             indices = new int[numPoints];
//             for (int i = 0; i < numPoints; i++) indices[i] = i;
//         }

//         int xOffset = 0, yOffset = 0, zOffset = 0, rgbOffset = -1;
//         foreach (var field in msg.fields)
//         {
//             if (field.name == "x") xOffset = (int)field.offset;
//             if (field.name == "y") yOffset = (int)field.offset;
//             if (field.name == "z") zOffset = (int)field.offset;
//             if (field.name == "rgb" || field.name == "rgba") rgbOffset = (int)field.offset;
//         }

//         for (int i = 0; i < numPoints; i++)
//         {
//             int byteIndex = i * pointStep;
//             float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
//             float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
//             float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

//             if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
//             {
//                 vertices[i] = new Vector3(-rosY, rosZ, rosX);

//                 if (rgbOffset != -1)
//                 {
//                     uint rgbCode = BitConverter.ToUInt32(msg.data, byteIndex + rgbOffset);
//                     byte b = (byte)((rgbCode >> 16) & 0xFF); 
//                     byte g = (byte)((rgbCode >> 8) & 0xFF);  
//                     byte r = (byte)(rgbCode & 0xFF);         
//                     colors[i] = new Color32(r, g, b, 255);
//                 }
//                 else
//                 {
//                     colors[i] = new Color32(0, 255, 255, 255);
//                 }
//             }
//             else
//             {
//                 vertices[i] = Vector3.zero;
//                 colors[i] = new Color32(0, 0, 0, 0);
//             }
//         }

//         // =======================================================
//         // A SOLUÇÃO: Limpa a malha antiga para evitar conflito de tamanhos
//         mesh.Clear(); 
//         // =======================================================

//         // Aplica todos os novos dados na Placa de Vídeo de uma vez
//         mesh.vertices = vertices;
//         mesh.colors32 = colors;
//         // desenha pontos em vez de polígonos
//         mesh.SetIndices(indices, MeshTopology.Points, 0); 
//         mesh.RecalculateBounds();
//     }
// }









// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Sensor;
// using System;
// using UnityEngine.InputSystem;

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// public class PointCloudReceiver : MonoBehaviour
// {
//     [Header("Configurações do ROS")]
//     [Tooltip("O tópico da nuvem ancorada na base global (Pós-Voxel e PassThrough)")]
//     public string pointCloudTopic = "/points_fixed_to_base";
    
//     [Header("Controle de Snapshot (Duplo Clique)")]
//     [Tooltip("Arraste aqui a Ação de Pinça/Select da Mão Esquerda")]
//     public InputActionReference leftHandPinchAction; 
//     public bool isFrozen = false;
    
//     [Tooltip("Tempo máximo em segundos entre as pinças para considerar duplo clique")]
//     public float doubleClickTime = 0.4f;
//     private float lastPinchTime = 0f;
    
//     // Variáveis da Malha Única de Alta Performance
//     private Mesh mesh;
//     private Vector3[] vertices;
//     private Color32[] colors;
//     private int[] indices;

//     void Start()
//     {
//         mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//         GetComponent<MeshFilter>().mesh = mesh;
        
//         ROSConnection.GetOrCreateInstance().Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//     }

//     void Update()
//     {
//         // Lógica do Duplo Clique
//         if (leftHandPinchAction != null && leftHandPinchAction.action.WasPressedThisFrame())
//         {
//             float timeSinceLastPinch = Time.time - lastPinchTime;

//             if (timeSinceLastPinch <= doubleClickTime)
//             {
//                 isFrozen = !isFrozen;
//                 Debug.Log(isFrozen ? "<color=blue>SNAPSHOT CONGELADO</color>" : "<color=yellow>VÍDEO AO VIVO</color>");
//                 lastPinchTime = 0f; 
//             }
//             else
//             {
//                 lastPinchTime = Time.time;
//             }
//         }
//     }

//     void ReceivePointCloud(PointCloud2Msg msg)
//     {
//         if (isFrozen) return;

//         int numPoints = (int)(msg.width * msg.height);
//         int pointStep = (int)msg.point_step;

//         if (vertices == null || vertices.Length != numPoints)
//         {
//             vertices = new Vector3[numPoints];
//             colors = new Color32[numPoints];
//             indices = new int[numPoints];
//             for (int i = 0; i < numPoints; i++) indices[i] = i;
//         }

//         int xOffset = 0, yOffset = 0, zOffset = 0, rgbOffset = -1;
//         foreach (var field in msg.fields)
//         {
//             if (field.name == "x") xOffset = (int)field.offset;
//             if (field.name == "y") yOffset = (int)field.offset;
//             if (field.name == "z") zOffset = (int)field.offset;
//             if (field.name == "rgb" || field.name == "rgba") rgbOffset = (int)field.offset;
//         }

//         for (int i = 0; i < numPoints; i++)
//         {
//             int byteIndex = i * pointStep;

//             float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
//             float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
//             float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

//             if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
//             {
//                 vertices[i] = new Vector3(-rosY, rosZ, rosX);

//                 if (rgbOffset != -1)
//                 {
//                     uint rgbCode = BitConverter.ToUInt32(msg.data, byteIndex + rgbOffset);
                    
//                     // ==========================================
//                     // A MÁGICA DA CORREÇÃO DE CORES (BGR -> RGB)
//                     // ==========================================
//                     // Ao inverter a ordem das variáveis (b e r) que recebem os bits, 
//                     // o Unity passa a injetar o Vermelho no Vermelho e o Azul no Azul.
//                     byte b = (byte)((rgbCode >> 16) & 0xFF); 
//                     byte g = (byte)((rgbCode >> 8) & 0xFF);  
//                     byte r = (byte)(rgbCode & 0xFF);         
                    
//                     colors[i] = new Color32(r, g, b, 255);
//                 }
//                 else
//                 {
//                     colors[i] = new Color32(0, 255, 255, 255);
//                 }
//             }
//             else
//             {
//                 vertices[i] = Vector3.zero;
//                 colors[i] = new Color32(0, 0, 0, 0);
//             }
//         }

//         mesh.vertices = vertices;
//         mesh.colors32 = colors;
//         mesh.SetIndices(indices, MeshTopology.Points, 0); 
//         mesh.RecalculateBounds();
//     }
// }












// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Sensor;
// using System;
// using UnityEngine.InputSystem;

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// public class PointCloudReceiver : MonoBehaviour
// {
//     [Header("Configurações do ROS")]
//     [Tooltip("O tópico da nuvem ancorada na base global")]
//     public string pointCloudTopic = "/points_fixed_to_base";
    
//     [Header("Controle de Snapshot (Duplo Clique)")]
//     [Tooltip("Arraste aqui a Ação de Pinça/Select da Mão Esquerda")]
//     public InputActionReference leftHandPinchAction; 
//     public bool isFrozen = false;
    
//     [Tooltip("Tempo máximo em segundos entre as pinças para considerar duplo clique")]
//     public float doubleClickTime = 0.4f;
//     private float lastPinchTime = 0f;
    
//     // Variáveis da Malha Única de Alta Performance
//     private Mesh mesh;
//     private Vector3[] vertices;
//     private Color32[] colors; // Armazena as texturas (Cores Reais)
//     private int[] indices;

//     void Start()
//     {
//         // Prepara a renderização de milhões de pontos
//         mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
//         GetComponent<MeshFilter>().mesh = mesh;
        
//         ROSConnection.GetOrCreateInstance().Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//     }

//     void Update()
//     {
//         // Lógica do Duplo Clique via XR Interaction Toolkit
//         if (leftHandPinchAction != null && leftHandPinchAction.action.WasPressedThisFrame())
//         {
//             float timeSinceLastPinch = Time.time - lastPinchTime;

//             if (timeSinceLastPinch <= doubleClickTime)
//             {
//                 isFrozen = !isFrozen;
//                 Debug.Log(isFrozen ? "<color=blue>SNAPSHOT CONGELADO (Duplo Clique)</color>" : "<color=yellow>VÍDEO AO VIVO</color>");
                
//                 // Reseta o tempo para evitar acionamentos triplos indesejados
//                 lastPinchTime = 0f; 
//             }
//             else
//             {
//                 // Registra o tempo do primeiro clique
//                 lastPinchTime = Time.time;
//             }
//         }
//     }

//     void ReceivePointCloud(PointCloud2Msg msg)
//     {
//         // Trava de atualização caso o operador tenha congelado a cena
//         if (isFrozen) return;

//         int numPoints = (int)(msg.width * msg.height);
//         int pointStep = (int)msg.point_step;

//         if (vertices == null || vertices.Length != numPoints)
//         {
//             vertices = new Vector3[numPoints];
//             colors = new Color32[numPoints];
//             indices = new int[numPoints];
//             for (int i = 0; i < numPoints; i++) indices[i] = i;
//         }

//         int xOffset = 0, yOffset = 0, zOffset = 0, rgbOffset = -1;
//         foreach (var field in msg.fields)
//         {
//             if (field.name == "x") xOffset = (int)field.offset;
//             if (field.name == "y") yOffset = (int)field.offset;
//             if (field.name == "z") zOffset = (int)field.offset;
//             if (field.name == "rgb" || field.name == "rgba") rgbOffset = (int)field.offset;
//         }

//         for (int i = 0; i < numPoints; i++)
//         {
//             int byteIndex = i * pointStep;

//             float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
//             float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
//             float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

//             if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
//             {
//                 // Converte Coordenadas
//                 vertices[i] = new Vector3(-rosY, rosZ, rosX);

//                 // Converte Cores
//                 if (rgbOffset != -1)
//                 {
//                     uint rgbCode = BitConverter.ToUInt32(msg.data, byteIndex + rgbOffset);
//                     byte r = (byte)((rgbCode >> 16) & 0xFF);
//                     byte g = (byte)((rgbCode >> 8) & 0xFF);
//                     byte b = (byte)(rgbCode & 0xFF);
//                     colors[i] = new Color32(r, g, b, 255);
//                 }
//                 else
//                 {
//                     colors[i] = new Color32(0, 255, 255, 255); // Fallback Ciano
//                 }
//             }
//             else
//             {
//                 vertices[i] = Vector3.zero;
//                 colors[i] = new Color32(0, 0, 0, 0); // Invisível
//             }
//         }

//         mesh.vertices = vertices;
//         mesh.colors32 = colors;
//         mesh.SetIndices(indices, MeshTopology.Points, 0); 
//         mesh.RecalculateBounds();
//     }
// }













// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Sensor;
// using System;

// /*

// The script reads the ROS byte packets, extracts the X, Y, Z coordinates of each point, converts from FLU to RUF, and draws the points on the screen.
// */

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
// public class PointCloudReceiver : MonoBehaviour
// {
//     [Tooltip("Tópico da nuvem de pontos reduzida (Downsampled)")]
//     public string pointCloudTopic = "/camera/depth/color/points_downsampled";
    
//     private Mesh mesh;
//     private Vector3[] vertices;
//     private int[] indices;
    
//     void Start()
//     {
//         // Prepara a Malha de renderização otimizada
//         mesh = new Mesh();
//         mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Suporta mais de 65k pontos
//         GetComponent<MeshFilter>().mesh = mesh;
        
//         // Inscreve no tópico ROS
//         ROSConnection.GetOrCreateInstance().Subscribe<PointCloud2Msg>(pointCloudTopic, ReceivePointCloud);
//     }

//     void ReceivePointCloud(PointCloud2Msg msg)
//     {
//         int numPoints = (int)(msg.width * msg.height);
//         int pointStep = (int)msg.point_step;

//         // Redimensiona arrays se necessário
//         if (vertices == null || vertices.Length != numPoints)
//         {
//             vertices = new Vector3[numPoints];
//             indices = new int[numPoints];
//             for (int i = 0; i < numPoints; i++) indices[i] = i;
//         }

//         // Encontra onde os dados X, Y, Z começam dentro do pacote de bytes
//         int xOffset = 0, yOffset = 0, zOffset = 0;
//         foreach (var field in msg.fields)
//         {
//             if (field.name == "x") xOffset = (int)field.offset;
//             if (field.name == "y") yOffset = (int)field.offset;
//             if (field.name == "z") zOffset = (int)field.offset;
//         }

//         // Loop de conversão hiper-rápido de Bytes para Vector3
//         for (int i = 0; i < numPoints; i++)
//         {
//             int byteIndex = i * pointStep;

//             float rosX = BitConverter.ToSingle(msg.data, byteIndex + xOffset);
//             float rosY = BitConverter.ToSingle(msg.data, byteIndex + yOffset);
//             float rosZ = BitConverter.ToSingle(msg.data, byteIndex + zOffset);

//             // A MATEMÁTICA DE CONVERSÃO FLU (ROS) -> RUF (Unity)
//             // Filtra pontos inválidos (NaN)
//             if (!float.IsNaN(rosX) && !float.IsNaN(rosY) && !float.IsNaN(rosZ))
//             {
//                 vertices[i] = new Vector3(-rosY, rosZ, rosX);
//             }
//             else
//             {
//                 vertices[i] = Vector3.zero; 
//             }
//         }

//         // Atualiza a placa de vídeo
//         mesh.vertices = vertices;
//         // Desenha usando a topologia de PONTOS, não triângulos
//         mesh.SetIndices(indices, MeshTopology.Points, 0); 
//         mesh.RecalculateBounds();
//     }
// }