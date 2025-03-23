using Unity.Entities;
using Unity.Mathematics;

public struct PointMass : IComponentData
{
    public float3 Position;
    public float3 PreviousPosition;
    public bool IsAnchored; 
    public float Mass; 
}