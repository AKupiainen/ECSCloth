using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class WindRandomizationSystem : SystemBase
{
    private float SimplexNoise(float x, float y)
    {
        return 2f * noise.snoise(new float2(x, y)) - 1f;
    }
    
    protected override void OnUpdate()
    {
        float time = (float)SystemAPI.Time.ElapsedTime;
        
        foreach ((RefRW<ClothSettings> clothSettings, RefRW<WindRandomizationSettings> windSettings) in 
                 SystemAPI.Query<RefRW<ClothSettings>, RefRW<WindRandomizationSettings>>())
        {
            if (time - windSettings.ValueRO.LastUpdateTime < windSettings.ValueRO.ChangeSpeed * 0.1f)
            {
                continue;
            }

            windSettings.ValueRW.LastUpdateTime = time;
            
            float timeScale = time * windSettings.ValueRO.ChangeSpeed;
            
            float dirVarX = SimplexNoise(timeScale + windSettings.ValueRO.NoiseOffsetX, 0);
            float dirVarY = SimplexNoise(timeScale + windSettings.ValueRO.NoiseOffsetY, 10);
            float dirVarZ = SimplexNoise(timeScale + windSettings.ValueRO.NoiseOffsetZ, 20);
            
            float3 dirVariation = new float3(dirVarX, dirVarY, dirVarZ) * windSettings.ValueRO.DirectionVariation;
            float3 newDir = windSettings.ValueRO.BaseWindDirection + dirVariation;
            newDir = math.normalize(newDir);
            
            float baseVariation = SimplexNoise(timeScale * 0.5f, 0.5f) * windSettings.ValueRO.ForceVariation;
            
            float gustFactor = 0;
            if (windSettings.ValueRO.GustFrequency > 0)
            {
                float gustTime = time * windSettings.ValueRO.GustFrequency;
                float gustNoise = (SimplexNoise(gustTime, 0.7f) + 1f) * 0.5f; 
                
                if (gustNoise > 0.7f)
                {
                    gustFactor = (gustNoise - 0.7f) * 3.33f * windSettings.ValueRO.GustIntensity;
                }
            }
            
            float forceMultiplier = 1.0f + baseVariation + gustFactor;
            float newForce = windSettings.ValueRO.BaseWindForce * math.max(forceMultiplier, 0.1f);
            
            clothSettings.ValueRW.WindDirection = newDir;
            clothSettings.ValueRW.WindForce = newForce;
        }
    }
}