using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Timers;
using Vizor._1_System;
using VizorLibs;

namespace Vizor._3_Robot
{
    /// <summary>  
    /// The MotionSimulation component simulates a single robot task, both in the Rhino viewport and on the HoloLens.  
    /// It visualizes the robot's tool center point (TCP) and attached geometry in Grasshopper, while also publishing  
    /// trajectory data to the HoloLens (PublishTrajectory).  
    ///  
    /// Inputs:  
    /// - Robot (Robot): The registered robot object to simulate.  
    /// - Robot Task (Task): The task to execute, which must be a robotic task.  
    /// - Step Interval (Interval): Optional update interval in milliseconds (default: 1000 ms).  
    /// - Start (Start): A toggle to start or stop the simulation.  
    ///  
    /// Outputs:  
    /// - Output (Out): Status messages indicating the simulation state.  
    /// - Current TCP (TCP): The target tool center point (TCP) in the current frame.  
    /// - Current Geometry (Geometry): The current position of the attached geometry, if any.  
    /// - External Axis (E1): The external axis value for virtual robot simulation with 7 axes.  
    ///  
    /// This component manages the simulation lifecycle, including starting, stopping, and updating the simulation.  
    /// Use e.g. VirtualRobot to visualise the robot poses. 
    /// </summary>

    public class MotionSimulation : VizorBaseComponent
    {
        private RobotObject robot;
        private GeneralTaskObject currentTask;
        private int cachedId;
        private int cachedSignature;
        private int interval;
        private volatile bool startSim;
        private Mesh targetMesh;
        private float e1Axis = 0;

        private volatile bool timerActive;
        private Plane targetTcp;
        private Timer timer;
        private int executionCounter;

        /// <summary>
        /// Initializes a new instance of the RobotSimulation class.
        /// </summary>
        public MotionSimulation()
          : base("RobotSimulator", "Simulator",
              "Receives robot tasks and (virtually) execute them", "4_Utilities")
        {
            isListener = false;
            cachedId = -1;

            timer = new Timer();
            timer.Elapsed += new ElapsedEventHandler(RunSimulation);
            timerActive = false;
        }

        /// <summary>
        /// Provide the robot object and task to simulate. Simulation can be controlled / reset through the start toggle. 
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Robot", "Robot", "registed robot object", GH_ParamAccess.item);
            pManager.AddGenericParameter("Robot Task", "Task", "task to execute", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Step Interval", "Interval", "optional update interval in ms", GH_ParamAccess.item, 1000);
            pManager.AddBooleanParameter("Start", "Start", "disable the stream by setting it to false", GH_ParamAccess.item, true);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Current TCP", "TCP", "target TCP in the current frame", GH_ParamAccess.item);
            pManager.AddMeshParameter("Current Geometry", "Geometry", "current position of the attached geometry, if any", GH_ParamAccess.list);
            pManager.AddNumberParameter("External Axis", "E1", "external axis value (temporary fix for virtual robot simulation with 7 axis)", GH_ParamAccess.item);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (! IsDocumentActive()) return;
            DA.GetData("Robot Task", ref currentTask);
            DA.GetData(3, ref startSim);
            DA.GetData("Step Interval", ref interval);

            // when toggle is false, reset cached ID
            if (!startSim)
            {
                cachedId = -1;
                CancelTimer();
                DA.SetData(0, "simulation stopped" + "\nlast updated on " + DateTime.Now.ToString());
                this.Message = "Stopped";
                return;
            }

            // if no task is provided, return
            if (currentTask == null) return;

            // the provided task must be a robotic task
            if (!(currentTask.gTarget is RobotObject))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "you did not provide a robotic task");
                return;
            }

            // Set robot object
            RobotObject _robot = new RobotObject();
            if (DA.GetData("Robot", ref _robot))
            {
                if (_robot != this.robot)
                {
                    this.robot = _robot;
                    if (UpdateDevice((Device)_robot))
                        DA.SetData(0, "device updated\nlast updated on " + DateTime.Now.ToString());
                }
                ROSMessageHandler.Advertise(this.wscObj, this.robot.name + "_Command", "std_msgs/String");
            }
            else
            {
                CleanupConnection();
                DA.SetData(0, "no connection");
                return;
            }

            // if a new task is provided, or anything within the task changed (trajectory, content, etc.), restart the timer
            int taskSignature = ComputeTaskSignature(currentTask);
            if (currentTask.id != cachedId || taskSignature != cachedSignature)
            {
                cachedId = currentTask.id;
                cachedSignature = taskSignature;
                CancelTimer();
                if (StartSimulation())
                {
                    this.Message = "Started";
                }
            }

            // Set output target TCP for visualisation in Grasshopper
            if (this.timerActive && startSim)
            {
                DA.SetData(0, "sending frames" + "\nlast updated on " + DateTime.Now.ToString());
                DA.SetData(1, targetTcp);
                DA.SetData(3, e1Axis);
                this.Message = "Running";
            }

            // get mesh target in the task
            if ((currentTask.gContentObject != null) && (currentTask.gContentObject.geomObjects != null))
            {
                List<Mesh> meshes = new List<Mesh>();
                foreach (SceneGeometryObject go in currentTask.gContentObject.geomObjects)
                {
                    targetMesh = go.gMesh.DuplicateMesh();
                    if (go.operation.EndsWith("local")) {
                        targetMesh.Transform(Transform.PlaneToPlane(Plane.WorldXY, targetTcp));
                    }
                    meshes.Add(targetMesh);
                }
                DA.SetDataList(2, meshes);
            }
        }
        private void CancelTimer()
        {
            timerActive = false;
            timer.Stop();
        }

        /// <summary>
        /// Builds a lightweight fingerprint of the parts of the task relevant to the simulation
        /// (trajectory, attached geometry, target, instruction), so a restart can be triggered
        /// even when the task's id is unchanged but its content was edited upstream.
        /// </summary>
        private int ComputeTaskSignature(GeneralTaskObject task)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (task.gTarget?.name?.GetHashCode() ?? 0);
                hash = hash * 31 + (task.instruction?.GetHashCode() ?? 0);
                hash = hash * 31 + task.deadline;

                RobotTrajectoryObject traj = task.gTrajectoryObject;
                if (traj != null)
                {
                    hash = hash * 31 + (traj.gTrajectoryFrames?.Count ?? 0);
                    if (traj.gTrajectoryFrames != null)
                    {
                        foreach (Plane p in traj.gTrajectoryFrames)
{
    hash = hash * 31 + p.Origin.X.GetHashCode();
    hash = hash * 31 + p.Origin.Y.GetHashCode();
    hash = hash * 31 + p.Origin.Z.GetHashCode();

    // Include orientation as well as position
    hash = hash * 31 + p.XAxis.X.GetHashCode();
    hash = hash * 31 + p.XAxis.Y.GetHashCode();
    hash = hash * 31 + p.XAxis.Z.GetHashCode();
    hash = hash * 31 + p.YAxis.X.GetHashCode();
    hash = hash * 31 + p.YAxis.Y.GetHashCode();
    hash = hash * 31 + p.YAxis.Z.GetHashCode();
}
                    }
                    hash = hash * 31 + (traj.joint_trajectory?.Count ?? 0);
                    hash = hash * 31 + (traj.gMesh?.Vertices.Count ?? 0);
                }

                SceneContentObject content = task.gContentObject;
                if (content?.geomObjects != null)
                {
                    hash = hash * 31 + content.geomObjects.Length;
                    foreach (SceneGeometryObject go in content.geomObjects)
                    {
hash = hash * 31 + (go?.gMesh?.Vertices.Count ?? 0);
                    }
                }

                return hash;
            }
        }

        /// <summary>
        /// Publishes the task's trajectory and starts the playback timer.
        /// Returns false when the task carries no motion, in which case nothing is started.
        /// </summary>
        private bool StartSimulation()
        {
            // a robotic task may legitimately carry no motion at all (e.g. "Gripper Open", dispatched
            // by name on the backend). there is nothing to publish or step through, so report it
            // quietly rather than erroring and spinning the timer
            if (currentTask.gTrajectoryObject == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "this task carries no motion to simulate");
                this.Message = "No Motion";
                return false;
            }

            VizorLibs.MessageTypes.RobotTrajectoryMsg trajectoryMsg = currentTask.GetTrajectoryMessages();
            bool hasJointData = trajectoryMsg.joint_trajectory?.points != null && trajectoryMsg.joint_trajectory.points.Length > 0;

            if (string.IsNullOrEmpty(trajectoryMsg.platform_name) || !hasJointData)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "no valid trajectory data to publish for this robot task");
            }
            else if (!ROSMessageHandler.PublishTrajectory(wscObj, robot.name, trajectoryMsg))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "trajectory could not be published (connection not open)");
            }

            timerActive = true;
            executionCounter = 0;
            timer.Interval = interval >= 20 ? interval : 20;
            timer.Start();
            return true;
        }

        private void RunSimulation(object source, ElapsedEventArgs e)
        {
            var task = currentTask; // snapshot shared reference, prevents null ref if task is replaced mid-execution
            if (task == null) return;

            if (isAskingNewSolution) return;
            if (!this.timerActive || !startSim) return;
            if ((task.gTrajectoryObject == null) || (task.gTrajectoryObject.gTrajectoryFrames.Count == 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "no frame in this robot task");
                CancelTimer();
                return;
            }

            // bounds check: gTrajectoryFrames is the authoritative frame list
            int counter = executionCounter;
            if (counter >= task.gTrajectoryObject.gTrajectoryFrames.Count)
            {
                CancelTimer();
                return;
            }

            // update value displays
            targetTcp = task.gTrajectoryObject.gTrajectoryFrames[counter];
            // joint_trajectory may be absent or shorter (e.g. no external axis data)
            if (task.gTrajectoryObject.joint_trajectory != null &&
                task.gTrajectoryObject.joint_trajectory.Count > counter)
                e1Axis = task.gTrajectoryObject.joint_trajectory[counter][0];

            executionCounter++;
            if (executionCounter >= task.gTrajectoryObject.gTrajectoryFrames.Count)
            {
                CancelTimer();
                //ROSMessageHandler.PublishSimComplete(wscObj, this.robot.name);
                this.Message = "Finished";
            }

            // expire solution
            Grasshopper.Instances.DocumentEditor.BeginInvoke((Action)delegate ()
            {
                if (ghDocument.SolutionState != GH_ProcessStep.Process)
                {
                    isAskingNewSolution = true;
                    this.ExpireSolution(true);
                    isAskingNewSolution = false;
                }
            });
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                return Vizor.Properties.Resources.Motion_Simulation;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("b8d94705-4be0-4baa-bc06-f54f4a3a68c1"); }
        }
    }
}