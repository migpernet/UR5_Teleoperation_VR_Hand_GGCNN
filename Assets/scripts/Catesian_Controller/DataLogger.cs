using UnityEngine;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour
{
    [Header("Referências")]
    public CartesianHandController handController;
    
    [Header("Controle de Gravação")]
    public bool isRecording = false;
    public string fileName = "Experimento_Tracking.csv";
    
    private StreamWriter writer;
    private float startTime;

    void Start()
    {
        string filePath = Path.Combine(Application.dataPath, fileName);
        writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        // NOVO: Adicionamos as colunas do Gazebo (Robo_Real_X, Y, Z)
        writer.WriteLine("Tempo(s),Mao_Bruta_X,Mao_Bruta_Y,Mao_Bruta_Z,Filtro_X,Filtro_Y,Filtro_Z,Cerca_X,Cerca_Y,Cerca_Z,Robo_Real_X,Robo_Real_Y,Robo_Real_Z,Teleoperando");
        Debug.Log($"[DataLogger] Arquivo CSV criado em: {filePath}");
    }

    void Update()
    {
        if (!isRecording || handController == null) return;

        if (startTime == 0f) startTime = Time.time;
        float currentTime = Time.time - startTime;

        Vector3 raw = handController.rawHandPosition;
        Vector3 filtered = handController.ghostCube.position; // Este é o Gizmo (Target)
        Vector3 safe = handController.safeTargetPosition;
        
        // NOVO: Captura a posição do end-effector espelhado do Gazebo
        Vector3 realRobot = Vector3.zero;
        if (handController.robotEndEffector != null)
        {
            realRobot = handController.robotEndEffector.position;
        }

        int isTeleop = handController.isTeleoperating ? 1 : 0;

        string line = $"{currentTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{raw.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{raw.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{raw.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{filtered.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{filtered.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{filtered.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{safe.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{safe.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{safe.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{realRobot.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{realRobot.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{realRobot.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
                      $"{isTeleop}";

        writer.WriteLine(line);
    }

    void OnApplicationQuit()
    {
        if (writer != null)
        {
            writer.Close();
            Debug.Log("[DataLogger] Arquivo CSV salvo com sucesso.");
        }
    }
}








// using UnityEngine;
// using System.IO;
// using System.Text;

// public class DataLogger : MonoBehaviour
// {
//     [Header("Referências")]
//     public CartesianHandController handController;
    
//     [Header("Controle de Gravação")]
//     public bool isRecording = false;
//     public string fileName = "Experimento_Robo.csv";
    
//     private StreamWriter writer;
//     private float startTime;

//     void Start()
//     {
//         // Cria o arquivo na pasta "Assets" do seu projeto Unity
//         string filePath = Path.Combine(Application.dataPath, fileName);
//         writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
//         // Cria o cabeçalho das colunas no Excel
//         writer.WriteLine("Tempo(s),Mao_Bruta_X,Mao_Bruta_Y,Mao_Bruta_Z,Filtro_X,Filtro_Y,Filtro_Z,Cerca_X,Cerca_Y,Cerca_Z,Teleoperando");
//         Debug.Log($"[DataLogger] Arquivo CSV criado em: {filePath}");
//     }

//     void Update()
//     {
//         if (!isRecording || handController == null) return;

//         // Se acabou de começar a gravar, zera o cronômetro
//         if (startTime == 0f) startTime = Time.time;
//         float currentTime = Time.time - startTime;

//         // Coleta os dados
//         Vector3 raw = handController.rawHandPosition;
//         Vector3 filtered = handController.ghostCube.position;
//         Vector3 safe = handController.safeTargetPosition;
//         int isTeleop = handController.isTeleoperating ? 1 : 0;

//         // Escreve a linha no CSV (usando ponto para decimais para não confundir o Excel)
//         string line = $"{currentTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}," +
//                       $"{raw.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{raw.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{raw.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
//                       $"{filtered.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{filtered.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{filtered.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
//                       $"{safe.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{safe.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},{safe.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}," +
//                       $"{isTeleop}";

//         writer.WriteLine(line);
//     }

//     void OnApplicationQuit()
//     {
//         // Salva e fecha o arquivo quando você der o Stop no Unity
//         if (writer != null)
//         {
//             writer.Close();
//             Debug.Log("[DataLogger] Arquivo CSV salvo com sucesso.");
//         }
//     }
// }