using Grasshopper.Kernel;
using System;
using System.Windows.Forms;
using VizorLibs;

namespace Vizor._2_Content
{
    public class MakeDisplayRule : GH_Component
    {
        private string anchorType = "session";

        public MakeDisplayRule()
          : base("Display Rule", "Rule",
              "Create a display rule for AR scene geometry anchoring.\n" +
              "Right-click to select the anchor type.\n" +
              "Use 'Link' with a Link Name to attach geometry to a specific robot link.",
              "VizorGH", "2_Content")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Target Anchor", "T",
                "Target device (required when Anchor Type is 'Link', to validate link names)",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Link Name", "L",
                "Robot link name (only used when Anchor Type is 'Link'). " +
                "Must match one of the Link Names defined in the Robot component.",
                GH_ParamAccess.item, "");
            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Rule", "R",
                "Display rule string — connect to Display Rules (R) on Make Mesh, Make Wireframe, or Make Text",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Device device = null;
            string linkName = "";

            DA.GetData(0, ref device);
            DA.GetData(1, ref linkName);

            string rule;
            if (anchorType == "link")
            {
                if (string.IsNullOrEmpty(linkName))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "A Link Name is required when Anchor Type is 'Link'.");
                    return;
                }

                if (device is RobotObject rob &&
                    System.Array.IndexOf(rob.joint_names, linkName) < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        string.Format("Link '{0}' not found in the robot's joint names: {1}",
                            linkName, string.Join(", ", rob.joint_names)));
                }

                rule = "link:" + linkName;
            }
            else
            {
                rule = anchorType;
            }

            this.Message = rule;
            DA.SetData(0, rule);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem anchorRoot = Menu_AppendItem(menu, "Anchor Type");
            AppendChoice(anchorRoot.DropDown, "Session",    "session",    anchorType, v => anchorType = v);
            AppendChoice(anchorRoot.DropDown, "Step",       "step",       anchorType, v => anchorType = v);
            AppendChoice(anchorRoot.DropDown, "Persistent", "persistent", anchorType, v => anchorType = v);
            AppendChoice(anchorRoot.DropDown, "Flange",     "flange",     anchorType, v => anchorType = v);
            AppendChoice(anchorRoot.DropDown, "Link",       "link",       anchorType, v => anchorType = v);
        }

        private void AppendChoice(ToolStrip dropdown, string label, string value, string current, Action<string> setter)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (current == value) return;
                RecordUndoEvent(label);
                setter(value);
                ExpireSolution(true);
            }, true, current == value);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetString("anchorType", anchorType);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            anchorType = reader.GetString("anchorType");
            return base.Read(reader);
        }

        protected override System.Drawing.Bitmap Icon =>
            Vizor.Properties.Resources.DisplayRule;

        public override Guid ComponentGuid =>
            new Guid("a3f72b4e-1c85-4d30-9e6a-7b3c8f5d2e10");
    }
}
