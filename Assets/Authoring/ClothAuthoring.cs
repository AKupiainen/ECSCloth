using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class ClothAuthoring : MonoBehaviour
{
    [SerializeField] private int _width = 10;
    [SerializeField] private int _height = 10;
    [SerializeField] private float _pointMassDistance = 0.25f;
    [SerializeField] private float _springStiffness = 0.8f;
    [SerializeField] private float _gravity = 9.8f;
    [SerializeField] private float _timeStep = 0.016f; 
    [SerializeField] private int _constraintIterations = 5;
    [SerializeField] private bool[] _anchorPoints;
    
    [SerializeField] private float _defaultMass = 1.0f;
    [SerializeField] private bool _variableMass = false;
    [SerializeField] private float _edgeMassFactor = 1.5f;
    [SerializeField] private float _cornerMassFactor = 2.0f;
    [SerializeField] private AnimationCurve _massDistributionCurve;

    private class ClothBaker : Baker<ClothAuthoring>
    {
        public override void Bake(ClothAuthoring authoring)
        {
            Entity[,] pointMasses = new Entity[authoring._width, authoring._height];

            for (int x = 0; x < authoring._width; x++)
            {
                for (int y = 0; y < authoring._height; y++)
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

            for (int x = 0; x < authoring._width; x++)
            {
                for (int y = 0; y < authoring._height; y++)
                {
                    if (x < authoring._width - 1)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 1, y],
                            authoring._pointMassDistance,
                            authoring._springStiffness
                        );
                    }

                    if (y < authoring._height - 1)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x, y + 1],
                            authoring._pointMassDistance,
                            authoring._springStiffness
                        );
                    }

                    if (x < authoring._width - 1 && y < authoring._height - 1)
                    {
                        float diagonalLength = authoring._pointMassDistance * math.sqrt(2);

                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 1, y + 1],
                            diagonalLength,
                            authoring._springStiffness * 0.8f
                        );

                        CreateSpring(
                            pointMasses[x + 1, y],
                            pointMasses[x, y + 1],
                            diagonalLength,
                            authoring._springStiffness * 0.8f
                        );
                    }

                    if (x < authoring._width - 2)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x + 2, y],
                            authoring._pointMassDistance * 2,
                            authoring._springStiffness * 0.5f
                        );
                    }

                    if (y < authoring._height - 2)
                    {
                        CreateSpring(
                            pointMasses[x, y],
                            pointMasses[x, y + 2],
                            authoring._pointMassDistance * 2,
                            authoring._springStiffness * 0.5f
                        );
                    }
                }
            }

            Entity settingsEntity = CreateAdditionalEntity(TransformUsageFlags.None);

            AddComponent(settingsEntity, new ClothSettings
            {
                Gravity = authoring._gravity,
                TimeStep = authoring._timeStep,
                ConstraintIterations = authoring._constraintIterations
            });
        }

        private float CalculatePointMass(ClothAuthoring authoring, int x, int y)
        {
            if (!authoring._variableMass)
            {
                return authoring._defaultMass;
            }
            
            bool isCorner = (x == 0 || x == authoring._width - 1) && (y == 0 || y == authoring._height - 1);
            bool isEdge = x == 0 || x == authoring._width - 1 || y == 0 || y == authoring._height - 1;
            
            if (isCorner)
            {
                return authoring._defaultMass * authoring._cornerMassFactor;
            }
            
            if (isEdge)
            {
                return authoring._defaultMass * authoring._edgeMassFactor;
            }
            
            if (authoring._massDistributionCurve != null)
            {
                float normalizedX = (float)x / (authoring._width - 1);
                float normalizedY = (float)y / (authoring._height - 1);
                
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
}