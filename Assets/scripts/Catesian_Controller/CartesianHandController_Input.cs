/*
Arquivo 2: CartesianHandController_Input.cs (Gestos e Cinemática)
Este arquivo isola toda a inteligência complexa de leitura das mãos do XR Origin, os cálculos espaciais da pinça e as restrições de giro dos anéis virtuais.
*/

using UnityEngine;
using UnityEngine.XR.Hands;

// A palavra 'partial' junta este arquivo com o principal automaticamente.
public partial class CartesianHandController
{
    // --- Variáveis da Válvula Unidirecional (Mão Direita) ---
    private Vector3 lastAcceptedWristPosition; 
    private bool isOutsideFenceOnGrab = false; 

    // --- Variáveis de Estado das Esferas (Mão Esquerda) ---
    private bool areGizmosVisible = true;
    private float lastGizmoToggleTime = 0f;
    private bool wasLeftMiddlePinching = false;

    void HandleRightHandPosition(XRHand rightHand)
    {
        var thumb = rightHand.GetJoint(XRHandJointID.ThumbTip);
        var index = rightHand.GetJoint(XRHandJointID.IndexTip);

        if (thumb.TryGetPose(out Pose tP) && index.TryGetPose(out Pose iP))
        {
            if (Vector3.Distance(tP.position, iP.position) < pinchThreshold)
            {
                if (!isRightPinching) 
                {
                    isRightPinching = true;
                    initialRightHandPos = iP.position;
                    initialGhostPos = ghostCube.position;

                    Vector3 forwardDir = targetRotation * Vector3.up; 
                    Vector3 rawTipPos = initialGhostPos + (forwardDir * tcpZOffset);
                    
                    Vector3 safeWristInit = safetyWorkspace.ApplyLimits(initialGhostPos);
                    Vector3 safeTipInit = safetyWorkspace.ApplyLimits(rawTipPos);

                    if (Vector3.Distance(initialGhostPos, safeWristInit) > 0.001f || 
                        Vector3.Distance(rawTipPos, safeTipInit) > 0.001f)
                    {
                        isOutsideFenceOnGrab = true; 
                        lastAcceptedWristPosition = initialGhostPos;
                    }
                    else
                    {
                        isOutsideFenceOnGrab = false; 
                    }

                    if (!isLeftPinchingGizmo) SetCubeColor(colorPosition);
                }

                Vector3 localOffset = iP.position - initialRightHandPos;
                Vector3 worldOffset = transform.TransformDirection(localOffset);
                Vector3 rawWristPosition = initialGhostPos + worldOffset;
                rawHandPosition = rawWristPosition; 

                if (safetyWorkspace != null)
                {
                    Vector3 forwardDirection = targetRotation * Vector3.up; 
                    
                    Vector3 fullySafeWrist = rawWristPosition;
                    fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);
                    
                    Vector3 currentTip = fullySafeWrist + (forwardDirection * tcpZOffset);
                    Vector3 safeTip = safetyWorkspace.ApplyLimits(currentTip);
                    fullySafeWrist = safeTip - (forwardDirection * tcpZOffset);
                    
                    fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);

                    if (isOutsideFenceOnGrab) 
                    {
                        float distanceToSafeZone = Vector3.Distance(rawWristPosition, fullySafeWrist);

                        if (distanceToSafeZone > 0.001f) 
                        {
                            Vector3 movementVector = rawWristPosition - lastAcceptedWristPosition;
                            Vector3 directionToSafety = (fullySafeWrist - rawWristPosition).normalized;

                            if (Vector3.Dot(movementVector, directionToSafety) > 0)
                            {
                                lastAcceptedWristPosition = rawWristPosition; 
                            }
                            else
                            {
                                rawWristPosition = lastAcceptedWristPosition; 
                            }
                        }
                        else 
                        {
                            isOutsideFenceOnGrab = false;
                            rawWristPosition = fullySafeWrist; 
                        }
                        
                        targetPosition = rawWristPosition;
                    }
                    else 
                    {
                        targetPosition = fullySafeWrist; 
                    }

                    safeTargetPosition = targetPosition; 
                }
                else
                {
                    targetPosition = rawWristPosition; 
                }
            }
            else 
            {
                if (isRightPinching)
                {
                    isRightPinching = false;
                    if (!isLeftPinchingGizmo) SetCubeColor(colorDefault);
                }
            }
        }
    }

    void HandleLeftHandOrientationAndGripper(XRHand leftHand)
    {
        var thumb = leftHand.GetJoint(XRHandJointID.ThumbTip);
        var index = leftHand.GetJoint(XRHandJointID.IndexTip);
        var middle = leftHand.GetJoint(XRHandJointID.MiddleTip);
        var ring = leftHand.GetJoint(XRHandJointID.RingTip);     
        var pinky = leftHand.GetJoint(XRHandJointID.LittleTip);  

        if (thumb.TryGetPose(out Pose tP) && index.TryGetPose(out Pose iP) && 
            middle.TryGetPose(out Pose mP) && ring.TryGetPose(out Pose rP) && pinky.TryGetPose(out Pose lP))
        {
            float distThumbIndex = Vector3.Distance(tP.position, iP.position);
            float distThumbMiddle = Vector3.Distance(tP.position, mP.position);
            float distThumbRing = Vector3.Distance(tP.position, rP.position);
            float distThumbPinky = Vector3.Distance(tP.position, lP.position);
            
            Vector3 pinchCenterLocal = (tP.position + iP.position) / 2f;
            Vector3 pinchCenterWorld = transform.TransformPoint(pinchCenterLocal);

            // Pinça Primária (Esferas)
            bool isPinch = (distThumbIndex < pinchThreshold) && (distThumbRing > grabThreshold);
            
            // Pinça Secundária (Ligar/Desligar Esferas)
            bool isMiddlePinch = (distThumbMiddle < pinchThreshold) && (distThumbIndex > grabThreshold);
            
            // Punho Verdadeiro (Garra)
            bool isFist = (distThumbIndex < grabThreshold) && 
                          (distThumbMiddle < grabThreshold) && 
                          (distThumbRing < grabThreshold + 0.015f) && 
                          (distThumbPinky < grabThreshold + 0.02f);
            
            // Mão Aberta
            bool isHandOpen = (distThumbIndex > releaseThreshold) && 
                              (distThumbMiddle > releaseThreshold) && 
                              (distThumbRing > releaseThreshold); 

            // --- LÓGICA DE VISIBILIDADE DAS ESFERAS ---
            if (isMiddlePinch && !wasLeftMiddlePinching && (Time.time - lastGizmoToggleTime > gestureCooldown))
            {
                areGizmosVisible = !areGizmosVisible;
                
                if (!isLeftPinchingGizmo)
                {
                    SetAllGizmosVisibility(areGizmosVisible, areGizmosVisible);
                }

                lastGizmoToggleTime = Time.time;
                wasLeftMiddlePinching = true;
                PlayClick();
            }
            else if (!isMiddlePinch)
            {
                wasLeftMiddlePinching = false;
            }

            // --- LÓGICA DE MANIPULAR AS ESFERAS ---
            if (isPinch && areGizmosVisible)
            {
                if (!isLeftPinchingGizmo) 
                {
                    float distToX = Vector3.Distance(pinchCenterWorld, gizmoPitchX.position);
                    float distToY = Vector3.Distance(pinchCenterWorld, gizmoYawY.position);
                    float distToZ = Vector3.Distance(pinchCenterWorld, gizmoRollZ.position);

                    float shortestDistance = gizmoGrabRadius; 
                    AxisLock bestAxis = AxisLock.None;
                    Transform bestSphere = null;

                    if (distToX < shortestDistance) { shortestDistance = distToX; bestAxis = AxisLock.X; bestSphere = gizmoPitchX; }
                    if (distToY < shortestDistance) { shortestDistance = distToY; bestAxis = AxisLock.Y; bestSphere = gizmoYawY; }
                    if (distToZ < shortestDistance) { shortestDistance = distToZ; bestAxis = AxisLock.Z; bestSphere = gizmoRollZ; }

                    if (bestAxis != AxisLock.None)
                    {
                        lockedAxis = bestAxis;
                        currentGrabbedSphere = bestSphere;
                        
                        isLeftPinchingGizmo = true;
                        initialLeftHandPos = pinchCenterLocal; 
                        initialGhostRot = ghostCube.rotation;
                        initialSpherePos = currentGrabbedSphere.localPosition; 
                        
                        SetCubeColor(colorRotation);
                        PlayClick();

                        SetAllGizmosVisibility(false, false); 
                        SetSingleGizmoVisibility(lockedAxis, true); 
                    }
                }
                
                if (isLeftPinchingGizmo)
                {
                    currentGrabbedSphere.position = pinchCenterWorld;

                    Vector3 localOffset = pinchCenterLocal - initialLeftHandPos;
                    float angle = 0f;

                    if (lockedAxis == AxisLock.X) angle = localOffset.y * gizmoSensitivity;
                    else if (lockedAxis == AxisLock.Y) angle = localOffset.y * gizmoSensitivity; 
                    else if (lockedAxis == AxisLock.Z) angle = localOffset.y * gizmoSensitivity;

                    Vector3 axisVector = Vector3.zero;
                    if (lockedAxis == AxisLock.X) axisVector = Vector3.right;
                    else if (lockedAxis == AxisLock.Y) axisVector = Vector3.up;
                    else if (lockedAxis == AxisLock.Z) axisVector = Vector3.forward;

                    targetRotation = initialGhostRot * Quaternion.AngleAxis(angle, axisVector);
                }
            }
            else 
            {
                if (isLeftPinchingGizmo)
                {
                    isLeftPinchingGizmo = false;
                    SetAllGizmosVisibility(areGizmosVisible, areGizmosVisible); 
                    lockedAxis = AxisLock.None;
                    
                    if (currentGrabbedSphere != null)
                    {
                        currentGrabbedSphere.localPosition = initialSpherePos; 
                        currentGrabbedSphere = null;
                    }
                    
                    SetCubeColor(isRightPinching ? colorPosition : colorDefault);
                }
            }

            // --- LÓGICA DA GARRA DO ROBÔ ---
            if (!isLeftPinchingGizmo)
            {
                if (isHandOpen)
                {
                    readyToToggleGripper = true;
                }

                // Garra agora exige apenas o "Punho Verdadeiro" (dedos recolhidos) e estar pronta
                if (isFist && readyToToggleGripper && (Time.time - lastGestureTime > gestureCooldown))
                {
                    lastGripperState = !lastGripperState;
                    
                    float targetValue = lastGripperState ? gripperClosedValue : gripperOpenValue;
                    SendGripperCommand(targetValue);
                    PlayClick(); 

                    lastGestureTime = Time.time;
                    readyToToggleGripper = false; 
                }
            }
        }
    }
}













// using UnityEngine;
// using UnityEngine.XR.Hands;

// // A palavra 'partial' junta este arquivo com o principal automaticamente.
// public partial class CartesianHandController
// {
//     // --- Variáveis da Válvula Unidirecional (Mão Direita) ---
//     private Vector3 lastAcceptedWristPosition; 
//     private bool isOutsideFenceOnGrab = false; 

//     // --- Novas Variáveis de Estado das Esferas (Mão Esquerda) ---
//     private bool areGizmosVisible = true;
//     private float lastGizmoToggleTime = 0f;
//     private bool wasLeftMiddlePinching = false;

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

//                     Vector3 forwardDir = targetRotation * Vector3.up; 
//                     Vector3 rawTipPos = initialGhostPos + (forwardDir * tcpZOffset);
                    
//                     Vector3 safeWristInit = safetyWorkspace.ApplyLimits(initialGhostPos);
//                     Vector3 safeTipInit = safetyWorkspace.ApplyLimits(rawTipPos);

//                     if (Vector3.Distance(initialGhostPos, safeWristInit) > 0.001f || 
//                         Vector3.Distance(rawTipPos, safeTipInit) > 0.001f)
//                     {
//                         isOutsideFenceOnGrab = true; 
//                         lastAcceptedWristPosition = initialGhostPos;
//                     }
//                     else
//                     {
//                         isOutsideFenceOnGrab = false; 
//                     }

//                     if (!isLeftPinchingGizmo) SetCubeColor(colorPosition);
//                 }

//                 Vector3 localOffset = iP.position - initialRightHandPos;
//                 Vector3 worldOffset = transform.TransformDirection(localOffset);
//                 Vector3 rawWristPosition = initialGhostPos + worldOffset;
//                 rawHandPosition = rawWristPosition; 

//                 if (safetyWorkspace != null)
//                 {
//                     Vector3 forwardDirection = targetRotation * Vector3.up; 
                    
//                     Vector3 fullySafeWrist = rawWristPosition;
//                     fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);
                    
//                     Vector3 currentTip = fullySafeWrist + (forwardDirection * tcpZOffset);
//                     Vector3 safeTip = safetyWorkspace.ApplyLimits(currentTip);
//                     fullySafeWrist = safeTip - (forwardDirection * tcpZOffset);
                    
//                     fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);

//                     if (isOutsideFenceOnGrab) 
//                     {
//                         float distanceToSafeZone = Vector3.Distance(rawWristPosition, fullySafeWrist);

//                         if (distanceToSafeZone > 0.001f) 
//                         {
//                             Vector3 movementVector = rawWristPosition - lastAcceptedWristPosition;
//                             Vector3 directionToSafety = (fullySafeWrist - rawWristPosition).normalized;

//                             if (Vector3.Dot(movementVector, directionToSafety) > 0)
//                             {
//                                 lastAcceptedWristPosition = rawWristPosition; 
//                             }
//                             else
//                             {
//                                 rawWristPosition = lastAcceptedWristPosition; 
//                             }
//                         }
//                         else 
//                         {
//                             isOutsideFenceOnGrab = false;
//                             rawWristPosition = fullySafeWrist; 
//                         }
                        
//                         targetPosition = rawWristPosition;
//                     }
//                     else 
//                     {
//                         targetPosition = fullySafeWrist; 
//                     }

//                     safeTargetPosition = targetPosition; 
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

//             // --- DEFINIÇÃO ESTRITA DOS GESTOS (Mutuamente Exclusivos) ---
//             // Pinça Primária: Apenas Indicador (Para pegar esferas)
//             bool isPinch = (distThumbIndex < pinchThreshold) && (distThumbMiddle > grabThreshold);
            
//             // Pinça Secundária: Apenas Dedo Médio (Para ligar/desligar esferas)
//             bool isMiddlePinch = (distThumbMiddle < pinchThreshold) && (distThumbIndex > grabThreshold);
            
//             // Mão Fechada: Ambos os dedos próximos (Para a garra do robô)
//             bool isFist = (distThumbIndex < grabThreshold) && (distThumbMiddle < grabThreshold);
            
//             // Mão Aberta: Ambos os dedos abertos
//             bool isHandOpen = (distThumbIndex > releaseThreshold) && (distThumbMiddle > releaseThreshold); 


//             // --- NOVA LÓGICA: Ligar/Desligar Esferas ---
//             if (isMiddlePinch && !wasLeftMiddlePinching && (Time.time - lastGizmoToggleTime > gestureCooldown))
//             {
//                 areGizmosVisible = !areGizmosVisible;
                
//                 // Só aplica a visibilidade visualmente se não estiver segurando a esfera agora
//                 if (!isLeftPinchingGizmo)
//                 {
//                     SetAllGizmosVisibility(areGizmosVisible, areGizmosVisible);
//                 }

//                 lastGizmoToggleTime = Time.time;
//                 wasLeftMiddlePinching = true;
//                 PlayClick(); // Toca o mesmo barulhinho de clique para confirmar!
//                 Debug.Log($"[Controle Cartesiano] Esferas de comando visíveis: {areGizmosVisible}");
//             }
//             else if (!isMiddlePinch)
//             {
//                 wasLeftMiddlePinching = false;
//             }


//             // --- LÓGICA EXISTENTE: Pegar e Girar as Esferas ---
//             // Modificação: Só permite engatar a pinça se as esferas estiverem ligadas (areGizmosVisible)
//             if (isPinch && areGizmosVisible)
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
                    
//                     // Modificação: Em vez de ligar todas (true, true) ao soltar, respeita o estado do Toggle
//                     SetAllGizmosVisibility(areGizmosVisible, areGizmosVisible); 
//                     lockedAxis = AxisLock.None;
                    
//                     if (currentGrabbedSphere != null)
//                     {
//                         currentGrabbedSphere.localPosition = initialSpherePos; 
//                         currentGrabbedSphere = null;
//                     }
                    
//                     SetCubeColor(isRightPinching ? colorPosition : colorDefault);
//                 }
//             }


//             // --- LÓGICA EXISTENTE: Garra do Robô ---
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
// }














// using UnityEngine;
// using UnityEngine.XR.Hands;

// // A palavra 'partial' junta este arquivo com o principal automaticamente.
// public partial class CartesianHandController
// {
//     // A Válvula agora monitora o Pulso (que é imune a falsos positivos por rotação da mão)
//     private Vector3 lastAcceptedWristPosition; 
//     private bool isOutsideFenceOnGrab = false; 

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

//                     Vector3 forwardDir = targetRotation * Vector3.up; 
//                     Vector3 rawTipPos = initialGhostPos + (forwardDir * tcpZOffset);
                    
//                     // TESTE DE DUPLA FRONTEIRA (Início do Grab)
//                     Vector3 safeWristInit = safetyWorkspace.ApplyLimits(initialGhostPos);
//                     Vector3 safeTipInit = safetyWorkspace.ApplyLimits(rawTipPos);

//                     // Se o PULSO ou a PONTA estiverem fora, liga a Válvula
//                     if (Vector3.Distance(initialGhostPos, safeWristInit) > 0.001f || 
//                         Vector3.Distance(rawTipPos, safeTipInit) > 0.001f)
//                     {
//                         isOutsideFenceOnGrab = true; 
//                         lastAcceptedWristPosition = initialGhostPos;
//                     }
//                     else
//                     {
//                         isOutsideFenceOnGrab = false; 
//                     }

//                     if (!isLeftPinchingGizmo) SetCubeColor(colorPosition);
//                 }

//                 // Calcula o offset direcional baseado no XR Origin (Rastreamento do Pulso)
//                 Vector3 localOffset = iP.position - initialRightHandPos;
//                 Vector3 worldOffset = transform.TransformDirection(localOffset);
//                 Vector3 rawWristPosition = initialGhostPos + worldOffset;
//                 rawHandPosition = rawWristPosition; 

//                 // --- APLICA A SEGURANÇA COM LIMITADOR EM CASCATA ---
//                 if (safetyWorkspace != null)
//                 {
//                     Vector3 forwardDirection = targetRotation * Vector3.up; 
                    
//                     // 1. O ALGORITMO DE CASCATA (Protege os dois extremos da ferramenta)
//                     Vector3 fullySafeWrist = rawWristPosition;
                    
//                     // Passo A: Força o pulso para dentro
//                     fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);
                    
//                     // Passo B: Força a ponta para dentro (pode arrastar o pulso)
//                     Vector3 currentTip = fullySafeWrist + (forwardDirection * tcpZOffset);
//                     Vector3 safeTip = safetyWorkspace.ApplyLimits(currentTip);
//                     fullySafeWrist = safeTip - (forwardDirection * tcpZOffset);
                    
//                     // Passo C: Garantia final do pulso
//                     fullySafeWrist = safetyWorkspace.ApplyLimits(fullySafeWrist);
//                     // -------------------------------------------------------------

//                     if (isOutsideFenceOnGrab) 
//                     {
//                         // ESTADO 1: Robô fora da Cerca (Atuando como Válvula)
//                         float distanceToSafeZone = Vector3.Distance(rawWristPosition, fullySafeWrist);

//                         if (distanceToSafeZone > 0.001f) 
//                         {
//                             Vector3 movementVector = rawWristPosition - lastAcceptedWristPosition;
//                             Vector3 directionToSafety = (fullySafeWrist - rawWristPosition).normalized;

//                             // PRODUTO ESCALAR: Só avança se estiver indo rumo ao ponto seguro absoluto
//                             if (Vector3.Dot(movementVector, directionToSafety) > 0)
//                             {
//                                 lastAcceptedWristPosition = rawWristPosition; 
//                             }
//                             else
//                             {
//                                 rawWristPosition = lastAcceptedWristPosition; 
//                             }
//                         }
//                         else 
//                         {
//                             // VITÓRIA! Tanto a ponta quanto o pulso entraram 100% na Cerca.
//                             isOutsideFenceOnGrab = false;
//                             rawWristPosition = fullySafeWrist; 
//                         }
                        
//                         targetPosition = rawWristPosition;
//                     }
//                     else 
//                     {
//                         // ESTADO 2: O ROBÔ ESTÁ DENTRO (Parede de Concreto Dupla)
//                         targetPosition = fullySafeWrist; 
//                     }

//                     safeTargetPosition = targetPosition; 
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
// }


















// using UnityEngine;
// using UnityEngine.XR.Hands;

// // A palavra 'partial' junta este arquivo com o principal automaticamente.
// public partial class CartesianHandController
// {
//     // Variáveis de estado para a Válvula Unidirecional (Permissão Assimétrica)
//     private Vector3 lastAcceptedTipPosition; 
//     private bool isOutsideFenceOnGrab = false; // Trava inteligente de estado

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

//                     // INICIALIZAÇÃO INTELIGENTE
//                     Vector3 forwardDir = targetRotation * Vector3.up; 
//                     Vector3 rawTipPos = initialGhostPos + (forwardDir * tcpZOffset);
//                     Vector3 safeTipPos = safetyWorkspace.ApplyLimits(rawTipPos);

//                     // Verifica ONDE o engate aconteceu
//                     if (Vector3.Distance(rawTipPos, safeTipPos) > 0.001f)
//                     {
//                         isOutsideFenceOnGrab = true; // Começou fora! Liga a Válvula.
//                         lastAcceptedTipPosition = rawTipPos;
//                     }
//                     else
//                     {
//                         isOutsideFenceOnGrab = false; // Começou dentro! Parede de Concreto.
//                     }

//                     if (!isLeftPinchingGizmo) SetCubeColor(colorPosition);
//                 }

//                 // Calcula o offset direcional baseado no XR Origin
//                 Vector3 localOffset = iP.position - initialRightHandPos;
//                 Vector3 worldOffset = transform.TransformDirection(localOffset);
//                 Vector3 rawWristPosition = initialGhostPos + worldOffset;
//                 rawHandPosition = rawWristPosition; 

//                 // --- APLICA A SEGURANÇA COM MÁQUINA DE ESTADOS ---
//                 if (safetyWorkspace != null)
//                 {
//                     Vector3 forwardDirection = targetRotation * Vector3.up; 
//                     Vector3 rawTipPosition = rawWristPosition + (forwardDirection * tcpZOffset);
//                     Vector3 safeTipPosition = safetyWorkspace.ApplyLimits(rawTipPosition);

//                     if (isOutsideFenceOnGrab) 
//                     {
//                         // ESTADO 1: O ROBÔ ESTÁ FORA (Tentando voltar via Válvula)
//                         float distanceToSafeZone = Vector3.Distance(rawTipPosition, safeTipPosition);

//                         if (distanceToSafeZone > 0.001f) 
//                         {
//                             Vector3 movementVector = rawTipPosition - lastAcceptedTipPosition;
//                             Vector3 directionToSafety = (safeTipPosition - rawTipPosition).normalized;

//                             // PRODUTO ESCALAR: Só aceita movimento rumo ao centro
//                             if (Vector3.Dot(movementVector, directionToSafety) > 0)
//                             {
//                                 lastAcceptedTipPosition = rawTipPosition; 
//                             }
//                             else
//                             {
//                                 rawTipPosition = lastAcceptedTipPosition; 
//                             }
//                         }
//                         else 
//                         {
//                             // VITÓRIA! O robô cruzou a fronteira para dentro.
//                             // Desativa a válvula permanentemente para este Grab.
//                             isOutsideFenceOnGrab = false;
//                             rawTipPosition = safeTipPosition; 
//                         }
//                     }
//                     else 
//                     {
//                         // ESTADO 2: O ROBÔ ESTÁ DENTRO (Parede de Concreto Absoluta)
//                         // Ignora vetores e aplica a restrição matemática bruta.
//                         rawTipPosition = safeTipPosition; 
//                     }

//                     // Recalcula o pulso com base na ponta validada
//                     targetPosition = rawTipPosition - (forwardDirection * tcpZOffset);
//                     safeTargetPosition = targetPosition; 
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
// }