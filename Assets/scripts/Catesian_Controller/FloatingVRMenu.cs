using UnityEngine;
using UnityEngine.XR.Hands;
using System.Collections.Generic;

public class FloatingVRMenu : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Arraste a Main Camera do XR Origin")]
    public Transform mainCamera;
    
    [Tooltip("CUIDADO: Este objeto DEVE ser um filho do Canvas. Nunca arraste o próprio Canvas aqui!")]
    public GameObject menuContent;

    [Header("Configurações de Seguimento")]
    [Tooltip("Distância para frente")]
    public float followDistance = 0.55f; 
    
    [Tooltip("Deslocamento Lateral: Positivo = Direita, Negativo = Esquerda")]
    public float lateralOffset = 0.3f; 
    
    [Tooltip("Altura: Positivo = Cima, Negativo = Baixo")]
    public float heightOffset = -0.1f; 
    
    public float smoothSpeed = 5f; 
    public float deadzoneAngle = 15f; 

    [Header("Gesto do Pulso (Wrist Turn)")]
    [Tooltip("Ângulo máximo (em graus) entre o olhar e a palma para ativar o menu")]
    public float palmFacingThreshold = 45f; 
    public float gestureCooldown = 1.0f; // Aumentado para evitar duplo clique ao virar a mão rápido

    [Header("Áudio (Opcional)")]
    public AudioSource audioSource;
    public AudioClip toggleClip;

    private XRHandSubsystem handSubsystem;
    private bool isVisible = true;
    private float lastToggleTime = 0f;
    private bool wasLookingAtPalm = false; 

    private Vector3 targetPosition;
    private Quaternion targetRotation;

    void Start()
    {
        var handSubsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetInstances(handSubsystems);
        if (handSubsystems.Count > 0) handSubsystem = handSubsystems[0];

        if (menuContent != null) menuContent.SetActive(isVisible);

        if (mainCamera != null)
        {
            targetPosition = CalculateTargetPosition();
            targetRotation = CalculateTargetRotation();
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }
    }

    void Update()
    {
        HandleWristGesture();

        if (isVisible && mainCamera != null)
        {
            UpdateLazyFollow();
        }
    }

    private void HandleWristGesture()
    {
        if (handSubsystem == null) return;

        // Verifica estritamente a MÃO DIREITA para o Canvas Menu
        bool isLookingAtPalm = CheckPalmFacingCamera(handSubsystem.rightHand);

        // Lógica de "Trigger" (Só aciona 1 vez ao olhar para a palma)
        if (isLookingAtPalm && !wasLookingAtPalm && Time.time - lastToggleTime > gestureCooldown)
        {
            ToggleMenu();
            lastToggleTime = Time.time;
            wasLookingAtPalm = true;
        }
        else if (!isLookingAtPalm)
        {
            wasLookingAtPalm = false; // Reseta quando você vira a mão de volta
        }
    }

    // A NOVA MÁGICA: Detecção da Palma da Mão
    private bool CheckPalmFacingCamera(XRHand hand)
    {
        if (!hand.isTracked || mainCamera == null) return false;

        // Pegamos a articulação do Pulso (Wrist)
        var wrist = hand.GetJoint(XRHandJointID.Wrist);
        if (!wrist.TryGetPose(out Pose wristPose)) return false;

        // No Unity XR Hands, para a mão DIREITA, o vetor que sai da PALMA
        // aponta para o Eixo Y Positivo (Vector3.up) em relação ao pulso.
        Vector3 palmDirection = wristPose.rotation * Vector3.up; 
        
        // Vetor que sai do seu olho (câmera) em direção à sua mão
        Vector3 directionToFace = (mainCamera.position - wristPose.position).normalized;

        // O ângulo entre a sua palma e os seus olhos
        float angle = Vector3.Angle(palmDirection, directionToFace);

        // Se o ângulo for menor que o limite (ex: 45 graus), você está olhando para a palma
        return angle < palmFacingThreshold;
    }

    private void ToggleMenu()
    {
        isVisible = !isVisible;
        Debug.Log($"[VR Menu] Palma Direita detectada! Menu visível: {isVisible}");

        if (audioSource != null && toggleClip != null)
        {
            audioSource.PlayOneShot(toggleClip);
        }
        
        if (menuContent != null)
        {
            menuContent.SetActive(isVisible);
            
            if (isVisible)
            {
                // Reposiciona o menu na sua frente ao aparecer
                targetPosition = CalculateTargetPosition();
                targetRotation = CalculateTargetRotation();
                transform.position = targetPosition;
                transform.rotation = targetRotation;
            }
        }
        else
        {
            Debug.LogWarning("[VR Menu] AVISO: Você esqueceu de arrastar o MenuContent no Inspector!");
        }
    }

    private void UpdateLazyFollow()
    {
        Vector3 idealPosition = CalculateTargetPosition();
        Quaternion idealRotation = CalculateTargetRotation();

        float angleDifference = Quaternion.Angle(transform.rotation, idealRotation);
        if (angleDifference > deadzoneAngle || Vector3.Distance(transform.position, idealPosition) > 0.25f)
        {
            targetPosition = idealPosition;
            targetRotation = idealRotation;
        }

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smoothSpeed);
    }

    private Vector3 CalculateTargetPosition()
    {
        Vector3 forwardFlat = new Vector3(mainCamera.forward.x, 0, mainCamera.forward.z).normalized;
        Vector3 rightFlat = new Vector3(mainCamera.right.x, 0, mainCamera.right.z).normalized;
        
        return mainCamera.position + (forwardFlat * followDistance) + (rightFlat * lateralOffset) + new Vector3(0, heightOffset, 0);
    }

    private Quaternion CalculateTargetRotation()
    {
        Vector3 directionToCamera = transform.position - mainCamera.position;
        directionToCamera.y = 0; 
        if (directionToCamera == Vector3.zero) return transform.rotation;
        return Quaternion.LookRotation(directionToCamera);
    }
}











// using UnityEngine;
// using UnityEngine.XR.Hands;
// using System.Collections.Generic;

// public class FloatingVRMenu : MonoBehaviour
// {
//     [Header("Referências")]
//     [Tooltip("Arraste a Main Camera do XR Origin")]
//     public Transform mainCamera;
    
//     [Tooltip("CUIDADO: Este objeto DEVE ser um filho do Canvas. Nunca arraste o próprio Canvas aqui!")]
//     public GameObject menuContent;

//     [Header("Configurações de Seguimento")]
//     [Tooltip("Distância para frente")]
//     public float followDistance = 0.55f; 
    
//     [Tooltip("Deslocamento Lateral: Positivo = Direita, Negativo = Esquerda")]
//     public float lateralOffset = 0.3f; 
    
//     [Tooltip("Altura: Positivo = Cima, Negativo = Baixo")]
//     public float heightOffset = -0.1f; 
    
//     public float smoothSpeed = 5f; 
//     public float deadzoneAngle = 15f; 

//     [Header("Gesto de Alternância (Show/Hide)")]
//     public float pinchThreshold = 0.025f; 
//     public float gestureCooldown = 0.5f; 

//     [Header("Áudio (Opcional)")]
//     public AudioSource audioSource;
//     public AudioClip toggleClip;

//     private XRHandSubsystem handSubsystem;
//     private bool isVisible = true;
//     private float lastToggleTime = 0f;
//     private bool wasPinching = false; 

//     private Vector3 targetPosition;
//     private Quaternion targetRotation;

//     void Start()
//     {
//         var handSubsystems = new List<XRHandSubsystem>();
//         SubsystemManager.GetInstances(handSubsystems);
//         if (handSubsystems.Count > 0) handSubsystem = handSubsystems[0];

//         if (menuContent != null) menuContent.SetActive(isVisible);

//         if (mainCamera != null)
//         {
//             targetPosition = CalculateTargetPosition();
//             targetRotation = CalculateTargetRotation();
//             transform.position = targetPosition;
//             transform.rotation = targetRotation;
//         }
//     }

//     void Update()
//     {
//         HandleGestureLogic();

//         if (isVisible && mainCamera != null)
//         {
//             UpdateLazyFollow();
//         }
//     }

//     private void HandleGestureLogic()
//     {
//         if (handSubsystem == null) return;

//         // MUDANÇA AQUI: Agora verificamos estritamente a mão DIREITA
//         bool isRightHandPinching = CheckPinch(handSubsystem.rightHand);

//         // Lógica de "Trigger"
//         if (isRightHandPinching && !wasPinching && Time.time - lastToggleTime > gestureCooldown)
//         {
//             ToggleMenu();
//             lastToggleTime = Time.time;
//             wasPinching = true;
//         }
//         else if (!isRightHandPinching)
//         {
//             wasPinching = false;
//         }
//     }

//     private bool CheckPinch(XRHand hand)
//     {
//         if (!hand.isTracked) return false;

//         var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
//         var middle = hand.GetJoint(XRHandJointID.MiddleTip);

//         if (thumb.TryGetPose(out Pose tP) && middle.TryGetPose(out Pose mP))
//         {
//             return Vector3.Distance(tP.position, mP.position) < pinchThreshold;
//         }
//         return false;
//     }

//     private void ToggleMenu()
//     {
//         isVisible = !isVisible;
//         Debug.Log($"[VR Menu] Gesto detectado na Mão Direita! Menu visível: {isVisible}");

//         if (audioSource != null && toggleClip != null)
//         {
//             audioSource.PlayOneShot(toggleClip);
//         }
        
//         if (menuContent != null)
//         {
//             menuContent.SetActive(isVisible);
            
//             if (isVisible)
//             {
//                 targetPosition = CalculateTargetPosition();
//                 targetRotation = CalculateTargetRotation();
//                 transform.position = targetPosition;
//                 transform.rotation = targetRotation;
//             }
//         }
//         else
//         {
//             Debug.LogWarning("[VR Menu] AVISO: Você esqueceu de arrastar o MenuContent no Inspector!");
//         }
//     }

//     private void UpdateLazyFollow()
//     {
//         Vector3 idealPosition = CalculateTargetPosition();
//         Quaternion idealRotation = CalculateTargetRotation();

//         float angleDifference = Quaternion.Angle(transform.rotation, idealRotation);
//         if (angleDifference > deadzoneAngle || Vector3.Distance(transform.position, idealPosition) > 0.25f)
//         {
//             targetPosition = idealPosition;
//             targetRotation = idealRotation;
//         }

//         transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
//         transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smoothSpeed);
//     }

//     private Vector3 CalculateTargetPosition()
//     {
//         Vector3 forwardFlat = new Vector3(mainCamera.forward.x, 0, mainCamera.forward.z).normalized;
//         Vector3 rightFlat = new Vector3(mainCamera.right.x, 0, mainCamera.right.z).normalized;
        
//         return mainCamera.position + (forwardFlat * followDistance) + (rightFlat * lateralOffset) + new Vector3(0, heightOffset, 0);
//     }

//     private Quaternion CalculateTargetRotation()
//     {
//         Vector3 directionToCamera = transform.position - mainCamera.position;
//         directionToCamera.y = 0; 
//         if (directionToCamera == Vector3.zero) return transform.rotation;
//         return Quaternion.LookRotation(directionToCamera);
//     }
// }