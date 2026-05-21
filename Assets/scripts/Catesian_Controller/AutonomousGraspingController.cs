/*  
Script responsável por receber o comando de início da rotina de preensão autônoma (duplo "Legal" / Thumbs Up) e executar a sequência de movimentos 
para realizar a preensão usando os dados da GGCNN. Ele se comunica com o nó de ROS que calcula a cinemática inversa e o polinômio quíntuplo, enviando 
as poses alvo para o robô seguir.   
*/


using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std; 
using System.Collections;
using System;

public class AutonomousGraspingController : MonoBehaviour
{
    [Header("Dependências Core")]
    public CartesianHandController handController;
    public GGCNN_Subscriber ghostGripperController;
    public Transform robotBaseLink;

    [Header("Configurações ROS")]
    public string commandTopic = "unity/target_pose_autonomous";
    public string jointStateTopic = "/ur5/joint_states";

    [Header("Calibração de Trajetória")]
    public float preGraspHeightOffset = 0.08f; 
    public float gripperOpeningPadding = 0.01f;
    public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);

    private ROSConnection ros;
    private bool isExecutingGrasp = false;

    // --- Variáveis de Controle Concorrente da Garra ---
    private float currentTrackedGripperValue;
    private bool enforceGripperState = false;

    // --- Variáveis de Sincronismo (Malha Fechada) ---
    private readonly string[] allJointNames = new string[]
    {
        "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
        "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
    };
    private double[] currentJoints = new double[6];
    private bool hasReceivedJoints = false;
    private bool motionExecutionValid = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
        ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
    }

    void JointStateCallback(JointStateMsg msg)
    {
        for (int i = 0; i < allJointNames.Length; i++)
        {
            int index = Array.IndexOf(msg.name, allJointNames[i]);
            if (index != -1) currentJoints[i] = msg.position[index];
        }
        hasReceivedJoints = true;
    }

    public void TriggerGrasping()
    {
        if (isExecutingGrasp) return;

        if (handController == null || ghostGripperController == null || robotBaseLink == null)
        {
            Debug.LogError("[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!");
            return;
        }

        if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
        {
            StartCoroutine(GraspingRoutine());
        }
        else
        {
            Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma oculta.");
        }
    }

    private IEnumerator GripperEnforcementLoop()
    {
        while (isExecutingGrasp)
        {
            if (enforceGripperState) handController.SendGripperCommand(currentTrackedGripperValue);
            yield return new WaitForSeconds(0.05f); 
        }
    }

    private IEnumerator GraspingRoutine()
    {
        isExecutingGrasp = true;
        bool useFlippedOrientation = false; // <-- ESTADO INTELIGENTE DA SIMETRIA
        
        StartCoroutine(GripperEnforcementLoop());
        handController.PauseManualControl();

        // 1. CAPTURA DE DADOS
        Vector3 graspLocalUnityPos = ghostGripperController.GetAcceptedPosition();
        Quaternion graspLocalUnityRot = ghostGripperController.GetAcceptedRotation();
        float widthMeters = ghostGripperController.GetTargetWidth();

        // 2. ABERTURA DINÂMICA INICIAL
        float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
        currentTrackedGripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
        enforceGripperState = true; 
        
        yield return new WaitForSeconds(0.8f);

        // 3. VOO DE PRÉ-PREENSÃO (TENTATIVA 1)
        Vector3 preGraspLocalUnityPos = graspLocalUnityPos + (Vector3.up * preGraspHeightOffset);
        Debug.Log("[Grasping Modular] 2. Solicitando trajetória de Pré-Preensão ao ROS...");
        SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
        yield return new WaitForSeconds(0.4f); 
        yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
        // =====================================================================
        // SISTEMA DE AUTO-RECUPERAÇÃO (TENTATIVA 2 COM INVERSÃO DE SIMETRIA)
        // =====================================================================
        if (!motionExecutionValid)
        {
            Debug.LogWarning("[Grasping Modular] IK Rejeitado na orientação principal! Acionando Auto-Recuperação: girando a garra em 180° e recalculando...");
            
            useFlippedOrientation = true; // Ativa a inversão para o resto da rotina!
            SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
            
            yield return new WaitForSeconds(0.4f);
            yield return StartCoroutine(WaitForRobotToMoveAndStop());

            // Se falhar de novo, aí sim nós abortamos
            if (!motionExecutionValid)
            {
                AbortRoutine("IK REJEITADO mesmo após inversão de 180°. Alvo fisicamente fora de alcance.");
                yield break;
            }
            else
            {
                Debug.Log("<color=green>[Grasping Modular] Auto-Recuperação bem sucedida! O robô aceitou a orientação invertida.</color>");
            }
        }

        // 4. MERGULHO LINEAR (Utiliza a orientação que funcionou no passo 3)
        Debug.Log("[Grasping Modular] 3. Solicitando trajetória de Mergulho Linear...");
        SendPoseToPolynomialNode(graspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
        yield return new WaitForSeconds(0.4f);
        yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
        if (!motionExecutionValid)
        {
            AbortRoutine("IK do Ponto de Contato REJEITADO pelo ROS. Movimento perigoso.");
            yield break;
        }

        // 5. FECHAMENTO DA GARRA (PREENSÃO DA PEÇA)
        Debug.Log("[Grasping Modular] 4. Objeto alcançado. Efetuando fechamento da garra!");
        currentTrackedGripperValue = handController.gripperClosedValue; 
        
        yield return new WaitForSeconds(1.5f); 

        // 6. RETIRADA (LEVANTAMENTO COM MANUTENÇÃO DO GRASP)
        Debug.Log("[Grasping Modular] 5. Solicitando trajetória de Retirada (Içamento)...");
        SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
        yield return new WaitForSeconds(0.4f);
        yield return StartCoroutine(WaitForRobotToMoveAndStop());

        enforceGripperState = false; 
        isExecutingGrasp = false;
        handController.ResumeManualControl(false);
        Debug.Log("<color=green>[Grasping Modular] Operação concluída. Objeto seguro!</color>");
    }

    private void AbortRoutine(string reason)
    {
        Debug.LogError($"<color=red>[Grasping Modular] EXECUÇÃO ABORTADA:</color> {reason}");
        enforceGripperState = false;
        isExecutingGrasp = false;
        handController.SendGripperCommand(handController.gripperOpenValue);
        handController.ResumeManualControl(false);
    }

    private void SendPoseToPolynomialNode(Vector3 localUnityPos, Quaternion localUnityRot, bool applyFlip)
    {
        if (applyFlip)
        {
            // Aplica a inversão de 180 graus na orientação
            localUnityRot = localUnityRot * Quaternion.Euler(0, -180f, 0);
            Debug.Log("[Grasping Invertido] Garra girada em 180° para tentar alcançar alvos difíceis.");
        }

        PoseStampedMsg msg = new PoseStampedMsg();
        msg.header = new HeaderMsg { frame_id = "base_link" };

        msg.pose.position.x = localUnityPos.z + ikPositionOffset.x; 
        msg.pose.position.y = -localUnityPos.x + ikPositionOffset.y;
        msg.pose.position.z = localUnityPos.y + ikPositionOffset.z;

        msg.pose.orientation.x = -localUnityRot.z;
        msg.pose.orientation.y = localUnityRot.x; 
        msg.pose.orientation.z = -localUnityRot.y;
        msg.pose.orientation.w = localUnityRot.w;

        ros.Publish(commandTopic, msg);
    }

    private IEnumerator WaitForRobotToMoveAndStop()
    {
        motionExecutionValid = false;
        float timeoutToStart = 1.5f; // Tempo estendido para o robô começar a se mover
        float timer = 0f;
        
        double[] startJoints = new double[6];
        Array.Copy(currentJoints, startJoints, 6);
        bool hasStartedMoving = false;

        while (timer < timeoutToStart)
        {
            if (hasReceivedJoints)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (Math.Abs(currentJoints[i] - startJoints[i]) > 0.001f) 
                    {
                        hasStartedMoving = true;
                        break;
                    }
                }
            }
            if (hasStartedMoving) break;
            timer += Time.deltaTime;
            yield return null;
        }

        if (!hasStartedMoving)
        {
            motionExecutionValid = false;
            yield break;
        }

        motionExecutionValid = true;
        float timeoutToStop = 10.0f;
        timer = 0f;
        double[] previousJoints = new double[6];
        Array.Copy(currentJoints, previousJoints, 6);
        float stationaryTimer = 0f;

        while (timer < timeoutToStop)
        {
            if (hasReceivedJoints)
            {
                bool isMoving = false;
                for (int i = 0; i < 6; i++)
                {
                    if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f)
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
                    if (stationaryTimer >= 0.3f) break; 
                }
            }
            timer += Time.deltaTime;
            yield return null;
        }
    }
}









//problemática

// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std; 
// using System.Collections;
// using System;

// public class AutonomousGraspingController : MonoBehaviour
// {
//     [Header("Dependências Core")]
//     public CartesianHandController handController;
//     public GGCNN_Subscriber ghostGripperController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     public string commandTopic = "unity/target_pose_autonomous";
//     public string jointStateTopic = "/ur5/joint_states";

//     [Header("Calibração de Trajetória")]
//     public float preGraspHeightOffset = 0.08f; 
//     public float gripperOpeningPadding = 0.01f;
    
//     [Tooltip("Compensações para a Cinemática Inversa (X=0.1072, Y=0.05, Z=0.09)")]
//     public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);

//     private ROSConnection ros;
//     private bool isExecutingGrasp = false;

//     // --- Variáveis de Controle Concorrente da Garra ---
//     private float currentTrackedGripperValue;
//     private bool enforceGripperState = false;

//     // --- Variáveis de Sincronismo (Malha Fechada) ---
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };
//     private double[] currentJoints = new double[6];
//     private bool hasReceivedJoints = false;
//     private bool motionExecutionValid = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1) currentJoints[i] = msg.position[index];
//         }
//         hasReceivedJoints = true;
//     }

//     public void TriggerGrasping()
//     {
//         Debug.Log("[Grasping Modular] Executando Gatilho de Preensão.");

//         if (isExecutingGrasp) return;

//         if (handController == null || ghostGripperController == null || robotBaseLink == null)
//         {
//             Debug.LogError("[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!");
//             return;
//         }

//         if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
//         {
//             StartCoroutine(GraspingRoutine());
//         }
//         else
//         {
//             Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma oculta.");
//         }
//     }

//     private IEnumerator GripperEnforcementLoop()
//     {
//         while (isExecutingGrasp)
//         {
//             if (enforceGripperState) handController.SendGripperCommand(currentTrackedGripperValue);
//             yield return new WaitForSeconds(0.05f); 
//         }
//     }

//     private IEnumerator GraspingRoutine()
//     {
//         isExecutingGrasp = true;
//         bool useFlippedOrientation = false; 
        
//         StartCoroutine(GripperEnforcementLoop());
//         handController.PauseManualControl();

//         // 1. CAPTURA DE DADOS (Usamos a Pose PURA, ignorando a correção visual CAD)
//         Vector3 graspPureUnityPos = ghostGripperController.GetPurePosition();
//         Quaternion graspPureUnityRot = ghostGripperController.GetPureRotation();
//         float widthMeters = ghostGripperController.GetTargetWidth();

//         // 2. ABERTURA DINÂMICA
//         float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
//         currentTrackedGripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
//         enforceGripperState = true; 
        
//         yield return new WaitForSeconds(0.8f);

//         // 3. VOO DE PRÉ-PREENSÃO (Waypoint Offset)
//         // No local Unity (base_link), a altura é o eixo Y (up)
//         Vector3 preGraspPureUnityPos = graspPureUnityPos + (Vector3.up * preGraspHeightOffset);
        
//         Debug.Log("[Grasping Modular] 2. Solicitando trajetória de Pré-Preensão ao ROS...");
//         SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f); 
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             Debug.LogWarning("[Grasping Modular] IK Rejeitado! Acionando Auto-Recuperação de Simetria...");
            
//             useFlippedOrientation = true; 
//             SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
            
//             yield return new WaitForSeconds(0.4f);
//             yield return StartCoroutine(WaitForRobotToMoveAndStop());

//             if (!motionExecutionValid)
//             {
//                 AbortRoutine("IK REJEITADO definitivamente. Alvo fora do espaço de trabalho seguro.");
//                 yield break;
//             }
//         }

//         // 4. MERGULHO LINEAR 
//         Debug.Log("[Grasping Modular] 3. Mergulhando para o alvo...");
//         SendPoseToPolynomialNode(graspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             AbortRoutine("IK do Ponto de Contato REJEITADO. Colisão detectada no mergulho.");
//             yield break;
//         }

//         // 5. FECHAMENTO DA GARRA
//         Debug.Log("[Grasping Modular] 4. Fechando garra!");
//         currentTrackedGripperValue = handController.gripperClosedValue; 
        
//         yield return new WaitForSeconds(1.5f); 

//         // 6. RETIRADA (LEVANTAMENTO COM MANUTENÇÃO DO GRASP)
//         Debug.Log("[Grasping Modular] 5. Levantando o objeto...");
//         SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());

//         enforceGripperState = false; 
//         isExecutingGrasp = false;
//         handController.ResumeManualControl(false);
//         Debug.Log("<color=green>[Grasping Modular] Operação concluída com sucesso!</color>");
//     }

//     private void AbortRoutine(string reason)
//     {
//         Debug.LogError($"<color=red>[Grasping Modular] EXECUÇÃO ABORTADA:</color> {reason}");
//         enforceGripperState = false;
//         isExecutingGrasp = false;
//         handController.SendGripperCommand(handController.gripperOpenValue);
//         handController.ResumeManualControl(false);
//     }

//     // =========================================================================
//     // O MAPA MANUAL INFALÍVEL: Traduz a Pose Pura para os eixos do seu IK
//     // =========================================================================
//     private void SendPoseToPolynomialNode(Vector3 pureUnityLocalPos, Quaternion pureUnityLocalRot, bool applyFlip)
//     {
//         if (applyFlip)
//         {
//             // A inversão ocorre no eixo Yaw. 180° = Garra mantém a preensão, mas punho gira.
//             pureUnityLocalRot = pureUnityLocalRot * Quaternion.Euler(0, 180f, 0);
//         }

//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header = new HeaderMsg { frame_id = "base_link" };

//         // --- MAPA MANUAL DE POSIÇÃO (Idêntico ao que funciona no seu setup) ---
//         // localUnity.y -> ROS.z (Altura)
//         // -localUnity.x -> ROS.y (Lateral)
//         // localUnity.z -> ROS.x (Aproximação)
//         msg.pose.position.x = pureUnityLocalPos.z + ikPositionOffset.x; 
//         msg.pose.position.y = -pureUnityLocalPos.x + ikPositionOffset.y;
//         msg.pose.position.z = pureUnityLocalPos.y + ikPositionOffset.z;

//         // --- MAPA MANUAL DE ORIENTAÇÃO ---
//         msg.pose.orientation.x = -pureUnityLocalRot.z;
//         msg.pose.orientation.y = pureUnityLocalRot.x; 
//         msg.pose.orientation.z = -pureUnityLocalRot.y;
//         msg.pose.orientation.w = pureUnityLocalRot.w;

//         ros.Publish(commandTopic, msg);
//     }

//     private IEnumerator WaitForRobotToMoveAndStop()
//     {
//         motionExecutionValid = false;
//         float timeoutToStart = 3.0f; 
//         float timer = 0f;
        
//         double[] startJoints = new double[6];
//         Array.Copy(currentJoints, startJoints, 6);
//         bool hasStartedMoving = false;

//         while (timer < timeoutToStart)
//         {
//             if (hasReceivedJoints)
//             {
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - startJoints[i]) > 0.001f) 
//                     {
//                         hasStartedMoving = true;
//                         break;
//                     }
//                 }
//             }
//             if (hasStartedMoving) break;
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         if (!hasStartedMoving) yield break;

//         motionExecutionValid = true;
//         float timeoutToStop = 10.0f;
//         timer = 0f;
//         double[] previousJoints = new double[6];
//         Array.Copy(currentJoints, previousJoints, 6);
//         float stationaryTimer = 0f;

//         while (timer < timeoutToStop)
//         {
//             if (hasReceivedJoints)
//             {
//                 bool isMoving = false;
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f)
//                     {
//                         isMoving = true;
//                         break;
//                     }
//                 }

//                 if (isMoving)
//                 {
//                     stationaryTimer = 0f;
//                     Array.Copy(currentJoints, previousJoints, 6);
//                 }
//                 else
//                 {
//                     stationaryTimer += Time.deltaTime;
//                     if (stationaryTimer >= 0.3f) break; 
//                 }
//             }
//             timer += Time.deltaTime;
//             yield return null;
//         }
//     }
// }








// Problemática

// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry; // Necessário para a conversão To<FLU>()
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std; 
// using System.Collections;
// using System;

// public class AutonomousGraspingController : MonoBehaviour
// {
//     [Header("Dependências Core")]
//     public CartesianHandController handController;
//     public GGCNN_Subscriber ghostGripperController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     public string commandTopic = "unity/target_pose_autonomous";
//     public string jointStateTopic = "/ur5/joint_states";

//     [Header("Calibração de Trajetória")]
//     public float preGraspHeightOffset = 0.08f; 
//     public float gripperOpeningPadding = 0.01f;
//     public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);

//     private ROSConnection ros;
//     private bool isExecutingGrasp = false;

//     // --- Variáveis de Controle Concorrente da Garra ---
//     private float currentTrackedGripperValue;
//     private bool enforceGripperState = false;

//     // --- Variáveis de Sincronismo (Malha Fechada) ---
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };
//     private double[] currentJoints = new double[6];
//     private bool hasReceivedJoints = false;
//     private bool motionExecutionValid = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1) currentJoints[i] = msg.position[index];
//         }
//         hasReceivedJoints = true;
//     }

//     public void TriggerGrasping()
//     {
//         if (isExecutingGrasp) return;

//         if (handController == null || ghostGripperController == null || robotBaseLink == null)
//         {
//             Debug.LogError("[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!");
//             return;
//         }

//         if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
//         {
//             StartCoroutine(GraspingRoutine());
//         }
//         else
//         {
//             Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma oculta.");
//         }
//     }

//     private IEnumerator GripperEnforcementLoop()
//     {
//         while (isExecutingGrasp)
//         {
//             if (enforceGripperState) handController.SendGripperCommand(currentTrackedGripperValue);
//             yield return new WaitForSeconds(0.05f); 
//         }
//     }

//     private IEnumerator GraspingRoutine()
//     {
//         isExecutingGrasp = true;
//         bool useFlippedOrientation = false; 
        
//         StartCoroutine(GripperEnforcementLoop());
//         handController.PauseManualControl();

//         // 1. CAPTURA DE DADOS (Agora usamos a Pose Pura, ignorando a correção visual 3D)
//         Vector3 graspPureUnityPos = ghostGripperController.GetPurePosition();
//         Quaternion graspPureUnityRot = ghostGripperController.GetPureRotation();
//         float widthMeters = ghostGripperController.GetTargetWidth();

//         // 2. ABERTURA DINÂMICA
//         float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
//         currentTrackedGripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
//         enforceGripperState = true; 
        
//         yield return new WaitForSeconds(0.8f);

//         // 3. VOO DE PRÉ-PREENSÃO
//         // O vetor UP no Unity vira automaticamente o eixo Z (altura) quando convertido para ROS
//         Vector3 preGraspPureUnityPos = graspPureUnityPos + (Vector3.up * preGraspHeightOffset);
        
//         Debug.Log("[Grasping Modular] 2. Solicitando trajetória de Pré-Preensão ao ROS...");
//         SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f); 
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             Debug.LogWarning("[Grasping Modular] IK Rejeitado! Acionando Auto-Recuperação de Simetria...");
            
//             useFlippedOrientation = true; 
//             SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
            
//             yield return new WaitForSeconds(0.4f);
//             yield return StartCoroutine(WaitForRobotToMoveAndStop());

//             if (!motionExecutionValid)
//             {
//                 AbortRoutine("IK REJEITADO definitivamente. Posição fora do espaço de trabalho seguro.");
//                 yield break;
//             }
//         }

//         // 4. MERGULHO LINEAR 
//         Debug.Log("[Grasping Modular] 3. Mergulhando para o alvo...");
//         SendPoseToPolynomialNode(graspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             AbortRoutine("IK do Ponto de Contato REJEITADO. Colisão detectada no mergulho.");
//             yield break;
//         }

//         // 5. FECHAMENTO DA GARRA
//         Debug.Log("[Grasping Modular] 4. Fechando garra!");
//         currentTrackedGripperValue = handController.gripperClosedValue; 
        
//         yield return new WaitForSeconds(1.5f); 

//         // 6. RETIRADA 
//         Debug.Log("[Grasping Modular] 5. Levantando o objeto...");
//         SendPoseToPolynomialNode(preGraspPureUnityPos, graspPureUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());

//         enforceGripperState = false; 
//         isExecutingGrasp = false;
//         handController.ResumeManualControl(false);
//         Debug.Log("<color=green>[Grasping Modular] Operação concluída com sucesso!</color>");
//     }

//     private void AbortRoutine(string reason)
//     {
//         Debug.LogError($"<color=red>[Grasping Modular] ABORTADO:</color> {reason}");
//         enforceGripperState = false;
//         isExecutingGrasp = false;
//         handController.SendGripperCommand(handController.gripperOpenValue);
//         handController.ResumeManualControl(false);
//     }

//     private void SendPoseToPolynomialNode(Vector3 pureUnityPos, Quaternion pureUnityRot, bool applyFlip)
//     {
//         if (applyFlip)
//         {
//             // A inversão ocorre no eixo Yaw. 180° = Garra mantém a preensão, mas punho gira.
//             pureUnityRot = pureUnityRot * Quaternion.Euler(0, 180f, 0);
//         }

//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header = new HeaderMsg { frame_id = "base_link" };

//         // 1. Conversão Nativa (Unity -> ROS) infalível! 
//         // A função To<FLU>() já devolve os tipos oficiais PointMsg e QuaternionMsg.
//         msg.pose.position = pureUnityPos.To<FLU>();
//         msg.pose.orientation = pureUnityRot.To<FLU>();

//         // 2. Aplica as compensações do seu nó de Cinemática Inversa (IK)
//         msg.pose.position.x += ikPositionOffset.x; 
//         msg.pose.position.y += ikPositionOffset.y;
//         msg.pose.position.z += ikPositionOffset.z;

//         ros.Publish(commandTopic, msg);
//     }

//     private IEnumerator WaitForRobotToMoveAndStop()
//     {
//         motionExecutionValid = false;
//         float timeoutToStart = 3.0f; 
//         float timer = 0f;
        
//         double[] startJoints = new double[6];
//         Array.Copy(currentJoints, startJoints, 6);
//         bool hasStartedMoving = false;

//         while (timer < timeoutToStart)
//         {
//             if (hasReceivedJoints)
//             {
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - startJoints[i]) > 0.001f) 
//                     {
//                         hasStartedMoving = true;
//                         break;
//                     }
//                 }
//             }
//             if (hasStartedMoving) break;
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         if (!hasStartedMoving) yield break;

//         motionExecutionValid = true;
//         float timeoutToStop = 10.0f;
//         timer = 0f;
//         double[] previousJoints = new double[6];
//         Array.Copy(currentJoints, previousJoints, 6);
//         float stationaryTimer = 0f;

//         while (timer < timeoutToStop)
//         {
//             if (hasReceivedJoints)
//             {
//                 bool isMoving = false;
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f)
//                     {
//                         isMoving = true;
//                         break;
//                     }
//                 }

//                 if (isMoving)
//                 {
//                     stationaryTimer = 0f;
//                     Array.Copy(currentJoints, previousJoints, 6);
//                 }
//                 else
//                 {
//                     stationaryTimer += Time.deltaTime;
//                     if (stationaryTimer >= 0.3f) break; 
//                 }
//             }
//             timer += Time.deltaTime;
//             yield return null;
//         }
//     }
// }











// versão boa

// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std; 
// using System.Collections;
// using System;

// public class AutonomousGraspingController : MonoBehaviour
// {
//     [Header("Dependências Core")]
//     public CartesianHandController handController;
//     public GGCNN_Subscriber ghostGripperController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     public string commandTopic = "unity/target_pose_autonomous";
//     public string jointStateTopic = "/ur5/joint_states";

//     [Header("Calibração de Trajetória")]
//     public float preGraspHeightOffset = 0.08f; 
//     public float gripperOpeningPadding = 0.01f;
//     public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);

//     private ROSConnection ros;
//     private bool isExecutingGrasp = false;

//     // --- Variáveis de Controle Concorrente da Garra ---
//     private float currentTrackedGripperValue;
//     private bool enforceGripperState = false;

//     // --- Variáveis de Sincronismo (Malha Fechada) ---
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };
//     private double[] currentJoints = new double[6];
//     private bool hasReceivedJoints = false;
//     private bool motionExecutionValid = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1) currentJoints[i] = msg.position[index];
//         }
//         hasReceivedJoints = true;
//     }

//     public void TriggerGrasping()
//     {
//         if (isExecutingGrasp) return;

//         if (handController == null || ghostGripperController == null || robotBaseLink == null)
//         {
//             Debug.LogError("[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!");
//             return;
//         }

//         if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
//         {
//             StartCoroutine(GraspingRoutine());
//         }
//         else
//         {
//             Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma oculta.");
//         }
//     }

//     private IEnumerator GripperEnforcementLoop()
//     {
//         while (isExecutingGrasp)
//         {
//             if (enforceGripperState) handController.SendGripperCommand(currentTrackedGripperValue);
//             yield return new WaitForSeconds(0.05f); 
//         }
//     }

//     private IEnumerator GraspingRoutine()
//     {
//         isExecutingGrasp = true;
//         bool useFlippedOrientation = false; // <-- ESTADO INTELIGENTE DA SIMETRIA
        
//         StartCoroutine(GripperEnforcementLoop());
//         handController.PauseManualControl();

//         // 1. CAPTURA DE DADOS
//         Vector3 graspLocalUnityPos = ghostGripperController.GetAcceptedPosition();
//         Quaternion graspLocalUnityRot = ghostGripperController.GetAcceptedRotation();
//         float widthMeters = ghostGripperController.GetTargetWidth();

//         // 2. ABERTURA DINÂMICA INICIAL
//         float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
//         currentTrackedGripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
//         enforceGripperState = true; 
        
//         yield return new WaitForSeconds(0.8f);

//         // 3. VOO DE PRÉ-PREENSÃO (TENTATIVA 1)
//         Vector3 preGraspLocalUnityPos = graspLocalUnityPos + (Vector3.up * preGraspHeightOffset);
//         Debug.Log("[Grasping Modular] 2. Solicitando trajetória de Pré-Preensão ao ROS...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f); 
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         // =====================================================================
//         // SISTEMA DE AUTO-RECUPERAÇÃO (TENTATIVA 2 COM INVERSÃO DE SIMETRIA)
//         // =====================================================================
//         if (!motionExecutionValid)
//         {
//             Debug.LogWarning("[Grasping Modular] IK Rejeitado na orientação principal! Acionando Auto-Recuperação: girando a garra em 180° e recalculando...");
            
//             useFlippedOrientation = true; // Ativa a inversão para o resto da rotina!
//             SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
            
//             yield return new WaitForSeconds(0.4f);
//             yield return StartCoroutine(WaitForRobotToMoveAndStop());

//             // Se falhar de novo, aí sim nós abortamos
//             if (!motionExecutionValid)
//             {
//                 AbortRoutine("IK REJEITADO mesmo após inversão de 180°. Alvo fisicamente fora de alcance.");
//                 yield break;
//             }
//             else
//             {
//                 Debug.Log("<color=green>[Grasping Modular] Auto-Recuperação bem sucedida! O robô aceitou a orientação invertida.</color>");
//             }
//         }

//         // 4. MERGULHO LINEAR (Utiliza a orientação que funcionou no passo 3)
//         Debug.Log("[Grasping Modular] 3. Solicitando trajetória de Mergulho Linear...");
//         SendPoseToPolynomialNode(graspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             AbortRoutine("IK do Ponto de Contato REJEITADO pelo ROS. Movimento perigoso.");
//             yield break;
//         }

//         // 5. FECHAMENTO DA GARRA (PREENSÃO DA PEÇA)
//         Debug.Log("[Grasping Modular] 4. Objeto alcançado. Efetuando fechamento da garra!");
//         currentTrackedGripperValue = handController.gripperClosedValue; 
        
//         yield return new WaitForSeconds(1.5f); 

//         // 6. RETIRADA (LEVANTAMENTO COM MANUTENÇÃO DO GRASP)
//         Debug.Log("[Grasping Modular] 5. Solicitando trajetória de Retirada (Içamento)...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot, useFlippedOrientation);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());

//         enforceGripperState = false; 
//         isExecutingGrasp = false;
//         handController.ResumeManualControl(false);
//         Debug.Log("<color=green>[Grasping Modular] Operação concluída. Objeto seguro!</color>");
//     }

//     private void AbortRoutine(string reason)
//     {
//         Debug.LogError($"<color=red>[Grasping Modular] EXECUÇÃO ABORTADA:</color> {reason}");
//         enforceGripperState = false;
//         isExecutingGrasp = false;
//         handController.SendGripperCommand(handController.gripperOpenValue);
//         handController.ResumeManualControl(false);
//     }

//     private void SendPoseToPolynomialNode(Vector3 localUnityPos, Quaternion localUnityRot, bool applyFlip)
//     {
//         if (applyFlip)
//         {
//             // Aplica a inversão de 180 graus na orientação
//             localUnityRot = localUnityRot * Quaternion.Euler(0, 180f, 0);
//             Debug.Log("[Grasping Invertido] Garra girada em 180° para tentar alcançar alvos difíceis.");
//         }

//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header = new HeaderMsg { frame_id = "base_link" };

//         msg.pose.position.x = localUnityPos.z + ikPositionOffset.x; 
//         msg.pose.position.y = -localUnityPos.x + ikPositionOffset.y;
//         msg.pose.position.z = localUnityPos.y + ikPositionOffset.z;

//         msg.pose.orientation.x = -localUnityRot.z;
//         msg.pose.orientation.y = localUnityRot.x; 
//         msg.pose.orientation.z = -localUnityRot.y;
//         msg.pose.orientation.w = localUnityRot.w;

//         ros.Publish(commandTopic, msg);
//     }

//     private IEnumerator WaitForRobotToMoveAndStop()
//     {
//         motionExecutionValid = false;
//         float timeoutToStart = 1.5f; // Tempo estendido para o robô começar a se mover
//         float timer = 0f;
        
//         double[] startJoints = new double[6];
//         Array.Copy(currentJoints, startJoints, 6);
//         bool hasStartedMoving = false;

//         while (timer < timeoutToStart)
//         {
//             if (hasReceivedJoints)
//             {
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - startJoints[i]) > 0.001f) 
//                     {
//                         hasStartedMoving = true;
//                         break;
//                     }
//                 }
//             }
//             if (hasStartedMoving) break;
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         if (!hasStartedMoving)
//         {
//             motionExecutionValid = false;
//             yield break;
//         }

//         motionExecutionValid = true;
//         float timeoutToStop = 10.0f;
//         timer = 0f;
//         double[] previousJoints = new double[6];
//         Array.Copy(currentJoints, previousJoints, 6);
//         float stationaryTimer = 0f;

//         while (timer < timeoutToStop)
//         {
//             if (hasReceivedJoints)
//             {
//                 bool isMoving = false;
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f)
//                     {
//                         isMoving = true;
//                         break;
//                     }
//                 }

//                 if (isMoving)
//                 {
//                     stationaryTimer = 0f;
//                     Array.Copy(currentJoints, previousJoints, 6);
//                 }
//                 else
//                 {
//                     stationaryTimer += Time.deltaTime;
//                     if (stationaryTimer >= 0.3f) break; 
//                 }
//             }
//             timer += Time.deltaTime;
//             yield return null;
//         }
//     }
// }















// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std; 
// using System.Collections;
// using System;

// public class AutonomousGraspingController : MonoBehaviour
// {
//     [Header("Dependências Core")]
//     public CartesianHandController handController;
//     public GGCNN_Subscriber ghostGripperController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     public string commandTopic = "unity/target_pose_autonomous";
//     public string jointStateTopic = "/ur5/joint_states";

//     [Header("Calibração de Trajetória")]
//     public float preGraspHeightOffset = 0.08f; 

//     [Tooltip("Folga de segurança adicionada à abertura da garra (em metros). Ex: 0.01 = 1cm")]
//     public float gripperOpeningPadding = 0.01f;    
    
//     [Tooltip("Compensações para a Cinemática Inversa (X=0.1072, Y=0.05, Z=0.09)")]
//     public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);

//     private ROSConnection ros;
//     private bool isExecutingGrasp = false;

//     // --- Variáveis de Controle Concorrente da Garra ---
//     private float currentTrackedGripperValue;
//     private bool enforceGripperState = false;

//     // --- Variáveis de Sincronismo (Malha Fechada) ---
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };
//     private double[] currentJoints = new double[6];
//     private bool hasReceivedJoints = false;
//     private bool motionExecutionValid = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1) currentJoints[i] = msg.position[index];
//         }
//         hasReceivedJoints = true;
//     }

//     public void TriggerGrasping()
//     {
//         Debug.Log("[Grasping Modular] Executando Gatilho de Preensão.");

//         if (isExecutingGrasp) return;

//         if (handController == null || ghostGripperController == null || robotBaseLink == null)
//         {
//             Debug.LogError("[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!");
//             return;
//         }

//         if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
//         {
//             StartCoroutine(GraspingRoutine());
//         }
//         else
//         {
//             Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma oculta (IA sem alvos válidos).");
//         }
//     }

//     // =========================================================================
//     // LOOP DE PERSISTÊNCIA: Força o estado da garra contra sobreposições do ROS
//     // =========================================================================
//     private IEnumerator GripperEnforcementLoop()
//     {
//         while (isExecutingGrasp)
//         {
//             if (enforceGripperState)
//             {
//                 handController.SendGripperCommand(currentTrackedGripperValue);
//             }
//             yield return new WaitForSeconds(0.05f); // Envia a 20Hz
//         }
//     }

//     private IEnumerator GraspingRoutine()
//     {
//         isExecutingGrasp = true;
        
//         // Dispara o loop de persistência da garra em paralelo
//         StartCoroutine(GripperEnforcementLoop());

//         handController.PauseManualControl();

//         // 1. CAPTURA DE DADOS
//         Vector3 graspLocalUnityPos = ghostGripperController.GetAcceptedPosition();
//         Quaternion graspLocalUnityRot = ghostGripperController.GetAcceptedRotation();
//         float widthMeters = ghostGripperController.GetTargetWidth();

//         // 2. ABERTURA DINÂMICA INICIAL
//         float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
//         currentTrackedGripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
//         enforceGripperState = true; 
        
//         Debug.Log($"[Grasping Modular] 1. Definindo abertura alvo: {safeOpeningMeters*100:F1}cm");
//         yield return new WaitForSeconds(0.8f);

//         // 3. VOO DE PRÉ-PREENSÃO (SOBREVOO)
//         Vector3 preGraspLocalUnityPos = graspLocalUnityPos + (Vector3.up * preGraspHeightOffset);
//         Debug.Log("[Grasping Modular] 2. Solicitando trajetória de Pré-Preensão ao ROS...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.4f); // Margem para processamento do polinômio
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             AbortRoutine("IK do Ponto de Pré-Preensão REJEITADO pelo ROS. Alvo fora de alcance.");
//             yield break;
//         }

//         // 4. MERGULHO LINEAR (DESCIDA)
//         Debug.Log("[Grasping Modular] 3. Solicitando trajetória de Mergulho Linear...");
//         SendPoseToPolynomialNode(graspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());
        
//         if (!motionExecutionValid)
//         {
//             AbortRoutine("IK do Ponto de Contato REJEITADO pelo ROS. Movimento perigoso.");
//             yield break;
//         }

//         // 5. FECHAMENTO DA GARRA (PREENSÃO DA PEÇA)
//         Debug.Log("[Grasping Modular] 4. Objeto alcançado. Efetuando fechamento da garra!");
//         currentTrackedGripperValue = handController.gripperClosedValue; // Força fechamento total (0.8)
        
//         yield return new WaitForSeconds(1.5f); // Tempo mecânico para prender o objeto

//         // 6. RETIRADA (LEVANTAMENTO COM MANUTENÇÃO DO GRASP)
//         Debug.Log("[Grasping Modular] 5. Solicitando trajetória de Retirada (Içamento)...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.4f);
//         yield return StartCoroutine(WaitForRobotToMoveAndStop());

//         // FINALIZAÇÃO COM SUCESSO: Desliga a automação, mas NÃO abre a garra!
//         enforceGripperState = false; 
//         isExecutingGrasp = false;
        
//         // Mantém a garra fechada ao devolver o controle manual. 
//         // O operador precisará abrir voluntariamente fazendo o gesto de mão aberta.
//         handController.ResumeManualControl(false);
//         Debug.Log("<color=green>[Grasping Modular] Operação concluída. Objeto seguro!</color>");
//     }

//     private void AbortRoutine(string reason)
//     {
//         Debug.LogError($"<color=red>[Grasping Modular] EXECUÇÃO ABORTADA:</color> {reason}");
//         enforceGripperState = false;
//         isExecutingGrasp = false;
        
//         // Abre a garra por segurança e devolve o comando ao operador
//         handController.SendGripperCommand(handController.gripperOpenValue);
//         handController.ResumeManualControl(false);
//     }

//     private void SendPoseToPolynomialNode(Vector3 localUnityPos, Quaternion localUnityRot)
//     {
//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header = new HeaderMsg { frame_id = "base_link" };

//         msg.pose.position.x = localUnityPos.z + ikPositionOffset.x; 
//         msg.pose.position.y = -localUnityPos.x + ikPositionOffset.y;
//         msg.pose.position.z = localUnityPos.y + ikPositionOffset.z;

//         msg.pose.orientation.x = -localUnityRot.z;
//         msg.pose.orientation.y = localUnityRot.x; 
//         msg.pose.orientation.z = -localUnityRot.y;
//         msg.pose.orientation.w = localUnityRot.w;

//         ros.Publish(commandTopic, msg);
//     }

//     // =========================================================================
//     // VERIFICAÇÃO EM DUAS FASES (Anti-congelamento e Validação de IK)
//     // =========================================================================
//     private IEnumerator WaitForRobotToMoveAndStop()
//     {
//         motionExecutionValid = false;
//         float timeoutToStart = 1.5f;   //valor sugerido 1,5
//         float timer = 0f;
        
//         double[] startJoints = new double[6];
//         Array.Copy(currentJoints, startJoints, 6);
//         bool hasStartedMoving = false;

//         // FASE 1: Espera o robô quebrar a inércia e começar a se mover
//         while (timer < timeoutToStart)
//         {
//             if (hasReceivedJoints)
//             {
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - startJoints[i]) > 0.001f) // Mudança de 0.001 radianos
//                     {
//                         hasStartedMoving = true;
//                         break;
//                     }
//                 }
//             }
//             if (hasStartedMoving) break;
//             timer += Time.deltaTime;
//             yield return null;
//         }

//         if (!hasStartedMoving)
//         {
//             // Se o robô não se moveu em 1.5s, o nó ROS rejeitou a pose!
//             motionExecutionValid = false;
//             yield break;
//         }

//         // FASE 2: O movimento foi aceito. Agora espera o robô parar completamente.
//         motionExecutionValid = true;
//         float timeoutToStop = 10.0f;
//         timer = 0f;
//         double[] previousJoints = new double[6];
//         Array.Copy(currentJoints, previousJoints, 6);
//         float stationaryTimer = 0f;

//         while (timer < timeoutToStop)
//         {
//             if (hasReceivedJoints)
//             {
//                 bool isMoving = false;
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f)
//                     {
//                         isMoving = true;
//                         break;
//                     }
//                 }

//                 if (isMoving)
//                 {
//                     stationaryTimer = 0f;
//                     Array.Copy(currentJoints, previousJoints, 6);
//                 }
//                 else
//                 {
//                     stationaryTimer += Time.deltaTime;
//                     if (stationaryTimer >= 0.3f) break; // Considera parado após 0.3s estável
//                 }
//             }
//             timer += Time.deltaTime;
//             yield return null;
//         }
//     }
// }














// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Sensor;
// using RosMessageTypes.Std; 
// using System.Collections;
// using System;

// public class AutonomousGraspingController : MonoBehaviour
// {
//     [Header("Dependências Core")]
//     public CartesianHandController handController;
//     public GGCNN_Subscriber ghostGripperController;
//     public Transform robotBaseLink;

//     [Header("Configurações ROS")]
//     public string commandTopic = "unity/target_pose_autonomous";
//     public string jointStateTopic = "/ur5/joint_states";

//     [Header("Calibração de Trajetória")]
//     public float preGraspHeightOffset = 0.08f; 

//     [Tooltip("Folga de segurança adicionada à abertura da garra (em metros). Ex: 0.01 = 1cm")]
//     public float gripperOpeningPadding = 0.01f;    
    
//     [Tooltip("Compensações para a Cinemática Inversa (X=0.1072, Y=0.05, Z=0.09)")]
//     public Vector3 ikPositionOffset = new Vector3(0.1072f, 0.05f, 0.09f);
    

//     private ROSConnection ros;
//     private bool isExecutingGrasp = false;

//     // --- Variáveis de Sincronismo (Malha Fechada) ---
//     private readonly string[] allJointNames = new string[]
//     {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };
//     private double[] currentJoints = new double[6];
//     private bool hasReceivedJoints = false;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(commandTopic);
//         ros.Subscribe<JointStateMsg>(jointStateTopic, JointStateCallback);
//     }

//     void JointStateCallback(JointStateMsg msg)
//     {
//         for (int i = 0; i < allJointNames.Length; i++)
//         {
//             int index = Array.IndexOf(msg.name, allJointNames[i]);
//             if (index != -1) currentJoints[i] = msg.position[index];
//         }
//         hasReceivedJoints = true;
//     }

//     public void TriggerGrasping()
//     {
//         Debug.Log("[Grasping Modular] 0. Recebeu o comando de gatilho do Gesto Bimanual!");

//         if (isExecutingGrasp) return;

//         if (handController == null || ghostGripperController == null || robotBaseLink == null)
//         {
//             Debug.LogError("<color=red>[Grasping Modular] ERRO FATAL: Faltam referências no Inspector!</color>");
//             return;
//         }

//         if (ghostGripperController.visualGripperBase != null && ghostGripperController.visualGripperBase.gameObject.activeSelf)
//         {
//             StartCoroutine(GraspingRoutine());
//         }
//         else
//         {
//             Debug.LogWarning("[Grasping Modular] Abortado: Garra fantasma está invisível. A IA perdeu o objeto de vista.");
//         }
//     }

//     private IEnumerator GraspingRoutine()
//     {
//         isExecutingGrasp = true;
//         handController.PauseManualControl();

//         // 1. DADOS DA GARRA FANTASMA (Já estão no referencial Local da Base!)
//         Vector3 graspLocalUnityPos = ghostGripperController.GetAcceptedPosition();
//         Quaternion graspLocalUnityRot = ghostGripperController.GetAcceptedRotation();
//         float widthMeters = ghostGripperController.GetTargetWidth();

//         // 2. ABERTURA DINÂMICA
//         float safeOpeningMeters = Mathf.Min(widthMeters + gripperOpeningPadding, 0.14f);
//         float gripperValue = Mathf.Lerp(handController.gripperClosedValue, handController.gripperOpenValue, safeOpeningMeters / 0.14f);
        
//         Debug.Log($"[Grasping Modular] 1. Abrindo garra. Valor ROS: {gripperValue:F2}");
//         handController.SendGripperCommand(gripperValue);
        
//         yield return new WaitForSeconds(0.8f);

//         // 3. VOO DE PRÉ-PREENSÃO (Adiciona a altura no Y do Unity, que é o Z local)
//         Vector3 preGraspLocalUnityPos = graspLocalUnityPos + (Vector3.up * preGraspHeightOffset);
        
//         Debug.Log("[Grasping Modular] 2. Enviando robô para Ponto de Pré-Preensão (Sobrevoo)...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.5f); 
//         yield return StartCoroutine(WaitForRobotToStop());

//         // 4. MERGULHO LINEAR
//         Debug.Log("[Grasping Modular] 3. Mergulhando para o alvo (Contato)...");
//         SendPoseToPolynomialNode(graspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.5f);
//         yield return StartCoroutine(WaitForRobotToStop());

//         // 5. FECHAMENTO (GRASP)
//         Debug.Log("[Grasping Modular] 4. Fechando garra!");
//         handController.SendGripperCommand(handController.gripperClosedValue);
        
//         yield return new WaitForSeconds(1.5f); 

//         // 6. RETIRADA
//         Debug.Log("[Grasping Modular] 5. Levantando o objeto...");
//         SendPoseToPolynomialNode(preGraspLocalUnityPos, graspLocalUnityRot);
        
//         yield return new WaitForSeconds(0.5f);
//         yield return StartCoroutine(WaitForRobotToStop());

//         handController.ResumeManualControl(false);
//         isExecutingGrasp = false;
//         Debug.Log("<color=green>[Grasping Modular] Rotina Finalizada com Sucesso!</color>");
//     }

//     private void SendPoseToPolynomialNode(Vector3 localUnityPos, Quaternion localUnityRot)
//     {
//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header = new HeaderMsg { frame_id = "base_link" };

//         // Como a posição já é local, APENAS mapeamos os eixos do Unity de volta para o ROS (FLU)
//         // e aplicamos o Offset de calibração que você usa no seu nó IK.
//         msg.pose.position.x = localUnityPos.z + ikPositionOffset.x; 
//         msg.pose.position.y = -localUnityPos.x + ikPositionOffset.y;
//         msg.pose.position.z = localUnityPos.y + ikPositionOffset.z;

//         msg.pose.orientation.x = -localUnityRot.z;
//         msg.pose.orientation.y = localUnityRot.x; 
//         msg.pose.orientation.z = -localUnityRot.y;
//         msg.pose.orientation.w = localUnityRot.w;

//         ros.Publish(commandTopic, msg);
//     }

//     private IEnumerator WaitForRobotToStop()
//     {
//         float timeout = 10.0f;
//         float timer = 0f;
//         double[] previousJoints = new double[6];
//         Array.Copy(currentJoints, previousJoints, 6);
//         float stationaryTimer = 0f;

//         while (timer < timeout)
//         {
//             if (hasReceivedJoints)
//             {
//                 bool isMoving = false;
//                 for (int i = 0; i < 6; i++)
//                 {
//                     if (Math.Abs(currentJoints[i] - previousJoints[i]) > 0.001f) 
//                     {
//                         isMoving = true;
//                         break;
//                     }
//                 }

//                 if (isMoving)
//                 {
//                     stationaryTimer = 0f;
//                     Array.Copy(currentJoints, previousJoints, 6);
//                 }
//                 else
//                 {
//                     stationaryTimer += Time.deltaTime;
//                     if (stationaryTimer >= 0.3f) break; 
//                 }
//             }
//             timer += Time.deltaTime;
//             yield return null; 
//         }
//     }
// }