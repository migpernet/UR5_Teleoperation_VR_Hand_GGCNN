using UnityEngine;

public class UR5_Robotiq2F140_Mimic : MonoBehaviour
{
    [Header("Joint principal")]
    public ArticulationBody leftOuterKnuckle;  

    [Header("Multiplier = -1")]
    public ArticulationBody leftInnerKnuckle;
    public ArticulationBody rightOuterKnuckle;
    public ArticulationBody rightInnerKnuckle;

    [Header("Multiplier = +1")]
    public ArticulationBody leftInnerFinger;
    public ArticulationBody rightInnerFinger;

    void FixedUpdate()
    {
        if (leftOuterKnuckle == null) return;

        // posição da junta principal (rad)
        float q = leftOuterKnuckle.jointPosition[0];

        // mimic com multiplier -1
        SetJoint(leftInnerKnuckle, -q);
        SetJoint(rightOuterKnuckle, -q); //
        SetJoint(rightInnerKnuckle, -q); //

        // mimic com multiplier +1
        SetJoint(leftInnerFinger, q);
        SetJoint(rightInnerFinger, q); //
    }

    void SetJoint(ArticulationBody joint, float rad)
    {
        if (joint == null) return;

        var drive = joint.xDrive;
        drive.target = rad * Mathf.Rad2Deg; // ArticulationBody usa graus
        joint.xDrive = drive;
    }
}
