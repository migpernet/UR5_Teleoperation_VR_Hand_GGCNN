using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std; 
using RosMessageTypes.Sensor; // Necessário para JointStateMsg
using System.Collections;
using System;

public class TargetPointPublisher : MonoBehaviour
{
    [Header("Referências Core")]
    public CartesianHandController handController;
    public Transform robotBaseLink;

    [Header("Configurações ROS - Visão Ativa")]
    public string intentionTopic = "/ggcnn/target_intention_point";
    public string commandTopic = "unity/target_pose_autonomous"; 
    
    [Header("Configurações ROS - Sincronismo (Malha Fechada)")]
    public string jointStateTopic = "/ur5/joint_states";

    [Header("Parâmetros de Visão Ativa")]
    public float hoverHeight = 0.40f;

    private ROSConnection ros;
    private bool isMovingAutonomously = false;

    // --- Variáveis para Malha Fechada ---
    private readonly string[] allJointNames = new string[]
    {
        "shoulder_pan_joint",
        "shoulder_lift_joint",
        "elbow_joint",
        "wrist_1_joint",
        "wrist_2_joint",
        "wrist_3_joint"
    };
    private double[] currentJoints = new double[6];
    private bool hasReceivedJoints = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PointMsg>(intentionTopic);
        ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
        
        // Ouvinte de juntas para saber quando o robô físico parou
        ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
    }

    // --- LEITURA DO GAZEBO ---
    void JointStateCallback(JointStateMsg msg)
    {
        for (int i = 0; i < allJointNames.Length; i++)
        {
            int index = Array.IndexOf(msg.name, allJointNames[i]);
            if (index != -1)
            {
                currentJoints[i] = msg.position[index];
            }
        }
        hasReceivedJoints = true;
    }

    public void PublishTargetPoint(Vector3 unityWorldPoint)
    {
        if (robotBaseLink == null || handController == null) return;

        if (!isMovingAutonomously)
        {
            StartCoroutine(ActiveVisionRoutine(unityWorldPoint));
        }
    }

    private IEnumerator ActiveVisionRoutine(Vector3 worldTarget)
    {
        isMovingAutonomously = true;

        // 1. DESENGATA A EMBREAGEM
        handController.PauseManualControl();
        Debug.Log("[Active Vision] Controle pausado. Calculando cinemática...");

        // 2. CÁLCULO E ENVIO DA POSE AUTÔNOMA PARA O KDL
        Vector3 hoverWorld = new Vector3(worldTarget.x, worldTarget.y + hoverHeight, worldTarget.z);
        Vector3 localHover = robotBaseLink.InverseTransformPoint(hoverWorld);

        PoseStampedMsg hoverPose = new PoseStampedMsg();
        hoverPose.header = new HeaderMsg { frame_id = "base_link" };
        
        // Aplicação do seu Offset validado
        hoverPose.pose.position.x = localHover.z + 0.1072f;  
        hoverPose.pose.position.y = -localHover.x + 0.05f; 
        hoverPose.pose.position.z = localHover.y + 0.09f;  

        // Orientação ortogonal fixa
        hoverPose.pose.orientation.x = -1.0;
        hoverPose.pose.orientation.y = 0.0; 
        hoverPose.pose.orientation.z = 0.0;
        hoverPose.pose.orientation.w = 0.0;

        ros.Publish(commandTopic, hoverPose);

        // 3. ENVIA INTENÇÃO PARA O GGCNN
        Vector3 localTarget = robotBaseLink.InverseTransformPoint(worldTarget);
        PointMsg intentionMsg = new PointMsg(localTarget.y, localTarget.x, localTarget.z);
        ros.Publish(intentionTopic, intentionMsg);

        // ========================================================
        // 4. A ROTINA DE MALHA FECHADA (O seu sistema anti-congelamento)
        // ========================================================
        
        // Aguarda meio segundo para dar tempo do ROS processar e o Gazebo começar a andar
        yield return new WaitForSeconds(0.5f);

        float timeout = 10.0f; // Tempo máximo de segurança
        float timer = 0f;
        
        double[] previousJoints = new double[6];
        Array.Copy(currentJoints, previousJoints, 6);
        float stationaryTimer = 0f;

        while (timer < timeout)
        {
            if (hasReceivedJoints)
            {
                bool isMoving = false;
                for (int i = 0; i < 6; i++)
                {
                    // Checa se alguma junta mudou mais de 0.002 radianos
                    if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.002f) 
                    {
                        isMoving = true;
                        break;
                    }
                }

                if (isMoving)
                {
                    stationaryTimer = 0f;
                    Array.Copy(currentJoints, previousJoints, 6);
                }
                else
                {
                    stationaryTimer += Time.deltaTime;
                    if (stationaryTimer >= 0.5f) // Se ficou completamente parado por 0.5s
                    {
                        Debug.Log("[Active Vision] Motores estabilizaram fisicamente no alvo.");
                        break; 
                    }
                }
            }

            timer += Time.deltaTime;
            yield return null; 
        }

        // =================================================================
        // O SEGREDO DO SINCRONISMO MANTIDO
        // =================================================================
        yield return new WaitForSeconds(0.8f); 

        // 5. RETOMA CONTROLE (Start Automático)
        handController.ResumeManualControl(false);
        isMovingAutonomously = false;
        Debug.Log("[Active Vision] Sincronização perfeita concluída!");
    }
}














// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Std; // Necessário para o HeaderMsg
// using System.Collections;

// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Referências Core")]
//     public CartesianHandController handController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     [Tooltip("Tópico onde o GGCNN lê o clique para fazer a máscara")]
//     public string intentionTopic = "/ggcnn/target_intention_point";
    
//     [Tooltip("Tópico para envio de pose autônoma suavizada")]
//     public string commandTopic = "unity/target_pose_autonomous";

//     [Header("Parâmetros de Visão Ativa")]
//     [Tooltip("Altura de sobrevoo em relação ao ponto clicado (Metros)")]
//     public float hoverHeight = 0.4f;

//     private ROSConnection ros;
//     private bool isMovingAutonomously = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(intentionTopic);
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//     }

//     public void PublishTargetPoint(Vector3 unityWorldPoint)
//     {
//         if (robotBaseLink == null || handController == null) 
//         {
//             Debug.LogError("[Active Vision] Referências ausentes no TargetPointPublisher!");
//             return;
//         }

//         if (!isMovingAutonomously)
//         {
//             StartCoroutine(ActiveVisionRoutine(unityWorldPoint));
//         }
//     }

//     private IEnumerator ActiveVisionRoutine(Vector3 worldTarget)
//     {
//         isMovingAutonomously = true;

//         // 1. DESENGATA A EMBREAGEM (Pausa o Gizmo e a mão do VR)
//         handController.PauseManualControl();
//         Debug.Log("[Active Vision] Controle pausado. Sincronizando com a convenção do KDL Solver...");

//         // 2. Define o ponto de destino no mundo (Hover)
//         // Aqui o hoverHeight é utilizado para subir a altura no eixo Y do Unity
//         Vector3 hoverWorld = new Vector3(worldTarget.x, worldTarget.y + hoverHeight, worldTarget.z);
        
//         // 3. Converte para o referencial local da base_link
//         Vector3 localHover = robotBaseLink.InverseTransformPoint(hoverWorld);

//         // 4. APLICA A CONVENÇÃO VITORIOSA DO CARTESIANHANDCONTROLLER
//         // x ROS = z local | y ROS = -x local | z ROS = y local
//         PoseStampedMsg hoverPose = new PoseStampedMsg();
//         hoverPose.header = new HeaderMsg { frame_id = "base_link" };
        
//         hoverPose.pose.position.x = localHover.z + 0.1072f;  // Profundidade
//         hoverPose.pose.position.y = -localHover.x; // Lateralidade
//         hoverPose.pose.position.z = localHover.y + 0.09f;  // Altura

//         // Orientação: Lente para baixo (Pitch 90º no ROS)
//         hoverPose.pose.orientation.x = -1.0;
//         hoverPose.pose.orientation.y = 0.0; 
//         hoverPose.pose.orientation.z = 0.0;
//         hoverPose.pose.orientation.w = 0.0;

//         // Envia para o tópico que o KDL Solver escuta (unity/target_pose)
//         ros.Publish(commandTopic, hoverPose);

//         // 5. ENVIA O CLIQUE PARA O GGCNN (Mantendo sua lógica original de coordenadas)
//         Vector3 localTarget = robotBaseLink.InverseTransformPoint(worldTarget);
//         PointMsg intentionMsg = new PointMsg(localTarget.y, localTarget.x, localTarget.z);
//         ros.Publish(intentionTopic, intentionMsg);

//         // 6. AGUARDA O VOO FÍSICO E O PROCESSAMENTO DA IA
//         yield return new WaitForSeconds(3.0f);

//         // 7. RETOMA O CONTROLE (Sincroniza Gizmos com a posição real do robô)
//         handController.ResumeManualControl(false);
//         isMovingAutonomously = false;
//         Debug.Log("[Active Vision] Movimento concluído e sistemas sincronizados.");
//     }
// }


















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;

// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Configurações ROS")]
//     [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
//     public string topicName = "/ggcnn/target_intention_point";
    
//     [Header("Referenciais (Frames)")]
//     [Tooltip("Arraste o objeto correspondente ao 'base_link' do robô no Unity")]
//     public Transform robotBaseLink;

//     private ROSConnection ros;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(topicName);
//         Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
//     }

//     /// <summary>
//     /// Recebe a coordenada Global do Unity, converte para o referencial da Base e envia para o ROS
//     /// </summary>
//     public void PublishTargetPoint(Vector3 unityWorldPoint)
//     {
//         if (robotBaseLink == null)
//         {
//             Debug.LogError("[ROS GGCNN] ERRO: A referência do 'base_link' não foi atribuída no script!");
//             return;
//         }

//         // 1. CONVERSÃO DE ESPAÇO: Global (World) -> Local (base_link)
//         // Isto transforma as coordenadas para que a base do robô seja o (0,0,0)
//         Vector3 localTarget = robotBaseLink.InverseTransformPoint(unityWorldPoint);

//         // 2. CONVERSÃO DE REFERENCIAL (EIXOS): Unity (Esquerda) -> ROS FLU (Direita)

//         // Assumindo que no seu Unity: X=Lateral, Y=Profundidade, Z=Altura
//         PointMsg pointMsg = new PointMsg(
//             localTarget.y,  // ROS X (Frente) recebe a Profundidade do Unity
//             localTarget.x, // ROS Y (Esquerda) recebe o inverso da Lateral do Unity
//             localTarget.z   // ROS Z (Cima) recebe a Altura do Unity
//         );

//         // 3. PUBLICAÇÃO
//         ros.Publish(topicName, pointMsg);
        
//         Debug.Log($"[ROS] Coordenada Local (base_link) enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
//     }
// }










// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;

// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Configurações ROS")]
//     [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
//     public string topicName = "/ggcnn/target_intention_point";
    
//     [Header("Referenciais (Frames)")]
//     [Tooltip("Arraste o objeto correspondente ao 'base_link' do robô no Unity")]
//     public Transform robotBaseLink;

//     private ROSConnection ros;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(topicName);
//         Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
//     }

//     /// <summary>
//     /// Recebe a coordenada Global do Unity, converte para o referencial da Base e envia para o ROS
//     /// </summary>
//     public void PublishTargetPoint(Vector3 unityWorldPoint)
//     {
//         if (robotBaseLink == null)
//         {
//             Debug.LogError("[ROS GGCNN] ERRO: A referência do 'base_link' não foi atribuída no script!");
//             return;
//         }

//         // 1. CONVERSÃO DE ESPAÇO: Global (World) -> Local (base_link)
//         // Isto transforma as coordenadas para que a base do robô seja o (0,0,0)
//         Vector3 localTarget = robotBaseLink.InverseTransformPoint(unityWorldPoint);

//         // 2. CONVERSÃO DE REFERENCIAL (EIXOS): Unity (Esquerda) -> ROS FLU (Direita)
//         // No ROS FLU (Forward, Left, Up):
//         // X do ROS = Frente (Z do Unity)
//         // Y do ROS = Esquerda (-X do Unity)
//         // Z do ROS = Cima (Y do Unity)
//         PointMsg pointMsg = new PointMsg(
//             localTarget.z,  
//             -localTarget.x, 
//             localTarget.y   
//         );

//         // 3. PUBLICAÇÃO
//         ros.Publish(topicName, pointMsg);
        
//         Debug.Log($"[ROS] Coordenada Local (base_link) enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
//     }
// }











// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;

// // Este script é responsável por publicar a coordenada do objeto para o GGCNN no ROS. Ele deve ser chamado pelo PointCloudSelector quando um ponto for selecionado.


// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Configurações ROS")]
//     [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
//     public string topicName = "/ggcnn/target_intention_point";
    
//     private ROSConnection ros;

//     void Start()
//     {
//         // Inicializa a conexão e registra o publicador
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(topicName);
//         Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
//     }

//     /// <summary>
//     /// Recebe a coordenada do Unity e envia para o ROS
//     /// </summary>
//     public void PublishTargetPoint(Vector3 unityPoint)
//     {
//         // Conversão básica de referenciais (Unity Left-Handed -> ROS Right-Handed FLU)
//         // Atenção: Esta conversão pode precisar de ajustes dependendo de como o seu base_link está configurado
//         PointMsg pointMsg = new PointMsg(
//             unityPoint.z,  // X do ROS costuma ser a profundidade (Z do Unity)
//             -unityPoint.x, // Y do ROS costuma ser a lateral invertida (-X do Unity)
//             unityPoint.y   // Z do ROS costuma ser a altura (Y do Unity)
//         );

//         // Publica a mensagem na rede
//         ros.Publish(topicName, pointMsg);
//         Debug.Log($"[ROS] Coordenada de intenção enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
//     }
// }