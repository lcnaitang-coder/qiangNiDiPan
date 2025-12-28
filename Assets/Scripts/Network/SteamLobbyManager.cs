using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using System.Collections.Generic;

/// <summary>
/// Steam 大厅与网络连接管理器
/// </summary>
public class SteamLobbyManager : MonoBehaviour {
    
    public static SteamLobbyManager Singleton { get; private set; }

    // Facepunch.Steamworks: Lobby struct is in Steamworks.Data namespace
    public SteamId CurrentLobbyID { get; private set; }
    
    private const string HOST_ADDRESS_KEY = "HostAddress";

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() {
        // Facepunch.Steamworks 事件订阅
        SteamMatchmaking.OnLobbyCreated += OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        
        Debug.Log("[SteamLobbyManager] Callbacks registered (Facepunch.Steamworks style).");
    }
    
    private void OnDestroy() {
        SteamMatchmaking.OnLobbyCreated -= OnLobbyCreated;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
    }

    /// <summary>
    /// 创建大厅 (Host)
    /// </summary>
    public async void CreateLobby() {
        Debug.Log("[SteamLobbyManager] Creating Steam Lobby...");
        // Facepunch.Steamworks 异步创建大厅
        var lobby = await SteamMatchmaking.CreateLobbyAsync(4);
        
        if (!lobby.HasValue) {
             Debug.LogError("[SteamLobbyManager] Lobby creation failed.");
             return;
        }
        
        // 注意：Facepunch.Steamworks 的 CreateLobbyAsync 不会自动触发 OnLobbyCreated 事件
        // 所以我们需要手动调用或者直接处理逻辑
        // 这里直接处理逻辑：
        OnLobbyCreated(Result.OK, lobby.Value); // 模拟回调或直接调用处理函数
    }

    /// <summary>
    /// 回调：大厅创建完成
    /// </summary>
    private void OnLobbyCreated(Result result, Lobby lobby) {
        if (result != Result.OK) {
            Debug.LogError($"[SteamLobbyManager] Lobby creation failed: {result}");
            return;
        }

        CurrentLobbyID = lobby.Id;
        Debug.Log($"[SteamLobbyManager] Lobby Created! ID: {CurrentLobbyID}");

        // 1. 设置大厅数据：Host 的 SteamID
        lobby.SetData(HOST_ADDRESS_KEY, SteamClient.SteamId.ToString());
        lobby.SetPublic(); // 确保大厅是公开或好友可见
        lobby.SetJoinable(true);

        // 2. 启动 Netcode Host
        NetworkManager.Singleton.StartHost();
        
        // 3. 加载到大厅场景
        NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
    
    /// <summary>
    /// 回调：当用户在 Steam 好友列表右键点击“加入游戏”时触发
    /// </summary>
    private void OnGameLobbyJoinRequested(Lobby lobby, SteamId steamId) {
        Debug.Log($"[SteamLobbyManager] Request to join lobby: {lobby.Id}");
        // 尝试加入该大厅
        lobby.Join();
    }

    /// <summary>
    /// 通过 ID 加入大厅
    /// </summary>
    public async void JoinLobbyByID(string lobbyIDString) {
        if (ulong.TryParse(lobbyIDString, out ulong lobbyID)) {
            Debug.Log($"[SteamLobbyManager] Attempting to join lobby ID: {lobbyID}");
            var lobby = await SteamMatchmaking.JoinLobbyAsync(lobbyID);
            
            if (!lobby.HasValue) {
                 Debug.LogError("[SteamLobbyManager] Failed to join lobby.");
                 return;
            }
            
            // 成功加入后，SteamMatchmaking.OnLobbyEntered 回调会被触发，
            // 所以这里不需要手动处理场景跳转逻辑，交给 OnLobbyEntered 处理即可。
            Debug.Log($"[SteamLobbyManager] Successfully joined lobby: {lobby.Value.Id}");
        } else {
            Debug.LogError("[SteamLobbyManager] Invalid Lobby ID format.");
        }
    }

    /// <summary>
    /// 回调：成功进入大厅 (Client & Host 都会触发)
    /// </summary>
    private void OnLobbyEntered(Lobby lobby) {
        // 1. 如果是 Host (Netcode 已启动且为主机)，直接返回
        if (NetworkManager.Singleton.IsHost) return;

        // 2. 检查我们是否是该大厅的 Owner (防止创建者在 StartHost 前触发此回调，或者重连导致的问题)
        // 注意：Steam Networking 不支持自己连接自己，所以必须区分 Host 和 Client
        if (lobby.Owner.Id == SteamClient.SteamId) {
            Debug.Log("[SteamLobbyManager] We are the lobby owner. Treating as Host (skipping Client logic).");
            return;
        }

        CurrentLobbyID = lobby.Id;
        Debug.Log($"[SteamLobbyManager] Entered Lobby: {CurrentLobbyID}");

        // 3. 获取 Host 的 SteamID
        string hostAddress = lobby.GetData(HOST_ADDRESS_KEY);
        if (string.IsNullOrEmpty(hostAddress)) {
            Debug.LogError("[SteamLobbyManager] Lobby data 'HostAddress' is missing!");
            return;
        }
        
        Debug.Log($"[SteamLobbyManager] Host SteamID found: {hostAddress}. Connecting...");

        // 4. 设置 Transport 目标地址
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        
        // FacepunchTransport 设置
        var facepunchTransportType = transport.GetType();
        var targetIdField = facepunchTransportType.GetField("targetSteamId");
        if (targetIdField != null) {
            if (ulong.TryParse(hostAddress, out ulong steamId)) {
                // 安全检查：防止连接自己
                if (steamId == SteamClient.SteamId) {
                    Debug.LogError("[SteamLobbyManager] Cannot connect to self! Are you using the same Steam account for Client and Host?");
                    return;
                }

                targetIdField.SetValue(transport, steamId);
                Debug.Log($"[SteamLobbyManager] Set FacepunchTransport targetSteamId to {steamId}");
            }
        }

        // 5. 启动 Netcode Client
        NetworkManager.Singleton.StartClient();
    }
    
    /// <summary>
    /// 打开 Steam 邀请面板
    /// </summary>
    public void OpenInviteOverlay() {
        if (CurrentLobbyID.Value != 0) {
            SteamFriends.OpenGameInviteOverlay(CurrentLobbyID);
        } else {
            Debug.LogWarning("[SteamLobbyManager] No active lobby to invite to.");
        }
    }
}
