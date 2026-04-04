using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using System.IO;
using System.Collections.Generic;
using System.Globalization;

public class LatencyTester : MonoBehaviour
{
    ROSConnection ros;
    public string topicPing = "/latency_ping";
    public string topicEcho = "/latency_echo";

    // Configuração
    public int numberOfSamples = 1000; // Quantidade de amostras para o artigo
    public float pingInterval = 0.05f; // 20Hz (similar à sua teleoperação)

    private float lastPingTime = 0;
    private int samplesCollected = 0;
    private List<double> latencies = new List<double>(); // Em milissegundos
    private bool isTesting = false;

    // Dicionário para guardar o tempo de envio de cada pacote
    // Usamos o próprio valor do timestamp como chave ou identificador
    private double currentSentTimestamp;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Float64Msg>(topicPing);
        ros.Subscribe<Float64Msg>(topicEcho, OnEchoReceived);
        
        Debug.Log("Pressione SPACE para iniciar o teste de latência.");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !isTesting)
        {
            StartTest();
        }

        if (isTesting && samplesCollected < numberOfSamples)
        {
            if (Time.time - lastPingTime > pingInterval)
            {
                SendPing();
                lastPingTime = Time.time;
            }
        }
    }

    void StartTest()
    {
        isTesting = true;
        samplesCollected = 0;
        latencies.Clear();
        Debug.Log("Iniciando coleta de latência...");
    }

    void SendPing()
    {
        // Pega o tempo atual de alta precisão (em segundos)
        // Time.realtimeSinceStartup é melhor que Time.time para benchmarks
        double now = Time.realtimeSinceStartup;
        
        Float64Msg msg = new Float64Msg { data = now };
        ros.Publish(topicPing, msg);
    }

    void OnEchoReceived(Float64Msg msg)
    {
        if (!isTesting) return;

        double now = Time.realtimeSinceStartup;
        double sentTime = msg.data;

        // Calcula RTT (Round Trip Time) em Milissegundos
        double rtt_ms = (now - sentTime) * 1000.0;

        latencies.Add(rtt_ms);
        samplesCollected++;

        if (samplesCollected % 100 == 0)
        {
            Debug.Log($"Amostras: {samplesCollected}/{numberOfSamples} | Última: {rtt_ms:F2}ms");
        }

        if (samplesCollected >= numberOfSamples)
        {
            FinishTest();
        }
    }

    void FinishTest()
    {
        isTesting = false;
        SaveToCSV();
        Debug.Log("Teste Concluído! Arquivo salvo.");
    }

    void SaveToCSV()
    {
        string path = Application.dataPath + "/latency_results.csv";
        using (StreamWriter writer = new StreamWriter(path))
        {
            writer.WriteLine("SampleID,Latency_ms");
            for (int i = 0; i < latencies.Count; i++)
            {
                // Usa CultureInfo.InvariantCulture para garantir ponto em vez de vírgula
                writer.WriteLine($"{i},{latencies[i].ToString(CultureInfo.InvariantCulture)}");
            }
        }
        Debug.Log($"Dados salvos em: {path}");
    }
}