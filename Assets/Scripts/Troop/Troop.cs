using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Troop : NetworkBehaviour {
    // NetworkVariables for syncing state to clients automatically
    private NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<ulong> netOwnerId = new NetworkVariable<ulong>();
    public NetworkVariable<int> netTroopCount = new NetworkVariable<int>(1); // Sync troop count for visuals
    public NetworkVariable<int> troopTypeId = new NetworkVariable<int>(-1); // Sync troop type for visual prefab

    [Header("Visual Settings")]
    [SerializeField] private float clusterRadius = 0.5f;

    // --- Optimization: Visual Pooling ---
    private class VisualItem {
        public GameObject gameObject;
        public Renderer[] renderers;
    }
    private List<VisualItem> _activeItems = new List<VisualItem>();
    private Queue<VisualItem> _itemPool = new Queue<VisualItem>();
    private int _lastTypeId = -1;
    // ------------------------------------

    // Server-side only data for logic
    private ulong targetBuildingId;
    private float moveSpeed;
    private int troopCount;
    private int attackPower;

    public override void OnNetworkSpawn() {
        // 1. Hide Parent Visuals: Disable the "logic" sphere renderer
        Renderer parentRenderer = GetComponent<Renderer>();
        if (parentRenderer != null) parentRenderer.enabled = false;

        if (IsServer) {
            // Server initializes position
            netPosition.Value = transform.position;
        } else {
            // Client snaps to current network position immediately to avoid visual artifacts
            transform.position = netPosition.Value;
        }

        // 2. Remove Host Restriction: Run SpawnVisuals on all clients (including Host)
        SpawnVisuals();

        // Apply initial visual state based on current value
        UpdateColor(netOwnerId.Value);

        // Subscribe to changes
        netOwnerId.OnValueChanged += (oldVal, newVal) => {
            UpdateColor(newVal);
        };
        
        // 5. Sync Fix: Remove !IsServer check
        netTroopCount.OnValueChanged += (oldVal, newVal) => {
            SpawnVisuals();
        };

        // Listen for type changes to spawn correct model
        troopTypeId.OnValueChanged += (oldVal, newVal) => {
            SpawnVisuals();
        };
    }

    public override void OnNetworkDespawn() {
        // 当 Troop 被回收到对象池时，也需要回收它的视觉子对象
        foreach (var item in _activeItems) {
            if (item.gameObject != null) {
                item.gameObject.SetActive(false);
                _itemPool.Enqueue(item);
            }
        }
        _activeItems.Clear();
        
        base.OnNetworkDespawn();
    }

    private void SpawnVisuals() {
        int newTypeId = troopTypeId.Value;
        // If type is invalid, don't spawn
        if (newTypeId == -1) return;

        // 1. Check for Type Change -> Reset Pool
        if (newTypeId != _lastTypeId) {
            foreach (var item in _activeItems) Destroy(item.gameObject);
            foreach (var item in _itemPool) Destroy(item.gameObject);
            _activeItems.Clear();
            _itemPool.Clear();
            _lastTypeId = newTypeId;
        }

        // 2. Recycle Active to Pool
        foreach (var item in _activeItems) {
            item.gameObject.SetActive(false);
            _itemPool.Enqueue(item);
        }
        _activeItems.Clear();

        // 3. Get Prefab
        TroopData data = GameConfig.GetTroopData(newTypeId);
        GameObject prefabToSpawn = (data != null) ? data.visualPrefab : null;

        int count = netTroopCount.Value;
        // 4. Relax Count Limit: Increase to 20
        int visualCount = Mathf.Min(count, 20); 

        for (int i = 0; i < visualCount; i++) {
            VisualItem item;
            
            // Try reuse from pool
            if (_itemPool.Count > 0) {
                item = _itemPool.Dequeue();
            } else {
                // Create new
                GameObject model;
                if (prefabToSpawn != null) {
                    model = Instantiate(prefabToSpawn, transform);
                } else {
                    model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    model.transform.SetParent(transform);
                    Destroy(model.GetComponent<Collider>()); 
                }
                
                // Cache renderers for color updates
                item = new VisualItem {
                    gameObject = model,
                    renderers = model.GetComponentsInChildren<Renderer>()
                };
            }
            
            // Reset Transform
            item.gameObject.SetActive(true);
            Vector3 randomOffset = Random.insideUnitSphere * clusterRadius;
            randomOffset.y = 0; // Keep on ground plane relative to parent
            item.gameObject.transform.localPosition = randomOffset;
            item.gameObject.transform.localScale = Vector3.one * 0.3f; // Make them small
            item.gameObject.transform.localRotation = Quaternion.identity;

            _activeItems.Add(item);
        }
        
        // Apply color to new visuals
        UpdateColor(netOwnerId.Value);
    }

    // Initialize the troop (Server side only)
    public void Initialize(ulong targetId, ulong ownerId, TroopData data, int count) {
        if (!IsServer) return;
        if (data == null) return;

        targetBuildingId = targetId;
        moveSpeed = data.moveSpeed;
        troopCount = count;
        attackPower = data.attackPower;
        
        // Set NetworkVariables (will sync to clients)
        netOwnerId.Value = ownerId;
        netPosition.Value = transform.position;
        netTroopCount.Value = count;
        troopTypeId.Value = data.troopID;
    }

    // Cached PropertyBlock to avoid GC and VRAM allocation
    private static MaterialPropertyBlock _propBlock;
    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor"); // For URP

    void UpdateColor(ulong id) {
        // 使用全局配置获取颜色
        Color color = GameConfig.GetPlayerColor(id);

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        // Apply to all child renderers (the visual cluster) using cached list
        // Optimization: Use _activeItems instead of GetComponentsInChildren
        foreach (var item in _activeItems) {
            if (item.renderers != null) {
                foreach (var r in item.renderers) {
                    if (r == null) continue;
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorPropId, color);
                    _propBlock.SetColor(BaseColorPropId, color);
                    r.SetPropertyBlock(_propBlock);
                }
            }
        }
        
        // Also apply to self if self has renderer (legacy support)
        Renderer selfRend = GetComponent<Renderer>();
        if (selfRend != null) {
            selfRend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(ColorPropId, color);
            _propBlock.SetColor(BaseColorPropId, color);
            selfRend.SetPropertyBlock(_propBlock);
        }
    }

    void Update() {
        if (IsServer) {
            // --- SERVER LOGIC ---
            // Find the target building
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetBuildingId, out NetworkObject targetObj)) {
                Vector3 targetPos = targetObj.transform.position;
                
                // Move towards target
                float step = moveSpeed * Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, targetPos, step);
                
                // Update NetworkVariable
                netPosition.Value = transform.position;

                // Check distance
                if (Vector3.Distance(transform.position, targetPos) < 0.1f) {
                    Building building = targetObj.GetComponent<Building>();
                    if (building != null) {
                        // Trigger arrival logic
                        building.OnTroopArrive(netOwnerId.Value, troopCount, attackPower);
                    }
                    // Destroy self
                    NetworkObject.Despawn();
                }
            } else {
                // Target destroyed or missing, despawn troop
                NetworkObject.Despawn();
            }
        } 
        else {
            // --- CLIENT LOGIC ---
            // Interpolate position for smoothness (or just set it for simplicity)
            transform.position = Vector3.Lerp(transform.position, netPosition.Value, Time.deltaTime * 10f);
        }
    }
}
