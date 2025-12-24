using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// 游戏流程管理器
/// 负责处理从大厅到游戏的过渡，以及游戏内的胜负判定等全局逻辑。
/// 建议挂载在 NetworkManager 物体上，或者一个单独的 DontDestroyOnLoad 物体上。
/// </summary>
public class GameFlowManager : NetworkBehaviour {
    
    [Header("Game Settings")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private GameObject rtsPlayerPrefab; // 真正的 RTS 玩家控制器 Prefab

    // 单例模式
    public static GameFlowManager Singleton { get; private set; }

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 开始游戏 (仅 Server 调用)
    /// </summary>
    public void StartGame() {
        if (!IsServer) return;

        // 1. 检查所有玩家是否准备好 (双重保险)
        LobbyPlayerState[] players = FindObjectsOfType<LobbyPlayerState>();
        foreach (var p in players) {
            if (!p.IsReady.Value) {
                Debug.LogWarning("[GameFlowManager] Cannot start game, not all players are ready.");
                return;
            }
        }

        // 2. 切换场景
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        
        // 3. 监听场景加载完成，以便生成 RTS 角色
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
    }

    private void OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut) {
        if (sceneName == gameSceneName) {
            Debug.Log("[GameFlowManager] Game Scene Loaded. Spawning RTS Controllers...");
            
            // 只有服务器负责生成
            if (IsServer) {
                SpawnRTSPlayers();
            }
            
            // 取消订阅
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
    }

    private void SpawnRTSPlayers() {
        if (rtsPlayerPrefab == null) {
            Debug.LogError("[GameFlowManager] RTS Player Prefab is missing!");
            return;
        }

        // 遍历所有连接的客户端
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList) {
            // 1. 生成 RTS 控制器
            // 注意：这里需要确定生成位置，暂时生成在零点，PlayerController 会自己找城堡
            GameObject playerObj = Instantiate(rtsPlayerPrefab, Vector3.zero, Quaternion.identity);
            
            // 2. 获取 NetworkObject
            NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
            
            // 3. 生成并指定归属权
            netObj.SpawnAsPlayerObject(client.ClientId, true); 
            // 注意：SpawnAsPlayerObject 会替换掉之前的 LobbyPlayerState 成为新的 PlayerObject
            // 如果你想保留 LobbyPlayerState，可以使用 SpawnWithOwnership，但这会导致 PlayerObject 引用没变
            // 推荐：让 RTSController 成为新的 PlayerObject，LobbyPlayerState 可以销毁或作为数据容器保留
        }
    }
}
