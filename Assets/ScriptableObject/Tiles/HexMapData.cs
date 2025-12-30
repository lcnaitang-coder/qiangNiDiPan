using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewMapData", menuName = "RTS/Map Data")]
public class HexMapData : ScriptableObject
{
    public string mapName;
    public int width;
    public int height;
    
    // 存储地图格子数据
    // 我们使用一维数组来存储二维网格数据，索引 index = z * width + x
    // true = 路, false = 草
    [HideInInspector]
    public bool[] pathStates;

    // --- 建筑数据 ---
    [System.Serializable]
    public struct BuildingSaveData {
        public int x;
        public int z;
        public string buildingTypeId;
        public ulong ownerId;
        public int level;
        public int soldiers;
    }

    [HideInInspector]
    public List<BuildingSaveData> buildings = new List<BuildingSaveData>();

    public void Initialize(int w, int h) {
        width = w;
        height = h;
        pathStates = new bool[w * h];
        buildings.Clear();
    }

    public void SetTile(int x, int z, bool isPath) {
        if (x < 0 || x >= width || z < 0 || z >= height) return;
        pathStates[z * width + x] = isPath;
    }

    public bool GetTile(int x, int z) {
        if (x < 0 || x >= width || z < 0 || z >= height) return false;
        if (pathStates == null || pathStates.Length == 0) return false;
        return pathStates[z * width + x];
    }
}
