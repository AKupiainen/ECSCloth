using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public partial struct ClothMeshSystem : ISystem
{
    private bool isInitialized;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClothMeshTag>();
        isInitialized = false;
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            InitializeMesh(ref state);
            isInitialized = true;
        }
        else
        {
            UpdateMesh(ref state);
        }
    }
    
    private void InitializeMesh(ref SystemState state)
    {
        EntityQuery clothQuery = SystemAPI.QueryBuilder().WithAll<ClothAuthoring>().Build();
        EntityQuery meshQuery = SystemAPI.QueryBuilder().WithAll<ClothMeshTag>().Build();
        EntityQuery pointQuery = SystemAPI.QueryBuilder().WithAll<PointMass>().Build();
        
        if (pointQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        NativeArray<Entity> pointEntities = pointQuery.ToEntityArray(Allocator.Temp);
        NativeArray<PointMass> pointMasses = pointQuery.ToComponentDataArray<PointMass>(Allocator.Temp);
        
        try
        {
            DetermineGridDimensions(pointMasses, out int width, out int height);
            
            if (width <= 0 || height <= 0)
            {
                Debug.LogError("Failed to determine valid grid dimensions");
                return;
            }
            
            Entity meshEntity;
            DynamicBuffer<VertexPosition> vertexBuffer;
            DynamicBuffer<VertexNormal> normalBuffer;
    
            if (!meshQuery.IsEmptyIgnoreFilter)
            {
                meshEntity = meshQuery.GetSingletonEntity();
        
                state.EntityManager.SetComponentData(meshEntity, new ClothMeshTag
                {
                    Width = width,
                    Height = height
                });
        
                vertexBuffer = state.EntityManager.GetBuffer<VertexPosition>(meshEntity);
                normalBuffer = state.EntityManager.GetBuffer<VertexNormal>(meshEntity);
            }
            else
            {
                meshEntity = state.EntityManager.CreateEntity();
        
                state.EntityManager.AddComponentData(meshEntity, new ClothMeshTag
                {
                    Width = width,
                    Height = height
                });
        
                state.EntityManager.AddComponentData(meshEntity, new MeshData
                {
                    Owner = clothQuery.IsEmptyIgnoreFilter ? Entity.Null : clothQuery.GetSingletonEntity(),
                    VertexCount = width * height,
                    IsInitialized = true
                });
        
                vertexBuffer = state.EntityManager.AddBuffer<VertexPosition>(meshEntity);
                normalBuffer = state.EntityManager.AddBuffer<VertexNormal>(meshEntity);
        
                vertexBuffer.ResizeUninitialized(width * height);
                normalBuffer.ResizeUninitialized(width * height);
            }
    
            for (int i = 0; i < pointMasses.Length && i < vertexBuffer.Length; i++)
            {
                vertexBuffer[i] = new VertexPosition { Value = pointMasses[i].Position };
                normalBuffer[i] = new VertexNormal { Value = new float3(0, 0, 1) };
            }
        }
        finally
        {
            pointEntities.Dispose();
            pointMasses.Dispose();
        }
    }
    
    private void UpdateMesh(ref SystemState state)
    {
        EntityQuery meshQuery = SystemAPI.QueryBuilder().WithAll<ClothMeshTag>().Build();

        if (meshQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Entity meshEntity = meshQuery.GetSingletonEntity();
        
        ClothMeshTag meshTag = state.EntityManager.GetComponentData<ClothMeshTag>(meshEntity);
        DynamicBuffer<VertexPosition> vertexBuffer = state.EntityManager.GetBuffer<VertexPosition>(meshEntity);
        DynamicBuffer<VertexNormal> normalBuffer = state.EntityManager.GetBuffer<VertexNormal>(meshEntity);
        
        int width = meshTag.Width;
        int height = meshTag.Height;
        
        if (width <= 0 || height <= 0 || vertexBuffer.Length == 0)
        {
            return;
        }

        EntityQuery pointQuery = SystemAPI.QueryBuilder().WithAll<PointMass>().Build();
        
        if (pointQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        NativeArray<Entity> pointEntities = pointQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<PointMass> pointMasses = pointQuery.ToComponentDataArray<PointMass>(Allocator.TempJob);
        
        try
        {
            if (pointMasses.Length != vertexBuffer.Length)
            {
                
                if (pointMasses.Length > 0)
                {
                    isInitialized = false;
                    return;
                }
            }
            
            for (int i = 0; i < pointMasses.Length && i < vertexBuffer.Length; i++)
            {
                vertexBuffer[i] = new VertexPosition { Value = pointMasses[i].Position };
            }
            
            for (int i = 0; i < normalBuffer.Length; i++)
            {
                normalBuffer[i] = new VertexNormal { Value = float3.zero };
            }
            
            CalculateNormals(vertexBuffer, normalBuffer, width, height);
        }
        finally
        {
            pointEntities.Dispose();
            pointMasses.Dispose();
        }
    }
    
    private void CalculateNormals(DynamicBuffer<VertexPosition> vertexBuffer, 
                                 DynamicBuffer<VertexNormal> normalBuffer,
                                 int width, int height)
    {
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int bottomLeft = y * width + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = (y + 1) * width + x;
                int topRight = topLeft + 1;
                
                if (bottomLeft >= vertexBuffer.Length || bottomRight >= vertexBuffer.Length || 
                    topLeft >= vertexBuffer.Length || topRight >= vertexBuffer.Length)
                {
                    continue;
                }

                float3 blPos = vertexBuffer[bottomLeft].Value;
                float3 brPos = vertexBuffer[bottomRight].Value;
                float3 tlPos = vertexBuffer[topLeft].Value;
                float3 trPos = vertexBuffer[topRight].Value;
                
                float3 side1 = tlPos - blPos;
                float3 side2 = trPos - blPos;
                float3 normal1 = math.normalize(math.cross(side1, side2));
                
                float3 side3 = trPos - blPos;
                float3 side4 = brPos - blPos;
                float3 normal2 = math.normalize(math.cross(side3, side4));
                
                AddNormal(normalBuffer, bottomLeft, normal1);
                AddNormal(normalBuffer, bottomLeft, normal2);
                AddNormal(normalBuffer, topLeft, normal1);
                AddNormal(normalBuffer, topRight, normal1);
                AddNormal(normalBuffer, topRight, normal2);
                AddNormal(normalBuffer, bottomRight, normal2);
            }
        }
        
        for (int i = 0; i < normalBuffer.Length; i++)
        {
            float3 normal = normalBuffer[i].Value;
            
            if (!math.all(normal == float3.zero))
            {
                normalBuffer[i] = new VertexNormal { Value = math.normalize(normal) };
            }
            else
            {
                normalBuffer[i] = new VertexNormal { Value = new float3(0, 0, 1) };
            }
        }
    }
    
    private void AddNormal(DynamicBuffer<VertexNormal> buffer, int index, float3 normal)
    {
        if (index < 0 || index >= buffer.Length)
        {
            return;
        }

        float3 currentNormal = buffer[index].Value;
        buffer[index] = new VertexNormal { Value = currentNormal + normal };
    }
    
    private void DetermineGridDimensions(NativeArray<PointMass> points, out int width, out int height)
    {
        float maxX = float.MinValue;
        float minX = float.MaxValue;
        float maxY = float.MinValue;
        float minY = float.MaxValue;
        float minDistanceX = float.MaxValue;
        float minDistanceY = float.MaxValue;
        
        for (int i = 0; i < points.Length; i++)
        {
            float x = points[i].Position.x;
            float y = points[i].Position.y;
            
            maxX = math.max(maxX, x);
            minX = math.min(minX, x);
            maxY = math.max(maxY, y);
            minY = math.min(minY, y);
        }
        
        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                float distX = math.abs(points[i].Position.x - points[j].Position.x);
                float distY = math.abs(points[i].Position.y - points[j].Position.y);
                
                if (distX > 0.001f)
                {
                    minDistanceX = math.min(minDistanceX, distX);
                }

                if (distY > 0.001f)
                {
                    minDistanceY = math.min(minDistanceY, distY);
                }
            }
        }
        
        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        
        width = minDistanceX < float.MaxValue ? Mathf.RoundToInt(rangeX / minDistanceX) + 1 : 1;
        height = minDistanceY < float.MaxValue ? Mathf.RoundToInt(rangeY / minDistanceY) + 1 : 1;
        
        if (width * height != points.Length)
        {
            int pointCount = points.Length;
            
            for (int w = Mathf.FloorToInt(Mathf.Sqrt(pointCount)); w >= 1; w--)
            {
                if (pointCount % w == 0)
                {
                    width = w;
                    height = pointCount / w;
                    break;
                }
            }
        }
    }
}