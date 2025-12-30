using UnityEngine;
using UnityEngine.UI;

public class BuildingView : MonoBehaviour {
    [Header("核心引用")]
    [SerializeField] private Building targetBuilding;
    [SerializeField] private Transform modelContainer; // 用于挂载实例化模型的父物体
    [SerializeField] private Transform uiAttachPoint;

    [Header("UI 设置")]
    [SerializeField] private GameObject uiPrefab;
    [SerializeField] private Canvas mainCanvas;
    
    [Header("Visual Feedback")]
    [SerializeField] private GameObject selectionRing; // 选中时的光圈

    private BuildingUI _spawnedUI;
    private RectTransform _uiRect;
    private Camera _mainCamera;
    
    // 缓存当前动态模型的所有渲染器，以便更新颜色
    private System.Collections.Generic.List<Renderer> _currentRenderers = new System.Collections.Generic.List<Renderer>();

    void Start() {
        // 1. 自动获取引用
        if (targetBuilding == null) targetBuilding = GetComponent<Building>();
        
        // 尝试获取 Canvas，如果获取不到，LateUpdate 会重试
        if (mainCanvas == null) mainCanvas = FindObjectOfType<Canvas>();

        // 2. 尝试生成 UI
        CreateAttachedUI();

        // 3. 订阅事件
        if (targetBuilding != null) {
            targetBuilding.OnOwnerChanged += UpdateOwnerColor;
            targetBuilding.OnLevelChanged += UpdateVisualModel; // 监听等级变化
            
            // 初始化视图状态
            UpdateVisualModel(targetBuilding.Level);
            UpdateOwnerColor(targetBuilding.OwnerId);
        }

        // 默认隐藏光圈
        SetSelected(false);
    }

    void LateUpdate() {
        // --- 核心修复：更健壮的相机获取逻辑 ---
        if (!IsCameraValid()) {
            RefreshCamera();
            // 如果还没找到有效相机，暂时不要更新UI位置，也不要乱隐藏
            return; 
        }

        if (_spawnedUI != null && targetBuilding != null) {
            UpdateUIPosition();
        }
    }

    // 判断当前持有的相机是否有效
    private bool IsCameraValid() {
        // 相机必须存在，且必须是开启状态 (activeInHierarchy)
        return _mainCamera != null && _mainCamera.gameObject.activeInHierarchy;
    }

    private void RefreshCamera() {
        // 优先找 Tag 为 MainCamera 的
        _mainCamera = Camera.main;

        // 如果 Camera.main 没找到，或者找到的是个被禁用的相机
        if (_mainCamera == null || !_mainCamera.gameObject.activeInHierarchy) {
            // 尝试找所有相机，取第一个激活的（通常是玩家相机）
            foreach (var cam in FindObjectsOfType<Camera>()) {
                if (cam.gameObject.activeInHierarchy) {
                    _mainCamera = cam;
                    // Debug.Log($"[BuildingView] 找到了新的激活相机: {cam.name}");
                    break;
                }
            }
        }
    }

    void OnDestroy() {
        if (_spawnedUI != null) Destroy(_spawnedUI.gameObject);
        if (targetBuilding != null) {
            targetBuilding.OnOwnerChanged -= UpdateOwnerColor;
            targetBuilding.OnLevelChanged -= UpdateVisualModel;
        }
    }

    private void CreateAttachedUI() {
        if (uiPrefab == null || mainCanvas == null) return;

        GameObject uiObj = Instantiate(uiPrefab, mainCanvas.transform);
        _spawnedUI = uiObj.GetComponent<BuildingUI>();
        _uiRect = uiObj.GetComponent<RectTransform>();

        if (_spawnedUI != null) _spawnedUI.Initialize(targetBuilding);

        // 初始设为 false，但在 UpdateUIPosition 里会由代码控制打开
        uiObj.SetActive(false); 
    }

    private void UpdateUIPosition() {
        // 双重保险
        if (_mainCamera == null) return;

        // 1. 获取 3D 坐标
        Vector3 worldPos = uiAttachPoint != null ? uiAttachPoint.position : transform.position + Vector3.up * 2.0f;

        // 2. 转换屏幕坐标
        Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);

        // 3. 判断是否在相机前方
        // screenPos.z > 0 代表在相机前方
        bool isInFront = screenPos.z > 0;

        // 4. 视锥体剔除（可选）：判断是否在屏幕范围内 (x, y 在屏幕分辨率内)
        // 简单做法：只判断 z > 0 即可，UGUI 会自动处理屏幕外的裁剪
        
        if (isInFront) {
            // 如果之前是隐藏的，现在显示出来
            if (!_spawnedUI.gameObject.activeSelf) {
                _spawnedUI.gameObject.SetActive(true);
            }
            
            // 赋值位置 (Z轴保持0，防止 UI 深度错误)
            screenPos.z = 0; 
            _uiRect.position = screenPos;
        } 
        else {
            // 在相机背后，隐藏
            if (_spawnedUI.gameObject.activeSelf) {
                _spawnedUI.gameObject.SetActive(false);
            }
        }
    }

    // Optimization: Cache PropertyBlock and ID
    private static MaterialPropertyBlock _propBlock;
    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor"); // For URP

    private void UpdateOwnerColor(ulong ownerId) {
        // 使用全局配置获取颜色
        Color color = GameConfig.GetPlayerColor(ownerId);

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        // 遍历所有动态模型的渲染器并应用颜色
        if (_currentRenderers != null) {
            foreach (var renderer in _currentRenderers) {
                if (renderer != null) {
                    // CRITICAL FIX: 使用 PropertyBlock 替代 material.color
                    // 避免创建材质实例，修复 D3D11 显存泄漏和 Batching 失效问题
                    renderer.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorPropId, color);
                    _propBlock.SetColor(BaseColorPropId, color); // Set both for compatibility
                    renderer.SetPropertyBlock(_propBlock);
                }
            }
        }
        
    }

    /// <summary>
    /// 设置选中状态（显示/隐藏光圈）
    /// </summary>
    public void SetSelected(bool isSelected) {
        if (selectionRing != null && selectionRing.activeSelf != isSelected) {
            selectionRing.SetActive(isSelected);
        }
    }

    /// <summary>
    /// 根据等级更新建筑的 3D 外观
    /// </summary>
    private void UpdateVisualModel(int level) {
        if (modelContainer == null || targetBuilding == null || targetBuilding.data == null) return;

        bool isOnlineClient = Application.isPlaying && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening && Unity.Netcode.NetworkManager.Singleton.IsClient && !Unity.Netcode.NetworkManager.Singleton.IsServer;

        // 1. 销毁旧模型
        foreach (Transform child in modelContainer) {
            if (isOnlineClient && child.GetComponentInChildren<Unity.Netcode.NetworkObject>(true) != null) {
                continue;
            }
            Destroy(child.gameObject);
        }
        _currentRenderers.Clear();

        // 2. 获取新模型预制体
        GameObject prefab = targetBuilding.data.GetVisualModel(level);
        if (prefab == null) {
            Debug.LogWarning($"[BuildingView] Level {level} visual model missing for {name}");
            return;
        }

        // 3. 实例化新模型
        GameObject newModel = Instantiate(prefab, modelContainer);
        newModel.transform.localPosition = Vector3.zero;
        newModel.transform.localRotation = Quaternion.identity;

        // 4. 缓存渲染器以便后续染色
        // Optimization: Use non-alloc version
        newModel.GetComponentsInChildren(_currentRenderers);

        // 5. 立即应用当前的所有者颜色 (保持颜色一致)
        UpdateOwnerColor(targetBuilding.ownerId.Value);
    }
}
