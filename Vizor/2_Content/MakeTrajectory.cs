using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Vizor._3_Robot;
using VizorLibs;
using VizorLibs.MessageTypes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace Vizor._1_System
{
    /// <summary>
    /// The MakeTrajectory component is responsible for creating a robot trajectory object.
    /// It supports both Cartesian and joint space trajectory definitions, allowing users to define
    /// the trajectory either as tool center point (TCP) frames or as a tree of joint values.
    ///
    /// Inputs:
    /// - Target Robot: The robot client to associate the trajectory with.
    /// - TCP Frames / Joint Values: A single port carrying the trajectory data. It re-labels itself to
    ///   match the parameter space selected in the right-click menu.
    /// - Trajectory Width: Width of the trajectory visualization mesh [in metres].
    ///
    /// Right-click settings:
    /// - Parameter Space: Cartesian (TCP) or Joint. Persisted with the definition and always shown on the
    ///   component. The space is never inferred from the incoming data: if the data contradicts the selected
    ///   space the component reports it and names the fix, rather than silently reinterpreting the input.
    ///
    /// Data tree semantics (unchanged from when the two spaces had separate ports):
    /// - Cartesian: one branch of planes produces one trajectory, so a tree of frames produces one
    ///   trajectory per branch, output on the matching paths.
    /// - Joint: one branch is one waypoint, so the whole tree produces a single trajectory.
    ///
    /// Outputs:
    /// - Output: Status message describing the trajectory creation process.
    /// - Trajectory Object: The generated RobotTrajectoryObject(s).
    /// - Trajectory Mesh: A mesh representation of each trajectory for visualization.
    /// </summary>

    public class MakeTrajectory : GH_Component
    {
        private RobotObject target;
        private double width;
        private bool isCartesian = true; //default to cartesian inputs (virtual robot)

        // shared by RegisterInputParams and updateParamName so the port always describes the active space,
        // and points at the menu that switches it - the menu is otherwise an invisible affordance
        private const string cartesianDescription =
            "planes defining the target tool centre points (TCPs). When a tree is supplied, each branch " +
            "produces its own trajectory. \nTo supply pre-computed joint values instead, set " +
            "'Parameter Space > Joint' in this component's right-click menu. ";
        private const string jointDescription =
            "tree of joint values for each trajectory point [in degrees]. Each branch is one waypoint of a " +
            "single trajectory. \nTo supply TCP planes instead, set 'Parameter Space > Cartesian (TCP)' in " +
            "this component's right-click menu. ";

        /// <summary>
        /// Initializes a new instance of the MakeTrajectory class.
        /// </summary>
        public MakeTrajectory()
          : base("Robot Trajectory Object", "Trajectory",
              "Create a trajectory object for a robot or machine. ",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component. The trajectory data port is registered in
        /// its cartesian form (the default space); updateParamName re-labels it when the space changes.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Target Robot", "R", "robot client", GH_ParamAccess.item);
            pManager.AddGenericParameter("TCP Frames", "TCPs", cartesianDescription, GH_ParamAccess.tree);
            pManager.AddNumberParameter("Trajectory Width", "W", "width of the trajectory visualisation [in metres]", GH_ParamAccess.item, 0.01);
            // a task that is not meant to move supplies no trajectory data at all, which is a valid outcome
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Outputs a status message, the trajectory object, and a mesh pipe for visualising the trajectory.
        /// A cartesian tree can produce several trajectories in one solve, so these are list access.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
pManager.AddTextParameter("Output", "Out", "status output", GH_ParamAccess.tree);
pManager.AddGenericParameter("Trajectory Object", "Trajectory", "robot trajectory", GH_ParamAccess.tree);
pManager.AddMeshParameter("Trajectory Mesh", "Path Mesh", "mesh of the trajectory", GH_ParamAccess.tree);
        }

        /// <summary>
        /// Re-labels the trajectory data port to match the active parameter space. This is the component's
        /// main discoverability mechanism, so it is called from the menu handler and from Read as well as
        /// during a solve. The write is guarded so a normal solve does not fire OnParametersChanged.
        /// </summary>
        private void updateParamName(bool isCartesian)
        {
            if (Params.Input.Count < 2) return;

            string name = isCartesian ? "TCP Frames" : "Joint Values";
            if (Params.Input[1].Name == name) return;

            Params.Input[1].Name = name;
            Params.Input[1].NickName = isCartesian ? "TCPs" : "Joints";
            Params.Input[1].Description = isCartesian ? cartesianDescription : jointDescription;
            Params.OnParametersChanged();
        }

        /// <summary>
        /// The active parameter space, shown on the component so the right-click setting is readable
        /// straight off the canvas.
        /// </summary>
        private string SpaceLabel()
        {
            return isCartesian ? "cartesian (TCP)" : "joint space";
        }

        /// <summary>
        /// Reports that no usable trajectory could be built. A null path is a valid outcome, not a
        /// failure, so this is a warning: the component simply emits no trajectory object.
        /// </summary>
        private void ReportNoPath()
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "No trajectory point could be resolved from the given input, so no trajectory object is produced. " +
                "\nThis is fine for a robotic task that is not meant to move (e.g. a gripper or tool change): " +
                "the task can be built without motion data. ");
            this.Message = SpaceLabel() + "\nno path";
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            updateParamName(isCartesian);
            this.Message = SpaceLabel();

            DA.GetData(0, ref target);
            if (!(target is RobotObject))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "You did not add a correct robot client");
                this.Message = "no robot";
                return;
            }

            GH_Structure<IGH_Goo> inputTree;
            if (!DA.GetDataTree(1, out inputTree) || !inputTree.AllData(true).Any())
            {
                ReportNoPath();
                return;
            }

            // the space is a deliberate setting, so contradicting data is reported rather than obeyed
            if (!DataMatchesSpace(inputTree)) return;

            DA.GetData(2, ref width);
            // width is documented/entered in metres; convert to the active document's unit system
            // before using it as a raw geometry radius (mirrors MsgDataConverter's ROS<->Rhino unit handling)
            double radius = width * MsgDataConverter.ROSToRhinoMultiplier();

            GH_Structure<GH_String> outputs = new GH_Structure<GH_String>();
            GH_Structure<IGH_Goo> trajectories = new GH_Structure<IGH_Goo>();
            GH_Structure<GH_Mesh> meshes = new GH_Structure<GH_Mesh>();
            bool reportedNoPath = false;

            if (isCartesian)
            {
                // a branch of planes is one trajectory, matching how a tree of frames behaved when this
                // port was list access and Grasshopper did the iterating
                foreach (GH_Path path in inputTree.Paths)
                {
                    List<Plane> frames;
                    if (!TryCastFrames(inputTree.get_Branch(path) as List<IGH_Goo>, out frames)) return;

                    //the cartesian planes should be transformed according to the robot base
                    List<float[]> joint_trajectory = RobotLibraryConverter.ghFramesToJointTrajectory(frames, target);
                    // a branch that resolves to no path is skipped, the remaining branches still produce theirs
                    AppendTrajectory(frames, joint_trajectory, radius, path,
                                     outputs, trajectories, meshes, ref reportedNoPath);
                }
            }
            else
            {
                // Pre-computed joint trajectory points: every branch is a waypoint of one trajectory
                if (inputTree.Paths.Any(p => p.Length > 1))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, string.Format(
                        "The input has nested branches; all {0} branches are read as waypoints of a single " +
                        "trajectory. \nTo build several joint trajectories, use one component per trajectory. ",
                        inputTree.PathCount));
                }

                List<float[]> joint_trajectory = new List<float[]>();
                foreach (GH_Path path in inputTree.Paths)
                {
                    float[] waypoint;
                    if (!TryCastWaypoint(inputTree.get_Branch(path) as List<IGH_Goo>, out waypoint)) return;
                    if (waypoint != null) joint_trajectory.Add(waypoint);
                }

                List<Plane> frames = RobotLibraryConverter.jointTrajectoryToGhFrames(joint_trajectory, target);
                AppendTrajectory(frames, joint_trajectory, radius, new GH_Path(0),
                                 outputs, trajectories, meshes, ref reportedNoPath);
            }

            if (trajectories.PathCount == 0) return;

            // some branches may have resolved to no path while others succeeded: the warning already
            // reports that, so the component itself reads as the space it is working in
            this.Message = trajectories.PathCount > 1
                ? string.Format("{0}\n{1} trajectories", SpaceLabel(), trajectories.PathCount)
                : SpaceLabel();

            DA.SetDataTree(0, outputs);
            DA.SetDataTree(1, trajectories);
            DA.SetDataTree(2, meshes);
        }

        /// <summary>
        /// Builds one trajectory object and appends it, its mesh and its status line to the output
        /// structures under the given path. Returns false when the group resolved to no path, which is a
        /// valid outcome: the group is skipped and the remaining groups still produce their trajectories.
        /// </summary>
        private bool AppendTrajectory(List<Plane> frames, List<float[]> joint_trajectory, double radius, GH_Path path,
                                      GH_Structure<GH_String> outputs, GH_Structure<IGH_Goo> trajectories,
                                      GH_Structure<GH_Mesh> meshes, ref bool reportedNoPath)
        {
            // without at least one frame there is no path to pipe or publish. bail out with a clear
            // message instead of letting the mesh construction below fail on an empty point set
            if ((joint_trajectory == null) || (joint_trajectory.Count == 0) ||
                (frames == null) || (frames.Count == 0))
            {
                if (!reportedNoPath)
                {
                    ReportNoPath();
                    reportedNoPath = true;
                }
                return false;
            }

            Mesh gMesh;
            if (frames.Count == 1)
            {
                // a single waypoint has no path to pipe, so mark it with a small sphere instead
                Sphere sphere = new Sphere(frames[0].Origin, radius);
                gMesh = Mesh.CreateFromSphere(sphere, 8, 8);
            }
            else
            {
                Curve path_curve = new PolylineCurve(frames.Select(f => f.Origin).ToArray());
                gMesh = Mesh.CreateFromCurvePipe(path_curve, radius, 6, 1, MeshPipeCapStyle.Dome, true);
            }
            if (gMesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Could not generate a trajectory visualisation mesh. Trajectory data is still valid.");
                gMesh = new Mesh();
            }

            // mesh_trajectory is parented to the robot on the headset, so it is published base-local.
            // the joint branch's frames already are (jointTrajectoryToGhFrames rebases the FK output),
            // but cartesian frames are the user's world TCPs, so the mesh piped from them still needs it.
            // only the visualisation is rebased here - the joint values are untouched, and the frames fed
            // to ghFramesToJointTrajectory stay in world because VirtualRobot's IK is base-aware
            if (isCartesian) gMesh = VizorUtilities.TransformVisualisation(gMesh, target);

            RobotTrajectoryObject trajObject = new RobotTrajectoryObject
            {
                robot = this.target,
                gTrajectoryFrames = frames,
                joint_trajectory = joint_trajectory,
                gMesh = gMesh,
            };

            string output = string.Format("{0} trajectory is created with {1} points in {2} space",
                target.name, joint_trajectory.Count, isCartesian ? "cartesian" : "joint");

            outputs.Append(new GH_String(output), path);
            trajectories.Append(new GH_ObjectWrapper(trajObject), path);
            meshes.Append(new GH_Mesh(gMesh), path);
            return true;
        }

        /// <summary>
        /// Checks the incoming data against the selected parameter space. The space is never inferred from
        /// the data - a mismatch is reported with the menu path that fixes it, so the user's stated intent
        /// is never silently overridden.
        /// </summary>
        private bool DataMatchesSpace(GH_Structure<IGH_Goo> inputTree)
        {
            foreach (IGH_Goo goo in inputTree.AllData(true))
            {
                bool looksCartesian;
                if (goo is GH_Plane) looksCartesian = true;
                else if ((goo is GH_Number) || (goo is GH_Integer)) looksCartesian = false;
                else
                {
                    Plane plane;
                    double number;
                    if (goo.CastTo(out plane)) looksCartesian = true;
                    else if (goo.CastTo(out number)) looksCartesian = false;
                    else break; // unrecognised type: let the per-item cast report it with the actual value
                }

                if (looksCartesian == isCartesian) break;

                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, isCartesian
                    ? "Parameter Space is set to Cartesian (TCP), which expects planes, but the input contains " +
                      "numbers. \nSet 'Parameter Space > Joint' in this component's right-click menu to read " +
                      "them as joint values. "
                    : "Parameter Space is set to Joint, which expects joint values, but the input contains " +
                      "planes. \nSet 'Parameter Space > Cartesian (TCP)' in this component's right-click menu " +
                      "to read them as TCP frames. ");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Casts one branch into TCP frames, reporting the offending value and the menu path that fixes it.
        /// </summary>
        private bool TryCastFrames(List<IGH_Goo> branch, out List<Plane> frames)
        {
            frames = new List<Plane>();
            if (branch == null) return true;

            foreach (IGH_Goo goo in branch)
            {
                if (goo == null) continue;

                Plane plane;
                if (!goo.CastTo(out plane))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                        "Cartesian space expects planes but received '{0}'. \nIf these are joint values, set " +
                        "'Parameter Space > Joint' in this component's right-click menu. ", goo));
                    return false;
                }
                frames.Add(plane);
            }
            return true;
        }

        /// <summary>
        /// Casts one branch into a single joint-space waypoint, reporting the offending value and the menu
        /// path that fixes it.
        /// </summary>
        private bool TryCastWaypoint(List<IGH_Goo> branch, out float[] waypoint)
        {
            waypoint = null;
            if (branch == null) return true;

            List<float> values = new List<float>();
            foreach (IGH_Goo goo in branch)
            {
                if (goo == null) continue;

                double value;
                if (!goo.CastTo(out value))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format(
                        "Joint space expects numbers but received '{0}'. \nIf these are TCP frames, set " +
                        "'Parameter Space > Cartesian (TCP)' in this component's right-click menu. ", goo));
                    return false;
                }
                values.Add((float)value);
            }

            if (values.Count > 0) waypoint = values.ToArray();
            return true;
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = Menu_AppendItem(menu, "Parameter Space");
            AppendSpaceChoice(root.DropDown, "Cartesian (TCP)", true);
            AppendSpaceChoice(root.DropDown, "Joint", false);
        }

        private void AppendSpaceChoice(ToolStrip dropdown, string label, bool value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (isCartesian == value) return;
                RecordUndoEvent(label);
                isCartesian = value;
                updateParamName(isCartesian);
                ExpireSolution(true);
            }, true, isCartesian == value);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("IsCartesian", isCartesian);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool b = true;
            if (reader.TryGetBoolean("IsCartesian", ref b))
                isCartesian = b;

            bool result = base.Read(reader);
            // relabel straight away so a reopened definition shows the space it was saved in
            updateParamName(isCartesian);
            return result;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                return Vizor.Properties.Resources.Trajectory;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9a03197b-3b8b-492e-93f1-32baf9fd7a12"); }
        }
    }
}
