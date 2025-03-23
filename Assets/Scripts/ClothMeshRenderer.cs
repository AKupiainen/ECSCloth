using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ClothMeshRenderer : MonoBehaviour
{
    [SerializeField] private Material _clothMaterial;
    [SerializeField] private bool _doubleSided = true;
    [SerializeField] private int _meshWidth = 10;
    [SerializeField] private int _meshHeight = 10;
    
    private EntityManager _entityManager;
    private Entity _meshEntity;
    
    private Mesh _clothMesh;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    
    private Vector3[] _vertices;
    private Vector3[] _normals;
    private Vector2[] _uvs;
    private int[] _triangles;
    
    private bool _initialized;
    private int _width;
    private int _height;
    
    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        
        if (_meshFilter == null)
        {
            _meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        _meshRenderer = GetComponent<MeshRenderer>();
        
        if (_meshRenderer == null)
        {
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        if (_clothMaterial != null)
        {
            _meshRenderer.material = _clothMaterial;
        }
    
        _clothMesh = new Mesh
        {
            name = "ClothMesh"
        };
    
        _meshFilter.mesh = _clothMesh;
    }
    
    private void OnEnable()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
        {
            Debug.LogError("Default World not available!");
            return;
        }
        
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _meshEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(_meshEntity, new ClothMeshTag { Width = _meshWidth, Height = _meshHeight });
        _entityManager.AddBuffer<VertexPosition>(_meshEntity);
        _entityManager.AddBuffer<VertexNormal>(_meshEntity);

        int vertexCount = _meshWidth * _meshHeight;
        var vertexBuffer = _entityManager.GetBuffer<VertexPosition>(_meshEntity);
        var normalBuffer = _entityManager.GetBuffer<VertexNormal>(_meshEntity);

        vertexBuffer.ResizeUninitialized(vertexCount);
        normalBuffer.ResizeUninitialized(vertexCount);
        
        _width = _meshWidth;
        _height = _meshHeight;
        
        InitializeMesh(_width, _height);
        
        for (int i = 0; i < vertexCount; i++)
        {
            vertexBuffer[i] = new VertexPosition { Value = new float3(_vertices[i].x, _vertices[i].y, _vertices[i].z) };
            normalBuffer[i] = new VertexNormal { Value = new float3(_normals[i].x, _normals[i].y, _normals[i].z) };
        }
    }
    
    private void Update()
    {
        if (!_initialized || !_entityManager.Exists(_meshEntity))
        {
            return;
        }

        try
        {
            DynamicBuffer<VertexPosition> vertexBuffer = _entityManager.GetBuffer<VertexPosition>(_meshEntity);
            DynamicBuffer<VertexNormal> normalBuffer = _entityManager.GetBuffer<VertexNormal>(_meshEntity);
            
            if (vertexBuffer.Length != _vertices.Length || normalBuffer.Length != _normals.Length)
            {
                if (vertexBuffer.Length > 0)
                {
                    ClothMeshTag tag = _entityManager.GetComponentData<ClothMeshTag>(_meshEntity);
                    
                    if (tag is { Width: > 0, Height: > 0 } && tag.Width * tag.Height == vertexBuffer.Length)
                    {
                        _width = tag.Width;
                        _height = tag.Height;
                        InitializeMesh(_width, _height);
                    }
                }
                
                return;
            }
                
            bool hasChanges = false;
            
            for (int i = 0; i < _vertices.Length; i++)
            {
                float3 pos = vertexBuffer[i].Value;
                float3 norm = normalBuffer[i].Value;
                
                Vector3 newPos = new(pos.x, pos.y, pos.z);
                Vector3 newNorm = new(norm.x, norm.y, norm.z);
                
                if (_vertices[i] != newPos || _normals[i] != newNorm)
                {
                    _vertices[i] = newPos;
                    _normals[i] = newNorm;
                    hasChanges = true;
                }
            }
            
            if (hasChanges)
            {
                _clothMesh.vertices = _vertices;
                _clothMesh.normals = _normals;
                _clothMesh.RecalculateBounds();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating cloth mesh: {e.Message}");
        }
    }
    
    private void InitializeMesh(int width, int height)
    {
        int vertexCount = width * height;
        _vertices = new Vector3[vertexCount];
        _normals = new Vector3[vertexCount];
        _uvs = new Vector2[vertexCount];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                _uvs[index] = new Vector2((float)x / (width - 1), (float)y / (height - 1));
            }
        }
        
        int triangleCount = 2 * (width - 1) * (height - 1);

        if (_doubleSided)
        {
            triangleCount *= 2;
        }
        
        _triangles = new int[triangleCount * 3];
        
        int triangleIndex = 0;
        
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int bottomLeft = y * width + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = (y + 1) * width + x;
                int topRight = topLeft + 1;
                
                _triangles[triangleIndex++] = bottomLeft;
                _triangles[triangleIndex++] = topLeft;
                _triangles[triangleIndex++] = topRight;
                
                _triangles[triangleIndex++] = bottomLeft;
                _triangles[triangleIndex++] = topRight;
                _triangles[triangleIndex++] = bottomRight;
            }
        }
        
        if (_doubleSided)
        {
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    int bottomLeft = y * width + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = (y + 1) * width + x;
                    int topRight = topLeft + 1;
                    
                    _triangles[triangleIndex++] = bottomLeft;
                    _triangles[triangleIndex++] = topRight;
                    _triangles[triangleIndex++] = topLeft;
                    
                    _triangles[triangleIndex++] = bottomLeft;
                    _triangles[triangleIndex++] = bottomRight;
                    _triangles[triangleIndex++] = topRight;
                }
            }
        }
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                _vertices[index] = new Vector3(
                    (x / (float)(width - 1) - 0.5f) * 2f, 
                    (y / (float)(height - 1) - 0.5f) * 2f, 
                    0
                );
                _normals[index] = Vector3.forward;
            }
        }
        
        _clothMesh.Clear();
        _clothMesh.vertices = _vertices;
        _clothMesh.normals = _normals;
        _clothMesh.uv = _uvs;
        _clothMesh.triangles = _triangles;
        _clothMesh.RecalculateBounds();
        
        _initialized = true;
    }
    
    private void OnDisable()
    {
        if (_entityManager.Exists(_meshEntity))
        {
            _entityManager.DestroyEntity(_meshEntity);
        }
    }
    
    private void OnDestroy()
    {
        if (_clothMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_clothMesh);
            }
            else
            {
                DestroyImmediate(_clothMesh);
            }
        }
    }
}