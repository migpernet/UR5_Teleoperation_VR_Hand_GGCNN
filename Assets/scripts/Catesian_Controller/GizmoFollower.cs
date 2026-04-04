using UnityEngine;

public class GizmoFollower : MonoBehaviour
{
    [Header("Alvo")]
    [Tooltip("A Câmera do jogador (se vazio, ele busca a Main Camera)")]
    public Transform vrCamera;

    [Header("Posicionamento")]
    [Tooltip("Distância à frente do rosto (em metros)")]
    public float distance = 0.2f; 
    
    [Tooltip("Altura relativa ao rosto (ex: -0.2 deixa na altura do peito)")]
    public float heightOffset = -0.7f; 
    
    [Tooltip("Deslocamento lateral (ex: -0.3 joga o painel para a esquerda, 0.3 para a direita)")]
    public float lateralOffset = 0.3f; // Ideal para a mão esquerda!

    [Header("Suavidade (Lazy Follow)")]
    [Tooltip("Velocidade com que o painel alcança a sua posição")]
    public float positionFollowSpeed = 4.0f;
    [Tooltip("Velocidade com que o painel gira para encarar você")]
    public float rotationFollowSpeed = 4.0f;

    void Start()
    {
        // Tenta achar a câmera principal automaticamente se você esquecer de arrastar
        if (vrCamera == null && Camera.main != null)
        {
            vrCamera = Camera.main.transform;
        }
    }

    void LateUpdate()
    {
        if (vrCamera == null) return;

        // 1. Achamos a direção "Frente" e "Lado" achatadas no plano (sem contar a inclinação do pescoço)
        Vector3 flatForward = new Vector3(vrCamera.forward.x, 0, vrCamera.forward.z).normalized;
        Vector3 flatRight = new Vector3(vrCamera.right.x, 0, vrCamera.right.z).normalized;

        if (flatForward == Vector3.zero) flatForward = transform.forward; 

        // 2. Calcula a posição alvo somando Frente, Lado e Altura
        Vector3 targetPosition = vrCamera.position 
                                 + (flatForward * distance) 
                                 + (flatRight * lateralOffset);
        
        targetPosition.y = vrCamera.position.y + heightOffset;

        // 3. Calcula para onde o painel deve olhar (sempre reto para você)
        Quaternion targetRotation = Quaternion.LookRotation(flatForward);

        // 4. Aplica o movimento elástico (Lerp) para evitar enjoo
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * positionFollowSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationFollowSpeed);
    }
}