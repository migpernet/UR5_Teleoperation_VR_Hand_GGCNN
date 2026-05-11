
// Este script é responsável por publicar a coordenada do ponto selecionado para o GGCNN no ROS (esfera amarelinha). Ele deve ser chamado pelo PointCloudSelector quando um ponto for selecionado.

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class TargetPointPublisher : MonoBehaviour
{
    [Header("Configurações ROS")]
    [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
    public string topicName = "/ggcnn/target_intention_point";
    
    [Header("Referenciais (Frames)")]
    [Tooltip("Arraste o objeto correspondente ao 'base_link' do robô no Unity")]
    public Transform robotBaseLink;

    private ROSConnection ros;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PointMsg>(topicName);
        Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
    }

    /// <summary>
    /// Recebe a coordenada Global do Unity, converte para o referencial da Base e envia para o ROS
    /// </summary>
    public void PublishTargetPoint(Vector3 unityWorldPoint)
    {
        if (robotBaseLink == null)
        {
            Debug.LogError("[ROS GGCNN] ERRO: A referência do 'base_link' não foi atribuída no script!");
            return;
        }

        // 1. CONVERSÃO DE ESPAÇO: Global (World) -> Local (base_link)
        // Isto transforma as coordenadas para que a base do robô seja o (0,0,0)
        Vector3 localTarget = robotBaseLink.InverseTransformPoint(unityWorldPoint);

        // 2. CONVERSÃO DE REFERENCIAL (EIXOS): Unity (Esquerda) -> ROS FLU (Direita)
        // No ROS FLU (Forward, Left, Up):
        // X do ROS = Frente (Z do Unity)
        // Y do ROS = Esquerda (-X do Unity)
        // Z do ROS = Cima (Y do Unity)
        // CÓDIGO NOVO (O Alinhamento Perfeito)
        PointMsg pointMsg = new PointMsg(
            localTarget.x,  // X do ROS (Frente) = X do Unity (Seta Vermelha)
            -localTarget.z, // Y do ROS (Esquerda) = Inverso do Z do Unity (Seta Azul aponta p/ Direita)
            localTarget.y   // Z do ROS (Cima) = Y do Unity (Seta Verde)
        );

        // 3. PUBLICAÇÃO
        ros.Publish(topicName, pointMsg);
        
        Debug.Log($"[ROS] Coordenada Local (base_link) enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
    }
}










// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;

// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Configurações ROS")]
//     [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
//     public string topicName = "/ggcnn/target_intention_point";
    
//     [Header("Referenciais (Frames)")]
//     [Tooltip("Arraste o objeto correspondente ao 'base_link' do robô no Unity")]
//     public Transform robotBaseLink;

//     private ROSConnection ros;

//     void Start()
//     {
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(topicName);
//         Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
//     }

//     /// <summary>
//     /// Recebe a coordenada Global do Unity, converte para o referencial da Base e envia para o ROS
//     /// </summary>
//     public void PublishTargetPoint(Vector3 unityWorldPoint)
//     {
//         if (robotBaseLink == null)
//         {
//             Debug.LogError("[ROS GGCNN] ERRO: A referência do 'base_link' não foi atribuída no script!");
//             return;
//         }

//         // 1. CONVERSÃO DE ESPAÇO: Global (World) -> Local (base_link)
//         // Isto transforma as coordenadas para que a base do robô seja o (0,0,0)
//         Vector3 localTarget = robotBaseLink.InverseTransformPoint(unityWorldPoint);

//         // 2. CONVERSÃO DE REFERENCIAL (EIXOS): Unity (Esquerda) -> ROS FLU (Direita)
//         // No ROS FLU (Forward, Left, Up):
//         // X do ROS = Frente (Z do Unity)
//         // Y do ROS = Esquerda (-X do Unity)
//         // Z do ROS = Cima (Y do Unity)
//         PointMsg pointMsg = new PointMsg(
//             localTarget.z,  
//             -localTarget.x, 
//             localTarget.y   
//         );

//         // 3. PUBLICAÇÃO
//         ros.Publish(topicName, pointMsg);
        
//         Debug.Log($"[ROS] Coordenada Local (base_link) enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
//     }
// }











// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using RosMessageTypes.Geometry;

// // Este script é responsável por publicar a coordenada do objeto para o GGCNN no ROS. Ele deve ser chamado pelo PointCloudSelector quando um ponto for selecionado.


// public class TargetPointPublisher : MonoBehaviour
// {
//     [Header("Configurações ROS")]
//     [Tooltip("Nome do tópico que o nó em Python no Ubuntu vai escutar")]
//     public string topicName = "/ggcnn/target_intention_point";
    
//     private ROSConnection ros;

//     void Start()
//     {
//         // Inicializa a conexão e registra o publicador
//         ros = ROSConnection.GetOrCreateInstance();
//         ros.RegisterPublisher<PointMsg>(topicName);
//         Debug.Log($"[ROS] Publisher registrado no tópico: {topicName}");
//     }

//     /// <summary>
//     /// Recebe a coordenada do Unity e envia para o ROS
//     /// </summary>
//     public void PublishTargetPoint(Vector3 unityPoint)
//     {
//         // Conversão básica de referenciais (Unity Left-Handed -> ROS Right-Handed FLU)
//         // Atenção: Esta conversão pode precisar de ajustes dependendo de como o seu base_link está configurado
//         PointMsg pointMsg = new PointMsg(
//             unityPoint.z,  // X do ROS costuma ser a profundidade (Z do Unity)
//             -unityPoint.x, // Y do ROS costuma ser a lateral invertida (-X do Unity)
//             unityPoint.y   // Z do ROS costuma ser a altura (Y do Unity)
//         );

//         // Publica a mensagem na rede
//         ros.Publish(topicName, pointMsg);
//         Debug.Log($"[ROS] Coordenada de intenção enviada: X:{pointMsg.x:F3}, Y:{pointMsg.y:F3}, Z:{pointMsg.z:F3}");
//     }
// }