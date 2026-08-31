using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using VizorLibs;

namespace Vizor._2_Content
{
    /// <summary>  
    /// The MakeText component is a Grasshopper component designed to create text objects for augmented reality (AR) spaces.  
    /// It allows users to define various properties for the text objects, such as their content, orientation, color, and display rules.  
    ///  
    /// Inputs:  
    /// - Target Anchor: The device to which the text objects will be anchored.  
    /// - Texts: A list of text strings to be displayed.  
    /// - Planes: A list of planes defining the orientation of the text objects.  
    /// - Names: A list of names for the text objects.  
    /// - Colors: A list of colors for the text objects.  
    /// - Display Rules: A list of rules defining the display behavior of the text objects.  
    ///  
    /// Outputs:  
    /// - Output: A summary of the registered AR geometries.  
    /// - Text Objects: A list of SceneTextObject instances representing the created text objects.  
    ///  
    /// This component integrates with Vizor's AR framework and utilizes utility methods to determine layers and operations based on display rules.  
    /// </summary>
    
    public class MakeText : GH_Component
    {
        private Device anchorDevice;
        private List<SceneTextObject> textObjects;
        private List<string> texts;
        private List<Plane> planes;
        private List<string> names;
        //private List<string> layers;
        private List<System.Drawing.Color> colours;
        private List<string> materials;
        //private List<bool> solidFlag;
        private List<string> rules;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MakeText()
          : base("Scene Text Object", "Text",
              "Create a text object for the AR space",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // TODO: add a size input for the text objects
            pManager.AddGenericParameter("Target Anchor", "T", "target device to anchor the text to", GH_ParamAccess.item);
            pManager.AddTextParameter("Texts", "T", "list of texts for the task", GH_ParamAccess.list);

            pManager.AddPlaneParameter("Planes", "P", "list of planes for the text (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, Plane.WorldXY);
            pManager.AddTextParameter("Names", "N", "list of text names (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, "text");
            pManager.AddColourParameter("Colours", "C", "list of colours for the text (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, System.Drawing.Color.FromArgb(255, 237, 195, 9));

            //pManager.AddBooleanParameter("Solid", "S", 
            //    "if true, the text will appear as 3D solids (beware of display performance), if false, the text will appear as a tag, (if only one is provided it will be applied to all)", 
            //    GH_ParamAccess.list, false);
            pManager.AddTextParameter("Display Rules", "R", "list of rules, e.g., one of [persistent, session, step, flange] (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, "session");
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "out", "name of registered AR geometries", GH_ParamAccess.item);
            pManager.AddGenericParameter("Text Objects", "T", "Text objects", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            texts = new List<string>();
            planes = new List<Plane>();
            names = new List<string>();
            //layers = new List<string>();
            colours = new List<System.Drawing.Color>();
            rules = new List<string>();
            //solidFlag = new List<bool>();

            if (!DA.GetData(0, ref anchorDevice)) return;
            if (!DA.GetDataList(1, texts)) return;
            if (!DA.GetDataList(2, planes)) return;
            if (!DA.GetDataList(3, names)) return;
            if (!DA.GetDataList(4, colours)) return;
            //if (!DA.GetDataList(5, solidFlag)) return;
            if (!DA.GetDataList(5, rules)) return;

            materials = new List<string>();
            foreach (System.Drawing.Color col in colours)
            {
                materials.Add(String.Format("{0},{1},{2},{3}", col.R, col.G, col.B, col.A));
            }

            textObjects = new List<SceneTextObject>();
            string output = "";

            for (int i = 0; i< texts.Count; i++)
            {
                string name = "Txt_" + names[0];
                if (names.Count == 1)
                {
                    if (texts.Count == 1)
                        name = "Txt_" + names[0];
                    else
                        name = "Txt_" + names[0] + i.ToString();
                }
                else
                {
                    name = "Txt_" + names[i];
                }
                string rule = rules.Count == 1 ? rules[0] : rules[i];
                Plane textPlane = planes.Count == 1 ? planes[0] : planes[i];
                textObjects.Add(new SceneTextObject
                {
                    text = texts[i],
                    // link-attached text is parented to a moving joint, so it is published as authored.
                    // everything else is rebased into the anchor's content frame, same as MakeMesh
                    plane = VizorUtilities.IsLinkAttachedRule(rule) ? textPlane : VizorUtilities.TransformVisualisation(textPlane, anchorDevice),
                    name = name,
                    layer = VizorUtilities.GetLayerFromRule(rule, anchorDevice), //layers.Count == 1 ? layers[0] : layers[i],
                    material = materials.Count == 1 ? materials[0] : materials[i],
                    solid = false, //solidFlag.Count == 1 ? solidFlag[0] : solidFlag[i],
                    operation = VizorUtilities.GetOperationFromRule(rule, anchorDevice)
                });
                output += String.Format("{0} texts\n", textObjects[i].name);
            }

            this.Message = String.Format("{0} texts ({1})", textObjects.Count, VizorUtilities.GetTypeSummary(rules));
            DA.SetDataList(1, textObjects);
            DA.SetData(0, output);

        }

        /// <summary>
        /// Custom preview display for text objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (textObjects == null || textObjects.Count == 0)
                return;

            // Render all text objects using ContentUtilities
            foreach (SceneTextObject textObj in textObjects)
            {
                ContentUtilities.DrawText(args, textObj);
            }
        }

        /// <summary>
        /// Override bounding box to include text positions
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;

                if (textObjects != null)
                {
                    foreach (SceneTextObject textObj in textObjects)
                    {
                        bbox.Union(ContentUtilities.GetTextBoundingBox(textObj));
                    }
                }

                return bbox;
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
                return Vizor.Properties.Resources.Text;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("daffdd63-8fc0-4b92-a56a-c31ae4eadaf4"); }
        }
    }
}