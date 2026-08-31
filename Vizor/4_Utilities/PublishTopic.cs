using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Windows.Forms;
using VizorLibs;

namespace Vizor._1_System
{
    /// <summary>
    /// Publish a string message to a ROS topic via WebSocket.
    /// Message type is selected from the right-click context menu.
    /// </summary>
    public class PublishTopic : VizorBaseComponent
    {
        private string topicName;
        private string messageType = "std_msgs/String";
        private bool publish;

        public PublishTopic()
          : base("Publish Topic", "PublishTopic",
              "Publish a string message to a specified topic via WebSocket.", "4_Utilities")
        {
            isListener = false;
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("WSC", "WSC", "WebSocket connection object", GH_ParamAccess.item);
            pManager.AddTextParameter("Topic Name", "Topic", "Topic to publish to", GH_ParamAccess.item, "/Robot/reset");
            pManager.AddGenericParameter("Message", "Msg", "Message to publish (string, bool, int, or float depending on the selected message type)", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Publish", "P", "Set to true to publish the message", GH_ParamAccess.item, false);

            // AddGenericParameter has no default-value overload, so seed the default directly.
            ((Param_GenericObject)pManager[2]).SetPersistentData(new GH_String("UR10"));
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "Out", "Status output", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_Goo msgGoo = null;

            DA.GetData(0, ref wscObj);
            DA.GetData(1, ref topicName);
            DA.GetData(2, ref msgGoo);
            DA.GetData(3, ref publish);

            this.Message = string.IsNullOrWhiteSpace(topicName)
                ? messageType
                : $"{topicName}\n{messageType}";

            if (wscObj == null)
            {
                DA.SetData(0, "No WebSocket connection object provided.");
                return;
            }

            if (string.IsNullOrWhiteSpace(topicName))
            {
                DA.SetData(0, "Topic name is required.");
                return;
            }

            if (!wscObj.isConnected())
            {
                DA.SetData(0, "WebSocket is not connected.");
                return;
            }

            if (!publish)
            {
                DA.SetData(0, "Set 'Publish' to true to send the message.");
                return;
            }

            if (msgGoo == null)
            {
                DA.SetData(0, "No message provided.");
                return;
            }

            // Convert first: an invalid value must not advertise the topic, since rosbridge
            // keeps the first type a topic is advertised with for the life of the connection.
            string published;

            switch (messageType)
            {
                case "std_msgs/Bool":
                    bool boolVal;
                    if (!msgGoo.CastTo(out boolVal))
                    {
                        ReportCastFailure(DA, msgGoo, "a boolean");
                        return;
                    }
                    ROSMessageHandler.Advertise(wscObj, topicName, messageType);
                    ROSMessageHandler.PublishBoolMessage(wscObj, topicName, boolVal);
                    published = boolVal.ToString();
                    break;

                case "std_msgs/Int32":
                    int intVal;
                    if (!TryCastToInt(msgGoo, out intVal))
                    {
                        ReportCastFailure(DA, msgGoo, "an integer");
                        return;
                    }
                    ROSMessageHandler.Advertise(wscObj, topicName, messageType);
                    ROSMessageHandler.PublishInt32Message(wscObj, topicName, intVal);
                    published = intVal.ToString();
                    break;

                case "std_msgs/Float64":
                    double dblVal;
                    if (!msgGoo.CastTo(out dblVal))
                    {
                        ReportCastFailure(DA, msgGoo, "a number");
                        return;
                    }
                    ROSMessageHandler.Advertise(wscObj, topicName, messageType);
                    ROSMessageHandler.PublishFloat64Message(wscObj, topicName, dblVal);
                    published = dblVal.ToString();
                    break;

                default: // std_msgs/String
                    string strVal;
                    if (!msgGoo.CastTo(out strVal))
                        strVal = msgGoo.ToString(); // every goo has a text representation
                    ROSMessageHandler.Advertise(wscObj, topicName, messageType);
                    ROSMessageHandler.PublishStringMessage(wscObj, topicName, strVal ?? "");
                    published = strVal ?? "";
                    break;
            }

            DA.SetData(0, $"Published '{published}' to '{topicName}' at {DateTime.Now}.");
        }

        /// <summary>
        /// Casts to int, falling back to a rounded number so a GH_Number slider works too.
        /// </summary>
        private static bool TryCastToInt(IGH_Goo goo, out int value)
        {
            if (goo.CastTo(out value)) return true;

            double dbl;
            if (goo.CastTo(out dbl))
            {
                value = (int)Math.Round(dbl);
                return true;
            }

            value = 0;
            return false;
        }

        private void ReportCastFailure(IGH_DataAccess DA, IGH_Goo goo, string expected)
        {
            string error = $"Message '{goo}' cannot be converted to {expected}, required by {messageType}.";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
            DA.SetData(0, error);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = Menu_AppendItem(menu, "Message Type");
            string[] types =
            {
                "std_msgs/String",
                "std_msgs/Bool",
                "std_msgs/Int32",
                "std_msgs/Float64",
            };
            foreach (string t in types)
                AppendChoice(root.DropDown, t, t);
        }

        private void AppendChoice(ToolStrip dropdown, string label, string value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (messageType == value) return;
                RecordUndoEvent(label);
                messageType = value;
                ExpireSolution(true);
            }, true, messageType == value);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString("MessageType", messageType ?? "std_msgs/String");
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            string s = "";
            if (reader.TryGetString("MessageType", ref s) && !string.IsNullOrEmpty(s))
                messageType = s;
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Vizor.Properties.Resources.PublishMsg; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("e2b7c7e2-1b2a-4e7a-9b2e-2c7e2b7c7e2a"); }
        }
    }
}
