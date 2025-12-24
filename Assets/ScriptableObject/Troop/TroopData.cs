using UnityEngine;

[CreateAssetMenu(fileName = "NewTroopData", menuName = "Game/TroopData")]
public class TroopData : ScriptableObject {
    [Header("Identity")]
    public int troopID; // Unique Identifier
    public string unitName = "Soldier";
    
    [Header("Visuals")]
    public GameObject visualPrefab; // Client-side visual model

    [Header("Stats")]
    public int maxHealth = 10;
    public int attackPower = 1; // Damage per unit
    public float attackRange = 1.0f;
    public float moveSpeed = 5.0f;
    public int level = 1;

    // Future roguelike stats can be added here
    // public float critChance;
    // public float defense;
}
