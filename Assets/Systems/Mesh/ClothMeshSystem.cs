using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateAfter(typeof(ClothSimulationSystem))]
public partial struct ClothMeshSystem : ISystem
{
    private bool _isInitialized;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClothMeshTag>();
        _isInitialized = false;
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!_isInitialized)
        {
            InitializeMesh(ref state);
            _isInitialized = true;
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
                Debug.LogError($"Failed to determine valid grid dimensions. Points: {pointMasses.Length}");
                return;
            }
            
            Entity meshEntity;
            DynamicBuffer<VertexPosition> vertexBuffer;
            DynamicBuffer<VertexNormal> normalBuffer;

            if (!meshQuery.IsEmptyIgnoreFilter)
            {
                NativeArray<Entity> meshEntities = meshQuery.ToEntityArray(Allocator.Temp);
                
                try
                {
                    if (meshEntities.Length > 1)
                    {
                        Debug.LogWarning($"Found {meshEntities.Length} cloth mesh entities. Using the first one and ignoring others.");
                        
                        for (int i = 1; i < meshEntities.Length; i++)
                        {
                            state.EntityManager.DestroyEntity(meshEntities[i]);
                        }
                    }
                    
                    meshEntity = meshEntities[0];
                }
                finally
                {
                    meshEntities.Dispose();
                }
        
                state.EntityManager.SetComponentData(meshEntity, new ClothMeshTag
                {
                    Width = width,
                    Height = height
                });
        
                vertexBuffer = state.EntityManager.GetBuffer<VertexPosition>(meshEntity);
                normalBuffer = state.EntityManager.GetBuffer<VertexNormal>(meshEntity);
                
                if (vertexBuffer.Length != width * height || normalBuffer.Length != width * height)
                {
                    vertexBuffer.ResizeUninitialized(width * height);
                    normalBuffer.ResizeUninitialized(width * height);
                }
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

            SortPointMassesByPosition(pointMasses, width, height);
            SmoothEdges(pointMasses, width, height);
            
            for (int i = 0; i < pointMasses.Length && i < vertexBuffer.Length; i++)
            {
                vertexBuffer[i] = new VertexPosition { Value = pointMasses[i].Position };
                normalBuffer[i] = new VertexNormal { Value = new float3(0, 0, 1) };
            }
            
            CalculateNormals(vertexBuffer, normalBuffer, width, height);
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
            Debug.LogWarning($"Invalid mesh dimensions: {width}x{height}, Vertices: {vertexBuffer.Length}");
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
                Debug.LogWarning($"Point count mismatch: {pointMasses.Length} points vs {vertexBuffer.Length} vertices");
                
                if (pointMasses.Length > 0)
                {
                    _isInitialized = false;
                    return;
                }
            }
            
            SortPointMassesByPosition(pointMasses, width, height);
            SmoothEdges(pointMasses, width, height);
            
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
                
                float3 edge1 = tlPos - blPos;
                float3 edge2 = trPos - blPos;
                float3 normal1 = math.normalize(math.cross(edge1, edge2));
                
                float3 edge3 = brPos - blPos;
                float3 edge4 = trPos - blPos;
                float3 normal2 = math.normalize(math.cross(edge4, edge3));
                
                float area1 = 0.5f * math.length(math.cross(edge1, edge2));
                float area2 = 0.5f * math.length(math.cross(edge4, edge3));
                
                AddWeightedNormal(normalBuffer, bottomLeft, normal1, area1);
                AddWeightedNormal(normalBuffer, bottomLeft, normal2, area2);
                AddWeightedNormal(normalBuffer, topLeft, normal1, area1);
                AddWeightedNormal(normalBuffer, topRight, normal1, area1);
                AddWeightedNormal(normalBuffer, topRight, normal2, area2);
                AddWeightedNormal(normalBuffer, bottomRight, normal2, area2);
            }
        }
        
        AddEdgeNormals(vertexBuffer, normalBuffer, width, height);
        
        for (int i = 0; i < normalBuffer.Length; i++)
        {
            float3 normal = normalBuffer[i].Value;
            
            if (math.lengthsq(normal) > 1e-5f)
            {
                normalBuffer[i] = new VertexNormal { Value = math.normalize(normal) };
            }
            else
            {
                normalBuffer[i] = new VertexNormal { Value = new float3(0, 0, 1) };
            }
        }
    }
    
    private void AddWeightedNormal(DynamicBuffer<VertexNormal> buffer, int index, float3 normal, float weight)
    {
        if (index < 0 || index >= buffer.Length)
        {
            return;
        }

        if (!math.any(math.isnan(normal)) && math.lengthsq(normal) > 1e-5f)
        {
            float3 currentNormal = buffer[index].Value;
            buffer[index] = new VertexNormal { Value = currentNormal + normal * weight };
        }
    }
    
    private void AddNormal(DynamicBuffer<VertexNormal> buffer, int index, float3 normal)
    {
        if (index < 0 || index >= buffer.Length)
        {
            return;
        }

        if (!math.any(math.isnan(normal)) && math.lengthsq(normal) > 1e-5f)
        {
            float3 currentNormal = buffer[index].Value;
            buffer[index] = new VertexNormal { Value = currentNormal + normal };
        }
    }
    
    private void AddEdgeNormals(DynamicBuffer<VertexPosition> vertexBuffer, 
                              DynamicBuffer<VertexNormal> normalBuffer,
                              int width, int height)
    {
        for (int y = 1; y < height - 1; y++)
        {
            int leftIdx = y * width;
            int rightIdx = y * width + (width - 1);
            
            if (math.lengthsq(normalBuffer[leftIdx].Value) < 1e-5f)
            {
                float3 innerPos = vertexBuffer[leftIdx + 1].Value;
                float3 abovePos = vertexBuffer[(y + 1) * width].Value;
                float3 belowPos = vertexBuffer[(y - 1) * width].Value;
                float3 edgePos = vertexBuffer[leftIdx].Value;
                
                float3 edge1 = abovePos - edgePos;
                float3 edge2 = innerPos - edgePos;
                float3 normal1 = math.normalize(math.cross(edge1, edge2));
                
                float3 edge3 = innerPos - edgePos;
                float3 edge4 = belowPos - edgePos;
                float3 normal2 = math.normalize(math.cross(edge3, edge4));
                
                AddNormal(normalBuffer, leftIdx, normal1);
                AddNormal(normalBuffer, leftIdx, normal2);
            }
            
            if (math.lengthsq(normalBuffer[rightIdx].Value) < 1e-5f)
            {
                float3 innerPos = vertexBuffer[rightIdx - 1].Value;
                float3 abovePos = vertexBuffer[(y + 1) * width + (width - 1)].Value;
                float3 belowPos = vertexBuffer[(y - 1) * width + (width - 1)].Value;
                float3 edgePos = vertexBuffer[rightIdx].Value;
                
                float3 edge1 = innerPos - edgePos;
                float3 edge2 = abovePos - edgePos;
                float3 normal1 = math.normalize(math.cross(edge1, edge2));
                
                float3 edge3 = belowPos - edgePos;
                float3 edge4 = innerPos - edgePos;
                float3 normal2 = math.normalize(math.cross(edge3, edge4));
                
                AddNormal(normalBuffer, rightIdx, normal1);
                AddNormal(normalBuffer, rightIdx, normal2);
            }
        }
        
        for (int x = 1; x < width - 1; x++)
        {
            int bottomIdx = x;
            int topIdx = (height - 1) * width + x;
            
            if (math.lengthsq(normalBuffer[bottomIdx].Value) < 1e-5f)
            {
                float3 innerPos = vertexBuffer[bottomIdx + width].Value;
                float3 leftPos = vertexBuffer[bottomIdx - 1].Value;
                float3 rightPos = vertexBuffer[bottomIdx + 1].Value;
                float3 edgePos = vertexBuffer[bottomIdx].Value;
                
                float3 edge1 = leftPos - edgePos;
                float3 edge2 = innerPos - edgePos;
                float3 normal1 = math.normalize(math.cross(edge1, edge2));
                
                float3 edge3 = innerPos - edgePos;
                float3 edge4 = rightPos - edgePos;
                float3 normal2 = math.normalize(math.cross(edge3, edge4));
                
                AddNormal(normalBuffer, bottomIdx, normal1);
                AddNormal(normalBuffer, bottomIdx, normal2);
            }
            
            if (math.lengthsq(normalBuffer[topIdx].Value) < 1e-5f)
            {
                float3 innerPos = vertexBuffer[topIdx - width].Value;
                float3 leftPos = vertexBuffer[topIdx - 1].Value;
                float3 rightPos = vertexBuffer[topIdx + 1].Value;
                float3 edgePos = vertexBuffer[topIdx].Value;
                
                float3 edge1 = innerPos - edgePos;
                float3 edge2 = leftPos - edgePos;
                float3 normal1 = math.normalize(math.cross(edge1, edge2));
                
                float3 edge3 = rightPos - edgePos;
                float3 edge4 = innerPos - edgePos;
                float3 normal2 = math.normalize(math.cross(edge3, edge4));
                
                AddNormal(normalBuffer, topIdx, normal1);
                AddNormal(normalBuffer, topIdx, normal2);
            }
        }
        
        int bottomLeftIdx = 0;
        int bottomRightIdx = width - 1;
        int topLeftIdx = (height - 1) * width;
        int topRightIdx = height * width - 1;
        
        if (math.lengthsq(normalBuffer[bottomLeftIdx].Value) < 1e-5f)
        {
            float3 rightPos = vertexBuffer[bottomLeftIdx + 1].Value;
            float3 topPos = vertexBuffer[bottomLeftIdx + width].Value;
            float3 cornerPos = vertexBuffer[bottomLeftIdx].Value;
            
            float3 edge1 = topPos - cornerPos;
            float3 edge2 = rightPos - cornerPos;
            float3 normal = math.normalize(math.cross(edge1, edge2));
            
            AddNormal(normalBuffer, bottomLeftIdx, normal);
        }
        
        if (math.lengthsq(normalBuffer[bottomRightIdx].Value) < 1e-5f)
        {
            float3 leftPos = vertexBuffer[bottomRightIdx - 1].Value;
            float3 topPos = vertexBuffer[bottomRightIdx + width].Value;
            float3 cornerPos = vertexBuffer[bottomRightIdx].Value;
            
            float3 edge1 = leftPos - cornerPos;
            float3 edge2 = topPos - cornerPos;
            float3 normal = math.normalize(math.cross(edge1, edge2));
            
            AddNormal(normalBuffer, bottomRightIdx, normal);
        }
        
        if (math.lengthsq(normalBuffer[topLeftIdx].Value) < 1e-5f)
        {
            float3 rightPos = vertexBuffer[topLeftIdx + 1].Value;
            float3 bottomPos = vertexBuffer[topLeftIdx - width].Value;
            float3 cornerPos = vertexBuffer[topLeftIdx].Value;
            
            float3 edge1 = bottomPos - cornerPos;
            float3 edge2 = rightPos - cornerPos;
            float3 normal = math.normalize(math.cross(edge1, edge2));
            
            AddNormal(normalBuffer, topLeftIdx, normal);
        }
        
        if (math.lengthsq(normalBuffer[topRightIdx].Value) < 1e-5f)
        {
            float3 leftPos = vertexBuffer[topRightIdx - 1].Value;
            float3 bottomPos = vertexBuffer[topRightIdx - width].Value;
            float3 cornerPos = vertexBuffer[topRightIdx].Value;
            
            float3 edge1 = leftPos - cornerPos;
            float3 edge2 = bottomPos - cornerPos;
            float3 normal = math.normalize(math.cross(edge1, edge2));
            
            AddNormal(normalBuffer, topRightIdx, normal);
        }
    }
    
    private void SortPointMassesByPosition(NativeArray<PointMass> points, int width, int height)
    {
        if (points.Length < width * height)
        {
            Debug.LogWarning($"Not enough points to fill grid: {points.Length} points for {width}x{height} grid");
            return;
        }
        
        NativeArray<PointMass> sortedPoints = new(points.Length, Allocator.Temp);
        
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        
        for (int i = 0; i < points.Length; i++)
        {
            minX = math.min(minX, points[i].Position.x);
            minY = math.min(minY, points[i].Position.y);
            maxX = math.max(maxX, points[i].Position.x);
            maxY = math.max(maxY, points[i].Position.y);
        }
        
        float cellWidth = (maxX - minX) / (width - 1);
        float cellHeight = (maxY - minY) / (height - 1);
        
        NativeArray<int> gridToPointIndex = new(width * height, Allocator.Temp);
        for (int i = 0; i < gridToPointIndex.Length; i++)
        {
            gridToPointIndex[i] = -1;
        }

        for (int i = 0; i < points.Length; i++)
        {
            float normalizedX = (points[i].Position.x - minX) / (maxX - minX);
            float normalizedY = (points[i].Position.y - minY) / (maxY - minY);
            
            int gridX = math.clamp(Mathf.RoundToInt(normalizedX * (width - 1)), 0, width - 1);
            int gridY = math.clamp(Mathf.RoundToInt(normalizedY * (height - 1)), 0, height - 1);
            int gridIndex = gridY * width + gridX;
            
            if (gridIndex >= 0 && gridIndex < gridToPointIndex.Length)
            {
                if (gridToPointIndex[gridIndex] != -1)
                {
                    int existingIndex = gridToPointIndex[gridIndex];
                    float3 gridCenter = new(
                        minX + gridX * cellWidth,
                        minY + gridY * cellHeight,
                        0
                    );
                    
                    float existingDist = math.lengthsq(points[existingIndex].Position - gridCenter);
                    float newDist = math.lengthsq(points[i].Position - gridCenter);
                    
                    if (newDist < existingDist)
                    {
                        gridToPointIndex[gridIndex] = i;
                    }
                }
                else
                {
                    gridToPointIndex[gridIndex] = i;
                }
            }
        }
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int gridIndex = y * width + x;
                
                if (gridToPointIndex[gridIndex] == -1)
                {
                    int nearestPointIndex = -1;
                    float nearestDist = float.MaxValue;
                    
                    for (int ny = 0; ny < height; ny++)
                    {
                        for (int nx = 0; nx < width; nx++)
                        {
                            int neighborIndex = ny * width + nx;
                            
                            if (gridToPointIndex[neighborIndex] != -1)
                            {
                                float dist = (nx - x) * (nx - x) + (ny - y) * (ny - y);
                                
                                if (dist < nearestDist)
                                {
                                    nearestDist = dist;
                                    nearestPointIndex = gridToPointIndex[neighborIndex];
                                }
                            }
                        }
                    }
                    
                    if (nearestPointIndex != -1)
                    {
                        gridToPointIndex[gridIndex] = nearestPointIndex;
                    }
                }
            }
        }
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int gridIndex = y * width + x;
                int pointIndex = gridToPointIndex[gridIndex];
                bool isEdge = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                
                if (pointIndex >= 0 && pointIndex < points.Length)
                {
                    if (isEdge)
                    {
                        float3 idealPosition = new(
                            minX + x * cellWidth,
                            minY + y * cellHeight,
                            points[pointIndex].Position.z
                        );
                        
                        float edgeBlendFactor = 0.2f; 
                        float3 blendedPosition = math.lerp(
                            points[pointIndex].Position,
                            idealPosition,
                            edgeBlendFactor
                        );
                        
                        PointMass tempPoint = points[pointIndex];
                        tempPoint.Position = blendedPosition;
                        sortedPoints[gridIndex] = tempPoint;
                    }
                    else
                    {
                        sortedPoints[gridIndex] = points[pointIndex];
                    }
                }
                else
                {
                    float3 estimatedPosition = new(
                        minX + x * cellWidth,
                        minY + y * cellHeight,
                        0
                    );
                    
                    sortedPoints[gridIndex] = new PointMass { Position = estimatedPosition };
                }
            }
        }
        
        for (int i = 0; i < points.Length && i < sortedPoints.Length; i++)
        {
            points[i] = sortedPoints[i];
        }
        
        sortedPoints.Dispose();
        gridToPointIndex.Dispose();
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
        
        int estimatedWidth = minDistanceX < float.MaxValue ? Mathf.RoundToInt(rangeX / minDistanceX) + 1 : 1;
        int estimatedHeight = minDistanceY < float.MaxValue ? Mathf.RoundToInt(rangeY / minDistanceY) + 1 : 1;
        
        if (estimatedWidth * estimatedHeight == points.Length)
        {
            width = estimatedWidth;
            height = estimatedHeight;
            return;
        }
        
        int pointCount = points.Length;
        width = 1;
        height = pointCount;
        
        for (int w = Mathf.FloorToInt(Mathf.Sqrt(pointCount)); w >= 1; w--)
        {
            if (pointCount % w == 0)
            {
                width = w;
                height = pointCount / w;
                break;
            }
        }
        
        if (width * height != pointCount)
        {
            int squareDim = Mathf.CeilToInt(Mathf.Sqrt(pointCount));
            width = squareDim;
            height = squareDim;
        }
    }
    
    [BurstCompile]
    private void SmoothEdges(NativeArray<PointMass> points, int width, int height)
    {
        if (width <= 2 || height <= 2 || points.Length < width * height)
        {
            Debug.LogWarning($"Cannot smooth edges: dimensions too small or insufficient points. Width: {width}, Height: {height}, Points: {points.Length}");
            return;
        }

        NativeArray<float3> smoothedPositions = new(points.Length, Allocator.Temp);
        
        for (int i = 0; i < points.Length; i++)
        {
            smoothedPositions[i] = points[i].Position;
        }
        
        float edgeSmoothFactor = 0.5f;  
        int smoothingPasses = 2;      
        
        NativeList<int> neighbors = new(8, Allocator.Temp);
        
        for (int pass = 0; pass < smoothingPasses; pass++)
        {

            for (int x = 0; x < width; x++)
            {
                int idx = x;  // y = 0
                SmoothEdgeVertex(points, smoothedPositions, neighbors, idx, x, 0, width, height, edgeSmoothFactor);
            }
            
            for (int x = 0; x < width; x++)
            {
                int idx = (height - 1) * width + x; 
                SmoothEdgeVertex(points, smoothedPositions, neighbors, idx, x, height - 1, width, height, edgeSmoothFactor);
            }
            
            
            for (int y = 1; y < height - 1; y++)
            {
                int idx = y * width;  // x = 0
                SmoothEdgeVertex(points, smoothedPositions, neighbors, idx, 0, y, width, height, edgeSmoothFactor);
            }
            
            for (int y = 1; y < height - 1; y++)
            {
                int idx = y * width + (width - 1); 
                SmoothEdgeVertex(points, smoothedPositions, neighbors, idx, width - 1, y, width, height, edgeSmoothFactor);
            }
            
          
            if (pass < smoothingPasses - 1)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    PointMass temp = points[i];
                    temp.Position = smoothedPositions[i];
                    points[i] = temp;
                }
            }
        }
        
        for (int i = 0; i < points.Length; i++)
        {
            PointMass temp = points[i];
            temp.Position = smoothedPositions[i];
            points[i] = temp;
        }
        
        smoothedPositions.Dispose();
        neighbors.Dispose();
    }
    
    [BurstCompile]
    private void SmoothEdgeVertex(NativeArray<PointMass> points, NativeArray<float3> smoothedPositions, 
                                 NativeList<int> neighbors, int vertexIndex, int x, int y, int width, int height, float smoothFactor)
    {
        if (vertexIndex < 0 || vertexIndex >= points.Length)
        {
            return;
        }
        
        float3 originalPosition = points[vertexIndex].Position;
        float3 smoothedPosition = originalPosition;
        float totalWeight = 0;
        
        GetValidNeighborIndices(neighbors, x, y, width, height);
        
        if (neighbors.Length > 0)
        {
            float3 weightedSum = float3.zero;
            
            for (int i = 0; i < neighbors.Length; i++)
            {
                int neighborIdx = neighbors[i];
                if (neighborIdx >= 0 && neighborIdx < points.Length)
                {
                    float3 neighborPos = points[neighborIdx].Position;
                    float dist = math.distance(originalPosition, neighborPos);
                    
                    float weight = 1.0f / math.max(0.5f, dist);
                    
                    int nx = neighborIdx % width;
                    int ny = neighborIdx / width;
                    bool isEdgeNeighbor = nx == 0 || nx == width - 1 || ny == 0 || ny == height - 1;
                    
                    if (!isEdgeNeighbor) {
                        weight *= 0.5f;
                    }
                    
                    weightedSum += neighborPos * weight;
                    totalWeight += weight;
                }
            }
            
            if (totalWeight > 0)
            {
                float3 averagePos = weightedSum / totalWeight;
                
                averagePos.z = originalPosition.z;
                float actualSmoothFactor;
                
                if ((x == 0 && y == 0) || (x == 0 && y == height - 1) || 
                    (x == width - 1 && y == 0) || (x == width - 1 && y == height - 1))
                {
                    actualSmoothFactor = smoothFactor * 0.3f;
                }
                else if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    actualSmoothFactor = smoothFactor * 0.5f;
                }
                else
                {
                    actualSmoothFactor = smoothFactor;
                }
                
                smoothedPosition = math.lerp(originalPosition, averagePos, actualSmoothFactor);
            }
        }
        
        smoothedPositions[vertexIndex] = smoothedPosition;
    }

    private void GetValidNeighborIndices(NativeList<int> neighbors, int x, int y, int width, int height)
    {
        neighbors.Clear();
        int neighborCount = 8;
        
        NativeList<int> innerNeighbors = new(4, Allocator.Temp);
        NativeList<int> edgeNeighbors = new(4, Allocator.Temp);
        
        try
        {
            for (int i = 0; i < neighborCount; i++)
            {
                int nx = x;
                int ny = y;
                
                switch (i)
                {
                    case 0: nx = x - 1; ny = y - 1; break; // Top-left
                    case 1: nx = x    ; ny = y - 1; break; // Top
                    case 2: nx = x + 1; ny = y - 1; break; // Top-right
                    case 3: nx = x - 1; ny = y    ; break; // Left
                    case 4: nx = x + 1; ny = y    ; break; // Right
                    case 5: nx = x - 1; ny = y + 1; break; // Bottom-left
                    case 6: nx = x    ; ny = y + 1; break; // Bottom
                    case 7: nx = x + 1; ny = y + 1; break; // Bottom-right
                }
                
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    bool isInnerNeighbor = !(nx == 0 || nx == width - 1 || ny == 0 || ny == height - 1);
                    
                    if (isInnerNeighbor)
                    {
                        innerNeighbors.Add(ny * width + nx);
                    }
                    else
                    {
                        edgeNeighbors.Add(ny * width + nx);
                    }
                }
            }
            
            for (int i = 0; i < innerNeighbors.Length; i++)
            {
                neighbors.Add(innerNeighbors[i]);
            }
            
            for (int i = 0; i < edgeNeighbors.Length; i++)
            {
                neighbors.Add(edgeNeighbors[i]);
            }
        }
        finally
        {
            innerNeighbors.Dispose();
            edgeNeighbors.Dispose();
        }
    }
}