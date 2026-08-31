using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Vizor._1_System;
using VizorLibs.MessageTypes;
using VizorLibs;
using System.Linq;
using Transform = Rhino.Geometry.Transform;
using Mesh = Rhino.Geometry.Mesh;
using System.ComponentModel;
using System.Xml.Linq;
using Vizor.Properties;
using System.Text;
using System.Windows.Forms;
using GH_IO.Serialization;

namespace Vizor._2_Content
{

    ///<summary>
    /// The ModelTracker component is responsible for tracking and managing mesh objects in an AR space.
    /// It listens to pose changes sent from AR devices (e.g., HoloLens) and updates the poses of model elements accordingly.
    /// The component provides transformed scene geometry objects as output, which can be used to reflect changes in the AR space.
    ///
    /// How it Works:
    /// - Accepts SceneGeometryObjects (from MakeMesh) as input, preserving their material and layer settings.
    /// - Tracks and updates the orientation and translation of meshes based on live AR device data.
    /// - Each received transform is applied as a delta on top of the object's cumulative transform,
    ///   which is always tracked relative to the original (untransformed) input mesh.
    /// - Publishes the committed geometry back to the AR device automatically on change.
    /// - Outputs the current committed SceneGeometryObjects, directly usable in SceneModel or ConstructContent.
    ///
    /// Inputs:
    /// - Online (Boolean): Enables or disables live sensor data reception.
    /// - AR Device (Generic): The AR device manipulating the model.
    /// - Scene Geometry Objects (Generic List): SceneGeometryObjects to track (output of MakeMesh).
    /// - Model Name (Text): Scene name used when publishing committed geometry back to the AR device.
    ///
    /// Outputs:
    /// - Status (Text): Status of the component, including connection and update information.
    /// - Names (Text List): Names of the tracked model elements.
    /// - Transform (Transform List): Cumulative transform per tracked model, relative to the original
    ///   input mesh — applying this to the original mesh reproduces the current committed mesh exactly.
    /// - Meshes (Mesh List): Committed meshes with updated mesh positions.
    /// </summary>
    public class ModelTracker : VizorBaseComponent
    {
        Device currentDevice;
        private List<string> modelNames = new List<string>();
        private List<Transform> modelOrient = new List<Transform>();
        private List<Transform> modelTranslate = new List<Transform>();

        private List<SceneGeometryObject> _geomObjects;
        private Dictionary<string, SceneGeometryObject> originalStore; // untransformed baseline; only reset alongside store
        private Dictionary<string, SceneGeometryObject> store; // persists committed state; only reset when offline
        private Dictionary<string, Transform> cumulativeTransforms = new Dictionary<string, Transform>(); // per-name, relative to originalStore
        private bool needsInitialPublish = false;
        private string storeInputSignature;
        private string _publishedModelName = null;
        private bool _wasOnline = false;

        // Manual: optimized for input that rarely changes — while Online, input changes are
        // only reported, and cumulative transforms are held until the user explicitly resets
        // (toggle Online off/on), which then scrubs every transform back to identity.
        // Automatic: optimized for input that's constantly changing — whenever an input-driven
        // solve detects the geometry actually changed, the store is re-synced AND cumulative
        // transforms are scrubbed back to identity, no manual reset needed. AR pose deltas only
        // accumulate between one input change and the next.
        private bool dynamicInputMode = false;

        /// <summary>
        /// Initializes a new instance of the ModelTracker class.
        /// </summary>
        public ModelTracker()
          : base("ModelTracker", "ModelTracker",
              "Listen to changes to changed poses for model elements",
              "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Online", "O",
                "Set this to true if you want GH to receive the live sensor data. \n" +
                "If this is false, the data will still be sent to the target devices, but GH will not provide the sensor reading. ", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("AR Device", "HoloLens", "AR device which is manipulating the model", GH_ParamAccess.item);
            pManager.AddGenericParameter("Scene Meshes", "SMs", "Scene Mesh Objects to track (output of Scene Mesh Object)", GH_ParamAccess.list);
            pManager.AddTextParameter("Model Name", "MN", "Model name used when publishing the committed geometry back to the AR device", GH_ParamAccess.item, "tracked_model");
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status", "status output", GH_ParamAccess.item);
            pManager.AddTextParameter("Names", "N", "names of the tracked model elements", GH_ParamAccess.list);
            pManager.AddTransformParameter("Transform", "X", "Cumulative transform per tracked model, relative to the original input mesh — applying this to the original mesh reproduces the committed mesh exactly", GH_ParamAccess.list);
            pManager.AddMeshParameter("Meshes", "M", "Committed meshes with updated positions", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!IsDocumentActive()) return;

            DA.GetData(0, ref this.isListener);
            _geomObjects = new List<SceneGeometryObject>();
            DA.GetDataList(2, _geomObjects);

            string modelName = "tracked_model";
            DA.GetData(3, ref modelName);

            if (!isListener)
            {
                string currentSignature = BuildStoreInputSignature(_geomObjects);
                bool justWentOffline = _wasOnline && !isListener;
                bool geometryChanged = store == null || currentSignature != storeInputSignature;

                if (wscObj != null && _publishedModelName != null && (justWentOffline || geometryChanged))
                {
                    RemoveScene(_publishedModelName);
                    _publishedModelName = null;
                }

                if (geometryChanged)
                {
                    // Dispose the outgoing store's and baseline's meshes before dropping the
                    // references — GeometryBase wraps unmanaged memory that the GC won't
                    // reclaim promptly.
                    if (store != null)
                    {
                        foreach (SceneGeometryObject geom in store.Values)
                            geom.gMesh?.Dispose();
                    }
                    if (originalStore != null)
                    {
                        foreach (SceneGeometryObject geom in originalStore.Values)
                            geom.gMesh?.Dispose();
                    }

                    // Rebuild baseline + store from current inputs when offline — resets to GH
                    // baseline and zeroes each object's cumulative transform. Meshes are duplicated
                    // so both are independent from the GH input objects.
                    originalStore = new Dictionary<string, SceneGeometryObject>();
                    store = new Dictionary<string, SceneGeometryObject>();
                    cumulativeTransforms = new Dictionary<string, Transform>();
                    foreach (SceneGeometryObject geom in _geomObjects)
                    {
                        if (geom != null && geom.gMesh != null)
                            UpsertTrackedObject(geom);
                    }
                    storeInputSignature = currentSignature;
                    needsInitialPublish = true;
                }
                else if (justWentOffline)
                {
                    needsInitialPublish = true;
                }
                modelNames.Clear();
                modelOrient.Clear();
                modelTranslate.Clear();
                this.Message = String.Format("new objects: {0}", store.Count);
            }
            else
            {
                // Only check/sync against GH inputs on solves that weren't triggered by an
                // incoming AR pose message — an incoming message shouldn't pay for (or be
                // blocked behind) a geometry comparison against inputs that haven't changed.
                if (!this.onMessageTriggered)
                {
                    if (originalStore == null)
                    {
                        originalStore = new Dictionary<string, SceneGeometryObject>();
                        store = new Dictionary<string, SceneGeometryObject>();
                        cumulativeTransforms = new Dictionary<string, Transform>();
                    }

                    string currentSignature = BuildStoreInputSignature(_geomObjects);
                    bool inputChanged = store.Count == 0 || currentSignature != storeInputSignature;

                    if (dynamicInputMode)
                    {
                        // Only actually resync (and republish) when the input really changed —
                        // avoids re-cloning every tracked mesh and republishing the whole scene
                        // on every solve, and keeps storeInputSignature current so switching
                        // back to Manual mode doesn't see a stale mismatch.
                        if (inputChanged)
                        {
                            SyncStoreWithInputs(_geomObjects);
                            storeInputSignature = currentSignature;
                        }
                        this.Message = String.Format("{0} objects (automatic)", store.Count);
                    }
                    else
                    {
                        if (inputChanged)
                        {
                            this.Message = "Input changed - reset!";
                            return;
                        }
                        this.Message = String.Format("{0} objects", store.Count);
                    }
                }
                else if (originalStore == null)
                {
                    // A message arrived before the store was ever initialized (e.g. Online was
                    // switched on and a message beat the first normal solve) — nothing to apply yet.
                    this.onMessageTriggered = false;
                    return;
                }
            }

            if (!this.onMessageTriggered)
            {
                if (DA.GetData(1, ref currentDevice))
                {
                    bool deviceUpdated = UpdateDevice(currentDevice);
                    if (needsInitialPublish && wscObj != null && isListener)
                    {
                        if (_publishedModelName != null && _publishedModelName != modelName)
                            RemoveScene(_publishedModelName);
                        PublishStore(modelName);
                        needsInitialPublish = false;
                    }
                    if (deviceUpdated)
                        DA.SetData(0, "device updated\nlast updated on " + DateTime.Now.ToString());
                }
                else
                {
                    CleanupConnection();
                    DA.SetData(0, "no connection");
                    return;
                }
            }
            else
            {
                this.onMessageTriggered = false;

                if (this.wscObj.message != null)
                {
                    ModelMsg data = ROSMessageHandler.ParseModelMessage(currentDevice.name, wscObj.message);

                    if (data != null)
                    {
                        modelNames.Clear();
                        modelOrient.Clear();
                        modelTranslate.Clear();

                        for (int i = 0; i < data.names.Length; i++)
                        {
                            string name = data.names[i];
                            BuiltInMsg.Pose pose = data.poses[i];

                            /*
                             * Convert quaternion w, y, z, x
                             * Convert position -y, x, z
                               ROS  FLU  (X = forward, Y = left, Z = up)
                               Rhino ENU (X = east / right, Y = north / forward, Z = up)
                             * Position arrives in ROS units (meters); scale up to the
                               active Rhino document's units before building the transform.
                            */
                            float rosToRhino = MsgDataConverter.ROSToRhinoMultiplier();
                            Point3d pos = new Point3d(-pose.position.y * rosToRhino, pose.position.x * rosToRhino, pose.position.z * rosToRhino);
                            Rhino.Geometry.Quaternion q = new Rhino.Geometry.Quaternion
                                (pose.orientation.w, -pose.orientation.y, pose.orientation.x, pose.orientation.z);

                            q.GetRotation(out Plane plane);
                            Transform xform = Transform.PlaneToPlane(Plane.WorldXY, plane);
                            Transform tform = Transform.Translation(pos - Point3d.Origin);

                            modelNames.Add(name);
                            modelOrient.Add(xform);
                            modelTranslate.Add(tform);
                        }

                        // Only commit and publish if at least one transform is non-identity (something actually moved)
                        bool hasChanges = modelOrient.Any(x => !x.IsIdentity) || modelTranslate.Any(t => !t.IsIdentity);
                        if (hasChanges)
                        {
                            // Snapshot current cumulative transforms so duplicate names in a single
                            // message don't compound onto each other within the same batch.
                            var baselineCumulative = new Dictionary<string, Transform>(cumulativeTransforms);

                            for (int i = 0; i < modelNames.Count; i++)
                            {
                                // Skip objects whose own pose didn't change — avoids needless
                                // mesh duplication/disposal churn for the rest of the batch.
                                if (modelOrient[i].IsIdentity && modelTranslate[i].IsIdentity)
                                    continue;

                                string name = modelNames[i];
                                if (!originalStore.ContainsKey(name))
                                    continue;

                                // Combine this message's rotation + translation into a single delta,
                                // then fold it onto the object's prior cumulative transform (relative
                                // to the untransformed original mesh, not the last committed pose).
                                Transform delta = modelTranslate[i] * modelOrient[i];
                                Transform baseCumulative = baselineCumulative.TryGetValue(name, out Transform bc) ? bc : Transform.Identity;
                                cumulativeTransforms[name] = delta * baseCumulative;

                                RebuildCommittedMesh(name);
                            }

                            PublishStore(modelName);
                        }

                    }

                    DA.SetData(0, "committed. \nlast updated on " + DateTime.Now.ToString());
                }
            }

            // Always output the current committed store state, keyed consistently so Names,
            // Transform, and Meshes line up index-for-index.
            List<string> names = store.Keys.ToList();
            DA.SetDataList(1, names);
            DA.SetDataList(2, names.Select(n => cumulativeTransforms.TryGetValue(n, out Transform t) ? t : Transform.Identity).ToList());
            DA.SetDataList(3, names.Select(n => store[n].gMesh).ToList());
            _wasOnline = isListener;
        }

        /// <summary>
        /// Rebuilds the committed mesh for a tracked object by transforming a fresh duplicate of its
        /// original (untransformed) mesh with its current cumulative transform. Recomputing from the
        /// original each time — rather than re-transforming the previous committed mesh — keeps the
        /// invariant that committed mesh == Transform(cumulativeTransforms[name], originalStore[name])
        /// exact, with no accumulated floating-point drift across many updates.
        /// </summary>
        private void RebuildCommittedMesh(string name)
        {
            if (!originalStore.TryGetValue(name, out SceneGeometryObject original)) return;

            Mesh mesh = original.gMesh.DuplicateMesh();
            mesh.Transform(cumulativeTransforms[name]);

            // Dispose the mesh being replaced — GeometryBase wraps unmanaged native memory that
            // isn't freed by the GC until finalization, so leaving this to the GC lets native
            // memory balloon under frequent pose updates (e.g. live AR tracking).
            if (store.TryGetValue(name, out SceneGeometryObject previous))
                previous.gMesh?.Dispose();

            store[name] = new SceneGeometryObject
            {
                operation = original.operation,
                layer = original.layer,
                name = original.name,
                material = original.material,
                gMesh = mesh
            };
        }

        /// <summary>
        /// Adds or replaces a tracked object's baseline in originalStore — disposing any
        /// previous mesh under that name — resets its cumulative transform to identity, and
        /// rebuilds its committed mesh. Shared by the offline reset path and the Automatic-mode
        /// sync, which both need "start this object clean from its current input geometry."
        /// </summary>
        private void UpsertTrackedObject(SceneGeometryObject geom)
        {
            if (originalStore.TryGetValue(geom.name, out SceneGeometryObject existing))
                existing.gMesh?.Dispose();

            originalStore[geom.name] = new SceneGeometryObject
            {
                operation = geom.operation,
                layer = geom.layer,
                name = geom.name,
                material = geom.material,
                gMesh = geom.gMesh.DuplicateMesh()
            };
            cumulativeTransforms[geom.name] = Transform.Identity;
            RebuildCommittedMesh(geom.name);
        }

        /// <summary>
        /// Automatic-mode sync: keeps originalStore/store aligned with whatever geometry is
        /// currently connected, without an explicit reset step. Only called once the caller has
        /// already determined the input actually changed, so every synced object's cumulative
        /// transform is scrubbed back to identity here — matching the "input is always changing"
        /// use case this mode is optimized for: each new baseline starts clean, and only AR pose
        /// deltas received after this solve accumulate on top of it until the next input change
        /// scrubs it again.
        /// </summary>
        private void SyncStoreWithInputs(List<SceneGeometryObject> geomObjects)
        {
            var incomingNames = new HashSet<string>();

            foreach (SceneGeometryObject geom in geomObjects)
            {
                if (geom == null || geom.gMesh == null) continue;
                incomingNames.Add(geom.name);
                UpsertTrackedObject(geom);
            }

            List<string> removedNames = originalStore.Keys.Where(n => !incomingNames.Contains(n)).ToList();
            foreach (string name in removedNames)
            {
                originalStore[name].gMesh?.Dispose();
                originalStore.Remove(name);
                if (store.TryGetValue(name, out SceneGeometryObject removed))
                {
                    removed.gMesh?.Dispose();
                    store.Remove(name);
                }
                cumulativeTransforms.Remove(name);
            }

            needsInitialPublish = true;
        }

        private void RemoveScene(string modelName)
        {
            if (wscObj == null || currentDevice == null) return;
            SceneContentObject removeContent = new SceneContentObject
            {
                name = modelName,
                operation = "remove",
                geomObjects = new SceneGeometryObject[0],
                wireObjects = new SceneWireframeObject[0],
                textObjects = new SceneTextObject[0],
                LoD = 1
            };
            ROSMessageHandler.PublishContent(wscObj, currentDevice.name, removeContent.GetSceneContentMsg());
        }

        private void PublishStore(string modelName)
        {
            if (wscObj == null || currentDevice == null) return;

            SceneGeometryObject[] geomObjects = store
                .Select(kvp => new SceneGeometryObject
                {
                    operation = kvp.Value.operation,
                    layer = kvp.Value.layer,
                    name = kvp.Value.name,
                    material = kvp.Value.material,
                    gMesh = kvp.Value.gMesh
                }).ToArray();

            SceneContentObject sceneContent = new SceneContentObject
            {
                name = modelName,
                operation = "add-scene",
                geomObjects = geomObjects,
                wireObjects = new SceneWireframeObject[0],
                textObjects = new SceneTextObject[0],
                LoD = 1
            };

            ROSMessageHandler.PublishContent(wscObj, currentDevice.name, sceneContent.GetSceneContentMsg());
            _publishedModelName = modelName;
        }

        private string BuildStoreInputSignature(List<SceneGeometryObject> geomObjects)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(geomObjects.Count).Append(";");
            foreach (SceneGeometryObject geom in geomObjects)
            {
                if (geom == null || geom.gMesh == null)
                {
                    sb.Append("null;");
                    continue;
                }

                sb.Append(geom.name).Append("|")
                  .Append(geom.operation).Append("|")
                  .Append(geom.layer).Append("|")
                  .Append(geom.material).Append("|")
                  .Append(FingerprintMeshGeometry(geom.gMesh)).Append(";");
            }
            return sb.ToString();
        }

        private string FingerprintMeshGeometry(Mesh mesh)
        {
            unchecked
            {
                const long fnvOffsetBasis = 1469598103934665603;
                const long fnvPrime = 1099511628211;
                long hash = fnvOffsetBasis;

                hash = (hash ^ mesh.Vertices.Count) * fnvPrime;
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    Point3f v = mesh.Vertices[i];
                    hash = (hash ^ v.X.GetHashCode()) * fnvPrime;
                    hash = (hash ^ v.Y.GetHashCode()) * fnvPrime;
                    hash = (hash ^ v.Z.GetHashCode()) * fnvPrime;
                }

                hash = (hash ^ mesh.Faces.Count) * fnvPrime;
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    MeshFace face = mesh.Faces[i];
                    hash = (hash ^ face.A) * fnvPrime;
                    hash = (hash ^ face.B) * fnvPrime;
                    hash = (hash ^ face.C) * fnvPrime;
                    hash = (hash ^ face.D) * fnvPrime;
                    hash = (hash ^ (face.IsQuad ? 1 : 0)) * fnvPrime;
                }

                return hash.ToString("X");
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);

            ToolStripMenuItem root = Menu_AppendItem(menu, "Reset Input");
            AppendModeChoice(root.DropDown, "Manual", false);
            AppendModeChoice(root.DropDown, "Automatic", true);
        }

        private void AppendModeChoice(ToolStrip dropdown, string label, bool value)
        {
            Menu_AppendItem(dropdown, label, (s, e) =>
            {
                if (dynamicInputMode == value) return;
                RecordUndoEvent(label);
                dynamicInputMode = value;
                ExpireSolution(true);
            }, true, dynamicInputMode == value);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("DynamicInputMode", dynamicInputMode);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool b = false;
            if (reader.TryGetBoolean("DynamicInputMode", ref b))
                dynamicInputMode = b;
            return base.Read(reader);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Resources.ModelTracker;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("CE1AF613-6D17-4DD3-8D2D-626E55884D7D"); }
        }
    }
}
