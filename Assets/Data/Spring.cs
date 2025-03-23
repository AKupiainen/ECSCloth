using Unity.Entities;

public struct Spring : IComponentData
{
    public Entity PointA;
    public Entity PointB;
    public float RestLength;
    public float Stiffness;
}