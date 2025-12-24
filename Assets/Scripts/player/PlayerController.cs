using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// 玩家控制器：主要负责网络同步、出兵逻辑和组件管理。
/// 相机控制逻辑已剥离至 RTSCameraController。
/// </summary>
public class PlayerController : NetworkBehaviour {
    [Header("兵力设置")]
    [SerializeField] private GameObject defaultTroopHostPrefab; // 士兵预制体 (通用空壳，需包含 NetworkObject)

    private Camera _myCamera;
    // private Building _selectedSource; // Deprecated
    private System.Collections.Generic.List<Building> _selectedBuildings = new System.Collections.Generic.List<Building>();
    private RTSCameraController _cameraController;

    // --- 初始化部分 ---

    public override void OnNetworkSpawn() {
        // 注册对象池 (确保两端都注册了 Handler)
        if (NetworkObjectPool.Singleton != null && defaultTroopHostPrefab != null) {
            NetworkObjectPool.Singleton.RegisterPrefab(defaultTroopHostPrefab);
        }

        // 获取组件引用
        _myCamera = GetComponent<Camera>();
        _cameraController = GetComponent<RTSCameraController>();
        var listener = GetComponent<AudioListener>();
        var raycaster = GetComponent<UnityEngine.EventSystems.PhysicsRaycaster>();

        // 1. 隐藏玩家控制器本身的渲染和碰撞（逻辑对象不可见）
        var collider = GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        var renderer = GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;

        if (IsOwner) {
            // 本地玩家：启用相机、监听器和相机控制器
            if (_myCamera) _myCamera.enabled = true;
            if (listener) listener.enabled = true;
            if (raycaster) raycaster.enabled = true;
            if (_cameraController) _cameraController.enabled = true;

            // 禁用场景中默认的临时相机
            var sceneCam = GameObject.FindGameObjectWithTag("MainCamera");
            if (sceneCam != null && sceneCam != gameObject) {
                sceneCam.SetActive(false);
            }

            // 确保自己的相机被标记为 MainCamera，以便 UI 脚本能找到它
            if (_myCamera != null) {
                _myCamera.tag = "MainCamera";
            }

            // 自动定位到自己的初始城堡
            StartCoroutine(FindAndMoveToBaseRoutine());
        } else {
            // 远程玩家：禁用所有交互和渲染组件
            if (_myCamera) _myCamera.enabled = false;
            if (listener) listener.enabled = false;
            if (raycaster) raycaster.enabled = false;
            if (_cameraController) _cameraController.enabled = false;
        }
    }

    private IEnumerator FindAndMoveToBaseRoutine() {
        bool found = false;
        int attempts = 0;
        
        // 尝试寻找属于自己的建筑
        while (!found && attempts < 20) {
            Building[] buildings = FindObjectsOfType<Building>();
            foreach (var b in buildings) {
                if (b.ownerId.Value == OwnerClientId) {
                    Vector3 offset = new Vector3(0, 20, -15); // 初始俯视偏移
                    transform.position = b.transform.position + offset;
                    transform.LookAt(b.transform.position);
                    found = true;
                    break;
                }
            }
            attempts++;
            yield return new WaitForSeconds(0.5f);
        }
    }

    // --- 每帧逻辑部分 ---

    void Update() {
        if (!IsOwner) return;

        // 相机控制由 RTSCameraController 处理
        // 这里只处理业务逻辑（选择和出兵）
        HandleSelection();
    }

    /// <summary>
    /// 处理左键点击选择建筑及出兵逻辑 (多选版)
    /// </summary>
    private void HandleSelection() {
        if (Input.GetMouseButtonDown(0)) {
            if (_myCamera == null) return;

            Ray ray = _myCamera.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                // 点击到建筑：获取其 Building 组件
                Building clickedBuilding = hit.collider.GetComponentInParent<Building>();
                
                if (clickedBuilding != null) {
                    // 分支 A: 点击了建筑
                    if (clickedBuilding.ownerId.Value == OwnerClientId) {
                        // 1. 点击己方建筑 -> 选择逻辑
                        HandleFriendlySelection(clickedBuilding);
                    } else {
                        // 2. 点击非己方建筑 -> 攻击逻辑
                        HandleHostileInteraction(clickedBuilding);
                    }
                } else {
                    // 点击了非建筑物体 (如地面) -> 取消选择
                    DeselectAll();
                }
            } else {
                // 点击了虚空 -> 取消选择
                DeselectAll();
            }
        }
    }

    private void HandleFriendlySelection(Building building) {
        bool isCtrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (isCtrlHeld) {
            // Ctrl 模式：切换选择
            if (_selectedBuildings.Contains(building)) {
                RemoveFromSelection(building);
            } else {
                AddToSelection(building);
            }
        } else {
            // Ctrl 键未按下：检查是否为“增援”操作
            // 如果当前有选中建筑，且点击的新建筑不在选中列表中 -> 视为向该建筑输送兵力
            if (_selectedBuildings.Count > 0 && !_selectedBuildings.Contains(building)) {
                // 执行增援逻辑 (Reinforce)
                var sources = new System.Collections.Generic.List<Building>(_selectedBuildings);
                
                foreach (var source in sources) {
                    if (source != null && source != building) {
                        SendTroopsServerRpc(source.NetworkObjectId, building.NetworkObjectId);
                    }
                }
                
                // 交互完成，取消所有选择
                DeselectAll();
            } else {
                // 普通模式：单选
                DeselectAll();
                AddToSelection(building);
            }
        }
    }

    private void HandleHostileInteraction(Building targetBuilding) {
        // 让所有已选中的己方建筑向目标发兵
        // 使用副本遍历，防止RPC可能导致的集合修改（虽然目前逻辑不会，但安全第一）
        var sources = new System.Collections.Generic.List<Building>(_selectedBuildings);
        
        foreach (var source in sources) {
            if (source != null && source != targetBuilding) {
                SendTroopsServerRpc(source.NetworkObjectId, targetBuilding.NetworkObjectId);
            }
        }
        
        // 按照需求：发兵结束后清空选择
        DeselectAll();
    }

    private void AddToSelection(Building b) {
        if (!_selectedBuildings.Contains(b)) {
            _selectedBuildings.Add(b);
            // Visual Feedback
            var view = b.GetComponent<BuildingView>();
            if (view != null) view.SetSelected(true);
        }
    }

    private void RemoveFromSelection(Building b) {
        if (_selectedBuildings.Contains(b)) {
            _selectedBuildings.Remove(b);
            // Visual Feedback
            var view = b.GetComponent<BuildingView>();
            if (view != null) view.SetSelected(false);
        }
    }

    private void DeselectAll() {
        foreach (var b in _selectedBuildings) {
            if (b != null) {
                var view = b.GetComponent<BuildingView>();
                if (view != null) view.SetSelected(false);
            }
        }
        _selectedBuildings.Clear();
    }

    // --- 服务器逻辑部分 ---

    [ServerRpc]
    private void SendTroopsServerRpc(ulong fromId, ulong toId) {
        // 1. 获取服务器端的物体实例
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(fromId, out NetworkObject fromObj) &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(toId, out NetworkObject toObj)) {
            
            Building sourceBuilding = fromObj.GetComponent<Building>();

            // 2. 验证权限与兵力
            if (sourceBuilding != null && sourceBuilding.ownerId.Value == OwnerClientId) {
                int totalToSpawn = sourceBuilding.currentSoldiers.Value / 2;
                
                if (totalToSpawn > 0) {
                    // 3. 立即扣除总兵力
                    sourceBuilding.currentSoldiers.Value -= totalToSpawn;

                    // 4. 获取兵种数据
                    TroopData troopData = null;
                    if (sourceBuilding.data != null && sourceBuilding.data.troopData != null) {
                        troopData = sourceBuilding.data.troopData;
                    } else {
                        Debug.LogError("[PlayerController] 建筑没有配置兵种数据！");
                        return;
                    }

                    // 5. 开启协程分批出兵
                    StartCoroutine(SpawnTroopsRoutine(sourceBuilding.transform.position, toId, OwnerClientId, totalToSpawn, troopData));
                }
            }
        }
    }

    private IEnumerator SpawnTroopsRoutine(Vector3 startPos, ulong targetId, ulong ownerId, int totalCount, TroopData data) {
        int spawnedCount = 0;
        int batchSize = 5; // 每波出多少个兵

        while (spawnedCount < totalCount) {
            int currentBatch = Mathf.Min(batchSize, totalCount - spawnedCount);
            
            // Spawn Troop Instance
            if (defaultTroopHostPrefab != null) {
                // 使用对象池获取实例
                NetworkObject troopNetObj = NetworkObjectPool.Singleton.GetNetworkObject(defaultTroopHostPrefab, startPos, Quaternion.identity);
                
                if (troopNetObj != null) {
                    troopNetObj.Spawn(true);

                    // Initialize Troop
                    Troop troopScript = troopNetObj.GetComponent<Troop>();
                    if (troopScript != null) {
                        troopScript.Initialize(targetId, ownerId, data, currentBatch);
                    }
                }
            } else {
                Debug.LogError("[PlayerController] defaultTroopHostPrefab 未赋值！");
            }

            spawnedCount += currentBatch;

            // Wait for next wave
            yield return new WaitForSeconds(0.2f);
        }
    }
}
