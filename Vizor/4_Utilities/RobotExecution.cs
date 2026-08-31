using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Timers;
using System.Collections.Generic;
using Vizor._1_System;
using VizorLibs;
using VizorLibs.MessageTypes;

namespace Vizor._3_Machine
{
    ///<summary>
    /// The RobotExecution component is responsible for receiving and executing robot tasks.
    /// It always commands the robot to execute; use MotionSimulation for previewing tasks
    /// without moving the robot, and the ROS-side `physical` launch argument to choose
    /// whether the execution pipeline targets real or simulated hardware.
    ///
    /// Inputs:
    /// - Robot (R): A registered RobotObject instance representing the robot to execute tasks.
    /// - Robot Task (T): A GeneralTaskObject containing the task details to be executed.
    /// - Start (S): A boolean to enable or disable the component. Set to true to start execution.
    ///
    /// Outputs:
    /// - Output (Out): A string providing the status of the execution, such as connection updates or errors.
    ///
    /// Usage:
    /// This component validates the provided task and robot, updates the robot device if necessary,
    /// and executes the task. It ensures safety by requiring pre-checked motions.
    /// </summary>

    public class RobotExecution : VizorBaseComponent
    {
        private RobotObject robot;
        private GeneralTaskObject currentTask;
        private int cachedId;
        private bool execute;
        /// <summary>
        /// Initializes a new instance of the RobotExecution class.
        /// </summary>
        public RobotExecution()
          : base("RobotExecution", "Executor",
              "Receives robot tasks and execute them via the ROS system. CAN BE DANGEROUS - ONLY USE WITH PRE-CHECKED MOTIONS.", "4_Utilities")
        {
            cachedId = -1;
            isListener = false;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Robot", "R", "registed robot object", GH_ParamAccess.item);
            pManager.AddGenericParameter("Robot Task", "T", "task to execute", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Start", "S", "disable the component by setting it to false",
                GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!IsDocumentActive()) return;
            DA.GetData("Robot Task", ref currentTask);
            DA.GetData(2, ref execute);

            // validity checks
            if (!execute)
            {
                cachedId = -1;
                this.Message = "Inactive";
                return;
            }
            if (currentTask == null) return;
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
                    {
                        DA.SetData(0, "device updated\nlast updated on " + DateTime.Now.ToString());
                    }
                }
            }
            else
            {
                CleanupConnection();
                DA.SetData(0, "no connection");
                return;
            }

            // receive static frames and a boolean to switch the simulation on and off
            // if current task changed, execute it (through a loop), once finished, deactivate
            // in iHRC implmenetation, the task should be sent to the Robot object in python
            // a null trajectory is valid (non-motion task, e.g. "Gripper Open"); a trajectory object
            // that carries no points is not, since motion was intended but could not be resolved
            if ((currentTask.gTrajectoryObject != null) && ((currentTask.gTrajectoryObject.joint_trajectory?.Count ?? 0) == 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "no frame in this robot task");
                return;
            }

            // if a new task is provided, send the execution message
            if ((currentTask.id != cachedId) && execute)
            {
                cachedId = currentTask.id;
                executeTask();
                this.Message = "Started";
            }
        }

        private void executeTask()
        {
            GeneralTaskMsg taskMsg = new GeneralTaskMsg(currentTask);

            ROSMessageHandler.PublishTaskToRobot(this.wscObj, robot.name, taskMsg);

            // the task message already embeds the trajectory. only mirror it onto the dedicated
            // trajectory topic when there is real motion, so a non-motion task does not push an
            // empty path that the receiver could read as "clear the current trajectory"
            if (currentTask.gTrajectoryObject != null)
            {
                ROSMessageHandler.PublishTrajectory(this.wscObj, robot.name, currentTask.GetTrajectoryMessages());
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                return Vizor.Properties.Resources.Robot_Execution;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("fbc5cfd7-580b-44eb-96af-12bb8f3ae636"); }
        }
    }
}