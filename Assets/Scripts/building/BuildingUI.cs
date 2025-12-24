using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// 挂载在 UI Prefab 上。
/// 负责处理 UI 的内部逻辑（显示数字、判断是否满级隐藏按钮）。
/// </summary>
public class BuildingUI : MonoBehaviour {
    [Header("UI 组件引用")]
    [SerializeField] private Text soldierCountText; // 显示兵力
    [SerializeField] private Button upgradeButton;  // 升级按钮
    [SerializeField] private GameObject upgradeButtonRoot; // 按钮的父物体（用于整体隐藏）

    private Building _targetBuilding;

    /// <summary>
    /// 初始化方法，由 BuildingView 在生成 UI 时调用
    /// </summary>
    public void Initialize(Building building) {
        _targetBuilding = building;

        // 1. 绑定按钮点击事件
        if (upgradeButton != null) {
            upgradeButton.onClick.AddListener(OnUpgradeClicked);
        }

        // 2. 订阅数据变化事件 (Model -> View)
        if (_targetBuilding != null) {
            _targetBuilding.OnSoldierCountChanged += UpdateCount;
            _targetBuilding.OnLevelChanged += UpdateLevelState;
            _targetBuilding.OnOwnerChanged += UpdateOwnerState;

            // 3. 立即刷新一次当前状态
            UpdateCount(_targetBuilding.currentSoldiers.Value);
            UpdateLevelState(_targetBuilding.currentLevel.Value);
            UpdateOwnerState(_targetBuilding.ownerId.Value);
        }
    }

    private void OnDestroy() {
        // 务必取消订阅，防止内存泄漏
        if (_targetBuilding != null) {
            _targetBuilding.OnSoldierCountChanged -= UpdateCount;
            _targetBuilding.OnLevelChanged -= UpdateLevelState;
            _targetBuilding.OnOwnerChanged -= UpdateOwnerState;
        }

        if (upgradeButton != null) {
            upgradeButton.onClick.RemoveAllListeners();
        }
    }

    // --- 逻辑处理 ---

    private void UpdateCount(int count) {
        if (soldierCountText != null) {
            soldierCountText.text = count.ToString();
        }
        // 兵力变化也会影响按钮的可交互状态 (Interactable)
        UpdateUpgradeButtonState();
    }

    /// <summary>
    /// 根据等级决定是否显示升级按钮
    /// </summary>
    private void UpdateLevelState(int currentLevel) {
        // 等级变化会影响按钮的显隐 (Active)
        UpdateUpgradeButtonState();
    }

    /// <summary>
    /// 当所有者改变时（比如被占领），重新检查按钮显隐
    /// </summary>
    private void UpdateOwnerState(ulong ownerId) {
        // 所有权改变会影响按钮显隐
        UpdateUpgradeButtonState();
    }

    /// <summary>
    /// 统一处理升级按钮的状态 (Active 和 Interactable)
    /// </summary>
    private void UpdateUpgradeButtonState() {
        if (upgradeButtonRoot == null || upgradeButton == null || _targetBuilding == null || _targetBuilding.data == null) return;

        int currentLevel = _targetBuilding.currentLevel.Value;
        int maxLevel = _targetBuilding.data.levels.Count;
        int currentSoldiers = _targetBuilding.currentSoldiers.Value;
        int maxCapacity = _targetBuilding.data.GetMaxCapacity(currentLevel);

        // 1. 判断是否显示 (Active)
        // 条件：未满级 且 是自己的建筑
        bool isMaxLevel = currentLevel >= maxLevel;
        bool isMine = _targetBuilding.ownerId.Value == NetworkManager.Singleton.LocalClientId;
        bool shouldShow = !isMaxLevel && isMine;

        upgradeButtonRoot.SetActive(shouldShow);

        // 2. 判断是否可点击 (Interactable)
        // 条件：人口已满员
        if (shouldShow) {
            bool isFull = currentSoldiers >= maxCapacity;
            upgradeButton.interactable = isFull;
        }
    }

    private void OnUpgradeClicked() {
        if (_targetBuilding == null) return;
        Debug.Log($"请求升级建筑: {_targetBuilding.name}");
        
        // 调用 ServerRpc 进行升级
        // 注意：Building 的 TryUpgradeServerRpc 验证了 ownership，这里安全调用
        _targetBuilding.TryUpgradeServerRpc(NetworkManager.Singleton.LocalClientId);
    }
}