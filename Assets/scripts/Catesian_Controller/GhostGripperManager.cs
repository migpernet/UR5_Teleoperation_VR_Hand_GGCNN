// Este script vai cuidar da transparência (Fade), de esconder/aparecer e de se "desconectar" do movimento do robô quando necessário.

// Modificado em 25/05/2026, às 21:50, o backup está antes deste script
using UnityEngine;
using System.Collections;

public class GhostGripperManager : MonoBehaviour
{
    [Header("Renderizadores da Garra")]
    [Tooltip("Arraste as partes visuais da garra aqui (MeshRenderers)")]
    public MeshRenderer[] gripperRenderers; 

    private bool isLockedInWorld = false;
    private Vector3 lockedWorldPos;
    private Quaternion lockedWorldRot;
    
    private Coroutine waitToShowCoroutine;

    void Start()
    {
        SetVisibility(false); 
        isLockedInWorld = false;
    }

    void LateUpdate()
    {
        if (isLockedInWorld)
        {
            transform.position = lockedWorldPos;
            transform.rotation = lockedWorldRot;
        }
    }

    // --- NOVA LÓGICA DE APARECIMENTO POR PROXIMIDADE ---
    public void ShowGhost(Vector3 clickLocation)
    {
        if (isLockedInWorld) return; 

        // Cancela qualquer contagem anterior
        if (waitToShowCoroutine != null) StopCoroutine(waitToShowCoroutine);
        
        // Inicia o vigia de proximidade
        waitToShowCoroutine = StartCoroutine(WaitToArriveAndShow(clickLocation));
    }

    private IEnumerator WaitToArriveAndShow(Vector3 target)
    {
        float timeout = 6.0f; // Tempo máximo de espera (1.5 segundos)
        float timer = 0f;

        // Pega a referência da peça que realmente se move (a primeira malha da garra)
        Transform movingPart = transform; 
        if (gripperRenderers.Length > 0 && gripperRenderers[0] != null)
        {
            movingPart = gripperRenderers[0].transform;
        }

        // Aguarda a garra chegar perto (25cm) OU o tempo limite estourar
        while (Vector3.Distance(movingPart.position, target) >= 0.08f && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null; 
        }

        // Chegou no alvo ou o tempo limite estourou: Materializa a garra!
        SetVisibility(true);
    }

    public void HideGhostInstantly()
    {
        if (isLockedInWorld) return; 

        if (waitToShowCoroutine != null) StopCoroutine(waitToShowCoroutine);
        SetVisibility(false);
    }

    public void LockPoseInWorld()
    {
        lockedWorldPos = transform.position;
        lockedWorldRot = transform.rotation;
        isLockedInWorld = true;
    }

    public void UnlockAndHide()
    {
        isLockedInWorld = false;
        HideGhostInstantly(); // Garante que zera tudo e esconde
    }

    public void TriggerFadeOut()
    {
        SetVisibility(false);
    }

    private void SetVisibility(bool state)
    {
        foreach (var rend in gripperRenderers)
        {
            if (rend != null)
            {
                rend.enabled = state; 
            }
        }
    }
}













// // Modificado em 25/05/2026, às 21:41, o backup está antes deste script
// using UnityEngine;

// public class GhostGripperManager : MonoBehaviour
// {
//     [Header("Renderizadores da Garra")]
//     [Tooltip("Arraste as partes visuais da garra aqui (MeshRenderers)")]
//     public MeshRenderer[] gripperRenderers; 

//     private bool isLockedInWorld = false;
//     private Vector3 lockedWorldPos;
//     private Quaternion lockedWorldRot;

//     void Start()
//     {
//         // Garante que o Unity comece com a garra 100% invisível
//         SetVisibility(false); 
//         isLockedInWorld = false;
//     }

//     void LateUpdate()
//     {
//         // Se estiver travada, força ela a ficar parada no ar ignorando o braço do robô
//         if (isLockedInWorld)
//         {
//             transform.position = lockedWorldPos;
//             transform.rotation = lockedWorldRot;
//         }
//     }

//     // Chamado quando o dedo aponta para a peça
//     public void ShowGhost()
//     {
//         if (isLockedInWorld) return; 
//         SetVisibility(true);
//     }

//     // Chamado quando o dedo aponta para o vazio
//     public void HideGhostInstantly()
//     {
//         if (isLockedInWorld) return; 
//         SetVisibility(false);
//     }

//     // Chamado pelo Duplo Legal para cravar a garra no espaço
//     public void LockPoseInWorld()
//     {
//         lockedWorldPos = transform.position;
//         lockedWorldRot = transform.rotation;
//         isLockedInWorld = true;
//     }

//     // Chamado quando a rotina termina, para resetar para a próxima peça
//     public void UnlockAndHide()
//     {
//         isLockedInWorld = false;
//         SetVisibility(false);
//     }

//     // Chamado no momento da descida vertical do robô
//     public void TriggerFadeOut()
//     {
//         // Como o fade dependia do material, agora nós simplesmente
//         // desligamos a garra no exato momento da preensão para evitar bugs visuais.
//         SetVisibility(false);
//     }

//     // Função central (Bala de Prata) que liga/desliga apenas o desenho da garra
//     private void SetVisibility(bool state)
//     {
//         foreach (var rend in gripperRenderers)
//         {
//             if (rend != null)
//             {
//                 rend.enabled = state; // Isso é 100% infalível, não importa o Material!
//             }
//         }
//     }
// }



















// using UnityEngine;
// using System.Collections;

// public class GhostGripperManager : MonoBehaviour
// {
//     [Header("Configurações Visuais")]
//     public MeshRenderer[] gripperRenderers; 
//     public float fadeDuration = 1.0f; 

//     private bool isLockedInWorld = false;
//     private Vector3 lockedWorldPos;
//     private Quaternion lockedWorldRot;
//     private Coroutine fadeCoroutine;
//     private bool isVisible = false;

//     void Start()
//     {
//         SetAlpha(0f); // Começa invisível por default
//         isVisible = false;
//     }

//     // O truque de ouro para manter a garra parada sem tirá-la da hierarquia!
//     void LateUpdate()
//     {
//         if (isLockedInWorld)
//         {
//             // Força a posição global, ignorando o movimento do braço do robô
//             transform.position = lockedWorldPos;
//             transform.rotation = lockedWorldRot;
//         }
//     }

//     // Chamado quando o dedo aponta para um ponto válido
//     public void ShowGhost()
//     {
//         if (isLockedInWorld) return; // Se já estiver na rotina de preensão, ignora

//         if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
//         SetAlpha(0.6f); // Fica semi-transparente aguardando o Duplo Legal
//         isVisible = true;
//     }

//     // Chamado quando o dedo aponta para o vazio
//     public void HideGhostInstantly()
//     {
//         if (isLockedInWorld) return; // Não deixa sumir se o robô já estiver operando

//         if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
//         SetAlpha(0f);
//         isVisible = false;
//     }

//     // Trava a garra no ar quando o Duplo Legal acontece
//     public void LockPoseInWorld()
//     {
//         lockedWorldPos = transform.position;
//         lockedWorldRot = transform.rotation;
//         isLockedInWorld = true;
//     }

//     // Destrava a garra e esconde quando a operação termina
//     public void UnlockAndHide()
//     {
//         isLockedInWorld = false;
//         HideGhostInstantly();
//     }

//     // Faz a garra sumir suavemente quando o robô chega nela
//     public void TriggerFadeOut()
//     {
//         if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
//         fadeCoroutine = StartCoroutine(FadeOutRoutine());
//     }

//     private IEnumerator FadeOutRoutine()
//     {
//         float elapsedTime = 0f;
//         float startAlpha = GetCurrentAlpha();

//         while (elapsedTime < fadeDuration)
//         {
//             elapsedTime += Time.deltaTime;
//             float newAlpha = Mathf.Lerp(startAlpha, 0f, elapsedTime / fadeDuration);
//             SetAlpha(newAlpha);
//             yield return null;
//         }

//         SetAlpha(0f);
//         isVisible = false;
//     }

//     private void SetAlpha(float alpha)
//     {
//         foreach (var rend in gripperRenderers)
//         {
//             if (rend != null)
//             {
//                 foreach (var mat in rend.materials)
//                 {
//                     Color color = mat.color;
//                     color.a = alpha;
//                     mat.color = color;
//                 }
//             }
//         }
//     }

//     private float GetCurrentAlpha()
//     {
//         if (gripperRenderers.Length > 0 && gripperRenderers[0] != null)
//             return gripperRenderers[0].material.color.a;
//         return 0f;
//     }
// }