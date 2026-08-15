using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TileCatalog", menuName = "ScriptableObjects/TileCatalog")]
public class TileCatalog : ScriptableObject
{
    public List<GameObject> floorTiles;
    public List<GameObject> wallTiles;
    public List<GameObject> obstacleTiles;
}