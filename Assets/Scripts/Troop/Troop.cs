using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class Troop : NetworkBehaviour {
    // NetworkVariables for syncing state to clients automatically
    public NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>();
    public NetworkVariable<ulong> netOwnerId = new NetworkVariable<ulong>();
    public NetworkVariable<int> netTroopCount = new NetworkVariable<int>(1); // Sync troop count for visuals
    public NetworkVariable<int> troopTypeId = new NetworkVariable<int>(-1); // Sync troop type for visual prefab

    // Server-side only data for logic
    private ulong targetBuildingId;
    private float moveSpeed;
    private int troopCount;
    private int attackPower;

    private TroopVisualController _visualController;

    private void Awake() {
        _visualController = GetComponent<TroopVisualController>();
    }

    public override void OnNetworkSpawn() {
        if (IsServer) {
            // Server initializes position
            netPosition.Value = transform.position;
        } else {
            // Client snaps to current network position immediately
            transform.position = netPosition.Value;
        }
    }

    public override void OnNetworkDespawn() {
        if (_visualController != null) {
            _visualController.OnDespawn();
        }
        base.OnNetworkDespawn();
    }

    // Initialize the troop (Server side only)
    public void Initialize(ulong targetId, ulong ownerId, TroopData data, int count) {
        if (!IsServer) return;
        if (data == null) return;

        targetBuildingId = targetId;
        moveSpeed = data.moveSpeed;
        troopCount = count;
        attackPower = data.attackPower;
        
        // Set NetworkVariables (will sync to clients)
        netOwnerId.Value = ownerId;
        netPosition.Value = transform.position;
        netTroopCount.Value = count;
        troopTypeId.Value = data.troopID;
    }

    void Update() {
        if (IsServer) {
            // --- SERVER LOGIC ---
            // Find the target building
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetBuildingId, out NetworkObject targetObj)) {
                Vector3 targetPos = targetObj.transform.position;
                
                // Move towards target
                float step = moveSpeed * Time.deltaTime;
                transform.position = Vector3.MoveTowards(transform.position, targetPos, step);
                
                // Update NetworkVariable
                netPosition.Value = transform.position;

                // Check distance
                if (Vector3.Distance(transform.position, targetPos) < 0.1f) {
                    Building building = targetObj.GetComponent<Building>();
                    if (building != null) {
                        // Trigger arrival logic
                        building.OnTroopArrive(netOwnerId.Value, troopCount, attackPower);
                    }
                    // Destroy self
                    NetworkObject.Despawn();
                }
            } else {
                // Target destroyed or missing, despawn troop
                NetworkObject.Despawn();
            }
        } 
        else {
            // --- CLIENT LOGIC ---
            // Interpolate position for smoothness
            transform.position = Vector3.Lerp(transform.position, netPosition.Value, Time.deltaTime * 10f);
        }
    }
}
