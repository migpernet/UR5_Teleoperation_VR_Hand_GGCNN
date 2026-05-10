using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System.Collections.Generic;

public class BimanualGestureSelector : MonoBehaviour
{
    [Header("Referência do Seletor")]
    [Tooltip("Arraste o objeto que contém o script PointCloudSelector")]
    public PointCloudSelector pointCloudSelector;

    private XRHandSubsystem handSubsystem;
    private bool wasThumbsUp = false;

    void Start()
    {
        // Procura o subsistema do XR Hands a rodar no Unity
        var subsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetInstances(subsystems);
        if (subsystems.Count > 0)
        {
            handSubsystem = subsystems[0];
        }
        else
        {
            Debug.LogError("[GGCNN] XRHandSubsystem não encontrado. Verifique se o XR Hands está ativo.");
        }
    }

    void Update()
    {
        if (handSubsystem == null || !handSubsystem.running) return;

        // Monitoriza a Mão Esquerda
        var leftHand = handSubsystem.leftHand;
        if (!leftHand.isTracked) return;

        // Verifica se o gesto atual é um "Fixe / Thumbs Up"
        bool isThumbsUp = CheckThumbsUp(leftHand);

        // Se fez o gesto agora (gatilho de borda de subida para não disparar 1000 vezes por segundo)
        if (isThumbsUp && !wasThumbsUp)
        {
            Debug.Log("[GGCNN] Gesto 'Legal' detectado na Mão Esquerda! Disparando seleção...");
            if (pointCloudSelector != null)
            {
                pointCloudSelector.ExecuteSelection();
            }
        }

        wasThumbsUp = isThumbsUp; // Atualiza o estado para o próximo frame
    }

    private bool CheckThumbsUp(XRHand hand)
    {
        // Captura a posição dos ossos chave
        var wrist = hand.GetJoint(XRHandJointID.Wrist);
        var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
        var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);

        if (!wrist.TryGetPose(out Pose wristPose) ||
            !thumbTip.TryGetPose(out Pose thumbPose) ||
            !indexTip.TryGetPose(out Pose indexPose) ||
            !middleTip.TryGetPose(out Pose middlePose))
        {
            return false; // Se a mão estiver ocluída e perder o tracking, cancela
        }

        // REGRA 1: Os dedos Indicador e Médio devem estar fechados (distância curta até ao pulso)
        // Valores em metros (0.1f = 10 cm). Ajuste se a sua mão for maior/menor no avatar
        float indexDist = Vector3.Distance(wristPose.position, indexPose.position);
        float middleDist = Vector3.Distance(wristPose.position, middlePose.position);
        bool fingersClosed = indexDist < 0.1f && middleDist < 0.1f;

        // REGRA 2: O Polegar deve estar esticado (distância longa até ao pulso)
        float thumbDist = Vector3.Distance(wristPose.position, thumbPose.position);
        bool thumbExtended = thumbDist > 0.11f;

        // REGRA 3: O Polegar deve estar fisicamente acima do pulso (apontando para o teto)
        bool pointingUp = thumbPose.position.y > (wristPose.position.y + 0.04f);

        // Só retorna VERDADEIRO se todas as 3 regras matemáticas forem cumpridas
        return fingersClosed && thumbExtended && pointingUp;
    }
}