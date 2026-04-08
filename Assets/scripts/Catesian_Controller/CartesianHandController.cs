/*
Arquivo 1: CartesianHandController.cs (O Núcleo e Máquina de Estados)
Este será o arquivo principal. Ele contém todas as declarações de variáveis que aparecem no Inspector e o ciclo de vida do Unity (Start, Update, e a Coroutine de Recuperação).
*/

using UnityEngine;
using UnityEngine.XR.Hands;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Trajectory; 
using RosMessageTypes.BuiltinInterfaces; 
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public partial class CartesianHandController : MonoBehaviour
{
    [Header("Variáveis para o Data Logger")]
    public Vector3 rawHandPosition; 
    public Vector3 safeTargetPosition; 

    [Header("Módulo de Segurança")]
    public SafetyWorkspace safetyWorkspace;
    public float tcpZOffset = 0.232f;

    [Header("Máquina de Estados e Segurança")]
    public bool isTeleoperating = false; 
    public float escapeRadiusSingularity = 0.001f; 
    public float escapeRadiusSafe = 0.0f; 

    private float activeEscapeRadius; 
    private Vector3 engagementPosition;
    private bool hasEscapedDeadband = false;

    [Header("Sincronização Inicial (Digital Twin)")]
    public Transform robotEndEffector; 
    private bool isSynced = false; 

    [Header("Feedback Dinâmico no GhostCube (Anéis)")]
    public Transform ringPitchX; 
    public Transform ringYawY;   
    public Transform ringRollZ;  

    [Header("Configurações do Gizmo Proxy (Bolinhas do Painel)")]
    public Transform gizmoPitchX; 
    public Transform gizmoYawY;   
    public Transform gizmoRollZ;  
    public float gizmoGrabRadius = 0.08f; 
    public float gizmoSensitivity = 300f; 

    private Transform currentGrabbedSphere; 
    private Vector3 initialSpherePos;       

    [Header("Configurações do Filtro 1 Euro")]
    public float minCutoff = 1.0f; 
    public float beta = 0.05f;     
    private OneEuroFilterVector3 positionFilter;
    private OneEuroFilterQuaternion rotationFilter;

    [Header("Referências Core")]
    public Transform robotBase; 
    public Transform ghostCube; 
    private MeshRenderer cubeRenderer;

    [Header("Cores de Feedback")]
    public Color colorDefault = Color.green;
    public Color colorPosition = Color.blue;
    public Color colorRotation = Color.yellow;

    [Header("Configurações de Áudio")]
    public AudioClip clickSound; 
    private AudioSource audioSource;

    [Header("Configurações do ROS")]
    private ROSConnection ros;
    public string poseTopic = "unity/target_pose";
    public string gripperTopic = "/ur5/gripper_controller/command";
    public string gripperJointName = "finger_joint"; 
    public float gripperOpenValue = 0.0f;
    public float gripperClosedValue = 0.8f; 

    [Header("Limiares de Gesto")]
    public float pinchThreshold = 0.02f;
    public float grabThreshold = 0.035f;   
    public float releaseThreshold = 0.06f; 
    public float gestureCooldown = 0.4f; 
    
    // Estados Ocultos
    private float lastGestureTime = 0f; 
    private bool lastGripperState = false; 
    private bool readyToToggleGripper = true; 
    private bool isRightPinching = false;
    private Vector3 initialRightHandPos, initialGhostPos;
    private bool isLeftPinchingGizmo = false;
    private Vector3 initialLeftHandPos;
    private Quaternion initialGhostRot;

    private enum AxisLock { None, X, Y, Z }
    private AxisLock lockedAxis = AxisLock.None;
    private XRHandSubsystem handSubsystem;
    private Vector3 targetPosition;
    private Quaternion targetRotation;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(poseTopic);
        ros.RegisterPublisher<JointTrajectoryMsg>(gripperTopic); 
        
        cubeRenderer = ghostCube.GetComponent<MeshRenderer>();
        audioSource = GetComponent<AudioSource>();
        SetCubeColor(colorDefault);

        var handSubsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetInstances(handSubsystems);
        if (handSubsystems.Count > 0) handSubsystem = handSubsystems[0];

        positionFilter = new OneEuroFilterVector3(minCutoff, beta);
        rotationFilter = new OneEuroFilterQuaternion(minCutoff, beta);

        targetPosition = ghostCube.position;
        targetRotation = ghostCube.rotation;

        SetAllGizmosVisibility(true, true); 
    }

    public void StartSystem()
    {
        if (robotBase == null || robotEndEffector == null)
        {
            Debug.LogWarning("[Sistema] Erro: Configure RobotBase e RobotEndEffector no Inspector.");
            return;
        }

        float reachDistance = Vector3.Distance(robotBase.position, robotEndEffector.position);
        bool startInSingularity = reachDistance > 0.75f;

        Debug.Log($"[Sistema] Start Pressionado! Alcance físico: {reachDistance:F2}m. Singularidade detectada: {startInSingularity}");
        ResumeManualControl(startInSingularity);
    }

    public void ForceSyncGhostCube()
    {
        if (robotEndEffector != null)
        {
            Physics.SyncTransforms();

            ghostCube.position = robotEndEffector.position;
            ghostCube.rotation = robotEndEffector.rotation;

            targetPosition = ghostCube.position;
            targetRotation = ghostCube.rotation;

            positionFilter = new OneEuroFilterVector3(minCutoff, beta);
            rotationFilter = new OneEuroFilterQuaternion(minCutoff, beta);

            isSynced = true; 
        }
    }

    void Update()
    {
        if (!isSynced) return; 

        if (!isTeleoperating)
        {
            if (robotEndEffector != null)
            {
                ghostCube.position = robotEndEffector.position;
                ghostCube.rotation = robotEndEffector.rotation;
                targetPosition = ghostCube.position;
                targetRotation = ghostCube.rotation;
            }
            return; 
        }

        if (handSubsystem != null)
        {
            XRHand rightHand = handSubsystem.rightHand;
            XRHand leftHand = handSubsystem.leftHand;

            if (rightHand.isTracked) HandleRightHandPosition(rightHand);
            if (leftHand.isTracked) HandleLeftHandOrientationAndGripper(leftHand);
        }

        float dt = Time.deltaTime;
        ghostCube.position = positionFilter.Filter(targetPosition, dt);
        ghostCube.rotation = rotationFilter.Filter(targetRotation, dt);

        if (!hasEscapedDeadband)
        {
            float dist = Vector3.Distance(ghostCube.position, engagementPosition);
            
            if (dist > activeEscapeRadius)
            {
                hasEscapedDeadband = true;
                PublishToROS();
                Debug.Log("[Controle Cartesiano] Intenção detectada. Movimento contínuo ativado!");
            }
        }
        else
        {
            PublishToROS();
        }
    }

    public void PauseManualControl()
    {
        isTeleoperating = false;
        Debug.Log("[Controle Cartesiano] Robô em viagem autônoma. Gizmo colado no tool0.");
    }

    public void ResumeManualControl(bool isSingularity)
    {
        ForceSyncGhostCube(); 
        engagementPosition = ghostCube.position; 
        
        hasEscapedDeadband = false; 
        isTeleoperating = true;

        if (isSingularity)
        {
            activeEscapeRadius = escapeRadiusSingularity; 
            Debug.Log("[Controle Cartesiano] Singularidade: Faça a pinça e puxe 2cm.");
        }
        else
        {
            activeEscapeRadius = escapeRadiusSafe; 
            Debug.Log("[Controle Cartesiano] Pose Segura: Faça a pinça para liberar.");
        }
    }
}












// using UnityEngine;
// using UnityEngine.XR.Hands;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;
// using RosMessageTypes.Trajectory; 
// using RosMessageTypes.BuiltinInterfaces; 
// using System.Collections.Generic;

// [RequireComponent(typeof(AudioSource))]
// public class CartesianHandController : MonoBehaviour
// {

//     [Header("Variáveis para o Data Logger")]
//     public Vector3 rawHandPosition; // Posição pura do rastreamento
//     public Vector3 safeTargetPosition; // Posição após a Cerca Virtual

//     [Header("Módulo de Segurança")]
//     [Tooltip("Arraste o objeto Safety_Manager aqui")]
//     public SafetyWorkspace safetyWorkspace;
    
//     [Tooltip("Distância do pulso (tool0) até a ponta dos dedos em metros. Ex: Robotiq 85 = ~0.15m. Para o garra Robotiq 2F-140 = ~0.232m")]
//     public float tcpZOffset = 0.232f;

//     [Header("Máquina de Estados e Segurança")]
//     [Tooltip("Controla se o Unity está enviando comandos para o ROS")]
//     public bool isTeleoperating = false; 
    
//     [Tooltip("Raio da bolha para poses perigosas (ex: 0.02 = 2cm)")]
//     public float escapeRadiusSingularity = 0.001f; 
    
//     [Tooltip("Raio da bolha para poses seguras (ex: 0.001 = 1mm)")]
//     public float escapeRadiusSafe = 0.0f; 

//     private float activeEscapeRadius; 
//     private Vector3 engagementPosition;
//     private bool hasEscapedDeadband = false;

//     [Header("Sincronização Inicial (Digital Twin)")]
//     [Tooltip("Arraste o 'tool0' ou a ponta física do robô virtual aqui")]
//     public Transform robotEndEffector; 
//     private bool isSynced = false; 

//     [Header("Melhoria #2: Feedback Dinâmico no GhostCube (Anéis)")]
//     public Transform ringPitchX; 
//     public Transform ringYawY;   
//     public Transform ringRollZ;  

//     [Header("Configurações do Gizmo Proxy (Bolinhas do Painel)")]
//     public Transform gizmoPitchX; 
//     public Transform gizmoYawY;   
//     public Transform gizmoRollZ;  
    
//     public float gizmoGrabRadius = 0.08f; 
//     public float gizmoSensitivity = 300f; 

//     private Transform currentGrabbedSphere; 
//     private Vector3 initialSpherePos;       

//     [Header("Configurações do Filtro 1 Euro")]
//     public float minCutoff = 1.0f; 
//     public float beta = 0.05f;     
//     private OneEuroFilterVector3 positionFilter;
//     private OneEuroFilterQuaternion rotationFilter;

//     [Header("Referências Core")]
//     public Transform robotBase; 
//     public Transform ghostCube; 
//     private MeshRenderer cubeRenderer;

//     [Header("Cores de Feedback")]
//     public Color colorDefault = Color.green;
//     public Color colorPosition = Color.blue;
//     public Color colorRotation = Color.yellow;

//     [Header("Configurações de Áudio")]
//     public AudioClip clickSound; 
//     private AudioSource audioSource;

//     [Header("Configurações do ROS")]
//     private ROSConnection ros;
//     public string poseTopic = "unity/target_pose";
//     public string gripperTopic = "/ur5/gripper_controller/command";
//     public string gripperJointName = "robotiq_85_left_knuckle_joint"; 
//     public float gripperOpenValue = 0.0f;
//     public float gripperClosedValue = 0.8f; 

//     [Header("Limiares de Gesto")]
//     public float pinchThreshold = 0.02f;
//     public float grabThreshold = 0.035f;   
//     public float releaseThreshold = 0.06f; 
//     public float gestureCooldown = 0.4f; 
    
//     // Estados
//     private float lastGestureTime = 0f; 
//     private bool lastGripperState = false; 
//     private bool readyToToggleGripper = true; 
//     private bool isRightPinching = false;
//     private Vector3 initialRightHandPos, initialGhostPos;
//     private bool isLeftPinchingGizmo = false;
//     private Vector3 initialLeftHandPos;
//     private Quaternion initialGhostRot;

//     private enum AxisLock { None, X, Y, Z }
//     private AxisLock lockedAxis = AxisLock.None;

//     private XRHandSubsystem handSubsystem;
    
//     private Vector3 targetPosition;
//     private Quaternion targetRotation;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PoseStampedMsg>(poseTopic);
//         ros.RegisterPublisher<JointTrajectoryMsg>(gripperTopic); 
        
//         cubeRenderer = ghostCube.GetComponent<MeshRenderer>();
//         audioSource = GetComponent<AudioSource>();
//         SetCubeColor(colorDefault);

//         var handSubsystems = new List<XRHandSubsystem>();
//         SubsystemManager.GetInstances(handSubsystems);
//         if (handSubsystems.Count > 0) handSubsystem = handSubsystems[0];

//         positionFilter = new OneEuroFilterVector3(minCutoff, beta);
//         rotationFilter = new OneEuroFilterQuaternion(minCutoff, beta);

//         targetPosition = ghostCube.position;
//         targetRotation = ghostCube.rotation;

//         SetAllGizmosVisibility(true, true); 
//     }

//     public void StartSystem()
//     {
//         if (robotBase == null || robotEndEffector == null)
//         {
//             Debug.LogWarning("[Sistema] Erro: Configure RobotBase e RobotEndEffector no Inspector.");
//             return;
//         }

//         float reachDistance = Vector3.Distance(robotBase.position, robotEndEffector.position);
//         bool startInSingularity = reachDistance > 0.75f;

//         Debug.Log($"[Sistema] Start Pressionado! Alcance físico: {reachDistance:F2}m. Singularidade detectada: {startInSingularity}");

//         ResumeManualControl(startInSingularity);
//     }

//     public void ForceSyncGhostCube()
//     {
//         if (robotEndEffector != null)
//         {
//             Physics.SyncTransforms();

//             ghostCube.position = robotEndEffector.position;
//             ghostCube.rotation = robotEndEffector.rotation;

//             targetPosition = ghostCube.position;
//             targetRotation = ghostCube.rotation;

//             positionFilter = new OneEuroFilterVector3(minCutoff, beta);
//             rotationFilter = new OneEuroFilterQuaternion(minCutoff, beta);

//             isSynced = true; 
//         }
//     }

//     void Update()
//     {
//         if (!isSynced) return; 

//         if (!isTeleoperating)
//         {
//             if (robotEndEffector != null)
//             {
//                 ghostCube.position = robotEndEffector.position;
//                 ghostCube.rotation = robotEndEffector.rotation;
//                 targetPosition = ghostCube.position;
//                 targetRotation = ghostCube.rotation;
//             }
//             return; 
//         }

//         if (handSubsystem != null)
//         {
//             XRHand rightHand = handSubsystem.rightHand;
//             XRHand leftHand = handSubsystem.leftHand;

//             if (rightHand.isTracked) HandleRightHandPosition(rightHand);
//             if (leftHand.isTracked) HandleLeftHandOrientationAndGripper(leftHand);
//         }

//         float dt = Time.deltaTime;
//         ghostCube.position = positionFilter.Filter(targetPosition, dt);
//         ghostCube.rotation = rotationFilter.Filter(targetRotation, dt);

//         if (!hasEscapedDeadband)
//         {
//             float dist = Vector3.Distance(ghostCube.position, engagementPosition);
            
//             if (dist > activeEscapeRadius)
//             {
//                 hasEscapedDeadband = true;
//                 PublishToROS();
//                 Debug.Log("[Controle Cartesiano] Intenção detectada. Movimento contínuo ativado!");
//             }
//         }
//         else
//         {
//             PublishToROS();
//         }
//     }

//     void HandleRightHandPosition(XRHand rightHand)
//     {
//         var thumb = rightHand.GetJoint(XRHandJointID.ThumbTip);
//         var index = rightHand.GetJoint(XRHandJointID.IndexTip);

//         if (thumb.TryGetPose(out Pose tP) && index.TryGetPose(out Pose iP))
//         {
//             if (Vector3.Distance(tP.position, iP.position) < pinchThreshold)
//             {
//                 if (!isRightPinching) 
//                 {
//                     isRightPinching = true;
//                     initialRightHandPos = iP.position;
//                     initialGhostPos = ghostCube.position;
//                     if (!isLeftPinchingGizmo) SetCubeColor(colorPosition);
//                 }

//                 Vector3 localOffset = iP.position - initialRightHandPos;
//                 Vector3 worldOffset = transform.TransformDirection(localOffset);
//                 Vector3 rawWristPosition = initialGhostPos + worldOffset;
//                 rawHandPosition = rawWristPosition; // <-- SALVA A POSIÇÃO BRUTA PARA O LOGGER

//                 // --- APLICA A CAMADA DE SEGURANÇA MODULAR COM TCP OFFSET ---
//                 if (safetyWorkspace != null)
//                 {
//                     // CORREÇÃO CRÍTICA: No Unity, o eixo Z do ROS (que aponta para fora do flange do UR5) 
//                     // mapeia para o eixo Y (Vector3.up). O Vector3.forward estava jogando o TCP para o lado!
//                     Vector3 forwardDirection = targetRotation * Vector3.up; 
                    
//                     Vector3 rawTipPosition = rawWristPosition + (forwardDirection * tcpZOffset);
//                     Vector3 safeTipPosition = safetyWorkspace.ApplyLimits(rawTipPosition);
                    
//                     targetPosition = safeTipPosition - (forwardDirection * tcpZOffset);
//                     safeTargetPosition = targetPosition; // <-- SALVA A POSIÇÃO SEGURA PARA O LOGGER
//                 }
//                 else
//                 {
//                     targetPosition = rawWristPosition; 
//                 }
//             }
//             else 
//             {
//                 if (isRightPinching)
//                 {
//                     isRightPinching = false;
//                     if (!isLeftPinchingGizmo) SetCubeColor(colorDefault);
//                 }
//             }
//         }
//     }

//     void HandleLeftHandOrientationAndGripper(XRHand leftHand)
//     {
//         var thumb = leftHand.GetJoint(XRHandJointID.ThumbTip);
//         var index = leftHand.GetJoint(XRHandJointID.IndexTip);
//         var middle = leftHand.GetJoint(XRHandJointID.MiddleTip);

//         if (thumb.TryGetPose(out Pose tP) && index.TryGetPose(out Pose iP) && middle.TryGetPose(out Pose mP))
//         {
//             float distThumbIndex = Vector3.Distance(tP.position, iP.position);
//             float distThumbMiddle = Vector3.Distance(tP.position, mP.position);
            
//             Vector3 pinchCenterLocal = (tP.position + iP.position) / 2f;
//             Vector3 pinchCenterWorld = transform.TransformPoint(pinchCenterLocal);

//             bool isPinch = (distThumbIndex < pinchThreshold) && (distThumbMiddle > grabThreshold);
//             bool isFist = (distThumbIndex < grabThreshold) && (distThumbMiddle < grabThreshold);
//             bool isHandOpen = (distThumbIndex > releaseThreshold) && (distThumbMiddle > releaseThreshold); 

//             if (isPinch)
//             {
//                 if (!isLeftPinchingGizmo) 
//                 {
//                     float distToX = Vector3.Distance(pinchCenterWorld, gizmoPitchX.position);
//                     float distToY = Vector3.Distance(pinchCenterWorld, gizmoYawY.position);
//                     float distToZ = Vector3.Distance(pinchCenterWorld, gizmoRollZ.position);

//                     float shortestDistance = gizmoGrabRadius; 
//                     AxisLock bestAxis = AxisLock.None;
//                     Transform bestSphere = null;

//                     if (distToX < shortestDistance) { shortestDistance = distToX; bestAxis = AxisLock.X; bestSphere = gizmoPitchX; }
//                     if (distToY < shortestDistance) { shortestDistance = distToY; bestAxis = AxisLock.Y; bestSphere = gizmoYawY; }
//                     if (distToZ < shortestDistance) { shortestDistance = distToZ; bestAxis = AxisLock.Z; bestSphere = gizmoRollZ; }

//                     if (bestAxis != AxisLock.None)
//                     {
//                         lockedAxis = bestAxis;
//                         currentGrabbedSphere = bestSphere;
                        
//                         isLeftPinchingGizmo = true;
//                         initialLeftHandPos = pinchCenterLocal; 
//                         initialGhostRot = ghostCube.rotation;
//                         initialSpherePos = currentGrabbedSphere.localPosition; 
                        
//                         SetCubeColor(colorRotation);
//                         PlayClick();

//                         SetAllGizmosVisibility(false, false); 
//                         SetSingleGizmoVisibility(lockedAxis, true); 
//                     }
//                 }
                
//                 if (isLeftPinchingGizmo)
//                 {
//                     currentGrabbedSphere.position = pinchCenterWorld;

//                     Vector3 localOffset = pinchCenterLocal - initialLeftHandPos;
//                     float angle = 0f;

//                     if (lockedAxis == AxisLock.X) angle = localOffset.y * gizmoSensitivity;
//                     else if (lockedAxis == AxisLock.Y) angle = localOffset.y * gizmoSensitivity; 
//                     else if (lockedAxis == AxisLock.Z) angle = localOffset.y * gizmoSensitivity;

//                     Vector3 axisVector = Vector3.zero;
//                     if (lockedAxis == AxisLock.X) axisVector = Vector3.right;
//                     else if (lockedAxis == AxisLock.Y) axisVector = Vector3.up;
//                     else if (lockedAxis == AxisLock.Z) axisVector = Vector3.forward;

//                     targetRotation = initialGhostRot * Quaternion.AngleAxis(angle, axisVector);
//                 }
//             }
//             else 
//             {
//                 if (isLeftPinchingGizmo)
//                 {
//                     isLeftPinchingGizmo = false;
                    
//                     SetAllGizmosVisibility(true, true); 
//                     lockedAxis = AxisLock.None;
                    
//                     if (currentGrabbedSphere != null)
//                     {
//                         currentGrabbedSphere.localPosition = initialSpherePos; 
//                         currentGrabbedSphere = null;
//                     }
                    
//                     SetCubeColor(isRightPinching ? colorPosition : colorDefault);
//                 }
//             }

//             if (!isLeftPinchingGizmo)
//             {
//                 if (isHandOpen)
//                 {
//                     readyToToggleGripper = true;
//                 }

//                 if (isFist && readyToToggleGripper && (Time.time - lastGestureTime > gestureCooldown))
//                 {
//                     lastGripperState = !lastGripperState;
                    
//                     float targetValue = lastGripperState ? gripperClosedValue : gripperOpenValue;
//                     SendGripperCommand(targetValue);
//                     PlayClick(); 

//                     lastGestureTime = Time.time;
//                     readyToToggleGripper = false; 
//                 }
//             }
//         }
//     }

//     void PlayClick() { if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound); }

//     void SendGripperCommand(float value)
//     {
//         JointTrajectoryMsg msg = new JointTrajectoryMsg();
//         msg.joint_names = new string[] { gripperJointName };
//         JointTrajectoryPointMsg point = new JointTrajectoryPointMsg();
//         point.positions = new double[] { value };
//         point.time_from_start = new DurationMsg(0, 500000000); 
//         msg.points = new JointTrajectoryPointMsg[] { point };
//         ros.Publish(gripperTopic, msg);
//     }

//     void PublishToROS()
//     {
//         if (robotBase == null) return; 
//         PoseStampedMsg msg = new PoseStampedMsg();
//         msg.header.frame_id = "base_link";

//         Vector3 localPos = robotBase.InverseTransformPoint(ghostCube.position);
//         Quaternion localRot = Quaternion.Inverse(robotBase.rotation) * ghostCube.rotation;

//         msg.pose.position.x = localPos.z; 
//         msg.pose.position.y = -localPos.x;
//         msg.pose.position.z = localPos.y;

//         msg.pose.orientation.x = -localRot.z;
//         msg.pose.orientation.y = localRot.x; 
//         msg.pose.orientation.z = -localRot.y;
//         msg.pose.orientation.w = localRot.w;

//         ros.Publish(poseTopic, msg);
//     }

//     void SetCubeColor(Color color) { if (cubeRenderer != null) cubeRenderer.material.color = color; }

//     void SetAllGizmosVisibility(bool showSpheresInPanel, bool showRingsOnRobot)
//     {
//         if (gizmoPitchX != null) gizmoPitchX.gameObject.SetActive(showSpheresInPanel);
//         if (gizmoYawY != null) gizmoYawY.gameObject.SetActive(showSpheresInPanel);
//         if (gizmoRollZ != null) gizmoRollZ.gameObject.SetActive(showSpheresInPanel);

//         if (ringPitchX != null) ringPitchX.gameObject.SetActive(showRingsOnRobot);
//         if (ringYawY != null) ringYawY.gameObject.SetActive(showRingsOnRobot);
//         if (ringRollZ != null) ringRollZ.gameObject.SetActive(showRingsOnRobot);
//     }

//     void SetSingleGizmoVisibility(AxisLock axis, bool isVisible)
//     {
//         if (axis == AxisLock.X)
//         {
//             if (gizmoPitchX != null) gizmoPitchX.gameObject.SetActive(isVisible);
//             if (ringPitchX != null) ringPitchX.gameObject.SetActive(isVisible);
//         }
//         else if (axis == AxisLock.Y)
//         {
//             if (gizmoYawY != null) gizmoYawY.gameObject.SetActive(isVisible);
//             if (ringYawY != null) ringYawY.gameObject.SetActive(isVisible);
//         }
//         else if (axis == AxisLock.Z)
//         {
//             if (gizmoRollZ != null) gizmoRollZ.gameObject.SetActive(isVisible);
//             if (ringRollZ != null) ringRollZ.gameObject.SetActive(isVisible);
//         }
//     }

//     public void PauseManualControl()
//     {
//         isTeleoperating = false;
//         Debug.Log("[Controle Cartesiano] Robô em viagem autônoma. Gizmo colado no tool0.");
//     }

//     public void ResumeManualControl(bool isSingularity)
//     {
//         ForceSyncGhostCube(); 
//         engagementPosition = ghostCube.position; 
        
//         hasEscapedDeadband = false; 
//         isTeleoperating = true;

//         if (isSingularity)
//         {
//             activeEscapeRadius = escapeRadiusSingularity; 
//             Debug.Log("[Controle Cartesiano] Singularidade: Faça a pinça e puxe 2cm.");
//         }
//         else
//         {
//             activeEscapeRadius = escapeRadiusSafe; 
//             Debug.Log("[Controle Cartesiano] Pose Segura: Faça a pinça para liberar.");
//         }
//     }
// }












