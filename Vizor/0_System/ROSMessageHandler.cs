// This file contains methods for handling ROS messages, including subscribing, unsubscribing, and publishing messages.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using VizorLibs;
using VizorLibs.MessageTypes;
using Vizor._3_Robot;
using Rhino.Geometry;
using System.Linq;

namespace Vizor._1_System
{
    /// <summary>
    /// The ROSMessageHandler class provides a static interface for handling ROS (Robot Operating System) messages.  
    /// It includes methods for subscribing, unsubscribing, and advertising topics, as well as publishing various types of messages.  
    /// Additionally, it offers functionality to parse incoming messages for specific data types.  
    /// This class is designed to facilitate communication between the system and ROS-based robots or devices.  
    /// </summary>
    
    static class ROSMessageHandler
    {
        #region REGISTER PUBLISHER + SUBSCRIBER

        /// <summary>
        /// Subscribes to a specified topic with a given message type.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <param name="type">The message type.</param>
        public static void Subscribe(WsObject wsc, string topic, string type)
        {
            ROSMessage msg = new ROSMessage
            {
                op = "subscribe",
                topic = topic,
                type = type
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Unsubscribes from a specified topic with a given message type.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to unsubscribe from.</param>
        /// <param name="type">The message type.</param>
        public static void Unsubscribe(WsObject wsc, string topic, string type)
        {
            ROSMessage msg = new ROSMessage
            {
                op = "unsubscribe",
                topic = topic,
                type = type
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Subscribes to a topic under a caller-supplied rosbridge id.
        /// The id-less overload above shares one subscription per topic across the whole
        /// connection, so an unsubscribe from any component silently cuts off every other
        /// component listening to the same topic. Passing an id scopes both halves to the caller.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to subscribe to.</param>
        /// <param name="type">The message type, or null to let rosbridge resolve it.</param>
        /// <param name="id">Identifier echoed back on any status frame about this subscription.</param>
        public static void Subscribe(WsObject wsc, string topic, string type, string id)
        {
            SendSubscription(wsc, "subscribe", topic, type, id);
        }

        /// <summary>
        /// Unsubscribes a subscription previously made under the same rosbridge id.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to unsubscribe from.</param>
        /// <param name="type">The message type, or null if none was declared.</param>
        /// <param name="id">The id used when subscribing.</param>
        public static void Unsubscribe(WsObject wsc, string topic, string type, string id)
        {
            SendSubscription(wsc, "unsubscribe", topic, type, id);
        }

        /// <summary>
        /// Builds a subscribe/unsubscribe frame by hand rather than through <see cref="ROSMessage"/>,
        /// which always emits a "type" field: ROSMessage.type has no null handling, so an unset
        /// type would serialize as "type": null and rosbridge rejects a subscribe whose declared
        /// type conflicts with the topic's real type.
        /// </summary>
        private static void SendSubscription(WsObject wsc, string op, string topic, string type, string id)
        {
            JObject frame = new JObject();
            frame["op"] = op;
            frame["topic"] = topic;
            if (!string.IsNullOrEmpty(id)) frame["id"] = id;
            if (!string.IsNullOrEmpty(type)) frame["type"] = type;

            // JsonConvert.SerializeObject rather than JToken.ToString(Formatting): Rhino 8 ships
            // Newtonsoft.Json 13.0.3 and wins the assembly binding over the 13.0.4 this builds
            // against, so any 13.0.4-only overload throws MissingMethodException at runtime.
            wsc.send(JsonConvert.SerializeObject(frame));
        }

        /// <summary>
        /// Advertises a topic with a given message type.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to advertise.</param>
        /// <param name="type">The message type.</param>
        public static void Advertise(WsObject wsc, string topic, string type)
        {
            ROSMessage msg = new ROSMessage
            {
                op = "advertise",
                topic = topic,
                type = type
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Compares topics ignoring a leading slash, which ROS treats as the same topic but
        /// which publishers in this system are inconsistent about — ARWorker subscribes to
        /// "/{name}_GazePoint" while TrackedObject subscribes to "{name}/data", and
        /// ParseGazePointMessage already accepts either spelling.
        /// </summary>
        public static bool SameTopic(string a, string b)
        {
            if (a == null || b == null) return false;
            return a.TrimStart('/') == b.TrimStart('/');
        }

        #endregion

        #region OUTGOING MESSAGES (tasks)

        /// <summary>
        /// Publishes a task list message. This occurs once at the start of task execution.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="list">The task list message to publish.</param>
        public static void PublishTaskList(WsObject wsc, TaskListMsg list)
        {
            ROSMessageHandler.Advertise(wsc, "Task/listing", "vizor_package/TaskList");
            ROSMessage msg = new ROSMessageTaskList
            {
                op = "publish",
                topic = "Task/listing",
                type = "vizor_package/TaskList",
                msg = list,
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a task to the worker pool.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="task">The task message to publish.</param>
        public static void PublishTaskToPool(WsObject wsc, GeneralTaskMsg task)
        {
            ROSMessage msg = new ROSMessageTask
            {
                op = "publish",
                topic = "WorkerPool/task",
                type = "vizor_package/GeneralTask",
                msg = task
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a task to the data store.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="task">The task message to publish.</param>
        public static void PublishTaskToStore(WsObject wsc, GeneralTaskMsg task)
        {
            ROSMessage msg = new ROSMessageTask
            {
                op = "publish",
                topic = "DataStore/task/add",
                type = "vizor_package/GeneralTask",
                msg = task
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes an execution task to a specific robot.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="task">The task message to publish.</param>
        public static void PublishTaskToRobot(WsObject wsc, string target, GeneralTaskMsg task)
        {
            ROSMessage msg = new ROSMessageTask
            {
                op = "publish",
                topic = target + "/task/execute",
                type = "vizor_package/GeneralTask",
                msg = task
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a simulation task to a specific robot.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="task">The task message to publish.</param>
        public static void PublishSimTaskToRobot(WsObject wsc, string target, GeneralTaskMsg task)
        {
            ROSMessage msg = new ROSMessageTask
            {
                op = "publish",
                topic = target + "/task/simulate",
                type = "vizor_package/GeneralTask",
                msg = task
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        #endregion

        #region OUTGOING MESSAGES (actor signals)

        /// <summary>
        /// Publishes a mock reject message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="device">The device name.</param>
        /// <param name="taskId">The task ID.</param>
        public static void PublishMockReject(WsObject wsc, string topic, string device, int taskId)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String($"reject_{device}_{taskId}")
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a robot status message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="taskId">The task ID.</param>
        /// <param name="success">Indicates if the task was successful.</param>
        public static void PublishRobotStatus(WsObject wsc, string topic, int taskId, bool success)
        {
            int code = success ? 1 : 0;
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String($"{taskId}_{code}")
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a robot control message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="command">The command to send.</param>
        public static void PublishRobotControl(WsObject wsc, string topic, string command)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String($"{command}")
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a mock acknowledge message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        public static void PublishMockAcknowledge(WsObject wsc, string topic)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String("next_step")
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a mock hurry message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="name">The name of the device.</param>
        public static void PublishMockHurry(WsObject wsc, string topic, string name)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String("hurry_" + name)
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a mock help message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="name">The name of the device.</param>
        /// <param name="taskId">The task ID.</param>
        public static void PublishMockHelp(WsObject wsc, string topic, string name, int taskId)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String($"help_{name}_{taskId}")
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        public static void PublishStringMessage(WsObject wsc, string topic, string message)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String(message)
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        public static void PublishBoolMessage(WsObject wsc, string topic, bool value)
        {
            ROSMessage msg = new ROSMessageBool
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.Bool(value)
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        public static void PublishInt32Message(WsObject wsc, string topic, int value)
        {
            ROSMessage msg = new ROSMessageInt32
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.Int32(value)
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        public static void PublishFloat64Message(WsObject wsc, string topic, double value)
        {
            ROSMessage msg = new ROSMessageFloat64
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.Float64(value)
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        #endregion

        #region OUTGOING MESSAGES (custom objects)

        /// <summary>
        /// Publishes a work area message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="workAreasMsg">The work areas message to publish.</param>
        public static void PublishWorkArea(WsObject wsc, WorkAreasMsg workAreasMsg)
        {
            ROSMessage msg = new ROSMessageWorkAreas
            {
                op = "publish",
                topic = "/WorkZone/areas",
                msg = workAreasMsg
            };
            wsc.send(JsonConvert.SerializeObject(msg));
        }

        /// <summary>
        /// Publishes a sensor registration message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="sensorMsg">The sensor registration message to publish.</param>
        public static void PublishSensorRegistration(WsObject wsc, RegisterSensorMsg sensorMsg)
        {
            ROSMessage msg = new ROSMessageRegisterSensor
            {
                op = "publish",
                topic = "WorkerPool_Sensor",
                type = "vizor_package/RegistorSensor",
                msg = sensorMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a content message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="sceneContentMsg">The scene content message to publish.</param>
        public static void PublishContent(WsObject wsc, string target, SceneContentMsg sceneContentMsg)
        {
            ROSMessage msg = new ROSMessageContent
            {
                op = "publish",
                topic = target + "_Content",
                type = "vizor_package/SceneContent",
                msg = sceneContentMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes an interactable message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="deviceName">The target device name.</param>
        /// <param name="interactableMsg">The interactable message to publish.</param>
        public static void PublishInteractable(WsObject wsc, string deviceName, InteractableMsg interactableMsg)
        {
            ROSMessage msg = new ROSMessageInteractable
            {
                op = "publish",
                topic = deviceName + "_Interactable",
                type = "vizor_package/Interactable",
                msg = interactableMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a content message to the data store.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="sceneContentMsg">The scene content message to publish.</param>
        public static void UploadContent(WsObject wsc, SceneContentMsg sceneContentMsg)
        {
            ROSMessage msg = new ROSMessageContent
            {
                op = "publish",
                topic = "DataStore/scene/add",
                type = "vizor_package/SceneContent",
                msg = sceneContentMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a trajectory message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="robotTrajectoryMsg">The robot trajectory message to publish.</param>
        /// <returns>True if the message was sent (the websocket was open), false otherwise.</returns>
        public static bool PublishTrajectory(WsObject wsc, string target, RobotTrajectoryMsg robotTrajectoryMsg)
        {
            ROSMessage msg = new ROSMessageTrajectory
            {
                op = "publish",
                topic = target + "_Trajectory",
                type = "vizor_package/RobotPlatformTrajectory",
                msg = robotTrajectoryMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            return wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a planning geometry message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="planningGeometryMsg">The planning geometry message to publish.</param>
        public static void PublishPlanningGeometry(WsObject wsc, string target, PlanningGeometryMsg planningGeometryMsg)
        {
            ROSMessage msg = new ROSMessagePlanningGeometry
            {
                op = "publish",
                topic = target + "/scene/update",
                type = "vizor_package/PlanningGeometry",
                msg = planningGeometryMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a planning free message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="planningFreeMsg">The planning free message to publish.</param>
        public static void PublishPlanRequest(WsObject wsc, string target, PlanningFreeMsg planningFreeMsg)
        {
            ROSMessage msg = new ROSMessagePlanningFree
            {
                op = "publish",
                topic = target + "/request/free",
                type = "vizor_package/PlanningFree",
                msg = planningFreeMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a planning cartesian message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="planningCartesianMsg">The planning cartesian message to publish.</param>
        public static void PublishPlanRequest(WsObject wsc, string target, PlanningCartesianMsg planningCartesianMsg)
        {
            ROSMessage msg = new ROSMessagePlanningCartesian
            {
                op = "publish",
                topic = target + "/request/cartesian",
                type = "vizor_package/PlanningCartesian",
                msg = planningCartesianMsg
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a skill configuration message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="config">The skill configuration to publish.</param>
        public static void PublishSkillConfig(WsObject wsc, string target, string config)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = target + "_Config",
                msg = new BuiltInMsg.String(config)
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }
       
        /// <summary>
        /// Publishes an execution command message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="prog_name">The program name to execute.</param>
        public static void PublishExecutionCommand(WsObject wsc, string target, string prog_name)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = target + "/command/execute",
                msg = new BuiltInMsg.String(prog_name)
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// Publishes a path request command message.
        /// </summary>
        /// <param name="wsc">The websocket object.</param>
        /// <param name="target">The target robot.</param>
        /// <param name="prog_name">The program name to request.</param>
        public static void PublishPathRquestCommand(WsObject wsc, string target, string prog_name)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = target + "/request/stored",
                msg = new BuiltInMsg.String(prog_name)
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        /// <summary>
        /// TEMPORARY FUNCTION - needs to better suppot specific robot agents
        /// </summary>
        /// <param name="wsc"></param>
        /// <param name="command"></param>
        public static void CustomRobotCommand(WsObject wsc, string topic, string command)
        {
            ROSMessage msg = new ROSMessageString
            {
                op = "publish",
                topic = topic,
                msg = new BuiltInMsg.String(command)
            };
            string _msg = JsonConvert.SerializeObject(msg);
            wsc.send(_msg);
        }

        #endregion

        #region PARSE INCOMING MESSAGES

        /// <summary>
        /// Parses a command message.
        /// </summary>
        /// <param name="message">The message to parse.</param>
        /// <returns>The command message.</returns>
        public static string[] ParseCommand(string message)
        {
            // "HOLO1_Command"
            //ROSMessage msg = JsonConvert.DeserializeObject<ROSMessage>(message);
            ROSMessageString _msg = JsonConvert.DeserializeObject<ROSMessageString>(message);
            string[] result = _msg.topic.Split('_');
            if (result.Length != 2) return null;
            string caller = result[0];
            string type = result[1];

            if (type == "Command")
            {
                return new string[] { caller, _msg.msg.data };
            }
            return null;
        }

        /// <summary>
        /// Parses a status message.
        /// </summary>
        /// <param name="message">The message to parse.</param>
        /// <returns>The status message.</returns>
        public static string[] ParseStatus(string message)
        {
            // "WorkerPool/status", "Robot/status"
            ROSMessageString _msg = JsonConvert.DeserializeObject<ROSMessageString>(message);
            string[] result = _msg.topic.Split('/');
            if (result.Length != 2) return null;

            string caller = result[0];
            string type = result[1];
            if (type == "status")
            {
                return new string[] { caller, _msg.msg.data} ;
            }
            return null;
        }

        /// <summary>
        /// Parses a trajectory message.
        /// </summary>
        /// <param name="robot">The robot object.</param>
        /// <param name="path_name">The name of the path.</param>
        /// <param name="message">The message to parse.</param>
        /// <returns>The trajectory message.</returns>
        public static RobotTrajectoryObject ParseTrajectory(RobotObject robot, string path_name, string message)
        {
            ROSMessagePlannedTrajectory _msg = JsonConvert.DeserializeObject<ROSMessagePlannedTrajectory>(message);
            string[] result = _msg.topic.Split('/');
            if (result.Length != 3) return null;
            string caller = _msg.topic.Split('/')[0];
            string type = _msg.topic.Split('/')[2];
            PlannedTrajectoryMsg msg = _msg.msg;

            if ((caller == robot.name) && (type == "planned_path") && (msg.name == path_name))
            {

                // convert joint to degrees
                List<float[]> joint_trajectory = RobotTrajectoryObject.MsgToJointTrajectory(msg);

                // the frame conversion applies the offset, the version stored in traj object is unmodified
                List<Plane> gTrajectoryFrames = RobotLibraryConverter.jointTrajectoryToGhFrames(joint_trajectory, robot);
                Curve path = new PolylineCurve(gTrajectoryFrames.Select(f => f.Origin).ToArray());
                Mesh gMesh = Mesh.CreateFromCurvePipe(path, 0.01, 6, 1, MeshPipeCapStyle.Dome, true);
                return new RobotTrajectoryObject
                {
                    robot = robot,
                    gMesh = gMesh,
                    gTrajectoryFrames = gTrajectoryFrames,
                    joint_trajectory = joint_trajectory
                };

            }
            return null;
        }

        /// <summary>
        /// Parses a tracked data message.
        /// </summary>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="message">The message to parse.</param>
        /// <returns>The tracked data message.</returns>
        public static string ParseTrackedData(string deviceName, string message)
        {
            // "ForceSensor/data"
            ROSMessageString _msg = JsonConvert.DeserializeObject<ROSMessageString>(message);
            string[] result = _msg.topic.Split('/');
            if (result.Length != 2) return null; 

            string caller = _msg.topic.Split('/')[0];
            string type = _msg.topic.Split('/')[1];
            if ((deviceName == caller) && (type == "data"))
            {
                return _msg.msg.data;
            }
            return null;
        }

        /// <summary>
        /// Parses a device transform message.
        /// </summary>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="message">The message to parse.</param>
        /// <returns>The device transform message.</returns>
        public static BuiltInMsg.Pose ParseDeviceTransform(string deviceName, string message)
        {
            ROSMessage msg = JsonConvert.DeserializeObject<ROSMessagePose>(message);
            string caller = msg.topic.Split('_')[0];
            string type = msg.topic.Split('_')[1];

            if (caller != deviceName) return null;
            
            if (type == "Transform")
            {
                return JsonConvert.DeserializeObject<ROSMessagePose>(message).msg;
            }
            return null;
        }

        /// <summary>
        /// Parses a model message.
        /// </summary>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="message">The message to parse.</param>
        /// <returns>The model message.</returns>
        public static ModelMsg ParseModelMessage(string deviceName, string message) 
        {
            ROSMessageModel _msg = JsonConvert.DeserializeObject<ROSMessageModel>(message);
            string caller = _msg.topic.Split('_')[0];
            string type = _msg.topic.Split('_')[1];
            //if (caller != "/" + deviceName) return null; // listen to all model changes from all devices
            if (type == "Model")
            {
                return _msg.msg;
            }
            return null;
        }

        /// <summary>
        /// Parses a gaze point message.
        /// </summary>
        /// <param name="deviceName">The name of the device.</param>
        /// <param name="message">The message to parse.</param>
        /// <returns>The gaze point message.</returns>
        public static string ParseGazePointMessage(string deviceName, string message)
        {
            ROSMessageString _msg = JsonConvert.DeserializeObject<ROSMessageString>(message);
            string caller = _msg.topic.Split('_')[0];
            string type = _msg.topic.Split('_')[1];
            if ((caller != deviceName) && (caller != "/" + deviceName)) return null;
            if (type == "GazePoint")
            {
                return _msg.msg.data;
            }
            return null;
        }

        #endregion
    }
}