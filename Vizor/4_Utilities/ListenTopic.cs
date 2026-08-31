using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Windows.Forms;
using VizorLibs;

namespace Vizor._1_System
{
    /// <summary>
    /// Report the latest message heard on a ROS topic via WebSocket.
    /// The read-side counterpart of <see cref="PublishTopic"/>: message type is selected
    /// from the right-click context menu, and "Raw (any)" passes the payload through as JSON.
    /// </summary>
    public class ListenTopic : VizorBaseComponent
    {
        // Menu mode stored in the file. "raw" is a sentinel, the rest are real ROS type names.
        private const string RawMode = "raw";
        private const string RawLabel = "Raw (any)";

        private string messageType = RawMode;

        // Subscription state. Kept separately from the current inputs so the component can
        // unsubscribe from what it actually subscribed to, not from what is wired now.
        private WsObject listeningSocket;
        private string listeningTopic;
        private string listeningType;
        private bool listening;

        // Sent as the rosbridge "id" on subscribe, and echoed back on any status frame about
        // this subscription. The socket is shared, so without it a rosbridge error could not be
        // told apart from one caused by another component.
        private string SubscriptionId
        {
            get { return "ListenTopic-" + InstanceGuid.ToString("N"); }
        }

        // Captured on the websocket thread, read on the UI thread. Guarded by captureLock
        // because payload, timestamp and error are only meaningful as a set.
        private readonly object captureLock = new object();
        private IGH_Goo capturedPayload;
        private string capturedTimestamp;
        private string capturedError;
        // What the capture came from, so a payload is never emitted after the component has been
        // pointed somewhere else. Tracked separately from the subscription, which is torn down
        // whenever Active goes false and so cannot answer the question on its own.
        private WsObject capturedSocket;
        private string capturedTopic;

        public ListenTopic()
          : base("Listen Topic", "ListenTopic",
              "Report the latest message heard on a specified topic via WebSocket.", "4_Utilities")
        {
            isListener = false; // the base-class device plumbing is not used; see AttachListener
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("WSC", "WSC", "WebSocket connection object", GH_ParamAccess.item);
            pManager.AddTextParameter("Topic Name", "Topic", "Topic to listen to", GH_ParamAccess.item, "/Robot/status");
            pManager.AddBooleanParameter("Active", "A", "Set to true to subscribe and report incoming messages", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Payload", "Out", "Content of the last message heard on the topic", GH_ParamAccess.item);
            pManager.AddTextParameter("Timestamp", "T", "Local time the last message arrived", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!IsDocumentActive()) return;

            WsObject socket = null;
            string topicName = "";
            bool active = false;

            DA.GetData(0, ref socket);
            DA.GetData(1, ref topicName);
            DA.GetData(2, ref active);

            this.onMessageTriggered = false;
            this.wscObj = socket;

            // WsConnection treats OPEN and MESSAGE as connected (WsConnection.cs:151) while
            // isConnected() inspects the native socket state. Accept either, so this component
            // never refuses to subscribe to a connection WsConnection is reporting as live.
            bool connected = socket != null
                             && (socket.isConnected()
                                 || socket.status == WsObject.ConnectionStatus.OPEN
                                 || socket.status == WsObject.ConnectionStatus.MESSAGE);
            bool hasTopic = !string.IsNullOrWhiteSpace(topicName);
            bool shouldListen = active && connected && hasTopic;

            // Drop a subscription that no longer matches the wiring, the topic or the menu mode.
            if (listening && (!shouldListen
                              || !ReferenceEquals(socket, listeningSocket)
                              || topicName != listeningTopic
                              || SubscribeType() != listeningType))
            {
                StopListening();
            }

            if (shouldListen && !listening) StartListening(socket, topicName);

            this.Message = string.Format("{0}\n{1} · {2}",
                hasTopic ? topicName : "(no topic)",
                ModeLabel(messageType),
                listening ? "listening" : "idle");

            // Configuration problems are reported as warnings rather than on a status port,
            // so a misconfigured component is visible across the whole canvas.
            if (socket == null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No WebSocket connection object provided.");
            else if (!hasTopic)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Topic name is required.");
            else if (!connected)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "WebSocket is not connected.");

            IGH_Goo payload;
            string timestamp;
            string error;
            lock (captureLock)
            {
                // Discard anything captured from a topic or connection the component has since
                // been moved off, so the port never reports a value it is no longer listening for.
                if ((capturedTopic != null && !SameTopic(capturedTopic, topicName))
                    || (capturedSocket != null && !ReferenceEquals(capturedSocket, socket)))
                {
                    ClearCapture();
                }

                payload = capturedPayload;
                timestamp = capturedTimestamp;
                error = capturedError;
            }

            if (error != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
                return;
            }

            if (payload == null)
            {
                if (shouldListen)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Nothing heard on '" + topicName + "' yet.");
                return;
            }

            DA.SetData(0, payload);
            DA.SetData(1, timestamp);
        }

        #region SUBSCRIPTION LIFECYCLE

        /// <summary>
        /// The type declared to rosbridge on subscribe, or null in raw mode.
        /// rosbridge rejects a subscribe whose declared type conflicts with the topic's real
        /// type, so raw mode must omit the field entirely and let rosbridge resolve it rather
        /// than assert a placeholder that would be wrong on every non-string topic.
        /// </summary>
        private string SubscribeType()
        {
            return messageType == RawMode ? null : messageType;
        }

        /// <summary>
        /// Sends a subscribe or unsubscribe. Built here rather than through ROSMessageHandler
        /// because that helper always emits a "type" field (ROSMessage.type has no null
        /// handling, so an unset type would serialize as "type": null) and cannot carry an id.
        /// </summary>
        private void SendSubscription(WsObject socket, string op, string topicName, string type)
        {
            JObject frame = new JObject();
            frame["op"] = op;
            frame["id"] = SubscriptionId;
            frame["topic"] = topicName;
            if (!string.IsNullOrEmpty(type)) frame["type"] = type;

            // JsonConvert.SerializeObject rather than JToken.ToString(Formatting): Rhino 8 ships
            // Newtonsoft.Json 13.0.3 and wins the assembly binding over the 13.0.4 this builds
            // against, so any 13.0.4-only overload throws MissingMethodException at runtime.
            socket.send(JsonConvert.SerializeObject(frame));
        }

        private void StartListening(WsObject socket, string topicName)
        {
            listeningSocket = socket;
            listeningTopic = topicName;
            listeningType = SubscribeType();
            listening = true;

            socket.changed += this.OnSocketChanged;
            SendSubscription(socket, "subscribe", listeningTopic, listeningType);
        }

        private void StopListening()
        {
            if (!listening) return;

            if (listeningSocket != null)
            {
                try { listeningSocket.changed -= this.OnSocketChanged; }
                catch { }
                SendSubscription(listeningSocket, "unsubscribe", listeningTopic, listeningType);
            }

            listening = false;
            listeningSocket = null;
            listeningTopic = null;
            listeningType = null;
        }

        /// <summary>
        /// Unsubscribe when the component is deleted, so the rosbridge subscription does not
        /// outlive it and keep waking a dead handler.
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            StopListening();
            base.RemovedFromDocument(document);
        }

        #endregion

        #region INCOMING MESSAGES

        /// <summary>
        /// Runs on the websocket thread. The message is filtered and copied out here rather than
        /// in SolveInstance, because WsObject.message is a single slot shared by every topic on
        /// the connection: by the time a re-solve runs, another component's topic may have
        /// overwritten it. Every other listener in this project reads it in SolveInstance and is
        /// exposed to that race.
        /// </summary>
        private void OnSocketChanged(object sender, EventArgs e)
        {
            WsObject socket = sender as WsObject;
            if (socket == null || socket.message == null) return;

            // WsObject raises this same event on open, close and error without clearing the
            // message slot, so without this guard a reconnect would re-capture the previous
            // message and stamp it with a fresh arrival time.
            if (socket.status != WsObject.ConnectionStatus.MESSAGE) return;

            if (!Capture(socket, socket.message)) return;

            // Re-solve through the base class so the guarding stays in one place.
            WscObjOnChanged(sender, e);
        }

        /// <summary>
        /// Parses an incoming envelope and stores the payload if it belongs to this topic.
        /// </summary>
        /// <returns>True if a re-solve is warranted, i.e. the message was ours.</returns>
        private bool Capture(WsObject socket, string message)
        {
            JToken msgToken;
            string expectedTopic = listeningTopic;

            try
            {
                JObject root = JObject.Parse(message);

                // A rosbridge status frame tagged with our id is the server explaining why this
                // subscription failed. Without surfacing it the component can only report
                // silence, which is what a rejected subscribe and an idle topic look like alike.
                if ((string)root["op"] == "status" && (string)root["id"] == SubscriptionId)
                {
                    string level = (string)root["level"];
                    if (level == "error" || level == "warning")
                    {
                        lock (captureLock)
                        {
                            ClearCapture();
                            capturedError = "rosbridge " + level + ": " + (string)root["msg"];
                        }
                        return true;
                    }
                    return false;
                }

                // Publish frames carry a topic; service and untagged status frames do not.
                string topic = (string)root["topic"];
                if (topic == null || !SameTopic(topic, expectedTopic)) return false;
                msgToken = root["msg"];
            }
            catch
            {
                return false; // not JSON, or not an envelope this component can read
            }

            IGH_Goo goo;
            string error;
            bool ok = TryExtract(msgToken, out goo, out error);

            lock (captureLock)
            {
                capturedSocket = socket;
                capturedTopic = expectedTopic;

                if (ok)
                {
                    capturedPayload = goo;
                    capturedTimestamp = DateTime.Now.ToString();
                    capturedError = null;
                }
                else
                {
                    capturedPayload = null;
                    capturedTimestamp = null;
                    capturedError = error;
                }
            }
            return true;
        }

        /// <summary>
        /// Compares topics ignoring a leading slash. Shared with TaskTrigger, which needs the
        /// same tolerance when matching its activation topic.
        /// </summary>
        private static bool SameTopic(string a, string b)
        {
            return ROSMessageHandler.SameTopic(a, b);
        }

        /// <summary>
        /// Drops the captured message. Callers must hold captureLock.
        /// </summary>
        private void ClearCapture()
        {
            capturedPayload = null;
            capturedTimestamp = null;
            capturedError = null;
            capturedSocket = null;
            capturedTopic = null;
        }

        /// <summary>
        /// Converts the payload to the goo type matching the selected mode.
        /// </summary>
        private bool TryExtract(JToken msgToken, out IGH_Goo goo, out string error)
        {
            goo = null;
            error = null;

            if (msgToken == null)
            {
                error = "Message on this topic carried no 'msg' field.";
                return false;
            }

            if (messageType == RawMode)
            {
                goo = new GH_String(JsonConvert.SerializeObject(msgToken));
                return true;
            }

            JToken data = msgToken["data"];
            if (data == null)
            {
                error = string.Format(
                    "Payload has no 'data' field, required by {0}. Switch to '{1}' to see the raw message.",
                    messageType, RawLabel);
                return false;
            }

            try
            {
                switch (messageType)
                {
                    case "std_msgs/Bool":
                        goo = new GH_Boolean(data.Value<bool>());
                        return true;
                    case "std_msgs/Int32":
                        goo = new GH_Integer(data.Value<int>());
                        return true;
                    case "std_msgs/Float64":
                        goo = new GH_Number(data.Value<double>());
                        return true;
                    default: // std_msgs/String
                        goo = new GH_String(data.Value<string>() ?? "");
                        return true;
                }
            }
            catch
            {
                error = string.Format("Payload '{0}' cannot be read as {1}.",
                    JsonConvert.SerializeObject(data), messageType);
                return false;
            }
        }

        #endregion

        #region MENU + SERIALIZATION

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = Menu_AppendItem(menu, "Message Type");
            string[] types =
            {
                RawMode,
                "std_msgs/String",
                "std_msgs/Bool",
                "std_msgs/Int32",
                "std_msgs/Float64",
            };
            foreach (string t in types)
                AppendChoice(root.DropDown, ModeLabel(t), t);
        }

        private static string ModeLabel(string mode)
        {
            return mode == RawMode ? RawLabel : mode;
        }

        private void AppendChoice(ToolStrip dropdown, string label, string value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (messageType == value) return;
                RecordUndoEvent(label);
                messageType = value;

                // The captured payload was parsed under the old mode, so it no longer applies.
                lock (captureLock) { ClearCapture(); }
                ExpireSolution(true);
            }, true, messageType == value);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("MessageType", messageType ?? RawMode);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            string s = "";
            if (reader.TryGetString("MessageType", ref s) && !string.IsNullOrEmpty(s))
                messageType = s;
            return base.Read(reader);
        }

        #endregion

        protected override System.Drawing.Bitmap Icon
        {
            get { return Vizor.Properties.Resources.ListenTopic; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("9c5758c2-0b10-4be3-9b23-c316b793dcf8"); }
        }
    }
}
