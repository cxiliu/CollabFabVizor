using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using VizorLibs;

namespace Vizor._2_Content
{
    /// <summary>
    /// The MakeWireframe component is responsible for creating wireframe objects for augmented reality (AR) spaces.
    ///
    /// Geometry is supplied as a single tree of breps or points, and each branch becomes one wireframe:
    /// a flat list is a single branch and therefore a single wireframe, a tree of N branches yields N
    /// wireframes. Points in a branch are connected in order as an open polyline; the edges of the breps
    /// in a branch are traced as one continuous line.
    ///
    /// Inputs:
    /// - Target Anchor (Device): The device to anchor the wireframe to.
    /// - Geometry (GH_Structure): Breps or points, one wireframe per branch.
    /// - Names (List<string>): Names for the wireframes (applied to all if only one is provided).
    /// - Colors (List<Color>): Colors for the wireframes (applied to all if only one is provided).
    /// - Widths (List<int>): Widths for the wires in mm (applied to all if only one is provided).
    /// - Display Rules (List<string>): Rules for display behavior (e.g., persistent, session, step, flange).
    /// - Curve Resolution (int): Number of segments used to approximate a non-linear brep edge.
    ///
    /// Outputs:
    /// - Output (string): Summary of registered AR wireframes.
    /// - Wireframe Objects (List<SceneWireframeObject>): The generated wireframe objects.
    ///
    /// This component is part of the VizorGH plugin and is categorized under "2_Content".
    /// </summary>

    public class MakeWireframe : GH_Component
    {
        private const int DefaultCurveResolution = 8;

        private Device anchorDevice;
        public List<SceneWireframeObject> wireframes;
        private List<string> names;
        private List<int> widths;
        private List<System.Drawing.Color> colours;
        private List<string> materials;
        private List<string> rules;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MakeWireframe()
          : base("Scene Wireframe Object", "Wireframe",
              "Create a wireframe object for the AR space",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Target Anchor", "T", "target device to anchor the wireframe to", GH_ParamAccess.item);
            pManager.AddGenericParameter("Geometry", "G",
                "breps or points to generate the wireframes. Each branch becomes one wireframe, so a flat list is a single wireframe " +
                "and a tree of N branches gives N wireframes. Points in a branch are connected in order as an open polyline; " +
                "brep edges are traced as one continuous line.",
                GH_ParamAccess.tree);

            pManager.AddTextParameter("Names", "N", "list of wireframe names (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "wireframe");
            pManager.AddColourParameter("Colors", "C", "list of colours for the wireframe (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, System.Drawing.Color.FromArgb(255, 237, 9, 153));
            pManager.AddIntegerParameter("Widths", "W", "list of widths for each wire in mm (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, 5);
            pManager.AddTextParameter("Display Rules", "R", "list of rules, e.g., one of [persistent, session, step, flange] (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "session");
            pManager.AddIntegerParameter("Curve Resolution", "Res", "number of segments used to approximate a non-linear brep edge (straight edges are always drawn with two points)",
                GH_ParamAccess.item, DefaultCurveResolution);
            pManager[6].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "out", "name of registered AR wireframes", GH_ParamAccess.item);
            pManager.AddGenericParameter("Wireframe Objects", "W", "Wireframe objects", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            names = new List<string>();
            widths= new List<int>();
            colours = new List<System.Drawing.Color>();
            materials = new List<string>();
            rules = new List<string>();
            wireframes = new List<SceneWireframeObject>();

            GH_Structure<IGH_Goo> geometry;
            int resolution = DefaultCurveResolution;

            if (!DA.GetData(0, ref anchorDevice)) return;
            if (!DA.GetDataTree(1, out geometry)) return;
            if (!DA.GetDataList(2, names)) return;
            if (!DA.GetDataList(3, colours)) return;
            if (!DA.GetDataList(4, widths)) return;
            if (!DA.GetDataList(5, rules)) return;
            DA.GetData(6, ref resolution);
            resolution = Math.Max(2, resolution);

            if (geometry == null || !geometry.AllData(true).Any())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "no geometry supplied - connect breps or points to build a wireframe");
                this.Message = "no geometry";
                return;
            }

            // one wireframe per branch that actually holds something
            List<GH_Path> paths = geometry.Paths
                .Where(p => geometry.get_Branch(p) != null && geometry.get_Branch(p).Count > 0)
                .ToList();

            if (!MatchesBranchCount(names.Count, paths.Count, "Names")) return;
            if (!MatchesBranchCount(colours.Count, paths.Count, "Colors")) return;
            if (!MatchesBranchCount(widths.Count, paths.Count, "Widths")) return;
            if (!MatchesBranchCount(rules.Count, paths.Count, "Display Rules")) return;

            foreach (System.Drawing.Color col in colours)
            {
                materials.Add(String.Format("{0},{1},{2},{3}", col.R, col.G, col.B, col.A));
            }

            double tolerance = Rhino.RhinoDoc.ActiveDoc != null ? Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : 0.001;
            string output = "";

            for (int i = 0; i < paths.Count; i++)
            {
                string rule = rules.Count == 1 ? rules[0] : rules[i];

                List<Brep> branchBreps = new List<Brep>();
                List<Point3d> branchPoints = new List<Point3d>();
                bool unsupported = false;

                foreach (IGH_Goo goo in geometry.get_Branch(paths[i]).Cast<IGH_Goo>())
                {
                    if (goo == null)
                        continue;

                    Point3d pt = Point3d.Unset;
                    Brep brep = null;
                    if (GH_Convert.ToPoint3d(goo, ref pt, GH_Conversion.Primary))
                        branchPoints.Add(pt);
                    else if (GH_Convert.ToBrep(goo, ref brep, GH_Conversion.Both) && brep != null)
                        branchBreps.Add(brep);
                    else
                        unsupported = true;
                }

                if (unsupported)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        String.Format("branch {0} holds items that are neither breps nor points - they were skipped", paths[i].ToString()));

                if (branchBreps.Count > 0 && branchPoints.Count > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        String.Format("branch {0} mixes breps and points - a connector will be drawn between them", paths[i].ToString()));

                // breps are traced first, then any loose points are appended to the same line
                List<Point3d> points = new List<Point3d>();
                int edgeCount = 0;
                if (branchBreps.Count > 0)
                    points.AddRange(ContentUtilities.BrepEdgesToWirePoints(branchBreps, resolution, tolerance, out edgeCount));
                points.AddRange(branchPoints);

                if (points.Count < 2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        String.Format("branch {0} did not produce a wire - at least two points are needed", paths[i].ToString()));
                    continue;
                }

                if (points.Count >= ContentUtilities.MaxWirePoints)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        String.Format("branch {0} was capped at {1} points - lower the curve resolution or split the geometry over more branches",
                            paths[i].ToString(), ContentUtilities.MaxWirePoints));

                Point3d[] wirePoints = VizorUtilities.IsLinkAttachedRule(rule)
                    ? points.ToArray()
                    : VizorUtilities.TransformVisualisation(points.ToArray(), anchorDevice);

                string name;
                if (names.Count == 1)
                {
                    if (paths.Count == 1)
                        name = "Wire_" + names[0];
                    else
                        name = "Wire_" + names[0] + i.ToString();
                }
                else
                {
                    name = "Wire_" + names[i];
                }

                wireframes.Add(new SceneWireframeObject
                {
                    points = wirePoints,
                    name = name,
                    layer = VizorUtilities.GetLayerFromRule(rule, anchorDevice),
                    material = materials.Count == 1 ? materials[0] : materials[i],
                    width = widths.Count == 1 ? widths[0] : widths[i],
                    operation = VizorUtilities.GetOperationFromRule(rule, anchorDevice)
                });

                if (edgeCount > 0)
                    output += String.Format("{0}, {1} points ({2} edges)\n", name, wirePoints.Length, edgeCount);
                else
                    output += String.Format("{0}, {1} points\n", name, wirePoints.Length);
            }

            this.Message = String.Format("{0} wireframes ({1})", wireframes.Count, VizorUtilities.GetTypeSummary(rules));
            DA.SetData(0, output);
            DA.SetDataList(1, wireframes);

        }

        /// <summary>
        /// A per-wireframe attribute list has to hold either one value for all branches, or one value
        /// per branch. Anything else is reported rather than silently indexed out of range.
        /// </summary>
        private bool MatchesBranchCount(int count, int branchCount, string inputName)
        {
            if (count == 1 || count == branchCount)
                return true;

            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                String.Format("{0} holds {1} values but there are {2} branches - supply either one value or one per branch",
                    inputName, count, branchCount));
            return false;
        }

        /// <summary>
        /// Custom preview display for wireframe objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);

            if (wireframes == null || wireframes.Count == 0)
                return;

            // Render all wireframe objects using ContentUtilities
            foreach (SceneWireframeObject wireframe in wireframes)
            {
                ContentUtilities.DrawWireframe(args, wireframe);
            }
        }

        /// <summary>
        /// Override bounding box to include wireframe geometry
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;

                if (wireframes != null)
                {
                    foreach (SceneWireframeObject wireframe in wireframes)
                    {
                        bbox.Union(ContentUtilities.GetWireframeBoundingBox(wireframe));
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
                return Vizor.Properties.Resources.Wireframe;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7b6e87d9-0187-4cce-bca1-d7393fd01070"); }
        }
    }
}
