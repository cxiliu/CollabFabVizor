
namespace VizorLibs.MessageTypes
{
    public class ROSMessageString : ROSMessage // deprecated
    {
        new public BuiltInMsg.String msg { get; set; }
    }
    public class ROSMessageBool : ROSMessage
    {
        new public BuiltInMsg.Bool msg { get; set; }
    }
    public class ROSMessageInt32 : ROSMessage
    {
        new public BuiltInMsg.Int32 msg { get; set; }
    }
    public class ROSMessageFloat64 : ROSMessage
    {
        new public BuiltInMsg.Float64 msg { get; set; }
    }
    public class ROSMessagePose : ROSMessage // deprecated
    {
        new public BuiltInMsg.Pose msg { get; set; }
    }

    /// <summary>
    /// A collection of ROS common_msg objects - http://wiki.ros.org/common_msgs?distro=noetic
    /// </summary>
    public class BuiltInMsg
    {
        /// <summary>
        /// shape_msgs/Mesh.msg
        /// </summary>
        public class MeshMsg
        {
            public MeshTriangle[] triangles;
            public Point[] vertices;

            public MeshMsg()
            {
                this.triangles = new MeshTriangle[] { };
                this.vertices = new Point[] { };
            }

            public MeshMsg(Point[] vertices, MeshTriangle[] triangles)
            {
                this.triangles = triangles;
                this.vertices = vertices;
            }
        }

        /// <summary>
        /// shape_msgs/MeshTriangle.msg
        /// </summary>
        public class MeshTriangle
        {
            public int[] vertex_indices;

            public MeshTriangle(int a, int b, int c)
            {
                vertex_indices = new int[3] { a, b, c };
            }
        }

        /// <summary>
        /// std_msgs/String.msg
        /// </summary>
        public class String
        {
            public string data;

            public String (string data)
            {
                this.data = data;
            }
        }

        /// <summary>
        /// std_msgs/Bool.msg
        /// </summary>
        public class Bool
        {
            public bool data;

            public Bool(bool data)
            {
                this.data = data;
            }
        }

        /// <summary>
        /// std_msgs/Int32.msg
        /// </summary>
        public class Int32
        {
            public int data;

            public Int32(int data)
            {
                this.data = data;
            }
        }

        /// <summary>
        /// std_msgs/Float64.msg
        /// </summary>
        public class Float64
        {
            public double data;

            public Float64(double data)
            {
                this.data = data;
            }
        }

        /// <summary>
        /// std_msgs/Time
        /// </summary>
        public class Time
        {
            public uint secs;
            public uint nsecs;

            public Time(uint secs, uint nsecs)
            {
                this.secs = secs;
                this.nsecs = nsecs;
            }
        }

        /// <summary>
        /// std_msgs/Header
        /// </summary>
        public class Header
        {
            //public uint seq;
            //public Time stamp;
            public string frame_id; 

            public Header(string frame_id) //uint seq, Time stamp)
            {
                //this.seq = seq;
                //this.stamp = stamp;
                this.frame_id = frame_id;
            }
        }

        /// <summary>
        /// geometry_msgs/PoseStamped.msg
        /// </summary>
        public class PoseStamped
        {
            public Pose pose;
            public Header header;

            public PoseStamped(Pose pose, Header header)
            {
                this.pose = pose;
                this.header = header;
            }
        }

        /// <summary>
        /// geometry_msgs/Pose.msg
        /// </summary>
        public class Pose
        {
            public Point position;
            public Quaternion orientation;

            public Pose(Point position, Quaternion orientation)
            {
                this.position = position;
                this.orientation = orientation;
            }
        }

        /// <summary>
        /// geometry_msgs/Transform.msg
        /// </summary>
        public class Transform
        {
            public Vector3 translation;
            public Quaternion rotation;

            public Transform()
            {
                this.translation = new Vector3();
                this.rotation = new Quaternion();
            }

            public Transform(Vector3 translation, Quaternion rotation)
            {
                this.translation = translation;
                this.rotation = rotation;
            }
        }

        /// <summary>
        /// geometry_msgs/Vector3.msg
        /// </summary>
        public class Vector3
        {
            public float x;
            public float y;
            public float z;

            public Vector3()
            {
                this.x = 0;
                this.y = 0;
                this.z = 0;
            }

            public Vector3(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        /// <summary>
        /// geometry_msgs/Quaternion.msg
        /// </summary>
        public class Quaternion
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public Quaternion()
            {
                this.x = 0;
                this.y = 0;
                this.z = 0;
                this.w = 0;
            }

            public Quaternion(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }
        }

        /// <summary>
        /// geometry_msgs/Point.msg
        /// </summary>
        public class Point
        {
            public float x;
            public float y;
            public float z;

            public Point(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }




        /// <summary>
        /// trajectory_msgs/JointTrajectoryPoint.msg
        /// </summary>
        public class JointTrajectoryPoint
        {
            public float[] positions;
            //public float[] velocities;
            //public float[] accelerations;
            //public float[] effort;

            public JointTrajectoryPoint()
            {
                this.positions = new float[] { };
            }

            public JointTrajectoryPoint(float[] positions)
            {
                this.positions = positions;
                //this.velocities = new float[positions.Length];
                //this.accelerations = new float[positions.Length];
                //this.effort = new float[positions.Length];
            }
        }

        /// <summary>
        /// trajectory_msgs/JointTrajectory.msg
        /// </summary>
        public class JointTrajectoryMsg
        {
            public string[] joint_names;
            public JointTrajectoryPoint[] points;

            public JointTrajectoryMsg()
            {
                this.joint_names = new string[] {};
                this.points = new JointTrajectoryPoint[] { };
            }

            public JointTrajectoryMsg(string[] joint_names, JointTrajectoryPoint[] points)
            {
                this.joint_names = joint_names;
                this.points = points;
            }
        }
    }
}
