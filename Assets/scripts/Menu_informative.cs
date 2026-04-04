using UnityEngine;
using UnityEngine.InputSystem;

public class Menu_informative : MonoBehaviour
{
    public Transform jogador;
    public GameObject menu_info;
    public InputActionProperty botao_ativar_menu;
    
    // NOVO: Armazena a última posição e rotação onde o menu estava visível.
    private Vector3 ultimaPosicao;
    private Quaternion ultimaRotacao;

    // --- Configurações Iniciais para Posicionamento ---
    [Header("Positioning Configuration")]
    public float distanciaInicial = 1.5f; // Distância padrão para quando o menu for ativado pela primeira vez.
    public float alturaInicial = 0.0f;     // Altura (offset Y) do menu em relação ao jogador.

    void Awake()
    {
        // Inicializa as ações de input.
        botao_ativar_menu.action.Enable();

        // Inicializa a última posição com o home position do jogador.
        ultimaPosicao = jogador.position;
        ultimaRotacao = jogador.rotation;
    }

    void Update()
    {
        if (botao_ativar_menu.action.WasPressedThisFrame())
        {
            bool menuAtivo = menu_info.activeSelf;
            
            // 1. DESATIVAÇÃO (Se o menu estiver ativo, salve a posição atual antes de desativar)
            if (menuAtivo)
            {
                // Salva a posição e rotação EXATAS do menu antes de escondê-lo.
                ultimaPosicao = menu_info.transform.position;
                ultimaRotacao = menu_info.transform.rotation;

                menu_info.SetActive(false);
            }
            // 2. ATIVAÇÃO (Se o menu estiver desativado, reapareça na última posição salva)
            else
            {
                // 🔹 Lógica: O menu SÓ DEVE CALCULAR A POSIÇÃO INICIAL se for a primeiríssima vez.
                // Usamos o Vector3.zero como uma flag segura para verificar se a posição foi definida.
                if (ultimaPosicao == jogador.position) // Se ainda estiver na posição inicial do Awake
                {
                    // Calcula a posição inicial na frente do jogador (apenas na 1ª vez).
                    Vector3 direcaoFrente = new Vector3(jogador.forward.x, 0, jogador.forward.z).normalized;
                    ultimaPosicao = jogador.position + direcaoFrente * distanciaInicial;
                    ultimaPosicao.y += alturaInicial; 
                    ultimaRotacao = Quaternion.LookRotation(-direcaoFrente); // Rotação para olhar para o jogador
                }

                // Aplica a última posição e rotação salvas.
                menu_info.transform.position = ultimaPosicao;
                menu_info.transform.rotation = ultimaRotacao;

                menu_info.SetActive(true);
            }
        }

        // REMOVIDO: O código de LookAt() e multiplicação por -1 foi removido do Update(), 
        // pois queremos que a rotação seja fixa na última rotação salva.
    }

    void OnDisable()
    {
        // Garante que a ação seja desabilitada ao desativar o objeto.
        botao_ativar_menu.action.Disable();
    }
}















// using System.Collections;
// using System.Collections.Generic;
// using UnityEditor;
// using UnityEngine;
// using UnityEngine.InputSystem;

// public class Menu_informative : MonoBehaviour
// {
//     public Transform jogador;
//     public float distancia_para_ativar = 3.0f;
//     public GameObject menu_info;
//     public InputActionProperty botao_ativar_menu;
 
//     // Update is called once per frame
//     void Update()
//     {
//         if(botao_ativar_menu.action.WasPressedThisFrame())
//         {
//             menu_info.SetActive(!menu_info.activeSelf);
//             menu_info.transform.position = jogador.position + new Vector3(jogador.forward.x, 0, jogador.forward.z).normalized * distancia_para_ativar;
//         }

//         menu_info.transform.LookAt(new Vector3(jogador.position.x, menu_info.transform.position.y, jogador.position.z));
//         menu_info.transform.forward *= -1;

//     }
// }
