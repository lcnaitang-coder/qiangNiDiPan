using UnityEngine;

public class HexCell : MonoBehaviour
{
    [Header("Debug Info")]
    public Vector2Int coordinates; // 网格坐标 (x, z)
    public bool isPath = false;    // 状态：是路还是草

    private HexGridManager _grid;

    /// <summary>
    /// 初始化设置
    /// </summary>
    public void Setup(HexGridManager grid, int x, int z) {
        _grid = grid;
        coordinates = new Vector2Int(x, z);
    }

    /// <summary>
    /// 修改路径状态 (供外部调用)
    /// </summary>
    public void SetPathState(bool isPathState) {
        // 即便是相同状态，我们也可能需要强制刷新（比如邻居变了导致我也要变）
        // 所以这里去掉了 if (this.isPath == isPathState) return; 
        // 但为了性能，只有在值真正改变或者显式要求刷新时才做操作。
        // 不过在路径生成时，我们通常只调用一次。
        
        // 如果状态确实变了，才设置，否则只是刷新视觉
        bool changed = (this.isPath != isPathState);
        this.isPath = isPathState;

        // Safety check
        if (_grid == null) return;

        // 1. 刷新自己
        UpdateVisuals();

        // 2. 通知周围所有邻居刷新
        // 这一步非常关键：因为我变成了路，我的邻居如果也是路，它们的"连接掩码"就会变，模型就需要变
        foreach(var neighbor in _grid.GetNeighbors(coordinates)) {
            if (neighbor != null) {
                // 强制邻居刷新视觉，哪怕它的 isPath 状态没变
                neighbor.UpdateVisuals();
            }
        }
    }

    /// <summary>
    /// 核心逻辑：根据状态和周围环境更新模型
    /// </summary>
    public void UpdateVisuals() {
        if (!_grid || !_grid.profile) return;

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
                Debug.LogWarning($"HexCell {coordinates} (Mask: {mask}, Binary: {System.Convert.ToString(mask, 2)}) - No matching path prefab found! \n" +
                                 $"Suggested Fix: Add a new Tile Definition in HexTileProfile with Canonical Mask = {canonical}.");
                
                // 优先使用 defaultPathPrefab，如果没有则用 defaultGrassPrefab
                prefabToSpawn = _grid.profile.defaultPathPrefab != null ? _grid.profile.defaultPathPrefab : _grid.profile.defaultGrassPrefab;
            }
        } 
        else {
            // B. 如果是草：直接使用默认草地模型
            prefabToSpawn = _grid.profile.defaultGrassPrefab;
            rotationY = 0; 
        }

        // --- 步骤 2: 仅在有东西可生成时才替换 ---
        if (prefabToSpawn != null) {
            // 清理旧模型
            foreach (Transform child in transform) {
                Destroy(child.gameObject);
            }

            // 实例化新模型
            var instance = Instantiate(prefabToSpawn, transform);
            // 保持局部位置归零，只旋转
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0, rotationY, 0);
        } else {
            Debug.LogWarning($"HexCell {coordinates}: No prefab found for state isPath={isPath}. Keeping previous visuals.");
        }
    }
}