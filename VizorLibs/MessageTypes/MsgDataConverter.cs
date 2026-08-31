using System.Linq;
using Rhino.Geometry;

namespace VizorLibs.MessageTypes
{
    public static class MsgDataConverter
    {
        private static float RhinoToROSMultiplier()
        {
            var doc = Rhino.RhinoDoc.ActiveDoc;
            float multiplier = 0.001f;
            if (doc != null)
            {
                if (doc.ModelUnitSystem == Rhino.UnitSystem.Millimeters)
                {
                    multiplier = 0.001f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Centimeters)
                {
                    multiplier = 0.01f;
                }
                else if (doc.ModelUnitSystem == Rhino.UnitSystem.Meters)
                {
                    multiplier = 1.0f;
                }
            }
            return multiplier;
        }

        /// <summary>
        /// Inverse of RhinoToROSMultiplier — scales incoming ROS-unit (meters) values
        /// up to the active Rhino document's units.
        /// </summary>
        public static float ROSToRhinoMultiplier()
        {
            return 1f / RhinoToROSMultiplier();
        }

        public static BuiltInMsg.MeshMsg ghMeshToMsg(Mesh mesh)
        {
            if (mesh == null) return new BuiltInMsg.MeshMsg();
            float multiplier = RhinoToROSMultiplier();
            mesh.Faces.ConvertQuadsToTriangles();
            return new BuiltInMsg.MeshMsg
            (mesh.Vertices.Select(v => new BuiltInMsg.Point(v.X * multiplier, v.Y * multiplier, v.Z * multiplier)).ToArray(),
              mesh.Faces.Select(f => new BuiltInMsg.MeshTriangle(f.A, f.B, f.C)).ToArray()
            );
        }

        public static BuiltInMsg.Point[] ghEdgesToMsg(Point3d[] points)
        {
            float multiplier = RhinoToROSMultiplier();
            BuiltInMsg.Point[] pointMsgs = new BuiltInMsg.Point[points.Length];
            for (int i=0; i<points.Length; i++)
            {
                pointMsgs[i] = new BuiltInMsg.Point((float)points[i].X * multiplier, (float)points[i].Y * multiplier, (float)points[i].Z * multiplier); 
            }
            return pointMsgs;
        }

        /// <summary>
        /// https://github.com/siemens/ros-sharp/wiki/Dev_ROSUnityCoordinateSystemConversion
        /// </summary>
        /// <param name="plane"></param>
        /// <returns></returns>
        public static BuiltInMsg.Transform PlaneToTransform(Plane plane)
        {
            float num = RhinoToROSMultiplier();
            // xx, z, xx
            BuiltInMsg.Vector3 point = new BuiltInMsg.Vector3((float)plane.Origin.X * num, (float)plane.Origin.Z * num, (float)plane.Origin.Y * num);
            Quaternion quat = Quaternion.Rotation(Plane.WorldXY, plane);
            BuiltInMsg.Quaternion quaternion = new BuiltInMsg.Quaternion((float)quat.B, (float)quat.C, (float)quat.D, (float)quat.A);
            return new BuiltInMsg.Transform(point, quaternion);
        }
    }
}