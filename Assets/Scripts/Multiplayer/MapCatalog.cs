using System.Collections.Generic;
using UnityEngine;

/// <summary>선택 가능한 맵 목록 (Resources/MapCatalog)</summary>
[CreateAssetMenu(menuName = "TTT/Map Catalog", fileName = "MapCatalog")]
public class MapCatalog : ScriptableObject
{
    public List<MapEntry> maps = new List<MapEntry>();

    static MapCatalog cached;

    public static MapCatalog Load()
    {
        if (cached == null)
            cached = Resources.Load<MapCatalog>("MapCatalog");
        return cached;
    }

    public MapEntry Get(int index)
    {
        if (maps.Count == 0)
            return null;
        return maps[Mathf.Clamp(index, 0, maps.Count - 1)];
    }

    public MapEntry FindByScene(string sceneName)
    {
        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i].available && maps[i].sceneName == sceneName)
                return maps[i];
        }
        return null;
    }
}
