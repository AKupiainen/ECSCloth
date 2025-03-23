using Unity.Entities;

public struct ClothSettings : IComponentData
{
    public float Gravity;
    public float TimeStep;
    public int ConstraintIterations;
}