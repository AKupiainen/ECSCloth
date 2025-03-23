using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ClothMouseForceController : MonoBehaviour
{
    [Header("Mouse Force Settings")]
    [SerializeField] private float _forceStrength = 5.0f;
    [SerializeField] private float _forceRadius = 1.0f;
    [SerializeField] private AnimationCurve _forceFalloff = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    [Header("Visualization")]
    [SerializeField] private Color _forceColor = new(1, 0.5f, 0, 0.5f); 
    [SerializeField] private bool _showForceArea = true;
    
    [Header("Input Settings")]
    [SerializeField] private KeyCode _forceKey = KeyCode.Mouse0;
    [SerializeField] private float _forceDistance = 10f;
    
    private Vector3 _hitPoint;
    private bool _showDebugSphere;
    private float _debugTimer;
    
    private EntityManager _entityManager;
    private EntityQuery _pointMassQuery;
    
    private void OnEnable()
    {
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _pointMassQuery = _entityManager.CreateEntityQuery(ComponentType.ReadWrite<PointMass>());
        }
    }
    
    private void Update()
    {
        if (_showDebugSphere)
        {
            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0)
            {
                _showDebugSphere = false;
            }
        }
        
        if (Input.GetKey(_forceKey))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 forcePoint = ray.origin + ray.direction * _forceDistance;
            
            ApplyForceToClosestPoints(forcePoint);
            
            if (_showForceArea)
            {
                _hitPoint = forcePoint;
                _showDebugSphere = true;
                _debugTimer = 0.2f; 
            }
        }
    }
    
    private void ApplyForceToClosestPoints(Vector3 point)
    {
        if (_pointMassQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        NativeArray<Entity> pointEntities = _pointMassQuery.ToEntityArray(Allocator.Temp);
        
        try
        {
            foreach (Entity entity in pointEntities)
            {
                PointMass pointMass = _entityManager.GetComponentData<PointMass>(entity);
                
                if (pointMass.IsAnchored)
                {
                    continue;
                }

                float3 pointPosition = pointMass.Position;
                float3 forceCenter = new(point.x, point.y, point.z);
                float distance = math.length(pointPosition - forceCenter);
                
                if (distance <= _forceRadius)
                {
                    float3 forceDir = math.normalize(pointPosition - forceCenter);
                    
                    float normalizedDistance = distance / _forceRadius;
                    float forceMagnitude = _forceStrength * _forceFalloff.Evaluate(1 - normalizedDistance);
                    
                    float3 forceVector = forceDir * forceMagnitude * Time.deltaTime;
                    pointMass.Position += forceVector;
                    
                    _entityManager.SetComponentData(entity, pointMass);
                }
            }
        }
        finally
        {
            pointEntities.Dispose();
        }
    }
    
    private void OnDrawGizmos()
    {
        if (_showDebugSphere)
        {
            Gizmos.color = _forceColor;
            Gizmos.DrawSphere(_hitPoint, _forceRadius);
        }
    }
}