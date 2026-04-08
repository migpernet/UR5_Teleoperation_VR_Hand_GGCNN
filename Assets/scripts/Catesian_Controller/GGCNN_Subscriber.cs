using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;

public class GGCNN_Subscriber : MonoBehaviour
{
    [Header("Configuração de Tópico")]
    public string ggcnnPoseTopic = "/ggcnn/unity_target_pose";
    
    [Header("Referência Visual")]
    public Transform visualGripperHologram;

    [Header("Suavização de Rede")]
    public float lerpSpeed = 10f; 

    [Header("Correção de Frame (ROS -> Unity CAD)")]
    [Tooltip("Rotação rígida para alinhar o CAD importado com o sistema de coordenadas do ROS. Use apenas múltiplos exatos (0, 90, 180, 270,-90).")]
    public Vector3 cadFrameCorrection = new Vector3(0, 0, 0);

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasReceivedData = false;

    void Start()
    {
        ROSConnection.GetOrCreateInstance().Subscribe<PoseStampedMsg>(ggcnnPoseTopic, ReceiveGraspPose);
    }

    void ReceiveGraspPose(PoseStampedMsg message)
    {
        // 1. Recebe a Pose Matemática exata do ROS e converte para a regra da mão esquerda do Unity
        Vector3 rosPosition = message.pose.position.From<FLU>();
        Quaternion rosRotation = message.pose.orientation.From<FLU>();

        // 2. Aplica a correção RÍGIDA do modelo 3D (O CAD da Robotiq)
        // Isso garante que o "Frente" do modelo 3D corresponda ao "Frente" calculado pela IA
        targetRotation = rosRotation * Quaternion.Euler(cadFrameCorrection);
        targetPosition = rosPosition;

        hasReceivedData = true;
    }

    void Update()
    {
        if (!hasReceivedData || visualGripperHologram == null) return;

        // Mantém a precisão milimétrica, apenas suavizando os saltos da rede TCP
        visualGripperHologram.localPosition = Vector3.Lerp(visualGripperHologram.localPosition, targetPosition, Time.deltaTime * lerpSpeed);
        visualGripperHologram.localRotation = Quaternion.Slerp(visualGripperHologram.localRotation, targetRotation, Time.deltaTime * lerpSpeed);
    }
}









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
