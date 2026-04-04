using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor; 
using System.Collections;
using System;

public class RobotPoseMenu : MonoBehaviour
{
    [Header("Controle do Cabo de Guerra")]
    public CartesianHandController handController;

    [Header("Configurações da Nova Arquitetura")]
    [Tooltip("Tópico onde o Unity envia o pedido de pose para o Python ler")]
    public string targetTopic = "/unity/target_joints"; 
    
    [Tooltip("Tópico onde o Unity ouve a realidade do Gazebo")]
    public string jointStateTopic = "/ur5/joint_states"; 
    private ROSConnection ros;

    [Header("Configurações da Garra")]
    public string gripperJointName = "robotiq_85_left_knuckle_joint"; 
    public float gripperOpenValue = 0.0f;
    public float gripperClosedValue = 0.8f;

    [Header("Parâmetros de Malha Fechada")]
    [Tooltip("Margem de erro em radianos para considerar que chegou no alvo")]
    public float arrivalTolerance = 0.05f;

    // A lista oficial de 7 juntas (6 do braço + 1 da garra)
    private readonly string[] allJointNames = new string[]
    {
        "shoulder_pan_joint",
        "shoulder_lift_joint",
        "elbow_joint",
        "wrist_1_joint",
        "wrist_2_joint",
        "wrist_3_joint",
        "robotiq_85_left_knuckle_joint" 
    };

    private double[] currentJoints = new double[7];
    private bool hasReceivedJoints = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // Novo Publicador Simples
        ros.RegisterPublisher<JointStateMsg>(targetTopic);
        
        // Mantemos o ouvinte para a malha fechada
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

    // --- POSES PREDEFINIDAS ---

    public void GoToZero()
    {
        double[] targetJoints = { 0.0, -0.05, 0.10, -0.05, 0.0, 0.0, gripperOpenValue };
        StartCoroutine(ExecutePoseRoutine(targetJoints, true)); // Requer Bolha
    }

    public void GoToHome()
    {
        double[] targetJoints = { 0.0, -1.5708, -0.05, -1.5708, 0.0, -1.5708, gripperOpenValue };  
        StartCoroutine(ExecutePoseRoutine(targetJoints, true)); // Requer Bolha
    }

    public void GoToPick()
    {
        // currentJoints[6] garante que a garra permaneça no estado que o jogador deixou
        double[] targetJoints = { 0.0, -1.5708, -1.5708, -1.5708, 1.5708, 1.5708, currentJoints[6] };
        StartCoroutine(ExecutePoseRoutine(targetJoints, false)); // Passe Livre
    }

    public void GoToPlace()
    {
        // currentJoints[6] garante que a garra permaneça no estado que o jogador deixou
        double[] targetJoints = { 0.710, -1.276, -1.833, -1.531, 1.509, 2.102, currentJoints[6] };
        StartCoroutine(ExecutePoseRoutine(targetJoints, false)); // Passe Livre
    }

    // --- A ROTINA DE MALHA FECHADA COM AUTO-ENGATE ---
    private IEnumerator ExecutePoseRoutine(double[] targetJoints, bool isSingularity)
    {
        // 1. DESENGATA A EMBREAGEM (Pausa o controle manual instantaneamente)
        if (handController != null) handController.PauseManualControl();

        // 2. Envia a intenção para o Cérebro Python no ROS
        SendIntentionCommand(targetJoints);

        // 3. Aguarda meio segundo para o Python calcular o polinômio e o Gazebo começar a mover
        yield return new WaitForSeconds(0.5f);

        // 4. Loop de Checagem Real
        float timeout = 12.0f; 
        float timer = 0f;

        while (timer < timeout)
        {
            bool hasArrived = true;

            if (hasReceivedJoints)
            {
                // Checa as 7 juntas
                for (int i = 0; i < 7; i++)
                {
                    if (Math.Abs(currentJoints[i] - targetJoints[i]) > arrivalTolerance)
                    {
                        hasArrived = false;
                        break; 
                    }
                }
            }
            else
            {
                hasArrived = false;
            }

            if (hasArrived)
            {
                Debug.Log("[Menu de Poses] Destino cruzou a tolerância! Aguardando estabilização mecânica...");
                break; 
            }

            timer += Time.deltaTime;
            yield return null; 
        }

        if (timer >= timeout) Debug.LogWarning("[Menu de Poses] Tempo limite excedido. Armando engate por segurança.");

        // --- O AJUSTE FINO (SETTLING TIME) ---
        // Espera 0.8 segundos para os PIDs do Gazebo frearem o robô completamente 
        // na posição exata ANTES de descolar o Gizmo e travar a bolha.
        yield return new WaitForSeconds(0.8f); 

        // 5. AUTO-ENGATE INTELIGENTE
        if (handController != null)
        {
            handController.ResumeManualControl(isSingularity);
        }

    } // Fim da rotina

    // --- ENVIO SIMPLIFICADO ---
    private void SendIntentionCommand(double[] jointPositions)
    {
        JointStateMsg msg = new JointStateMsg();
        msg.name = allJointNames;
        msg.position = jointPositions;
        
        ros.Publish(targetTopic, msg);
        Debug.Log("[Menu de Poses] Alvo enviado para o Planejador Polinomial no ROS.");
    }
}


















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Sensor; 
// using System.Collections;
// using System;

// public class RobotPoseMenu : MonoBehaviour
// {
//     [Header("Controle do Cabo de Guerra")]
//     public CartesianHandController handController;

//     [Header("Configurações da Nova Arquitetura")]
//     [Tooltip("Tópico onde o Unity envia o pedido de pose para o Python ler")]
//     public string targetTopic = "/unity/target_joints"; 
    
//     [Tooltip("Tópico onde o Unity ouve a realidade do Gazebo")]
//     public string jointStateTopic = "/ur5/joint_states"; 
//     private ROSConnection ros;

//     [Header("Configurações da Garra")]
//     public string gripperJointName = "robotiq_85_left_knuckle_joint"; 
//     public float gripperOpenValue = 0.0f;
//     public float gripperClosedValue = 0.8f;

//     [Header("Parâmetros de Malha Fechada")]
//     [Tooltip("Margem de erro em radianos para considerar que chegou no alvo")]
//     public float arrivalTolerance = 0.05f;

//     // A lista oficial de 7 juntas (6 do braço + 1 da garra)
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint",
//         "shoulder_lift_joint",
//         "elbow_joint",
//         "wrist_1_joint",
//         "wrist_2_joint",
//         "wrist_3_joint",
//         "robotiq_85_left_knuckle_joint" 
//     };

//     private double[] currentJoints = new double[7];
//     private bool hasReceivedJoints = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
        
//         // Novo Publicador Simples
//         ros.RegisterPublisher<JointStateMsg>(targetTopic);
        
//         // Mantemos o ouvinte para a malha fechada
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     // --- LEITURA DO GAZEBO ---
//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1)
//             {
//                 currentJoints[i] = msg.position[index];
//             }
//         }
//         hasReceivedJoints = true;
//     }

//     // --- POSES PREDEFINIDAS ---

//     public void GoToZero()
//     {
//         double[] targetJoints = { 0.0, -0.05, 0.10, -0.05, 0.0, 0.0, gripperOpenValue };
//         StartCoroutine(ExecutePoseRoutine(targetJoints, true)); // Requer Bolha
//     }

//     public void GoToHome()
//     {
//         double[] targetJoints = { 0.0, -1.5708, -0.05, -1.5708, 0.0, -1.5708, gripperOpenValue };  
//         StartCoroutine(ExecutePoseRoutine(targetJoints, true)); // Requer Bolha
//     }

//     public void GoToPick()
//     {
//         double[] targetJoints = { 0.0, -1.5708, -1.5708, -1.5708, 1.5708, 1.5708, gripperOpenValue };
//         StartCoroutine(ExecutePoseRoutine(targetJoints, false)); // Passe Livre
//     }

//     public void GoToPlace()
//     {
//         double[] targetJoints = { 0.710, -1.276, -1.833, -1.531, 1.509, 2.102, gripperOpenValue };
//         StartCoroutine(ExecutePoseRoutine(targetJoints, false)); // Passe Livre
//     }

//     // --- A ROTINA DE MALHA FECHADA COM AUTO-ENGATE ---
//     private IEnumerator ExecutePoseRoutine(double[] targetJoints, bool isSingularity)
//     {
//         // 1. DESENGATA A EMBREAGEM (Pausa o controle manual instantaneamente)
//         if (handController != null) handController.PauseManualControl();

//         // 2. Envia a intenção para o Cérebro Python no ROS
//         SendIntentionCommand(targetJoints);

//         // 3. Aguarda meio segundo para o Python calcular o polinômio e o Gazebo começar a mover
//         yield return new WaitForSeconds(0.5f);

//         // 4. Loop de Checagem Real
//         float timeout = 12.0f; 
//         float timer = 0f;

//         while (timer < timeout)
//         {
//             bool hasArrived = true;

//             if (hasReceivedJoints)
//             {
//                 // Checa as 7 juntas
//                 for (int i = 0; i < 7; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - targetJoints[i]) > arrivalTolerance)
//                     {
//                         hasArrived = false;
//                         break; 
//                     }
//                 }
//             }
//             else
//             {
//                 hasArrived = false;
//             }

//             if (hasArrived)
//             {
//                 Debug.Log("[Menu de Poses] Destino cruzou a tolerância! Aguardando estabilização mecânica...");
//                 break; 
//             }

//             timer += Time.deltaTime;
//             yield return null; 
//         }

//         if (timer >= timeout) Debug.LogWarning("[Menu de Poses] Tempo limite excedido. Armando engate por segurança.");

//         // --- O AJUSTE FINO (SETTLING TIME) ---
//         // Espera 0.8 segundos para os PIDs do Gazebo frearem o robô completamente 
//         // na posição exata ANTES de descolar o Gizmo e travar a bolha.
//         yield return new WaitForSeconds(0.8f); 

//         // 5. AUTO-ENGATE INTELIGENTE
//         if (handController != null)
//         {
//             handController.ResumeManualControl(isSingularity);
//         }

//     } // Fim da rotina

//     // --- ENVIO SIMPLIFICADO ---
//     private void SendIntentionCommand(double[] jointPositions)
//     {
//         JointStateMsg msg = new JointStateMsg();
//         msg.name = allJointNames;
//         msg.position = jointPositions;
        
//         ros.Publish(targetTopic, msg);
//         Debug.Log("[Menu de Poses] Alvo enviado para o Planejador Polinomial no ROS.");
//     }
// }


