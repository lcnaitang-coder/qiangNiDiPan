using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 通用网络对象池 (Singleton)，实现 INetworkPrefabInstanceHandler 接口。
/// 支持多 Prefab 管理，通过 GameObject 引用区分队列。
/// </summary>
public class NetworkObjectPool : MonoBehaviour, INetworkPrefabInstanceHandler {
    
    public static NetworkObjectPool Singleton { get; private set; }

    [Header("Pool Configuration")]
    [Tooltip("需要进行池化管理的 Prefab (必须包含 NetworkObject 组件)")]
    [SerializeField] private List<NetworkObject> pooledPrefabs = new List<NetworkObject>();
    
    [Tooltip("每个 Prefab 的初始池大小")]
    [SerializeField] private int initialPoolSize = 50;

    // 核心数据结构：Prefab -> 对象池队列
    private Dictionary<GameObject, Queue<NetworkObject>> pooledObjects = new Dictionary<GameObject, Queue<NetworkObject>>();
    
    // 辅助映射：GlobalObjectIdHash -> Prefab (用于 Netcode 内部回调查找)
    private Dictionary<uint, NetworkObject> _hashToPrefab = new Dictionary<uint, NetworkObject>();

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() {
        if (NetworkManager.Singleton != null) {
            RegisterToNetworkManager();
        } else {
            Debug.LogWarning("[NetworkObjectPool] NetworkManager not found at Start.");
        }
    }

    /// <summary>
    /// 手动初始化并注册池子
    /// </summary>
    public void InitializePool() {
        RegisterToNetworkManager();
    }

    /// <summary>
    /// 注册所有配置的 Prefab
    /// </summary>
    private void RegisterToNetworkManager() {
        if (NetworkManager.Singleton == null) return;

        foreach (var prefab in pooledPrefabs) {
            if (prefab == null) continue;
            RegisterPrefab(prefab.gameObject, initialPoolSize);
        }
    }

    /// <summary>
    /// 动态注册 Prefab 到池子
    /// </summary>
    public void RegisterPrefab(GameObject prefab, int initialSize = 20) {
        if (prefab == null) return;
        
        var netObj = prefab.GetComponent<NetworkObject>();
        if (netObj == null) {
            Debug.LogError($"[NetworkObjectPool] Prefab {prefab.name} missing NetworkObject!");
            return;
        }

        // 防止重复注册
        if (pooledObjects.ContainsKey(prefab)) return;

        // 1. 初始化队列
        pooledObjects[prefab] = new Queue<NetworkObject>();

        // 2. 记录 Hash 映射 (供 Handler 使用)
        uint hash = netObj.PrefabIdHash;
        _hashToPrefab[hash] = netObj;

        // 3. 预先填充
        for (int i = 0; i < initialSize; i++) {
            NetworkObject instance = Instantiate(netObj, Vector3.zero, Quaternion.identity, transform);
            instance.gameObject.SetActive(false);
            // 注意：这里初始生成时设置父节点是安全的，因为对象还未被 Netcode 管理
            // 但一旦开始使用，就不再修改父节点
            pooledObjects[prefab].Enqueue(instance);
        }

        // 4. 注册 Handler (必须!)
        if (NetworkManager.Singleton != null) {
            // 使用我们自定义的 Handler
            var handler = new PooledPrefabInstanceHandler(netObj, this);
            try {
                // 如果已经有 Handler 则先移除（防止热重载报错）
                // NetworkManager.Singleton.PrefabHandler.RemoveHandler(netObj); 
                NetworkManager.Singleton.PrefabHandler.AddHandler(netObj, handler);
                Debug.Log($"[NetworkObjectPool] Registered handler for {prefab.name} (Hash: {hash})");
            } catch (System.Exception e) {
                Debug.LogWarning($"[NetworkObjectPool] Failed to register handler for {prefab.name}: {e.Message}");
            }
        }
    }

    public void OnDestroy() {
        if (NetworkManager.Singleton != null) {
            foreach (var kvp in pooledObjects) {
                if (kvp.Key != null) {
                    var netObj = kvp.Key.GetComponent<NetworkObject>();
                    if (netObj != null) {
                        NetworkManager.Singleton.PrefabHandler.RemoveHandler(netObj);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 从池中获取对象 (逻辑层调用)
    /// </summary>
    public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation) {
        if (!pooledObjects.ContainsKey(prefab)) {
            Debug.LogWarning($"[NetworkObjectPool] Prefab {prefab.name} not registered! Registering now...");
            RegisterPrefab(prefab);
        }
        
        return GetNetworkObjectInternal(prefab, position, rotation);
    }

    /// <summary>
    /// 内部获取逻辑 (核心)
    /// </summary>
    private NetworkObject GetNetworkObjectInternal(GameObject prefab, Vector3 position, Quaternion rotation) {
        Queue<NetworkObject> queue = pooledObjects[prefab];
        NetworkObject instance = null;

        // 1. 尝试从队列取
        while (queue.Count > 0) {
            instance = queue.Dequeue();
            if (instance != null) break;
        }

        // 2. 如果池空了，新建一个
        if (instance == null) {
            var netObj = prefab.GetComponent<NetworkObject>();
            // 注意：新创建的对象也不要设置父节点，让它保持在根节点，避免后续麻烦
            instance = Instantiate(netObj, position, rotation);
        }

        // 3. 重置状态
        instance.transform.position = position;
        instance.transform.rotation = rotation;
        instance.gameObject.SetActive(true);

        // 4. 彻底移除 SetParent(null) 
        // 遵循 "No-Reparenting" 原则，无论它现在在哪（通常在根节点或 Pool 节点下），都不去动它
        // 这样可以避免 "NetworkObject can only be reparented after being spawned" 错误

        return instance;
    }

    /// <summary>
    /// 归还对象到池子 (由 Handler 调用)
    /// </summary>
    public void ReturnNetworkObject(NetworkObject networkObject, GameObject prefab) {
        if (networkObject == null || prefab == null) return;

        // 1. 重置并隐藏
        networkObject.gameObject.SetActive(false);

        // 2. 彻底移除 SetParent(transform) 和 _reparentQueue
        // 让对象留在原地（无论是根节点还是之前的父节点），只管隐藏和入队

        // 3. 入队
        if (!pooledObjects.ContainsKey(prefab)) {
            // 理论上不应该发生，除非 Prefab 被销毁了
            pooledObjects[prefab] = new Queue<NetworkObject>();
        }
        pooledObjects[prefab].Enqueue(networkObject);
    }

    // --- INetworkPrefabInstanceHandler 接口实现 (供 Netcode 回调) ---
    
    // 注意：Netcode 调用此方法时只给 hash，我们需要查找对应的 Prefab
    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation) {
        throw new System.NotImplementedException("Global Instantiate not supported. Use PooledPrefabInstanceHandler.");
    }

    public void Destroy(NetworkObject networkObject) {
        throw new System.NotImplementedException("Global Destroy not supported. Use PooledPrefabInstanceHandler.");
    }
    
    // 公开给 Handler 使用的查找方法
    public NetworkObject GetPrefabByHash(uint hash) {
        if (_hashToPrefab.TryGetValue(hash, out var prefab)) {
            return prefab;
        }
        return null;
    }
}

/// <summary>
/// 辅助类：为每个 Prefab 绑定对应的 Pool 逻辑
/// </summary>
public class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler {
    private NetworkObject _prefab;
    private NetworkObjectPool _pool;

    public PooledPrefabInstanceHandler(NetworkObject prefab, NetworkObjectPool pool) {
        _prefab = prefab;
        _pool = pool;
    }

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation) {
        // Client 端逻辑：从池中获取
        // 注意：这里的 _prefab 就是当初注册 Handler 时绑定的那个 Prefab
        return _pool.GetNetworkObject(_prefab.gameObject, position, rotation);
    }

    public void Destroy(NetworkObject networkObject) {
        // Client/Server 端逻辑：回收进池
        _pool.ReturnNetworkObject(networkObject, _prefab.gameObject);
    }
}
