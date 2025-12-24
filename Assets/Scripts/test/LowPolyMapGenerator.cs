using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LowPolyMapGenerator : MonoBehaviour {
    [Header("Generation Settings")]
    public int seed = 12345;
    public int mapSize = 50;
    public float noiseScale = 0.1f;
    public float heightMultiplier = 5f;

    [Header("Graph Settings")]
    public List<Vector2> buildingPoints = new List<Vector2>();
    public float flatRadius = 3f; // 建筑周围平坦区域半径

    void Start() {
        GenerateMap();
    }

    void GenerateMap() {
        Random.InitState(seed);
        Mesh mesh = new Mesh();
        
        // 1. 模拟几个建筑点 (实际可以用泊松采样生成)
        buildingPoints.Clear();
        for(int i = 0; i < 5; i++) {
            buildingPoints.Add(new Vector2(Random.Range(5, mapSize-5), Random.Range(5, mapSize-5)));
        }

        // 2. 生成顶点和高度
        List<Vector3> vertices = new List<Vector3>();
        int[] triangles = new int[(mapSize - 1) * (mapSize - 1) * 6];

        for (int z = 0; z < mapSize; z++) {
            for (int x = 0; x < mapSize; x++) {
                float xCoord = x * noiseScale;
                float zCoord = z * noiseScale;
                float height = Mathf.PerlinNoise(xCoord + seed, zCoord + seed) * heightMultiplier;

                // 核心逻辑：如果是建筑点周围，强制平整
                foreach (Vector2 p in buildingPoints) {
                    float dist = Vector2.Distance(new Vector2(x, z), p);
                    if (dist < flatRadius) {
                        // 使用平滑插值，让平地和山脉过渡自然
                        float t = Mathf.SmoothStep(0, 1, dist / flatRadius);
                        height = Mathf.Lerp(1.0f, height, t); // 1.0f 是平地高度
                    }
                }
                vertices.Add(new Vector3(x, height, z));
            }
        }

        // 3. 构建三角形 (为了低多边形效果，每个三角面不共享顶点)
        // 注意：标准 Grid 生成会平滑，真正的 Low Poly 需要重新组织顶点为 Flat Shading
        GetComponent<MeshFilter>().mesh = CreateFlatShadedMesh(vertices, mapSize);
    }

    Mesh CreateFlatShadedMesh(List<Vector3> gridVerts, int size) {
        Mesh flatMesh = new Mesh();
        List<Vector3> newVerts = new List<Vector3>();
        List<int> newTris = new List<int>();

        for (int z = 0; z < size - 1; z++) {
            for (int x = 0; x < size - 1; x++) {
                // 获取格子的四个角
                Vector3 v1 = gridVerts[z * size + x];
                Vector3 v2 = gridVerts[(z + 1) * size + x];
                Vector3 v3 = gridVerts[z * size + x + 1];
                Vector3 v4 = gridVerts[(z + 1) * size + x + 1];

                // 三角形1
                newVerts.Add(v1); newVerts.Add(v2); newVerts.Add(v3);
                // 三角形2
                newVerts.Add(v3); newVerts.Add(v2); newVerts.Add(v4);
            }
        }

        for (int i = 0; i < newVerts.Count; i++) newTris.Add(i);

        flatMesh.vertices = newVerts.ToArray();
        flatMesh.triangles = newTris.ToArray();
        flatMesh.RecalculateNormals(); // 这是 Low Poly 棱角感的关键
        return flatMesh;
    }
}