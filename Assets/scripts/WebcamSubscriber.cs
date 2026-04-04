using UnityEngine;
using UnityEngine.UI; // NECESSÁRIO PARA USAR RAWIMAGE
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class WebcamSubscriber : MonoBehaviour
{
    [Header("ROS Configuration")]
    public string topicName = "/usb_cam/image_raw/compressed"; 

    [Header("Visual Output")]
    public RawImage screenDisplay; // Mudamos de MeshRenderer para RawImage

    private ROSConnection ros;
    private Texture2D texture2D;
    private byte[] imageData;
    private bool isMessageReceived = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // Cria uma textura inicial (vermelha para debug - se ficar vermelha, o objeto existe!)
        texture2D = new Texture2D(1, 1);
        texture2D.SetPixel(0, 0, Color.red); 
        texture2D.Apply();
        
        // Vincula a textura ao componente de UI
        if (screenDisplay != null)
        {
            screenDisplay.texture = texture2D;
        }

        ros.Subscribe<CompressedImageMsg>(topicName, OnImageReceived);
    }

    void OnImageReceived(CompressedImageMsg msg)
    {
        imageData = msg.data;
        isMessageReceived = true;
    }

    void Update()
    {
        if (isMessageReceived)
        {
            ProcessImage();
            isMessageReceived = false;
        }
    }

    void ProcessImage()
    {
        if (imageData != null && imageData.Length > 0)
        {
            // Tenta carregar o JPG
            bool success = texture2D.LoadImage(imageData);
            
            if (success)
            {
                texture2D.Apply();
            }
            else
            {
                Debug.LogWarning("Falha ao decodificar imagem JPG do ROS.");
            }
        }
    }
}