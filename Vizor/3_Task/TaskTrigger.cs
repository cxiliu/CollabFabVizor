using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Vizor._1_System;
using VizorLibs;
using VizorLibs.MessageTypes;

namespace Vizor._4_Task
{
    /// <summary>
    /// TaskTrigger dispatches tasks on demand: each message on "Task/activate" publishes exactly
    /// one task, addressed by ID or by name. Unlike <see cref="TaskController"/> it holds no
    /// sequence state — the backend decides what runs next and when. It publishes the activatable
    /// set once on "Task/listing" at start, and brackets a session on "Task/signal" with
    /// "start_job_trig" / "end_job_trig".
    ///
    /// Inputs:
    /// - Start: Boolean input to begin accepting activation messages.
    /// - HRC Tasks: List of GeneralTaskObject that may be activated.
    ///
    /// Outputs:
    /// - Current Task: The task most recently dispatched.
    /// - Process Log: Log of the activation history, including errors.
    ///
    /// Note: safe to sit on a canvas alongside TaskController, but the two should not both be
    /// running against the same worker pool — "Task/listing" is a replacement rather than a merge,
    /// both number their tasks from 0, and both publish to "WorkerPool/task".
    /// </summary>
    public class TaskTrigger : VizorBaseComponent
    {
        private const string ActivateTopic = "Task/activate";
        private const string StringType = "std_msgs/String";

        // Same topic as TaskController, but the payloads carry a "_trig" suffix: Task/signal has
        // no sender field, so without it a listener could not tell which controller's session
        // ended when both are live.
        private const string JobSignalTopic = "Task/signal";
        private const string JobStartSignal = "start_job_trig";
        private const string JobEndSignal = "end_job_trig";

        // Activations are captured on the websocket thread and drained on the UI thread, so a
        // burst can outpace Grasshopper's solves. The cap stops a runaway publisher growing the
        // queue without bound; overflow is reported rather than swallowed.
        private const int MaxQueuedActivations = 32;

        // gh input
        private bool start;
        private List<GeneralTaskObject> tasks;

        // runtime var
        private string controllerLog = string.Empty;
        private TaskListMsg taskListMsg;
        private List<Device> rawDevices;

        // Written on the UI thread, read on the websocket thread.
        private volatile bool currentJobActive;

        // The last task published, re-emitted on every solve so an unrelated re-solve does not
        // blank the port and drop downstream geometry.
        private GeneralTaskObject lastDispatched;

        // Tracked separately from wscObj so the component detaches from the socket it actually
        // attached to, not the one currently wired.
        private WsObject attachedSocket;
        private bool subscribed;

        // Captured on the websocket thread, drained on the UI thread.
        private readonly object captureLock = new object();
        private readonly Queue<string> pendingActivations = new Queue<string>();
        private bool queueOverflowed;

        /// <summary>
        /// Sent as the rosbridge "id" on subscribe and echoed on unsubscribe. The socket is shared,
        /// so an id-less unsubscribe would drop every other component's subscription to this topic.
        /// </summary>
        private string SubscriptionId
        {
            get { return "TaskTrigger-" + InstanceGuid.ToString("N"); }
        }

        /// <summary>
        /// Initializes a new instance of the TaskTrigger class.
        /// </summary>
        public TaskTrigger()
          : base("Task Trigger", "TaskTrig",
              "Dispatch a task on demand when an activation message arrives on 'Task/activate'.",
              "3_Task")
        {
            // The base-class listener plumbing reads WsObject.message inside SolveInstance, which
            // races every other listener on the connection. This component wires its own handler
            // and captures on the websocket thread instead; see OnSocketChanged.
            isListener = false;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start", "Start", "Toggle to begin accepting activation messages", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("HRC Tasks", "Tasks", "List of tasks that may be activated", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Current Task", "Task", "Task most recently dispatched", GH_ParamAccess.item);
            pManager.AddTextParameter("Process Log", "Log", "Process log with activation history", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!IsDocumentActive()) return;

            // Phase 1: not message-triggered (preparation and job lifecycle)
            if (!this.onMessageTriggered)
            {
                DA.GetData(0, ref start);

                if (!start)
                {
                    // currentJobActive is only true once a job has started, so this fires exactly
                    // once on the true -> false transition rather than on every idle solve.
                    if (currentJobActive) OnJobEnd();

                    tasks = new List<GeneralTaskObject>();
                    DA.GetDataList(1, tasks);

                    if (tasks.Count != 0)
                    {
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

                        // Resolves this.wscObj from the task targets. With isListener false it
                        // performs no subscribe/unsubscribe, so the handler is wired separately.
                        this.UpdateDevices(rawDevices);
                        AttachToSocket();

                        // impose artificial ID on task list
                        for (int i = 0; i < tasks.Count; i++)
                        {
                            tasks[i].id = i;
                        }

                        List<string> duplicateTaskNames = tasks
                            .Where(t => !string.IsNullOrWhiteSpace(t.name))
                            .GroupBy(t => t.name)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();
                        string duplicateNameWarning = string.Empty;
                        if (duplicateTaskNames.Count > 0)
                        {
                            string duplicateNameList = string.Join(", ", duplicateTaskNames);
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Duplicate task names detected: " + duplicateNameList + ". Activate by task ID to disambiguate.");
                            duplicateNameWarning = "WARNING: Duplicate task names detected (" + duplicateNameList + "). Activate by task ID to disambiguate.";
                        }

                        taskListMsg = TaskUtilities.GenerateTaskList(tasks);

                        controllerLog = string.IsNullOrWhiteSpace(duplicateNameWarning)
                            ? "Tasks Generated - ready. Toggle start to begin."
                            : duplicateNameWarning + "\nTasks Generated - ready. Toggle start to begin.";
                        this.Message = String.Format("{0} Tasks Pending", tasks.Count);
                    }
                }

                // taskListMsg survives from a previous run, so it alone does not prove there is
                // anything to activate: clearing the Tasks input leaves a stale listing behind and
                // would otherwise open a session that can never match an activation.
                bool hasTasks = taskListMsg != null && tasks != null && tasks.Count > 0;

                // When start turns true: open the session
                if (this.wscObj != null && !currentJobActive &&
                    this.wscObj.isConnected() && hasTasks && start)
                {
                    OnJobStart();
                }
                else if (start && !hasTasks)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No tasks available. Set Start to false, provide tasks, then set Start to true.");
                    controllerLog = "WARNING: No tasks available. Set Start to false, provide tasks, then set Start to true.";
                }

                EmitOutputs(DA);
                return;
            }

            // Phase 2: message-triggered (dispatch whatever was captured)
            this.onMessageTriggered = false;
            DrainActivations();
            EmitOutputs(DA);
        }

        /// <summary>
        /// Writes both output ports. The current task is re-emitted every solve so downstream
        /// components keep their data when an unrelated change re-solves the document.
        /// </summary>
        private void EmitOutputs(IGH_DataAccess DA)
        {
            if (lastDispatched != null) DA.SetData(0, lastDispatched);
            DA.SetData(1, controllerLog);
        }

        #region JOB LIFECYCLE

        /// <summary>
        /// Opens a session: advertises the outgoing topics, subscribes to activations, publishes
        /// the activatable set and signals the start.
        /// </summary>
        private void OnJobStart()
        {
            currentJobActive = true;
            ClearActivations();
            lastDispatched = null;

            ROSMessageHandler.Advertise(this.wscObj, "WorkerPool/task", "vizor_package/GeneralTask");
            ROSMessageHandler.Advertise(this.wscObj, JobSignalTopic, StringType);
            SubscribeToActivations();
            ROSMessageHandler.PublishTaskList(this.wscObj, taskListMsg);
            ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobStartSignal);

            controllerLog += "\n\nTask Trigger started | " + DateTime.Now.ToString() + "\n";
            controllerLog += "Listening for activation messages on '" + ActivateTopic + "'...\n";

            this.Message = String.Format("{0} Tasks Ready", tasks.Count);
        }

        /// <summary>
        /// Closes a session: signals the end, then stops listening so the subscription does not
        /// outlive the job and keep waking the component for nothing.
        /// </summary>
        private void OnJobEnd()
        {
            if (this.wscObj != null)
                ROSMessageHandler.CustomRobotCommand(this.wscObj, JobSignalTopic, JobEndSignal);

            UnsubscribeFromActivations();
            currentJobActive = false;
            ClearActivations();
            lastDispatched = null;

            controllerLog += "\n\nTask Trigger stopped | " + DateTime.Now.ToString();
            this.Message = "Stopped";
        }

        #endregion

        #region SUBSCRIPTION LIFECYCLE

        /// <summary>
        /// Attaches the message handler to the current socket, detaching from the previous one.
        /// </summary>
        private void AttachToSocket()
        {
            if (ReferenceEquals(this.wscObj, attachedSocket)) return;

            DetachFromSocket();

            if (this.wscObj == null) return;
            attachedSocket = this.wscObj;
            attachedSocket.changed += this.OnSocketChanged;
        }

        private void DetachFromSocket()
        {
            if (attachedSocket == null) return;

            UnsubscribeFromActivations();
            try { attachedSocket.changed -= this.OnSocketChanged; }
            catch { }
            attachedSocket = null;
        }

        private void SubscribeToActivations()
        {
            if (subscribed || attachedSocket == null) return;
            ROSMessageHandler.Subscribe(attachedSocket, ActivateTopic, StringType, SubscriptionId);
            subscribed = true;
        }

        private void UnsubscribeFromActivations()
        {
            if (!subscribed) return;
            if (attachedSocket != null)
                ROSMessageHandler.Unsubscribe(attachedSocket, ActivateTopic, StringType, SubscriptionId);
            subscribed = false;
        }

        /// <summary>
        /// Stop listening when the component is deleted, so the rosbridge subscription does not
        /// outlive it and keep waking a dead handler.
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            DetachFromSocket();
            base.RemovedFromDocument(document);
        }

        #endregion

        #region INCOMING MESSAGES

        /// <summary>
        /// Runs on the websocket thread. The activation is filtered and copied out here rather
        /// than in SolveInstance, because WsObject.message is a single slot shared by every topic
        /// on the connection: by the time a re-solve runs, another component may have overwritten
        /// or cleared it. For the same reason this method never writes to that slot — doing so
        /// would destroy messages belonging to every other listener.
        /// </summary>
        private void OnSocketChanged(object sender, EventArgs e)
        {
            WsObject socket = sender as WsObject;
            if (socket == null) return;

            // Idle: nothing to dispatch, and the slot belongs to whoever else is listening.
            if (!currentJobActive) return;

            // WsObject raises this same event on open, close and error without clearing the
            // message slot, so without this guard a reconnect would replay the last activation
            // and dispatch the same task a second time.
            if (socket.status != WsObject.ConnectionStatus.MESSAGE) return;
            if (socket.message == null) return;

            if (!Capture(socket.message)) return;

            // Re-solve through the base class so the guarding stays in one place.
            WscObjOnChanged(sender, e);
        }

        /// <summary>
        /// Parses an incoming envelope and queues the payload if it is an activation for us.
        /// </summary>
        /// <returns>True if a re-solve is warranted, i.e. the message was ours.</returns>
        private bool Capture(string message)
        {
            string activation;

            try
            {
                JObject root = JObject.Parse(message);

                // Publish frames carry a topic; service and status frames do not. Compared with
                // slash tolerance because rosbridge echoes topics as they were published.
                if (!ROSMessageHandler.SameTopic((string)root["topic"], ActivateTopic)) return false;

                JToken msgToken = root["msg"];
                JToken dataToken = msgToken == null ? null : msgToken["data"];
                if (dataToken == null) return false;
                if (dataToken.Type == JTokenType.Object || dataToken.Type == JTokenType.Array) return false;

                // A numeric payload converts to its string form, so a task ID published as an int
                // reads the same as one published as a string.
                activation = (string)dataToken;
            }
            catch
            {
                return false; // not JSON, or not an envelope this component can read
            }

            if (string.IsNullOrWhiteSpace(activation)) return false;

            lock (captureLock)
            {
                if (pendingActivations.Count >= MaxQueuedActivations)
                {
                    queueOverflowed = true;
                    return true; // still re-solve, so the overflow warning surfaces
                }
                pendingActivations.Enqueue(activation.Trim());
            }
            return true;
        }

        /// <summary>
        /// Dispatches every activation captured since the last solve. Queued rather than kept in a
        /// single slot so a burst arriving between solves does not silently lose all but the last.
        /// </summary>
        private void DrainActivations()
        {
            List<string> batch = new List<string>();
            bool overflowed;

            lock (captureLock)
            {
                while (pendingActivations.Count > 0) batch.Add(pendingActivations.Dequeue());
                overflowed = queueOverflowed;
                queueOverflowed = false;
            }

            if (overflowed)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Activations arrived faster than they could be dispatched; some were dropped.");
                controllerLog += "\n>> WARNING: activation queue overflowed, some activations were dropped | " + DateTime.Now.ToString();
            }

            if (!currentJobActive) return;

            foreach (string activation in batch)
            {
                int taskId;
                if (int.TryParse(activation, out taskId)) ActivateTaskById(taskId);
                else ActivateTaskByName(activation);
            }
        }

        #endregion

        #region DISPATCH

        /// <summary>
        /// Activates a task by its ID.
        /// </summary>
        private void ActivateTaskById(int id)
        {
            GeneralTaskObject task = tasks == null ? null : tasks.Find(t => t.id == id);

            if (task == null)
            {
                controllerLog += String.Format("\n>> ERROR: Task ID {0} not found | {1}", id, DateTime.Now.ToString());
                this.Message = "Error: Task Not Found";
                return;
            }

            if (PublishTask(task))
                controllerLog += String.Format("\n>> Activated Task ID {0}: {1} | {2}", id, task.name, DateTime.Now.ToString());
            else
                controllerLog += String.Format("\n>> ERROR: Task ID {0} not published, no connection | {1}", id, DateTime.Now.ToString());
        }

        /// <summary>
        /// Activates a task by its name.
        /// </summary>
        private void ActivateTaskByName(string name)
        {
            GeneralTaskObject task = tasks == null ? null : tasks.Find(t => t.name == name);

            if (task == null)
            {
                controllerLog += String.Format("\n>> ERROR: Task name '{0}' not found | {1}", name, DateTime.Now.ToString());
                this.Message = "Error: Task Not Found";
                return;
            }

            if (PublishTask(task))
                controllerLog += String.Format("\n>> Activated Task '{0}' (ID {1}) | {2}", name, task.id, DateTime.Now.ToString());
            else
                controllerLog += String.Format("\n>> ERROR: Task '{0}' not published, no connection | {1}", name, DateTime.Now.ToString());
        }

        /// <summary>
        /// Publishes a task to the worker pool.
        /// </summary>
        /// <returns>True if the task was published, so the caller does not log a dispatch that never happened.</returns>
        private bool PublishTask(GeneralTaskObject task)
        {
            if (this.wscObj == null || !this.wscObj.isConnected()) return false;

            GeneralTaskMsg taskMsg = new GeneralTaskMsg(task);
            ROSMessageHandler.PublishTaskToPool(this.wscObj, taskMsg);

            lastDispatched = task;
            this.Message = String.Format("Task {0}: {1}", task.id, task.name);
            return true;
        }

        private void ClearActivations()
        {
            lock (captureLock)
            {
                pendingActivations.Clear();
                queueOverflowed = false;
            }
        }

        #endregion

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Vizor.Properties.Resources.Dyn_Control;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("5EEF1920-D124-4ED0-A2EC-14143CB41535"); }
        }
    }
}
