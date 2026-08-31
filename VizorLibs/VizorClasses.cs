using System.Collections.Generic;
using Rhino.Geometry;
using VizorLibs.MessageTypes;
using ICD.VirtualRobot.Core;
using System;
using System.Linq;

namespace VizorLibs
{
    
    #region Devices
    /// <summary>
    /// A generic device object, with websocket communication capability
    /// </summary>
    public class Device
    {
        public string name { get; set; }
        public WsObject wscObj { get; set; }
        public bool IsSameAs(Device device)
        {
            if ((this != null) && (device.name == this.name) && (device.wscObj == this.wscObj)) return true;
            else return false;
        }
    }

    /// <summary>
    /// Some other networked device
    /// </summary>
    public class InputDevice : Device
    {
        public string type = "AR Input Client";
    }

    // TODO: rename this to something better
    public class ARSystemDevice : Device
    {
        public string type = "AR Client";
    }

    /// <summary>
    /// Robot object
    /// </summary>
    public class RobotObject : Device
    {
        public string type = "Machine Client";
        //public object virtualRobotObject { get; set; }
        public NonSphericalWrist6AxisRobot virtualRobotObjectNS { get; set; }
        public SphericalWrist6AxisRobot virtualRobotObjectS { get; set; }
        public List<bool> axisToggles { get; set; }
        public string[] joint_names { get; set; }
    }

    #endregion

    #region Visual Elements

    public class SceneContentObject
    {
        public string name { get; set; }
        public string operation { get; set; }
        public SceneGeometryObject[] geomObjects { get; set; }
        public SceneWireframeObject[] wireObjects { get; set; }
        public SceneTextObject[] textObjects { get; set; }
        public int LoD { get; set; }

        public SceneContentMsg GetSceneContentMsg()
        {
            return new SceneContentMsg
            {
                name = this.name,
                operation = this.operation, 
                geometries = Array.ConvertAll(this.geomObjects, x => x.GetSceneGeometryMsg()),
                wires = Array.ConvertAll(this.wireObjects, x => x.GetSceneWireframeMsg()),
                texts = Array.ConvertAll(this.textObjects, x => x.GetSceneTextMsg()),
                LoD = this.LoD
            };
        }
    }

    /// <summary>
    /// General geometry operation object that holding meshes for mesh-renderers in unity
    /// Converts to vizor msg format "SceneGeometryMsg"
    /// </summary>
    public class SceneGeometryObject
    {
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; } // color of the mesh
        public Mesh gMesh { get; set; } // mesh object

        public static SceneGeometryObject WokrAreaGeometry(WorkAreaObject wao)
        {
            return new SceneGeometryObject
            {
                operation = "persistent",
                layer = VizorUtilities.GetLayerFromRule("persistent", wao.target),
                name = wao.target.name + " area " + wao.id.ToString(),
                material = "(255,0,0,255)",
                gMesh = Mesh.CreateFromBrep(Brep.CreatePlanarBreps(wao.rectangle.ToNurbsCurve(), 0.001)[0], new MeshingParameters(0.01, 0.001))[0],
            };
        }

        public SceneGeometryMsg GetSceneGeometryMsg()
        {
            return new SceneGeometryMsg
            {
                operation = this.operation,
                layer = this.layer,
                name = this.name,
                material = this.material,
                mesh = MessageTypes.MsgDataConverter.ghMeshToMsg(this.gMesh),
            };
        }
    }

    /// <summary>
    /// General wireframe object holding wires for line-renderers in unity
    /// Converts to vizor msg format "SceneWireframeMsg"
    /// </summary>
    public class SceneWireframeObject
    {
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; } // color of the wireframe
        public float width { get; set; } // thickness of the wireframe
        public Point3d[] points { get; set; } // points defining where to draw wires

        public SceneWireframeMsg GetSceneWireframeMsg ()
        {
            return new SceneWireframeMsg
            {
                operation = this.operation,
                layer = this.layer,
                name = this.name,
                material = this.material,
                width = this.width,
                points = MessageTypes.MsgDataConverter.ghEdgesToMsg(this.points),
            };
        }

    }

    /// <summary>
    /// General text operation object that holds visualisation features for unity
    /// Converts to vizor msg format "SceneTextMsg"
    /// </summary>
    public class SceneTextObject
    {
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; } // color of the text / background
        public Plane plane{ get; set; } // plane to orient the text
        public string text { get; set; } // text content
        public bool solid { get; set; } // solid v.s. panel (default solid)

        public SceneTextMsg GetSceneTextMsg()
        {
            return new SceneTextMsg
            {
                operation = this.operation,
                layer = this.layer,
                name = this.name,
                material = this.material,
                transform = MsgDataConverter.PlaneToTransform(this.plane),
                text = this.text,
            };
        }
    }

    #endregion

    #region Robot Elements

    /// <summary>
    /// General robot trajectory object handling visualisation features for unity
    /// Converts to vizor msg format "RobotTrajectoryMsg"
    /// </summary>
    public class RobotTrajectoryObject
    {
        public RobotObject robot { get; set; }
        public List<float[]> joint_trajectory { get; set; } // unity format
        public List<Plane> gTrajectoryFrames { get; set; } // gh format
        public Mesh gMesh { get; set; }

        public BuiltInMsg.JointTrajectoryMsg GetRobotTrajectoryMsg()
        {
            BuiltInMsg.JointTrajectoryPoint[] jointTrajectoryPoints = new BuiltInMsg.JointTrajectoryPoint[joint_trajectory.Count];
            for (int i = 0; i < joint_trajectory.Count; i++)
            {
                float[] values = joint_trajectory[i];

                if (values != null)
                {
                    jointTrajectoryPoints[i] = new BuiltInMsg.JointTrajectoryPoint(values);
                }
            }
            return new BuiltInMsg.JointTrajectoryMsg(robot.joint_names, jointTrajectoryPoints);
        }

        public static List<float[]> MsgToJointTrajectory (PlannedTrajectoryMsg msg)
        {
            List<float[]> joint_trajectory = new List<float[]>();
            int num_of_pts = msg.joint_trajectory.points.Length;
            int num_of_axis = msg.joint_trajectory.points[0].positions.Length;
            int MAX_POINTS_IN_TRAJ = 40;
            //int MAX_POINTS_IN_TRAJ = 20;
            int step = 1;
            if (num_of_pts > MAX_POINTS_IN_TRAJ)
                step = (int) (num_of_pts / MAX_POINTS_IN_TRAJ);
            for (int i = 0; i < num_of_pts; i+=step)
            {
                float[] values = msg.joint_trajectory.points[i].positions;
                if (num_of_axis == 6)
                {
                    joint_trajectory.Add(Array.ConvertAll(values, x => (float)(x * 180 / Math.PI)));
                }
                else
                {
                    float[] newvalues = new float[num_of_axis];
                    for (int j = 0; j < num_of_axis; j++) {
                        // linear axis values
                        if (j < (num_of_axis - 6))
                            newvalues[j] = values[j];
                        // 6-axis values
                        else
                            newvalues[j] = (float)(values[j] * 180 / Math.PI); 
                    }
                    joint_trajectory.Add(newvalues);
                }
            }
            return joint_trajectory;
        }

    }


    /// <summary>
    /// Safety zone object that holds visualisation of the danger zone
    /// Converts to vizor msg format "SafetyZoneMsg"
    /// </summary>
    public class SafetyZoneObject
    {
        public string identifier { get; set; }
        public int[] zone_ids { get; set; }
        public float alert_distance { get; set; }
        public SceneGeometryObject[] boundaries{ get; set; }
        public SafetyZoneMsg GetSafetyZoneMsg()
        {
            return new SafetyZoneMsg
            {
                identifier = this.identifier,
                zone_ids = this.zone_ids,
                alert_distance = this.alert_distance,
                boundaries = Array.ConvertAll(this.boundaries, x => x.GetSceneGeometryMsg()),
            };
        }
    }


    /// <summary>
    /// Work are object that holds the ID, boundary, and origin object
    /// Does not have a message format (used for computing the safety zones internally in VizorGH)
    /// </summary>
    public class WorkAreaObject
    {
        public Device target { get; set; }
        public int id { get; set; }
        public Rectangle3d rectangle { get; set; }
        public WorkAreaObject(Device _target, int _id, Rectangle3d rect) 
        {
            this.target = _target;
            this.id = _id;
            this.rectangle = rect;
        }
    }

    #endregion


    /// <summary>
    /// General task object that holds necessary information for task execution
    /// Converts to vizor msg format "GeneralTaskMsg"
    /// </summary>
    public class GeneralTaskObject
    {
        public int id { get; set; }
        public string name { get; set; }
        public Device gTarget { get; set; }
        public string type { get; set; } // sequential / parallel + individual / shared
        public string skill { get; set; }
        public int deadline { get; set; } // estimated duration
        public string instruction { get; set; }
        public SceneContentObject gContentObject { get; set; }
        public RobotTrajectoryObject gTrajectoryObject { get; set; }
        //public List<Plane> gTrajectoryFrames { get; set; }

        public SafetyZoneObject gSafetyZoneObject { get; set; }

        public SceneContentMsg GetSceneContentMsg()
        {
            if (this.gContentObject != null)
            {
                return gContentObject.GetSceneContentMsg();
            }
            else
            {
                return new SceneContentMsg();
            }
        }

        public RobotTrajectoryMsg GetTrajectoryMessages()
        {
            if (!(this.gTarget is RobotObject robot))
            {
                return new MessageTypes.RobotTrajectoryMsg("", new BuiltInMsg.JointTrajectoryMsg(), new BuiltInMsg.MeshMsg());
            }

            // a robotic task is allowed to carry no motion at all (e.g. "Gripper Open", where the
            // backend dispatches on the task name). the platform name is still reported so the
            // receiver can tell "robot task without motion" from "not a robot task"
            if (gTrajectoryObject == null)
            {
                return new MessageTypes.RobotTrajectoryMsg(robot.name, new BuiltInMsg.JointTrajectoryMsg(), new BuiltInMsg.MeshMsg());
            }

            // joint trajectory and mesh trajectory are converted independently, so a failure
            // converting one (e.g. a null trajectory mesh) can't wipe out the other's valid data
            BuiltInMsg.JointTrajectoryMsg jointTrajectoryMsg;
            try
            {
                jointTrajectoryMsg = gTrajectoryObject.GetRobotTrajectoryMsg();
            }
            catch
            {
                jointTrajectoryMsg = new BuiltInMsg.JointTrajectoryMsg();
            }

            BuiltInMsg.MeshMsg meshMsg;
            try
            {
                meshMsg = gTrajectoryObject.gMesh != null
                    ? MessageTypes.MsgDataConverter.ghMeshToMsg(gTrajectoryObject.gMesh)
                    : new BuiltInMsg.MeshMsg();
            }
            catch
            {
                meshMsg = new BuiltInMsg.MeshMsg();
            }

            return new MessageTypes.RobotTrajectoryMsg(robot.name, jointTrajectoryMsg, meshMsg);
        }

        public SafetyZoneMsg GetSafetyZoneMessage()
        {
            if (this.gSafetyZoneObject != null)
            {
                return this.gSafetyZoneObject.GetSafetyZoneMsg();
            }
            else
            {
                return new MessageTypes.SafetyZoneMsg();
            }
        }
    }
    
}
