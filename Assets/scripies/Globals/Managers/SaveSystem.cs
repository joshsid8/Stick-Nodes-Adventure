using System.IO;
using System.Net;
using UnityEditor.Overlays;
using UnityEngine;

public class SaveSystem
{
    private static SaveData saveData = new SaveData();

    [System.Serializable]
    public struct SaveData
    {
        public PlayerSaveCurrentData playerCurrentData;
        public PlayerSaveStats playerStats;
        public PlayerSaveInventory playerInventory;
        public PlayerSaveIndex playerIndex;
        public PlayerSaveQuests playerQuests;
    }

    public static string SaveFileName()
    {
        string saveFile = Application.persistentDataPath + "/save" + ".sna";
        return saveFile;
    }
    public static void HandleSaveData()
    {
        PlayerHealth.Instance.pc.Save(ref saveData.playerCurrentData);
        PlayerManager.Instance.Save(ref saveData.playerStats);
        InvManager.Instance.Save(ref saveData.playerInventory);
        InvManager.Instance.Index.Save(ref saveData.playerIndex);
        QuestLogManager.Instance.Save(ref saveData.playerQuests);
    }
    
    public static void HandleLoadData()
    {
        PlayerHealth.Instance.pc.Load(saveData.playerCurrentData);
        PlayerManager.Instance.Load(saveData.playerStats);
        InvManager.Instance.Load(saveData.playerInventory);
        InvManager.Instance.Index.Load(saveData.playerIndex);
        QuestLogManager.Instance.Load(saveData.playerQuests);
    }

    public static void Save()
    {
        HandleSaveData();

        try
        {
            var json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SaveFileName(), json);
            Debug.Log($"[SaveSystem] Saved to: {SaveFileName()}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to save: {ex.Message}");
        }

    }

    public static void Load()
    {
        var path = SaveFileName();
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveSystem] No save file found at: {path}");
            return;
        }

        try
        {
            string saveDataString = File.ReadAllText(path);
            saveData = JsonUtility.FromJson<SaveData>(saveDataString);

            // Apply loaded data to the game
            HandleLoadData();
            Debug.Log($"[SaveSystem] Loaded from: {path}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to load: {ex.Message}");
        }

    } 
    
}
