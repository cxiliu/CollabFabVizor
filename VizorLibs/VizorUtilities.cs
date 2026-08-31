using System;
using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICD.VirtualRobot.Core;

namespace VizorLibs
{
    public enum TargetType
    {
        CompleteManual = 0,
        CompleteRobotic = 1,
        Hybrid = 2
    }

    public class VizorUtilities
    {

        /// <summary>
        /// Generate a multiplier for preset functions in the library
        /// Hard-coded values always assume meter as a unit. 
        /// </summary>
        /// <returns>multipler for converting numbers in the unit of meters</returns>
        public static float RhinoUnitMultiplier()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 1.0f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier = 1.0f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 0.01f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 0.001f;
                }
            }
            return multiplier;
        }

        public static string GetExecutionTypeFromName(string name)
        {
            string task_type;
            if (name.StartsWith("NS"))
            {
                task_type = "sequential_shared";
            }
            else if (name.StartsWith("NI"))
            {
                task_type = "sequential_individual";
            }
            else if (name.StartsWith("PS"))
            {
                task_type = "parallel_shared";
            }
            else if (name.StartsWith("PI"))
            {
                task_type = "parallel_individual";
            }
            else
            {
                task_type = "sequential_individual";
            }

            string[] results = name.Split('_');
            if (results.Length != 3)
            {
                return task_type;
            }
            else
            {
                int i = Convert.ToInt32(results[1]);
                if (i == 0)
                    return task_type;
                else
                    return task_type + "_" + i.ToString();
            }

        }

        public static string GetOperationFromRule(string displayRule, Device target)
        {
            bool isRobot = target is RobotObject;
            // TODO: remove the local flag
            switch (displayRule)
            {
                // changes color after each step
                case "session":
                    if (!isRobot)
                        return "session";
                    else
                        return "session-local";
                // disappears after each step
                case "step":
                    if (!isRobot)
                        return "step";
                    else
                        return "step-local";
                // disappears after each step
                case "flange":
                    if (!isRobot)
                        return "step";
                    else
                        return "step-local";
                // stays the same after each step
                case "persistent":
                    if (!isRobot)
                        return "persistent";
                    else
                        return "persistent-local";
                default:
                    if (displayRule.StartsWith("link:"))
                        return isRobot ? "step-local" : "step";
                    if (displayRule.StartsWith("add-"))
                        return displayRule;
                    return "session";
            }
        }

        /// <summary>
        /// Generate the layer for XR geometries
        /// </summary>
        /// <param name="displayRule">string input e.g. persistent, session, step</param>
        /// <param name="device">vizor device object </param>
        /// <returns></returns>
        public static string GetLayerFromRule(string displayRule, Device device)
        {
            string persistent;
            string session;
            string step;
            string flange;
            if (device is RobotObject) // following QR code prefab layers
            {
                RobotObject rob = (RobotObject)device;
                persistent = "Machines/" + device.name + "/platform_transform/g_persistent";
                session = "Machines/" + device.name + "/platform_transform/g_session";
                step = "Machines/" + device.name + "/platform_transform/g_step";
                flange = "Machines/" + device.name + "/platform_transform/" + String.Join("/", rob.joint_names);
            }
            else if (device is ARSystemDevice) // follows the live location of the particular AR device
            {
                persistent = "ARSpace/" + device.name + "/g_persistent";
                session = "ARSpace/" + device.name + "/g_session";
                step = "ARSpace/" + device.name + "/g_step";
                flange = "ARSpace/" + device.name + "/g_trajectory";
            }
            else if (device is InputDevice) // follows the QR code prefab layers
            {
                persistent = "ARSpace/" + device.name + "/platform_transform/g_persistent";
                session = "ARSpace/" + device.name + "/platform_transform/g_session";
                step = "ARSpace/" + device.name + "/platform_transform/g_step";
                flange = "ARSpace/" + device.name + "/g_trajectory";
            }
            else // ad-hoc obejcts are added by default to ARSpace
            {
                persistent = "ARSpace/" + device.name + "/g_persistent";
                session = "ARSpace/" + device.name + "/g_session";
                step = "ARSpace/" + device.name + "/g_step";
                flange = "ARSpace/" + device.name + "/g_trajectory";
            }
            switch (displayRule)
            {
                case "session":
                    return session;
                case "step":
                    return step;
                case "persistent":
                    return persistent;
                case "flange":
                    return flange;
                default:
                    if (displayRule.StartsWith("link:") && device is RobotObject)
                    {
                        RobotObject rob = (RobotObject)device;
                        string targetLink = displayRule.Substring(5);
                        int linkIndex = Array.IndexOf(rob.joint_names, targetLink);
                        if (linkIndex >= 0)
                            return "Machines/" + device.name + "/platform_transform/" +
                                   String.Join("/", rob.joint_names.Take(linkIndex + 1).ToArray());
                        return flange;
                    }
                    return session;
            }
        }

        public static bool IsLinkAttachedRule(string rule) =>
            rule == "flange" || rule.StartsWith("link:");

        // todo (extension): create a function for rule-based appearances
        // define a display rule and generate colors / abstract v.s. solid appearances programmatically
        // highlight / background / work element (can have their own rules)

        public static string GetTypeSummary(List<string> rules)
        {
            string types;
            if (rules.Count == 1)
            {
                types = rules[0];
            }
            else
            {
                foreach (string s in rules)
                {
                    if (s != rules[0])
                    {
                        types = "mixed";
                        break;
                    }
                }
                types = rules[0];
            }
            return types;
        }

        public static TargetType GetTargetType(List<Device> targets) // -1 for manual, 1 for robotic, 0 for hybrid
        {
            if (targets.Count == 1)
            {
                if (targets[0] is RobotObject) return TargetType.CompleteRobotic;
                return TargetType.CompleteManual;
            }
            else
            {
                int roboticTaskCount = 0;
                foreach (Device t in targets)
                {
                    if (t is RobotObject) roboticTaskCount += 1;
                }
                if (roboticTaskCount == targets.Count) return TargetType.CompleteRobotic;
                if (roboticTaskCount == 0) return TargetType.CompleteManual;
                return TargetType.Hybrid;
            }
        }

        //public static GeometryBase TransformVisualisation(GeometryBase input, RobotObject robot)
        //{
        //    GeometryBase copy = input.Duplicate();
        //    copy.Rotate(Math.PI * 0.5, Vector3d.ZAxis, robot.virtualRobotObject.RobRootFrame.Origin);
        //    copy.Translate(new Vector3d(new Point3d() - robot.virtualRobotObject.RobRootFrame.Origin));
        //    return copy;
        //}

        /// <summary>
        /// Expresses geometry in the frame its AR content is published in. Robot-anchored content is
        /// parented to platform_transform on the headset, which applies the robot's own base transform,
        /// so it has to be published base-local. Everything else is published in world coordinates.
        /// </summary>
        /// <param name="input">geometry in Rhino world coordinates</param>
        /// <param name="device">the device the geometry is anchored to</param>
        /// <returns>a copy of the input expressed in that device's content frame</returns>
        /// <summary>
        /// The transform that takes Rhino world coordinates into the content frame of a device.
        /// Identity for everything except robots, whose content is published base-local.
        /// </summary>
        /// <param name="device">the device the geometry is anchored to</param>
        /// <returns>the transform to apply, or identity if the device needs no rebasing</returns>
        private static Transform GetVisualisationTransform(Device device)
        {
            if (device is RobotObject)
            {
                RobotObject robot = (RobotObject)device;
                if (robot.virtualRobotObjectS is SphericalWrist6AxisRobot spherical)
                {
                    return Transform.PlaneToPlane(spherical.RobRootFrame, Plane.WorldXY);
                }
                else if (robot.virtualRobotObjectNS is NonSphericalWrist6AxisRobot nonspherical)
                {
                    return Transform.PlaneToPlane(nonspherical.BaseFrame, Plane.WorldXY);
                }
                //copy.Translate(new Vector3d(new Point3d() - robot.virtualRobotObject.RobRootFrame.Origin));
            }
            //copy.Rotate(-Math.PI * 0.5, Vector3d.ZAxis, new Point3d());
            return Transform.Identity;
        }

        public static Mesh TransformVisualisation(Mesh input, Device device)
        {
            Mesh copy = input.DuplicateMesh();
            copy.Transform(GetVisualisationTransform(device));
            return copy;
        }

        public static Plane TransformVisualisation(Plane input, Device device)
        {
            Plane copy = input.Clone();
            copy.Transform(GetVisualisationTransform(device));
            return copy;
        }

        public static Brep TransformVisualisation(Brep input, Device device)
        {
            Brep copy = input.DuplicateBrep();
            copy.Transform(GetVisualisationTransform(device));
            return copy;
        }

        public static Point3d[] TransformVisualisation(Point3d[] input, Device device)
        {
            if (input == null)
                return new Point3d[0];

            Transform xform = GetVisualisationTransform(device);
            Point3d[] copy = new Point3d[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                copy[i] = new Point3d(input[i]);
                copy[i].Transform(xform);
            }
            return copy;
        }
    }
}
