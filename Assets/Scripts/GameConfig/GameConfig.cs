using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 全局游戏配置类，用于管理常量和静态数据
/// </summary>
public static class GameConfig {
    // --- 颜色配置 ---

    // 玩家颜色定义 (0=Red, 1=Blue, 2=Green, 3=Yellow)
    // 注意：可根据实际需求调整顺序
    private static readonly Color[] PlayerColors = new Color[] {
        Color.red,      // ID 0
        Color.blue,     // ID 1
        Color.green,    // ID 2
        Color.yellow,   // ID 3
        Color.cyan,     // ID 4
        Color.magenta   // ID 5
    };

    // 中立颜色 (ID 999)
    public static readonly Color NeutralColor = Color.gray;

    /// <summary>
    /// 根据玩家 ID 获取对应颜色
    /// </summary>
    /// <param name="playerId">玩家 ClientId</param>
    /// <returns>对应的颜色</returns>
    public static Color GetPlayerColor(ulong playerId) {
        // 1. 检查中立 ID
        if (playerId == 999) {
            return NeutralColor;
        }

        // 2. 检查数组越界
        if (playerId < (ulong)PlayerColors.Length) {
            return PlayerColors[playerId];
        }

        // 3. 默认返回中立颜色或最后一种颜色
        Debug.LogWarning($"[GameConfig] Player ID {playerId} 超出颜色配置范围，使用默认颜色。");
        return NeutralColor;
    }

    // --- 兵种注册表 (Registry) ---

    private static Dictionary<int, TroopData> _troopLibrary;

    /// <summary>
    /// 初始化兵种库
    /// </summary>
    public static void InitializeLibrary(List<TroopData> dataList) {
        _troopLibrary = new Dictionary<int, TroopData>();
        if (dataList == null) return;

        foreach (var data in dataList) {
            if (data != null && !_troopLibrary.ContainsKey(data.troopID)) {
                _troopLibrary.Add(data.troopID, data);
            } else {
                Debug.LogWarning($"[GameConfig] 重复或无效的 TroopID: {data?.troopID}");
            }
        }
        Debug.Log($"[GameConfig] 兵种库初始化完成，共加载 {_troopLibrary.Count} 个兵种。");
    }

    /// <summary>
    /// 根据 ID 获取兵种数据
    /// </summary>
    public static TroopData GetTroopData(int id) {
        if (_troopLibrary != null && _troopLibrary.TryGetValue(id, out var data)) {
            return data;
        }
        Debug.LogError($"[GameConfig] 未找到 ID 为 {id} 的兵种数据！");
        return null;
    }
}
