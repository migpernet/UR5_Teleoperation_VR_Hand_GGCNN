using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using System.Collections; // NECESSÁRIO PARA A ESPERA DE FÍSICA

public class UR5JointSubscriber : MonoBehaviour
{
    [Header("Configurações do ROS")]
    public string topicName = "/ur5/joint_states";
    private ROSConnection ros;

    [System.Serializable]
    public class RobotJoint
    {
        public string rosJointName; 
        public GameObject unityJoint; 
        
        [Header("Ajustes Manuais")]
        public Vector3 rotationAxis = new Vector3(0, 0, 1); 
        public bool invertDirection = false;
        
        [HideInInspector] public Quaternion initialRotation;
        [HideInInspector] public ArticulationBody articulationBody;
    }

    [Header("Mapeamento das Juntas")]
    public RobotJoint[] joints;

    [Header("Sincronização Bidirecional (Ghost Cube)")]
    public Transform toolLink; 
    public Transform ghostCube; 
    public bool forceSyncToRobot = false; 

    // --- TRAVA DE INICIALIZAÇÃO ---
    private bool isFirstSync = true;
    private CartesianHandController handController;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<JointStateMsg>(topicName, JointStateCallback);

        handController = FindObjectOfType<CartesianHandController>();

        foreach (var joint in joints)
        {
            if (joint.unityJoint != null)
            {
                joint.initialRotation = joint.unityJoint.transform.localRotation;
                joint.articulationBody = joint.unityJoint.GetComponent<ArticulationBody>();
            }
        }
    }

    void JointStateCallback(JointStateMsg msg)
    {
        for (int i = 0; i < msg.name.Length; i++)
        {
            string incomingName = msg.name[i];
            float incomingAngleRad = (float)msg.position[i];

            foreach (var joint in joints)
            {
                if (joint.rosJointName == incomingName && joint.unityJoint != null)
                {
                    float angleDeg = incomingAngleRad * Mathf.Rad2Deg;

                    if (joint.articulationBody != null)
                    {
                        var drive = joint.articulationBody.xDrive;
                        drive.target = joint.invertDirection ? -angleDeg : angleDeg;
                        joint.articulationBody.xDrive = drive;

                        // Teletransporte mecânico imediato
                        if (isFirstSync)
                        {
                            float radTarget = joint.invertDirection ? -incomingAngleRad : incomingAngleRad;
                            joint.articulationBody.jointPosition = new ArticulationReducedSpace(radTarget);
                            joint.articulationBody.jointVelocity = new ArticulationReducedSpace(0f);
                        }
                    }
                    else
                    {
                        if (joint.invertDirection) angleDeg = -angleDeg;
                        joint.unityJoint.transform.localRotation = joint.initialRotation * Quaternion.AngleAxis(angleDeg, joint.rotationAxis);
                    }
                    break; 
                }
            }
        }

        // --- A GRANDE MUDANÇA ESTÁ AQUI ---
        // Se for a primeira leitura, nós NÃO avisamos o controlador imediatamente.
        // Nós iniciamos uma rotina de espera da engine de física.
        if (isFirstSync)
        {
            isFirstSync = false;
            StartCoroutine(WaitPhysicsAndSync());
        }
    }

    // Rotina que aguarda a física do Unity calcular a posição final antes de colar o cubo
    private IEnumerator WaitPhysicsAndSync()
    {
        // Espera DOIS ciclos completos da engine de física (FixedUpdate)
        // Isso garante que o Unity já moveu as malhas 3D (Transforms) para os novos ângulos
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate(); 

        if (handController != null)
        {
            handController.ForceSyncGhostCube();
        }
    }

    void LateUpdate()
    {
        if (forceSyncToRobot && toolLink != null && ghostCube != null)
        {
            ghostCube.position = toolLink.position;
            ghostCube.rotation = toolLink.rotation;
        }
    }
}









