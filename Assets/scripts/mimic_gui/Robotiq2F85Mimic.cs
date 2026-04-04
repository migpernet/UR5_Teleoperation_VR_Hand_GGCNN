using UnityEngine;

public class Robotiq2F85Mimic : MonoBehaviour
{
    [Header("Joint principal")]
    public ArticulationBody leftKnuckle;

    [Header("Juntas que seguem (mimic direto)")]
    public ArticulationBody rightKnuckle;
    public ArticulationBody leftInnerKnuckle;
    public ArticulationBody rightInnerKnuckle;

    [Header("Juntas com inversão")]
    public ArticulationBody leftFingerTip;
    public ArticulationBody rightFingerTip;

    void FixedUpdate()
    {
        float q = leftKnuckle.jointPosition[0]; // posição da joint principal (rad)

        SetJoint(rightKnuckle, q);
        SetJoint(leftInnerKnuckle, q);
        SetJoint(rightInnerKnuckle, q);

        SetJoint(leftFingerTip, -q);
        SetJoint(rightFingerTip, -q);
    }

    void SetJoint(ArticulationBody joint, float target)
    {
        if (joint == null) return;

        var drive = joint.xDrive;
        drive.target = Mathf.Rad2Deg * target; // Articulation usa graus
        joint.xDrive = drive;
    }
}
