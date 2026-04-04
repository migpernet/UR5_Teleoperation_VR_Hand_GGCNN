using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System.Collections.Generic;
using System;

/// <summary>
/// Subscritor dedicado que recebe o JointState real do ROS (Gazebo/Controlador)
/// e armazena os valores para uso pelo controlador VR.
/// </summary>
public class ROSJointStateSubscriber : MonoBehaviour
{
    [Header("ROS Feedback Configuration")]
    public string jointStateTopic = "/ur5/joint_states"; // Tópico de saída do Gazebo/Controlador

    [HideInInspector]
    public float[] latestJointAngles = new float[7];

   
    
    private ROSConnection ros;
    private Dictionary<string, int> jointNameToIndex;
    private bool hasReceivedFirstMessage = false;

    // Evento disparado APENAS uma vez na primeira mensagem ROS (para sincronia inicial)
    public event Action<float[]> OnJointAnglesUpdated; 

    // Mapeamento de nomes ROS para índices do array (necessário para a Unity)
    private readonly string[] rosJointNames = {
        "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
        "wrist_1_joint", "wrist_2_joint", "wrist_3_joint",
        "robotiq_85_left_knuckle_joint"
    };

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // 1. Cria o mapeamento nome ROS (ex: shoulder_pan_joint) → índice (0)
        jointNameToIndex = new Dictionary<string, int>();
        for (int i = 0; i < rosJointNames.Length; i++)
            jointNameToIndex[rosJointNames[i]] = i;
        
        // 2. Registra o Subscriber
        ros.Subscribe<JointStateMsg>(jointStateTopic, OnJointStateReceived);
        Debug.Log($"[ROS Subscriber] Escutando o tópico: {jointStateTopic}");
        
    }

    private void OnJointStateReceived(JointStateMsg msg)
    {
        float[] orderedJointAngles = new float[7];

        for (int i = 0; i < msg.name.Length; i++)
        {
            string jointName = msg.name[i];
            double positionRad = msg.position[i];

            if (jointNameToIndex.TryGetValue(jointName, out int idx))
            {
                float value;
                // UR5 (Juntas 0-5): Converte Radianos para Graus
                if (idx < 6)
                {
                    value = (float)(positionRad * Mathf.Rad2Deg); 
                }
                // Garra (Junta 6): Mantém em Radianos (unidade nativa do gripper)
                else
                {
                    value = (float)positionRad;
                }
                
                orderedJointAngles[idx] = value;
            }
        }

        latestJointAngles = orderedJointAngles;
        // Debug.Log($"[ROS Subscriber] Posições ordenadas: {string.Join(", ", latestJointAngles)}");

        // 🔹 DISPARA O EVENTO APENAS NA PRIMEIRA MENSAGEM
        if (!hasReceivedFirstMessage)
        {
            hasReceivedFirstMessage = true;
            Debug.Log("[ROS Subscriber] Primeira mensagem recebida - sincronização inicial enviada!");
            OnJointAnglesUpdated?.Invoke(latestJointAngles); 
        }
    }

    public void TriggerInitialSync()
    {
        // Usado para garantir que novos scripts inscritos obtenham a pose se a conexão já ocorreu.
        if (hasReceivedFirstMessage)
        {
            OnJointAnglesUpdated?.Invoke(latestJointAngles);
        }
    }
}















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Sensor;
// using System.Collections.Generic;
// using System.Linq;
// using System;

// public class ROSJointStateSubscriber : MonoBehaviour
// {
//     [Header("ROS Feedback Configuration")]
//     public string jointStateTopic = "/ur5/joint_states"; // Tópico do ROS

//     [HideInInspector]
//     public float[] latestJointAngles = new float[7];

//     private ROSConnection ros;
//     private Dictionary<string, int> jointNameToIndex;

//     private readonly string[] rosJointNames = {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint",
//         "robotiq_85_left_knuckle_joint"
//     };

//     private const string GRIPPER_ROS_NAME = "robotiq_85_left_knuckle_joint";

//     public event Action<float[]> OnJointAnglesUpdated;
//     private bool hasReceivedFirstMessage = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();

//         // 🔹 Cria o mapeamento nome → índice baseado na ordem que o Unity precisa
//         jointNameToIndex = new Dictionary<string, int>();
//         for (int i = 0; i < rosJointNames.Length; i++)
//             jointNameToIndex[rosJointNames[i]] = i;

//         // 🔹 Inscreve no tópico do ROS
//         ros.Subscribe<JointStateMsg>(jointStateTopic, OnJointStateReceived);
//         // Debug.Log($"[ROS Subscriber] Escutando o tópico: {jointStateTopic}");
//     }

//     private void OnJointStateReceived(JointStateMsg msg)
//     {
//         // Debug.Log($"[Subscriber] Juntas recebidas do tópico {jointStateTopic}: {string.Join(", ", msg.name)}");

//         // 🔹 Cria array temporário para armazenar os ângulos já ordenados corretamente
//         float[] orderedJointAngles = new float[7];

//         // 🔹 Percorre cada junta recebida do ROS
//         for (int i = 0; i < msg.name.Length; i++)
//         {
//             string jointName = msg.name[i];
//             double positionRad = msg.position[i];

//             if (jointNameToIndex.TryGetValue(jointName, out int idx))
//             {
//                 float value;
//                 // Converte apenas as 6 primeiras juntas (braço UR5)
//                 if (idx < 6)
//                 {
//                     value = (float)(positionRad * Mathf.Rad2Deg);
//                 }

//                 else
//                 {
//                     value = (float)positionRad; // Garra: mantém em radianos
//                 }


//                 orderedJointAngles[idx] = value;
//             }
//             else
//             {
//                 Debug.LogWarning($"[ROS Subscriber] Junta desconhecida recebida: {jointName}");
//             }
//         }

//         // 🔹 Atualiza o vetor global na ordem esperada pelo Unity
//         latestJointAngles = orderedJointAngles;

//         // Debug.Log($"[ROS Subscriber] Posições ordenadas: {string.Join(", ", latestJointAngles)}");

//         // 🔹 Dispara evento para outros scripts (ex: controladores no Unity)
//         OnJointAnglesUpdated?.Invoke(latestJointAngles);

//         if (!hasReceivedFirstMessage)
//         {
//             hasReceivedFirstMessage = true;
//             Debug.Log("[ROS Subscriber] Primeira mensagem recebida — sincronização inicial completa!");
//             // Dispara o evento APENAS quando a primeira mensagem chega.
//             OnJointAnglesUpdated?.Invoke(latestJointAngles);
//         }
//     }

//     public void TriggerInitialSync()
//     {
//         if (hasReceivedFirstMessage)
//         {
//             Debug.Log("[ROS Subscriber] Sincronizando pose inicial com o novo inscrito...");
//             OnJointAnglesUpdated?.Invoke(latestJointAngles);
//         }
//         else
//         {
//             Debug.LogWarning("[ROS Subscriber] Nenhuma mensagem ROS recebida ainda — aguardando primeira atualização...");
//         }
//     }
// }










































