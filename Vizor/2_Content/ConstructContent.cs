using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using VizorLibs;

namespace Vizor._2_Content
{
    /// <summary>  
    /// The ConstructContent class is a Grasshopper component designed to create a composite content object for AR spaces.  
    /// It takes various inputs such as meshes, wireframes, texts, and a level of detail (LoD) to construct a SceneContentObject.  
    /// The component outputs the constructed content object, which can be used in AR applications.  
    /// 
    /// Inputs:  
    ///   - Name: A string representing the name of the content object.  
    ///   - Meshes: A list of SceneGeometryObject instances representing 3D geometry.  
    ///   - Wireframes: A list of SceneWireframeObject instances representing wireframe geometry.  
    ///   - Texts: A list of SceneTextObject instances representing text elements.  
    ///   - Level of Detail (LoD): An integer specifying the level of detail for the content.  
    ///   
    /// Outputs:  
    ///   - Content Object: A SceneContentObject containing the combined inputs.  
    ///   - The component also displays a message summarizing the number of meshes, wireframes, and texts processed.  
    ///   
    /// </summary>

    public class ConstructContent : GH_Component
    {
        private List<SceneGeometryObject> meshes;
        private List<SceneWireframeObject> wires;
        private List<SceneTextObject> texts;
        private int LoD;
        private string name;

        /// <summary>
        /// Initializes a new instance of the MakeContent class.
        /// </summary>
        public ConstructContent()
          : base("Construct Scene Content", "Content",
              "Create a composite content object for the AR space",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "name for the content object", GH_ParamAccess.item, "content object");
            pManager.AddGenericParameter("Meshes", "M", "mesh bject(s)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Wireframes", "W", "wireframe object(s)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Texts", "T", "text object(s)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Level of Detail", "LOD", "integer specifying the level of detail to associate the content with", GH_ParamAccess.item, 0);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Content Object", "Content", "AR Content Object", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            meshes = new List<SceneGeometryObject>();
            wires = new List<SceneWireframeObject>();
            texts = new List<SceneTextObject>();
            DA.GetData(0, ref name);
            DA.GetDataList(1, meshes);
            DA.GetDataList(2, wires);
            DA.GetDataList(3, texts);
            DA.GetData(4, ref LoD);

            SceneContentObject content = new SceneContentObject
            {
                name = name,
                operation = "",//(waiting for assignment in scene model or task controller)
                geomObjects = meshes.ToArray(),
                wireObjects = wires.ToArray(),
                textObjects = texts.ToArray(), 
                LoD = LoD,
            };

            this.Message = name + String.Format("\n{0}M {1}W {2}T", meshes.Count, wires.Count, texts.Count);
            DA.SetData("Content Object", content);
        }

        /// <summary>
        /// Custom preview display for mesh objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (meshes == null || meshes.Count == 0)
                return;

            // Render all mesh geometry objects using ContentUtilities
            foreach (SceneGeometryObject geom in meshes)
            {
                ContentUtilities.DrawMeshShaded(args, geom);
            }
        }

        /// <summary>
        /// Custom preview display for wireframes and text objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            //// Render mesh edges using ContentUtilities
            //if (meshes != null)
            //{
            //    foreach (SceneGeometryObject geom in meshes)
            //    {
            //        ContentUtilities.DrawMeshWires(args, geom);
            //    }
            //}

            // Render wireframe objects using ContentUtilities
            if (wires != null)
            {
                foreach (SceneWireframeObject wireframe in wires)
                {
                    ContentUtilities.DrawWireframe(args, wireframe);
                }
            }

            // Render text objects using ContentUtilities
            if (texts != null)
            {
                foreach (SceneTextObject textObj in texts)
                {
                    ContentUtilities.DrawText(args, textObj);
                }
            }
        }

        /// <summary>
        /// Override bounding box to include all sub-objects
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;

                // Include mesh geometry using ContentUtilities
                if (meshes != null)
                {
                    foreach (SceneGeometryObject geom in meshes)
                    {
                        bbox.Union(ContentUtilities.GetMeshBoundingBox(geom));
                    }
                }

                // Include wireframe points using ContentUtilities
                if (wires != null)
                {
                    foreach (SceneWireframeObject wireframe in wires)
                    {
                        bbox.Union(ContentUtilities.GetWireframeBoundingBox(wireframe));
                    }
                }

                // Include text positions using ContentUtilities
                if (texts != null)
                {
                    foreach (SceneTextObject textObj in texts)
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
                return Vizor.Properties.Resources.ConstructContent;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("bd2ab8e4-9622-4ce4-bead-1570878b4532"); }
        }
    }
}