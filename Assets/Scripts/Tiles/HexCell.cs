using UnityEngine;
using Unity.Netcode; // 保留引用以防万一，但主要转为 MonoBehaviour

public class HexCell : MonoBehaviour
{
    public enum HexCellType { Empty, Path, Building, Obstacle }

    [Header("Debug Info")]
    public Vector2Int coordinates; // 网格坐标 (x, z) - 本地缓存
    public bool isPath = false;    // 状态：是路还是草 - 本地缓存
    
    [Header("Cell State")]
    public HexCellType cellType = HexCellType.Empty;
    public Building currentBuilding { get; private set; }

    private HexGridManager _grid;

    /// <summary>
    /// 初始化设置 (由 HexGridManager 调用)
    /// </summary>
    public void Setup(HexGridManager grid, int x, int z) {
        _grid = grid;
        coordinates = new Vector2Int(x, z);
        name = $"Cell {coordinates.x},{coordinates.y}";
        
        // 注册到 Grid (本地)
        if (_grid != null) {
            _grid.RegisterCell(coordinates, this);
        }
    }

    private void RefreshStateAndVisuals() {
        // 更新 CellType
        if (isPath) {
            cellType = HexCellType.Path;
        } else if (cellType == HexCellType.Path) {
            // 只有当之前是 Path 现在不是 Path 时，才重置为 Empty
            // 如果之前是 Building，不要覆盖
            cellType = HexCellType.Empty;
        }
        
        UpdateVisuals();
        
        // 通知邻居
        if (_grid != null) {
            foreach(var neighbor in _grid.GetNeighbors(coordinates)) {
                if (neighbor != null) neighbor.UpdateVisuals();
            }
        }
    }

    /// <summary>
    /// 修改路径状态 (供 HexGridManager 或 Editor 调用)
    /// </summary>
    public void SetPathState(bool isPathState) {
        if (isPath != isPathState) {
            isPath = isPathState;
            RefreshStateAndVisuals();
        }
    }

    /// <summary>
    /// 绑定建筑到该格子
    /// </summary>
    public void AssignBuilding(Building building) {
        currentBuilding = building;
        cellType = HexCellType.Building;
        
        // 关键修复：当放置建筑时，通知周围邻居更新视觉
        // 因为邻居的路径可能需要连接到这个新建筑
        if (_grid != null) {
            foreach(var neighbor in _grid.GetNeighbors(coordinates)) {
                if (neighbor != null) {
                    neighbor.UpdateVisuals();
                }
            }
        }
    }

    /// <summary>
    /// 清除该格子的建筑引用
    /// </summary>
    public void ClearBuilding() {
        currentBuilding = null;
        // 恢复状态：如果是路就是路，否则是空
        // 注意：这里必须小心，不要把 Pre-marking 冲掉了。
        // 但通常 ClearBuilding 发生在建筑被销毁时，所以可以重置。
        cellType = isPath ? HexCellType.Path : HexCellType.Empty;

        // 移除建筑时，同样通知邻居更新 (断开连接)
        if (_grid != null) {
            foreach(var neighbor in _grid.GetNeighbors(coordinates)) {
                if (neighbor != null) {
                    neighbor.UpdateVisuals();
                }
            }
        }
    }

    /// <summary>
    /// 核心逻辑：根据状态和周围环境更新模型
    /// </summary>
    public void UpdateVisuals() {
        if (!_grid) _grid = HexGridManager.Singleton;
        if (!_grid || !_grid.profile) return;

        bool isOnlineClient = Application.isPlaying
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && !NetworkManager.Singleton.IsServer;

        // --- 步骤 1: 决定要生成什么 ---
        GameObject prefabToSpawn = null;
        float rotationY = 0;

        if (isPath) {
            // A. 如果是路：计算掩码，查表找形状
            int mask = _grid.GetConnectionMask(coordinates);
            if (_grid.profile.GetPrefabAndRotation(mask, out GameObject foundPrefab, out float rotY)) {
                prefabToSpawn = foundPrefab;
                rotationY = rotY;
            } else {
                // 如果是路但没找到匹配的形状 (比如孤岛)，使用默认路或者报错
                int canonical = _grid.profile.GetCanonicalMask(mask);
                // Debug.LogWarning($"HexCell {coordinates} - No matching path prefab found for mask {mask}.");
                
                // 优先使用 defaultPathPrefab，如果没有则用 defaultGrassPrefab
                prefabToSpawn = _grid.profile.defaultPathPrefab != null ? _grid.profile.defaultPathPrefab : _grid.profile.defaultGrassPrefab;
            }
        } 
        else {
            // B. 如果是草：直接使用默认草地模型
            // 注意：即使是 Building 类型，如果还没有建筑模型覆盖(例如还在Pre-marking阶段)，
            // 地形本身应该显示草地。建筑模型是独立的 GameObject。
            prefabToSpawn = _grid.profile.defaultGrassPrefab;
            rotationY = 0; 
        }

        // --- 步骤 2: 仅在有东西可生成时才替换 ---
        if (prefabToSpawn != null) {
            // 清理旧模型
            foreach (Transform child in transform) {
                if (isOnlineClient && child.GetComponentInChildren<NetworkObject>(true) != null) {
                    continue;
                }
                Destroy(child.gameObject);
            }

            // 实例化新模型
            var instance = Instantiate(prefabToSpawn, transform);
            // 保持局部位置归零，只旋转
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0, rotationY, 0);
        }
    }
}
