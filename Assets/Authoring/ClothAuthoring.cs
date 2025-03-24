using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class ClothAuthoring : MonoBehaviour
{
    [Header("Cloth Dimensions")]
    [SerializeField] private int _width = 10;
    [SerializeField] private int _height = 10;
    [SerializeField] private float _pointMassDistance = 0.25f;
    [SerializeField] private bool[] _anchorPoints;
    
    [Header("Spring Properties")]
    [SerializeField, Range(0.1f, 1.0f)] private float _springStiffness = 0.8f;
    [SerializeField, Range(0.5f, 1.0f)] private float _diagonalStiffnessMultiplier = 0.8f;
    [SerializeField, Range(0.3f, 0.8f)] private float _bendStiffnessMultiplier = 0.5f;
    
    [Header("Physics Parameters")]
    [SerializeField, Range(0f, 20f)] private float _gravity = 9.8f;
    [SerializeField, Range(0.001f, 0.05f)] private float _timeStep = 0.016f; 
    [SerializeField, Range(1, 10)] private int _constraintIterations = 5;
    [SerializeField, Range(1, 10)] private int _substeps = 3;
    [SerializeField, Range(0f, 0.2f)] private float _damping = 0.05f;
    
    [Header("Mass Distribution")]
    [SerializeField] private float _defaultMass = 1.0f;
    [SerializeField] private bool _variableMass;
    [SerializeField, Range(1f, 3f)] private float _edgeMassFactor = 1.5f;
    [SerializeField, Range(1.5f, 5f)] private float _cornerMassFactor = 2.0f;
    [SerializeField] private AnimationCurve _massDistributionCurve;
    
    [Header("Wind Settings")]
    [SerializeField] private bool _enableWind = false;
    [SerializeField, Range(0f, 10f)] private float _baseWindForce = 1.0f;
    [SerializeField] private Vector3 _baseWindDirection = new(1, 0, 0);
    
    [Header("Wind Randomization")]
    [SerializeField] private bool _randomizeWind = true;
    [SerializeField, Range(0f, 1f)] private float _windDirectionVariation = 0.3f;
    [SerializeField, Range(0f, 1f)] private float _windForceVariation = 0.5f;
    [SerializeField, Range(0.1f, 5f)] private float _windChangeSpeed = 1.0f;
    [SerializeField] private AnimationCurve _windGustPattern;
    [SerializeField, Range(0f, 10f)] private float _gustFrequency = 2.0f;
    [SerializeField, Range(1f, 5f)] private float _gustIntensity = 2.0f;
    
    [Header("Self Collision")]
    [SerializeField] private bool _enableSelfCollision = false;
    [SerializeField, Range(0.05f, 1f)] private float _selfCollisionRadius = 0.1f;

    private class ClothBaker : Baker<ClothAuthoring>
    {
        public override void Bake(ClothAuthoring authoring)
        {
            Entity[,] pointMasses = new Entity[authoring.Width, authoring.Height];

            for (int x = 0; x < authoring.Width; x++)
            {
                for (int y = 0; y < authoring.Height; y++)
                {
                    Entity pointEntity = CreateAdditionalEntity(TransformUsageFlags.Dynamic);
                    
                    float3 position = new(
                        x * authoring._pointMassDistance,
                        -y * authoring._pointMassDistance,
                        0
                    );

                    bool isAnchored = y == 0 && (authoring._anchorPoints == null ||
                                                 authoring._anchorPoints.Length <= x ||
                                                 authoring._anchorPoints[x]);
                    
                    float mass = CalculatePointMass(authoring, x, y);

                    AddComponent(pointEntity, new PointMass
                    {
                        Position = position,
                        PreviousPosition = position,
                        IsAnchored = isAnchored,
                        Mass = mass
                    });

                    AddComponent(pointEntity, LocalTransform.FromPosition(position));
                    pointMasses[x, y] = pointEntity;
                }
            }

            for (int x = 0; x < authoring.Width; x++)
            {
                for (int y = 0; y < authoring.Height; y++)
                {
                    if (x < authoring.Width - 1)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 1, y],
                            authoring._pointMassDistance,
                            authoring._springStiffness
                        );
                    }

                    if (y < authoring.Height - 1)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x, y + 1],
                            authoring._pointMassDistance,
                            authoring._springStiffness
                        );
                    }

                    if (x < authoring.Width - 1 && y < authoring.Height - 1)
                    {
                        float diagonalLength = authoring._pointMassDistance * math.sqrt(2);
                        float diagonalStiffness = authoring._springStiffness * authoring._diagonalStiffnessMultiplier;

                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 1, y + 1],
                            diagonalLength,
                            diagonalStiffness
                        );

                        CreateSpring(
                            pointMasses[x + 1, y],
                            pointMasses[x, y + 1],
                            diagonalLength,
                            diagonalStiffness
                        );
                    }

                    float bendStiffness = authoring._springStiffness * authoring._bendStiffnessMultiplier;
                    
                    if (x < authoring.Width - 2)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 2, y],
                            authoring._pointMassDistance * 2,
                            bendStiffness
                        );
                    }

                    if (y < authoring.Height - 2)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x, y + 2],
                            authoring._pointMassDistance * 2,
                            bendStiffness
                        );
                    }
                }
            }

            Entity settingsEntity = CreateAdditionalEntity(TransformUsageFlags.None);
            
            float windForce = authoring._enableWind ? authoring._baseWindForce : 0;
            float3 windDirection = math.normalize(new float3(
                authoring._baseWindDirection.x,
                authoring._baseWindDirection.y,
                authoring._baseWindDirection.z
            ));
            
            AddComponent(settingsEntity, new ClothSettings
            {
                Gravity = authoring._gravity,
                TimeStep = authoring._timeStep,
                ConstraintIterations = authoring._constraintIterations,
                Damping = authoring._damping,
                Substeps = authoring._substeps,
                WindForce = windForce,
                WindDirection = windDirection,
                EnableSelfCollision = authoring._enableSelfCollision,
                SelfCollisionRadius = authoring._selfCollisionRadius
            });
            
            if (authoring._enableWind && authoring._randomizeWind)
            {
                AddComponent(settingsEntity, new WindRandomizationSettings
                {
                    BaseWindForce = authoring._baseWindForce,
                    BaseWindDirection = windDirection,
                    DirectionVariation = authoring._windDirectionVariation,
                    ForceVariation = authoring._windForceVariation,
                    ChangeSpeed = authoring._windChangeSpeed,
                    GustFrequency = authoring._gustFrequency,
                    GustIntensity = authoring._gustIntensity,
                    LastUpdateTime = 0,
                    NoiseOffsetX = UnityEngine.Random.Range(0f, 1000f),
                    NoiseOffsetY = UnityEngine.Random.Range(0f, 1000f),
                    NoiseOffsetZ = UnityEngine.Random.Range(0f, 1000f)
                });
            }
        }

        private float CalculatePointMass(ClothAuthoring authoring, int x, int y)
        {
            if (!authoring._variableMass)
            {
                return authoring._defaultMass;
            }
            
            bool isCorner = (x == 0 || x == authoring.Width - 1) && (y == 0 || y == authoring.Height - 1);
            bool isEdge = x == 0 || x == authoring.Width - 1 || y == 0 || y == authoring.Height - 1;
            
            if (isCorner)
            {
                return authoring._defaultMass * authoring._cornerMassFactor;
            }
            
            if (isEdge)
            {
                return authoring._defaultMass * authoring._edgeMassFactor;
            }
            
            if (authoring._massDistributionCurve is { length: > 0 })
            {
                float normalizedX = (float)x / (authoring.Width - 1);
                float normalizedY = (float)y / (authoring.Height - 1);
                
                float centerX = 0.5f;
                float centerY = 0.5f;
                float distFromCenter = math.sqrt(math.pow(normalizedX - centerX, 2) + math.pow(normalizedY - centerY, 2));
                distFromCenter = math.clamp(distFromCenter * 2, 0, 1);
                
                float massFactor = authoring._massDistributionCurve.Evaluate(distFromCenter);
                return authoring._defaultMass * massFactor;
            }
            
            return authoring._defaultMass;
        }

        private void CreateSpring(Entity pointA, Entity pointB, float restLength, float stiffness)
        {
            Entity springEntity = CreateAdditionalEntity(TransformUsageFlags.None);

            AddComponent(springEntity, new Spring
            {
                PointA = pointA,
                PointB = pointB,
                RestLength = restLength,
                Stiffness = stiffness,
            });
        }
    }

    public int Width => _width;

    public int Height => _height;
    
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        
        // Draw cloth grid
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                Vector3 pos = transform.position + new Vector3(
                    x * _pointMassDistance,
                    -y * _pointMassDistance,
                    0
                );
                
                bool isAnchored = y == 0 && (_anchorPoints == null ||
                                           _anchorPoints.Length <= x ||
                                           _anchorPoints[x]);
                
                Gizmos.color = isAnchored ? Color.red : Color.white;
                Gizmos.DrawSphere(pos, 0.05f);
                
                if (x < _width - 1)
                {
                    Vector3 next = transform.position + new Vector3(
                        (x + 1) * _pointMassDistance,
                        -y * _pointMassDistance,
                        0
                    );
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, next);
                }
                
                if (y < _height - 1)
                {
                    Vector3 next = transform.position + new Vector3(
                        x * _pointMassDistance,
                        -(y + 1) * _pointMassDistance,
                        0
                    );
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos, next);
                }
            }
        }
        
        if (_enableWind && _baseWindForce > 0)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = transform.position + new Vector3(
                _width * _pointMassDistance * 0.5f,
                -_height * _pointMassDistance * 0.5f,
                0
            );
            
            Vector3 windDir = _baseWindDirection.normalized; 
            float arrowLength = 2.0f;
            Gizmos.DrawRay(center, windDir * arrowLength);
            
            Vector3 right = Vector3.Cross(Vector3.forward, windDir).normalized;
            Vector3 arrowEnd = center + windDir * arrowLength;
            Gizmos.DrawRay(arrowEnd, -windDir * 0.3f + right * 0.15f);
            Gizmos.DrawRay(arrowEnd, -windDir * 0.3f - right * 0.15f);
            
            if (_randomizeWind)
            {
                Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.6f);
                float variation = _windDirectionVariation * 0.5f;
                Vector3 altDir1 = Quaternion.AngleAxis(variation * 45f, Vector3.forward) * windDir;
                Vector3 altDir2 = Quaternion.AngleAxis(-variation * 45f, Vector3.forward) * windDir;
                
                Gizmos.DrawRay(center, altDir1 * arrowLength * 0.7f);
                Gizmos.DrawRay(center, altDir2 * arrowLength * 0.7f);
            }
        }
    }
#endif
}