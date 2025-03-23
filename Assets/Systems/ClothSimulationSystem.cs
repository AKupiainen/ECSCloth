using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct ClothSimulationSystem : ISystem
{
    // Store reference to entity data
    private EntityQuery _pointMassQuery;
    private EntityQuery _springQuery;
    private EntityQuery _transformQuery;
    
    private NativeArray<Spring> _springArray;
    private NativeArray<PointMassData> _pointMassArray;
    private NativeArray<Entity> _pointMassEntities;
    
    private struct PointMassData
    {
        public float3 Position;
        public float3 PreviousPosition;
        public bool IsAnchored;
        public float Mass;  // Added mass property
    }
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ClothSettings>();
        
        _pointMassQuery = SystemAPI.QueryBuilder().WithAll<PointMass>().Build();
        _springQuery = SystemAPI.QueryBuilder().WithAll<Spring>().Build();
        _transformQuery = SystemAPI.QueryBuilder().WithAll<PointMass, LocalTransform>().Build();
    }
    
    public void OnDestroy(ref SystemState state)
    {
        if (_springArray.IsCreated)
        {
            _springArray.Dispose();
        }

        if (_pointMassArray.IsCreated)
        {
            _pointMassArray.Dispose();
        }

        if (_pointMassEntities.IsCreated)
        {
            _pointMassEntities.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ClothSettings clothSettings = SystemAPI.GetSingleton<ClothSettings>();
        float deltaTime = clothSettings.TimeStep > 0 
            ? clothSettings.TimeStep 
            : SystemAPI.Time.DeltaTime;
        
        float deltaTimeSq = deltaTime * deltaTime;
        float3 gravity = new(0, -clothSettings.Gravity, 0);
        
        int pointMassCount = _pointMassQuery.CalculateEntityCount();
        int springCount = _springQuery.CalculateEntityCount();
        
        if (!_pointMassArray.IsCreated || _pointMassArray.Length != pointMassCount)
        {
            if (_pointMassArray.IsCreated)
            {
                _pointMassArray.Dispose();
            }

            _pointMassArray = new NativeArray<PointMassData>(pointMassCount, Allocator.Persistent);
            
            if (_pointMassEntities.IsCreated)
            {
                _pointMassEntities.Dispose();
            }

            _pointMassEntities = new NativeArray<Entity>(pointMassCount, Allocator.Persistent);
        }
        
        if (!_springArray.IsCreated || _springArray.Length != springCount)
        {
            if (_springArray.IsCreated)
            {
                _springArray.Dispose();
            }

            _springArray = new NativeArray<Spring>(springCount, Allocator.Persistent);
        }
        
        NativeArray<Entity> pointMassEntitiesFromQuery = _pointMassQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<PointMass> pointMassComponentsFromQuery = _pointMassQuery.ToComponentDataArray<PointMass>(Allocator.TempJob);
        
        for (int i = 0; i < pointMassCount; i++)
        {
            _pointMassEntities[i] = pointMassEntitiesFromQuery[i];
            _pointMassArray[i] = new PointMassData
            {
                Position = pointMassComponentsFromQuery[i].Position,
                PreviousPosition = pointMassComponentsFromQuery[i].PreviousPosition,
                IsAnchored = pointMassComponentsFromQuery[i].IsAnchored,
                Mass = pointMassComponentsFromQuery[i].Mass 
            };
        }
        
        NativeArray<Spring> springsFromQuery = _springQuery.ToComponentDataArray<Spring>(Allocator.TempJob);
        
        for (int i = 0; i < springCount; i++)
        {
            _springArray[i] = springsFromQuery[i];
        }
        
        ApplyForcesJob applyForcesJob = new()
        {
            PointMassArray = _pointMassArray,
            Gravity = gravity,
            DeltaTimeSq = deltaTimeSq
        };
        
        state.Dependency = applyForcesJob.Schedule(_pointMassArray.Length, 64, state.Dependency);
        state.Dependency.Complete();
        
        NativeHashMap<Entity, int> entityToIndexMap = new(pointMassCount, Allocator.TempJob);
        
        for (int i = 0; i < pointMassCount; i++)
        {
            entityToIndexMap.Add(_pointMassEntities[i], i);
        }
        
        for (int i = 0; i < clothSettings.ConstraintIterations; i++)
        {
            ProcessConstraintsJob constraintsJob = new()
            {
                PointMassArray = _pointMassArray,
                SpringArray = _springArray,
                EntityToIndexMap = entityToIndexMap,
                IterationFactor = 1.0f / clothSettings.ConstraintIterations
            };
            
            state.Dependency = constraintsJob.Schedule(_springArray.Length, 64, state.Dependency);
            state.Dependency.Complete();
        }
        
        for (int i = 0; i < pointMassCount; i++)
        {
            Entity entity = _pointMassEntities[i];
            PointMass pointMass = state.EntityManager.GetComponentData<PointMass>(entity);
            pointMass.Position = _pointMassArray[i].Position;
            pointMass.PreviousPosition = _pointMassArray[i].PreviousPosition;

            state.EntityManager.SetComponentData(entity, pointMass);
        }
        
        state.Dependency = new UpdateTransformJob().ScheduleParallel(_transformQuery, state.Dependency);
        
        pointMassEntitiesFromQuery.Dispose();
        pointMassComponentsFromQuery.Dispose();
        springsFromQuery.Dispose();
        entityToIndexMap.Dispose();
    }
    
    [BurstCompile]
    private struct ApplyForcesJob : IJobParallelFor
    {
        public NativeArray<PointMassData> PointMassArray;
        public float3 Gravity;
        public float DeltaTimeSq;
        
        public void Execute(int index)
        {
            PointMassData pointMass = PointMassArray[index];
            
            if (pointMass.IsAnchored)
            {
                return;
            }

            float3 acceleration = Gravity; // Gravity is the same regardless of mass
            
            // For other forces, we would divide by mass: force / mass = acceleration
            // Example: if we had wind or other forces
            // acceleration += externalForce / pointMass.Mass;
            
            float3 currentPos = pointMass.Position;
            float3 previousPos = pointMass.PreviousPosition;
            float3 newPosition = currentPos + (currentPos - previousPos) + acceleration * DeltaTimeSq;
            
            pointMass.PreviousPosition = currentPos;
            pointMass.Position = newPosition;
            PointMassArray[index] = pointMass;
        }
    }
    
    [BurstCompile]
    private struct ProcessConstraintsJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] 
        public NativeArray<PointMassData> PointMassArray;
        [ReadOnly] public NativeArray<Spring> SpringArray;
        [ReadOnly] public NativeHashMap<Entity, int> EntityToIndexMap;
        public float IterationFactor;
        
        public void Execute(int index)
        {
            Spring spring = SpringArray[index];
            
            if (!EntityToIndexMap.TryGetValue(spring.PointA, out int pointAIndex) ||
                !EntityToIndexMap.TryGetValue(spring.PointB, out int pointBIndex))
            {
                return;
            }

            PointMassData pointA = PointMassArray[pointAIndex];
            PointMassData pointB = PointMassArray[pointBIndex];
            
            float3 delta = pointB.Position - pointA.Position;
            float currentDistance = math.length(delta);
            
            if (currentDistance < 1e-6f)
            {
                return;
            }

            float correction = (currentDistance - spring.RestLength) / currentDistance;
            float stiffnessFactor = spring.Stiffness * IterationFactor;
            
            if (!pointA.IsAnchored && !pointB.IsAnchored)
            {
                float totalMass = pointA.Mass + pointB.Mass;
                float massRatioA = pointB.Mass / totalMass;
                float massRatioB = pointA.Mass / totalMass;
                
                float3 offset = delta * correction * stiffnessFactor;
                pointA.Position += offset * massRatioA;
                pointB.Position -= offset * massRatioB;
            }
            else if (!pointA.IsAnchored)
            {
                pointA.Position += delta * correction * stiffnessFactor;
            }
            else if (!pointB.IsAnchored)
            {
                pointB.Position -= delta * correction * stiffnessFactor;
            }
            
            PointMassArray[pointAIndex] = pointA;
            PointMassArray[pointBIndex] = pointB;
        }
    }
    
    [BurstCompile]
    private partial struct UpdateTransformJob : IJobEntity
    {
        private void Execute(in PointMass pointMass, ref LocalTransform transform)
        {
            transform.Position = pointMass.Position;
        }
    }
}