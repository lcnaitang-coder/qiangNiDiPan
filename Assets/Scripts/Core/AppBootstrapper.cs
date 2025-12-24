using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// 应用程序启动器
/// 挂载在 Bootstrap Scene 的空物体上。
/// 负责初始化全局单例并跳转到主菜单。
/// </summary>
public class AppBootstrapper : MonoBehaviour {
    
    [Header("Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private void Start() {
        // 1. 确保 NetworkManager 存在
        if (NetworkManager.Singleton == null) {
            Debug.LogError("[AppBootstrapper] NetworkManager is missing! Please add a NetworkManager to the Bootstrap scene.");
            // 可以在这里尝试实例化一个 NetworkManager Prefab，但通常建议直接在场景里放一个
            return;
        }

        // 确保 NetworkManager 会在场景切换时保留
        DontDestroyOnLoad(NetworkManager.Singleton.gameObject);

        // 2. 初始化其他可能的全局单例 (如 GameConfig, NetworkObjectPool)
        // 注意：如果 NetworkObjectPool 已经在场景中并设置为 DontDestroyOnLoad，这里不需要额外操作
        if (NetworkObjectPool.Singleton == null) {
            // 如果 NetworkObjectPool 没有在场景中，可以在这里动态生成，或者依赖 GameBootstrapper
            Debug.Log("[AppBootstrapper] NetworkObjectPool not found, assuming it will be initialized later or in GameBootstrapper.");
        }

        // 3. 加载主菜单
        Debug.Log("[AppBootstrapper] Initialization complete. Loading MainMenu...");
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
