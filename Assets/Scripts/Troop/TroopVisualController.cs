using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// 负责 Troop 的视觉表现（View 层）
/// 包含模型生成、颜色更新、对象池管理等
/// </summary>
[RequireComponent(typeof(Troop))]
public class TroopVisualController : MonoBehaviour {
    
    [Header("Visual Settings")]
    [SerializeField] private float clusterRadius = 0.5f;

    private Troop _troop;
    
    // --- Optimization: Visual Pooling ---
    private class VisualItem {
        public GameObject gameObject;
        public Renderer[] renderers;
    }
    private List<VisualItem> _activeItems = new List<VisualItem>();
    private Queue<VisualItem> _itemPool = new Queue<VisualItem>();
    private int _lastTypeId = -1;
    
    // Cached PropertyBlock
    private static MaterialPropertyBlock _propBlock;
    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor"); // For URP

    private void Awake() {
        _troop = GetComponent<Troop>();
    }

    private void Start() {
        // 订阅数据变化
        if (_troop != null) {
            _troop.netOwnerId.OnValueChanged += (oldVal, newVal) => UpdateColor(newVal);
            _troop.netTroopCount.OnValueChanged += (oldVal, newVal) => SpawnVisuals();
            _troop.troopTypeId.OnValueChanged += (oldVal, newVal) => SpawnVisuals();
            
            // 初始刷新
            SpawnVisuals();
            UpdateColor(_troop.netOwnerId.Value);
        }

        // 隐藏父物体的渲染器（如果有）
        Renderer parentRenderer = GetComponent<Renderer>();
        if (parentRenderer != null) parentRenderer.enabled = false;
    }

    private void OnDestroy() {
        // 清理
        foreach (var item in _activeItems) {
            if (item.gameObject != null) Destroy(item.gameObject);
        }
        foreach (var item in _itemPool) {
            if (item.gameObject != null) Destroy(item.gameObject);
        }
    }

    /// <summary>
    /// 当 Troop 被回收到对象池时调用（由 Troop.OnNetworkDespawn 调用）
    /// </summary>
    public void OnDespawn() {
        foreach (var item in _activeItems) {
            if (item.gameObject != null) {
                item.gameObject.SetActive(false);
                _itemPool.Enqueue(item);
            }
        }
        _activeItems.Clear();
    }

    private void SpawnVisuals() {
        if (_troop == null) return;

        int newTypeId = _troop.troopTypeId.Value;
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

        int count = _troop.netTroopCount.Value;
        // 4. Relax Count Limit
        int visualCount = Mathf.Min(count, 20); 

        for (int i = 0; i < visualCount; i++) {
            VisualItem item;
            
            if (_itemPool.Count > 0) {
                item = _itemPool.Dequeue();
            } else {
                GameObject model;
                if (prefabToSpawn != null) {
                    model = Instantiate(prefabToSpawn, transform);
                } else {
                    model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    model.transform.SetParent(transform);
                    Destroy(model.GetComponent<Collider>()); 
                }
                
                item = new VisualItem {
                    gameObject = model,
                    renderers = model.GetComponentsInChildren<Renderer>()
                };
            }
            
            item.gameObject.SetActive(true);
            Vector3 randomOffset = Random.insideUnitSphere * clusterRadius;
            randomOffset.y = 0; 
            item.gameObject.transform.localPosition = randomOffset;
            item.gameObject.transform.localScale = Vector3.one * 0.3f;
            item.gameObject.transform.localRotation = Quaternion.identity;

            _activeItems.Add(item);
        }
        
        UpdateColor(_troop.netOwnerId.Value);
    }

    private void UpdateColor(ulong id) {
        Color color = GameConfig.GetPlayerColor(id);

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

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
        
        Renderer selfRend = GetComponent<Renderer>();
        if (selfRend != null) {
            selfRend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(ColorPropId, color);
            _propBlock.SetColor(BaseColorPropId, color);
            selfRend.SetPropertyBlock(_propBlock);
        }
    }
}
