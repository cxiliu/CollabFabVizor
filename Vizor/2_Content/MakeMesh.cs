using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using VizorLibs;

namespace Vizor._2_Content
{
    /// <summary>  
    /// The MakeMesh class is a Grasshopper component designed to create mesh objects for AR spaces.  
    /// It takes input parameters such as target anchor devices, meshes, names, colors, and display rules,  
    /// and outputs registered AR geometries and mesh geometry objects.  
    ///  
    /// Input:  
    /// - Target Anchor (Device): The device to anchor the mesh geometry to.  
    /// - Meshes (List<Mesh>): A list of meshes to process.  
    /// - Names (List<string>): Optional names for the geometries.  
    /// - Colors (List<Color>): Optional colors for the geometries.  
    /// - Display Rules (List<string>): Optional rules for geometry display (e.g., persistent, session, step, flange).  
    ///  
    /// Output:  
    /// - Output (string): A summary of the registered AR geometries.  
    /// - Mesh Geometry Objects (List<SceneGeometryObject>): The processed mesh geometry objects.  
    ///  
    /// Notes:  
    /// - Default values are provided for names, colors, and display rules if not explicitly specified.  
    /// - The component dynamically adjusts to handle single or multiple input values for names, colors, and rules.  
    /// </summary>

    public class MakeMesh : GH_Component
    {
        private Device anchorDevice;
        private List<SceneGeometryObject> geoms;
        private List<Mesh> meshes;
        private List<string> names;
        //private List<string> layers;
        private List<System.Drawing.Color> colours;
        private List<string> materials;
        private List<string> rules;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MakeMesh()
          : base("Scene Mesh Object", "Mesh",
              "Create mesh objects for the AR space",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Target Anchor", "T", "target device to anchor the mesh geometry to", GH_ParamAccess.item);
            pManager.AddMeshParameter("Meshes", "M", "list of meshes for the task", GH_ParamAccess.list);

            pManager.AddTextParameter("Names", "N", "list of geometry names (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, "mesh");
            pManager.AddColourParameter("Colours", "C", 
                "list of colours for the geometries (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, System.Drawing.Color.FromArgb(255, 9, 169, 237));
            pManager.AddTextParameter("Display Rules", "R", 
                "list of rules, e.g., one of [persistent, session, step, flange] (if only one is provided it will be applied to all)", 
                GH_ParamAccess.list, "session");
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "out", "name of registered AR geometries", GH_ParamAccess.item);
            pManager.AddGenericParameter("Mesh Geometry Objects", "M", "Mesh geometry objects", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {

            meshes = new List<Mesh>();
            names = new List<string>();
            //layers = new List<string>();
            colours = new List<System.Drawing.Color>();
            materials = new List<string>();
            rules = new List<string>();
            if (!DA.GetData(0, ref anchorDevice)) return;
            if (!DA.GetDataList(1, meshes)) return;
            if (!DA.GetDataList(2, names)) return;
            //if (!DA.GetDataList(2, layers)) return;
            if (!DA.GetDataList(3, colours)) return;
            if (!DA.GetDataList(4, rules)) return;
            foreach (System.Drawing.Color col in colours)
            {
                materials.Add(String.Format("{0},{1},{2},{3}", col.R, col.G, col.B, col.A));
            }

            geoms = new List<SceneGeometryObject>();
            string output = "";

            int meshFaces = 0;
            for (int i = 0; i< meshes.Count; i++)
            {
                string rule = rules.Count == 1 ? rules[0] : rules[i];
                string name = names[0];
                if (names.Count == 1)
                {
                    if (meshes.Count == 1)
                        name = names[0];
                    else
                        name = names[0] + i.ToString();
                }
                else
                {
                    name = names[i];
                }

                geoms.Add(new SceneGeometryObject
                {
                    gMesh = VizorUtilities.IsLinkAttachedRule(rule) ? meshes[i] : VizorUtilities.TransformVisualisation(meshes[i], anchorDevice),
                    //gMesh = meshes[i],
                    name = name,
                    layer = VizorUtilities.GetLayerFromRule(rule, anchorDevice), //layers.Count == 1 ? layers[0] : layers[i],
                    material = materials.Count == 1 ? materials[0] : materials[i],
                    operation = VizorUtilities.GetOperationFromRule(rule, anchorDevice)
                });
                meshFaces += meshes[i].Faces.Count;
                output += String.Format("{0}, {1} faces\n", geoms[i].name, meshes[i].Faces.Count);
            }

            this.Message = String.Format("{0} faces ({1})", meshFaces, VizorUtilities.GetTypeSummary(rules));
            DA.SetDataList(1, geoms);
            DA.SetData(0, output);

        }

        /// <summary>
        /// Custom preview display for mesh objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (geoms == null || geoms.Count == 0)
                return;

            // Render all mesh geometry objects using ContentUtilities
            foreach (SceneGeometryObject geom in geoms)
            {
                ContentUtilities.DrawMeshShaded(args, geom);
            }
        }

        /// <summary>
        /// Draw mesh edges for better visibility
        /// </summary>
        //public override void DrawViewportWires(IGH_PreviewArgs args)
        //{
        //    base.DrawViewportWires(args);

        //    if (geoms == null || geoms.Count == 0)
        //        return;

        //    // Render mesh edges using ContentUtilities
        //    foreach (SceneGeometryObject geom in geoms)
        //    {
        //        ContentUtilities.DrawMeshWires(args, geom);
        //    }
        //}

        /// <summary>
        /// Override bounding box to include mesh geometry
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;

                if (geoms != null)
                {
                    foreach (SceneGeometryObject geom in geoms)
                    {
                        bbox.Union(ContentUtilities.GetMeshBoundingBox(geom));
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
                return Vizor.Properties.Resources.Mesh;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6bcc3c7e-4f62-4369-82f0-4fd02e28ce4c"); }
        }
    }
}