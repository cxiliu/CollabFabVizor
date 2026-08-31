// This file contains utility methods for rendering content objects in the Rhino viewport.

using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VizorLibs;

namespace Vizor._2_Content
{
    /// <summary>
    /// The ContentUtilities class provides static methods for rendering AR content objects 
    /// (meshes, wireframes, and texts) in the Rhino viewport.
    /// </summary>
    public static class ContentUtilities
    {
        #region Mesh Rendering

        /// <summary>
        /// Draws a shaded mesh in the viewport with the specified display arguments.
        /// </summary>
        /// <param name="args">The preview display arguments.</param>
        /// <param name="geom">The scene geometry object containing the mesh and material.</param>
        public static void DrawMeshShaded(Grasshopper.Kernel.IGH_PreviewArgs args, SceneGeometryObject geom)
        {
            if (geom == null || geom.gMesh == null)
                return;

            Color meshColor = ParseColorFromMaterial(geom.material, Color.Gray);

            // Create display material
            Rhino.Display.DisplayMaterial displayMaterial = new Rhino.Display.DisplayMaterial(meshColor);
            displayMaterial.Transparency = 1.0 - (meshColor.A / 255.0);

            // Draw the mesh
            args.Display.DrawMeshShaded(geom.gMesh, displayMaterial);
        }

        /// <summary>
        /// Draws mesh edges in the viewport with the specified display arguments.
        /// </summary>
        /// <param name="args">The preview display arguments.</param>
        /// <param name="geom">The scene geometry object containing the mesh and material.</param>
        public static void DrawMeshWires(Grasshopper.Kernel.IGH_PreviewArgs args, SceneGeometryObject geom)
        {
            if (geom == null || geom.gMesh == null)
                return;

            Color meshColor = ParseColorFromMaterial(geom.material, Color.Gray);

            // Draw mesh edges in darker color
            Color edgeColor = Color.FromArgb(
                Math.Max(0, meshColor.R - 50),
                Math.Max(0, meshColor.G - 50),
                Math.Max(0, meshColor.B - 50)
            );

            args.Display.DrawMeshWires(geom.gMesh, edgeColor);
        }

        #endregion

        #region Wireframe Rendering

        /// <summary>
        /// Draws a wireframe object in the viewport with the specified display arguments.
        /// </summary>
        /// <param name="args">The preview display arguments.</param>
        /// <param name="wireframe">The scene wireframe object containing points, material, and width.</param>
        public static void DrawWireframe(Grasshopper.Kernel.IGH_PreviewArgs args, SceneWireframeObject wireframe)
        {
            if (wireframe == null || wireframe.points == null || wireframe.points.Length < 2)
                return;

            Color wireColor = ParseColorFromMaterial(wireframe.material, Color.Magenta);

            // Get width and scale it for viewport display (mm to pixels)
            int displayWidth = (int) Math.Max(1, Math.Min(10, wireframe.width / 2));

            // Create polyline and draw it
            Polyline polyline = new Polyline(wireframe.points);
            args.Display.DrawPolyline(polyline, wireColor, displayWidth);

            // Draw points at vertices
            foreach (Point3d pt in wireframe.points)
            {
                args.Display.DrawPoint(pt, Rhino.Display.PointStyle.Circle, 1, wireColor);
            }
        }

        #endregion

        #region Wireframe Construction

        /// <summary>
        /// Upper bound on the number of points in a single wireframe. A wireframe is drawn by the AR
        /// clients as one line renderer, so an unbounded point count is a rendering hazard rather than
        /// a detail worth preserving.
        /// </summary>
        public const int MaxWirePoints = 10000;

        /// <summary>
        /// Tolerance used to strip degenerate brep edges before tracing.
        /// </summary>
        private const double MicroEdgeTolerance = 0.001;

        /// <summary>
        /// Traces the edges of one or more breps as a single continuous polyline.
        ///
        /// A SceneWireframeObject is drawn as one connected line (a Unity LineRenderer, a THREE.Line,
        /// or a Rhino Polyline), so simply concatenating edges would draw a visible connector between
        /// every pair of unconnected edges. Instead the edges are walked depth-first and each edge is
        /// re-emitted in reverse when the walk backtracks over it, so every connecting segment lies
        /// exactly on top of an edge that has already been drawn and no spurious lines are visible.
        /// The only unavoidable jump is between disconnected components, which is kept as short as
        /// possible by starting each component at the vertex nearest the end of the previous one.
        /// </summary>
        /// <param name="breps">the breps whose edges make up this wireframe</param>
        /// <param name="resolution">number of segments used to approximate a non-linear edge</param>
        /// <param name="tolerance">model tolerance, used to merge shared edge endpoints</param>
        /// <param name="edgeCount">number of edges that went into the trace</param>
        /// <returns>the points of the traced polyline, in draw order</returns>
        public static Point3d[] BrepEdgesToWirePoints(IEnumerable<Brep> breps, int resolution, double tolerance, out int edgeCount)
        {
            edgeCount = 0;
            if (breps == null)
                return new Point3d[0];

            double tol = tolerance > 0 ? tolerance : MicroEdgeTolerance;

            // sample every edge once, up front
            List<Point3d[]> edges = new List<Point3d[]>();
            foreach (Brep brep in breps)
            {
                if (brep == null)
                    continue;

                // work on a copy - RemoveNakedMicroEdges mutates the brep
                Brep copy = brep.DuplicateBrep();
                copy.Edges.RemoveNakedMicroEdges(MicroEdgeTolerance);

                foreach (Curve curve in copy.DuplicateEdgeCurves())
                {
                    Point3d[] sample = SampleEdge(curve, resolution, tol);
                    if (sample.Length >= 2)
                        edges.Add(sample);
                }
            }

            edgeCount = edges.Count;
            if (edgeCount == 0)
                return new Point3d[0];

            // build the vertex graph over the edge endpoints
            List<Point3d> vertices = new List<Point3d>();
            Dictionary<string, List<int>> cells = new Dictionary<string, List<int>>();
            List<List<int>> incident = new List<List<int>>();
            int[] edgeStarts = new int[edgeCount];
            int[] edgeEnds = new int[edgeCount];

            for (int e = 0; e < edgeCount; e++)
            {
                edgeStarts[e] = GetVertexId(edges[e][0], tol, cells, vertices, incident);
                edgeEnds[e] = GetVertexId(edges[e][edges[e].Length - 1], tol, cells, vertices, incident);
                incident[edgeStarts[e]].Add(e);
                if (edgeEnds[e] != edgeStarts[e])
                    incident[edgeEnds[e]].Add(e);
            }

            bool[] used = new bool[edgeCount];
            int[] cursor = new int[vertices.Count];
            List<Point3d> points = new List<Point3d>();

            // walk one connected component at a time
            int root = 0;
            while (root >= 0 && points.Count < MaxWirePoints)
            {
                WalkComponent(root, edges, edgeStarts, edgeEnds, incident, used, cursor, vertices, points);
                root = NextUnusedRoot(incident, used, vertices, points);
            }

            return points.ToArray();
        }

        /// <summary>
        /// Walks every unused edge reachable from a start vertex, emitting each edge forwards on the
        /// way out and backwards on the way back so the trace never leaves the geometry.
        /// </summary>
        private static void WalkComponent(int root, List<Point3d[]> edges, int[] edgeStarts, int[] edgeEnds,
            List<List<int>> incident, bool[] used, int[] cursor, List<Point3d> vertices, List<Point3d> points)
        {
            // seed the component - for the first one this is the very first point of the wireframe,
            // for later ones it closes the jump from wherever the previous component ended
            points.Add(vertices[root]);

            Stack<int> pathVertices = new Stack<int>();
            Stack<int> pathEdges = new Stack<int>();
            pathVertices.Push(root);
            pathEdges.Push(-1);

            while (pathVertices.Count > 0 && points.Count < MaxWirePoints)
            {
                int v = pathVertices.Peek();

                // advance this vertex's cursor to its next unused edge
                int next = -1;
                while (cursor[v] < incident[v].Count)
                {
                    int candidate = incident[v][cursor[v]];
                    cursor[v]++;
                    if (!used[candidate])
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next >= 0)
                {
                    used[next] = true;
                    bool forward = edgeStarts[next] == v;
                    int other = forward ? edgeEnds[next] : edgeStarts[next];

                    Append(points, edges[next], forward);

                    // a closed edge (a full circle, say) already returns to v, so there is nothing to
                    // walk into and no retrace to make
                    if (other != v)
                    {
                        pathVertices.Push(other);
                        pathEdges.Push(next);
                    }
                }
                else
                {
                    pathVertices.Pop();
                    int arrival = pathEdges.Pop();
                    if (arrival >= 0)
                    {
                        // retrace the edge we arrived on, back the way we came
                        bool cameForward = edgeEnds[arrival] == v;
                        Append(points, edges[arrival], !cameForward);
                    }
                }
            }
        }

        /// <summary>
        /// Finds the vertex with unused edges nearest the current end of the trace, so the jump
        /// between two disconnected components is as short as it can be.
        /// </summary>
        private static int NextUnusedRoot(List<List<int>> incident, bool[] used, List<Point3d> vertices, List<Point3d> points)
        {
            Point3d from = points.Count > 0 ? points[points.Count - 1] : Point3d.Origin;
            int best = -1;
            double bestDistance = double.MaxValue;

            for (int v = 0; v < vertices.Count; v++)
            {
                if (!incident[v].Any(e => !used[e]))
                    continue;

                double distance = vertices[v].DistanceTo(from);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = v;
                }
            }

            return best;
        }

        /// <summary>
        /// Appends an edge sample to the trace, skipping its first point because the trace is already
        /// standing on it.
        /// </summary>
        private static void Append(List<Point3d> points, Point3d[] sample, bool forward)
        {
            if (forward)
            {
                for (int i = 1; i < sample.Length; i++)
                    points.Add(sample[i]);
            }
            else
            {
                for (int i = sample.Length - 2; i >= 0; i--)
                    points.Add(sample[i]);
            }
        }

        /// <summary>
        /// Looks up the id of a shared edge endpoint, adding it to the graph if it is new. Endpoints
        /// within tolerance of each other are the same vertex.
        /// </summary>
        private static int GetVertexId(Point3d pt, double tolerance, Dictionary<string, List<int>> cells,
            List<Point3d> vertices, List<List<int>> incident)
        {
            long gx = (long)Math.Floor(pt.X / tolerance);
            long gy = (long)Math.Floor(pt.Y / tolerance);
            long gz = (long)Math.Floor(pt.Z / tolerance);

            // a point can land anywhere in its cell, so a match may sit in any neighbouring one
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    for (long dz = -1; dz <= 1; dz++)
                    {
                        List<int> ids;
                        if (!cells.TryGetValue(CellKey(gx + dx, gy + dy, gz + dz), out ids))
                            continue;

                        foreach (int id in ids)
                        {
                            if (vertices[id].DistanceTo(pt) <= tolerance)
                                return id;
                        }
                    }
                }
            }

            int added = vertices.Count;
            vertices.Add(pt);
            incident.Add(new List<int>());

            string ownKey = CellKey(gx, gy, gz);
            if (!cells.ContainsKey(ownKey))
                cells[ownKey] = new List<int>();
            cells[ownKey].Add(added);

            return added;
        }

        private static string CellKey(long x, long y, long z)
        {
            return x.ToString() + "_" + y.ToString() + "_" + z.ToString();
        }

        /// <summary>
        /// Samples a single edge into points. Polylines keep their exact corners, straight edges need
        /// only their two ends, and everything else is divided to the requested resolution.
        /// </summary>
        private static Point3d[] SampleEdge(Curve curve, int resolution, double tolerance)
        {
            if (curve == null)
                return new Point3d[0];

            Polyline polyline;
            if (curve.TryGetPolyline(out polyline) && polyline.Count >= 2)
                return polyline.ToArray();

            if (curve.IsLinear(tolerance))
                return new Point3d[] { curve.PointAtStart, curve.PointAtEnd };

            double[] parameters = curve.DivideByCount(Math.Max(2, resolution), true);
            if (parameters == null || parameters.Length < 2)
                return new Point3d[] { curve.PointAtStart, curve.PointAtEnd };

            List<Point3d> sample = new List<Point3d>();
            foreach (double t in parameters)
                sample.Add(curve.PointAt(t));

            // DivideByCount omits the closing point on a closed curve
            if (curve.IsClosed && sample[sample.Count - 1].DistanceTo(curve.PointAtStart) > tolerance)
                sample.Add(curve.PointAtStart);

            return sample.ToArray();
        }

        #endregion

        #region Text Rendering

        /// <summary>
        /// Draws a text object in the viewport with the specified display arguments.
        /// </summary>
        /// <param name="args">The preview display arguments.</param>
        /// <param name="textObj">The scene text object containing text content, plane, and material.</param>
        public static void DrawText(Grasshopper.Kernel.IGH_PreviewArgs args, SceneTextObject textObj)
        {
            if (textObj == null)
                return;

            Color textColor = ParseColorFromMaterial(textObj.material, Color.Black);

            // Draw plane origin and axes
            args.Display.DrawPoint(textObj.plane.Origin, Rhino.Display.PointStyle.X, 3, textColor);

            //double axisLength = 0.1;
            //args.Display.DrawArrow(new Line(textObj.plane.Origin, textObj.plane.Origin + textObj.plane.XAxis * axisLength), textColor);
            //args.Display.DrawArrow(new Line(textObj.plane.Origin, textObj.plane.Origin + textObj.plane.YAxis * axisLength), textColor);
            //args.Display.DrawArrow(new Line(textObj.plane.Origin, textObj.plane.Origin + textObj.plane.ZAxis * axisLength), textColor);
            Plane displayPlane = new Plane(textObj.plane);
            displayPlane.Rotate(Math.PI, displayPlane.XAxis);
            displayPlane.Rotate(Math.PI / 2, displayPlane.ZAxis);

            // Draw text
            args.Display.Draw3dText(textObj.text, textColor, displayPlane, 36, "Arial");
        }

        #endregion

        #region Bounding Box Utilities

        /// <summary>
        /// Gets the bounding box for a mesh geometry object.
        /// </summary>
        /// <param name="geom">The scene geometry object.</param>
        /// <returns>The bounding box of the mesh, or an empty box if the mesh is null.</returns>
        public static BoundingBox GetMeshBoundingBox(SceneGeometryObject geom)
        {
            if (geom != null && geom.gMesh != null)
            {
                return geom.gMesh.GetBoundingBox(false);
            }
            return BoundingBox.Empty;
        }

        /// <summary>
        /// Gets the bounding box for a wireframe object.
        /// </summary>
        /// <param name="wireframe">The scene wireframe object.</param>
        /// <returns>The bounding box containing all wireframe points, or an empty box if points are null.</returns>
        public static BoundingBox GetWireframeBoundingBox(SceneWireframeObject wireframe)
        {
            BoundingBox bbox = BoundingBox.Empty;

            if (wireframe != null && wireframe.points != null)
            {
                foreach (Point3d pt in wireframe.points)
                {
                    bbox.Union(pt);
                }
            }

            return bbox;
        }

        /// <summary>
        /// Gets the bounding box for a text object.
        /// </summary>
        /// <param name="textObj">The scene text object.</param>
        /// <returns>The bounding box containing the text plane origin, or an empty box if the object is null.</returns>
        public static BoundingBox GetTextBoundingBox(SceneTextObject textObj)
        {
            BoundingBox bbox = BoundingBox.Empty;

            if (textObj != null)
            {
                bbox.Union(textObj.plane.Origin);
            }

            return bbox;
        }

        #endregion

        #region Color Parsing

        /// <summary>
        /// Parses a color from a material string in the format "R,G,B,A".
        /// </summary>
        /// <param name="material">The material string containing RGBA values.</param>
        /// <param name="defaultColor">The default color to use if parsing fails.</param>
        /// <returns>The parsed color, or the default color if parsing fails.</returns>
        public static Color ParseColorFromMaterial(string material, Color defaultColor)
        {
            if (string.IsNullOrEmpty(material))
                return defaultColor;

            try
            {
                string[] colorParts = material.Split(',');
                if (colorParts.Length >= 3)
                {
                    int r = int.Parse(colorParts[0]);
                    int g = int.Parse(colorParts[1]);
                    int b = int.Parse(colorParts[2]);
                    int a = colorParts.Length >= 4 ? int.Parse(colorParts[3]) : 255;
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch
            {
                // Return default color on parse failure
            }

            return defaultColor;
        }

        #endregion
    }
}