using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 战斗地图管理器
/// 负责在游戏开始时加载指定的地图数据，并同步给所有客户端。
/// </summary>
public class BattleMapManager : NetworkBehaviour {
    public static BattleMapManager Singleton { get; private set; }

    [Header("Map Settings")]
    [Tooltip("可供加载的地图列表")]
    public List<HexMapData> availableMaps;
    
    [Tooltip("默认加载的地图索引 (如果没有指定)")]
    public int defaultMapIndex = 0;

    // 同步当前地图索引，-1 表示未加载
    public NetworkVariable<int> currentMapIndex = new NetworkVariable<int>(-1);

    [Header("Debug")]
    [Tooltip("启用此选项可在不启动网络主机的情况下测试地图加载 (仅用于调试)")]
    public bool enableOfflineDebug = false;

    private Coroutine _loadingCoroutine;

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
    }

    private void OnDestroy() {
        if (Singleton == this) Singleton = null;
    }

    private void Start() {
    }

    public override void OnNetworkSpawn() {
        // 监听地图变化（支持运行时切图）
        currentMapIndex.OnValueChanged += OnMapIndexChanged;

        if (IsServer) {
            if (currentMapIndex.Value != -1) {
                LoadMapLocal(currentMapIndex.Value);
            }
        } else {
            // 客户端：如果已有地图索引，立即加载
            if (currentMapIndex.Value != -1) {
                LoadMapLocal(currentMapIndex.Value);
            }
        }
    }

    public void ServerSetInitialMapIndexIfNeeded() {
        if (!IsServer) return;
        if (currentMapIndex.Value != -1) return;
        if (availableMaps == null || availableMaps.Count == 0) return;
        currentMapIndex.Value = defaultMapIndex;
    }

    public override void OnNetworkDespawn() {
        currentMapIndex.OnValueChanged -= OnMapIndexChanged;
    }

    private void OnMapIndexChanged(int oldIndex, int newIndex) {
        if (newIndex != -1) {
             Debug.Log($"[BattleMapManager] Map Index Changed: {oldIndex} -> {newIndex}");
             LoadMapLocal(newIndex);
        }
    }

    private void LoadMapLocal(int mapIndex) {
        if (mapIndex < 0 || mapIndex >= availableMaps.Count) {
            Debug.LogError($"[BattleMapManager] Invalid map index: {mapIndex}");
            return;
        }

        // 启动安全加载协程
        if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
        _loadingCoroutine = StartCoroutine(SafeLoadMapRoutine(availableMaps[mapIndex]));
    }

    private IEnumerator SafeLoadMapRoutine(HexMapData mapData) {
        // 1. 等待 HexGridManager 就绪
        float timeout = 10f;
        float elapsed = 0f;
        while (HexGridManager.Singleton == null) {
            if (elapsed >= timeout) {
                Debug.LogError("[BattleMapManager] SafeLoadMapRoutine Timed out waiting for HexGridManager.");
                yield break;
            }
            elapsed += Time.deltaTime;
            Debug.Log("[BattleMapManager] Waiting for HexGridManager...");
            yield return null;
        }

        // 2. 清理旧建筑 (防止 NetworkObject 残留)
        CleanupExistingBuildings();

        // 3. 调用 GridManager 加载 (包含 Pre-marking 和 本地 HexCell 生成)
        Debug.Log($"[BattleMapManager] Loading map: {mapData.name}");
        HexGridManager.Singleton.LoadMap(mapData);
        
        _loadingCoroutine = null;
    }

    private void CleanupExistingBuildings() {
        // 客户端严禁销毁 NetworkObject
        // 任何带有 NetworkObject 的物体都应该由 Netcode 系统管理 (Server Despawn -> Client Destroy)
        
        var buildings = FindObjectsOfType<Building>();
        foreach (var b in buildings) {
            if (b == null) continue;
            var netObj = b.GetComponent<NetworkObject>();
            
            // 1. 客户端保护：只要有 NetworkObject，一律不碰
            if (netObj != null) {
                if (!IsServer) {
                    // Client: Do nothing. Wait for server synchronization.
                    continue;
                }
                
                // Server: 如果 Spawned，正常 Despawn
                if (netObj.IsSpawned) {
                    netObj.Despawn();
                } 
                // Server: 如果没 Spawned (本地残留)，销毁
                else {
                    Destroy(b.gameObject);
                }
            } 
            // 2. 纯本地对象 (没有 NetworkObject)
            else {
                Destroy(b.gameObject);
            }
        }
    }
}
