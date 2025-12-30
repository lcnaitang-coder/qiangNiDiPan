using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CastleData", menuName = "Game/BuildingData")]
public class BuildingData : ScriptableObject {
    [Header("Identity")]
    public string buildingTypeId; // 用于存档/读档的唯一标识符 (例如: "Barracks", "Castle")

    [Tooltip("用于修正模型的轴心点高度偏差（例如：如果模型轴心在中心，需要填入高度的一半）")]
    public float modelVerticalOffset = 0f;

    [System.Serializable]
    public class LevelStats {
        public int maxCapacity;
        public float productionRate; // Units per second
        public GameObject visualModelPrefab; // 每一级独特的外观预制体
    }

    [Header("Building Configuration")]
    public List<LevelStats> levels = new List<LevelStats>();

    [Header("Troop Configuration")]
    public TroopData troopData; // The type of troop this building produces

    // Defaults if list is empty
    public int GetMaxCapacity(int level) {
        if (levels == null || levels.Count == 0) return 10 * level;
        int index = Mathf.Clamp(level - 1, 0, levels.Count - 1);
        return levels[index].maxCapacity;
    }

    public float GetProductionRate(int level) {
        if (levels == null || levels.Count == 0) return 1.0f;
        int index = Mathf.Clamp(level - 1, 0, levels.Count - 1);
        return levels[index].productionRate;
    }

    /// <summary>
    /// 获取指定等级的建筑外观预制体
    /// </summary>
    /// <param name="level">基于1的等级索引</param>
    /// <returns>外观预制体</returns>
    public GameObject GetVisualModel(int level) {
        if (levels == null || levels.Count == 0) return null;
        int index = Mathf.Clamp(level - 1, 0, levels.Count - 1);
        return levels[index].visualModelPrefab;
    }
}