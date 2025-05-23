using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainScript : MonoBehaviour {


    public void ReceiveData(string data)
    {
        Debug.Log("Received: " + data);
    }


    public Shader TerrainShader;
    public ComputeShader heightmapComputeShader;
    private int FractalNoiseCS;


    [Header("Vegetation Settings")]
    public ComputeShader vegetationComputeShader;
    public Mesh vegetationMesh;
    public Material vegetationMaterial;
    public float scale = 0.1f;
    public Vector2 minMaxHeight = new Vector2(0.5f, 1.5f);
    private Bounds bounds;
    private int terrainTriangleCount = 0;
    private GraphicsBuffer terrainTriangleBuffer;
    private GraphicsBuffer terrainVertexBuffer;
    private GraphicsBuffer transformMatrixBuffer;
    private GraphicsBuffer vegetationTriangleBuffer;
    private GraphicsBuffer vegetationVertexBuffer;
    private GraphicsBuffer vegetationUVBuffer;
    private int vegetation_kernel;
    [Range(0.0f, 1.0f)] public float vegetationDensity = 1.0f;
    [Range(0.0f, 1.0f)] public float vegetationSlope = 1.0f;
    [Range(-50.0f, 50.0f)] public float vegetationHeight = 1.0f;
    private float maxheight=0;



    // Plane
    private const int planeSize = 100;
    private const float halfLength = planeSize * 0.5f;
    private const int sideVertCount = planeSize * 2;
    private const int vert_num = sideVertCount + 1;
    private Mesh mesh;
    private Mesh Cmesh;
    private Vector3[] vertices;
    private int[] triangles;
    private Vector3[] normals;
    private Material objMaterial;
    private RenderTexture computeResult;
    private int GRID_DIM, resN;

    public struct OctaveParams {
        public float lacunarity;
        public float persistence;
        public int rotation;
        public float shift;
    }

    const int OctaveCount = 4;
    OctaveParams[] octaves = new OctaveParams[OctaveCount];
    float[] heightMap = new float[vert_num * vert_num];

    [System.Serializable]
    public struct UI_OctaveParams {
        public float lacunarity;
        [Range(0.0f, 1.0f)] public float persistence;
        [Range(0, 360)] public int rotation;
        public float shift;
    }

    [Header("Heightmap Settings")]
    public bool updateOctave = false;
    public float baseFrequency = 1;
    [SerializeField] public UI_OctaveParams octave1;
    [SerializeField] public UI_OctaveParams octave2;
    [SerializeField] public UI_OctaveParams octave3;
    [SerializeField] public UI_OctaveParams octave4;
    private ComputeBuffer octaveBuffer;
    private ComputeBuffer heightBuffer;
    public bool reCalcCollision = false;

    [Header("Material Settings")]
    public Texture2D albedoTex;
    public Texture2D higher_albedoTex;
    [Range(0.0f, 20.0f)]public float uvMap = 20.0f;
    [Range(0.0f, 100.0f)]public float minY,maxY = 1;
    [Range(0.0f, 1.0f)] public float metalic = 1;
    [Range(0.0f, 1.0f)] public float roughness = 1;
    [Range(0.0f, 1.0f)] public float subsurface = 1;
    [Range(0.0f, 2.0f)] public float specular = 1;
    [Range(0.0f, 1.0f)] public float specularTint = 1;
    [Range(0.0f, 1.0f)] public float anisotropic = 1;
    [Range(0.0f, 1.0f)] public float sheen = 1;
    [Range(0.0f, 1.0f)] public float sheenTint = 1;
    [Range(0.0f, 1.0f)] public float clearCoat = 1;
    [Range(0.0f, 1.0f)] public float clearCoatGloss = 1;


    void FillOctaveStruct(UI_OctaveParams displaySettings, ref OctaveParams computeSettings) {
        computeSettings.lacunarity = displaySettings.lacunarity;
        computeSettings.persistence = displaySettings.persistence;
        computeSettings.rotation = displaySettings.rotation;
        computeSettings.shift = displaySettings.shift;
    }

    void SetSOctaveBuffers() {
        FillOctaveStruct(octave1, ref octaves[0]);
        FillOctaveStruct(octave2, ref octaves[1]);
        FillOctaveStruct(octave3, ref octaves[2]);
        FillOctaveStruct(octave4, ref octaves[3]);
        octaveBuffer.SetData(octaves);
        heightmapComputeShader.SetBuffer(FractalNoiseCS, "_Octaves", octaveBuffer);
    }

    RenderTexture CreateRenderTex(int width, int height, int depth, RenderTextureFormat format, bool useMips) {
        RenderTexture rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
        rt.dimension = TextureDimension.Tex2DArray;
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.enableRandomWrite = true;
        rt.volumeDepth = depth;
        rt.useMipMap = useMips;
        rt.autoGenerateMips = false;
        rt.anisoLevel = 16;
        rt.Create();
        return rt;
    }

    private void CreatePlaneMesh() {
        mesh = GetComponent<MeshFilter>().mesh = new Mesh();
        mesh.name = "TerrainMesh";
        
        vertices = new Vector3[vert_num * vert_num];
        Vector2[] uv = new Vector2[vertices.Length];
        Vector4[] tangents = new Vector4[vertices.Length];
        Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
        
        for (int i = 0, x = 0; x <= sideVertCount; ++x) {
            for (int z = 0; z <= sideVertCount; ++z, ++i) {
                vertices[i] = new Vector3(((float)x / sideVertCount * planeSize) - halfLength, 0,//heightMap[z*resN+x]*100, 
                                          ((float)z / sideVertCount * planeSize) - halfLength);
                uv[i] = new Vector2((float)x / sideVertCount, (float)z / sideVertCount);
                tangents[i] = tangent;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.tangents = tangents;
        triangles = new int[sideVertCount * sideVertCount * 6];
        
        for (int ti = 0, vi = 0, x = 0; x < sideVertCount; ++vi, ++x) {
            for (int z = 0; z < sideVertCount; ti += 6, ++vi, ++z) {
                triangles[ti + 0] = vi;
                triangles[ti + 1] = vi + 1;
                triangles[ti + 2] = vi + sideVertCount + 2;
                triangles[ti + 3] = vi;
                triangles[ti + 4] = vi + sideVertCount + 2;
                triangles[ti + 5] = vi + sideVertCount + 1;
            }
        }

        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        normals = mesh.normals;
    }
    private void CreateCollisionPlane() {
        Cmesh = new Mesh();
        Cmesh.name = "CollisionPlane";

        heightBuffer.GetData(heightMap);
        maxheight=0;

        vertices = new Vector3[vert_num * vert_num];
        Vector2[] uv = new Vector2[vertices.Length];
        Vector4[] tangents = new Vector4[vertices.Length];
        Vector4 tangent = new Vector4(1f, 0f, 0f, -1f);
        
        for (int i = 0, x = 0; x <= sideVertCount; ++x) {
            for (int z = 0; z <= sideVertCount; ++z, ++i) {
                vertices[i] = new Vector3(((float)x / sideVertCount * planeSize) - halfLength, heightMap[z*resN+x]*100, 
                                          ((float)z / sideVertCount * planeSize) - halfLength);
                                          if (maxheight<heightMap[z*resN+x]*100)
                                          {
                                            maxheight=heightMap[z*resN+x]*100;
                                          }

                uv[i] = new Vector2((float)x / sideVertCount, (float)z / sideVertCount);
                tangents[i] = tangent;
            }
        }

        Cmesh.vertices = vertices;
        Cmesh.uv = uv;
        Cmesh.tangents = tangents;
        triangles = new int[sideVertCount * sideVertCount * 6];
        
        for (int ti = 0, vi = 0, x = 0; x < sideVertCount; ++vi, ++x) {
            for (int z = 0; z < sideVertCount; ti += 6, ++vi, ++z) {
                triangles[ti + 0] = vi;
                triangles[ti + 1] = vi + 1;
                triangles[ti + 2] = vi + sideVertCount + 2;
                triangles[ti + 3] = vi;
                triangles[ti + 4] = vi + sideVertCount + 2;
                triangles[ti + 5] = vi + sideVertCount + 1;
            }
        }

        Cmesh.SetTriangles(triangles, 0);
        Cmesh.RecalculateNormals();
        Cmesh.RecalculateBounds();
        GetComponent<MeshCollider>().sharedMesh = Cmesh;
    }

        private void Createvegetation(){
        vegetation_kernel = vegetationComputeShader.FindKernel("TerrainOffsets");
        terrainVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, sizeof(float) * 3);
        terrainVertexBuffer.SetData(vertices);
        vegetationComputeShader.SetBuffer(vegetation_kernel, "_TerrainPositions", terrainVertexBuffer);


        terrainTriangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangles.Length, sizeof(int));
        terrainTriangleBuffer.SetData(triangles);
        vegetationComputeShader.SetBuffer(vegetation_kernel, "_TerrainTriangles", terrainTriangleBuffer);
        terrainTriangleCount = triangles.Length / 3;

        Vector3[] vegetationVertices = vegetationMesh.vertices;
        vegetationVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vegetationVertices.Length, sizeof(float) * 3);
        vegetationVertexBuffer.SetData(vegetationVertices);

        int[] vegetationTriangles = vegetationMesh.triangles;
        vegetationTriangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vegetationTriangles.Length, sizeof(int));
        vegetationTriangleBuffer.SetData(vegetationTriangles);

        Vector2[] vegetationUVs = vegetationMesh.uv;
        vegetationUVBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vegetationUVs.Length, sizeof(float) * 2);
        vegetationUVBuffer.SetData(vegetationUVs);

        transformMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, terrainTriangleCount, sizeof(float) * 16);
        vegetationComputeShader.SetBuffer(vegetation_kernel, "_TransformMatrices", transformMatrixBuffer);
        
        bounds = mesh.bounds;
        bounds.center += transform.position;
        bounds.Expand(minMaxHeight.y);

        vegetationComputeShader.SetMatrix("_TerrainObjectToWorld", transform.localToWorldMatrix);
        vegetationComputeShader.SetInt("_TerrainTriangleCount", terrainTriangleCount);
        vegetationComputeShader.SetVector("_MinMaxHeight", minMaxHeight);
        vegetationComputeShader.SetFloat("_Scale", scale);
        
        uint threadGroupSize;
        vegetationComputeShader.GetKernelThreadGroupSizes(vegetation_kernel, out threadGroupSize, out _, out _);
        int threadGroups = Mathf.CeilToInt(terrainTriangleCount / threadGroupSize);

        vegetationComputeShader.SetFloat("_vegetationDensity", vegetationDensity);
        vegetationComputeShader.SetFloat("_MinimumSlope", vegetationSlope);
        vegetationComputeShader.SetFloat("_minHeight", vegetationHeight/maxheight);
        vegetationComputeShader.Dispatch(vegetation_kernel, threadGroups, 1, 1);
    }


    void CreateMaterial() {
        if (TerrainShader == null) return;
        objMaterial = new Material(TerrainShader);
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        renderer.material = objMaterial;
    }

    int getGridDimFor(int kernelIdx) {
        heightmapComputeShader.GetKernelThreadGroupSizes(kernelIdx, out uint BLOCK_DIM, out _, out _);
        return (int)((resN + (BLOCK_DIM - 1)) / BLOCK_DIM);
    }

    void Start() {
        FractalNoiseCS = heightmapComputeShader.FindKernel("FractalNoiseCS");
        resN = vert_num;
        GRID_DIM = getGridDimFor(FractalNoiseCS);
        computeResult = CreateRenderTex(resN, resN, 1, RenderTextureFormat.Default, true);
        octaveBuffer = new ComputeBuffer(OctaveCount, 3 * sizeof(float) + sizeof(int));
        heightBuffer = new ComputeBuffer(vert_num*vert_num, sizeof(float));
        SetSOctaveBuffers();
        heightmapComputeShader.SetTexture(FractalNoiseCS, "_Result", computeResult);
        heightmapComputeShader.SetBuffer(FractalNoiseCS, "_Collision", heightBuffer);
        heightmapComputeShader.SetInt("_OctaveCount", OctaveCount);
        heightmapComputeShader.SetInt("_N", resN);
        heightmapComputeShader.SetFloat("_BaseFrequency", baseFrequency);
        heightmapComputeShader.Dispatch(FractalNoiseCS, GRID_DIM, GRID_DIM, 1);

        heightBuffer.GetData(heightMap);

        CreatePlaneMesh();
        CreateCollisionPlane();
        Createvegetation();
        CreateMaterial();
        objMaterial.SetTexture("_HeightTex", computeResult);

    }

    void Update() {
        if (updateOctave) {
            SetSOctaveBuffers();
            heightmapComputeShader.SetTexture(FractalNoiseCS, "_Result", computeResult);
            heightmapComputeShader.SetFloat("_BaseFrequency", baseFrequency);
            heightmapComputeShader.Dispatch(FractalNoiseCS, GRID_DIM, GRID_DIM, 1);
            
            vegetationComputeShader.SetVector("_MinMaxHeight", minMaxHeight);
            vegetationComputeShader.SetFloat("_Scale", scale);

            uint threadGroupSize;
            vegetationComputeShader.GetKernelThreadGroupSizes(vegetation_kernel, out threadGroupSize, out _, out _);
            int threadGroups = Mathf.CeilToInt(terrainTriangleCount / threadGroupSize);

            vegetationComputeShader.SetFloat("_Density", vegetationDensity);
            vegetationComputeShader.SetFloat("_MinimumSlope", vegetationSlope);
            vegetationComputeShader.SetFloat("_minHeight", vegetationHeight);
            vegetationComputeShader.Dispatch(vegetation_kernel, threadGroups, 1, 1);
        }
        if(reCalcCollision){
            if (Cmesh != null) {
                Destroy(Cmesh);
                Cmesh = null;
                vertices = null;
                normals = null;
            }
            CreateCollisionPlane();
            reCalcCollision = false;
            Createvegetation();
        }
        objMaterial.SetTexture("_AlbedoTex", albedoTex);
        objMaterial.SetTexture("_HighAlbedoTex", higher_albedoTex);
        objMaterial.SetFloat("_MinY", minY);
        objMaterial.SetFloat("_MaxY", maxY);
        objMaterial.SetFloat("_UV", uvMap);
        objMaterial.SetFloat("_Metallic", metalic);
        objMaterial.SetFloat("_Subsurface", subsurface);
        objMaterial.SetFloat("_Specular", specular);
        objMaterial.SetFloat("_Roughness", roughness);
        objMaterial.SetFloat("_SpecularTint", specularTint);
        objMaterial.SetFloat("_Anisotropic", anisotropic);
        objMaterial.SetFloat("_Sheen", sheen);
        objMaterial.SetFloat("_SheenTint", sheenTint);
        objMaterial.SetFloat("_ClearCoat", clearCoat);
        objMaterial.SetFloat("_ClearCoatGloss", clearCoatGloss);
    
        RenderParams rp = new RenderParams(vegetationMaterial);
        rp.worldBounds = bounds;
        rp.matProps = new MaterialPropertyBlock();
        rp.matProps.SetBuffer("_TransformMatrices", transformMatrixBuffer);
        rp.matProps.SetBuffer("_Positions", vegetationVertexBuffer);
        rp.matProps.SetBuffer("_UVs", vegetationUVBuffer);
        Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, vegetationTriangleBuffer, vegetationTriangleBuffer.count, instanceCount: terrainTriangleCount);

    
    
    }

    void OnDisable() {
        if (objMaterial != null) {
            Destroy(objMaterial);
            objMaterial = null;
        }

        if (mesh != null) {
            Destroy(mesh);
            mesh = null;
            vertices = null;
            normals = null;
        }

        if (Cmesh != null) {
            Destroy(Cmesh);
            Cmesh = null;
            vertices = null;
            normals = null;
        }
        Destroy(computeResult);
        octaveBuffer.Dispose();
        heightBuffer.Dispose();
        terrainTriangleBuffer.Dispose();
        terrainVertexBuffer.Dispose();
        transformMatrixBuffer.Dispose();
        vegetationTriangleBuffer.Dispose();
        vegetationVertexBuffer.Dispose();
        vegetationUVBuffer.Dispose();
    }
}
