using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACE.Server.Physics.BSP;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Collision;
using ACE.Server.Physics.Extensions;

namespace ACE.Server.Physics.Common
{
    public class EnvCell: ObjCell
    {
        public int NumSurfaces;
        //public List<Surface> Surfaces;
        public CellStruct CellStructure;
        public uint CellStructureID;
        //public Environment Env;
        public int NumPortals;
        public List<DatLoader.Entity.CellPortal> Portals;
        public int NumStaticObjects;
        public List<uint> StaticObjectIDs;
        public List<AFrame> StaticObjectFrames;
        public List<PhysicsObj> StaticObjects;
        public List<ushort> LightArray;
        public int InCellTimestamp;
        public List<ushort> VisibleCellIDs;
        public Dictionary<uint, EnvCell> VisibleCells;
        public uint Bitfield;
        public uint EnvironmentID;
        public DatLoader.FileTypes.Environment Environment;

        public EnvCell() : base()
        {
            Init();
        }

        public EnvCell(DatLoader.FileTypes.EnvCell envCell): base()
        {
            Bitfield = envCell.Bitfield;
            ID = envCell.Id;
            ShadowObjectIDs = envCell.Shadows;
            Pos = new Position(ID, new AFrame(envCell.Position));
            Portals = envCell.CellPortals;
            NumPortals = Portals.Count;
            StaticObjectIDs = new List<uint>();
            StaticObjectFrames = new List<AFrame>();
            foreach (var staticObj in envCell.StaticObjects)
            {
                StaticObjectIDs.Add(staticObj.Id);
                StaticObjectFrames.Add(new AFrame(staticObj.Frame));
            }
            NumStabs = StaticObjectIDs.Count;
            VisibleCellIDs = envCell.VisibleCells;
            RestrictionObj = envCell.RestrictionObj;
            SeenOutside = envCell.SeenOutside;

            EnvironmentID = envCell.EnvironmentId;
            Environment = (DatLoader.FileTypes.Environment)DBObj.Get(new QualifiedDataID(16, EnvironmentID));
            CellStructureID = envCell.CellStructure;
            if (Environment.Cells != null && Environment.Cells.ContainsKey(CellStructureID))
                CellStructure = new CellStruct(Environment.Cells[CellStructureID]);
        }

        public override TransitionState FindEnvCollisions(Transition transition)
        {
            var transitState = check_entry_restrictions(transition);

            if (transitState != TransitionState.OK)
                return transitState;

            transition.SpherePath.ObstructionEthereal = false;

            if (CellStructure.PhysicsBSP != null)
            {
                transition.SpherePath.CacheLocalSpaceSphere(Pos, 1.0f);

                if (transition.SpherePath.InsertType == InsertType.InitialPlacement)
                    transitState = CellStructure.PhysicsBSP.placement_insert(transition);
                else
                    transitState = CellStructure.PhysicsBSP.find_collisions(transition, 1.0f);

                if (transitState != TransitionState.OK && !transition.ObjectInfo.State.HasFlag(ObjectInfoState.Contact))
                    transition.CollisionInfo.CollidedWithEnvironment = true;
            }
            return transitState;
        }

        public override TransitionState FindCollisions(Transition transition)
        {
            var transitionState = FindEnvCollisions(transition);
            if (transitionState == TransitionState.OK)
                transitionState = FindObjCollisions(transition);
            return transitionState;
        }

        public void build_visible_cells()
        {
            VisibleCells = new Dictionary<uint, EnvCell>();

            foreach (var visibleCellID in VisibleCellIDs)
            {
                var blockCellID = ID & 0xFFFF0000 | visibleCellID;
                if (VisibleCells.ContainsKey(blockCellID)) continue;
                var cell = (EnvCell)LScape.get_landcell(blockCellID);
                VisibleCells.Add(visibleCellID, cell);
            }
        }

        public void check_building_transit(ushort portalId, Position pos, int numSphere, List<Sphere> spheres, CellArray cellArray, SpherePath path)
        {
            if (portalId == 0) return;
            foreach (var sphere in spheres)
            {
                var globSphere = new Sphere(Pos.Frame.GlobalToLocal(sphere.Center), sphere.Radius);
                if (CellStructure.sphere_intersects_cell(globSphere) == BoundingType.Outside)
                    continue;

                if (path != null)
                    path.HitsInteriorCell = true;

                cellArray.add_cell(ID, this);
            }
        }

        public void check_building_transit(int portalId, int numParts, List<PhysicsPart> parts, CellArray cellArray)
        {
            if (portalId == 0) return;

            var portal = Portals[portalId];
            var portalPoly = CellStructure.Portals[portalId];

            foreach (var part in parts)
            {
                if (part == null) continue;

                Sphere boundingSphere = null;
                if (part.GfxObj[0].PhysicsSphere != null)
                    boundingSphere = part.GfxObj[0].PhysicsSphere;
                else
                    boundingSphere = part.GfxObj[0].DrawingSphere;

                if (boundingSphere == null) continue;

                var center = Pos.LocalToLocal(part.Pos, boundingSphere.Center);
                var rad = boundingSphere.Radius + PhysicsGlobals.EPSILON;

                var diff = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                if (portal.PortalSide)
                {
                    if (diff > rad) continue;
                }
                else
                {
                    if (diff < -rad) continue;
                }

                var box = new BBox();
                box.LocalToLocal(part.GetBoundingBox(), part.Pos, Pos);
                var intersect = portalPoly.Plane.intersect_box(box);
                if (intersect == Sidedness.Crossing || intersect == Sidedness.Positive && portal.PortalSide || intersect == Sidedness.Negative && !portal.PortalSide)
                {
                    if (!CellStructure.box_intersects_cell(box))
                        continue;

                    cellArray.add_cell(ID, this);
                    find_transit_cells(numParts, parts, cellArray);
                    return;
                }
            }
        }

        public ObjCell find_visible_child_cell(Vector3 origin, bool searchCells)
        {
            if (point_in_cell(origin))
                return this;

            if (searchCells)
            {
                foreach (var visibleCell in VisibleCells.Values)
                {
                    var envCell = GetVisible(visibleCell.ID);
                    if (envCell != null && envCell.point_in_cell(origin))
                        return envCell;
                }
            }
            else
            {
                foreach (var portal in Portals)
                {
                    var envCell = GetVisible(portal.OtherCellId);
                    if (envCell != null && envCell.point_in_cell(origin))
                        return envCell;
                }
            }
            return null;
        }

        public new EnvCell GetVisible(uint cellID)
        {
            EnvCell envCell = null;
            VisibleCells.TryGetValue(cellID, out envCell);
            return envCell;
        }

        public new void Init()
        {
            CellStructure = new CellStruct();
            StaticObjectIDs = new List<uint>();
            StaticObjectFrames = new List<AFrame>();
            StaticObjects = new List<PhysicsObj>();
            VisibleCells = new Dictionary<uint, EnvCell>();
        }

        public EnvCell add_visible_cell(uint cellID)
        {
            var envCell = (EnvCell)DBObj.Get(new QualifiedDataID(3, cellID));
            VisibleCells.Add(cellID, envCell);
            return envCell;
        }

        public override void find_transit_cells(int numParts, List<PhysicsPart> parts, CellArray cellArray)
        {
            var checkOutside = false;
            float rad;

            var i = 0;
            foreach (var portal in Portals)
            {
                var portalPoly = CellStructure.Portals[i];

                foreach (var part in parts)
                {
                    if (part == null) continue;
                    var sphere = part.GfxObj[0].PhysicsSphere;
                    if (sphere == null)
                        sphere = part.GfxObj[0].DrawingSphere;
                    if (sphere == null)
                        continue;

                    var center = Pos.LocalToGlobal(part.Pos, sphere.Center);
                    rad = sphere.Radius + PhysicsGlobals.EPSILON;

                    var dist = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                    if (portal.PortalSide)
                    {
                        if (dist < -rad)
                            continue;
                    }
                    else
                    {
                        if (dist > rad)
                            continue;
                    }

                    var bbox = part.GetBoundingBox();
                    BBox box = null;
                    box.LocalToLocal(bbox, part.Pos, Pos);
                    var sidedness = portalPoly.Plane.intersect_box(box);
                    if (sidedness == Sidedness.Positive && !portal.PortalSide || sidedness == Sidedness.Negative && portal.PortalSide)
                        continue;

                    if (portal.OtherCellId == ushort.MaxValue)
                    {
                        checkOutside = true;
                        break;
                    }

                    // LoadCells
                    var otherCell = GetVisible(portal.OtherCellId);
                    if (otherCell == null)
                    {
                        cellArray.add_cell(portal.OtherCellId, null);
                        break;
                    }

                    BBox cellBox = null;
                    cellBox.LocalToLocal(bbox, Pos, otherCell.Pos);
                    if (otherCell.CellStructure.box_intersects_cell(cellBox))
                    {
                        cellArray.add_cell(otherCell.ID, otherCell);
                        break;
                    }
                }
            }
            if (checkOutside)
                LandCell.add_all_outside_cells(numParts, parts, cellArray);
        }

        public override void find_transit_cells(Position position, int numSphere, List<Sphere> spheres, CellArray cellArray, SpherePath path)
        {
            var checkOutside = false;

            var i = 0;
            foreach (var portal in Portals)
            {
                var portalPoly = CellStructure.Portals[i];

                if (portal.OtherCellId == ushort.MaxValue)
                {
                    foreach (var sphere in spheres)
                    {
                        var rad = sphere.Radius + PhysicsGlobals.EPSILON;
                        var center = Pos.Frame.GlobalToLocal(sphere.Center);

                        var dist = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                        if (dist > -rad && dist < -rad)
                        {
                            checkOutside = true;
                            break;
                        }
                    }
                }
                else
                {
                    var otherCell = GetVisible(portal.OtherCellId);
                    if (otherCell != null)
                    {
                        foreach (var sphere in spheres)
                        {
                            var center = otherCell.Pos.Frame.GlobalToLocal(sphere.Center);
                            var _sphere = new Sphere(center, sphere.Radius);

                            var boundingType = otherCell.CellStructure.sphere_intersects_cell(_sphere);
                            if (boundingType != BoundingType.Outside)
                            {
                                cellArray.add_cell(otherCell.ID, otherCell);
                                break;
                            }
                        }
                    }
                    else
                    {
                        foreach (var sphere in spheres)
                        {
                            var center = Pos.Frame.GlobalToLocal(sphere.Center);
                            var _sphere = new Sphere(center, sphere.Radius + PhysicsGlobals.EPSILON);
                            var portalSide = portal.PortalSide;
                            var dist = Vector3.Dot(_sphere.Center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                            if (dist > -_sphere.Radius && portalSide || dist < _sphere.Radius && !portalSide)
                            {
                                cellArray.add_cell(portal.OtherCellId, null);
                                break;
                            }
                        }
                    }
                }
            }
            if (checkOutside)
                LandCell.add_all_outside_cells(position, numSphere, spheres, cellArray);
        }

        public static ObjCell get_visible(uint cellID)
        {
            var cell = (EnvCell)LScape.get_landcell(cellID);
            return cell.VisibleCells.Values.First();
        }

        public void grab_visible(List<uint> stabs)
        {
            foreach (var stab in stabs)
                add_visible_cell(stab);
        }

        public override bool point_in_cell(Vector3 point)
        {
            var localPoint = Pos.Frame.GlobalToLocal(point);
            return CellStructure.point_in_cell(localPoint);
        }

        public void release_visible(List<uint> stabs)
        {
            foreach (var stab in stabs)
                VisibleCells.Remove(stab);
        }
    }
}
