
namespace VizorLibs.MessageTypes
{
    public class ROSMessageContent : ROSMessage
    {
        new public SceneContentMsg msg { get; set; }
    }

    public class ROSMessageGeometry : ROSMessage
    {
        new public SceneGeometryMsg msg { get; set; }
    }

    public class ROSMessageText : ROSMessage
    {
        new public SceneTextMsg msg { get; set; }
    }
    public class ROSMessageInteractable : ROSMessage
    {
        new public InteractableMsg msg { get; set; }
    }

    public class SceneContentMsg
    {
        public const string k_RosMessageName = "vizor_package/SceneContent";
        public string operation { get; set; }
        public string name { get; set; }
        public SceneGeometryMsg[] geometries { get; set; }
        public SceneWireframeMsg[] wires { get; set; }
        public SceneTextMsg[] texts { get; set; }
        public int LoD { get; set; }

        public SceneContentMsg() 
        {
            operation = "";
            name = "";
            geometries = new SceneGeometryMsg[] { };
            wires = new SceneWireframeMsg[] { };
            texts = new SceneTextMsg[] { };
            LoD = 0;
        }
    }

    public class SceneWireframeMsg
    {
        public const string k_RosMessageName = "vizor_package/SceneWireframe";
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; }
        public float width{ get; set; }
        public BuiltInMsg.Point [] points { get; set; }

        public SceneWireframeMsg() {
            operation = "";
            name = "";
            layer = "";
            material = "";
            width = 0.01f;
            points = new BuiltInMsg.Point[]{ };
        }
    }

    public class SceneGeometryMsg
    {
        public const string k_RosMessageName = "vizor_package/SceneGeometry";
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; }
        public BuiltInMsg.MeshMsg mesh { get; set; }

        public SceneGeometryMsg()
        {
            this.operation = "";
            this.layer = "";
            this.name = "";
            this.material = "";
            this.mesh = new BuiltInMsg.MeshMsg();
        }

        public SceneGeometryMsg(string operation, string layer, string name, string material, BuiltInMsg.MeshMsg mesh)
        {
            this.operation = operation;
            this.layer = layer;
            this.name = name;
            this.material = material;
            this.mesh = mesh;
        }

    }

    public class SceneTextMsg
    {
        public const string k_RosMessageName = "vizor_package/SceneText";
        public string operation { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; }
        public string text { get; set; }
        public BuiltInMsg.Transform transform { get; set; }

        public SceneTextMsg() {
            operation = "";
            name = "";
            layer = "";
            material = "";
            text = "";
            transform = new BuiltInMsg.Transform();
        }

        public SceneTextMsg(string operation, string layer, string name, string material, BuiltInMsg.Transform transform, string text)
        {
            this.operation = operation;
            this.layer = layer;
            this.name = name;
            this.material = material;
            this.text = text;
            this.transform = transform;
        }

    }


    public class InteractableMsg
    {
        public const string k_RosMessageName = "vizor_package/Interactable";
        public string type { get; set; }
        public string topic { get; set; }
        public string message { get; set; }
        public string layer { get; set; }
        public string name { get; set; }
        public string material { get; set; }
        public BuiltInMsg.MeshMsg mesh { get; set; }

        public InteractableMsg()
        {
            this.type = "";
            this.topic = "";
            this.message = "";
            this.layer = "";
            this.name = "";
            this.material = "";
            this.mesh = new BuiltInMsg.MeshMsg();
        }

        public InteractableMsg(string type, string topic, string message, string layer, string name, string material, BuiltInMsg.MeshMsg mesh)
        {
            this.type = type;
            this.topic = topic;
            this.message = message;
            this.layer = layer;
            this.name = name;
            this.material = material;
            this.mesh = mesh;
        }
    }

    //TODO: Add image / video message
}
