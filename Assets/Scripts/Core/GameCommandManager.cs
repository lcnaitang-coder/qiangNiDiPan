using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// 统一命令管理器
/// 负责处理所有客户端发起的 Gameplay 操作请求 (RPC)
/// </summary>
public class GameCommandManager : NetworkBehaviour {
    
    public static GameCommandManager Singleton { get; private set; }

    [Header("Configuration")]
    [SerializeField] private GameObject defaultTroopHostPrefab; 

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        // 场景级单例，跟随场景销毁，不需要 DontDestroyOnLoad
    }

    public override void OnNetworkSpawn() {
        // 注册对象池
        if (NetworkObjectPool.Singleton != null && defaultTroopHostPrefab != null) {
            NetworkObjectPool.Singleton.RegisterPrefab(defaultTroopHostPrefab);
        }
    }

    // --- Public API for Clients ---

    public void RequestSendTroops(ulong fromBuildingId, ulong toBuildingId) {
        SendTroopsServerRpc(fromBuildingId, toBuildingId);
    }

    public void RequestUpgradeBuilding(ulong buildingId) {
        UpgradeBuildingServerRpc(buildingId);
    }

    // --- Server RPCs ---

    [ServerRpc(RequireOwnership = false)]
    private void SendTroopsServerRpc(ulong fromId, ulong toId, ServerRpcParams rpcParams = default) {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        // 1. 获取建筑实例
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fromId, out NetworkObject fromObj) &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(toId, out NetworkObject toObj)) {
            
            Building sourceBuilding = fromObj.GetComponent<Building>();

            // 2. 验证权限
            if (sourceBuilding == null || sourceBuilding.ownerId.Value != senderClientId) {
                Debug.LogWarning($"[GameCommand] 玩家 {senderClientId} 尝试操作非己方建筑 {fromId}");
                return;
            }

            // 3. 计算出兵数量 (当前兵力的一半)
            int totalToSpawn = sourceBuilding.currentSoldiers.Value / 2;
            
            if (totalToSpawn > 0) {
                // 扣除兵力
                sourceBuilding.currentSoldiers.Value -= totalToSpawn;

                // 获取兵种数据
                TroopData troopData = null;
                if (sourceBuilding.data != null && sourceBuilding.data.troopData != null) {
                    troopData = sourceBuilding.data.troopData;
                } else {
                    Debug.LogError("[GameCommand] 建筑配置缺失兵种数据");
                    return;
                }

                // 开启出兵协程
                StartCoroutine(SpawnTroopsRoutine(sourceBuilding.transform.position, toId, senderClientId, totalToSpawn, troopData));
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpgradeBuildingServerRpc(ulong buildingId, ServerRpcParams rpcParams = default) {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(buildingId, out NetworkObject buildingObj)) {
            Building building = buildingObj.GetComponent<Building>();
            
            if (building == null) return;

            // 验证所有权
            if (building.ownerId.Value != senderClientId) {
                Debug.LogWarning($"[GameCommand] 玩家 {senderClientId} 尝试升级非己方建筑 {buildingId}");
                return;
            }

            // 执行升级逻辑
            building.Upgrade();
        }
    }

    // --- Helper Coroutines ---

    private IEnumerator SpawnTroopsRoutine(Vector3 startPos, ulong targetId, ulong ownerId, int totalCount, TroopData data) {
        int spawnedCount = 0;
        int batchSize = 5; 

        while (spawnedCount < totalCount) {
            int currentBatch = Mathf.Min(batchSize, totalCount - spawnedCount);
            
            if (defaultTroopHostPrefab != null) {
                // 从对象池获取
                NetworkObject troopNetObj = NetworkObjectPool.Singleton.GetNetworkObject(defaultTroopHostPrefab, startPos, Quaternion.identity);
                
                if (troopNetObj != null) {
                    troopNetObj.Spawn(true);

                    Troop troopScript = troopNetObj.GetComponent<Troop>();
                    if (troopScript != null) {
                        troopScript.Initialize(targetId, ownerId, data, currentBatch);
                    }
                }
            } else {
                Debug.LogError("[GameCommand] defaultTroopHostPrefab is null!");
            }

            spawnedCount += currentBatch;
            yield return new WaitForSeconds(0.2f);
        }
    }
}
