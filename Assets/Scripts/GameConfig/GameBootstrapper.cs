using UnityEngine;
using System.Collections.Generic;

public class GameBootstrapper : MonoBehaviour {
    [Header("Registry Configuration")]
    public List<TroopData> allTroopConfigs;

    private void Awake() {
        // 游戏启动时初始化注册表
        GameConfig.InitializeLibrary(allTroopConfigs);
        
        // 自动创建 NetworkObjectPool (如果场景中没有)
        if (NetworkObjectPool.Singleton == null) {
            GameObject poolObj = new GameObject("NetworkObjectPool");
            poolObj.AddComponent<NetworkObjectPool>();
            Debug.Log("[GameBootstrapper] Auto-created NetworkObjectPool.");
        }

        // 保持此物体不销毁（可选，视架构而定）
        DontDestroyOnLoad(gameObject);
    }
}
