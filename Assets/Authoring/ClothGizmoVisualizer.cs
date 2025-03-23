using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class ClothGizmoVisualizer : MonoBehaviour
{
    [SerializeField] private Color _pointColor = Color.white;
    [SerializeField] private Color _anchorPointColor = Color.red;
    [SerializeField] private Color _springColor = Color.cyan;
    [SerializeField] private float _pointSize = 0.05f;
    
    private EntityManager _entityManager;
    private EntityQuery _pointMassQuery;
    private EntityQuery _springQuery;
    
    private void OnEnable()
    {
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            
            _pointMassQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<PointMass>());
            _springQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<Spring>());
        }
    }
    
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            return;
        }
        
        DrawPointMasses();
        DrawSprings();
    }
    
    private void DrawPointMasses()
    {
        if (!_pointMassQuery.IsEmptyIgnoreFilter)
        {
            NativeArray<PointMass> pointMasses = _pointMassQuery.ToComponentDataArray<PointMass>(Allocator.Temp);
            NativeArray<Entity> pointEntities = _pointMassQuery.ToEntityArray(Allocator.Temp);
            
            try
            {
                for (int i = 0; i < pointMasses.Length; i++)
                {
                    Gizmos.color = pointMasses[i].IsAnchored ? _anchorPointColor : _pointColor;
                    Gizmos.DrawSphere(pointMasses[i].Position, _pointSize);
                }
            }
            finally
            {
                pointMasses.Dispose();
                pointEntities.Dispose();
            }
        }
    }
    
    private void DrawSprings()
    {
        if (!_springQuery.IsEmptyIgnoreFilter)
        {
            NativeArray<Spring> springs = _springQuery.ToComponentDataArray<Spring>(Allocator.Temp);
            
            try
            {
                Gizmos.color = _springColor;
                
                foreach (var spring in springs)
                {
                    if (_entityManager.Exists(spring.PointA) && _entityManager.Exists(spring.PointB))
                    {
                        PointMass pointA = _entityManager.GetComponentData<PointMass>(spring.PointA);
                        PointMass pointB = _entityManager.GetComponentData<PointMass>(spring.PointB);
                        
                        Gizmos.DrawLine(pointA.Position, pointB.Position);
                    }
                }
            }
            finally
            {
                springs.Dispose();
            }
        }
    }
}