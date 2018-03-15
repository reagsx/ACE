using ACE.Server.Physics.Common;

namespace ACE.Server.Physics.Animation
{
    public class MovementStruct
    {
        public MovementType Type;
        public uint Motion;
        public int ObjectId;
        public int TopLevelId;
        public Position Position;
        public float Radius;
        public float Height;
        public MovementParameters Params;

        public MovementStruct() { }

        public MovementStruct(MovementType type)
        {
            Type = type;
        }

        public MovementStruct(MovementType type, uint motion, MovementParameters movementParams)
        {
            Type = type;
            Motion = motion;
            Params = movementParams;
        }
    }
}
