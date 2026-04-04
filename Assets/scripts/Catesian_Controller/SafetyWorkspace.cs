using UnityEngine;

public class SafetyWorkspace : MonoBehaviour
{
    [Header("Referências Core")]
    [Tooltip("Arraste a Base do robô (ex: base_link) aqui. Tudo será medido a partir dela.")]
    public Transform robotBase;

    [Header("Limite Esférico (Singularidade)")]
    [Tooltip("Alcance máximo permitido em metros. (UR5 = ~0.85m)")]
    public float maxReachRadius = 0.80f; 

    [Header("Limites da Caixa (Cerca Cartesiana)")]
    [Tooltip("Valores mínimos relativos à base.")]
    public Vector3 minBounds = new Vector3(-0.6f, 0.0f, -0.6f); 
    
    [Tooltip("Valores máximos relativos à base.")]
    public Vector3 maxBounds = new Vector3(0.6f, 0.8f, 0.6f);

    [Header("Visualização (UI Toggle)")]
    [Tooltip("Arraste o Material translúcido (Mat_WorkspaceBounds) criado aqui.")]
    public Material boxMaterial;
    
    [Tooltip("Inicia com a caixa visível?")]
    public bool showVisualBoxOnStart = true;

    // Variáveis Internas da Malha Visual
    private GameObject visualBoxObject;
    private MeshRenderer boxRenderer;
    private bool isVisualBoxEnabled = false;

    void Start()
    {
        // Cria a malha visual automaticamente se tivermos o material
        if (boxMaterial != null && robotBase != null)
        {
            CreateVisualBox();
            SetVisualBoxVisibility(showVisualBoxOnStart);
        }
    }

    // --- NOVA FUNÇÃO: Cria e dimensiona o cubo visual ---
    private void CreateVisualBox()
    {
        // 1. Cria um cubo primitivo do Unity
        visualBoxObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visualBoxObject.name = "Visual_Workspace_Bounds";
        
        // 2. Remove o Collider para que ele NÃO atrapalhe a mão VR (NÃO QUEREMOS COLISÃO FÍSICA AQUI)
        Collider cubeCollider = visualBoxObject.GetComponent<Collider>();
        if (cubeCollider != null) Destroy(cubeCollider);

        // 3. Aplica o material translúcido
        boxRenderer = visualBoxObject.GetComponent<MeshRenderer>();
        boxRenderer.material = boxMaterial;
        boxRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Sem sombras

        // 4. Coloca o cubo como "filho" da base do robô (para rodar junto com ele)
        visualBoxObject.transform.SetParent(robotBase);

        // 5. Dimensiona e posiciona o cubo com base nos 'minBounds' e 'maxBounds'
        UpdateVisualBoxBounds();
    }

    // --- NOVA FUNÇÃO: Atualiza o tamanho da caixa se você mudar os limites no Editor ---
    // (Pode chamar essa função se você implementar alteração dinâmica de limites)
    public void UpdateVisualBoxBounds()
    {
        if (visualBoxObject == null) return;

        // Calcula o centro e o tamanho baseados nas restrições cartesianas locais
        Vector3 center = (minBounds + maxBounds) / 2f;
        Vector3 size = maxBounds - minBounds;

        visualBoxObject.transform.localPosition = center;
        visualBoxObject.transform.localScale = size;
    }

    // --- NOVA FUNÇÃO PÚBLICA (Para o Botão do Canvas) ---
    public void ToggleVisualBox()
    {
        SetVisualBoxVisibility(!isVisualBoxEnabled);
    }

    public void SetVisualBoxVisibility(bool visible)
    {
        isVisualBoxEnabled = visible;
        if (boxRenderer != null)
        {
            boxRenderer.enabled = visible;
        }
    }

    // --- A FUNÇÃO DE FILTRAGEM (O CORAÇÃO DO MÓDULO) ---
    public Vector3 ApplyLimits(Vector3 desiredPosition)
    {
        if (robotBase == null) return desiredPosition;

        Vector3 safePosition = desiredPosition;

        // Converte a coordenada global da mão para o espaço local (em relação à base do robô)
        Vector3 localPos = robotBase.InverseTransformPoint(safePosition);

        // O 'Mathf.Clamp' é a função matemática que "espreme" o valor dentro do limite
        localPos.x = Mathf.Clamp(localPos.x, minBounds.x, maxBounds.x);
        localPos.y = Mathf.Clamp(localPos.y, minBounds.y, maxBounds.y);
        localPos.z = Mathf.Clamp(localPos.z, minBounds.z, maxBounds.z);

        // Devolve o valor limitado para o espaço global
        safePosition = robotBase.TransformPoint(localPos);

        // Mede o vetor que sai da base do robô até a coordenada que passou pela caixa
        Vector3 baseToHand = safePosition - robotBase.position;
        
        if (baseToHand.magnitude > maxReachRadius)
        {
            // Se a distância for maior que o braço, encurta o vetor e puxa de volta para a borda da esfera
            safePosition = robotBase.position + baseToHand.normalized * maxReachRadius;
        }

        return safePosition;
    }

    // --- MAGIA VISUAL: Mantemos os Gizmos do Editor também ---
    private void OnDrawGizmos()
    {
        if (robotBase == null) return;

        Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.03f); // Amarelo bem transparente
        Gizmos.DrawSphere(robotBase.position, maxReachRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(robotBase.position, maxReachRadius);

        Gizmos.matrix = robotBase.localToWorldMatrix; 
        Gizmos.color = new Color(0f, 1f, 1f, 0.05f);  // Ciano bem transparente
        Vector3 center = (minBounds + maxBounds) / 2f;
        Vector3 size = maxBounds - minBounds;
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, size);
    }
}

















// using UnityEngine;

// public class SafetyWorkspace : MonoBehaviour
// {
//     [Header("Referências")]
//     [Tooltip("Arraste a Base do robô (ex: base_link) aqui. Tudo será medido a partir dela.")]
//     public Transform robotBase;

//     [Header("Limite Esférico (Singularidade)")]
//     [Tooltip("Alcance máximo permitido em metros. (UR5 = ~0.85m)")]
//     public float maxReachRadius = 0.80f; // Colocamos 80cm para manter uma margem de segurança

//     [Header("Limites da Caixa (Cerca Cartesiana)")]
//     [Tooltip("Valores mínimos relativos à base. Ex: Y=0 impede de bater na mesa.")]
//     public Vector3 minBounds = new Vector3(-0.6f, 0.0f, -0.6f); 
    
//     [Tooltip("Valores máximos relativos à base.")]
//     public Vector3 maxBounds = new Vector3(0.6f, 0.8f, 0.6f);

//     // --- A FUNÇÃO DE FILTRAGEM (O CORAÇÃO DO MÓDULO) ---
//     public Vector3 ApplyLimits(Vector3 desiredPosition)
//     {
//         if (robotBase == null) return desiredPosition;

//         Vector3 safePosition = desiredPosition;

//         // 1. LIMITES DA CAIXA (Cerca Cartesiana)
//         // Converte a coordenada global da mão para o espaço local (em relação à base do robô)
//         Vector3 localPos = robotBase.InverseTransformPoint(safePosition);

//         // O 'Mathf.Clamp' é a função matemática que "espreme" o valor dentro do limite
//         localPos.x = Mathf.Clamp(localPos.x, minBounds.x, maxBounds.x);
//         localPos.y = Mathf.Clamp(localPos.y, minBounds.y, maxBounds.y);
//         localPos.z = Mathf.Clamp(localPos.z, minBounds.z, maxBounds.z);

//         // Devolve o valor limitado para o espaço global
//         safePosition = robotBase.TransformPoint(localPos);

//         // 2. LIMITE ESFÉRICO (Prevenção de Singularidade Externa)
//         // Mede o vetor que sai da base do robô até a coordenada que passou pela caixa
//         Vector3 baseToHand = safePosition - robotBase.position;
        
//         if (baseToHand.magnitude > maxReachRadius)
//         {
//             // Se a distância for maior que o braço, encurta o vetor e puxa de volta para a borda da esfera
//             safePosition = robotBase.position + baseToHand.normalized * maxReachRadius;
//         }

//         return safePosition;
//     }

//     // --- MAGIA VISUAL: Desenha a jaula no Editor do Unity ---
//     private void OnDrawGizmos()
//     {
//         if (robotBase == null) return;

//         // 1. Desenha a Esfera de Alcance Máximo (Amarelo)
//         Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.1f); // Amarelo transparente
//         Gizmos.DrawSphere(robotBase.position, maxReachRadius);
//         Gizmos.color = Color.yellow;
//         Gizmos.DrawWireSphere(robotBase.position, maxReachRadius);

//         // 2. Desenha a Caixa de Restrição (Ciano)
//         Gizmos.matrix = robotBase.localToWorldMatrix; // Alinha a caixa com a rotação da base
//         Gizmos.color = new Color(0f, 1f, 1f, 0.15f);  // Ciano transparente
//         Vector3 center = (minBounds + maxBounds) / 2f;
//         Vector3 size = maxBounds - minBounds;
//         Gizmos.DrawCube(center, size);
//         Gizmos.color = Color.cyan;
//         Gizmos.DrawWireCube(center, size);
//     }
// }