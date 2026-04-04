using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using Unity.Robotics.UrdfImporter;
using System.Linq;
using RosMessageTypes.Std; // NOVO: Para Float64MultiArrayMsg

/// <summary>
/// Aplica os valores das juntas à simulação ArticulationBody, publica no ROS
/// e gerencia a comunicação de Reset.
/// </summary>
public class UR5JointSimulator : MonoBehaviour
{
    // --- Configurações ROS ---
    public string topicName = "unity/joint_command";
    private ROSConnection ros;
    private JointStateMsg jointState;

    [Header("External References")]
    public ROSTrajectorySubscriber trajectorySubscriber; // NOVO

    [Header("MoveIt/Reset Configuration")]
    public string resetGoalTopic = "reset_pose_request"; // Tópico para o nó Python escutar
    public float resetExecutionTime = 4.0f; // Tempo que o nó Python levará para executar o reset

    // --- Componentes do Robô ---
    public ArticulationBody[] robotJoints = new ArticulationBody[6];
    public ArticulationBody gripperJoint;

    // --- Nomes das Juntas (para o Unity/ROS) ---
    [HideInInspector]
    public string[] jointNames = {
        "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
        "wrist_1_joint", "wrist_2_joint", "wrist_3_joint",
        "gripper_joint"
    };

    

    // --- Referência ao Controlador VR ---
    public UR5JointTeleopVR vrController;

    // --- DEAD BAND (Mantido para Teleoperação) ---
    [Header("Deadband (em graus para Unity)")]
    public float deadbandDegrees = 0.5f;
    private float[] lastSentJointValues = new float[7];
    
    private bool isExecutingReset = false; // Flag para bloquear a teleoperação

    private void Start()
    {
        // 1. Inicialização ROS
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(topicName);
        
        // NOVO: Registra o publisher para enviar o sinal de reset
        ros.RegisterPublisher<Float64MultiArrayMsg>(resetGoalTopic);
        
        jointState = new JointStateMsg();
        
        // ... (Lógica de Encontrar ArticulationBody's permanece)
        Time.fixedDeltaTime = 1.0f / 50.0f;

        // Encontrar articulações
        var allUrdfJoints = GetComponentsInChildren<UrdfJoint>();
        int index = 0;

        foreach (var urdfJoint in allUrdfJoints)
        {
            if (urdfJoint.JointType != UrdfJoint.JointTypes.Fixed)
            {
                if (urdfJoint.jointName.Contains("robotiq") || urdfJoint.jointName.Contains("left_outer_knuckle"))
                {
                    gripperJoint = urdfJoint.GetComponent<ArticulationBody>();
                }
                else if (index < 6)
                {
                    robotJoints[index] = urdfJoint.GetComponent<ArticulationBody>();
                    index++;
                }
            }
        }

        if (vrController == null)
            Debug.LogError("UR5JointTeleopVR não está referenciado!");

    }

    /// <summary>
    /// Envia a pose de reset desejada para o nó Python ROS.
    /// </summary>
    public void SendResetGoalToROS(float[] resetPoseDegrees)
    {
        if (isExecutingReset) return;
        
        // 1. Converte a pose inteira para Radianos (ROS espera radianos)
        double[] resetPoseRad = new double[7];
        for (int i = 0; i < 6; i++)
        {
            // UR5: Graus (Unity) -> Radianos (ROS)
            resetPoseRad[i] = resetPoseDegrees[i] * Mathf.Deg2Rad;
        }
        // Garra: Já está em radianos (ou unidade ROS nativa)
        resetPoseRad[6] = resetPoseDegrees[6]; 
        
        // 2. Monta e publica a mensagem Float64MultiArrayMsg
        var msg = new Float64MultiArrayMsg { data = resetPoseRad };
        ros.Publish(resetGoalTopic, msg);

        // 3. Assume que o ROS está no controle e agenda a reativação
        isExecutingReset = true;
        
        // O nó Python levará 'resetExecutionTime' para terminar. 
        // Usamos Invoke para reativar o input VR.
        Invoke(nameof(FinishResetControl), resetExecutionTime + 0.5f); // + 0.5s de buffer
    }

    private void FinishResetControl()
    {
        isExecutingReset = false;
        
        // 1. Força a sincronia para que o controlador VR comece da pose final (0/Up)
        if (vrController.jointSubscriber != null)
        {
             // Usa a pose mais recente recebida do ROS, garantindo que o VR esteja no alvo.
             vrController.jointSubscriber.latestJointAngles.CopyTo(vrController.jointAngles, 0);
        }

        // 2. Reabilita o Input VR
        vrController.inputActions.XRControl.Enable();
        
        Debug.Log("[Soft Reset] Completed. VR controller reactivated.");
    }
    
    // ... (restante do FixedUpdate)
    
    private void FixedUpdate()
    {
        if (vrController == null) return;
        
        // // BLOQUEIA A TELEOPERAÇÃO ENQUANTO O RESET ESTIVER SENDO EXECUTADO PELO ROS
        if (isExecutingReset) return;

        // // 🔹 BLOQUEIA A TELEOPERAÇÃO se o Subscriber de Trajetória estiver ativo.
        // if (trajectorySubscriber != null && trajectorySubscriber.isExecutingSmoothReset) 
        // {
        //     // Neste modo, o ROSTrajectorySubscriber está movendo os ArticulationBody's.
        //     return; 
        // }

        // --- Restante da Teleoperação (DEAD BAND, Aplicação na Simulação, Publicação ROS) ---
        // ... (O código abaixo permanece o mesmo, sem modificações)
        float[] jointValues = vrController.jointAngles; // Em graus

        // --- DEAD BAND CHECK ---
        if (!ShouldSend(jointValues))
            return; // NÃO envia nem aplica se a mudança for pequena

        // --- APLICA NA SIMULAÇÃO UNITY ---
        for (int i = 0; i < 6; i++)
        {
            var drive = robotJoints[i].xDrive;
            drive.target = jointValues[i];
            robotJoints[i].xDrive = drive;
        }

        if (gripperJoint != null)
        {
            var gdrive = gripperJoint.xDrive;
            gdrive.target = jointValues[6];
            gripperJoint.xDrive = gdrive;
        }

        // --- PUBLICA NO ROS ---
        List<float> positions = new List<float>();
        for (int i = 0; i < 6; i++)
        {
            positions.Add(jointValues[i] * Mathf.Deg2Rad); // Unity graus → ROS radianos
        }
        positions.Add(jointValues[6] * Mathf.Deg2Rad); // Garra já em unidade ROS

        jointState.position = positions.Select(v => (double)v).ToArray();
        jointState.name = jointNames;

        ros.Publish(topicName, jointState);

        // Atualiza último comando enviado
        UpdateLastSent(jointValues);

    }

    // ... (Métodos ShouldSend e UpdateLastSent permanecem)

    // ---------------------------------------------------------------
    // DEAD BAND IMPLEMENTAÇÃO
    // ---------------------------------------------------------------

    /// <summary>
    /// Verifica se a diferença entre o valor atual e o último enviado
    /// ultrapassa o deadband configurado.
    /// </summary>
    private bool ShouldSend(float[] current)
    {
        for (int i = 0; i < 7; i++)
        {
            if (Mathf.Abs(current[i] - lastSentJointValues[i]) > deadbandDegrees)
                return true;
        }
        return false;
    }

    private void UpdateLastSent(float[] current)
    {
        for (int i = 0; i < 7; i++)
            lastSentJointValues[i] = current[i];
    }


}


















// using System.Collections.Generic;
// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Sensor;
// using Unity.Robotics.UrdfImporter;
// using System.Linq;

// /// <summary>
// /// Aplica os valores das juntas à simulação ArticulationBody e publica no ROS.
// /// Agora com implementação de DEAD BAND para evitar jitter/drift.
// /// </summary>
// public class UR5JointSimulator : MonoBehaviour
// {
//     // --- Configurações ROS ---
//     public string topicName = "unity/joint_command";
//     private ROSConnection ros;
//     private JointStateMsg jointState;

//     // --- Componentes do Robô ---
//     public ArticulationBody[] robotJoints = new ArticulationBody[6];
//     public ArticulationBody gripperJoint;

//     // --- Nomes das Juntas ---
//     [HideInInspector]
//     public string[] jointNames = {
//         "shoulder_pan_joint", "shoulder_lift_joint", "elbow_joint",
//         "wrist_1_joint", "wrist_2_joint", "wrist_3_joint"
//     };

//     // --- Controlador VR ---
//     public UR5JointTeleopVR vrController;

//     // --- DEAD BAND ---
//     [Header("Deadband (em graus para Unity)")]
//     public float deadbandDegrees = 0.5f;   // ~0.5° (recomendado)
//     private float[] lastSentJointValues = new float[7];   // 6 juntas + garra

//     private void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<JointStateMsg>(topicName);
//         jointState = new JointStateMsg();

//         Time.fixedDeltaTime = 1.0f / 50.0f;

//         // Encontrar articulações
//         var allUrdfJoints = GetComponentsInChildren<UrdfJoint>();
//         int index = 0;

//         foreach (var urdfJoint in allUrdfJoints)
//         {
//             if (urdfJoint.JointType != UrdfJoint.JointTypes.Fixed)
//             {
//                 if (urdfJoint.jointName.Contains("robotiq") || urdfJoint.jointName.Contains("knuckle"))
//                 {
//                     gripperJoint = urdfJoint.GetComponent<ArticulationBody>();
//                 }
//                 else if (index < 6)
//                 {
//                     robotJoints[index] = urdfJoint.GetComponent<ArticulationBody>();
//                     index++;
//                 }
//             }
//         }

//         if (vrController == null)
//             Debug.LogError("UR5JointTeleopVR não está referenciado!");
//     }

//     private void FixedUpdate()
//     {
//         if (vrController == null) return;

//         float[] jointValues = vrController.jointAngles; // Em graus

//         // --- DEAD BAND CHECK ---
//         if (!ShouldSend(jointValues))
//             return; // NÃO envia nem aplica se a mudança for pequena

//         // --- APLICA NA SIMULAÇÃO UNITY ---
//         for (int i = 0; i < 6; i++)
//         {
//             var drive = robotJoints[i].xDrive;
//             drive.target = jointValues[i];
//             robotJoints[i].xDrive = drive;
//         }

//         if (gripperJoint != null)
//         {
//             var gdrive = gripperJoint.xDrive;
//             gdrive.target = jointValues[6];
//             gripperJoint.xDrive = gdrive;
//         }

//         // --- PUBLICA NO ROS ---
//         List<float> positions = new List<float>();
//         for (int i = 0; i < 6; i++)
//         {
//             positions.Add(jointValues[i] * Mathf.Deg2Rad); // Unity graus → ROS radianos
//         }
//         positions.Add(jointValues[6]); // Garra já em unidade ROS

//         jointState.position = positions.Select(v => (double)v).ToArray();
//         jointState.name = jointNames;

//         ros.Publish(topicName, jointState);

//         // Atualiza último comando enviado
//         UpdateLastSent(jointValues);
//     }


//     // ---------------------------------------------------------------
//     // DEAD BAND IMPLEMENTAÇÃO
//     // ---------------------------------------------------------------

//     /// <summary>
//     /// Verifica se a diferença entre o valor atual e o último enviado
//     /// ultrapassa o deadband configurado.
//     /// </summary>
//     private bool ShouldSend(float[] current)
//     {
//         for (int i = 0; i < 7; i++)
//         {
//             if (Mathf.Abs(current[i] - lastSentJointValues[i]) > deadbandDegrees)
//                 return true;
//         }
//         return false;
//     }

//     private void UpdateLastSent(float[] current)
//     {
//         for (int i = 0; i < 7; i++)
//             lastSentJointValues[i] = current[i];
//     }
// }












