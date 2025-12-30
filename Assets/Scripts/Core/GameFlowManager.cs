using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections.Generic;

/// <summary>
/// 玩家加载状态结构体
/// </summary>
public struct PlayerLoadState : INetworkSerializable, System.IEquatable<PlayerLoadState> {
    public ulong ClientId;
    public FixedString64Bytes PlayerName;
    public ulong SteamId;
    public bool IsFinished;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref SteamId);
        serializer.SerializeValue(ref IsFinished);
    }

    public bool Equals(PlayerLoadState other) {
        return ClientId == other.ClientId && 
               PlayerName == other.PlayerName && 
               SteamId == other.SteamId && 
               IsFinished == other.IsFinished;
    }
}

/// <summary>
/// 游戏流程管理器
/// 负责处理从大厅到游戏的过渡，以及游戏内的胜负判定等全局逻辑。
/// 建议挂载在 NetworkManager 物体上，或者一个单独的 DontDestroyOnLoad 物体上。
/// </summary>
public class GameFlowManager : NetworkBehaviour {
    
    [Header("Game Settings")]
    [SerializeField] private string gameSceneName = "GameScene";
    [SerializeField] private GameObject rtsPlayerPrefab; // 真正的 RTS 玩家控制器 Prefab

    // 状态同步
    public NetworkList<PlayerLoadState> PlayerLoadStates;
    public NetworkVariable<bool> IsGameStarted = new NetworkVariable<bool>(false);

    // 单例模式
    public static GameFlowManager Singleton { get; private set; }

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
        
        // 初始化 NetworkList
        PlayerLoadStates = new NetworkList<PlayerLoadState>();
    }

    /// <summary>
    /// 开始游戏 (仅 Server 调用)
    /// </summary>
    public void StartGame() {
        if (!IsServer) return;

        // 1. 检查所有玩家是否准备好 (双重保险)
        LobbyPlayerState[] lobbyPlayers = FindObjectsOfType<LobbyPlayerState>();
        foreach (var p in lobbyPlayers) {
            if (!p.IsReady.Value) {
                Debug.LogWarning("[GameFlowManager] Cannot start game, not all players are ready.");
                return;
            }
        }

        // 2. 初始化状态列表
        PlayerLoadStates.Clear();
        foreach (var p in lobbyPlayers) {
            PlayerLoadStates.Add(new PlayerLoadState {
                ClientId = p.ClientId,
                PlayerName = p.PlayerName.Value,
                SteamId = p.SteamId.Value,
                IsFinished = false
            });
        }
        
        IsGameStarted.Value = false;

        // 3. 订阅事件
        NetworkManager.Singleton.SceneManager.OnLoadComplete += OnClientLoadedScene;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;

        // 4. 切换场景
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void OnClientLoadedScene(ulong clientId, string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode) {
        if (sceneName == gameSceneName) {
            // 更新对应玩家的状态
            for (int i = 0; i < PlayerLoadStates.Count; i++) {
                if (PlayerLoadStates[i].ClientId == clientId) {
                    var state = PlayerLoadStates[i];
                    state.IsFinished = true;
                    PlayerLoadStates[i] = state; // 写回 NetworkList
                    break;
                }
            }
            Debug.Log($"[GameFlowManager] Client {clientId} loaded {sceneName}.");
        }
    }

    private void OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut) {
        if (sceneName == gameSceneName) {
            Debug.Log("[GameFlowManager] All Clients Loaded. Starting Game...");
            
            // 只有服务器负责生成
            if (IsServer) {
                BattleMapManager mapMgr = BattleMapManager.Singleton != null ? BattleMapManager.Singleton : FindObjectOfType<BattleMapManager>();
                if (mapMgr != null) {
                    mapMgr.ServerSetInitialMapIndexIfNeeded();
                }
                SpawnRTSPlayers();
                IsGameStarted.Value = true;
            }
            
            // 取消订阅
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnClientLoadedScene;
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
