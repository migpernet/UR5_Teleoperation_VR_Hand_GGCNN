using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Trajectory;
using System.Collections;
using System.Linq;

/// <summary>
/// Subscriber dedicado para receber a lista de JointTrajectoryPoints para execução de movimento suave (Reset).
/// </summary>
public class ROSTrajectorySubscriber : MonoBehaviour
{
    [Header("ROS Trajectory Configuration")]
    public string trajectoryTopic = "/reset_trajectory_points"; // Tópico que o nó Python publica

    // Referências necessárias
    public UR5JointSimulator simulator; 
    public UR5JointTeleopVR vrController; 

    private ROSConnection ros;
    
    // A flag para evitar que o FixedUpdate() publique comandos enquanto o reset está ativo
    public bool isExecutingSmoothReset = false; 

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // 1. Registra o Subscriber
        ros.Subscribe<JointTrajectoryMsg>(trajectoryTopic, OnTrajectoryReceived);
        Debug.Log($"[Trajectory Subscriber] Escutando o tópico: {trajectoryTopic}");
    }

    private void OnTrajectoryReceived(JointTrajectoryMsg msg)
    {
        if (msg.points.Length == 0) return;
        
        // Assume o controle para executar a trajetória
        isExecutingSmoothReset = true;
        
        // Desabilita o Input VR, se não estiver desabilitado (segurança)
        if(vrController != null) vrController.inputActions.XRControl.Disable();

        // Inicia a corotina para aplicar os pontos da trajetória suave
        StartCoroutine(ExecuteSmoothTrajectory(msg));
    }


    IEnumerator ExecuteSmoothTrajectory(JointTrajectoryMsg msg)
    {
        if (simulator == null || msg.points.Length < 2)
        {
            // Se houver apenas 1 ponto (ou nenhum), não há trajetória suave para interpolar.
            FinishSmoothReset();
            yield break;
        }

        // [Setup de Início]
        isExecutingSmoothReset = true;
        
        float startTime = Time.time;
        // Pega a duração total do último ponto da trajetória.
        float totalDuration = (float)msg.points.Last().time_from_start.sec + (float)msg.points.Last().time_from_start.nanosec / 1e9f;
        float timeElapsed = 0f;
        
        // [Loop Principal: Interpolação]
        while (timeElapsed < totalDuration)
        {
            timeElapsed = Time.time - startTime;
            
            int currentPointIndex = -1;
            
            // 1. Encontra o segmento de tempo (ponto anterior e próximo) para interpolação
            // O loop busca o ponto 'i' onde 'timeElapsed' se encontra entre 'i-1' e 'i'.
            for (int i = 1; i < msg.points.Length; i++)
            {
                float pointTime = (float)msg.points[i].time_from_start.sec + (float)msg.points[i].time_from_start.nanosec / 1e9f;
                if (timeElapsed < pointTime)
                {
                    currentPointIndex = i;
                    break;
                }
            }

            if (currentPointIndex > 0)
            {
                var prevPoint = msg.points[currentPointIndex - 1];
                var nextPoint = msg.points[currentPointIndex];

                // 2. Extrai os tempos e calcula a taxa de interpolação 't' (0.0 a 1.0)
                float prevTime = (float)prevPoint.time_from_start.sec + (float)prevPoint.time_from_start.nanosec / 1e9f;
                float nextTime = (float)nextPoint.time_from_start.sec + (float)nextPoint.time_from_start.nanosec / 1e9f;

                float segmentDuration = nextTime - prevTime;
                float timeIntoSegment = timeElapsed - prevTime;
                float t = Mathf.Clamp01(timeIntoSegment / segmentDuration); // Taxa de 0 a 1

                // 3. Aplica o Lerp para todas as 7 juntas
                float[] interpolatedPositions = new float[7];
                for (int j = 0; j < 7; j++)
                {
                    float startRad = (float)prevPoint.positions[j];
                    float endRad = (float)nextPoint.positions[j];
                    // Interpolação linear da posição atual (Rad)
                    interpolatedPositions[j] = Mathf.Lerp(startRad, endRad, t);
                }

                ApplyPositionsToSimulator(interpolatedPositions);
            }
            
            yield return null; // CRUCIAL: Espera pelo próximo frame para avançar no tempo
        }
        
        // [Finalização]
        // 4. Garante que o ponto final (a meta do reset) seja aplicado perfeitamente.
        ApplyPositionsToSimulator(msg.points.Last().positions.Select(p => (float)p).ToArray());

        FinishSmoothReset();
    }



    private void ApplyPositionsToSimulator(float[] positionsRad)
    {
        // Esta função aplica os valores de RADIANOS diretamente nas juntas do simulador
        if (positionsRad.Length != 7) return;

        // UR5 (6 Juntas)
        for (int i = 0; i < 6; i++)
        {
            var drive = simulator.robotJoints[i].xDrive;
            // Converte RADIANOS (da Trajetória ROS) para GRAUS (Unity ArticulationBody)
            drive.target = positionsRad[i] * Mathf.Rad2Deg; 
            simulator.robotJoints[i].xDrive = drive;
        }

        // Garra (1 Junta)
        if (simulator.gripperJoint != null)
        {
            var drive = simulator.gripperJoint.xDrive;
            // Garra já está em unidade ROS (0.0-0.8)
            drive.target = positionsRad[6]; 
            simulator.gripperJoint.xDrive = drive;
        }
    }
    
    private void FinishSmoothReset()
    {
        isExecutingSmoothReset = false;
        
        // Reativa o input VR e sincroniza o controlador
        if (vrController != null)
        {
            vrController.inputActions.XRControl.Enable();
            
            // Sincroniza a pose final com o controlador VR, lendo o último ponto.
            // (Assumimos que o último ApplyPositionsToSimulator deixou a pose correta).
            if (vrController.jointSubscriber != null)
            {
                vrController.jointSubscriber.TriggerInitialSync(); 
            }
        }
        
        Debug.Log("[Espelhamento de Trajetória] Reset Suave concluído. Controle VR reativado.");
    }
}