using UnityEngine;
using Unity.Netcode;
using System;

public class Building : NetworkBehaviour {
    [Header("Configuration")]
    public BuildingData data;

    [Header("Network State")]
    public NetworkVariable<int> currentSoldiers = new NetworkVariable<int>(10);
    public NetworkVariable<ulong> ownerId = new NetworkVariable<ulong>(999); // 999代表中立
    public NetworkVariable<int> currentLevel = new NetworkVariable<int>(1);
    
    [Header("Initial Setup")]
    [SerializeField] private ulong initialOwnerId = 999;
    [SerializeField] private int initialPlayerLevel = 2;
    [SerializeField] private int initialNeutralLevel = 1;

    // --- Model Events (供 View 层监听) ---
    public event Action<int> OnSoldierCountChanged;
    public event Action<ulong> OnOwnerChanged;
    public event Action<int> OnLevelChanged; // 新增：等级变化事件

    private float timer;

    public override void OnNetworkSpawn() {
        // 1. 服务器端初始化数据
        if (IsServer) {
            ownerId.Value = initialOwnerId;
            // 根据归属设定初始等级
            if (initialOwnerId == 999) {
                currentLevel.Value = initialNeutralLevel;
            } else {
                currentLevel.Value = initialPlayerLevel;
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

    // --- 后续预留：升级功能的接口 ---
    [ServerRpc(RequireOwnership = false)]
    public void TryUpgradeServerRpc(ulong requestPlayerId) {
        // 1. 只有该建筑的主人才能升级
        if (ownerId.Value != requestPlayerId) {
            Debug.LogWarning($"[Building] 升级失败：玩家 {requestPlayerId} 不是建筑 {name} 的所有者。");
            return;
        }

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