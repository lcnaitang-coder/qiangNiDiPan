using UnityEngine;
using Unity.Netcode;
using System;

public class Building : NetworkBehaviour {
    [Header("Network State")]
    public NetworkVariable<int> currentSoldiers = new NetworkVariable<int>(10);
    public NetworkVariable<ulong> ownerId = new NetworkVariable<ulong>(999); // 999代表中立
    public NetworkVariable<int> currentLevel = new NetworkVariable<int>(1);
    public NetworkVariable<Vector2Int> gridPosition = new NetworkVariable<Vector2Int>(); // 所在网格坐标
    public NetworkVariable<Unity.Collections.FixedString64Bytes> buildingTypeId = new NetworkVariable<Unity.Collections.FixedString64Bytes>();

    // 离线模式下的后备数据 (也是编辑器中可配置的初始数据)
    [Header("Initial / Offline Settings")]
    [SerializeField] private ulong _offlineOwnerId = 999;
    [SerializeField] private int _offlineLevel = 1;
    [SerializeField] private int _offlineSoldiers = 0;
    
    // GridPosition 不需要编辑器配置，由 HexGridManager 自动设置
    private Vector2Int _offlineGridPosition;

    // 统一访问属性
    public ulong OwnerId {
        get => IsSpawned ? ownerId.Value : _offlineOwnerId;
        set {
            if (IsSpawned && IsServer) ownerId.Value = value;
            else _offlineOwnerId = value;
        }
    }
    
    public int Level {
        get => IsSpawned ? currentLevel.Value : _offlineLevel;
        set {
            if (IsSpawned && IsServer) currentLevel.Value = value;
            else _offlineLevel = value;
        }
    }

    public int Soldiers {
        get => IsSpawned ? currentSoldiers.Value : _offlineSoldiers;
        set {
            if (IsSpawned && IsServer) currentSoldiers.Value = value;
            else _offlineSoldiers = value;
        }
    }

    public Vector2Int GridPosition => IsSpawned ? gridPosition.Value : _offlineGridPosition;

    [Header("Configuration")]
    public BuildingData data;
    
    // 移除 isClientPreview 字段，不再支持客户端本地预览
    // public bool isClientPreview = false;

    // 移除旧的 Initial Setup 字段，避免混淆
    // [Header("Initial Setup")]
    // [SerializeField] private ulong initialOwnerId = 999;
    // [SerializeField] private int initialPlayerLevel = 2;
    // [SerializeField] private int initialNeutralLevel = 1;

    private void OnValidate() {
        // 移除强制覆盖逻辑，允许在 Inspector 直接修改 _offline* 字段
    }

    public override void OnDestroy() {
        // Debug Check: 谁在销毁我？
        if (IsSpawned && !IsServer) {
             Debug.LogError($"[Building] DESTROYED on Client while Spawned! Name: {name}. This will cause a Netcode error. Trace: {System.Environment.StackTrace}");
        }
        base.OnDestroy();
    }
    public event Action<int> OnSoldierCountChanged;
    public event Action<ulong> OnOwnerChanged;
    public event Action<int> OnLevelChanged; // 新增：等级变化事件
    public event Action OnDataLoaded; // 数据加载完成事件

    private float timer;
    private HexCell _currentCell; // 运行时缓存当前绑定的格子，用于清理

    private void OnTypeIdChanged(Unity.Collections.FixedString64Bytes oldVal, Unity.Collections.FixedString64Bytes newVal) {
        LoadData(newVal.ToString());
    }

    private void LoadData(string typeId) {
        if (HexGridManager.Singleton == null || HexGridManager.Singleton.availableBuildings == null) {
            Debug.LogWarning($"[Building] LoadData deferred for {name}. HexGridManager or availableBuildings not ready.");
            return;
        }

        data = HexGridManager.Singleton.availableBuildings.Find(b => b.buildingTypeId == typeId);
        if (data != null) {
            OnDataLoaded?.Invoke();
            // 强制刷新一次视觉
            OnLevelChanged?.Invoke(currentLevel.Value);
        }
    }

    /// <summary>
    /// 初始化：设置坐标并建立引用 (支持服务端和客户端)
    /// </summary>
    public void Setup(HexCell cell) {
        // [新增] 清理旧格子的引用 (防止移动或重绑时残留)
        if (_currentCell != null && _currentCell != cell) {
            _currentCell.ClearBuilding();
        }
        _currentCell = cell;

        if (IsServer) {
            gridPosition.Value = cell.coordinates;
        } else if (!Application.isPlaying) {
            _offlineGridPosition = cell.coordinates;
        }
        
        // 移除客户端预览清理逻辑，因为不再生成预览对象
        // if (cell.currentBuilding != null && cell.currentBuilding != this) { ... }

        // 2. 建立本地引用 (所有端都需要执行，包括客户端)
        // 这会将建筑绑定到 HexCell，并触发视觉更新(如路径连接)
        cell.AssignBuilding(this);
    }

    /// <summary>
    /// 离线/编辑器模式初始化
    /// </summary>
    public void InitOffline(BuildingData data, ulong ownerId) {
        this.data = data;
        
        // 设置离线数据
        _offlineOwnerId = ownerId;
        _offlineLevel = 1;
        _offlineSoldiers = 0;

        // 模拟数据加载
        LoadData(data.buildingTypeId.ToString());
        
        // 触发视觉更新事件
        OnOwnerChanged?.Invoke(_offlineOwnerId);
        // 默认等级1
        OnLevelChanged?.Invoke(_offlineLevel); 
        // 默认兵力0
        OnSoldierCountChanged?.Invoke(_offlineSoldiers);
    }

    /// <summary>
    /// 设置加载后的状态 (通用接口，自动处理在线/离线)
    /// </summary>
    public void SetLoadState(int level, int soldiers) {
        if (IsServer) {
            currentLevel.Value = level;
            currentSoldiers.Value = soldiers;
        } else if (!Application.isPlaying) {
            _offlineLevel = level;
            _offlineSoldiers = soldiers;
            
            // 手动触发事件刷新视图
            OnLevelChanged?.Invoke(_offlineLevel);
            OnSoldierCountChanged?.Invoke(_offlineSoldiers);
        }
    }

    public override void OnNetworkSpawn() {
        // 0. 客户端数据同步 (类型ID)
        buildingTypeId.OnValueChanged += OnTypeIdChanged;
        
        // 监听 GridPosition 变化 (处理移动或初始化同步延迟)
        gridPosition.OnValueChanged += OnGridPositionChanged;

        // 如果已有数据 (Host 或 后加入的客户端)，立即加载
        if (!string.IsNullOrEmpty(buildingTypeId.Value.ToString())) {
            LoadData(buildingTypeId.Value.ToString());
        }

        // 1. 服务器端初始化数据 (使用编辑器配置的初始值)
        if (IsServer) {
            ownerId.Value = _offlineOwnerId;
            currentLevel.Value = _offlineLevel;
            currentSoldiers.Value = _offlineSoldiers;
            
            // 确保同步建筑类型ID
            if (data != null) {
                buildingTypeId.Value = data.buildingTypeId;
            }
        }

        // 2. 绑定网络变量变化回调 (当数值改变 -> 触发本地 C# 事件)
        currentSoldiers.OnValueChanged += (oldVal, newVal) => {
            OnSoldierCountChanged?.Invoke(newVal);
        };

        ownerId.OnValueChanged += (oldVal, newVal) => {
            OnOwnerChanged?.Invoke(newVal);
        };

        // --- 新增：监听等级变化 ---
        currentLevel.OnValueChanged += (oldVal, newVal) => {
            OnLevelChanged?.Invoke(newVal);
        };

        // 3. 立即触发一次当前状态 (确保 UI 初始化时能获取正确数据)
        OnSoldierCountChanged?.Invoke(currentSoldiers.Value);
        OnOwnerChanged?.Invoke(ownerId.Value);
        OnLevelChanged?.Invoke(currentLevel.Value); // 确保 View 知道初始是否满级

        // 4. 智能绑定 (客户端)
        if (!IsServer) {
            // 如果数据还没加载成功 (说明刚才 LoadData 失败了)，尝试订阅重试
            if (data == null && !string.IsNullOrEmpty(buildingTypeId.Value.ToString())) {
                if (HexGridManager.Singleton != null) {
                    HexGridManager.Singleton.OnGridReady += RetryLoadData;
                } else {
                    StartCoroutine(WaitForGridToLoadData());
                }
            }
            
            TryBindOrSubscribe();
        }
    }

    private void RetryLoadData() {
        if (HexGridManager.Singleton != null) {
            HexGridManager.Singleton.OnGridReady -= RetryLoadData;
        }
        if (data == null && !string.IsNullOrEmpty(buildingTypeId.Value.ToString())) {
            LoadData(buildingTypeId.Value.ToString());
            Debug.Log($"[Building-Client] Retried LoadData for {name}: {(data != null ? "Success" : "Failed")}");
        }
    }

    private System.Collections.IEnumerator WaitForGridToLoadData() {
        float timeout = 10f;
        float elapsed = 0f;
        while (HexGridManager.Singleton == null && elapsed < timeout) {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (HexGridManager.Singleton != null) {
            HexGridManager.Singleton.OnGridReady += RetryLoadData;
            // 如果订阅时已经Ready了，手动触发一次
            if (HexGridManager.Singleton.Cells.Count > 0) {
                RetryLoadData();
            }
        } else {
            Debug.LogError($"[Building-Client] WaitForGridToLoadData Timed out for {name}");
        }
    }

    private void TryBindOrSubscribe() {
        // 如果 Grid 就绪且有数据，立即绑定
        if (HexGridManager.Singleton != null && HexGridManager.Singleton.Cells.Count > 0) {
            if (gridPosition.Value != Vector2Int.zero || IsValidGridPos(gridPosition.Value)) {
                BindToGrid(gridPosition.Value);
            }
        } else {
            // 否则订阅 OnGridReady
            Debug.Log($"[Building-Client] Grid not ready. Subscribing to OnGridReady... (Building: {name})");
            if (HexGridManager.Singleton != null) {
                HexGridManager.Singleton.OnGridReady += OnGridReadyCallback;
            } else {
                // 如果 Singleton 甚至都还没好 (极少情况)，可能需要协程等待或者在 Update 里检查
                // 但通常 Building 生成时 GridManager 应该已经 Awake 了
                StartCoroutine(WaitForGridManager());
            }
        }
    }

    private System.Collections.IEnumerator WaitForGridManager() {
        while (HexGridManager.Singleton == null) {
            yield return null;
        }
        HexGridManager.Singleton.OnGridReady += OnGridReadyCallback;
        // 如果订阅时 Grid 刚好好了，OnGridReady 可能不会再触发，所以手动检查一次
        if (HexGridManager.Singleton.Cells.Count > 0) {
             OnGridReadyCallback();
        }
    }

    private void OnGridReadyCallback() {
        // 取消订阅，防止重复调用
        if (HexGridManager.Singleton != null) {
            HexGridManager.Singleton.OnGridReady -= OnGridReadyCallback;
        }
        
        Debug.Log($"[Building-Client] Grid Ready Event Received. Binding... (Building: {name})");
        if (gridPosition.Value != Vector2Int.zero || IsValidGridPos(gridPosition.Value)) {
            BindToGrid(gridPosition.Value);
        }
    }

    public override void OnNetworkDespawn() {
        if (HexGridManager.Singleton != null) {
            HexGridManager.Singleton.OnGridReady -= OnGridReadyCallback;
            HexGridManager.Singleton.OnGridReady -= RetryLoadData;
        }
        base.OnNetworkDespawn();
    }

    private void OnGridPositionChanged(Vector2Int oldVal, Vector2Int newVal) {
        Debug.Log($"[Building-Client] GridPosition synced: {oldVal} -> {newVal}");
        BindToGrid(newVal);
    }

    private bool IsValidGridPos(Vector2Int pos) {
        // 简单校验，假设地图不会小到没有 (0,0)
        return HexGridManager.Singleton != null && HexGridManager.Singleton.Cells.ContainsKey(pos);
    }

    /// <summary>
    /// 客户端专用：根据 GridPosition 绑定到 Cell 并修正位置
    /// </summary>
    public void BindToGrid(Vector2Int coords) {
        // 1. 尝试获取管理器 (增加 fallback)
        if (HexGridManager.Singleton == null) {
            Debug.LogWarning("[Building-Client] HexGridManager.Singleton is null! Trying to find in scene...");
            // 尝试重新查找（有些情况下 Awake 还没执行或者引用丢失）
            var mgr = FindObjectOfType<HexGridManager>();
            if (mgr != null) {
                // 这是一个 hack，通常 Singleton 应该自动设置，但在时序混乱时强制获取
                Debug.Log("[Building-Client] Found HexGridManager in scene.");
                // 注意：这里我们只能拿到引用，无法强制赋值给 private set 的 Singleton
                // 所以我们需要临时使用 mgr 变量
                BindToGridInternal(mgr, coords);
                return; 
            } else {
                Debug.LogError("[Building-Client] HexGridManager not found in scene! Cannot bind building.");
                return;
            }
        }

        BindToGridInternal(HexGridManager.Singleton, coords);
    }

    private void BindToGridInternal(HexGridManager mgr, Vector2Int coords) {
        // 核心修复：如果数据尚未加载（因为之前 HexGridManager 为空），这里再次尝试加载
        if (data == null && !string.IsNullOrEmpty(buildingTypeId.Value.ToString())) {
            Debug.Log($"[Building-Client] Retrying LoadData for {buildingTypeId.Value} in BindToGrid");
            // 这里需要传入 mgr，因为 LoadData 内部依赖 Singleton
            // 我们需要修改 LoadData 或者暂时假定 Singleton 已修复
            // 为了安全，我们直接在这里做 LoadData 的逻辑
            if (mgr.availableBuildings != null) {
                 data = mgr.availableBuildings.Find(b => b.buildingTypeId == buildingTypeId.Value.ToString());
                 if (data != null) {
                     Debug.Log($"[Building] Loaded data for {buildingTypeId.Value}");
                     OnDataLoaded?.Invoke();
                     OnLevelChanged?.Invoke(currentLevel.Value);
                 }
            }
        }
        
        if (mgr.Cells.TryGetValue(coords, out HexCell cell)) {
            Debug.Log($"[Building-Client] Binding to Grid: {coords} -> {cell.name}");

            // 1. 绑定逻辑
            Setup(cell);
            
            // 2. 修正位置 (如果 Prefab 没有 NetworkTransform，或者需要精确贴地)
            // 客户端需要根据 Cell 位置把自己挪过去
            Vector3 targetPos = cell.transform.position;
            
            // 数据驱动的高度修正
            float modelOffset = data != null ? data.modelVerticalOffset : 0f;
            targetPos.y += mgr.verticalOffset + modelOffset;
            
            transform.position = targetPos;

            // 3. 调用 UpdateVisuals 确保路径连接正确
            cell.UpdateVisuals();
            if (mgr != null) {
                foreach (var neighbor in mgr.GetNeighbors(coords)) {
                    if (neighbor != null) {
                        neighbor.UpdateVisuals();
                    }
                }
            }
        } else {
            Debug.LogWarning($"[Building-Client] Grid Cell not found at {coords}. Map might not be loaded yet.");
        }
    }

    void Update() {
        if (!IsServer) return; // 只有服务器有权处理逻辑

        if (data == null) return;

        // 获取当前等级对应的配置
        int maxCap = data.GetMaxCapacity(currentLevel.Value);
        float prodRate = data.GetProductionRate(currentLevel.Value);

        // 生产与人口控制逻辑
        // 1. 未满员 (Current < Max): 按生产速度增加兵力
        // 2. 超员 (Current > Max): 按生产速度(此处视为消耗/衰减速度)减少兵力，直到恢复至上限
        if (currentSoldiers.Value != maxCap) {
            timer += Time.deltaTime;
            if (timer >= prodRate) {
                if (currentSoldiers.Value < maxCap) {
                    currentSoldiers.Value++;
                } else {
                    currentSoldiers.Value--;
                }
                timer = 0;
            }
        } else {
            // 刚好满员时，重置计时器
            timer = 0;
        }
    }

    // 处理士兵到达 (战斗与增援逻辑)
    public void OnTroopArrive(ulong troopOwnerId, int amount, int attackPower) {
        if (!IsServer) return;
        
        int damage = amount * attackPower;

        if (troopOwnerId == ownerId.Value) {
            // 1. 自己人：增援
            currentSoldiers.Value += amount; 
        } else {
            // 2. 敌人：进攻
            currentSoldiers.Value -= damage; 
            
            // 3. 兵力归零：易主
            if (currentSoldiers.Value < 0) {
                currentSoldiers.Value = Mathf.Abs(currentSoldiers.Value);
                ownerId.Value = troopOwnerId; 
                // 易主时，你可能还想重置等级，或者保留等级，视策划需求而定
                // currentLevel.Value = 1; 
            }
        }
    }

    // --- Upgrade Logic called by GameCommandManager ---
    public void Upgrade() {
        if (!IsServer) return;
        if (data == null) return;

        // 2. 检查是否已达最高级
        if (currentLevel.Value >= data.levels.Count) {
            Debug.LogWarning($"[Building] 升级失败：建筑 {name} 已达最高等级。");
            return;
        }

        // 3. 消耗检查：必须人口满员
        int maxCap = data.GetMaxCapacity(currentLevel.Value);
        if (currentSoldiers.Value < maxCap) {
            Debug.LogWarning($"[Building] 升级失败：建筑 {name} 人口未满 ({currentSoldiers.Value}/{maxCap})。");
            return;
        }

        // 4. 执行升级逻辑
        // 扣除一半兵力作为消耗
        int cost = currentSoldiers.Value / 2;
        currentSoldiers.Value -= cost;
        
        // 提升等级
        currentLevel.Value++;

        Debug.Log($"[Building] 建筑 {name} 升级成功！等级：{currentLevel.Value}，消耗兵力：{cost}，剩余兵力：{currentSoldiers.Value}");
    }
}
