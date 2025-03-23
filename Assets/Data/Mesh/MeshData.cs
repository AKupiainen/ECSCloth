using Unity.Entities;

public struct MeshData : IComponentData
{
    public Entity Owner;     
    public int VertexCount; 
    public bool IsInitialized;
}