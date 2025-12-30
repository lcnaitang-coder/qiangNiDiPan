using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using System.Linq; // Add LINQ namespace

public class HexGridManager : NetworkBehaviour
{
    public static HexGridManager Singleton { get; private set; }

    [Header("配置")]
    public int width = 10;
    public int height = 10;
    public float hexSize = 1f; // 你的模型半径
    public HexTileProfile profile; // 拖入配置文件

    [Header("地图数据 (可选)")]
    public HexMapData currentMapData; // 当前加载或要保存的地图数据

    [Header("建筑系统")]
    public List<BuildingData> availableBuildings; // 建筑数据注册表 (用于通过ID查找)
    public BuildingData selectedBuildingData; // 当前编辑器选中的建筑
    [Tooltip("放置建筑时使用的默认 Owner ID (999=中立, 0,1,2...=玩家)")]
    public ulong placementOwnerId = 999;
    
    public enum EditorMode { Path, Building, Obstacle }
    public EditorMode currentMode = EditorMode.Path;

    [Header("地块容器")]
    public GameObject cellPrefab; // 一个空物体Prefab，挂载 HexCell 脚本
    public GameObject buildingLogicPrefab; // 建筑逻辑预制体 (必须挂载 Building 和 NetworkObject)

    [Header("放置设置")]
    public LayerMask placementLayerMask = ~0; // 默认检测所有层
    public float verticalOffset = 0f; // 垂直偏移量

    // 数据存储
    private Dictionary<Vector2Int, HexCell> _cells = new Dictionary<Vector2Int, HexCell>();
    // Public getter for cells
    public Dictionary<Vector2Int, HexCell> Cells => _cells;

    private void Awake() {
        if (Application.isPlaying) {
            var netObj = GetComponent<NetworkObject>();
            if (netObj != null) {
                netObj.DestroyWithScene = true;
            }
        }

        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;

        EnsureNetworkPrefabsRegistered();
        EnsureBuildingsRoot();
    }

    private void OnDestroy() {
        if (Singleton == this) Singleton = null;
    }

    private Transform _buildingsRoot;
    private bool _networkPrefabsRegistered;

    private void EnsureNetworkPrefabsRegistered() {
        if (_networkPrefabsRegistered) return;
        _networkPrefabsRegistered = true;

        if (!Application.isPlaying) return;
        if (NetworkManager.Singleton == null) return;
        if (buildingLogicPrefab == null) return;

        try {
            NetworkManager.Singleton.AddNetworkPrefab(buildingLogicPrefab);
        } catch {
        }
    }

    private void EnsureBuildingsRoot() {
        if (_buildingsRoot != null) return;

        Transform found = transform.Find("BuildingsRoot");
        if (found != null && found.GetComponent<NetworkObject>() == null) {
            _buildingsRoot = found;
            return;
        }

        var rootObj = new GameObject("BuildingsRoot");
        rootObj.transform.SetParent(transform, false);
        _buildingsRoot = rootObj.transform;
    }

    void Start() {
        // 如果存在 BattleMapManager，则由它接管地图加载，自己不自动加载
        if (Application.isPlaying && BattleMapManager.Singleton != null) {
            return;
        }

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

    /// <summary>
    /// 注册格子 (供 HexCell OnNetworkSpawn 调用)
    /// </summary>
    public void RegisterCell(Vector2Int coords, HexCell cell) {
    if (_cells.ContainsKey(coords)) {
        // 如果已经存在旧格子
        if (_cells[coords] != null && _cells[coords] != cell) {
            var oldGo = _cells[coords].gameObject;
            var netObj = oldGo.GetComponent<NetworkObject>(); // [Check] 检查是否有网络组件
            
            // [Fix] 如果是客户端，且旧物体是网络对象，绝对不能销毁！
            if (netObj != null && !IsServer) {
                // Debug.Log($"[Client] Skipping destroy of NetworkObject at {coords}");
            } 
            else {
                // 只有本地物体，或者我是服务器且需要清理时，才能销毁
                Destroy(oldGo);
            }
        }
        _cells[coords] = cell;
    } else {
        _cells.Add(coords, cell);
    }
    
    if (cell.transform.parent != transform) {
        cell.transform.SetParent(transform);
    }
}
    private void CreateCell(int x, int z, float xOffset, float zOffset) {
        // 修改：客户端和服务端都可以生成本地 HexCell (因为现在 HexCell 是纯 MonoBehaviour)
        
        // 六边形坐标偏移计算 (Odd-R 布局)
        float xPos = x * xOffset;
        if (z % 2 != 0) xPos += xOffset * 0.5f;
        float zPos = z * zOffset;
        
        Vector3 cellPos = new Vector3(xPos, 0, zPos);
        
        // 贴地检测
        if (Physics.Raycast(cellPos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, placementLayerMask)) {
            cellPos.y = hit.point.y;
        }

        var go = Instantiate(cellPrefab, cellPos, Quaternion.identity, transform);
        go.name = $"Cell {x},{z}";
        
        // 移除 HexCell 的 NetworkObject 相关逻辑
        // var netObj = go.GetComponent<NetworkObject>(); ...
        
        var cell = go.GetComponent<HexCell>();
        if (cell == null) cell = go.AddComponent<HexCell>();
        
        // 先 Setup 设置坐标 (Setup 会调用 RegisterCell)
        cell.Setup(this, x, z);
    }

    public void ClearGrid() {
        Transform buildingsRoot = _buildingsRoot != null ? _buildingsRoot : transform.Find("BuildingsRoot");
        if (buildingsRoot != null) {
            for (int i = buildingsRoot.childCount - 1; i >= 0; i--) {
                Transform childTr = buildingsRoot.GetChild(i);
                GameObject child = childTr.gameObject;

                var netObj = child.GetComponent<NetworkObject>();
                if (netObj != null) {
                    if (Application.isPlaying && !IsServer) {
                        continue;
                    }
                    if (IsServer && netObj.IsSpawned) {
                        netObj.Despawn();
                        continue;
                    }
                }

                if (Application.isPlaying) {
                    Destroy(child);
                } else {
                    DestroyImmediate(child);
                }
            }
        }

        int childCount = transform.childCount;
        for (int i = childCount - 1; i >= 0; i--) {
            Transform childTr = transform.GetChild(i);
            if (buildingsRoot != null && childTr == buildingsRoot) continue;

            GameObject child = childTr.gameObject;

            var netObj = child.GetComponentInChildren<NetworkObject>(true);
            if (netObj != null) {
                if (Application.isPlaying && !IsServer) {
                    continue;
                }
                if (IsServer && netObj.IsSpawned) {
                    netObj.Despawn();
                    continue;
                }
            }

            if (Application.isPlaying) {
                Destroy(child);
            } else {
                DestroyImmediate(child);
            }
        }

        _cells.Clear();
}

    // --- 建筑生成系统 ---

    /// <summary>
    /// 请求在指定位置生成建筑 (客户端 -> 服务端)
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SpawnBuildingServerRpc(Vector2Int coords, int buildingDataIndex, ulong ownerId) {
        if (availableBuildings == null || buildingDataIndex < 0 || buildingDataIndex >= availableBuildings.Count) return;
        SpawnBuilding(coords, availableBuildings[buildingDataIndex], ownerId);
    }

    /// <summary>
    /// 在指定位置生成建筑 (服务端权威逻辑，支持离线编辑)
    /// </summary>
    public Building SpawnBuilding(Vector2Int coords, BuildingData data, ulong ownerId) {
        if (Application.isPlaying && !IsServer) {
            return null;
        }
        
        if (!_cells.TryGetValue(coords, out HexCell cell) || cell == null) return null;
        
        // 检查位置是否已有建筑
        if (cell.currentBuilding != null) {
            Debug.LogWarning($"[HexGridManager] 位置 {coords} 已有建筑: {cell.currentBuilding.name}");
            return null;
        }

        if (buildingLogicPrefab == null) {
            Debug.LogError("[HexGridManager] Building Logic Prefab 未配置");
            return null;
        }

        // ... (计算 spawnPos)
        Vector3 spawnPos = cell.transform.position;
        
        // 移除不确定的 Raycast，使用确定性的偏移计算
        float modelOffset = data != null ? data.modelVerticalOffset : 0f;
        spawnPos.y = cell.transform.position.y + modelOffset + verticalOffset;

        GameObject buildingObj = Instantiate(buildingLogicPrefab, spawnPos, Quaternion.identity);
        
        Building building = buildingObj.GetComponent<Building>();
        if (building == null) {
            Debug.LogError("Building Logic Prefab 缺少 Building 组件");
            Destroy(buildingObj);
            return null;
        }

        // --- 服务端 或 离线模式 ---
        
        // 1. 初始化数据
        building.data = data;
        building.OwnerId = ownerId; // 设置 _offlineOwnerId

        EnsureBuildingsRoot();

        if (Application.isPlaying) {
            NetworkObject netObj = buildingObj.GetComponent<NetworkObject>();
            if (netObj == null) {
                Destroy(buildingObj);
                return null;
            }
            netObj.Spawn(true);
        } else {
            buildingObj.transform.SetParent(_buildingsRoot, true);
        }

        // 绑定到格子
        building.Setup(cell);
        
        return building;
    }

    // --- 保存与加载系统 ---

    public void SaveToMapData() {
#if UNITY_EDITOR
        if (currentMapData == null) {
            Debug.LogError("请先在 Inspector 中创建一个 HexMapData 并赋值给 Current Map Data。");
            return;
        }

        currentMapData.Initialize(width, height);
        currentMapData.buildings.Clear(); // 清空旧数据

        // --- 1. 保存地形 (遍历场景中所有 HexCell) ---
        // 即使 _cells 字典丢失，场景里的 HexCell 还在
        HexCell[] allCells = FindObjectsOfType<HexCell>();
        
        foreach (var cell in allCells) {
            // 确保坐标正确（防止某些 Cell 没初始化）
            if (cell.coordinates == Vector2Int.zero && cell.transform.position != Vector3.zero) {
                 // 尝试反算坐标
                 cell.coordinates = WorldToHexCoords(cell.transform.InverseTransformPoint(cell.transform.position));
                 // 注意：这里 InverseTransformPoint 可能有问题，因为 WorldToHexCoords 期望的是相对于 Grid 的本地坐标
                 // 如果 Cell 是 Grid 的子物体，cell.transform.localPosition 就是我们要的
                 // 如果不是，我们需要 transform.InverseTransformPoint(cell.transform.position)
                 cell.coordinates = WorldToHexCoords(transform.InverseTransformPoint(cell.transform.position));
            }
            
            // 存入 MapData (注意边界检查)
            if (cell.coordinates.x >= 0 && cell.coordinates.x < width && 
                cell.coordinates.y >= 0 && cell.coordinates.y < height) {
                currentMapData.SetTile(cell.coordinates.x, cell.coordinates.y, cell.isPath);
            }
        }

        // --- 2. 保存建筑 (遍历场景中所有 Building) ---
        // 即使引用链断裂，场景里的 Building 还在
        Building[] allBuildings = FindObjectsOfType<Building>();
        
        foreach (var b in allBuildings) {
            // 忽略未激活或将被销毁的对象
            if (b == null) continue;

            // 计算坐标：优先使用 Building 自己记录的坐标，如果不可信则用位置反算
            Vector2Int coords;
            
            // 如果是在编辑器里刚生成的，GridPosition 可能没值（因为 NetworkVariable 在非运行模式下不工作）
            // 所以我们总是优先信任物理位置反算，或者使用我们新加的 _offlineGridPosition
            if (Application.isPlaying) {
                 coords = b.GridPosition;
            } else {
                 // 编辑器模式下，反算坐标最稳
                 coords = WorldToHexCoords(transform.InverseTransformPoint(b.transform.position));
            }

            if (b.data != null) {
                HexMapData.BuildingSaveData bData = new HexMapData.BuildingSaveData {
                    x = coords.x,
                    z = coords.y,
                    buildingTypeId = b.data.buildingTypeId,
                    ownerId = b.OwnerId,
                    level = b.Level,
                    soldiers = b.Soldiers
                };
                currentMapData.buildings.Add(bData);
            }
        }
        
        UnityEditor.EditorUtility.SetDirty(currentMapData);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"地图已保存到: {currentMapData.name} (含 {currentMapData.buildings.Count} 个建筑, {allCells.Length} 个地块)");
#endif
    }

    public override void OnNetworkSpawn() {
        if (IsServer && currentMapData != null) {
            SpawnBuildingsFromMapData();
        }
    }

    public event System.Action OnGridReady;

    public void UnloadMap() {
        ClearGrid();
        // 确保清空字典，防止残留
        _cells.Clear();
    }

    public void LoadMap(HexMapData data) {
        if (data == null) return;
        
        // 0. 清理旧地图
        UnloadMap();
        
        width = data.width;
        height = data.height;
        currentMapData = data;

        float xOffset = Mathf.Sqrt(3) * hexSize;
        float zOffset = 1.5f * hexSize;

        // 1. 生成所有 HexCell (服务端和客户端各自生成本地地形)
        for (int z = 0; z < height; z++) {
            for (int x = 0; x < width; x++) {
                CreateCell(x, z, xOffset, zOffset);
            }
        }

        // 2. 恢复地形状态 (isPath)
        for (int z = 0; z < height; z++) {
            for (int x = 0; x < width; x++) {
                if (_cells.TryGetValue(new Vector2Int(x, z), out HexCell cell)) {
                    bool isPath = data.GetTile(x, z);
                    cell.isPath = isPath; 
                    // 暂时不更新视觉，等 Pre-marking 完成
                }
            }
        }

        // 3. Pre-marking (预占位) - 关键修复
        // 在生成建筑对象前，先将对应的 Cell 标记为 Building 类型
        // 这样道路在 UpdateVisuals 时就能检测到"未来会有建筑"，从而正确连接
        if (data.buildings != null) {
            foreach (var bData in data.buildings) {
                if (_cells.TryGetValue(new Vector2Int(bData.x, bData.z), out HexCell cell)) {
                    cell.cellType = HexCell.HexCellType.Building;
                    // 注意：此时 cell.currentBuilding 还是 null，但 cellType 已经是 Building
                }
            }
        }

        // 4. 批量更新视觉 (此时路径会根据 cellType 正确连接)
        foreach (var cell in _cells.Values) {
            cell.UpdateVisuals();
        }

        // 5. 生成建筑实体 (仅服务端)
        // 客户端不生成，而是等待网络同步或 BindExistingBuildings
        if (!Application.isPlaying || IsServer) {
            SpawnBuildingsFromMapData();
        }
        
        // 6. 客户端修正：尝试绑定已存在的网络建筑
        if (!IsServer && Application.isPlaying) {
            BindExistingBuildings();
        }

        Debug.Log($"地图加载完成: {data.name}");
        
        // 7. 触发事件通知 Building 等待者
        OnGridReady?.Invoke();
    }

    private void BindExistingBuildings() {
        Building[] buildings = FindObjectsOfType<Building>();
        Debug.Log($"[HexGridManager] BindExistingBuildings found {buildings.Length} buildings.");
        foreach (var b in buildings) {
            if (b != null && b.IsSpawned) {
                // 重新绑定位置 (这会修正 transform.position 并调用 Setup)
                b.BindToGrid(b.GridPosition);
            }
        }
    }

    public void SpawnBuildingsFromMapData() {
        if (currentMapData == null || currentMapData.buildings == null) return;
        if (Application.isPlaying && !IsServer) return;

        foreach (var bData in currentMapData.buildings) {
            // 查找 BuildingData
            BuildingData bd = availableBuildings.Find(b => b.buildingTypeId == bData.buildingTypeId);
            if (bd != null) {
                Building building = SpawnBuilding(new Vector2Int(bData.x, bData.z), bd, bData.ownerId);
                
                // 恢复等级和兵力
                if (building != null) {
                     building.SetLoadState(bData.level, bData.soldiers);
                }
            } else {
                Debug.LogWarning($"[LoadMap] Unknown building type: {bData.buildingTypeId}");
            }
        }
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
            HexCell neighbor = neighbors[i];
            if (neighbor == null) continue;

            // 1. 如果邻居是路 -> 连接
            if (neighbor.isPath) {
                mask |= (1 << i);
            }
            // 2. 如果邻居是建筑 -> 连接（任意方向）
            // 关键修改：检查 cellType 而不是 currentBuilding，支持 Pre-marking
            else if (neighbor.cellType == HexCell.HexCellType.Building) {
                // 回退到全方向连接，因为寻路逻辑现在会智能选择入口
                // 如果路铺到了建筑旁边，视觉上就应该连上去
                mask |= (1 << i);
            }
        }
        return mask;
    }

    [ContextMenu("Auto Connect Paths")]
    public void AutoConnectBuildings() {
        if (_cells == null || _cells.Count == 0) return;
        
        if (profile == null) {
            Debug.LogError("AutoConnect Failed: 'Profile' is not assigned in HexGridManager! Please assign a HexTileProfile.");
            return;
        }
        if (profile.defaultPathPrefab == null) {
            Debug.LogWarning("AutoConnect Warning: 'Default Path Prefab' is missing in the assigned Profile.");
        }

        // 1. 收集所有建筑
        List<Building> allBuildings = new List<Building>();
        foreach (var cell in _cells.Values) {
            if (cell.currentBuilding != null && !allBuildings.Contains(cell.currentBuilding)) {
                // Ensure the building knows its position (crucial for Editor/Offline mode)
                cell.currentBuilding.Setup(cell);
                allBuildings.Add(cell.currentBuilding);
            }
        }

        if (allBuildings.Count == 0) return;

        // 2. 区分玩家建筑和中立建筑 (999 为中立)
        List<Building> neutralBuildings = new List<Building>();
        List<Building> playerBuildings = new List<Building>();

        foreach (var b in allBuildings) {
            if (b.OwnerId == 999) {
                neutralBuildings.Add(b);
            } else {
                playerBuildings.Add(b);
            }
        }

        Debug.Log($"AutoConnect: Found {neutralBuildings.Count} Neutral, {playerBuildings.Count} Player buildings.");

        // 3. 中立建筑互连 (Connect to closest 2 other neutrals)
        foreach (var b in neutralBuildings) {
            var sortedNeutrals = neutralBuildings
                .Where(n => n != b)
                .OrderBy(n => Vector2Int.Distance(n.GridPosition, b.GridPosition))
                .Take(2)
                .ToList();

            foreach (var target in sortedNeutrals) {
                GeneratePathBetween(b.GridPosition, target.GridPosition);
            }
        }

        // 4. 玩家建筑连接到最近的中立建筑
        foreach (var p in playerBuildings) {
            var closestNeutral = neutralBuildings
                .OrderBy(n => Vector2Int.Distance(n.GridPosition, p.GridPosition))
                .FirstOrDefault();

            if (closestNeutral != null) {
                GeneratePathBetween(p.GridPosition, closestNeutral.GridPosition);
            } else {
                Debug.LogWarning($"Player building at {p.GridPosition} has no neutral buildings to connect to!");
            }
        }

        // 5. 统一刷新视觉
        foreach (var cell in _cells.Values) {
            cell.UpdateVisuals();
        }
        Debug.Log("Auto connect complete.");
    }

    // 辅助方法：寻找最佳出入口 (优先南向，否则找任意可用邻居)
    private Vector2Int? GetBestGate(Vector2Int buildingPos) {
        // 1. 优先尝试南向 (0, -1)
        Vector2Int southPos = buildingPos + new Vector2Int(0, -1);
        if (IsValidGate(southPos)) {
            return southPos;
        }

        // 2. 尝试其他所有邻居
        var neighbors = GetNeighborCoords(buildingPos);
        foreach (var pos in neighbors) {
            if (pos == southPos) continue; // 已经检查过了
            if (IsValidGate(pos)) {
                return pos;
            }
        }

        return null;
    }

    // 检查该位置是否适合作为出入口 (是路，或者空地，且不是建筑)
    private bool IsValidGate(Vector2Int pos) {
        if (!_cells.TryGetValue(pos, out HexCell cell)) return false;
        // 如果该位置有建筑，不能作为出入口 (除非它是自己，但逻辑上邻居不可能是自己)
        if (cell.currentBuilding != null) return false;
        
        // 它是空地或者已经是路，都可以
        return true; 
    }

    // 辅助方法：生成两点之间的路径并标记
    private void GeneratePathBetween(Vector2Int startBuildingPos, Vector2Int endBuildingPos) {
        if (startBuildingPos == endBuildingPos) return;

        // 寻找最佳出入口
        Vector2Int? startGate = GetBestGate(startBuildingPos);
        Vector2Int? endGate = GetBestGate(endBuildingPos);

        if (startGate == null || endGate == null) {
             Debug.LogWarning($"Cannot connect {startBuildingPos} to {endBuildingPos}: No valid gateways found (surrounded by obstacles?).");
             return;
        }

        Debug.Log($"Generating path from {startBuildingPos}(Gate:{startGate}) to {endBuildingPos}(Gate:{endGate})");

        List<Vector2Int> path = FindPath(startGate.Value, endGate.Value);
        if (path != null && path.Count > 0) {
            int changedCount = 0;
            foreach (var pos in path) {
                if (_cells.TryGetValue(pos, out HexCell cell)) {
                    // 只要不是建筑本身，就设为路
                    if (cell.currentBuilding == null) {
                        if (!cell.isPath) {
                            cell.isPath = true;
                            changedCount++;
                        }
                    }
                }
            }
            Debug.Log($"Path generated. Length: {path.Count}. Changed {changedCount} cells to Path.");
        } else {
             Debug.LogWarning($"Failed to find path between gateways {startGate} and {endGate}");
        }
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
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // 方案 C: 物理碰撞检测 (最精准，解决了远距离偏移问题)
        // 需确保 HexCell 预制体上有 Collider (BoxCollider 或 MeshCollider)
        if (Physics.Raycast(ray, out hit, 1000f, placementLayerMask)) {
            HexCell cell = null;

            // 1. 尝试获取 HexCell 组件
            // 注意：这里可能获取到多个 HexCell (例如子物体预制体里自带了未初始化的 HexCell)
            // 我们需要找到那个真正属于 Grid 管理的 HexCell
            HexCell[] foundCells = hit.collider.GetComponentsInParent<HexCell>();
            foreach (var c in foundCells) {
                if (c != null && _cells.TryGetValue(c.coordinates, out HexCell registeredCell)) {
                    // 关键校验：必须是同一个实例！
                    // 未初始化的 HexCell 坐标通常是 (0,0)，如果不校验实例，会导致所有点击都指向 (0,0)
                    if (registeredCell == c) {
                        cell = c;
                        break;
                    }
                }
            }
            
            // 2. 关键修复：如果打中的是已经存在的建筑，则尝试获取其所在的 HexCell
            if (cell == null) {
                // 尝试从建筑回溯到 Cell (假设建筑是 Cell 的子物体，或者位置重合)
                // 由于目前架构中建筑并不是 Cell 的子物体，而是与 Cell 重合放置
                // 所以如果点到了建筑的 Collider，我们需要通过坐标反算 Cell
                
                // 尝试获取 Building 组件
                Building building = hit.collider.GetComponentInParent<Building>();
                if (building != null && building.IsSpawned) {
                     // 如果点到了建筑，获取建筑所在的坐标
                     Vector2Int bCoords = building.gridPosition.Value;
                     if (_cells.TryGetValue(bCoords, out HexCell bCell)) {
                         cell = bCell;
                     }
                }
            }

            // 3. 如果还是没找到，可能点到了建筑模型但没挂 Building 组件，或者位置偏差
            // 尝试直接用击中点坐标反算
            if (cell == null) {
                 Vector3 localPoint = transform.InverseTransformPoint(hit.point);
                 Vector2Int coords = WorldToHexCoords(localPoint);
                 // 增加边界检查，防止反算出的坐标越界导致 TryGetValue 失败
                 if (_cells.TryGetValue(coords, out HexCell foundCell)) {
                     cell = foundCell;
                 } 
                 // 之前这里的 else 分支已被移除，继续让它自然回退
            }
            
            if (cell != null) {
                Vector2Int coords = cell.coordinates;
                // Debug.Log($"[Raycast Hit] Hit Collider: {hit.collider.name}, Found Cell: {coords}");
                
                switch (currentMode) {
                    case EditorMode.Path:
                        HandlePathInput(coords);
                        break;
                    case EditorMode.Building:
                        HandleBuildingInput(coords);
                        break;
                    // case EditorMode.Obstacle: ...
                }
                return;
            } else {
                 // 如果物理检测失败（比如打中了Collider但找不到Cell脚本），强制回退到数学平面检测
                 // 不要在这里直接 return，让它往下走到数学平面逻辑
                 // Debug.LogWarning($"[Raycast Hit] Hit {hit.collider.name} but no HexCell found!");
            }
        }

        // 降级方案: 如果没打中 Collider，尝试数学平面 (作为后备)
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float enter)) {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector3 localPoint = transform.InverseTransformPoint(hitPoint);
            Vector2Int coords = WorldToHexCoords(localPoint);
            
            if (_cells.TryGetValue(coords, out HexCell cell)) {
                 switch (currentMode) {
                    case EditorMode.Path:
                        HandlePathInput(coords);
                        break;
                    case EditorMode.Building:
                        HandleBuildingInput(coords);
                        break;
                }
            }
        }
    }

    private void HandlePathInput(Vector2Int coords) {
        // 如果是第一次点击，记录为起点
        if (_startPathCell == null) {
            _startPathCell = coords;
            Debug.Log($"Path Start Selected: {coords}");
            // 移除直接修改 isPath 的逻辑，避免破坏现有路径
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
    }

    private void HandleBuildingInput(Vector2Int coords) {
        if (selectedBuildingData == null) {
            Debug.LogWarning("请先在 Inspector 中赋值 Selected Building Data");
            return;
        }

        // 如果该位置已有建筑，则不做处理 (或者可以做销毁逻辑)
        if (_cells[coords].currentBuilding != null) {
            Debug.Log("该位置已有建筑");
            return;
        }

        if (IsServer) {
            // 服务端直接生成 (默认为中立 999，或者可以扩展 UI 来选 Owner)
            SpawnBuilding(coords, selectedBuildingData, placementOwnerId);
        } else {
            // 客户端发送 RPC 请求
            if (availableBuildings != null) {
                int index = availableBuildings.IndexOf(selectedBuildingData);
                if (index != -1) {
                    // 检查网络状态
                    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) {
                         // 离线模式：直接在本地生成
                         SpawnBuilding(coords, selectedBuildingData, placementOwnerId);
                    } else {
                         // 在线模式：请求归属于自己
                         SpawnBuildingServerRpc(coords, index, NetworkManager.Singleton.LocalClientId);
                    }
                } else {
                    Debug.LogWarning("Selected Building Data 不在 Available Buildings 列表中，无法通过 RPC 发送索引。");
                }
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
                if (!_cells.TryGetValue(next, out HexCell nextCell)) continue; // 必须在网格内且有效

                // 障碍物检测：
                // 如果该格子有建筑，且它不是终点，则视为不可通行
                if (nextCell.currentBuilding != null && next != end) {
                    continue; 
                }
                
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
