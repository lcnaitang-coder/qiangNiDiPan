using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Steamworks;

/// <summary>
/// 大厅玩家状态
/// 挂载在 Player Prefab 上。
/// 负责在大厅中同步玩家的准备状态和名称，并随玩家进入游戏场景。
/// </summary>
public class LobbyPlayerState : NetworkBehaviour {

    // 同步变量：是否准备好
    public NetworkVariable<bool> IsReady = new NetworkVariable<bool>(false);

    // 同步变量：玩家名称 (使用 FixedString32Bytes 以便网络传输)
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>("");

    // 同步变量：Steam ID
    public NetworkVariable<ulong> SteamId = new NetworkVariable<ulong>(0);

    // 快捷访问 ClientId
    public ulong ClientId => OwnerClientId;

    public override void OnNetworkSpawn() {
        // 确保此对象在场景切换时不会被销毁
        // 注意：如果这是 NetworkManager 的 PlayerPrefab，Netcode 默认会带着它跨场景。
        // 如果不是，则需要手动 DontDestroyOnLoad。
        // 这里假设它是 PlayerPrefab。
        DontDestroyOnLoad(gameObject);

        if (IsOwner) {
            // 本地玩家初始化自己的名字
            SetPlayerNameServerRpc($"Player {OwnerClientId}");

            // 如果 Steam 有效，同步 SteamID
            if (SteamClient.IsValid) {
                SetSteamIdServerRpc(SteamClient.SteamId);
            }
        }
    }

    /// <summary>
    /// 切换准备状态 (Client -> Server)
    /// </summary>
    public void ToggleReady() {
        if (IsOwner) {
            ToggleReadyServerRpc();
        }
    }

    [ServerRpc]
    private void ToggleReadyServerRpc() {
        IsReady.Value = !IsReady.Value;
    }

    [ServerRpc]
    private void SetPlayerNameServerRpc(string name) {
        PlayerName.Value = new FixedString32Bytes(name);
    }

    [ServerRpc]
    private void SetSteamIdServerRpc(ulong steamId) {
        SteamId.Value = steamId;
    }
}
