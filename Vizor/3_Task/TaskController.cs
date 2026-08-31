using Grasshopper.Kernel;
using GH_IO.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Vizor._1_System;
using VizorLibs;
using VizorLibs.MessageTypes;

namespace Vizor._4_Task
{
    /// <summary>
    /// TaskController is a surrogate controller for simulating and training tasks.
    /// It manages task sequences, communicates with devices, and handles task execution in both online and offline modes.
    ///
    /// Inputs:
    /// - Start: Boolean input to initiate the task sequence or upload tasks (renamed "Upload" in offline mode).
    /// - HRC Tasks: List of GeneralTaskObject representing the tasks to be executed.
    ///
    /// Right-click config:
    /// - Control Mode: Online (Grasshopper drives the task sequence live, default) or Offline (tasks uploaded to the DataStore on button press).
    /// - On Input Change: Reset (input task changes are ignored until Start is toggled off/on, default) or
    ///   Dynamic Substitute (when the ordered task names are unchanged, task content is substituted in-memory
    ///   so upcoming steps use the new content without a reset; when the names differ, the running job is
    ///   cancelled and restarted from the new tasks). Online mode only.
    ///
    /// Outputs:
    /// - Current Task: The task currently being executed.
    /// - Process Log: Log of the task execution process, including status updates and errors.
    /// </summary>
    public class TaskController : VizorBaseComponent
    {
        // job lifecycle signal: broadcast once when a job starts and once when it completes,
        // so any subscriber (robot driver, logger, AR app) can bracket the run.
        private const string JobSignalTopic = "Task/signal";
        private const string JobStartSignal = "start_job";
        private const string JobEndSignal = "end_job";

        // config (right-click, persisted)
        private bool onlineMode = true;
        private bool dynamicInputMode = false;

        // gh input
        private bool start;
        private List<GeneralTaskObject> tasks;

        //runtime var
        private string controllerLog;
        private string status;
        private int taskIndex = -1;
        private bool waitForHuman = false;
        private TaskListMsg taskListMsg;
        private bool currentJobActive;

        List<Device> rawDevices;

        /// <summary>
        /// Initializes a new instance of the HRC Controller class.
        /// </summary>
        public TaskController()
          : base("Task Controller", "TaskCtrl",
              "Provide human and machine tasks to be accessed on the network.", "3_Task")
        {
            isListener = true;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start", "Start", "click to initiate the task sequence", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("HRC Tasks", "Tasks", "list of tasks", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.item);
            pManager.AddGenericParameter("Current Task", "Task", "task to be executed", GH_ParamAccess.item);
            pManager.AddTextParameter("Process Log", "Log", "process log", GH_ParamAccess.item);
            //pManager.AddMeshParameter("Current Geometry", "Geometry", "current position of the task geometryy", GH_ParamAccess.list);
        }

        private void updateParamName(bool isOnline)
        {

            if (isOnline)
            {
                Params.Input[0].Name = "Start";
                Params.Input[0].NickName = "Start";
                Params.Input[0].Description = "click to initiate the task sequence";
            }
            else
            {
                Params.Input[0].Name = "Upload";
                Params.Input[0].NickName = "Upload";
                Params.Input[0].Description = "click to initiate upload";
            }
            Params.OnParametersChanged();
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!IsDocumentActive()) return;

            updateParamName(onlineMode);

            if (!this.onMessageTriggered)
            {
                DA.GetData(0, ref start);

                // update data when the task is not started
                if (!start)
                {
                    currentJobActive = false;

                    // Validate input
                    tasks = new List<GeneralTaskObject>();
                    DA.GetDataList(1, tasks);
                    if (tasks.Count != 0)
                    {
                        //this.wscObj = tasks[0].gTarget.wscObj;
                        rawDevices = TaskUtilities.GetDevicesForTasks(tasks);
                        foreach (Device d in rawDevices)
                        {
                            if (d is null)
                            {
                                this.Message = "Error";
                                controllerLog = "ERROR: the input 'device' is null.";
                                DA.SetData(1, controllerLog);
                                return;
                            }
                        }
                        List<string> deviceNames = TaskUtilities.GetDeviceNamesForTasks(tasks);

                        this.UpdateDevices(rawDevices);

                        // impose artificial ID on task list
                        for (int i=0; i < tasks.Count; i++)
                        {
                            tasks[i].id = i;
                        }


                        taskListMsg = TaskUtilities.GenerateTaskList(tasks);

                        controllerLog = "Tasks Generated";

                        if (!currentJobActive)
                            controllerLog += " - ready for " + string.Join(", ", deviceNames) + ". Toggle start to begin. \n";

                        this.Message = String.Format("{0} Tasks Pending", tasks.Count);
                    }
                }

                // input task changes while a job is running (online mode only)
                else if (onlineMode && currentJobActive && tasks != null)
                {
                    List<GeneralTaskObject> newTasks = new List<GeneralTaskObject>();
                    DA.GetDataList(1, newTasks);

                    // Reset (manual) mode: changes are ignored until Start is toggled off/on.
                    // Dynamic Substitute mode: swap content in-memory when the ordered task names
                    // match; when they differ, cancel the running job and restart from the new tasks.
                    if (dynamicInputMode && newTasks.Count != 0)
                    {
                        if (NameSignature(newTasks) == NameSignature(tasks))
                        {
                            // impose artificial ID on the substituted list (matches OnJobStart staging)
                            for (int i = 0; i < newTasks.Count; i++)
                                newTasks[i].id = i;
                            // future steps read tasks[taskIndex], so upcoming steps pick up the new
                            // content automatically; taskIndex is preserved and the pool list is not re-published.
                            tasks = newTasks;
                            controllerLog += "\n\n>> Task content substituted - execution state preserved, continuing from task "
                                + (taskIndex + 1).ToString() + " of " + tasks.Count.ToString() + " | " + DateTime.Now.ToString();
                            this.Message = "content substituted";
                        }
                        else
                        {
                            ResetJob(newTasks);
                        }
                    }
                }

                // broadcast the task to the pool (when triggered by button)
                if (this.wscObj != null && !currentJobActive && this.wscObj.isConnected() && taskListMsg != null)
                {
                    if (onlineMode)
                    {
                        isListener = true;

                        // start task
                        if (start)
                        {
                            OnJobStart();
                        }
                    }
                    else
                    {
                        isListener = false;

                        if (start)
                        {
                            // upload tasks
                            DA.SetData(1, "Uploading tasks... ");
                            UploadJob();
                            controllerLog = String.Format("{0} tasks uploaded!", tasks.Count);
                        }
                    }
                    DA.SetData(0, null); // prep stage
                    DA.SetData(1, controllerLog);
                    return;
                }
            }
            else
            {
                this.onMessageTriggered = false;
                if (!currentJobActive || !onlineMode)
                {
                    DA.SetData(1, controllerLog);
                    return;
                }

                if (this.wscObj.message != null)
                {
                    // check if this is a status message
                    string[] payload = ROSMessageHandler.ParseStatus(this.wscObj.message);
                    if (payload == null)
                    {
                        string[] payloadIndividual = ROSMessageHandler.ParseCommand(this.wscObj.message);

                        if (payloadIndividual != null) // e.g. gaze, transform messages
                        {
                            if (payloadIndividual[0] != "WorkerPool") // log device-wise acknowledgement
                                controllerLog += "\n >> " + payloadIndividual[0] + " completed task " + taskIndex.ToString() + " | " + DateTime.Now.ToString();
                        }

                        this.wscObj.message = null;
                        DA.SetData(1, controllerLog);
                        return;
                    }

                    // if this is a status message either
                    else
                    {
                        this.wscObj.message = null;
                        // case 1: advance to the next task

                        // message format "next_step" or "taskID_successIndex" (successIndex is 1 for success, 0 for failure)
                        if ((payload[0] == "WorkerPool" && waitForHuman && (payload[1] == "next_step" || payload[1].Split('_')[1] == "1" )) || 
                             (payload[0] == "Robot" && !waitForHuman && ( payload[1] == "cancel" || payload[1].Split('_')[1] == "1") ))
                             //(payload[0] == "Robot" && payload[1].Split('_')[1] == "1")) // && !waitForHuman
                        {
                            if (taskIndex == -1) // ignore messages received before the component is active
                            {
                                controllerLog = "No task in progress. Click start to begin. ";
                            }
                            else if (taskIndex < tasks.Count)
                            {
                                if (payload[1] == "cancel")
                                {
                                    controllerLog += "\n\n Task Cancelled. Toggle start again to begin. ";
                                    this.Message = "Cancelled";
                                    taskIndex = -1;
                                    currentJobActive = false;
                                    // close the job cycle so subscribers see an end signal for every run,
                                    // whether it completed or was cancelled
                                    ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobEndSignal);
                                }
                                else
                                {
                                    status = StepTask(taskIndex);
                                    controllerLog += "\n\n" + status + " | " + DateTime.Now.ToString();
                                    DA.SetData(0, tasks[taskIndex]);
                                    //DA.SetDataList(2, Array.ConvertAll(tasks[taskIndex].gContentObject.geomObjects, x => x.gMesh));
                                    taskIndex++;
                                    this.Message = String.Format("{0} of {1} Tasks", taskIndex, tasks.Count);
                                    if (status == "error")
                                    {
                                        this.Message = "Error";
                                        //return; // TODO: recover gracefully
                                    }
                                }
                            }
                            else
                            {
                                OnJobEnd();
                                //DA.SetData(1, null);
                            }
                        }

                        // case 2: reassign the task
                        else if (payload[1].StartsWith("reassign"))
                        {
                            string[] result = payload[1].Split('_');
                            if (result.Length == 3)
                            {
                                string target = result[1];
                                int task_id = -1;
                                int.TryParse(result[2], out task_id);
                                if (task_id >= 0)
                                {
                                    string _name = "";
                                    // TODO: reassign based on the target result
                                    //controllerLog += $"iterating {rawDevices} {this.devices}";
                                    foreach(Device d in rawDevices)
                                    {
                                        //controllerLog += $"{d.GetType()} {d.name} <<<<<";
                                        if (d is ARSystemDevice)
                                        {
                                            controllerLog += $"{d}";
                                            tasks[task_id].gTarget = d;
                                            _name = d.name;
                                            continue;
                                        }
                                    }
                                    controllerLog += $"\n\n>>>>> Reassigned Task  {task_id} to {_name} | {DateTime.Now.ToString()} <<<<<";
                                }
                            }
                        }
                    }

                }
            }
            DA.SetData(1, controllerLog);
        }

        private void OnJobStart()
        {
            currentJobActive = true;
            waitForHuman = true; // the first step is always an "acknolwedgement"
            taskIndex = 0;
            ROSMessageHandler.Advertise(this.wscObj, "WorkerPool/task", "vizor_package/GeneralTask");
            ROSMessageHandler.Subscribe(this.wscObj, "WorkerPool/status", "std_msgs/String");
            ROSMessageHandler.Subscribe(this.wscObj, "Robot/status", "std_msgs/String");
            ROSMessageHandler.PublishTaskList(this.wscObj, taskListMsg);
            ROSMessageHandler.Advertise(this.wscObj, JobSignalTopic, "std_msgs/String");
            controllerLog += "\n\nTask started | " + DateTime.Now.ToString() + "\n";
            ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobStartSignal);
            this.Message = String.Format("{0} Tasks Started", tasks.Count);
        }

        /// <summary>
        /// Dynamic mode, names changed: cancel the running job and restart from the new tasks.
        /// Stops the robot explicitly (Robot/control "stop"), ends the current job cycle,
        /// rebuilds the running set from the new input, and restarts the sequence from step 0.
        /// </summary>
        private void ResetJob(List<GeneralTaskObject> newTasks)
        {
            // Validate the new targets before tearing anything down.
            List<Device> newDevices = TaskUtilities.GetDevicesForTasks(newTasks);
            foreach (Device d in newDevices)
            {
                if (d is null)
                {
                    this.Message = "Error";
                    controllerLog = "ERROR: the input 'device' is null.";
                    return;
                }
            }

            // Halt the currently running job: explicit robot stop, then end the job cycle.
            ROSMessageHandler.Advertise(this.wscObj, "Robot/control", "std_msgs/String");
            ROSMessageHandler.CustomRobotCommand(this.wscObj, "Robot/control", "stop");
            ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobEndSignal);
            currentJobActive = false;
            taskIndex = -1;

            // Rebuild the running set from the new input (mirrors the !start staging path).
            for (int i = 0; i < newTasks.Count; i++)
                newTasks[i].id = i;
            tasks = newTasks;
            rawDevices = newDevices;
            this.UpdateDevices(rawDevices);
            taskListMsg = TaskUtilities.GenerateTaskList(tasks);

            controllerLog += "\n\n>> Tasks changed - job cancelled and RESTARTED from the first task | " + DateTime.Now.ToString();

            // Restart the sequence from step 0 with the new list.
            OnJobStart();
        }

        private void UploadJob()
        {
            DateTime start = DateTime.Now;
            ROSMessageHandler.Advertise(this.wscObj, "DataStore/task/add", "vizor_package/GeneralTask");
            foreach (GeneralTaskObject t in tasks)
            {
                GeneralTaskMsg msg = new GeneralTaskMsg(t);
                ROSMessageHandler.PublishTaskToStore(this.wscObj, msg);
                System.Threading.Thread.Sleep(100); 
            }
            TimeSpan duration = DateTime.Now - start;
            this.Message = "uploaded in " + duration.TotalMilliseconds.ToString() + "ms";
        }

        private void OnJobEnd()
        {
            taskIndex = -1;
            controllerLog += "\n\nTasks are complete! | " + DateTime.Now.ToString();
            ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobEndSignal);
            this.Message = String.Format("{0} Tasks Finished", tasks.Count);
            currentJobActive = false;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Tasks are completed. click start again to reset it. ");
        }

        /// <summary>
        /// Send one task step to the worker pool
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private string StepTask(int index)
        {
            if (this.wscObj != null)
            {
                GeneralTaskObject task = tasks[index];
                task.id = index;
                GeneralTaskMsg taskMsg = new GeneralTaskMsg(task);
                ROSMessageHandler.PublishTaskToPool(this.wscObj, taskMsg);

                waitForHuman = (tasks[index].gTarget is RobotObject)? false : true;

                return tasks[index].name + " sent to " + tasks[index].gTarget.name + " ( " + (index + 1).ToString() + " out of " + tasks.Count.ToString() + " )";
            }
            else return "error";
        }

        /// <summary>
        /// Builds a signature from the ordered task names, used to detect whether a changed
        /// input list is a pure content swap (same names) or a structural change (names differ).
        /// </summary>
        private static string NameSignature(List<GeneralTaskObject> list)
        {
            return string.Join("|", list.Select(t => t.name));
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem controlRoot = Menu_AppendItem(menu, "Control Mode");
            AppendControlModeChoice(controlRoot.DropDown, "Online (GH Control)", true);
            AppendControlModeChoice(controlRoot.DropDown, "Offline (Upload)", false);

            ToolStripMenuItem inputRoot = Menu_AppendItem(menu, "On Input Change");
            AppendInputModeChoice(inputRoot.DropDown, "Reset (manual)", false);
            AppendInputModeChoice(inputRoot.DropDown, "Dynamic Substitute", true);
        }

        private void AppendControlModeChoice(ToolStrip dropdown, string label, bool value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (onlineMode == value) return;
                RecordUndoEvent(label);
                onlineMode = value;
                updateParamName(onlineMode);
                ExpireSolution(true);
            }, true, onlineMode == value);
        }

        private void AppendInputModeChoice(ToolStrip dropdown, string label, bool value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (dynamicInputMode == value) return;
                RecordUndoEvent(label);
                dynamicInputMode = value;
                ExpireSolution(true);
            }, true, dynamicInputMode == value);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("OnlineMode", onlineMode);
            writer.SetBoolean("DynamicInputMode", dynamicInputMode);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool b = false;
            if (reader.TryGetBoolean("OnlineMode", ref b)) onlineMode = b;
            if (reader.TryGetBoolean("DynamicInputMode", ref b)) dynamicInputMode = b;
            return base.Read(reader);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                return Vizor.Properties.Resources.Task_Control;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("64507e61-dceb-41af-b34e-fdf823d4a111"); }
        }
    }
}