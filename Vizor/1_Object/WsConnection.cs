using System;
using Grasshopper;
using Grasshopper.Kernel;
using VizorLibs;
using VizorLibs.MessageTypes;

namespace Vizor._1_System
{
    /// <summary>
    /// The WsConnection component is responsible for establishing a WebSocket connection to a specified server.  
    /// The Websocket implementation here based on the open source work of Bengesht. 
	/// 
    /// Inputs:  
    /// - address: WebSocket server address. Scheme (ws://) should be included. For example, ws://127.0.0.1:9090
    /// - reset: Boolean flag to restart the connection.  
    ///  
    /// Outputs:  
    /// - Websocket Object: Provides access to the connection. Can be connected to device WebSocket inputs.
    /// </summary>
    public class WsConnection : GH_Component
    {
        public WsConnection()
          : base("WsConnection", "WsStart",
              "Start a new connection",
              "VizorGH", "1_Object")
        {
			this.isSubscribedToEvents = false;
		}

		private WsObject wscObj;
		private bool isSubscribedToEvents;
		private GH_Document ghDocument;
		private string Info = "Disconnected";

		~WsConnection()
		{
			this.Disconnect();
		}

		protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
		{
			pManager.AddTextParameter("address", "URL", "Websocket server address. Scheme (ws://) should be included. For example ws://echo.websocket.org", 
				GH_ParamAccess.item, "ws://localhost:9090");
			pManager.AddBooleanParameter("connectToggle", "connectToggle", "Connect when true; disconnect when false.",
				GH_ParamAccess.item, false);
		}

		protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
		{
			pManager.AddGenericParameter("Websocket Object", "WSC", "This object provides access to the connection. Connect this output to device web socket inputs.", GH_ParamAccess.item);
			pManager.AddTextParameter("Connection Info", "Info", "This output puts the current status of the connection", GH_ParamAccess.item);
		}

		/// <summary>
		/// Detecting the deletion of this component and run disconnect function.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void DocumentOnObjectsDeleted(object sender, GH_DocObjectEventArgs e)
		{
			if (e.Objects.Contains(this))
			{
				e.Document.ObjectsDeleted -= DocumentOnObjectsDeleted;
				this.Disconnect();
			}
		}
		
		private void DocumentServerOnDocumentClosed(GH_DocumentServer sender, GH_Document doc)
		{
			if (this.ghDocument != null && doc.DocumentID == this.ghDocument.DocumentID)
			{
				this.Disconnect();
			}
		}
		
		void OnObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
		{
			if (this.Locked)
				this.Disconnect();
		}
		
		private void SubscribeToEvents()
		{
			if (!this.isSubscribedToEvents)
			{
				this.ghDocument = OnPingDocument();

				if (this.ghDocument != null)
				{
					this.ghDocument.ObjectsDeleted += DocumentOnObjectsDeleted;
					Instances.DocumentServer.DocumentRemoved += DocumentServerOnDocumentClosed;
				}

				this.ObjectChanged += this.OnObjectChanged;
				this.isSubscribedToEvents = true;
			}
		}

		protected override void SolveInstance(IGH_DataAccess DA)
		{
			this.SubscribeToEvents();

			string address = null;
			bool connect = false;

			if (!DA.GetData(0, ref address) || !DA.GetData(1, ref connect)) return;

			if (!connect)
			{
				this.Disconnect();
				DA.SetData(0, null);
				DA.SetData(1, this.Info);
				return;
			}

			if (!WsObject.isAdressValid(address))
			{
				this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid websocket address");
				return;
			}

			if (this.wscObj == null || !this.wscObj.isSameAdress(address))
			{
				this.Disconnect();
				this.wscObj = new WsObject().init(address, null);
				this.Message = "Connecting";
				this.Info = "Connecting";
				this.wscObj.changed += this.WsObjectOnChange;
				this.wscObj.statusChanged += this.WsStatusOnChange;
				this.wscObj.Start();
			} else {
				this.Message = this.wscObj.status.ToString().ToLower();
			}

			if (this.wscObj.status != WsObject.ConnectionStatus.OPEN && this.wscObj.status != WsObject.ConnectionStatus.MESSAGE)
			{
				this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not connect to websocket server");
			}

			DA.SetData(0, this.wscObj);
			DA.SetData(1, this.Info);
		}

		
		// Update the Message of the component to the last changed status
		private void WsObjectOnChange(object sender, EventArgs e)
		{
			WsObject socket = sender as WsObject;
			if (socket == null) return;
			this.Message = socket.status.ToString().ToLower();
		}

		// Whenever the status of the websocket changes, expire the solution
		private void WsStatusOnChange(object sender, EventArgs e)
		{
			WsObject socket = sender as WsObject;
			if (socket == null) return;
			this.Info = socket.status.ToString().ToLower() + ": " + socket.diagnostic;

			// expire solution
            Grasshopper.Instances.DocumentEditor.BeginInvoke((Action)delegate ()
            {
                if (ghDocument.SolutionState != GH_ProcessStep.Process)
                {
                    this.ExpireSolution(true);
                }
            });
		}

		private void Disconnect()
		{
			if (this.wscObj == null) return;

			_ = this.wscObj.disconnect();
			this.wscObj.changed -= this.WsObjectOnChange;
			this.wscObj.statusChanged -= this.WsStatusOnChange;
			this.wscObj = null;
			this.Info = "Disconnected";
			this.Message = "disconnected";
		}

		/// <summary>
		/// Provides an Icon for the component.
		/// </summary>
		protected override System.Drawing.Bitmap Icon
        {
            get
            {
				//You can add image files to your project resources and access them like this:
				return Vizor.Properties.Resources.WsConnection;
			}
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("4dd685c6-885c-4d9f-a807-c25d526811a9"); }
        }
    }
}
