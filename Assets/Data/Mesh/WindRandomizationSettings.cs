using Unity.Entities;
using Unity.Mathematics;

public struct WindRandomizationSettings : IComponentData
{
    public float3 BaseWindDirection;
    public float BaseWindForce;
    public float DirectionVariation;
    public float ForceVariation;
    public float ChangeSpeed;
    public float GustFrequency;
    public float GustIntensity;
    public float LastUpdateTime;
    public float NoiseOffsetX;
    public float NoiseOffsetY;
    public float NoiseOffsetZ;
}