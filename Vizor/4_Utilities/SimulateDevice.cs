using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Drawing;
using System.Windows.Forms;
using VizorLibs;

namespace Vizor._1_System
{

    /// <summary>
    /// The SimulateDevice component is used to simulate the behavior of a network device,
    /// allowing manual triggering of various device responses such as acknowledge, reject,
    /// ping, and help messages. It supports both human-operated devices and robots.
    ///
    /// Inputs:
    /// - Network Device (Device): The network device to simulate.
    /// - TaskID (int): The task ID associated with the triggered message.
    ///
    /// The four responses are triggered by buttons drawn on the component itself rather than
    /// by boolean inputs. The button labels follow the device type: an AR worker sends
    /// acknowledge / reject / ping / help, a robot sends success / error / pause / stop.
    ///
    /// Only the first button is shown by default; the other three are added from the right
    /// click menu and that choice is saved with the definition. Buttons are greyed out while
    /// the device is missing or disconnected, and clicking one then reports why nothing was
    /// sent rather than doing nothing.
    ///
    /// A click gives two pieces of feedback: the button is drawn held while the mouse is down,
    /// and a dot marks the button that last actually published, so a click that was swallowed by
    /// a missing device or dead connection is visibly distinct from one that sent. As with any
    /// button, releasing the mouse away from the capsule cancels the press.
    ///
    /// Outputs:
    /// - Output (string): Provides status messages about the simulation, such as
    ///   connection status, message sent confirmations, or errors.
    ///
    /// This component also manages the connection state of the device and ensures
    /// that messages are only sent when the device is connected.
    /// </summary>

    public class SimulateDevice : VizorBaseComponent
    {
        // Indices of the four trigger buttons, in the order they are drawn.
        private const int TriggerAcknowledge = 0;
        private const int TriggerReject = 1;
        private const int TriggerPing = 2;
        private const int TriggerHelp = 3;
        private const int TriggerCount = 4;

        private static readonly string[] HumanLabels = { "Acknowledge", "Reject", "Ping", "Help" };
        private static readonly string[] RobotLabels = { "Success", "Error", "Pause", "Stop" };

        private static readonly string[] HumanDescriptions =
        {
            "trigger the acknowledge message",
            "trigger the reject message",
            "trigger the ping message (i.e. this device is reminding others in the team)",
            "trigger the help message (i.e. this device requires help)"
        };

        private static readonly string[] RobotDescriptions =
        {
            "trigger a robot success message for this task",
            "trigger a robot error for this task",
            "trigger a pause command",
            "trigger a stop command"
        };

        Device mock_device;
        bool isHuman = true;
        bool initiated = false;
        int taskId = 0;

        // Index of the button pressed since the last solution, or -1 when nothing is pending.
        private int pendingTrigger = -1;

        // Index of the button that last published a message, or -1 when nothing has been sent for
        // the current device. Held down is "your click registered"; this marker is "it actually
        // sent", and is deliberately only set after a publish, so a click that fails on a missing
        // or dead connection leaves no mark.
        private int lastSentTrigger = -1;

        // Which buttons are drawn. The first one is the common case and is always present; the
        // rest are opt-in through the context menu so the component stays compact by default.
        private readonly bool[] triggerVisible = { true, false, false, false };

        /// <summary>
        /// True when the button for the given trigger is currently drawn on the component.
        /// </summary>
        private bool IsTriggerVisible(int index)
        {
            return index == TriggerAcknowledge || triggerVisible[index];
        }

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public SimulateDevice()
          : base("Simulate Device", "Simulate",
              "manually trigger the acknowledge response of a device", "4_Utilities")
        {
            isListener = false;
            this.initiated = false;
        }

        /// <summary>
        /// Replaces the default attributes with ones that draw the four trigger buttons.
        /// </summary>
        public override void CreateAttributes()
        {
            m_attributes = new SimulateDeviceAttributes(this);
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Network Device", "Device", "Network device to mock up", GH_ParamAccess.item);
            pManager.AddIntegerParameter("TaskID", "ID", "task ID to request help on", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.item);
        }

        /// <summary>
        /// Labels of the four buttons for the currently simulated device type.
        /// </summary>
        private string[] TriggerLabels
        {
            get { return isHuman ? HumanLabels : RobotLabels; }
        }

        /// <summary>
        /// Tooltip descriptions of the four buttons for the currently simulated device type.
        /// </summary>
        private string[] TriggerDescriptions
        {
            get { return isHuman ? HumanDescriptions : RobotDescriptions; }
        }

        /// <summary>
        /// True when a button press can actually send something, i.e. a single device is
        /// assigned and its connection is live.
        /// </summary>
        private bool CanTrigger
        {
            get
            {
                return !Locked
                    && this.device != null
                    && this.device.wscObj != null
                    && this.device.wscObj.isConnected();
            }
        }

        /// <summary>
        /// Called by the attributes when one of the on-component buttons is clicked. A click is
        /// an event rather than a state, so the pending index is consumed by the next solution.
        /// </summary>
        private void FireTrigger(int index)
        {
            pendingTrigger = index;
            // Schedule rather than ExpireSolution(true): the click arrives inside a canvas
            // mouse event, and recomputing re-entrantly from there is asking for trouble.
            GH_Document doc = OnPingDocument();
            if (doc != null) doc.ScheduleSolution(1, d => ExpireSolution(false));
            else ExpireSolution(true);
        }

        /// <summary>
        /// Repaints the canvas so a button state change shows up without waiting for a solution.
        /// </summary>
        private void RedrawButtons()
        {
            if (Grasshopper.Instances.ActiveCanvas != null) Grasshopper.Instances.RedrawCanvas();
        }

        /// <summary>
        /// Drops the sent marker, e.g. when the device changes or its connection goes away, so it
        /// never implies a message was sent to the device now wired in.
        /// </summary>
        private void ClearSentMarker()
        {
            if (lastSentTrigger < 0) return;
            lastSentTrigger = -1;
            RedrawButtons();
        }

        /// <summary>
        /// Adds a checked entry per optional button, so the extra responses can be shown on
        /// demand. The first trigger has no entry because it is always drawn.
        /// </summary>
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            string[] labels = TriggerLabels;
            string[] descriptions = TriggerDescriptions;
            for (int i = TriggerAcknowledge + 1; i < TriggerCount; i++)
            {
                int index = i;
                ToolStripMenuItem item = Menu_AppendItem(menu, labels[index] + " button",
                    (sender, e) => ToggleTrigger(index), true, triggerVisible[index]);
                item.ToolTipText = descriptions[index];
            }
        }

        /// <summary>
        /// Shows or hides one of the optional buttons and relays out the component.
        /// </summary>
        private void ToggleTrigger(int index)
        {
            RecordUndoEvent(triggerVisible[index] ? "Hide button" : "Show button");
            triggerVisible[index] = !triggerVisible[index];
            if (Attributes != null) Attributes.ExpireLayout();
            if (Grasshopper.Instances.ActiveCanvas != null) Grasshopper.Instances.RedrawCanvas();
        }

        /// <summary>
        /// Persists which optional buttons are shown, so the choice survives save and reload.
        /// </summary>
        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            for (int i = TriggerAcknowledge + 1; i < TriggerCount; i++)
            {
                writer.SetBoolean("TriggerVisible" + i, triggerVisible[i]);
            }
            return base.Write(writer);
        }

        /// <summary>
        /// Restores which optional buttons are shown. Definitions saved before this setting
        /// existed simply get the default of showing only the first button.
        /// </summary>
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            for (int i = TriggerAcknowledge + 1; i < TriggerCount; i++)
            {
                bool visible = false;
                triggerVisible[i] = reader.TryGetBoolean("TriggerVisible" + i, ref visible) && visible;
            }
            return base.Read(reader);
        }

        /// <summary>
        /// Switches the button labels between the human and robot sets.
        /// </summary>
        private void SetMode(bool human)
        {
            if (isHuman == human) return;
            isHuman = human;
            // The labels change with the mode, so a marker from the old set would now sit on a
            // button that means something else.
            lastSentTrigger = -1;
            if (Attributes != null) Attributes.ExpireLayout();
            if (Grasshopper.Instances.ActiveCanvas != null) Grasshopper.Instances.RedrawCanvas();
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Consume the pending button press. Anything that stops us short of publishing
            // below discards it rather than letting it fire on an unrelated recompute.
            int trigger = pendingTrigger;
            pendingTrigger = -1;

            mock_device = null;
            DA.GetData(0, ref mock_device);
            DA.GetData(1, ref taskId);

            if (mock_device == null)
            {
                this.device = null;
                CleanupConnection();
                SetMode(true);
                ClearSentMarker();
                if (trigger >= 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Nothing was sent: no device is assigned.");
                DA.SetData(0, "no device");
                return;
            }

            if (mock_device.name.Contains(","))
            {
                this.Message = "error";
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cannot simulate multiple devices. Create individual devices first and simualte them separately. ");
                return;
            }

            if (UpdateDevice(mock_device))
            {
                this.initiated = false;
                ClearSentMarker();
            }

            SetMode(!(mock_device is RobotObject));
            this.Message = mock_device.name;

            // if the device is not connected, return and ignore
            if (this.device.wscObj == null || !this.device.wscObj.isConnected())
            {
                this.initiated = false;
                ClearSentMarker();
                if (trigger >= 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Nothing was sent: the device has no live connection.");
                DA.SetData(0, "no connection");
                return;
            }

            if (!this.initiated)
            {
                if (isHuman)
                {
                    ROSMessageHandler.Advertise(device.wscObj, device.name + "_Command", "std_msgs/String");
                    ROSMessageHandler.Advertise(device.wscObj, "WorkerPool_ShortMsg", "std_msgs/String");
                }
                else
                {
                    ROSMessageHandler.Advertise(device.wscObj, "Robot/status", "std_msgs/String");
                    ROSMessageHandler.Advertise(device.wscObj, "Robot/control", "std_msgs/String");
                }
                this.initiated = true;
            }

            if (trigger < 0)
            {
                DA.SetData(0, String.Format("{0} ready.", mock_device.name));
                return;
            }

            switch (trigger)
            {
                // task completion messages

                case TriggerAcknowledge:
                    if (isHuman)
                    {
                        ROSMessageHandler.PublishMockAcknowledge(mock_device.wscObj, mock_device.name + "_Command");
                        DA.SetData(0, String.Format("AR Device {0} sent acknowledge message at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    else
                    {
                        ROSMessageHandler.PublishRobotStatus(mock_device.wscObj, "Robot/status", taskId, true);
                        DA.SetData(0, String.Format("Robot {0} sent acknowledge message at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    break;

                // task rejection messages

                case TriggerReject:
                    if (isHuman)
                    {
                        ROSMessageHandler.PublishMockReject(mock_device.wscObj, mock_device.name + "_Command", mock_device.name, taskId);
                        DA.SetData(0, String.Format("AR Device {0} sent reject message at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    else
                    {
                        ROSMessageHandler.PublishRobotStatus(mock_device.wscObj, "Robot/status", taskId, false);
                        DA.SetData(0, String.Format("Robot {0} sent error message at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    break;

                // emote messages

                case TriggerPing:
                    if (isHuman)
                    {
                        ROSMessageHandler.PublishMockHurry(mock_device.wscObj, "WorkerPool_ShortMsg", mock_device.name);
                        DA.SetData(0, String.Format("AR Device {0} sent hurry message at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    else
                    {
                        ROSMessageHandler.PublishRobotControl(mock_device.wscObj, "Robot/control", "pause");
                        DA.SetData(0, String.Format("Robot {0} sent pause command at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    break;

                case TriggerHelp:
                    if (isHuman)
                    {
                        ROSMessageHandler.PublishMockHelp(mock_device.wscObj, "WorkerPool_ShortMsg", mock_device.name, taskId);
                        DA.SetData(0, String.Format("AR Device {0} asked for help at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    else
                    {
                        ROSMessageHandler.PublishRobotControl(mock_device.wscObj, "Robot/control", "stop");
                        DA.SetData(0, String.Format("Robot {0} sent stop command at {1}.", mock_device.name, DateTime.Now.ToString()));
                    }
                    break;
            }

            // Reached only past the device and connection guards, so the marker means a message
            // really was published rather than merely that the button was clicked.
            lastSentTrigger = trigger;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                return Vizor.Properties.Resources.Simulate_Device;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("1d5fcb9f-8af4-4cdc-b166-c15f958f4bde"); }
        }

        /// <summary>
        /// Draws four clickable capsules underneath the parameter grips and turns clicks on them
        /// into trigger events on the owning component. Only the height of the component is
        /// extended, so the parameter layout produced by the base class stays intact.
        /// </summary>
        private class SimulateDeviceAttributes : GH_ComponentAttributes
        {
            private const int ButtonHeight = 20;
            private const int ButtonGap = 3;
            private const int ButtonInset = 2;

            // Dot marking the button that last published a message.
            private const float MarkerDiameter = 6f;
            private const float MarkerInset = 6f;
            private static readonly Color MarkerColour = Color.FromArgb(255, 138, 214, 122);

            private readonly RectangleF[] buttonBounds = new RectangleF[TriggerCount];

            // Index of the button currently held down, or -1. Non-negative exactly while the
            // canvas mouse is captured by this attributes instance.
            private int pressedIndex = -1;

            public SimulateDeviceAttributes(SimulateDevice owner) : base(owner) { }

            private SimulateDevice Component
            {
                get { return (SimulateDevice)Owner; }
            }

            protected override void Layout()
            {
                base.Layout();

                int visibleCount = 0;
                for (int i = 0; i < TriggerCount; i++)
                {
                    if (Component.IsTriggerVisible(i)) visibleCount++;
                }

                RectangleF bounds = Bounds;
                float gridHeight = visibleCount * ButtonHeight + ButtonGap;
                bounds.Height += gridHeight;
                Bounds = bounds;

                // Hidden buttons keep an empty rectangle, which never contains a click and is
                // skipped when drawing, so visibility needs no separate bookkeeping.
                float top = bounds.Bottom - gridHeight + ButtonGap;
                int row = 0;
                for (int i = 0; i < buttonBounds.Length; i++)
                {
                    if (!Component.IsTriggerVisible(i))
                    {
                        buttonBounds[i] = RectangleF.Empty;
                        continue;
                    }
                    buttonBounds[i] = new RectangleF(bounds.X, top + row * ButtonHeight, bounds.Width, ButtonHeight);
                    buttonBounds[i].Inflate(-ButtonInset, -ButtonInset);
                    row++;
                }
            }

            protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
            {
                base.Render(canvas, graphics, channel);
                if (channel != GH_CanvasChannel.Objects) return;

                bool enabled = Component.CanTrigger;
                string[] labels = Component.TriggerLabels;

                for (int i = 0; i < buttonBounds.Length; i++)
                {
                    if (buttonBounds[i].IsEmpty) continue;
                    Rectangle box = GH_Convert.ToRectangle(buttonBounds[i]);
                    GH_Palette palette = enabled ? GH_Palette.Black : GH_Palette.Grey;
                    using (GH_Capsule capsule = GH_Capsule.CreateTextCapsule(box, box, palette, labels[i], 2, 0))
                    {
                        // The selected style is reused as the held look: it is the one highlight
                        // guaranteed to be legible against every palette and canvas theme.
                        capsule.Render(graphics, i == pressedIndex, Owner.Locked, false);
                    }

                    if (Component.lastSentTrigger == i) RenderSentMarker(graphics, box);
                }
            }

            /// <summary>
            /// Draws the dot marking the button that last published, inset from the right edge so
            /// it never collides with the centred label.
            /// </summary>
            private void RenderSentMarker(Graphics graphics, Rectangle box)
            {
                float top = box.Top + (box.Height - MarkerDiameter) / 2f;
                RectangleF dot = new RectangleF(box.Right - MarkerInset - MarkerDiameter, top, MarkerDiameter, MarkerDiameter);
                using (SolidBrush brush = new SolidBrush(MarkerColour))
                {
                    graphics.FillEllipse(brush, dot);
                }
            }

            // Capture on mouse down so the button can be drawn held and the press can be cancelled
            // by releasing elsewhere. Capture and pressedIndex are set on the same path and must
            // stay that way: an earlier version captured without setting the index when the device
            // was disconnected, so mouse up found no press to release, returned no Release, and
            // the canvas stayed captured for the rest of the session with all mouse input dead.
            public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    for (int i = 0; i < buttonBounds.Length; i++)
                    {
                        if (!buttonBounds[i].Contains(e.CanvasLocation)) continue;
                        // Press even with no live connection: the solution reports why nothing was
                        // sent, which beats a button that silently ignores the user.
                        pressedIndex = i;
                        sender.Refresh();
                        return GH_ObjectResponse.Capture;
                    }
                }
                return base.RespondToMouseDown(sender, e);
            }

            // Every captured press releases here, whatever it was pressed on and wherever it is
            // released, which is what keeps the capture from leaking.
            public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
            {
                if (pressedIndex < 0) return base.RespondToMouseUp(sender, e);

                int index = pressedIndex;
                pressedIndex = -1;
                sender.Refresh();
                // Releasing away from the capsule cancels the press, as with any button.
                if (buttonBounds[index].Contains(e.CanvasLocation)) Component.FireTrigger(index);
                return GH_ObjectResponse.Release;
            }

            public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs e)
            {
                for (int i = 0; i < buttonBounds.Length; i++)
                {
                    if (!buttonBounds[i].Contains(point)) continue;
                    e.Title = Component.TriggerLabels[i];
                    e.Text = Component.TriggerDescriptions[i];
                    e.Description = Component.CanTrigger ? string.Empty : "device is not connected";
                    return;
                }
                base.SetupTooltip(point, e);
            }
        }
    }
}
