using UnityEngine;
using System.Collections.Generic;

public class HexGridManager : MonoBehaviour
{
    [Header("配置")]
    public int width = 10;
    public int height = 10;
    public float hexSize = 1f; // 你的模型半径
    public HexTileProfile profile; // 拖入配置文件

    [Header("地图数据 (可选)")]
    public HexMapData currentMapData; // 当前加载或要保存的地图数据

    [Header("地块容器")]
    public GameObject cellPrefab; // 一个空物体Prefab，挂载 HexCell 脚本

    // 数据存储
    private Dictionary<Vector2Int, HexCell> _cells = new Dictionary<Vector2Int, HexCell>();

    void Start() {
        // 如果有配置地图数据，就加载数据；否则生成默认空网格
        if (currentMapData != null) {
            LoadMap(currentMapData);
        } else {
            GenerateGrid();
        }
    }

    public void GenerateGrid() {
        ClearGrid();
        // 简单的平铺生成
        float xOffset = Mathf.Sqrt(3) * hexSize;
        float zOffset = 1.5f * hexSize;

        for (int z = 0; z < height; z++) {
            for (int x = 0; x < width; x++) {
                CreateCell(x, z, xOffset, zOffset);
            }
        }
    }

    private void CreateCell(int x, int z, float xOffset, float zOffset) {
        // 六边形坐标偏移计算 (Odd-R 布局)
        float xPos = x * xOffset;
        if (z % 2 != 0) xPos += xOffset * 0.5f;
        float zPos = z * zOffset;

        var go = Instantiate(cellPrefab, new Vector3(xPos, 0, zPos), Quaternion.identity, transform);
        go.name = $"Cell {x},{z}";
        
        var cell = go.GetComponent<HexCell>();
        if (cell == null) cell = go.AddComponent<HexCell>();
        
        cell.Setup(this, x, z);
        _cells.Add(new Vector2Int(x, z), cell);
    }

    public void ClearGrid() {
        foreach (Transform child in transform) {
            Destroy(child.gameObject);
        }
        _cells.Clear();
    }

    // --- 保存与加载系统 ---

    public void SaveToMapData() {
#if UNITY_EDITOR
        if (currentMapData == null) {
            Debug.LogError("请先在 Inspector 中创建一个 HexMapData 并赋值给 Current Map Data，或者我将为你创建一个新的。");
            // 自动创建逻辑通常比较复杂，这里建议用户手动创建
            return;
        }

        currentMapData.Initialize(width, height);

        foreach (var kvp in _cells) {
            Vector2Int pos = kvp.Key;
            HexCell cell = kvp.Value;
            currentMapData.SetTile(pos.x, pos.y, cell.isPath);
        }

        UnityEditor.EditorUtility.SetDirty(currentMapData);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"地图已保存到: {currentMapData.name}");
#endif
    }

    public void LoadMap(HexMapData data) {
        if (data == null) return;
        
        ClearGrid();
        
        width = data.width;
        height = data.height;
        currentMapData = data;

        float xOffset = Mathf.Sqrt(3) * hexSize;
        float zOffset = 1.5f * hexSize;

        for (int z = 0; z < height; z++) {
            for (int x = 0; x < width; x++) {
                CreateCell(x, z, xOffset, zOffset);
                
                // 恢复状态
                if (_cells.TryGetValue(new Vector2Int(x, z), out HexCell cell)) {
                    bool isPath = data.GetTile(x, z);
                    cell.isPath = isPath; // 直接赋值状态，不触发UpdateVisuals以节省性能
                }
            }
        }

        // 批量更新视觉，避免生成时的重复刷新
        foreach (var cell in _cells.Values) {
            cell.UpdateVisuals();
        }
        
        Debug.Log($"地图加载完成: {data.name}");
    }

    // 核心数学：获取某坐标周围的邻居
    // 适用于 Pointy-Topped, Odd-R 布局 (偶数行不偏移，奇数行偏移)
    // 关键修正：必须确保这里的顺序与 GetNeighborCoords 完全一致，否则计算掩码时方向会错乱！
    public List<HexCell> GetNeighbors(Vector2Int coords) {
        List<HexCell> neighbors = new List<HexCell>();
        
        // 这里的 directions 必须和 GetNeighborCoords 里的完全一致！
        // 顺序定义 (假设顺时针，从右上角开始):
        // 0: Top-Right, 1: Right, 2: Bottom-Right, 3: Bottom-Left, 4: Left, 5: Top-Left
        
        Vector2Int[] directions;
        if (coords.y % 2 == 0) { // 偶数行 (Even Row)
            directions = new Vector2Int[] {
                new Vector2Int(0, 1),  new Vector2Int(1, 0),  new Vector2Int(0, -1),
                new Vector2Int(-1, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1)
            };
        } else { // 奇数行 (Odd Row)
            directions = new Vector2Int[] {
                new Vector2Int(1, 1),  new Vector2Int(1, 0),  new Vector2Int(1, -1),
                new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 1)
            };
        }

        // 必须按 Index 0-5 的顺序添加，这决定了掩码是否正确
        foreach (var dir in directions) {
            Vector2Int next = coords + dir;
            if (_cells.TryGetValue(next, out HexCell neighbor)) {
                neighbors.Add(neighbor);
            } else {
                neighbors.Add(null); // 边界外也要占位，保证List长度为6
            }
        }
        return neighbors;
    }

    // 计算掩码
    public int GetConnectionMask(Vector2Int coords) {
        var neighbors = GetNeighbors(coords);
        int mask = 0;

        for (int i = 0; i < 6; i++) {
            // 如果邻居存在，且邻居是路 (IsPath)
            if (neighbors[i] != null && neighbors[i].isPath) {
                mask |= (1 << i);
            }
        }
        return mask;
    }

    // --- 测试用：鼠标点击交互 (改进版) ---
    private Vector2Int? _startPathCell = null; // 记录路径起点

    void Update() {
        if (Input.GetMouseButtonDown(0)) {
            HandleInput();
        }
        // 右键取消选择
        if (Input.GetMouseButtonDown(1)) {
            _startPathCell = null;
            Debug.Log("Cancelled path start selection.");
        }
    }

    private void HandleInput() {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        // 方案 B: 数学平面检测 (专业，不需要Collider，点击更精准)
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float enter)) {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector3 localPoint = transform.InverseTransformPoint(hitPoint);
            Vector2Int coords = WorldToHexCoords(localPoint);
            
            if (_cells.TryGetValue(coords, out HexCell cell)) {
                // 如果是第一次点击，记录为起点
                if (_startPathCell == null) {
                    _startPathCell = coords;
                    Debug.Log($"Path Start Selected: {coords}");
                    // 移除直接修改 isPath 的逻辑，避免破坏现有路径
                    // cell.SetPathState(true); 
                    // 这里可以添加一个临时的视觉反馈，比如 Gizmos 或者高亮材质，但不要修改数据
                } 
                // 如果是第二次点击，生成路径
                else {
                    Vector2Int start = _startPathCell.Value;
                    Vector2Int end = coords;
                    
                    if (start == end) {
                        // 点击同一个格子，取消起点
                        _startPathCell = null;
                        Debug.Log("Unselected start cell.");
                    } else {
                        Debug.Log($"Path End Selected: {end}. Generating path...");
                        GenerateRandomPath(start, end);
                        _startPathCell = null; // 重置
                    }
                }
                return;
            }
        }
    }

    /// <summary>
    /// 生成从 start 到 end 的随机路径
    /// </summary>
    private void GenerateRandomPath(Vector2Int start, Vector2Int end) {
        // 1. 获取基础路径 (A*)
        List<Vector2Int> path = FindPath(start, end);
        
        if (path == null) {
            Debug.LogWarning("No path found!");
            return;
        }

        // 2. 批量设置路径状态
        // 关键修复：为了防止"断裂"，我们需要先更新数据，再统一刷新视觉
        // 如果一边改数据一边刷视觉，可能会因为邻居的数据还没更新，导致计算出错误的连接掩码
        
        // 第一步：纯数据更新
        foreach (var pos in path) {
            if (_cells.TryGetValue(pos, out HexCell cell)) {
                cell.isPath = true; // 直接修改字段，不触发 UpdateVisuals
            }
        }

        // 第二步：统一刷新视觉
        // 不仅要刷新路径上的点，还要刷新路径周围的邻居！
        HashSet<HexCell> cellsToUpdate = new HashSet<HexCell>();
        
        foreach (var pos in path) {
            if (_cells.TryGetValue(pos, out HexCell cell)) {
                cellsToUpdate.Add(cell);
                // 把邻居也加入刷新队列
                foreach (var neighbor in GetNeighbors(pos)) {
                    if (neighbor != null) cellsToUpdate.Add(neighbor);
                }
            }
        }

        // 执行刷新
        foreach (var cell in cellsToUpdate) {
            cell.UpdateVisuals();
        }
    }

    /// <summary>
    /// 简单的 A* 寻路或者 BFS，这里使用 BFS 因为权重相同
    /// </summary>
    private List<Vector2Int> FindPath(Vector2Int start, Vector2Int end) {
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        frontier.Enqueue(start);
        
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        cameFrom[start] = start; // 起点标记

        bool found = false;

        while (frontier.Count > 0) {
            Vector2Int current = frontier.Dequeue();

            if (current == end) {
                found = true;
                break;
            }

            // 获取邻居
            // 随机打乱邻居顺序，增加路径的随机性！
            List<Vector2Int> neighbors = GetNeighborCoords(current);
            Shuffle(neighbors); 

            foreach (var next in neighbors) {
                if (!_cells.ContainsKey(next)) continue; // 必须在网格内
                if (!cameFrom.ContainsKey(next)) {
                    frontier.Enqueue(next);
                    cameFrom[next] = current;
                }
            }
        }

        if (!found) return null;

        // 回溯路径
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int curr = end;
        while (curr != start) {
            path.Add(curr);
            curr = cameFrom[curr];
        }
        path.Add(start);
        path.Reverse();
        return path;
    }

    // 获取邻居坐标 (不依赖 HexCell 实例)
    // 必须与 GetNeighbors 保持完全一致的顺序！
    private List<Vector2Int> GetNeighborCoords(Vector2Int coords) {
        List<Vector2Int> list = new List<Vector2Int>();
        Vector2Int[] directions;
        if (coords.y % 2 == 0) { // 偶数行
            directions = new Vector2Int[] {
                new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1),
                new Vector2Int(-1, -1), new Vector2Int(-1, 0), new Vector2Int(-1, 1)
            };
        } else { // 奇数行
            directions = new Vector2Int[] {
                new Vector2Int(1, 1), new Vector2Int(1, 0), new Vector2Int(1, -1),
                new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 1)
            };
        }
        foreach (var dir in directions) list.Add(coords + dir);
        return list;
    }

    // 洗牌算法
    private void Shuffle<T>(List<T> list) {
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = UnityEngine.Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    /// <summary>
    /// 将世界坐标转换为六边形网格坐标 (Pointy-Top, Odd-R)
    /// 参考算法: https://www.redblobgames.com/grids/hexagons/
    /// </summary>
    private Vector2Int WorldToHexCoords(Vector3 position) {
        // 反向计算
        // zPos = z * zOffset => z = zPos / zOffset
        // zOffset = 1.5 * size
        float z = position.z / (1.5f * hexSize);
        
        // 这里的 z 是浮点数，需要圆整。但直接圆整在六边形边缘不准。
        // 更精确的做法是使用 Axial 坐标转换或者像素转换算法。
        // 这里使用简化版近似计算 (Pixel to Hex):
        
        float q = (Mathf.Sqrt(3) / 3f * position.x - 1f / 3f * position.z) / hexSize;
        float r = (2f / 3f * position.z) / hexSize;
        
        return CubeToOddR(CubeRound(q, -q - r, r));
    }

    // 立方体坐标圆整
    private Vector3Int CubeRound(float x, float y, float z) {
        int rx = Mathf.RoundToInt(x);
        int ry = Mathf.RoundToInt(y);
        int rz = Mathf.RoundToInt(z);

        float x_diff = Mathf.Abs(rx - x);
        float y_diff = Mathf.Abs(ry - y);
        float z_diff = Mathf.Abs(rz - z);

        if (x_diff > y_diff && x_diff > z_diff)
            rx = -ry - rz;
        else if (y_diff > z_diff)
            ry = -rx - rz;
        else
            rz = -rx - ry;

        return new Vector3Int(rx, ry, rz);
    }

    // Cube 坐标转 Odd-R 坐标
    private Vector2Int CubeToOddR(Vector3Int cube) {
        int col = cube.x + (cube.z - (cube.z & 1)) / 2;
        int row = cube.z;
        return new Vector2Int(col, row);
    }
}