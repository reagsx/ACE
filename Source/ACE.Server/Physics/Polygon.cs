using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Common;
using ACE.Server.Physics.Extensions;

namespace ACE.Server.Physics
{
    public enum CullMode
    {
        Clockwise = 0x0,
        None = 0x1,
        Unknown = 0x2
    };

    public enum Sidedness
    {
        Positive = 0x0,
        Negative = 0x1,
        InPlane  = 0x2,
        Crossing = 0x3
    };

    public enum SimplePolygonType
    {
        SimplePolygon   = 0x0,
        PathPolygon     = 0x1,
        PlanarPolygon   = 0x2
    };

    public enum StipplingType
    {
        None = 0x0,
        Positive = 0x1,
        Negative = 0x2,
        Both = 0x3,
        NoPos = 0x4,
        NoNeg = 0x8,
        NoUVS = 0x14
    };

    public class Polygon
    {
        public List<Vertex> Vertices;
        public List<short> VertexIDs;
        public List<Vector2> Screen;
        public int PolyID;
        public int NumPoints;
        public byte Stippling;
        public CullMode SidesType;
        public List<byte> PosUVIndices;
        public List<byte> NegUVIndices;
        public short PosSurface;
        public short NegSurface;
        public Plane Plane;

        public Polygon()
        {
            Init();
        }

        public Polygon(DatLoader.Entity.Polygon polygon, DatLoader.Entity.CVertexArray vertexArray)
        {
            NegSurface = polygon.NegSurface;
            NegUVIndices = polygon.NegUVIndices;
            NumPoints = polygon.NumPts;
            PosSurface = polygon.PosSurface;
            PosUVIndices = polygon.PosUVIndices;
            SidesType = (CullMode)polygon.SidesType;
            Stippling = polygon.Stippling;
            VertexIDs = polygon.VertexIds;
            Vertices = new List<Vertex>();
            foreach (var vertexIdx in VertexIDs)
                Vertices.Add(new Vertex(vertexArray.Vertices[(ushort)vertexIdx]));
            make_plane();
        }

        public Polygon(int idx, int numPoints, CullMode cullMode)
        {
            Init();
            PolyID = idx;
            NumPoints = numPoints;
            SidesType = cullMode;

            VertexIDs = new List<short>();
            Vertices = new List<Vertex>(numPoints);
            for (var i = 0; i < numPoints; i++)
            {
                Vertices.Add(null);
                VertexIDs.Add(-1);
            }
        }

        public void Init()
        {
            PolyID = -1;
            PosSurface = -1;
            NegSurface = -1;
        }

        public bool adjust_sphere_to_plane(SpherePath path, Sphere validPos, Vector3 movement)
        {
            var dpPos = Vector3.Dot(validPos.Center, Plane.Normal) + Plane.D;
            var dpMove = Vector3.Dot(movement, Plane.Normal);
            var dist = 0.0f;

            if (dpMove <= PhysicsGlobals.EPSILON)
            {
                if (dpMove >= -PhysicsGlobals.EPSILON)
                    return false;

                dist = dpPos - validPos.Radius;
            }
            else
                dist = -validPos.Radius - dpPos;

            var iDist = dist / dpMove;
            var interp = (1.0f - iDist) * path.WalkInterp;
            if (interp >= path.WalkInterp || interp < -0.5f)
                return false;

            validPos.Center -= movement * iDist;
            path.WalkInterp = interp;
            return true;
        }

        public double adjust_sphere_to_poly(Sphere checkPos, Vector3 currPos, Vector3 movement)
        {
            var dpPos = Vector3.Dot(currPos, Plane.Normal) + Plane.D;
            if (Math.Abs(dpPos) < checkPos.Radius)
                return 1.0f;

            var dpMove = Vector3.Dot(movement, Plane.Normal);    // dist?
            if (Math.Abs(dpMove) < PhysicsGlobals.EPSILON)
                return 0.0f;

            var dist = checkPos.Radius;
            if (movement.LengthSquared() <= dist * dist)
                dist *= -1.0f;

            return (dist - dpPos) / dpMove;
        }

        public void adjust_to_placement_poly(Sphere hitSphere, Sphere otherSphere, float radius, bool centerSolid, bool solidCheck)
        {
            var dp = Vector3.Dot(hitSphere.Center, Plane.Normal) + Plane.D;
            if (solidCheck && (centerSolid || dp <= 0.0f))
                radius *= -1.0f;
            var diff = radius - dp;
            var adjusted = Plane.Normal * diff;
            hitSphere.Center += adjusted;
            otherSphere.Center += adjusted;
        }

        public bool check_small_walkable(Sphere sphere, Vector3 up)
        {
            return check_walkable(sphere, up, true);
        }

        public bool check_walkable(Sphere sphere, Vector3 up, bool small = false)
        {
            var angleUp = Vector3.Dot(Plane.Normal, up);    // dist?
            if (angleUp < PhysicsGlobals.EPSILON) return false;

            var result = true;

            var center = sphere.Center - up * ((Vector3.Dot(Plane.Normal, sphere.Center) + Plane.D) / angleUp);
            var radsum = sphere.Radius * sphere.Radius;
            if (small) radsum *= 0.25f;

            var prevIdx = NumPoints - 1;
            for (var i = 0; i < Vertices.Count; i++)
            {
                var vertex = Vertices[i];
                var lastVertex = Vertices[prevIdx];
                prevIdx = i;

                var edge = vertex - lastVertex;
                var disp = center - lastVertex.Origin;
                var cross = Vector3.Cross(Plane.Normal, edge);
                var diff = Vector3.Dot(disp, cross);

                if (diff < 0.0f)
                {
                    if (cross.LengthSquared() * radsum < diff * diff)
                        return false;

                    var dispEdge = Vector3.Dot(disp, edge);
                    if (dispEdge >= 0.0f && dispEdge <= edge.LengthSquared())
                        return true;

                    return false;
                }

                if (disp.LengthSquared() <= radsum)
                    return true;
            }

            return result;
        }

        public bool find_crossed_edge(Sphere sphere, Vector3 up, ref Vector3 normal)
        {
            var angleUp = Vector3.Dot(Plane.Normal, up);
            if (Math.Abs(angleUp) < PhysicsGlobals.EPSILON)
                return false;

            var angle = (Vector3.Dot(Plane.Normal, sphere.Center) + Plane.D) / angleUp;
            var center = sphere.Center - up * angle;

            var prevIdx = NumPoints - 1;
            for (var i = 0; i < Vertices.Count; i++)
            {
                var vertex = Vertices[i];
                var lastVertex = Vertices[prevIdx];
                prevIdx = i;

                var edge = vertex - lastVertex;
                var disp = center - lastVertex.Origin;
                var cross = Vector3.Cross(Plane.Normal, edge);

                if (Vector3.Dot(disp, cross) < 0.0f)
                {
                    normal = cross * (1.0f / normal.Length());
                    return true;
                }
            }
            return false;
        }

        public bool hits_sphere(Sphere sphere)
        {
            Vector3 contactPoint = Vector3.Zero;
            return polygon_hits_sphere_precise(sphere, ref contactPoint);
        }

        public void make_plane()
        {
            var normal = Vector3.Zero;

            // calculate plane normal
            for (int i = NumPoints - 2, spreadIdx = 1; i > 0; i--)
            {
                var v1 = Vertices[spreadIdx++] - Vertices[0];
                var v2 = Vertices[spreadIdx] - Vertices[0];

                normal += Vector3.Cross(v1, v2);
            }
            normal = normal.Normalize();

            // calculate distance
            var distSum = 0.0f;
            for (int i = NumPoints, spread = 0; i > 0; i--, spread++)
                distSum += Vector3.Dot(normal, Vertices[spread].Origin);

            var dist = -(distSum / NumPoints);

            Plane = new Plane(normal, dist);
        }

        public bool point_in_poly2D(Vector3 point, Sidedness side)
        {
            var prevIdx = 0;
            for (var i = NumPoints - 1; i >= 0; i--)
            {
                var prevVertex = Vertices[prevIdx];
                var vertex = Vertices[i];

                var diff = vertex - prevVertex;

                // 2d cross product difference?
                var diffCross = (diff.Y * vertex.Origin.X) - (diff.X * vertex.Origin.Y) + (diff.X * point.Y) - (diff.Y * point.X);

                if (side != Sidedness.Positive)
                {
                    if (diffCross < 0.0f)
                        return false;
                }
                else
                {
                    if (diffCross > 0.0f)
                        return false;
                }
                prevIdx = i;
            }
            return true;
        }

        public bool point_in_polygon(Vector3 point)
        {
            var lastVertex = Vertices[NumPoints - 1];

            foreach (var vertex in Vertices)
            {
                var cross = Vector3.Cross(Plane.Normal, vertex - lastVertex);
                var dp = Vector3.Dot(cross, point - lastVertex.Origin);

                if (dp < 0.0f) return false;

                lastVertex = vertex;
            }
            return true;
        }

        public bool polygon_hits_ray(Ray ray, ref float time)
        {
            if (SidesType != CullMode.Clockwise && Vector3.Dot(Plane.Normal, ray.Dir) > 0.0f)   // dist?
                return false;

            if (!Plane.compute_time_of_intersection(ray, ref time))
                return false;

            return point_in_polygon(ray.Point + ray.Dir * time);
        }

        public bool polygon_hits_sphere(Sphere sphere, ref Vector3 contactPoint)
        {
            var dpPos = Vector3.Dot(sphere.Center, Plane.Normal) + Plane.D;
            var rad = sphere.Radius - PhysicsGlobals.EPSILON;

            if (Math.Abs(dpPos) > rad) return false;

            var diff = rad * rad - dpPos * dpPos;
            var result = true;

            contactPoint = sphere.Center - Plane.Normal * dpPos;

            var prevIdx = NumPoints - 1;
            for (var i = 0; i < Vertices.Count; i++)
            {
                var vertex = Vertices[i];
                var lastVertex = Vertices[prevIdx];
                prevIdx = i;

                var edge = vertex - lastVertex;
                var disp = contactPoint - lastVertex.Origin;
                var cross = Vector3.Cross(Plane.Normal, edge);
                var dp = Vector3.Dot(disp, cross);
                if (dp < 0.0f)
                {
                    if (cross.LengthSquared() * diff < dp * dp)
                        return false;

                    var dispEdge = Vector3.Dot(disp, edge);
                    if (dispEdge >= 0.0f && dispEdge <= edge.LengthSquared())
                        return true;

                    result = false;
                }

                if (disp.LengthSquared() <= diff)
                    return true;
            }

            return result;
        }

        public bool polygon_hits_sphere_precise(Sphere sphere, ref Vector3 contactPoint)
        {
            if (NumPoints == 0) return true;

            var dpPos = Vector3.Dot(Plane.Normal, sphere.Center) + Plane.D;
            var rad = sphere.Radius - PhysicsGlobals.EPSILON;
            if (Math.Abs(dpPos) > rad) return false;

            var diff = rad * rad - dpPos * dpPos;
            contactPoint = sphere.Center - Plane.Normal * dpPos;

            var prevIdx = NumPoints - 1;
            for (var i = 0; i < Vertices.Count; i++)
            {
                var vertex = Vertices[i];
                var lastVertex = Vertices[prevIdx];
                prevIdx = i;

                var edge = vertex - lastVertex;
                var disp = contactPoint - lastVertex.Origin;
                var cross = Vector3.Cross(Plane.Normal, edge);

                if (Vector3.Dot(disp, cross) >= 0.0f) continue;

                // inner loop
                prevIdx = NumPoints - 1;    // alt idx?
                for (var j = 0; j < Vertices.Count; j++)
                {
                    vertex = Vertices[j];
                    lastVertex = Vertices[prevIdx];
                    prevIdx = j;

                    edge = vertex - lastVertex;
                    disp = contactPoint - lastVertex.Origin;
                    cross = Vector3.Cross(Plane.Normal, edge);
                    var dispDot = Vector3.Dot(disp, cross);

                    if (dispDot < 0.0f)
                    {
                        if (cross.LengthSquared() * diff < dispDot * dispDot)
                            return false;

                        var dispEdge = Vector3.Dot(disp, edge);
                        if (dispEdge >= 0.0f && dispEdge <= edge.LengthSquared())
                            return true;
                    }

                    if (disp.LengthSquared() <= diff)
                        return true;
                }
                return false;
            }
            return true;
        }

        public bool pos_hits_sphere(Sphere sphere, Vector3 movement, ref Vector3 contactPoint, ref Polygon hitPoly)
        {
            var hit = polygon_hits_sphere_precise(sphere, ref contactPoint);

            if (hit)
                hitPoly = this;

            var dist = Vector3.Dot(movement, Plane.Normal);
            if (dist >= 0.0f)
                return false;

            return hit;
        }

        public bool walkable_hits_sphere(SpherePath path, Sphere sphere, Vector3 up)
        {
            var dp = Vector3.Dot(up, Plane.Normal);
            if (dp <= path.WalkableAllowance) return false;
            Vector3 contactPoint = Vector3.Zero;
            var hit = polygon_hits_sphere_precise(sphere, ref contactPoint);
            if (hit != polygon_hits_sphere(sphere, ref contactPoint))
            {
                polygon_hits_sphere_precise(sphere, ref contactPoint);
                polygon_hits_sphere(sphere, ref contactPoint);
            }
            return hit;
        }
    }
}
