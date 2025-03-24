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
        public float3 Velocity;     
        public bool IsAnchored;
        public float Mass;
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
        
        int substeps = clothSettings.Substeps > 0 ? clothSettings.Substeps : 1;
        float subDeltaTime = deltaTime / substeps;
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
        
        // Calculate initial velocities
        for (int i = 0; i < pointMassCount; i++)
        {
            _pointMassEntities[i] = pointMassEntitiesFromQuery[i];
            float3 velocity = (pointMassComponentsFromQuery[i].Position - pointMassComponentsFromQuery[i].PreviousPosition) / deltaTime;
            
            _pointMassArray[i] = new PointMassData
            {
                Position = pointMassComponentsFromQuery[i].Position,
                PreviousPosition = pointMassComponentsFromQuery[i].PreviousPosition,
                Velocity = velocity,
                IsAnchored = pointMassComponentsFromQuery[i].IsAnchored,
                Mass = pointMassComponentsFromQuery[i].Mass
            };
        }
        
        NativeArray<Spring> springsFromQuery = _springQuery.ToComponentDataArray<Spring>(Allocator.TempJob);
        
        for (int i = 0; i < springCount; i++)
        {
            _springArray[i] = springsFromQuery[i];
        }
        
        NativeHashMap<Entity, int> entityToIndexMap = new(pointMassCount, Allocator.TempJob);
        for (int i = 0; i < pointMassCount; i++)
        {
            entityToIndexMap.Add(_pointMassEntities[i], i);
        }
        
        for (int step = 0; step < substeps; step++)
        {
            ApplyDampingJob dampingJob = new()
            {
                PointMassArray = _pointMassArray,
                DampingFactor = clothSettings.Damping
            };
            
            state.Dependency = dampingJob.Schedule(_pointMassArray.Length, 64, state.Dependency);
            state.Dependency.Complete();
            
            ApplyForcesJob applyForcesJob = new()
            {
                PointMassArray = _pointMassArray,
                Gravity = gravity,
                DeltaTime = subDeltaTime,
                WindForce = clothSettings.WindForce,
                WindDirection = clothSettings.WindDirection,
                TimeVariance = (float)SystemAPI.Time.ElapsedTime
            };
            
            state.Dependency = applyForcesJob.Schedule(_pointMassArray.Length, 64, state.Dependency);
            state.Dependency.Complete();
            
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
            
            if (clothSettings.EnableSelfCollision && _pointMassArray.Length > 0)
            {
                SelfCollisionJob selfCollisionJob = new()
                {
                    PointMassArray = _pointMassArray,
                    CollisionRadius = clothSettings.SelfCollisionRadius
                };
                
                state.Dependency = selfCollisionJob.Schedule(_pointMassArray.Length, 64, state.Dependency);
                state.Dependency.Complete();
            }
            
            UpdateVelocityJob velocityJob = new()
            {
                PointMassArray = _pointMassArray,
                DeltaTime = subDeltaTime
            };
            
            state.Dependency = velocityJob.Schedule(_pointMassArray.Length, 64, state.Dependency);
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
    private struct ApplyDampingJob : IJobParallelFor
    {
        public NativeArray<PointMassData> PointMassArray;
        public float DampingFactor;
        
        public void Execute(int index)
        {
            PointMassData pointMass = PointMassArray[index];
            
            if (pointMass.IsAnchored)
            {
                return;
            }
            pointMass.Velocity *= math.max(0, 1.0f - DampingFactor);
            pointMass.PreviousPosition = pointMass.Position;
            
            PointMassArray[index] = pointMass;
        }
    }
    
    [BurstCompile]
    private struct ApplyForcesJob : IJobParallelFor
    {
        public NativeArray<PointMassData> PointMassArray;
        public float3 Gravity;
        public float DeltaTime;
        public float3 WindDirection;
        public float WindForce;
        public float TimeVariance;
    
        public void Execute(int index)
        {
            PointMassData pointMass = PointMassArray[index];
        
            if (pointMass.IsAnchored)
            {
                return;
            }

            float3 acceleration = Gravity;
        
            if (WindForce > 0)
            {
                float windDirLength = math.length(WindDirection);
                if (windDirLength > 1e-5f)
                {
                    float3 safeWindDir = WindDirection / windDirLength;
                
                    float safeWindForce = math.min(WindForce, 5.0f);
                    float noise = math.sin(TimeVariance * 2.0f + index * 0.1f) * 0.2f + 0.8f;
                    
                    float3 wind = safeWindDir * safeWindForce * noise;
                    acceleration += wind / math.max(0.01f, pointMass.Mass);
                }
            }
            
            pointMass.Velocity += acceleration * DeltaTime;
            
            float maxVelocity = 15.0f;
            float velocityLength = math.length(pointMass.Velocity);
            
            if (velocityLength > maxVelocity)
            {
                pointMass.Velocity *= (maxVelocity / velocityLength);
            }
        
            pointMass.Position += pointMass.Velocity * DeltaTime;
        
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

            float3 direction = delta / currentDistance;
            float correction = (currentDistance - spring.RestLength);
            
            float stretchFactor = math.abs(correction) / spring.RestLength;
            float adjustedStiffness = spring.Stiffness * (1.0f + stretchFactor * 0.5f);
            adjustedStiffness = math.min(adjustedStiffness, 1.0f);
            
            float stiffnessFactor = adjustedStiffness * IterationFactor;
            
            if (!pointA.IsAnchored && !pointB.IsAnchored)
            {
                float totalMass = pointA.Mass + pointB.Mass;
                float massRatioA = pointB.Mass / totalMass;
                float massRatioB = pointA.Mass / totalMass;
                
                float3 offset = direction * correction * stiffnessFactor;
                pointA.Position += offset * massRatioA;
                pointB.Position -= offset * massRatioB;
            }
            else if (!pointA.IsAnchored)
            {
                pointA.Position += direction * correction * stiffnessFactor;
            }
            else if (!pointB.IsAnchored)
            {
                pointB.Position -= direction * correction * stiffnessFactor;
            }
            
            PointMassArray[pointAIndex] = pointA;
            PointMassArray[pointBIndex] = pointB;
        }
    }
    
    [BurstCompile]
    private struct SelfCollisionJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] 
        public NativeArray<PointMassData> PointMassArray;
        public float CollisionRadius;
        
        public void Execute(int index)
        {
            PointMassData pointA = PointMassArray[index];
            if (pointA.IsAnchored)
            {
                return; 
            }
            
            int rangeStart = math.max(0, index - 20);
            int rangeEnd = math.min(PointMassArray.Length - 1, index + 20);
            
            for (int i = rangeStart; i <= rangeEnd; i += 3)
            {
                if (i == index)
                {
                    continue;
                }

                PointMassData pointB = PointMassArray[i];
                
                float3 delta = pointB.Position - pointA.Position;
                float distanceSq = math.lengthsq(delta);
                float radiusSq = CollisionRadius * CollisionRadius;
                
                if (distanceSq > 0 && distanceSq < radiusSq)
                {
                    float distance = math.sqrt(distanceSq);
                    float3 normal = delta / distance;
                    float penetration = CollisionRadius - distance;
                    
                    float correctionFactor = 0.5f;
                    
                    if (!pointB.IsAnchored)
                    {
                        float totalMass = pointA.Mass + pointB.Mass;
                        float massRatioA = pointB.Mass / totalMass;
                        
                        pointA.Position -= normal * penetration * correctionFactor * massRatioA;
                        PointMassArray[index] = pointA;
                    }
                    else
                    {
                        pointA.Position -= normal * penetration * correctionFactor;
                        PointMassArray[index] = pointA;
                    }
                }
            }
        }
    }
    
    [BurstCompile]
    private struct UpdateVelocityJob : IJobParallelFor
    {
        public NativeArray<PointMassData> PointMassArray;
        public float DeltaTime;
        
        public void Execute(int index)
        {
            PointMassData pointMass = PointMassArray[index];
            
            if (!pointMass.IsAnchored)
            {
                pointMass.Velocity = (pointMass.Position - pointMass.PreviousPosition) / DeltaTime;
                
                float maxVelocity = 10f;
                float velocityMagnitude = math.length(pointMass.Velocity);
                
                if (velocityMagnitude > maxVelocity)
                {
                    pointMass.Velocity *= (maxVelocity / velocityMagnitude);
                }
                
                PointMassArray[index] = pointMass;
            }
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