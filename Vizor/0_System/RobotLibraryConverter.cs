// This file contains utility methods for converting between different coordinate systems and robot models.

using ICD.VirtualRobot.Core;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using VizorLibs;
using VizorLibs.MessageTypes;

namespace Vizor._3_Robot
{
    /// <summary>  
    /// The RobotLibraryConverter class provides utility methods for converting between different coordinate systems  
    /// and robot models. It includes methods for scaling axis and frame values based on unit systems,  
    /// converting between URDF and VirtualRobot axis values, and transforming TCP frames to joint trajectory points  
    /// and vice versa. These utilities are essential for ensuring compatibility between different robot models  
    /// and coordinate systems in the Vizor framework.  
    /// </summary>
    
    public static class RobotLibraryConverter
    {
        /// <summary>
        /// Converts a Rhino plane to a pose message.
        /// </summary>
        //public static BuiltInMsg.Pose PlaneToPose(Plane plane)
        //{
        //    BuiltInMsg.Point point = new BuiltInMsg.Point((float)plane.Origin.X, (float)plane.Origin.Y, (float)plane.Origin.Z);
        //    Quaternion quat = Quaternion.Rotation(Plane.WorldXY, plane);
        //    BuiltInMsg.Quaternion quaternion = new BuiltInMsg.Quaternion((float)quat.B, (float)quat.C, (float)quat.D, (float)quat.A);
        //    return new BuiltInMsg.Pose(point, quaternion);
        //}
        public static BuiltInMsg.Pose PlaneToPose(Plane plane)
        {
            // rhino: x, y, z --> ros: y, -x, z
            // rhino: A, B, C, D --> ros: C, -B, D, A
            BuiltInMsg.Point point = new BuiltInMsg.Point((float)plane.Origin.Y, -(float)plane.Origin.X, (float)plane.Origin.Z);
            Quaternion quat = Quaternion.Rotation(Plane.WorldXY, plane);
            BuiltInMsg.Quaternion quaternion = new BuiltInMsg.Quaternion((float)quat.C, -(float)quat.B, (float)quat.D, (float)quat.A);
            return new BuiltInMsg.Pose(point, quaternion);
        }

        /// <summary>
        /// Scales linear axis values for the virtual robot model based on the active document's unit system. (Source unit is m. )
        /// </summary>
        public static float ScaleLinearAxisForVirtualRobotModel()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 1f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier =1000f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 100f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 1f;
                }
            }
            return multiplier;
        }

        /// <summary>
        /// Scales linear axis values for the world model based on the active document's unit system. (Target unit is m. )
        /// </summary>
        public static float ScaleLinearAxisForROSModel()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 1.0f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier = 0.001f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 0.01f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 1.0f;
                }
            }
            return multiplier;
        }

        /// <summary>
        /// Scales frame values for the world model based on the active document's unit system.
        /// </summary>
        public static float ScaleFrameForROSModel()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 0.001f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier = 1.0f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 0.1f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 0.001f;
                }
            }
            return multiplier;
        }

        /// <summary>
        /// Scales frame values for the virtual robot model based on the active document's unit system.
        /// </summary>
        public static float ScaleFrameForVirtualRobotModel()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 1f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier = 1f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 10f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 1000f;
                }
            }
            return multiplier;
        }

        /// <summary>
        /// Converts URDF based axis values to VirtualRobot axis values (linear axis values are at the start).
        /// </summary>
        /// <param name="values">The list of axis values to convert.</param>
        /// <param name="name">The name of the robot model.</param>
        /// <returns>A list of converted axis values.</returns>
        public static List<Double> ToVirtualRobotAxisValues(List<Double> values, string name)
        {
            List<Double> angles = new List<Double>();
            if (values.Count == 6)
            {
                if (name == "UR10")
                {
                    angles = values.Select(v => v).ToList();
                    //angles[0] = angles[0] - 180;
                }
                else
                {
                    // KUKA offset 
                    angles.Add(-values[0]);
                    angles.Add(values[1] - 90);
                    angles.Add(values[2] + 90);
                    angles.Add(-values[3]);
                    angles.Add(values[4]);
                    angles.Add(-values[5]);
                }
            }
            else if (values.Count == 7)
            {
                if (name == "Tintin")
                {
                    angles.Add(values[0] * ScaleLinearAxisForVirtualRobotModel());
                    angles.Add(-values[1]);
                    angles.Add(values[2] - 90);
                    angles.Add(values[3] + 90);
                    angles.Add(-values[4]);
                    angles.Add(values[5]);
                    angles.Add(-values[6]);
                }
                else if (name == "Timberley")
                {
                    angles.Add(values[0] * ScaleLinearAxisForVirtualRobotModel());
                    angles.Add(-values[1]-90); // only diff in timberley
                    angles.Add(values[2]-90);
                    angles.Add(values[3]+90);
                    angles.Add(-values[4]);
                    angles.Add(values[5]);
                    angles.Add(-values[6]);
                }
                else
                {
                    // calibrate other models as it is fit
                    angles.Add(values[0] * ScaleLinearAxisForVirtualRobotModel());
                    angles.Add(-values[1] - 90); // only diff in timberley
                    angles.Add(values[2] - 90);
                    angles.Add(values[3] + 90);
                    angles.Add(-values[4]);
                    angles.Add(values[5]);
                    angles.Add(-values[6]);
                }
            }
            return angles;
        }

        /// <summary>
        /// Converts VirtualRobot axis values to URDF based axis values (linear axis values at the start).
        /// </summary>
        /// <param name="values">The list of axis values to convert.</param>
        /// <returns>An array of converted axis values.</returns>
        public static float[] ToROSAxisValues(List<Double> values, string name)
        {
            float[] angles = new float[values.Count];
            if (values.Count == 6)
            {
                if (name == "UR10")
                {
                    angles = values.Select(v => (float)v).ToArray();
                }
                else
                {
                    // KUKA offset 
                    angles[0] = (float)-values[0];
                    angles[1] = (float)(values[1] + 90);
                    angles[2] = (float)(values[2] - 90);
                    angles[3] = (float)(-values[3]);
                    angles[4] = (float)(values[4]);
                    angles[5] = (float)(-values[5]);
                }
            }
            else if (values.Count == 7)
            {
                angles[0] = (float)values[0] * ScaleLinearAxisForROSModel();
                angles[1] = (float)-values[1];
                angles[2] = (float)(values[2] + 90);
                angles[3] = (float)(values[3] - 90);
                angles[4] = (float)(-values[4]);
                angles[5] = (float)(values[5]);
                angles[6] = (float)(-values[6]);
            }
            return angles;
        }

        /// <summary>
        /// Converts TCP frames first to VirtualRobot axis values and then to JointTrajectoryPoints.
        /// </summary>
        /// <param name="frames">The list of frames to convert.</param>
        /// <param name="robot">The robot object.</param>
        /// <returns>A list of joint trajectory points.</returns>
        public static List<float[]> ghFramesToJointTrajectory(List<Plane> frames, RobotObject robot)
        {
            List<float[]> jointTrajectoryPoints = new List<float[]>();
            float multiplier = ScaleFrameForVirtualRobotModel();

            for (int i = 0; i < frames.Count; i++)
            {
                // use virtual robot ik to turn frame into axis values
                Plane tcp = new Plane(frames[i]);

                List<Double> angles = null;
                if (robot.virtualRobotObjectS is SphericalWrist6AxisRobot spherical)
                {
                    tcp.Transform(Transform.Scale(spherical.RobRootFrame, multiplier, multiplier, multiplier)); // this ignores rotations, ik itself already considers base frame
                    //tcp.Transform(Transform.Scale(Plane.WorldXY, multiplier, multiplier, multiplier));
                    //tcp.Transform(Transform.PlaneToPlane(Plane.WorldXY, spherical.RobRootFrame));
                    angles = spherical.ComputeAxisValuesFromTargetTcpFrame(tcp, robot.axisToggles);
                }
                else if (robot.virtualRobotObjectNS is NonSphericalWrist6AxisRobot nonSpherical)
                {
                    tcp.Transform(Transform.Scale(nonSpherical.BaseFrame, multiplier, multiplier, multiplier)); // this ignores rotations, ik itself already considers base frame
                    //tcp.Transform(Transform.Scale(Plane.WorldXY, multiplier, multiplier, multiplier));
                    //tcp.Transform(Transform.PlaneToPlane(Plane.WorldXY, nonSpherical.BaseFrame));
                    angles = nonSpherical.ComputeAxisValuesFromTargetTcpFrame(tcp, robot.axisToggles);
                }

                //tcp.Transform(Transform.Scale(robot.virtualRobotObject.RobRootFrame, multiplier, multiplier, multiplier));
                //List<Double> angles = robot.virtualRobotObject.ComputeAxisValuesFromTargetTcpFrame(tcp, robot.axisToggles);

                // convert VR axis values into URDF based axis values
                if ((angles != null) && (!Double.IsNaN(angles[0])))
                {
                    float[] positions = ToROSAxisValues(angles, robot.name);
                    jointTrajectoryPoints.Add(positions);
                }
            }
            return jointTrajectoryPoints;
        }

        /// <summary>
        /// Converts joint trajectory points to Grasshopper frames.
        /// </summary>
        /// <param name="trajectoryPoints">The list of trajectory points to convert.</param>
        /// <param name="robot">The robot object.</param>
        /// <returns>A list of Grasshopper frames.</returns>
        public static List<Plane> jointTrajectoryToGhFrames(List<float[]> trajectoryPoints, RobotObject robot)
        {
            List<Plane> tcpFrames = new List<Plane>();
            List<float[]> angularTrajectoryPoints = new List<float[]>();

            // nothing to convert: callers treat an empty frame list as "no path"
            if ((trajectoryPoints == null) || (trajectoryPoints.Count == 0)) return tcpFrames;

            int num_of_axis = trajectoryPoints[0].Length;

            for (int i = 0; i < trajectoryPoints.Count; i++)
            {
                // convert URDF based axis values into VR axis values
                List<Double> axis_values = new List<Double>();
                foreach (float f in trajectoryPoints[i])
                    axis_values.Add(f);
                axis_values = ToVirtualRobotAxisValues(axis_values, robot.name);

                // get tcp from axis values
                Plane tcp = new Plane();
                List<Plane> axis_frames = new List<Plane>();
                Double linear_axis = 0;
                if (num_of_axis == 6)
                {
                    if (robot.virtualRobotObjectS is SphericalWrist6AxisRobot spherical)
                    {
                        spherical.ComputePoseFromAxisValues(axis_values, out tcp, out axis_frames);
                    }
                    else if (robot.virtualRobotObjectNS is NonSphericalWrist6AxisRobot nonSpherical)
                    {
                        nonSpherical.ComputePoseFromAxisValues(axis_values, out tcp);
                    }
                    //robot.virtualRobotObject.ComputePoseFromAxisValues(axis_values, out tcp, out axis_frames);

                }
                else if (num_of_axis == 7)
                {
                    linear_axis = axis_values[0];
                    axis_values.RemoveAt(0);
                    if (robot.virtualRobotObjectS is SphericalWrist6AxisRobot spherical)
                    {
                        spherical.ComputePoseFromAxisValues(axis_values, out tcp, out axis_frames);
                    }
                    else if (robot.virtualRobotObjectNS is NonSphericalWrist6AxisRobot nonSpherical)
                    {
                        nonSpherical.ComputePoseFromAxisValues(axis_values, out tcp);
                    }
                    //robot.virtualRobotObject.ComputePoseFromAxisValues(axis_values, out tcp, out axis_frames);
                }

                // scale tcp based on model units
                float multiplier = ScaleFrameForROSModel();

                if (robot.virtualRobotObjectS is SphericalWrist6AxisRobot sphericalRobot)
                {
                    tcp.Transform(Transform.PlaneToPlane(sphericalRobot.RobRootFrame, Plane.WorldXY));
                }
                else if (robot.virtualRobotObjectNS is NonSphericalWrist6AxisRobot nonSphericalRobot)
                {
                    tcp.Transform(Transform.PlaneToPlane(nonSphericalRobot.BaseFrame, Plane.WorldXY));
                }
                //tcp.Transform(Transform.PlaneToPlane(robot.virtualRobotObject.RobRootFrame, Plane.WorldXY));

                tcp.Transform(Transform.Scale(Plane.WorldXY, multiplier, multiplier, multiplier));
                if (num_of_axis == 7)
                {
                    tcp.Transform(Transform.Translation(0, linear_axis * multiplier * 1000, 0));
                    //tcp.Transform(Transform.Rotation(-Math.PI * 0.5, robot.virtualRobotObject.RobRootFrame.Origin));

                }
                tcpFrames.Add(tcp);
            }
            return tcpFrames;
        }
    }
}