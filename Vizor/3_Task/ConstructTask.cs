using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using Vizor._1_System;
using VizorLibs;

/* LIMITATION: one agent per task
 * A TASK CAN ONLY BE REASSIGNED FROM ROBOT->HUMAN, not the other way around

*/

namespace Vizor._4_Task
{
    /// <summary>  
    /// The ConstructTask component is responsible for creating task objects for execution.  
    /// It supports both manual and robotic tasks, as well as hybrid configurations.  
    ///  
    /// Inputs: 
    /// - Task Names: List of human-readable task descriptions.  
    /// - Target Devices: List of devices intended to execute the tasks.  
    /// - Scene Contents: Optional scene content objects for the tasks.  
    /// - Instruction Texts: Optional text instructions to guide the tasks.  
    /// - Robot Trajectory: Optional trajectory objects for robotic tasks.  
    /// - Safety Zone: Optional safety zone objects for robotic tasks.  
    /// - Associated Skill: Optional skill definition for the tasks (default: null).  
    /// - Estimated Duration: Optional task duration in seconds (default: 60 seconds).  
    ///  
    /// Outputs:  
    /// - Output: Status message summarizing the task creation process.  
    /// - Task Object: List of task objects created for execution.  
    /// </summary>
    
    public class ConstructTask : VizorBaseComponent
    {
        private List<Device> targets;
        private List<string> names;
        private List<string> instructions;
        private List<string> skills;
        private List<int> duration;
        private List<SceneContentObject> contentObjects;
        private List<RobotTrajectoryObject> trajectories;
        private List<GeneralTaskObject> tasks;
        private List<SafetyZoneObject> zones;
        private string input_instruction = "\n(should be equal to task count, or a single input will be applied to all)";

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ConstructTask()
          : base("Construct Task Object", "Task",
              "create a task", "3_Task")
        {
            isListener = false;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Task Names", "Names", "names of the task (human-readable description)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Target Devices", "Devices", "intended device to execute the task" + input_instruction, GH_ParamAccess.list);
            
            // task content
            pManager.AddGenericParameter("Scene Contents", "Content", 
                "optional scene content object for the task" + input_instruction, GH_ParamAccess.list);
            pManager.AddTextParameter("Instruction Texts", "Instruction", 
                "optional text to guide the task" + input_instruction, GH_ParamAccess.list);
            pManager.AddGenericParameter("Robot Trajectory", "Trajectory",
                "optional trajectory object for robotic tasks (a single input will be applied to all robotic tasks)" +
                "\nLeave unconnected for non-motion robotic tasks (e.g. gripper or tool change), which are driven by the task name. " +
                "\nIn a mixed series, supply one item per task and leave a null gap for each non-motion task. ", GH_ParamAccess.list);
            pManager.AddGenericParameter("Safety Zone", "Zone",
                "optional safety zone objects for robotic tasks (a single input will be applied to all tasks)"+ 
                "\nThe system sends an alert when monitored worker positions fall in range of the specified boundaries. "+
                "\nZones specified for manual tasks will be ignored. ", GH_ParamAccess.list);

            // new
            pManager.AddTextParameter("Associated Skill", "Skill",
                "optional text definition for the skill associated with the task (a single input will be applied to all tasks) \nIf no input is given, this will default to null (all actors can execute)", 
                GH_ParamAccess.list, "null");
            pManager.AddIntegerParameter("Estimated Duration", "Duration",
                "optional integer for the duration of the task, in seconds (a single input will be applied to all tasks) \nIf no input is given, this will default to 60 seconds. ", 
                GH_ParamAccess.list, 60);
            //pManager.AddTextParameter("Operation Type", "Type", "session, session-static, step, persistent, session-local, step-local", GH_ParamAccess.list, "add-session");

            // TODO: clarify how reassignment works. a robotic task can be overtaken by manual, but not vice versa
            foreach (int i in new int[]{2,3,4,5,6,7}){
                pManager[i].Optional = true;
                //pManager[i].DataMapping = GH_DataMapping.Flatten;
            }

        }

        private void updateParamName(bool isManual)
        {

            if (!isManual)
            {
                Params.Input[4].Name = "Robot Trajectory";
                Params.Input[4].NickName = "Trajectory";
                Params.Input[4].Description = "optional trajectory object for robotic tasks (a single input will be applied to all robotic tasks)" +
                    "\nLeave unconnected for non-motion robotic tasks (e.g. gripper or tool change), which are driven by the task name. " +
                    "\nIn a mixed series, supply one item per task and leave a null gap for each non-motion task. ";
                Params.Input[5].Name = "Safety Zone";
                Params.Input[5].NickName = "Zone";
                Params.Input[5].Description = "safety zone objects for robotic tasks (a single input will be applied to all robotic tasks)" +
                    "\nThe system sends an alert when monitored worker positions fall in defined range of the specified boundaries. " +
                    "\nZones specified for manual tasks will be ignored. ";
            }
            else
            {
                Params.Input[4].Name = "---";
                Params.Input[4].NickName = "---";
                Params.Input[4].Description = "trajectory input is ignored (target devices do not contain robotic clients)";
                Params.Input[5].Name = "---";
                Params.Input[5].NickName = "---";
                Params.Input[5].Description = "safety zone input is ignored (target devices do not contain robotic clients)";
            }
            Params.OnParametersChanged();
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.item);
            pManager.AddGenericParameter("Task Object", "Task", "task to be executed", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // mandatory inputs
            names = new List<string>();
            targets = new List<Device>();
            if (!DA.GetDataList(0, names)) return;
            if (!DA.GetDataList(1, targets)) return;
            if ((targets.Count != 1) && (!AssertInput("Targets", targets.Count, names.Count)))
            {
                DA.SetDataList(1, null);
                return;
            }

            // check input and recommend separating manual / robotic tasks
            updateParamName(VizorUtilities.GetTargetType(targets) == TargetType.CompleteManual);
            if (VizorUtilities.GetTargetType(targets) == TargetType.Hybrid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    "You have specified a mix of human and robotic actors. For a simpler configuration process, specify tasks for each actor separately. "+
                    "\nWhen using hybrid actors, both human and robotic data must be supplied for each task. Double check requirements on each input to make sure the tasks are correctly specified. ");
            }

            // optional inputs
            skills = new List<string>();
            duration = new List<int>();
            DA.GetDataList(6, skills);
            DA.GetDataList(7, duration);

            // initiate tasks with basic information
            tasks = new List<GeneralTaskObject>();
            InitiateTasks(tasks);

            // add content objects (must match the number of tasks)
            contentObjects = new List<SceneContentObject>();
            if (!DA.GetDataList(2, contentObjects))
            {
                tasks.ForEach(x => x.gContentObject = null);
            }
            else
            {
                if ((contentObjects.Count != 1) && (!AssertInput("Content", contentObjects.Count, tasks.Count)))
                {
                    DA.SetDataList(1, null);
                    return;
                }

                for (int i = 0; i < tasks.Count; i++)
                {
                    SceneContentObject content = (contentObjects.Count == 1 ? contentObjects[0] : contentObjects[i]);

                    tasks[i].gContentObject = new SceneContentObject
                    {
                        name = "task content " + i.ToString(),
                        operation = "add-task",
                        LoD = content.LoD,
                        geomObjects = content.geomObjects,
                        textObjects = content.textObjects,
                        wireObjects = content.wireObjects,
                    };
                }
            }
            

            // instructions (must match the number of tasks)
            instructions = new List<string>();
            if (!DA.GetDataList(3, instructions)) {
                tasks.ForEach(x => x.instruction = "* no instruction provided");
            }
            else
            {
                if ((instructions.Count != 1) && (!AssertInput("Instructions", instructions.Count, tasks.Count)))
                {
                    DA.SetDataList(1, null);
                    return;
                }
                for (int i = 0; i < tasks.Count; i++)
                {
                    tasks[i].instruction = instructions.Count == 1 ? instructions[0] : instructions[i];
                }
            }


            // robot trajectory objects
            // a robotic task without motion data is valid: the backend dispatches non-motion skills
            // (gripper, tool change, dwell) on the task name, and motion may also be planned robot-side
            trajectories = new List<RobotTrajectoryObject>();
            if (!DA.GetDataList(4, trajectories) || (trajectories.Count == 0)){
                tasks.ForEach(x => x.gTrajectoryObject = null);
                if (VizorUtilities.GetTargetType(targets) != TargetType.CompleteManual)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Robotic task(s) created without motion data. They will be dispatched as non-motion tasks, "+
                        "driven by the task name. \nConnect a trajectory object if the robot should move. ");
                }
            }
            else
            {
                if (VizorUtilities.GetTargetType(targets) != TargetType.CompleteManual)
                {
                    if ((trajectories.Count != 1) && (!AssertInput("Trajectories", trajectories.Count, tasks.Count)))
                    {
                        DA.SetDataList(1, null);
                        return;
                    }

                    // gaps in the list are intentional: they mark individual non-motion tasks within a
                    // mixed series (e.g. move -> close gripper -> move -> open gripper)
                    List<int> tasksWithoutMotion = new List<int>();
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        tasks[i].gTrajectoryObject = trajectories.Count == 1 ? trajectories[0] : trajectories[i];
                        if (tasks[i].gTrajectoryObject == null)
                        {
                            tasksWithoutMotion.Add(i + 1);
                        }
                    }
                    if (tasksWithoutMotion.Count > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            String.Format("Task(s) {0} have no motion data and will be dispatched as non-motion tasks. ",
                                String.Join(", ", tasksWithoutMotion)));
                    }
                }
            }

            // zone objects
            zones = new List<SafetyZoneObject>();
            if (!DA.GetDataList(5, zones)) {
                tasks.ForEach(x => x.gSafetyZoneObject = null);
                if (VizorUtilities.GetTargetType(targets) != TargetType.CompleteManual)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "You added a robot client, but did not provide any safety information. \nConsider adding a safety zone. ");
                }
            }
            else
            {
                if (VizorUtilities.GetTargetType(targets) != TargetType.CompleteManual)
                {
                    if (zones.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "You added a robot client, but did not provide any safety zone input. ");
                    }
                    else if ((zones.Count != 1) && (!AssertInput("Safety Zones", zones.Count, tasks.Count)))
                    {
                        DA.SetDataList(1, null);
                        return;
                    }
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        tasks[i].gSafetyZoneObject = zones.Count == 1 ? zones[0] : zones[i];
                        if (tasks[i].gSafetyZoneObject == null)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid trajectory input. ");
                            DA.SetDataList(1, null);
                            return;
                        }
                    }
                }
            }

            SummariseTasks(DA);
            DA.SetDataList(1, tasks);
        }

        private void InitiateTasks(List<GeneralTaskObject> tasks)
        {
            for (int i = 0; i < names.Count; i++)
            {
                GeneralTaskObject task = new GeneralTaskObject
                {
                    id = i+1,
                    name = names[i],
                    gTarget = (targets.Count == 1) ? targets[0] : targets[i],
                    type = VizorUtilities.GetExecutionTypeFromName(names[i]), 
                    skill = (skills.Count == 1) ? skills[0] : skills[i],
                    deadline = (duration.Count == 1) ? duration[0] : duration[i],
                };
                tasks.Add(task);
            }
        }

        private void SummariseTasks(IGH_DataAccess DA)
        {
            switch (VizorUtilities.GetTargetType(targets))
            {
                case TargetType.CompleteManual:
                    {
                        DA.SetData(0, String.Format("{0} tasks created for {1} execution. ", tasks.Count, "manual"));
                        this.Message = String.Format("{0} {1} tasks", tasks.Count, "manual");
                        break;
                    }
                case TargetType.CompleteRobotic:
                    {
                        DA.SetData(0, String.Format("{0} tasks created for {1} execution. ", tasks.Count, "robotic"));
                        this.Message = String.Format("{0} {1} tasks", tasks.Count, "robotic");
                        break;
                    }
                case TargetType.Hybrid:
                    {
                        DA.SetData(0, String.Format("{0} tasks created for {1} execution. ", tasks.Count, "hybrid"));
                        this.Message = String.Format("{0} {1} tasks", tasks.Count, "hybrid");
                        break;
                    }
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
                return Vizor.Properties.Resources.Task_Object;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("97128de9-5dbb-4b95-a3e7-e293506f6791"); }
        }
    }
}