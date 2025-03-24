using Unity.Entities;
using Unity.Mathematics;

public struct ClothSettings : IComponentData
{
    public float Gravity;
    public float TimeStep;
    public int ConstraintIterations;
    public float Damping;             
    public int Substeps;               
    public float3 WindDirection;       
    public float WindForce;            
    public bool EnableSelfCollision;   
    public float SelfCollisionRadius; 
}