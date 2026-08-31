using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Text;
using VizorLibs;
using VizorLibs.MessageTypes;
using Vizor._1_System;

namespace Vizor._2_Content
{
    /// <summary>
    /// The MakeInteractable class is a Grasshopper component designed to create interactable mesh objects for AR spaces.
    /// It takes input parameters such as target anchor device, AR device, meshes, names, colors, display rules, interaction type, topic names, and messages,
    /// and publishes interactable objects to the AR device.
    ///
    /// Input:
    /// - Target Anchor (Device): The device to anchor the mesh geometry to (for transformation).
    /// - Device (Device): The AR device to send the interactable to (for WebSocket connection).
    /// - Meshes (List<Mesh>): A list of meshes to make interactable.
    /// - Names (List<string>): Optional names for the interactable objects.
    /// - Colors (List<Color>): Optional colors for the interactable objects.
    /// - Display Rules (List<string>): Optional rules for object display (e.g., persistent, session, step, flange).
    /// - Type (string): Interaction type (default: "pub-custom").
    /// - Topic Names (List<string>): Topic names for the interactables (if only one is provided it will be applied to all).
    /// - Messages (List<string>): Messages to publish on interaction (if only one is provided it will be applied to all).
    ///
    /// Output:
    /// - Output (string): A summary of the published interactable objects.
    /// - Interactable Objects (List<InteractableMsg>): The processed interactable message objects.
    ///
    /// Notes:
    /// - Publishes to {deviceName}_Interactable topic when component is active.
    /// - Default values are provided for names, colors, display rules, topic names, and messages if not explicitly specified.
    /// </summary>

    public class MakeInteractable : GH_Component
    {
        private Device anchorDevice;
        private Device device;
        private List<InteractableMsg> interactables;
        private List<Mesh> meshes;
        private List<string> names;
        private List<System.Drawing.Color> colours;
        private List<string> materials;
        private List<string> rules;
        private string interactionType;
        private List<string> topicNames;
        private List<string> interactionMessages;
        private string lastPublishedSignature;
        private string lastAdvertisedTopic;

        /// <summary>
        /// Initializes a new instance of the MakeInteractable class.
        /// </summary>
        public MakeInteractable()
          : base("Scene Interactable Object", "Interactable",
              "Create interactable objects for the AR space",
              "VizorGH", "2_Content")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Target Anchor", "T", "target device to anchor the mesh geometry to", GH_ParamAccess.item);
            pManager.AddGenericParameter("Device", "D", "target AR device to send the interactable to", GH_ParamAccess.item);
            pManager.AddMeshParameter("Meshes", "M", "list of meshes for interactable objects", GH_ParamAccess.list);

            pManager.AddTextParameter("Names", "N", "list of object names (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "interactable");
            pManager.AddColourParameter("Colours", "C",
                "list of colours for the objects (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, System.Drawing.Color.FromArgb(255, 9, 169, 237));
            pManager.AddTextParameter("Display Rules", "R",
                "list of rules, e.g., one of [persistent, session, step] (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "session");
            pManager.AddTextParameter("Type", "Typ", "interaction type", GH_ParamAccess.item, "pub-custom");
            pManager.AddTextParameter("Topic Names", "Topic", "list of topic names for the interactables (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "my_topic");
            pManager.AddTextParameter("Messages", "Msg", "list of messages to publish on interaction (if only one is provided it will be applied to all)",
                GH_ParamAccess.list, "my_message");
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "out", "summary of published interactable objects", GH_ParamAccess.item);
            pManager.AddGenericParameter("Interactable Objects", "I", "Interactable message objects", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize lists
            meshes = new List<Mesh>();
            names = new List<string>();
            colours = new List<System.Drawing.Color>();
            materials = new List<string>();
            rules = new List<string>();
            topicNames = new List<string>();
            interactionMessages = new List<string>();

            // Get inputs
            if (!DA.GetData(0, ref anchorDevice)) return;
            if (!DA.GetData(1, ref device)) return;
            if (!DA.GetDataList(2, meshes)) return;
            if (!DA.GetDataList(3, names)) return;
            if (!DA.GetDataList(4, colours)) return;
            if (!DA.GetDataList(5, rules)) return;
            if (!DA.GetData(6, ref interactionType)) return;
            if (!DA.GetDataList(7, topicNames)) return;
            if (!DA.GetDataList(8, interactionMessages)) return;

            int meshCount = meshes.Count;
            if (meshCount == 0 || names.Count == 0 || colours.Count == 0 || rules.Count == 0 || topicNames.Count == 0 || interactionMessages.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Meshes and all list inputs must contain at least one item (or use the default values).");
                return;
            }
            if ((names.Count != 1 && names.Count != meshCount) ||
                (colours.Count != 1 && colours.Count != meshCount) ||
                (rules.Count != 1 && rules.Count != meshCount) ||
                (topicNames.Count != 1 && topicNames.Count != meshCount) ||
                (interactionMessages.Count != 1 && interactionMessages.Count != meshCount))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Names/Colours/Display Rules/Topic Names/Messages must have either 1 item or match the Meshes count.");
                return;
            }

            // Check if device has websocket connection
            if (device == null || device.wscObj == null || !device.wscObj.isConnected())
            {
                interactables = new List<InteractableMsg>();
                lastPublishedSignature = null;
                lastAdvertisedTopic = null;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Device is not connected. Please provide a connected device.");
                DA.SetData(0, "Device not connected");
                DA.SetDataList(1, interactables);
                return;
            }

            // Convert colors to material strings
            foreach (System.Drawing.Color col in colours)
            {
                materials.Add(String.Format("{0},{1},{2},{3}", col.R, col.G, col.B, col.A));
            }

            interactables = new List<InteractableMsg>();
            string output = "";
            int meshFaces = 0;
            StringBuilder signatureBuilder = new StringBuilder();
            signatureBuilder.Append(device.name).Append("|").Append(interactionType).Append("|").Append(meshes.Count).Append(";");

            // Create interactable messages for each mesh
            for (int i = 0; i < meshes.Count; i++)
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

                // Transform mesh using anchorDevice
                Mesh transformedMesh = VizorUtilities.IsLinkAttachedRule(rule) ? meshes[i] : VizorUtilities.TransformVisualisation(meshes[i], anchorDevice);

                // Get topic and message for this mesh
                string topicName = topicNames.Count == 1 ? topicNames[0] : topicNames[i];
                string message = interactionMessages.Count == 1 ? interactionMessages[0] : interactionMessages[i];

                // Create interactable message
                InteractableMsg interactableMsg = new InteractableMsg
                {
                    type = interactionType,
                    topic = topicName,
                    message = message,
                    layer = VizorUtilities.GetLayerFromRule(rule, anchorDevice),
                    name = name,
                    material = materials.Count == 1 ? materials[0] : materials[i],
                    mesh = MsgDataConverter.ghMeshToMsg(transformedMesh)
                };

                interactables.Add(interactableMsg);

                meshFaces += meshes[i].Faces.Count;
                output += String.Format("{0}, {1} faces\n", name, meshes[i].Faces.Count);

                string material = materials.Count == 1 ? materials[0] : materials[i];
                BoundingBox bbox = transformedMesh.GetBoundingBox(true);
                signatureBuilder.Append(name).Append("|")
                  .Append(rule).Append("|")
                  .Append(topicName).Append("|")
                  .Append(message).Append("|")
                  .Append(material).Append("|")
                  .Append(transformedMesh.Vertices.Count).Append("|")
                  .Append(transformedMesh.Faces.Count).Append("|")
                  .Append(bbox.Min.X).Append(",").Append(bbox.Min.Y).Append(",").Append(bbox.Min.Z).Append("|")
                  .Append(bbox.Max.X).Append(",").Append(bbox.Max.Y).Append(",").Append(bbox.Max.Z).Append(";");
            }

            string currentSignature = signatureBuilder.ToString();
            if (currentSignature != lastPublishedSignature)
            {
                string topic = device.name + "_Interactable";
                if (lastAdvertisedTopic != topic)
                {
                    ROSMessageHandler.Advertise(device.wscObj, topic, "vizor_package/Interactable");
                    lastAdvertisedTopic = topic;
                }

                foreach (InteractableMsg interactable in interactables)
                {
                    ROSMessageHandler.PublishInteractable(device.wscObj, device.name, interactable);
                }
                lastPublishedSignature = currentSignature;
            }

            this.Message = String.Format("{0} faces ({1})", meshFaces, VizorUtilities.GetTypeSummary(rules));
            DA.SetDataList(1, interactables);
            DA.SetData(0, output);
        }

        /// <summary>
        /// Custom preview display for mesh objects in the Rhino viewport
        /// </summary>
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);

            if (meshes == null || meshes.Count == 0)
                return;

            // Draw meshes with their colors
            for (int i = 0; i < meshes.Count; i++)
            {
                System.Drawing.Color color = colours.Count == 1 ? colours[0] : colours[i];
                args.Display.DrawMeshShaded(meshes[i], new Rhino.Display.DisplayMaterial(color));
            }
        }

        /// <summary>
        /// Override bounding box to include mesh geometry
        /// </summary>
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;

                if (meshes != null)
                {
                    foreach (Mesh mesh in meshes)
                    {
                        bbox.Union(mesh.GetBoundingBox(false));
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
                return Vizor.Properties.Resources.SceneInteractable;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("9AD8B02C-D076-4A92-9F5B-A12623B2894D"); }
        }
    }
}
