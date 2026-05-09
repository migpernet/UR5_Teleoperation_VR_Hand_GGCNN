using UnityEngine;

public class PointCloudSelector : MonoBehaviour
{
    [Header("Configurações de Interação")]
    [Tooltip("O Transform que emite o raio (ex: Dedo indicador ou controle do Quest 3)")]
    public Transform rayOrigin;
    
    [Tooltip("Distância máxima de tolerância entre o raio e o ponto (em metros). 0.05 = 5 cm.")]
    public float maxSelectionTolerance = 0.05f;

    [Header("Referência da Nuvem de Pontos")]
    [Tooltip("O objeto que contém o MeshFilter com a nuvem renderizada")]
    public MeshFilter pointCloudMeshFilter;

    [Header("Feedback Visual")]
    [Tooltip("Objeto para indicar onde o sistema detectou o clique")]
    public Transform targetIndicator;

    private Vector3 selectedTargetPoint;

    public void ExecuteSelection()
    {
        if (pointCloudMeshFilter == null || pointCloudMeshFilter.mesh == null)
        {
            Debug.LogWarning("Nuvem de pontos não encontrada.");
            return;
        }

        Vector3[] vertices = pointCloudMeshFilter.mesh.vertices;
        Transform meshTransform = pointCloudMeshFilter.transform;

        Ray globalRay = new Ray(rayOrigin.position, rayOrigin.forward);
        Ray localRay = new Ray(
            meshTransform.InverseTransformPoint(globalRay.origin), 
            meshTransform.InverseTransformDirection(globalRay.direction)
        );

        Vector3 closestLocalPoint = Vector3.zero;
        float minDistanceToRay = float.MaxValue;
        bool foundValidPoint = false;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = vertices[i];

            // AJUSTE 1: Ignorar os pontos NaN que foram jogados para a origem (0,0,0) na desserialização
            // Como floats podem ter imprecisão, checamos se a magnitude é muito próxima de zero
            if (point.sqrMagnitude < 0.0001f) 
                continue;

            Vector3 pointToOrigin = point - localRay.origin;
            
            // AJUSTE 2: Produto Escalar (Dot Product) para garantir que o ponto está NA FRENTE do raio
            // Se o Dot Product for menor que 0, o ponto está atrás do raio e deve ser ignorado.
            if (Vector3.Dot(localRay.direction, pointToOrigin) < 0)
                continue;
            
            Vector3 crossProduct = Vector3.Cross(localRay.direction, pointToOrigin);
            float distanceToRay = crossProduct.magnitude;

            if (distanceToRay < maxSelectionTolerance && distanceToRay < minDistanceToRay)
            {
                minDistanceToRay = distanceToRay;
                closestLocalPoint = point;
                foundValidPoint = true;
            }
        }

        if (foundValidPoint)
        {
            selectedTargetPoint = meshTransform.TransformPoint(closestLocalPoint);
            
            Debug.Log($"[GGCNN] Ponto alvo válido selecionado em: {selectedTargetPoint}");

            if (targetIndicator != null)
            {
                targetIndicator.position = selectedTargetPoint;
                targetIndicator.gameObject.SetActive(true);
            }

            // EnviarCoordenadaParaROS(selectedTargetPoint);
        }
        else
        {
            Debug.Log("[GGCNN] Nenhum ponto válido atingido. Os pontos na origem (NaN) foram ignorados.");
        }
    }
}