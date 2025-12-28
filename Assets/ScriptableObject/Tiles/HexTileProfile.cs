using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "RTS/Hex Tile Profile")]
public class HexTileProfile : ScriptableObject
{
    [Header("默认地形配置")]
    [Tooltip("当格子不是路时，显示这个模型 (防止格子消失)")]
    public GameObject defaultGrassPrefab;
    [Tooltip("当格子是路但找不到匹配形状时，显示这个模型 (防止断裂成草地)")]
    public GameObject defaultPathPrefab;

    [System.Serializable]
    public class TileDefinition {
        public string name;         // 方便你自己看 (比如: Straight, Turn)
        public GameObject prefab;   // 模型
        public int canonicalMask;   // 标准掩码 (如: 9, 3, 21)
    }

    [Header("路径规则配置")]
    public List<TileDefinition> tileDefinitions;

    // 运行时缓存字典，提升查找速度
    private Dictionary<int, GameObject> _lookupTable;

    /// <summary>
    /// 初始化字典 (运行时自动调用)
    /// </summary>
    public void Initialize() {
        _lookupTable = new Dictionary<int, GameObject>();
        foreach (var def in tileDefinitions) {
            if (!_lookupTable.ContainsKey(def.canonicalMask)) {
                _lookupTable.Add(def.canonicalMask, def.prefab);
            }
        }
    }

    /// <summary>
    /// 根据掩码查找模型和旋转
    /// </summary>
    public bool GetPrefabAndRotation(int currentMask, out GameObject prefab, out float rotationY) {
        if (_lookupTable == null) Initialize();

        // 尝试旋转 6 次来匹配标准掩码
        // r=0: 不转, r=1: 顺时针转60度...
        for (int r = 0; r < 6; r++) {
            if (_lookupTable.TryGetValue(currentMask, out GameObject foundPrefab)) {
                prefab = foundPrefab;
                rotationY = r * 60f; // 找到了！记录需要的旋转角度
                return true;
            }
            // 没找到？把掩码向"右"转一格 (数据顺时针旋转)
            currentMask = RotateMaskClockwise(currentMask);
        }

        prefab = null;
        rotationY = 0;
        return false;
    }

    /// <summary>
    /// 获取该掩码的"标准形式" (所有旋转中数值最小的那个)
    /// 用于方便Debug缺失的形状
    /// </summary>
    public int GetCanonicalMask(int mask) {
        int minMask = mask;
        int current = mask;
        for (int i = 0; i < 6; i++) {
            if (current < minMask) minMask = current;
            current = RotateMaskClockwise(current);
        }
        return minMask;
    }

    // 二进制位移操作：模拟六边形旋转
    // 将最低位(Index 0) 移到 最高位(Index 5)
    public int RotateMaskClockwise(int mask) {
        int lastBit = mask & 1;
        int shifted = mask >> 1;
        if (lastBit == 1) shifted |= 32; // 32 = 100000 (Index 5)
        return shifted;
    }
}