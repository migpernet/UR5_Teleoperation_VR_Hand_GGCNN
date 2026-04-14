using UnityEngine;
using UnityEngine.UI; // Necessário para acessar o RawImage
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor; // Necessário para as mensagens de sensor do ROS

public class RealSense_Subscriber : MonoBehaviour
{
    [Header("Configuração de Rede")]
    [Tooltip("Use SEMPRE o tópico compressed para não travar o VR")]
    public string imageTopic = "/camera/color/image_raw/compressed";

    [Header("Referência Visual")]
    [Tooltip("Arraste o seu 'Feed_Video' (RawImage) aqui para mostrar o vídeo da câmera")]
    public RawImage telaDeVideo; // Arraste o seu 'Feed_Video' aqui

    private Texture2D texturaCamera;
    private bool framePronto = false;
    private byte[] dadosDaImagem;

    void Start()
    {
        // Inicializa uma textura vazia (o tamanho se ajusta automaticamente ao JPEG)
        texturaCamera = new Texture2D(1, 1);
        
        // Assina o tópico de imagem comprimida
        ROSConnection.GetOrCreateInstance().Subscribe<CompressedImageMsg>(imageTopic, ReceiveImage);
    }

    void ReceiveImage(CompressedImageMsg message)
    {
        // Salva os bytes do JPEG que chegaram do ROS
        dadosDaImagem = message.data;
        framePronto = true;
    }

    void Update()
    {
        // Atualiza a tela apenas quando um novo frame chega
        if (framePronto && telaDeVideo != null && dadosDaImagem != null)
        {
            // Decodifica o array de bytes (JPEG) direto para a textura do Unity
            texturaCamera.LoadImage(dadosDaImagem);
            texturaCamera.Apply();

            // Coloca a textura na nossa RawImage do Canvas
            telaDeVideo.texture = texturaCamera;
            
            framePronto = false;
        }
    }

    void OnDestroy()
    {
        // Limpa a memória da textura quando fechar o simulador
        if (texturaCamera != null)
        {
            Destroy(texturaCamera);
        }
    }
}