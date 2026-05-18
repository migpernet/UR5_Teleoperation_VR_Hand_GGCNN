// Este script é responsável por receber as poses de preensão calculadas pela GGCNN via ROS e atualizar a posição, rotação e abertura da garra holográfica no Unity. Ele também inclui opções de suavização para evitar jitter e correções para alinhar o modelo 3D com o sistema de coordenadas do ROS.

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;

public class GGCNN_Subscriber : MonoBehaviour
{
    [Header("Configurações de Tópicos ROS")]
    public string poseTopic = "/ggcnn/unity_target_pose";
    public string widthTopic = "/ggcnn/unity_gripper_width";
    
    [Header("Referências da Hierarquia")]
    public Transform visualGripperBase;
    public Transform fingerLeft, fingerRight;

    [Header("Matemática e Calibração")]
    public float lerpSpeed = 15f; 
    public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);
    public Vector3 tcpOffset = new Vector3(0, 0, 0);

    [Header("Filtros Anti-Jitter (Zona Morta)")]
    [Tooltip("Distância mínima (em metros) para a garra atualizar a posição. Ex: 0.015 = 1.5 cm")]
    public float positionTolerance = 0.015f; 
    [Tooltip("Ângulo mínimo (em graus) para a garra atualizar a rotação. Ex: 5 = 5 graus")]
    public float rotationTolerance = 5.0f;

    [Header("Controle de Visibilidade")]
    [Tooltip("Tempo em segundos para esconder a garra se a IA perder o objeto")]
    public float messageTimeout = 20f; // Aumentado para evitar o "pisca-pisca. Valor padrão recomendado: 1.5 segundos."

    // Variáveis de Estado Interno (Trava de Posição)
    private Vector3 acceptedUnityPos;
    private Quaternion acceptedUnityRot;

    // Variáveis de Destino Final
    private Vector3 targetPos;
    private Quaternion targetRot;
    private float targetWidth = 0.05f;
    private bool hasReceivedPose = false;
    
    // Variáveis de Controle de Exibição
    private float lastMessageTime;
    private bool isAllowedToShow = true;

    void Start()
    {
        if (visualGripperBase != null) visualGripperBase.gameObject.SetActive(false);

        var ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<PoseStampedMsg>(poseTopic, ReceivePose);
        ros.Subscribe<Float32Msg>(widthTopic, ReceiveWidth);
    }

    void ReceivePose(PoseStampedMsg msg)
    {
        // Atualiza o relógio sinalizando que o ROS está vivo
        lastMessageTime = Time.time;

        // Conversão Nativa do ROS para Unity
        Vector3 incomingUnityPos = msg.pose.position.From<FLU>();
        Quaternion incomingUnityRot = msg.pose.orientation.From<FLU>();

        // ========================================================
        // FILTRO DE ZONA MORTA (ANTI-JITTER / LATCHING)
        // ========================================================
        if (!hasReceivedPose)
        {
            // Primeiro frame: Aceita imediatamente
            acceptedUnityPos = incomingUnityPos;
            acceptedUnityRot = incomingUnityRot;
        }
        else
        {
            // Verifica a diferença entre a nova pose da IA e a pose que a garra está usando
            float dist = Vector3.Distance(acceptedUnityPos, incomingUnityPos);
            float angle = Quaternion.Angle(acceptedUnityRot, incomingUnityRot);

            // Só atualiza o alvo se a IA sugerir uma mudança maior que a tolerância (ruído)
            if (dist > positionTolerance || angle > rotationTolerance)
            {
                acceptedUnityPos = incomingUnityPos;
                acceptedUnityRot = incomingUnityRot;
            }
        }

        // Aplicação dos Offsets do modelo 3D sobre a pose aceita
        targetRot = acceptedUnityRot * Quaternion.Euler(cadFrameCorrection);
        targetPos = acceptedUnityPos + (targetRot * tcpOffset);

        // Snap inicial
        if (!hasReceivedPose && visualGripperBase != null)
        {
            visualGripperBase.localPosition = targetPos;
            visualGripperBase.localRotation = targetRot;
        }

        hasReceivedPose = true;

        // Garante que está visível se for permitido
        if (isAllowedToShow && visualGripperBase != null && !visualGripperBase.gameObject.activeSelf)
        {
            visualGripperBase.gameObject.SetActive(true);
        }
    }

    void ReceiveWidth(Float32Msg msg)
    {
        // Filtro passa-baixa simples para a largura dos dedos (evita tremulação)
        targetWidth = Mathf.Lerp(targetWidth, msg.data, 0.5f);
    }

    void Update()
    {
        if (!hasReceivedPose || visualGripperBase == null) return;

        // --- CONTROLE DE PERSISTÊNCIA ---
        if (visualGripperBase.gameObject.activeSelf)
        {
            if (Time.time - lastMessageTime > messageTimeout || !isAllowedToShow)
            {
                visualGripperBase.gameObject.SetActive(false);
                return; 
            }

            // Movimentação Suave para o alvo filtrado
            visualGripperBase.localPosition = Vector3.Lerp(visualGripperBase.localPosition, targetPos, Time.deltaTime * lerpSpeed);
            visualGripperBase.localRotation = Quaternion.Slerp(visualGripperBase.localRotation, targetRot, Time.deltaTime * lerpSpeed);

            if (fingerLeft != null && fingerRight != null)
            {
                float halfWidth = targetWidth / 2f;
                fingerLeft.localPosition = new Vector3(-halfWidth, fingerLeft.localPosition.y, fingerLeft.localPosition.z);
                fingerRight.localPosition = new Vector3(halfWidth, fingerRight.localPosition.y, fingerRight.localPosition.z);
            }
        }
    }

    public void SetVisibilityAllowance(bool allowed)
    {
        isAllowedToShow = allowed;
        if (!allowed && visualGripperBase != null)
        {
            visualGripperBase.gameObject.SetActive(false);
        }
    }
}














// // Este script é responsável por receber as poses de preensão calculadas pela GGCNN via ROS e atualizar a posição, rotação e abertura da garra holográfica no Unity. Ele também inclui opções de suavização para evitar jitter e correções para alinhar o modelo 3D com o sistema de coordenadas do ROS.

// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Std;

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configurações de Tópicos ROS")]
//     public string poseTopic = "/ggcnn/unity_target_pose";
//     public string widthTopic = "/ggcnn/unity_gripper_width";
    
//     [Header("Referências da Hierarquia")]
//     public Transform visualGripperBase;
//     public Transform fingerLeft, fingerRight;

//     [Header("Matemática e Calibração")]
//     public float lerpSpeed = 15f; 
//     public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);
//     public Vector3 tcpOffset = new Vector3(0, 0, 0);

//     // Variáveis de Estado
//     private Vector3 targetPos;
//     private Quaternion targetRot;
//     private float targetWidth = 0.05f;
//     private bool hasReceivedPose = false;

//     void Start()
//     {
//         // Garra fica sempre visível. Apenas iniciamos a escuta.
//         var ros = ROSConnection.GetOrCreateInstance();
//         ros.Subscribe<PoseStampedMsg>(poseTopic, ReceivePose);
//         ros.Subscribe<Float32Msg>(widthTopic, ReceiveWidth);
//     }

//     void ReceivePose(PoseStampedMsg msg)
//     {
//         // Dados Crus do ROS (Mão Direita - FLU)
//         Vector3 rosPosRaw = new Vector3((float)msg.pose.position.x, (float)msg.pose.position.y, (float)msg.pose.position.z);
//         Quaternion rosQuatRaw = new Quaternion((float)msg.pose.orientation.x, (float)msg.pose.orientation.y, (float)msg.pose.orientation.z, (float)msg.pose.orientation.w);
        
//         // Conversão Nativa
//         Vector3 unityPos = msg.pose.position.From<FLU>();
//         Quaternion unityRot = msg.pose.orientation.From<FLU>();

//         Debug.Log($"<color=cyan>[ROS -> Unity]</color> POS: ROS {rosPosRaw} => Unity {unityPos} | ROT: {rosQuatRaw.eulerAngles} => {unityRot.eulerAngles}");

//         // Aplicação dos Offsets
//         targetRot = unityRot * Quaternion.Euler(cadFrameCorrection);
//         targetPos = unityPos + (targetRot * tcpOffset);

//         // Se for a primeira pose recebida, dá um "snap" para não vir voando de longe
//         if (!hasReceivedPose && visualGripperBase != null)
//         {
//             visualGripperBase.localPosition = targetPos;
//             visualGripperBase.localRotation = targetRot;
//         }

//         hasReceivedPose = true;
//     }

//     void ReceiveWidth(Float32Msg msg)
//     {
//         targetWidth = msg.data;
//     }

//     void Update()
//     {
//         if (!hasReceivedPose || visualGripperBase == null) return;

//         // Movimentação Suave
//         visualGripperBase.localPosition = Vector3.Lerp(visualGripperBase.localPosition, targetPos, Time.deltaTime * lerpSpeed);
//         visualGripperBase.localRotation = Quaternion.Slerp(visualGripperBase.localRotation, targetRot, Time.deltaTime * lerpSpeed);

//         // Animação dos Dedos
//         if (fingerLeft != null && fingerRight != null)
//         {
//             float halfWidth = targetWidth / 2f;
//             fingerLeft.localPosition = new Vector3(-halfWidth, fingerLeft.localPosition.y, fingerLeft.localPosition.z);
//             fingerRight.localPosition = new Vector3(halfWidth, fingerRight.localPosition.y, fingerRight.localPosition.z);
//         }
//     }
// }













// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Std; // Necessário para o Float32 da largura

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configurações de Tópicos ROS")]
//     [Tooltip("O nome do tópico ROS onde a GGCNN publica as poses de preensão. Certifique-se de que corresponda ao nome usado no ROS!")]
//     public string poseTopic = "/ggcnn/unity_target_pose";
//     [Tooltip("O nome do tópico ROS onde a GGCNN publica a largura da garra. Certifique-se de que corresponda ao nome usado no ROS!")]
//     public string widthTopic = "/ggcnn/unity_gripper_width";
    
//     [Header("Referências da Hierarquia")]
//     [Tooltip("O objeto 'Malhas_Garra' que contém todo o visual.")]
//     public Transform visualGripperBase;
//     [Tooltip("As malhas dos dedos para animação de abertura.")]
//     public Transform fingerLeft, fingerRight;

//     [Header("Matemática e Calibração")]
//     public float lerpSpeed = 15f; 
//     [Tooltip("Correção para o arquivo CAD (ex: 90 no X ou Y).")]
//     public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);
//     [Tooltip("Recuo em metros do centro dos dedos até a base (ex: Z = -0.14).")]
//     public Vector3 tcpOffset = new Vector3(0, 0, 0);

//     // Variáveis de Estado
//     private Vector3 targetPos;
//     private Quaternion targetRot;
//     private float targetWidth = 0.05f;
//     private bool hasReceivedPose = false;

//     void Start()
//     {
//         var ros = ROSConnection.GetOrCreateInstance();
//         ros.Subscribe<PoseStampedMsg>(poseTopic, ReceivePose);
//         ros.Subscribe<Float32Msg>(widthTopic, ReceiveWidth);
//     }

//     void ReceivePose(PoseStampedMsg msg)
//     {
//         // --- BLOCO DE DEBUG (A INVESTIGAÇÃO QUE VOCÊ SOLICITOU) ---
//         // Dados Crus do ROS (Mão Direita - FLU)
//         Vector3 rosPosRaw = new Vector3((float)msg.pose.position.x, (float)msg.pose.position.y, (float)msg.pose.position.z);
//         Quaternion rosQuatRaw = new Quaternion((float)msg.pose.orientation.x, (float)msg.pose.orientation.y, (float)msg.pose.orientation.z, (float)msg.pose.orientation.w);
        
//         // Conversão Nativa (O que o Unity calcula)
//         Vector3 unityPos = msg.pose.position.From<FLU>();
//         Quaternion unityRot = msg.pose.orientation.From<FLU>();

//         // Log detalhado no Console (Pressione 'Collapse' no Unity para não poluir)
//         Debug.Log($"<color=cyan>[ROS -> Unity]</color> POS: ROS {rosPosRaw} => Unity {unityPos} | ROT(Euler): {rosQuatRaw.eulerAngles} => {unityRot.eulerAngles}");

//         // --- APLICAÇÃO DOS OFFSETS ---
//         // 1. Rotação: IA + Ajuste do CAD
//         targetRot = unityRot * Quaternion.Euler(cadFrameCorrection);
        
//         // 2. Posição: IA + Recuo do TCP (na direção da garra)
//         targetPos = unityPos + (targetRot * tcpOffset);

//         hasReceivedPose = true;
//     }

//     void ReceiveWidth(Float32Msg msg)
//     {
//         targetWidth = msg.data;
//     }

//     void Update()
//     {
//         if (!hasReceivedPose || visualGripperBase == null) return;

//         // Movimentação Suave da Base
//         visualGripperBase.localPosition = Vector3.Lerp(visualGripperBase.localPosition, targetPos, Time.deltaTime * lerpSpeed);
//         visualGripperBase.localRotation = Quaternion.Slerp(visualGripperBase.localRotation, targetRot, Time.deltaTime * lerpSpeed);

//         // Animação dos Dedos (Abertura Compartilhada)
//         if (fingerLeft != null && fingerRight != null)
//         {
//             float halfWidth = targetWidth / 2f;
//             // Ajuste o eixo conforme a malha (comumente X ou Z)
//             fingerLeft.localPosition = new Vector3(-halfWidth, fingerLeft.localPosition.y, fingerLeft.localPosition.z);
//             fingerRight.localPosition = new Vector3(halfWidth, fingerRight.localPosition.y, fingerRight.localPosition.z);
//         }
//     }
// }
















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Std;

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configurações de Tópicos ROS")]
//     public string poseTopic = "/ggcnn/unity_target_pose";
//     public string widthTopic = "/ggcnn/unity_gripper_width";
    
//     [Header("Referências da Hierarquia")]
//     public Transform visualGripperBase;
//     public Transform fingerLeft, fingerRight;

//     [Header("Matemática e Calibração")]
//     public float lerpSpeed = 15f; 
//     public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);
//     public Vector3 tcpOffset = new Vector3(0, 0, 0);

//     // Variáveis de Estado
//     private Vector3 targetPos;
//     private Quaternion targetRot;
//     private float targetWidth = 0.05f;
//     private bool hasReceivedPose = false;

//     void Start()
//     {
//         var ros = ROSConnection.GetOrCreateInstance();
//         ros.Subscribe<PoseStampedMsg>(poseTopic, ReceivePose);
//         ros.Subscribe<Float32Msg>(widthTopic, ReceiveWidth);
//     }

//     void ReceivePose(PoseStampedMsg msg)
//     {
//         // ---------------------------------------------------------
//         // 1. A MATEMÁTICA PURA: CONVERSÃO MANUAL ROS (FLU) -> UNITY
//         // ---------------------------------------------------------
        

//         // --- POSIÇÃO ---
//         // Padrão Teórico: Unity(X, Y, Z) = ROS(-Y, Z, X)
//         float posX = (float)-msg.pose.position.y; 
//         float posY = (float)msg.pose.position.z + 0.14f;  // Eixo Verde (Cima/Baixo)
//         float posZ = (float)msg.pose.position.x;
        
//         Vector3 manualPos = new Vector3(posX, posY, posZ);

//         // --- ROTAÇÃO (QUATERNIONS) ---
//         // Padrão Teórico: Unity(X, Y, Z, W) = ROS(-Y, Z, X, -W)
//         // Se a sua garra estiver invertida em algum eixo, altere o sinal (-) ou troque as letras aqui embaixo!
//         // // Se a sua garra estiver invertida em algum eixo, altere o sinal (-) ou troque as letras aqui embaixo!
//         float rotX = (float)-msg.pose.orientation.y;
//         float rotY = (float)msg.pose.orientation.z;  // Rotação no Eixo Verde
//         float rotZ = (float)msg.pose.orientation.x;
//         float rotW = (float)-msg.pose.orientation.w; 



//         // // --- POSIÇÃO ---
//         // // Padrão Teórico: Unity(X, Y, Z) = ROS(-Y, Z, X)
//         // float posX = (float)-msg.pose.position.y; 
//         // float posY = (float)msg.pose.position.z;  // Eixo Verde (Cima/Baixo)
//         // float posZ = (float)msg.pose.position.x;
        
//         // Vector3 manualPos = new Vector3(posX, posY, posZ);

//         // // --- ROTAÇÃO (QUATERNIONS) ---
//         // // Padrão Teórico: Unity(X, Y, Z, W) = ROS(-Y, Z, X, -W)
//         // // Se a sua garra estiver invertida em algum eixo, altere o sinal (-) ou troque as letras aqui embaixo!
//         // float rotX = (float)-msg.pose.orientation.y;
//         // float rotY = (float)msg.pose.orientation.z;  // Rotação no Eixo Verde
//         // float rotZ = (float)msg.pose.orientation.x;
//         // float rotW = (float)-msg.pose.orientation.w; 

//         Quaternion manualRot = new Quaternion(rotX, rotY, rotZ, rotW);

//         // ---------------------------------------------------------

//         // Log para Debug Visual
//         Debug.Log($"<color=green>[CONVERSÃO MANUAL]</color> POS: {manualPos} | ROT(Euler): {manualRot.eulerAngles}");

//         // 2. Aplicação das Correções do Usuário (Offsets)
//         targetRot = manualRot * Quaternion.Euler(cadFrameCorrection);
//         targetPos = manualPos + (targetRot * tcpOffset);

//         hasReceivedPose = true;
//     }

//     void ReceiveWidth(Float32Msg msg)
//     {
//         targetWidth = msg.data;
//     }

//     void Update()
//     {
//         if (!hasReceivedPose || visualGripperBase == null) return;

//         visualGripperBase.localPosition = Vector3.Lerp(visualGripperBase.localPosition, targetPos, Time.deltaTime * lerpSpeed);
//         visualGripperBase.localRotation = Quaternion.Slerp(visualGripperBase.localRotation, targetRot, Time.deltaTime * lerpSpeed);

//         if (fingerLeft != null && fingerRight != null)
//         {
//             float halfWidth = targetWidth / 2f;
//             fingerLeft.localPosition = new Vector3(-halfWidth, fingerLeft.localPosition.y, fingerLeft.localPosition.z);
//             fingerRight.localPosition = new Vector3(halfWidth, fingerRight.localPosition.y, fingerRight.localPosition.z);
//         }
//     }
// }



























// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Geometry;

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configuração de Tópico")]
//     public string ggcnnPoseTopic = "/ggcnn/unity_target_pose";
    
//     [Header("Referência Visual")]
//     public Transform visualGripperHologram;

//     [Header("Suavização de Rede")]
//     public float lerpSpeed = 10f; 

//     [Header("Correção de Frame (ROS -> Unity CAD)")]
//     [Tooltip("Rotação rígida para alinhar o CAD importado com o sistema de coordenadas do ROS. Use apenas múltiplos exatos (0, 90, 180, 270,-90).")]
//     public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);

//     private Vector3 targetPosition;
//     private Quaternion targetRotation;
//     private bool hasReceivedData = false;

//     void Start()
//     {
//         ROSConnection.GetOrCreateInstance().Subscribe<PoseStampedMsg>(ggcnnPoseTopic, ReceiveGraspPose);
//     }

//     void ReceiveGraspPose(PoseStampedMsg message)
//     {
//         // 1. Recebe a Pose Matemática exata do ROS e converte para a regra da mão esquerda do Unity
//         Vector3 rosPosition = message.pose.position.From<FLU>();
//         Quaternion rosRotation = message.pose.orientation.From<FLU>();

//         // 2. Aplica a correção RÍGIDA do modelo 3D (O CAD da Robotiq)
//         // Isso garante que o "Frente" do modelo 3D corresponda ao "Frente" calculado pela IA
//         targetRotation = rosRotation * Quaternion.Euler(cadFrameCorrection);
//         targetPosition = rosPosition;

//         hasReceivedData = true;
//     }

//     void Update()
//     {
//         if (!hasReceivedData || visualGripperHologram == null) return;

//         // Mantém a precisão milimétrica, apenas suavizando os saltos da rede TCP
//         visualGripperHologram.localPosition = Vector3.Lerp(visualGripperHologram.localPosition, targetPosition, Time.deltaTime * lerpSpeed);
//         visualGripperHologram.localRotation = Quaternion.Slerp(visualGripperHologram.localRotation, targetRotation, Time.deltaTime * lerpSpeed);
//     }
// }









// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Geometry;

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configuração de Tópico")]
//     [Tooltip("O nome do tópico ROS onde a GGCNN publica as poses de preensão. Certifique-se de que corresponda ao nome usado no ROS!")  ]
//     public string ggcnnPoseTopic = "ggcnn/unity_target_pose";
    
//     [Header("Referência Visual")]
//     [Tooltip("Arraste o GameObject do holograma do gripper aqui. Este é o objeto que se moverá para mostrar a pose de preensão sugerida pela GGCNN.")]  
//     public Transform visualGripperHologram;

//     [Header("Suavização (Anti-Jitter)")]
//     [Tooltip("Controla a velocidade que o holograma se move até o alvo. Use valores entre 5 e 15.")]
//     public float lerpSpeed = 10f; 

//     // Alvos brutos recebidos da rede
//     private Vector3 targetPosition;
//     private Quaternion targetRotation;
    
//     // Trava para não tentar mover o holograma antes de receber o primeiro dado
//     private bool hasReceivedData = false;

//     void Start()
//     {
//         // Pede ao ROSConnection para assinar o tópico da GGCNN
//         ROSConnection.GetOrCreateInstance().Subscribe<PoseStampedMsg>(ggcnnPoseTopic, ReceiveGraspPose);
//     }

//     // Callback: Chamada toda vez que uma mensagem chega do ROS (pode ser 10x ou 30x por segundo)
//     void ReceiveGraspPose(PoseStampedMsg message)
//     {
//         // 1. Conversão Mágica: O pacote ROSGeometry converte automaticamente do
//         // sistema do ROS (Z-up, mão direita) para o sistema do Unity (Y-up, mão esquerda)
//         targetPosition = message.pose.position.From<FLU>();
//         targetRotation = message.pose.orientation.From<FLU>();

//         // 2. Trava liberada
//         hasReceivedData = true;
//     }

// void Update()
//     {
//         if (!hasReceivedData || visualGripperHologram == null) return;

//         // Trocamos para LOCAL para respeitar o ponto zero do robô (base_link)
//         visualGripperHologram.localPosition = Vector3.Lerp(visualGripperHologram.localPosition, targetPosition, Time.deltaTime * lerpSpeed);
//         visualGripperHologram.localRotation = Quaternion.Slerp(visualGripperHologram.localRotation, targetRotation, Time.deltaTime * lerpSpeed);
//     }
// }










// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;
// using RosMessageTypes.Geometry;

// public class GGCNN_Subscriber : MonoBehaviour
// {
//     [Header("Configuração de Tópico")]
//     [Tooltip("O nome do tópico ROS onde a GGCNN publica as poses de preensão. Certifique-se de que corresponda ao nome usado no ROS!")  ]
//     public string ggcnnPoseTopic = "ggcnn/unity_target_pose";
    
//     [Header("Referência Visual")]
//     [Tooltip("Arraste o GameObject do holograma do gripper aqui. Este é o objeto que se moverá para mostrar a pose de preensão sugerida pela GGCNN.")]  
//     public Transform visualGripperHologram;

//     [Header("Suavização (Anti-Jitter)")]
//     [Tooltip("Controla a velocidade que o holograma se move até o alvo. Use valores entre 5 e 15.")]
//     public float lerpSpeed = 10f; 

//     // Alvos brutos recebidos da rede
//     private Vector3 targetPosition;
//     private Quaternion targetRotation;
    
//     // Trava para não tentar mover o holograma antes de receber o primeiro dado
//     private bool hasReceivedData = false;

//     void Start()
//     {
//         // Pede ao ROSConnection para assinar o tópico da GGCNN
//         ROSConnection.GetOrCreateInstance().Subscribe<PoseStampedMsg>(ggcnnPoseTopic, ReceiveGraspPose);
//     }

//     // Callback: Chamada toda vez que uma mensagem chega do ROS (pode ser 10x ou 30x por segundo)
//     void ReceiveGraspPose(PoseStampedMsg message)
//     {
//         // 1. Conversão Mágica: O pacote ROSGeometry converte automaticamente do
//         // sistema do ROS (Z-up, mão direita) para o sistema do Unity (Y-up, mão esquerda)
//         targetPosition = message.pose.position.From<FLU>();
//         targetRotation = message.pose.orientation.From<FLU>();

//         // 2. Trava liberada
//         hasReceivedData = true;
//     }

//     void Update()
//     {
//         if (!hasReceivedData || visualGripperHologram == null) return;

//         // 3. O Filtro Anti-Jitter Geométrico
//         // Em vez de "teleportar" o holograma a cada pacote que chega, nós o deslizamos (Lerp/Slerp).
//         // Isso mascara qualquer latência de rede ou pico de processamento do ROS.
//         visualGripperHologram.position = Vector3.Lerp(visualGripperHologram.position, targetPosition, Time.deltaTime * lerpSpeed);
//         visualGripperHologram.rotation = Quaternion.Slerp(visualGripperHologram.rotation, targetRotation, Time.deltaTime * lerpSpeed);
//     }
// }
