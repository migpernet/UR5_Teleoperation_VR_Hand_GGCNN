using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using TMPro;
using System;
using RosMessageTypes.Std;

/// <summary>
/// Responsável APENAS por calcular os valores das juntas (jointAngles) usando o Input VR,
/// gerenciar o feedback visual e iniciar o Reset via ROS.
/// </summary>
/// 
public class UR5JointTeleopVR : MonoBehaviour
{
    // --- Feedback 3D ---
    [Header("3D Visual Feedback")]
    public GameObject ghostSpherePrefab;
    public Transform[] jointTargets;

    // Variável de controle (em Graus)
    [HideInInspector]
    public float[] jointAngles = new float[7];

    [HideInInspector]
    public MyInputActions inputActions;

    [Header("Joint Control Settings")]
    public float rotationSpeed = 1.0f;
    [HideInInspector]
    public int activeJointIndex = 0;

    // 🎯 NOVO: Limites Individuais para as 6 juntas do UR5 (Configurável no Inspector)
    // Padrão: -180 a 180, mas você deve ajustar no Unity para evitar colisões (ex: Mesa)
    [Header("UR5 Individual Joint Limits (Degrees)")]
    public float[] minLimits = { -360f, -180f, -150f, -360f, -360f, -360f }; 
    public float[] maxLimits = {  360f,    0f,  150f,  360f,  360f,  360f };

    [Header("System References")]
    public ROSJointStateSubscriber jointSubscriber;
    public UR5JointSimulator simulator;

    [Header("Visual Feedback")]
    public TextMeshPro jointNameDisplay;

    // Limites da Garra (Mantidos fixos pois é mecânico)
    private readonly float jointMin_Gripper = 0.0f;
    private readonly float jointMax_Gripper = 44.0f;
    
    private readonly Vector3 GHOST_HOME_POSITION = new Vector3(0f, -10f, 0f);

    // Pose de Reset (Graus)
    private readonly float[] RESET_POSE_DEGREES = { 0f, -90f, 0f, -90f, 0f, 90f, 0.0f };


    void Awake()
    {
        inputActions = new MyInputActions();

        // 1. Inscrição para seleção de junta
        inputActions.XRControl.nextJointButton.performed += context => ChangeJointSelection(1);
        inputActions.XRControl.prevJointButton.performed += context => ChangeJointSelection(-1);
        
        // 2. Inscrição para Reset Suave
        inputActions.XRControl.resetPoseButton.performed += context => TriggerSmoothResetROS(); 
        
        // Initializes Joint Display
        if (jointNameDisplay != null && simulator != null)
        {
            string initialJointName = simulator.jointNames[activeJointIndex];
            jointNameDisplay.text = $"Active Joint:\n{initialJointName}";
        }
    }

    void Start()
    {
        // 🔹 Configuração da Sincronização Inicial
        if (jointSubscriber != null)
        {
            jointSubscriber.OnJointAnglesUpdated += SyncInitialPose;
            jointSubscriber.TriggerInitialSync();
        }
        else
        {
            Debug.LogError("[VR Teleop] jointSubscriber não atribuído no Inspetor!");
        }

        // Posiciona a esfera fantasma na junta inicial (0)
        UpdateGhostSpherePosition();

        //debug o nome das juntas
        Debug.Log($"[VR Teleop] Joint Names: {string.Join(", ", simulator.jointNames)}"); 


    }

    void OnEnable() => inputActions.XRControl.Enable();

    void OnDisable()
    {
        inputActions.XRControl.Disable();
        
        // Limpeza de lambdas
        inputActions.XRControl.nextJointButton.performed -= context => ChangeJointSelection(1);
        inputActions.XRControl.prevJointButton.performed -= context => ChangeJointSelection(-1);
        inputActions.XRControl.resetPoseButton.performed -= context => TriggerSmoothResetROS();
    }
    
    /// <summary>
    /// Sincroniza o array de controle VR com a pose real do robô e se desinscreve.
    /// </summary>
    private void SyncInitialPose(float[] angles)
    {
        if (angles == null || angles.Length == 0) return;

        angles.CopyTo(jointAngles, 0);
        
        if (jointSubscriber != null)
        {
            jointSubscriber.OnJointAnglesUpdated -= SyncInitialPose;
            Debug.Log("[VR Teleop] Sincronização CONCLUÍDA. Controle VR assumido.");
        }
    }

    // --- Lógica de Reset Suave ---
    void TriggerSmoothResetROS()
    {
        if (simulator != null)
        {
            if (ghostSpherePrefab != null)
            {
                ghostSpherePrefab.transform.position = GHOST_HOME_POSITION;
                ghostSpherePrefab.SetActive(false);
            }

            simulator.SendResetGoalToROS(RESET_POSE_DEGREES);
            
            inputActions.XRControl.Disable();
            
            Debug.Log("[VR Teleop] Comando de Reset Suave enviado. Input desabilitado.");
        }
    }
    
    // --- Funções de Manipulação ---

    void Update() => HandleJointMovement();

    void ChangeJointSelection(int direction)
    {
        activeJointIndex += direction;
        int totalJoints = jointAngles.Length;

        // 1. Lógica de loop (Clamping)
        if (activeJointIndex >= totalJoints) activeJointIndex = 0;
        else if (activeJointIndex < 0) activeJointIndex = totalJoints - 1;

        // 2. Centrelized Action: Move, activate and check the sphere.
        UpdateGhostSpherePosition(); 
        
        // 3. Feedback: Update TextMeshPro and Debug.Log
        if (simulator != null)
        {
            string currentJointName = simulator.jointNames[activeJointIndex];
            if (jointNameDisplay != null)
                jointNameDisplay.text = $"Active Joint:\n{currentJointName}";
            Debug.Log($"[VR Teleop] Active Joint: {currentJointName}");
        }
    }

    /// <summary>
    ///  Optimized: Return the limits based on the configurable array for each joint.
    /// </summary>
    private (float min, float max) GetActiveJointLimits()
    {
        // If it is a claw (index 6)
        if (activeJointIndex == 6)
        {
            return (jointMin_Gripper, jointMax_Gripper);
        }
        // If it is a UR5 (Indices 0 to 5)
        else if (activeJointIndex >= 0 && activeJointIndex < 6)
        {
            // Retorn the specific limit configured in the inspector for this joint.
            return (minLimits[activeJointIndex], maxLimits[activeJointIndex]);
        }
        
        // Standard Fallback (In case something goes wrong with the indexes)
        return (-180f, 180f);
    }

    /// <summary>
    /// Optimized: Move the joint using Only the Joiystick (direct rate).
    /// </summary>
    void HandleJointMovement()
    {
        if (!inputActions.XRControl.enabled) return;
        
        // Now call the function the searches for individual boundaries
        (float minLimit, float maxLimit) = GetActiveJointLimits();
        
        // 1.Reading the Inputs (Joistick X-axis)
        float movementAxis = inputActions.XRControl.joystickAction.ReadValue<Vector2>().x;

        // 2. Condition for moviment
        if (Mathf.Abs(movementAxis) > 0.1f)
        {
            float delta = movementAxis * rotationSpeed * Time.deltaTime;
            
            jointAngles[activeJointIndex] = Mathf.Clamp(
                jointAngles[activeJointIndex] + delta,
                minLimit, maxLimit
            );
        }
    }

    public void ReActivateInputAndVisuals()
    {
        inputActions.XRControl.Enable();
        UpdateGhostSpherePosition();
        Debug.Log("[VR Control] Input and visuals reactivated.");
    }
    
    private void UpdateGhostSpherePosition()
    {
        if (ghostSpherePrefab != null && jointTargets.Length > activeJointIndex)
        {
            Transform targetTransform = jointTargets[activeJointIndex];
            
            ghostSpherePrefab.SetActive(true); 

            // Referencial glocal world
            ghostSpherePrefab.transform.position = targetTransform.position;
            ghostSpherePrefab.transform.rotation = targetTransform.rotation;

            // Usando REFERENCIAL LOCAL
            // ghostSpherePrefab.transform.localPosition = targetTransform.localPosition;
            // ghostSpherePrefab.transform.localRotation = targetTransform.localRotation;

            // debug o nome da junta
            Debug.Log($"[VR Teleop] Ghost Sphere moved to Joint Name (AQUI): {simulator.jointNames[activeJointIndex]}");
            // debug a posição da esfere
            Debug.Log($"[VR Teleop] Ghost Sphere Position: {ghostSpherePrefab.transform.position}");
           
            //debug a rotação da esfera
            Debug.Log($"[VR Teleop] Ghost Sphere Rotation: {ghostSpherePrefab.transform.rotation}");
            
        }
        else if (ghostSpherePrefab != null)
        {
            ghostSpherePrefab.SetActive(false);
            ghostSpherePrefab.transform.position = GHOST_HOME_POSITION; 
        }
    }
}














// using UnityEngine;
// using UnityEngine.InputSystem;
// using Unity.Robotics.ROSTCPConnector;
// using TMPro;
// using System;
// using RosMessageTypes.Std;

// /// <summary>
// /// Responsável APENAS por calcular os valores das juntas (jointAngles) usando o Input VR,
// /// gerenciar o feedback visual e iniciar o Reset via ROS.
// /// </summary>
// public class UR5JointTeleopVR : MonoBehaviour
// {
//     // --- Feedback 3D ---
//     [Header("3D Visual Feedback")]
//     public GameObject ghostSpherePrefab;
//     public Transform[] jointTargets;

//     // Variável de controle (em Graus)
//     [HideInInspector]
//     public float[] jointAngles = new float[7];

//     [HideInInspector]
//     public MyInputActions inputActions;

//     [Header("Joint Control Settings")]
//     public float rotationSpeed = 1.0f;
//     [HideInInspector]
//     public int activeJointIndex = 0;

//     [Header("System References")]
//     public ROSJointStateSubscriber jointSubscriber;
//     public UR5JointSimulator simulator;

//     [Header("Visual Feedback")]
//     public TextMeshPro jointNameDisplay;

//     // Limites (Graus e Radianos)
//     private readonly float jointMin_UR5 = -180f;
//     private readonly float jointMax_UR5 = 180f;
//     private readonly float jointMin_Gripper = 0.0f;
//     private readonly float jointMax_Gripper = 0.8f;

//     // REMOVIDO: private float currentTriggerValue = 0f;
    
//     private readonly Vector3 GHOST_HOME_POSITION = new Vector3(0f, -10f, 0f);

//     // Pose de Reset (Graus)
//     private readonly float[] RESET_POSE_DEGREES = { 0f, -90f, 0f, -90f, 0f, 90f, 0.0f };


//     void Awake()
//     {
//         inputActions = new MyInputActions();

//         // 1. Inscrição para seleção de junta
//         inputActions.XRControl.nextJointButton.performed += context => ChangeJointSelection(1);
//         inputActions.XRControl.prevJointButton.performed += context => ChangeJointSelection(-1);
        
//         // 2. Inscrição para Reset Suave
//         inputActions.XRControl.resetPoseButton.performed += context => TriggerSmoothResetROS(); 
        
//         // REMOVIDO: Toda a lógica de inscrição da triggerAction foi removida.

//         // Inicializa display de junta
//         if (jointNameDisplay != null && simulator != null)
//         {
//             string initialJointName = simulator.jointNames[activeJointIndex];
//             jointNameDisplay.text = $"Junta Ativa:\n{initialJointName}";
//         }
//     }


//     void Start()
//     {
//         // 🔹 Configuração da Sincronização Inicial
//         if (jointSubscriber != null)
//         {
//             jointSubscriber.OnJointAnglesUpdated += SyncInitialPose;
//             jointSubscriber.TriggerInitialSync();
//         }
//         else
//         {
//             Debug.LogError("[VR Teleop] jointSubscriber não atribuído no Inspetor!");
//         }

//         // NOVO: Verifica e posiciona a esfera fantasma na junta inicial (0)
//         UpdateGhostSpherePosition();
//     }

//     void OnEnable() => inputActions.XRControl.Enable();

//     void OnDisable()
//     {
//         inputActions.XRControl.Disable();
        
//         // Limpeza de lambdas (mantida a simplicidade)
//         inputActions.XRControl.nextJointButton.performed -= context => ChangeJointSelection(1);
//         inputActions.XRControl.prevJointButton.performed -= context => ChangeJointSelection(-1);
//     }
    
//     /// <summary>
//     /// Sincroniza o array de controle VR com a pose real do robô e se desinscreve.
//     /// </summary>
//     private void SyncInitialPose(float[] angles)
//     {
//         if (angles == null || angles.Length == 0) return;

//         angles.CopyTo(jointAngles, 0);
        
//         // Desinscreve-se IMEDIATAMENTE para evitar conflitos de correção contínua.
//         if (jointSubscriber != null)
//         {
//             jointSubscriber.OnJointAnglesUpdated -= SyncInitialPose;
//             Debug.Log("[VR Teleop] Sincronização CONCLUÍDA. Controle VR assumido.");
//         }
//     }

//     // --- Lógica de Reset Suave ---
//     void TriggerSmoothResetROS()
//     {
//         if (simulator != null)
//         {
//             // Ação 1: Mover a esfera para a posição de "repouso" e desativá-la
//             if (ghostSpherePrefab != null)
//             {
//                 ghostSpherePrefab.transform.position = GHOST_HOME_POSITION;
//                 ghostSpherePrefab.SetActive(false);
//             }

//             // Ação 2: O Unity envia a meta de reset (em graus)
//             simulator.SendResetGoalToROS(RESET_POSE_DEGREES);
            
//             // Ação 3: Desabilita o input VR para o Controlador (Polinômio Quíntuplo) assumir
//             inputActions.XRControl.Disable();
            
//             Debug.Log("[VR Teleop] Comando de Reset Suave enviado. Input desabilitado.");
//         }
//     }
    
//     // --- Funções de Manipulação ---

//     void Update() => HandleJointMovement();

//     void ChangeJointSelection(int direction)
//     {
//         activeJointIndex += direction;
//         int totalJoints = jointAngles.Length;

//         // 1. Lógica de loop (Clamping)
//         if (activeJointIndex >= totalJoints) activeJointIndex = 0;
//         else if (activeJointIndex < 0) activeJointIndex = totalJoints - 1;

//         // 2. Ação Centralizada: Move, ativa e verifica a esfera
//         UpdateGhostSpherePosition(); 
        
//         // 3. Feedback: Atualiza o TextMeshPro e Debug.Log (usando o índice final)
//         if (simulator != null)
//         {
//             string currentJointName = simulator.jointNames[activeJointIndex];
//             if (jointNameDisplay != null)
//                 jointNameDisplay.text = $"Junta Ativa:\n{currentJointName}";
//             Debug.Log($"[VR Teleop] Junta ativa: {currentJointName}");
//         }
//     }

//     private (float min, float max) GetActiveJointLimits()
//     {
//         return activeJointIndex == 6 ? (jointMin_Gripper, jointMax_Gripper)
//                                      : (jointMin_UR5, jointMax_UR5);
//     }

//     /// <summary>
//     /// Otimizado: Move a junta usando APENAS o Joystick (taxa direta).
//     /// </summary>
//     void HandleJointMovement()
//     {
//         // Se o input estiver desabilitado (durante o reset), não faça nada.
//         if (!inputActions.XRControl.enabled) return;
        
//         (float minLimit, float maxLimit) = GetActiveJointLimits();
        
//         // 1. Leitura dos Inputs
//         // O eixo X do joystick controla a direção e o valor de entrada.
//         float movementAxis = inputActions.XRControl.joystickAction.ReadValue<Vector2>().x;

//         // 2. Condição para Movimento (Usamos um pequeno limite para evitar drift)
//         if (Mathf.Abs(movementAxis) > 0.1f)
//         {
//             // Calcula o movimento: delta = (Direção/Potência do Joystick) * (Velocidade Base) * (Tempo)
//             float delta = movementAxis * rotationSpeed * Time.deltaTime;
            
//             // Aplica o Clamp e atualiza a posição da junta
//             jointAngles[activeJointIndex] = Mathf.Clamp(
//                 jointAngles[activeJointIndex] + delta,
//                 minLimit, maxLimit
//             );
//         }
//     }

//     public void ReActivateInputAndVisuals()
//     {
//         // 1. Reabilita o Input VR
//         inputActions.XRControl.Enable();

//         // 2. AÇÃO CENTRALIZADA: Ativa e posiciona a esfera na junta ativa (Junta 0, após o reset).
//         UpdateGhostSpherePosition();
        
//         Debug.Log("[Controle VR] Input e visuais reativados.");
//     }
    
//     private void UpdateGhostSpherePosition()
//     {
//         if (ghostSpherePrefab != null && jointTargets.Length > activeJointIndex)
//         {
//             Transform targetTransform = jointTargets[activeJointIndex];
            
//             // Ativa a esfera 
//             ghostSpherePrefab.SetActive(true); 

//             // Move a esfera para a posição e orientação do link da junta
//             ghostSpherePrefab.transform.position = targetTransform.position;
//             ghostSpherePrefab.transform.rotation = targetTransform.rotation;
//         }
//         else if (ghostSpherePrefab != null)
//         {
//             // Se o índice for inválido ou o array não estiver configurado, desativa.
//             ghostSpherePrefab.SetActive(false);
//             ghostSpherePrefab.transform.position = GHOST_HOME_POSITION; 
//         }
//     }
// }



