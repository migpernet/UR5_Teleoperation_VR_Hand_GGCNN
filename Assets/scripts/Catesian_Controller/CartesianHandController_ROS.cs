/*
Arquivo 3: CartesianHandController_ROS.cs (Rede e UI)
Este arquivo lida estritamente com a publicação dos dados no ROS e com o feedback visual/sonoro do painel.
*/

using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Trajectory; 
using RosMessageTypes.BuiltinInterfaces; 

public partial class CartesianHandController
{
    void PlayClick() 
    { 
        if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound); 
    }

    void SendGripperCommand(float value)
    {
        JointTrajectoryMsg msg = new JointTrajectoryMsg();
        msg.joint_names = new string[] { gripperJointName };
        JointTrajectoryPointMsg point = new JointTrajectoryPointMsg();
        point.positions = new double[] { value };
        point.time_from_start = new DurationMsg(0, 500000000); 
        msg.points = new JointTrajectoryPointMsg[] { point };
        ros.Publish(gripperTopic, msg);
    }

    void PublishToROS()
    {
        if (robotBase == null) return; 
        PoseStampedMsg msg = new PoseStampedMsg();
        msg.header.frame_id = "base_link";

        Vector3 localPos = robotBase.InverseTransformPoint(ghostCube.position);
        Quaternion localRot = Quaternion.Inverse(robotBase.rotation) * ghostCube.rotation;

        msg.pose.position.x = localPos.z; 
        msg.pose.position.y = -localPos.x;
        msg.pose.position.z = localPos.y;

        msg.pose.orientation.x = -localRot.z;
        msg.pose.orientation.y = localRot.x; 
        msg.pose.orientation.z = -localRot.y;
        msg.pose.orientation.w = localRot.w;

        ros.Publish(poseTopic, msg);
    }

    void SetCubeColor(Color color) 
    { 
        if (cubeRenderer != null) cubeRenderer.material.color = color; 
    }

    void SetAllGizmosVisibility(bool showSpheresInPanel, bool showRingsOnRobot)
    {
        if (gizmoPitchX != null) gizmoPitchX.gameObject.SetActive(showSpheresInPanel);
        if (gizmoYawY != null) gizmoYawY.gameObject.SetActive(showSpheresInPanel);
        if (gizmoRollZ != null) gizmoRollZ.gameObject.SetActive(showSpheresInPanel);

        if (ringPitchX != null) ringPitchX.gameObject.SetActive(showRingsOnRobot);
        if (ringYawY != null) ringYawY.gameObject.SetActive(showRingsOnRobot);
        if (ringRollZ != null) ringRollZ.gameObject.SetActive(showRingsOnRobot);
    }

    void SetSingleGizmoVisibility(AxisLock axis, bool isVisible)
    {
        if (axis == AxisLock.X)
        {
            if (gizmoPitchX != null) gizmoPitchX.gameObject.SetActive(isVisible);
            if (ringPitchX != null) ringPitchX.gameObject.SetActive(isVisible);
        }
        else if (axis == AxisLock.Y)
        {
            if (gizmoYawY != null) gizmoYawY.gameObject.SetActive(isVisible);
            if (ringYawY != null) ringYawY.gameObject.SetActive(isVisible);
        }
        else if (axis == AxisLock.Z)
        {
            if (gizmoRollZ != null) gizmoRollZ.gameObject.SetActive(isVisible);
            if (ringRollZ != null) ringRollZ.gameObject.SetActive(isVisible);
        }
    }
}