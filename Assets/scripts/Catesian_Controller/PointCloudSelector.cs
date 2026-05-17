// Este script é responsável por permitir que o operador aponte para um ponto na nuvem de pontos usando a mão direita (com um laser visual) e, ao confirmar a seleção com a mão esquerda, envie as coordenadas do ponto selecionado para o ROS. Ele também inclui feedback visual imediato (uma esfera amarela) para indicar onde o sistema detectou o clique, e uma garra fantasma que aparecerá no local da seleção para simular a posição de preensão calculada pela GGCNN. O script é projetado para ser usado em conjunto com o GGCNN_Subscriber, que receberá as poses de preensão do ROS e atualizará a garra fantasma em tempo real.

using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System.Collections.Generic;
using System.Collections; // Necessário para a Coroutine

[RequireComponent(typeof(LineRenderer))]
public class PointCloudSelector : MonoBehaviour
{
    [Header("Comunicação ROS")]
    public TargetPointPublisher rosPublisher;

    [Header("Configurações de Interação")]
    public Transform rayOrigin;
    public float maxSelectionTolerance = 0.05f;
    public float laserLength = 1.5f;

    [Header("Referência da Nuvem de Pontos")]
    public MeshFilter pointCloudMeshFilter;

    [Header("Feedback Visual")]
    public Transform targetIndicator; // A esfera amarela
    [Tooltip("Tempo em segundos que a esfera de confirmação fica visível")]
    public float indicatorDisplayTime = 1.5f; // NOVA VARIÁVEL AQUI

    private LineRenderer visualRay;
    private XRHandSubsystem handSubsystem;
    private Coroutine hideIndicatorCoroutine; // Referência para cancelar a coroutine se clicar de novo rápido

    void Awake()
    {
        visualRay = GetComponent<LineRenderer>();
        visualRay.startWidth = 0.002f;
        visualRay.endWidth = 0.002f;
        visualRay.positionCount = 2;
        visualRay.enabled = false;
        
        // Garante que a esfera comece invisível
        if (targetIndicator != null)
        {
            targetIndicator.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        var subsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetInstances(subsystems);
        if (subsystems.Count > 0) handSubsystem = subsystems[0];
    }

    void Update()
    {
        bool isPointing = false;
        if (handSubsystem != null && handSubsystem.running)
        {
            var rightHand = handSubsystem.rightHand;
            if (rightHand.isTracked) isPointing = CheckPointingPose(rightHand);
        }

        if (isPointing && rayOrigin != null)
        {
            visualRay.enabled = true;
            visualRay.SetPosition(0, rayOrigin.position);
            visualRay.SetPosition(1, rayOrigin.position + rayOrigin.forward * laserLength);
        }
        else
        {
            visualRay.enabled = false;
        }
    }

    private bool CheckPointingPose(XRHand hand)
    {
        var wrist = hand.GetJoint(XRHandJointID.Wrist);
        var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);
        var ringTip = hand.GetJoint(XRHandJointID.RingTip);
        var littleTip = hand.GetJoint(XRHandJointID.LittleTip);

        if (!wrist.TryGetPose(out Pose wristPose) || 
            !indexTip.TryGetPose(out Pose indexPose) ||
            !middleTip.TryGetPose(out Pose middlePose) || 
            !ringTip.TryGetPose(out Pose ringPose) ||
            !littleTip.TryGetPose(out Pose littlePose)) 
        {
            return false;
        }

        float indexDist = Vector3.Distance(wristPose.position, indexPose.position);
        float middleDist = Vector3.Distance(wristPose.position, middlePose.position);
        float ringDist = Vector3.Distance(wristPose.position, ringPose.position);
        float littleDist = Vector3.Distance(wristPose.position, littlePose.position);
        
        return indexDist > 0.12f && middleDist < 0.10f && ringDist < 0.10f && littleDist < 0.10f;
    }

    public void ExecuteSelection()
    {
        if (!visualRay.enabled) return;

        if (pointCloudMeshFilter == null || pointCloudMeshFilter.sharedMesh == null) return;

        Vector3[] vertices = pointCloudMeshFilter.sharedMesh.vertices;
        Transform meshTransform = pointCloudMeshFilter.transform;

        Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
        Ray localRay = new Ray(meshTransform.InverseTransformPoint(globalRay.origin), meshTransform.InverseTransformDirection(globalRay.direction));

        Vector3 closestLocalPoint = Vector3.zero;
        float minDistanceToRay = float.MaxValue;
        bool foundValidPoint = false;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = vertices[i];
            if (point.sqrMagnitude < 0.0001f) continue;
            Vector3 pointToOrigin = point - localRay.origin;
            if (Vector3.Dot(localRay.direction, pointToOrigin) < 0) continue;
            float distanceToRay = Vector3.Cross(localRay.direction, pointToOrigin).magnitude;

            if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
            {
                minDistanceToRay = distanceToRay;
                closestLocalPoint = point;
                foundValidPoint = true;
            }
        }

        if (foundValidPoint)
        {
            Vector3 selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);

            // 1. POSICIONA E LIGA A ESFERA INDICADORA
            if (targetIndicator != null)
            {
                targetIndicator.position = selectedTargetPoint;
                targetIndicator.gameObject.SetActive(true);
                
                // Se já houver uma contagem rolando, cancela ela e começa uma nova
                if (hideIndicatorCoroutine != null)
                {
                    StopCoroutine(hideIndicatorCoroutine);
                }
                
                // Inicia o cronômetro para esconder a esfera
                hideIndicatorCoroutine = StartCoroutine(HideIndicatorAfterDelay());
            }

            // 2. ENVIA PARA O ROS
            if (rosPublisher != null)
            {
                rosPublisher.PublishTargetPoint(selectedTargetPoint);
            }
        }
        else
        {
            Debug.Log("[GGCNN] Seleção cancelada/limpa.");
            if (targetIndicator != null)
            {
                targetIndicator.gameObject.SetActive(false); // Esconde a esfera amarela
            }
        }
    }

    // ========================================================
    // COROUTINE: Esconde a esfera de feedback após X segundos
    // ========================================================
    private IEnumerator HideIndicatorAfterDelay()
    {
        yield return new WaitForSeconds(indicatorDisplayTime);
        
        if (targetIndicator != null)
        {
            targetIndicator.gameObject.SetActive(false);
        }
    }
}















// // Este script é responsável por permitir que o operador aponte para um ponto na nuvem de pontos usando a mão direita (com um laser visual) e, ao confirmar a seleção com a mão esquerda, envie as coordenadas do ponto selecionado para o ROS. Ele também inclui feedback visual imediato (uma esfera amarela) para indicar onde o sistema detectou o clique, e uma garra fantasma que aparecerá no local da seleção para simular a posição de preensão calculada pela GGCNN. O script é projetado para ser usado em conjunto com o GGCNN_Subscriber, que receberá as poses de preensão do ROS e atualizará a garra fantasma em tempo real.

// using UnityEngine;
// using UnityEngine.XR.Hands;
// using UnityEngine.XR.Management;
// using System.Collections.Generic;

// [RequireComponent(typeof(LineRenderer))]
// public class PointCloudSelector : MonoBehaviour
// {
//     [Header("Comunicação ROS")]
//     public TargetPointPublisher rosPublisher;

//     [Header("Configurações de Interação")]
//     public Transform rayOrigin;
//     public float maxSelectionTolerance = 0.05f;
//     public float laserLength = 1.5f;

//     [Header("Referência da Nuvem de Pontos")]
//     public MeshFilter pointCloudMeshFilter;

//     [Header("Feedback Visual")]
//     public Transform targetIndicator; // A esfera amarela

//     private LineRenderer visualRay;
//     private XRHandSubsystem handSubsystem;

//     void Awake()
//     {
//         visualRay = GetComponent<LineRenderer>();
//         visualRay.startWidth = 0.002f;
//         visualRay.endWidth = 0.002f;
//         visualRay.positionCount = 2;
//         visualRay.enabled = false;
//     }

//     void Start()
//     {
//         var subsystems = new List<XRHandSubsystem>();
//         SubsystemManager.GetInstances(subsystems);
//         if (subsystems.Count > 0) handSubsystem = subsystems[0];
//     }

//     void Update()
//     {
//         bool isPointing = false;
//         if (handSubsystem != null && handSubsystem.running)
//         {
//             var rightHand = handSubsystem.rightHand;
//             if (rightHand.isTracked) isPointing = CheckPointingPose(rightHand);
//         }

//         if (isPointing && rayOrigin != null)
//         {
//             visualRay.enabled = true;
//             visualRay.SetPosition(0, rayOrigin.position);
//             visualRay.SetPosition(1, rayOrigin.position + rayOrigin.forward * laserLength);
//         }
//         else
//         {
//             visualRay.enabled = false;
//         }
//     }

//     private bool CheckPointingPose(XRHand hand)
//     {
//         var wrist = hand.GetJoint(XRHandJointID.Wrist);
//         var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
//         var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);
//         var ringTip = hand.GetJoint(XRHandJointID.RingTip);
//         var littleTip = hand.GetJoint(XRHandJointID.LittleTip);

//         if (!wrist.TryGetPose(out Pose wristPose) || 
//             !indexTip.TryGetPose(out Pose indexPose) ||
//             !middleTip.TryGetPose(out Pose middlePose) || 
//             !ringTip.TryGetPose(out Pose ringPose) ||
//             !littleTip.TryGetPose(out Pose littlePose)) 
//         {
//             return false;
//         }

//         float indexDist = Vector3.Distance(wristPose.position, indexPose.position);
//         float middleDist = Vector3.Distance(wristPose.position, middlePose.position);
//         float ringDist = Vector3.Distance(wristPose.position, ringPose.position);
//         float littleDist = Vector3.Distance(wristPose.position, littlePose.position);
        
//         return indexDist > 0.12f && middleDist < 0.10f && ringDist < 0.10f && littleDist < 0.10f;
//     }

//     public void ExecuteSelection()
//     {
//         if (!visualRay.enabled) return;

//         if (pointCloudMeshFilter == null || pointCloudMeshFilter.sharedMesh == null) return;

//         Vector3[] vertices = pointCloudMeshFilter.sharedMesh.vertices;
//         Transform meshTransform = pointCloudMeshFilter.transform;

//         Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
//         Ray localRay = new Ray(meshTransform.InverseTransformPoint(globalRay.origin), meshTransform.InverseTransformDirection(globalRay.direction));

//         Vector3 closestLocalPoint = Vector3.zero;
//         float minDistanceToRay = float.MaxValue;
//         bool foundValidPoint = false;

//         for (int i = 0; i < vertices.Length; i++)
//         {
//             Vector3 point = vertices[i];
//             if (point.sqrMagnitude < 0.0001f) continue;
//             Vector3 pointToOrigin = point - localRay.origin;
//             if (Vector3.Dot(localRay.direction, pointToOrigin) < 0) continue;
//             float distanceToRay = Vector3.Cross(localRay.direction, pointToOrigin).magnitude;

//             if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
//             {
//                 minDistanceToRay = distanceToRay;
//                 closestLocalPoint = point;
//                 foundValidPoint = true;
//             }
//         }

//         if (foundValidPoint)
//         {
//             Vector3 selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);

//             // 1. POSICIONA A ESFERA INDICADORA
//             if (targetIndicator != null)
//             {
//                 targetIndicator.position = selectedTargetPoint;
//                 targetIndicator.gameObject.SetActive(true);
//             }

//             // 2. ENVIA PARA O ROS
//             if (rosPublisher != null)
//             {
//                 rosPublisher.PublishTargetPoint(selectedTargetPoint);
//             }
//         }
//         else
//         {
//             Debug.Log("[GGCNN] Seleção cancelada/limpa.");
//             if (targetIndicator != null)
//             {
//                 targetIndicator.gameObject.SetActive(false); // Esconde a esfera amarela
//             }
//         }
//     }
// }














// using UnityEngine;
// using UnityEngine.XR.Hands;
// using UnityEngine.XR.Management;
// using System.Collections.Generic;

// [RequireComponent(typeof(LineRenderer))]
// public class PointCloudSelector : MonoBehaviour
// {
//     [Header("Configurações de Interação")]
//     public Transform rayOrigin;
//     public float maxSelectionTolerance = 0.05f;
//     [Tooltip("Tamanho do laser de mira em metros")]
//     public float laserLength = 1.5f;

//     [Header("Referência da Nuvem de Pontos")]
//     public MeshFilter pointCloudMeshFilter;

//     [Header("Feedback Visual")]
//     public Transform targetIndicator;

//     private LineRenderer visualRay;
//     private XRHandSubsystem handSubsystem;

//     void Awake()
//     {
//         visualRay = GetComponent<LineRenderer>();
//         visualRay.startWidth = 0.002f;
//         visualRay.endWidth = 0.002f;
//         visualRay.positionCount = 2;
//         visualRay.enabled = false; // O laser começa desligado para evitar carga cognitiva
//     }

//     void Start()
//     {
//         // Conecta-se ao subsistema do Meta Quest / XR Hands
//         var subsystems = new List<XRHandSubsystem>();
//         SubsystemManager.GetInstances(subsystems);
//         if (subsystems.Count > 0)
//         {
//             handSubsystem = subsystems[0];
//         }
//     }

//     void Update()
//     {
//         bool isPointing = false;

//         // Monitoriza a Mão Direita para ligar/desligar o laser dinamicamente
//         if (handSubsystem != null && handSubsystem.running)
//         {
//             var rightHand = handSubsystem.rightHand;
//             if (rightHand.isTracked)
//             {
//                 isPointing = CheckPointingPose(rightHand);
//             }
//         }

//         // MIRA ATIVA: Só desenha o laser se o operador estiver fazendo a "arminha"
//         if (isPointing && rayOrigin != null)
//         {
//             visualRay.enabled = true;
//             visualRay.SetPosition(0, rayOrigin.position);
//             visualRay.SetPosition(1, rayOrigin.position + rayOrigin.forward * laserLength);
//         }
//         else
//         {
//             visualRay.enabled = false;
//         }
//     }

//     private bool CheckPointingPose(XRHand hand)
//     {
//         // Captura a posição dos nós dos dedos
//         var wrist = hand.GetJoint(XRHandJointID.Wrist);
//         var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
//         var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);
//         var ringTip = hand.GetJoint(XRHandJointID.RingTip);
//         var littleTip = hand.GetJoint(XRHandJointID.LittleTip);

//         if (!wrist.TryGetPose(out Pose wristPose) ||
//             !indexTip.TryGetPose(out Pose indexPose) ||
//             !middleTip.TryGetPose(out Pose middlePose) ||
//             !ringTip.TryGetPose(out Pose ringPose) ||
//             !littleTip.TryGetPose(out Pose littlePose))
//         {
//             return false;
//         }

//         // REGRA 1: Dedo indicador esticado (ponta distante do pulso)
//         float indexDist = Vector3.Distance(wristPose.position, indexPose.position);
//         bool indexExtended = indexDist > 0.12f; // Valores em metros (12 cm)

//         // REGRA 2: Outros dedos recolhidos (pontas próximas ao pulso)
//         float middleDist = Vector3.Distance(wristPose.position, middlePose.position);
//         float ringDist = Vector3.Distance(wristPose.position, ringPose.position);
//         float littleDist = Vector3.Distance(wristPose.position, littlePose.position);
        
//         bool othersClosed = middleDist < 0.10f && ringDist < 0.10f && littleDist < 0.10f;

//         return indexExtended && othersClosed;
//     }

//     public void ExecuteSelection()
//     {
//         // Trava de Ergonomia: Se o raio não estiver visível (mão relaxada), ignora o clique da mão esquerda
//         if (!visualRay.enabled)
//         {
//             Debug.Log("[GGCNN] Clique ignorado: A Mão Direita não está apontando para nada.");
//             return;
//         }

//         if (pointCloudMeshFilter == null || pointCloudMeshFilter.sharedMesh == null)
//         {
//             Debug.LogWarning("Nuvem de pontos não encontrada.");
//             return;
//         }

//         Vector3[] vertices = pointCloudMeshFilter.sharedMesh.vertices;
//         Transform meshTransform = pointCloudMeshFilter.transform;

//         Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
//         Ray localRay = new Ray(
//             meshTransform.InverseTransformPoint(globalRay.origin), 
//             meshTransform.InverseTransformDirection(globalRay.direction)
//         );

//         Vector3 closestLocalPoint = Vector3.zero;
//         float minDistanceToRay = float.MaxValue;
//         bool foundValidPoint = false;

//         for (int i = 0; i < vertices.Length; i++)
//         {
//             Vector3 point = vertices[i];

//             if (point.sqrMagnitude < 0.0001f) continue;

//             Vector3 pointToOrigin = point - localRay.origin;
//             if (Vector3.Dot(localRay.direction, pointToOrigin) < 0) continue;
            
//             Vector3 crossProduct = Vector3.Cross(localRay.direction, pointToOrigin);
//             float distanceToRay = crossProduct.magnitude;

//             if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
//             {
//                 minDistanceToRay = distanceToRay;
//                 closestLocalPoint = point;
//                 foundValidPoint = true;
//             }
//         }

//         if (foundValidPoint)
//         {
//             Vector3 selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);
//             Debug.Log($"[GGCNN] Ponto alvo válido: {selectedTargetPoint}");

//             if (targetIndicator != null)
//             {
//                 targetIndicator.position = selectedTargetPoint;
//                 targetIndicator.gameObject.SetActive(true);
//             }
//         }
//         else
//         {
//             Debug.Log("[GGCNN] Seleção cancelada/limpa.");
//             if (targetIndicator != null)
//             {
//                 targetIndicator.gameObject.SetActive(false);
//             }
//         }
//     }
// }



















// using UnityEngine;

// [RequireComponent(typeof(LineRenderer))]
// public class PointCloudSelector : MonoBehaviour
// {
//     [Header("Configurações de Interação")]
//     public Transform rayOrigin;
//     public float maxSelectionTolerance = 0.05f;
    
//     [Tooltip("Tamanho do laser de mira em metros (1.5 = 1,5 metros)")]
//     public float laserLength = 1.5f;

//     [Header("Referência da Nuvem de Pontos")]
//     public MeshFilter pointCloudMeshFilter;

//     [Header("Feedback Visual")]
//     public Transform targetIndicator;

//     private LineRenderer visualRay;

//     void Awake()
//     {
//         // Configura a estética do nosso laser contínuo
//         visualRay = GetComponent<LineRenderer>();
//         visualRay.startWidth = 0.002f; // 2 milímetros (bem fino para alta precisão)
//         visualRay.endWidth = 0.002f;
//         visualRay.positionCount = 2;
        
//         // Dica: Você pode criar um Material no Unity com a cor vermelha ou verde neon 
//         // e arrastar para o componente LineRenderer no Inspector para ficar igual a um laser real!
//     }

//     void Update()
//     {
//         // MIRA CONTÍNUA: Desenha o laser saindo do dedo todos os frames
//         if (rayOrigin != null)
//         {
//             visualRay.enabled = true;
//             visualRay.SetPosition(0, rayOrigin.position);
            
//             // Projeta o laser para frente na direção que o dedo aponta
//             visualRay.SetPosition(1, rayOrigin.position + rayOrigin.forward * laserLength);
//         }
//     }

//     public void ExecuteSelection()
//     {
//         if (pointCloudMeshFilter == null || pointCloudMeshFilter.sharedMesh == null)
//         {
//             Debug.LogWarning("Nuvem de pontos não encontrada.");
//             return;
//         }

//         Vector3[] vertices = pointCloudMeshFilter.sharedMesh.vertices;
//         Transform meshTransform = pointCloudMeshFilter.transform;

//         Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
//         Ray localRay = new Ray(
//             meshTransform.InverseTransformPoint(globalRay.origin), 
//             meshTransform.InverseTransformDirection(globalRay.direction)
//         );

//         Vector3 closestLocalPoint = Vector3.zero;
//         float minDistanceToRay = float.MaxValue;
//         bool foundValidPoint = false;

//         for (int i = 0; i < vertices.Length; i++)
//         {
//             Vector3 point = vertices[i];

//             if (point.sqrMagnitude < 0.0001f) continue;

//             Vector3 pointToOrigin = point - localRay.origin;
//             if (Vector3.Dot(localRay.direction, pointToOrigin) < 0) continue;
            
//             Vector3 crossProduct = Vector3.Cross(localRay.direction, pointToOrigin);
//             float distanceToRay = crossProduct.magnitude;

//             if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
//             {
//                 minDistanceToRay = distanceToRay;
//                 closestLocalPoint = point;
//                 foundValidPoint = true;
//             }
//         }

//         if (foundValidPoint)
//         {
//             Vector3 selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);
//             Debug.Log($"[GGCNN] Ponto alvo válido: {selectedTargetPoint}");

//             if (targetIndicator != null)
//             {
//                 targetIndicator.position = selectedTargetPoint;
//                 targetIndicator.gameObject.SetActive(true);
//             }
//         }
//         else
//         {
//             // CANCELAR A SELEÇÃO
//             Debug.Log("[GGCNN] Seleção cancelada/limpa.");
//             if (targetIndicator != null)
//             {
//                 targetIndicator.gameObject.SetActive(false);
//             }
//         }
//     }
// }










// using UnityEngine;

// // Força o Unity a adicionar um renderizador de linha ao objeto automaticamente
// [RequireComponent(typeof(LineRenderer))]
// public class PointCloudSelector : MonoBehaviour
// {
//     [Header("Configurações de Interação")]
//     public Transform rayOrigin;
//     public float maxSelectionTolerance = 0.05f;

//     [Header("Referência da Nuvem de Pontos")]
//     public MeshFilter pointCloudMeshFilter;

//     [Header("Feedback Visual")]
//     public Transform targetIndicator;
//     [Tooltip("Tempo em segundos que o raio visual (laser) fica visível após o clique")]
//     public float rayVisibilityDuration = 1.5f;

//     private Vector3 selectedTargetPoint;
//     private LineRenderer visualRay;

//     void Awake()
//     {
//         // Configura a aparência inicial do raio (laser)
//         visualRay = GetComponent<LineRenderer>();
//         visualRay.startWidth = 0.003f; // 3 mm de espessura (fino e preciso)
//         visualRay.endWidth = 0.003f;
//         visualRay.enabled = false;
//         visualRay.positionCount = 2;
//     }

//     public void ExecuteSelection()
//     {
//         // Uso do sharedMesh para evitar a criação de um clone e o congelamento da malha!
//         if (pointCloudMeshFilter == null || pointCloudMeshFilter.sharedMesh == null)
//         {
//             Debug.LogWarning("Nuvem de pontos não encontrada.");
//             return;
//         }

//         Vector3[] vertices = pointCloudMeshFilter.sharedMesh.vertices;
//         Transform meshTransform = pointCloudMeshFilter.transform;

//         Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
//         Ray localRay = new Ray(
//             meshTransform.InverseTransformPoint(globalRay.origin), 
//             meshTransform.InverseTransformDirection(globalRay.direction)
//         );

//         Vector3 closestLocalPoint = Vector3.zero;
//         float minDistanceToRay = float.MaxValue;
//         bool foundValidPoint = false;

//         for (int i = 0; i < vertices.Length; i++)
//         {
//             Vector3 point = vertices[i];

//             // Ignora os pontos NaN (jogados para a origem)
//             if (point.sqrMagnitude < 0.0001f) continue;

//             // 1º: CALCULA o vetor entre a origem do raio e o ponto atual
//             Vector3 pointToOrigin = point - localRay.origin;
            
//             // 2º: VERIFICA se o ponto está na frente do raio (Dot Product)
//             if (Vector3.Dot(localRay.direction, pointToOrigin) < 0) continue;
            
//             // 3º: CALCULA a distância perpendicular do ponto até a linha do raio
//             Vector3 crossProduct = Vector3.Cross(localRay.direction, pointToOrigin);
//             float distanceToRay = crossProduct.magnitude;

//             if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
//             {
//                 minDistanceToRay = distanceToRay;
//                 closestLocalPoint = point;
//                 foundValidPoint = true;
//             }
//         }

//         if (foundValidPoint)
//         {
//             selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);
//             Debug.Log($"[GGCNN] Ponto alvo válido: {selectedTargetPoint}");

//             if (targetIndicator != null)
//             {
//                 targetIndicator.position = selectedTargetPoint;
//                 targetIndicator.gameObject.SetActive(true);
//             }

//             // Desenha o laser visual conectando o dedo à esfera
//             visualRay.SetPosition(0, globalRay.origin);
//             visualRay.SetPosition(1, selectedTargetPoint);
//             visualRay.enabled = true;

//             // Inicia um contador para esconder o laser após um curto período
//             CancelInvoke("HideRay");
//             Invoke("HideRay", rayVisibilityDuration);
//         }
//         else
//         {
//             // MECANISMO DE CANCELAMENTO: Se apontar para o vazio e confirmar, apaga tudo
//             Debug.Log("[GGCNN] Seleção cancelada/limpa.");
//             if (targetIndicator != null)
//             {
//                 targetIndicator.gameObject.SetActive(false);
//             }
//             visualRay.enabled = false;
//         }
//     }

//     private void HideRay()
//     {
//         visualRay.enabled = false;
//     }
// }





















// using UnityEngine;

// public class PointCloudSelector : MonoBehaviour
// {
//     [Header("Configurações de Interação")]
//     [Tooltip("O Transform que emite o raio (ex: Dedo indicador ou controle do Quest 3)")]
//     public Transform rayOrigin;
    
//     [Tooltip("Distância máxima de tolerância entre o raio e o ponto (em metros). 0.05 = 5 cm.")]
//     public float maxSelectionTolerance = 0.05f;

//     [Header("Referência da Nuvem de Pontos")]
//     [Tooltip("O objeto que contém o MeshFilter com a nuvem renderizada")]
//     public MeshFilter pointCloudMeshFilter;

//     [Header("Feedback Visual")]
//     [Tooltip("Objeto para indicar onde o sistema detectou o clique")]
//     public Transform targetIndicator;

//     private Vector3 selectedTargetPoint;

//     public void ExecuteSelection()
//     {
//         if (pointCloudMeshFilter == null || pointCloudMeshFilter.mesh == null)
//         {
//             Debug.LogWarning("Nuvem de pontos não encontrada.");
//             return;
//         }

//         Vector3[] vertices = pointCloudMeshFilter.mesh.vertices;
//         Transform meshTransform = pointCloudMeshFilter.transform;

//         Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
//         Ray localRay = new Ray(
//             meshTransform.InverseTransformPoint(globalRay.origin), 
//             meshTransform.InverseTransformDirection(globalRay.direction)
//         );

//         Vector3 closestLocalPoint = Vector3.zero;
//         float minDistanceToRay = float.MaxValue;
//         bool foundValidPoint = false;

//         for (int i = 0; i < vertices.Length; i++)
//         {
//             Vector3 point = vertices[i];

//             // AJUSTE 1: Ignorar os pontos NaN que foram jogados para a origem (0,0,0) na desserialização
//             // Como floats podem ter imprecisão, checamos se a magnitude é muito próxima de zero
//             if (point.sqrMagnitude < 0.0001f) 
//                 continue;

//             Vector3 pointToOrigin = point - localRay.origin;
            
//             // AJUSTE 2: Produto Escalar (Dot Product) para garantir que o ponto está NA FRENTE do raio
//             // Se o Dot Product for menor que 0, o ponto está atrás do raio e deve ser ignorado.
//             if (Vector3.Dot(localRay.direction, pointToOrigin) < 0)
//                 continue;
            
//             Vector3 crossProduct = Vector3.Cross(localRay.direction, pointToOrigin);
//             float distanceToRay = crossProduct.magnitude;

//             if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
//             {
//                 minDistanceToRay = distanceToRay;
//                 closestLocalPoint = point;
//                 foundValidPoint = true;
//             }
//         }

//         if (foundValidPoint)
//         {
//             selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);
            
//             Debug.Log($"[GGCNN] Ponto alvo válido selecionado em: {selectedTargetPoint}");

//             if (targetIndicator != null)
//             {
//                 targetIndicator.position = selectedTargetPoint;
//                 targetIndicator.gameObject.SetActive(true);
//             }

//             // EnviarCoordenadaParaROS(selectedTargetPoint);
//         }
//         else
//         {
//             Debug.Log("[GGCNN] Nenhum ponto válido atingido. Os pontos na origem (NaN) foram ignorados.");
//         }
//     }
// }