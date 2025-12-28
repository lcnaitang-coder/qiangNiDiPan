using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Netcode.Transports.Facepunch; // 确保已安装 Facepunch Transport 包

namespace Network {
    /// <summary>
    /// 传输层切换器
    /// 允许在本地调试 (UnityTransport) 和 Steam P2P (FacepunchTransport) 之间一键切换。
    /// 挂载在 NetworkManager 物体上。
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class TransportSwitcher : MonoBehaviour {
        
        [Header("Transport Settings")]
        [Tooltip("勾选使用 Steam P2P，不勾选使用本地 UnityTransport (适合 ParrelSync 调试)")]
        public bool useSteam = false;

        private void Awake() {
            ConfigureTransport();
        }

        private void ConfigureTransport() {
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager == null) {
                Debug.LogError("[TransportSwitcher] NetworkManager not found!");
                return;
            }

            // 1. 清理现有的 Transport 组件
            // 注意：Destroy 是延迟销毁，但我们在同一帧添加新组件通常没问题，
            // 只要 NetworkManager 在 Start/OnEnable 中才去读取 Transport。
            // 为了安全起见，我们也可以手动移除引用。
            
            var existingFacepunch = GetComponent<FacepunchTransport>();
            if (existingFacepunch != null) {
                DestroyImmediate(existingFacepunch); // 使用 Immediate 确保立即移除
            }

            var existingUTP = GetComponent<UnityTransport>();
            if (existingUTP != null) {
                DestroyImmediate(existingUTP);
            }

            NetworkTransport newTransport = null;

            // 2. 根据设置添加新 Transport
            if (useSteam) {
                // --- Steam P2P Mode ---
                var fpTransport = gameObject.AddComponent<FacepunchTransport>();
                newTransport = fpTransport;
                Debug.Log("[TransportSwitcher] 已切换为 Steam P2P 模式 (FacepunchTransport)");
            } else {
                // --- Local Dev Mode ---
                var utp = gameObject.AddComponent<UnityTransport>();
                utp.ConnectionData.Address = "127.0.0.1";
                utp.ConnectionData.Port = 7777;
                utp.ConnectionData.ServerListenAddress = "0.0.0.0";
                newTransport = utp;
                Debug.Log("[TransportSwitcher] 已切换为本地开发模式 (UnityTransport)");
            }

            // 3. 绑定到 NetworkManager
            // 注意：NetworkConfig 在 Awake 时可能为空，需要检查
            if (networkManager.NetworkConfig == null) {
                // 如果 NetworkConfig 还没初始化（通常 Inspector 配置的会自动初始化），
                // 我们可能需要等待，但通常 Awake 阶段它已经反序列化了。
                Debug.LogWarning("[TransportSwitcher] NetworkConfig is null during Awake. attempting to set via reflection or late binding if needed.");
            } else {
                networkManager.NetworkConfig.NetworkTransport = newTransport;
            }
        }
    }
}
