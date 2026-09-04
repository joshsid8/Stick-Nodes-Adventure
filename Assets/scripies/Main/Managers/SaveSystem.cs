using System.IO;
using scripies.Player;
using UnityEngine;

public class SaveSystem
{
    private static SaveData saveData = new()
    {
        sceneData = new SceneData(true),
        playerCurrentData = new PlayerSaveCurrentData(),
        playerStats = new PlayerSaveStats(),
        playerInventory = new PlayerSaveInventory(),
        playerIndex = PlayerSaveIndex.CreateNew(),
        playerQuests = new PlayerSaveQuests(),
        mainMenuData = new MainMenuData(),
        currentScene = new SceneName()
    };

    [System.Serializable]
    public struct SaveData
    {
        public SceneData sceneData;
        public PlayerSaveCurrentData playerCurrentData;
        public PlayerSaveStats playerStats;
        public PlayerSaveInventory playerInventory;
        public PlayerSaveIndex playerIndex;
        public PlayerSaveQuests playerQuests;
        public MainMenuData mainMenuData;
        public SceneName currentScene;
    }

    public static string SaveFileName()
    {
        var saveFile = Application.persistentDataPath + "/save" + ".sna";
        return saveFile;
    }

    public static void HandleSaveData()
    {
        foreach (var map in MapManager.Instances.Values)
        {
            if (map != null)
            {
                map.SaveSceneData();
                map.MakeDataPersistent();
            }
        }
        if (DataManager.Instance != null) DataManager.Instance.Save(ref saveData.sceneData);
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.pc != null) PlayerHealth.Instance.pc.Save(ref saveData.playerCurrentData);
        if (PlayerManager.Instance != null) PlayerManager.Instance.Save(ref saveData.playerStats);
        if (InvManager.Instance != null)
        {
            InvManager.Instance.Save(ref saveData.playerInventory);
            if (InvManager.Instance.invIndex != null) InvManager.Instance.invIndex.Save(ref saveData.playerIndex);
        }
        if (QuestLogManager.Instance != null) QuestLogManager.Instance.Save(ref saveData.playerQuests);
        if (AudioManager.Instance != null) AudioManager.Instance.Save(ref saveData.currentScene);
    }

    public static void HandleSettingsSaveData() => MainMenuManager.Instance.Save(ref saveData.mainMenuData);
    public static void HandleSettingsLoadData()
    {
        AudioManager.Instance.Load(saveData.currentScene);
        MainMenuManager.Instance.Load(saveData.mainMenuData);
    }

    public static void HandleLoadData()
    {
        if (DataManager.Instance != null) DataManager.Instance.Load(saveData.sceneData);
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.pc != null) PlayerHealth.Instance.pc.Load(saveData.playerCurrentData);
        if (PlayerManager.Instance != null) PlayerManager.Instance.Load(saveData.playerStats);
        if (InvManager.Instance != null)
        {
            InvManager.Instance.Load(saveData.playerInventory);
            if (InvManager.Instance.invIndex != null) InvManager.Instance.invIndex.Load(saveData.playerIndex);
        }
        if (QuestLogManager.Instance != null) QuestLogManager.Instance.Load(saveData.playerQuests);
        if (AudioManager.Instance != null) AudioManager.Instance.Load(saveData.currentScene);
        
        scripies.Core.GameEvents.TriggerLoadMapData();
    }

    public static void Save()
    {
        if (DataManager.Instance == null || Application.isEditor && PlayerHealth.Instance.pc.debugMode) return;
        HandleSaveData();

        try
        {
            var json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SaveFileName(), json);
            if (Application.isEditor) Debug.Log($"[SaveSystem] Saved to: {SaveFileName()}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to save: {ex.Message}");
        }
    }
    
    public static void SaveSettings() => HandleSettingsSaveData();
    public static void LoadSettings()
    {
        var path = SaveFileName();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                saveData = JsonUtility.FromJson<SaveData>(json);
            }
            catch (IOException ex)
            {
                Debug.LogError($"[SaveSystem] Failed to load settings: {ex.Message}");
            }
        }
        HandleSettingsLoadData();
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
            var saveDataString = File.ReadAllText(path);
            saveData = JsonUtility.FromJson<SaveData>(saveDataString);
            HandleLoadData();
            //Debug.Log($"[SaveSystem] Loaded from: {path}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to load: {ex.Message}");
        }

    }

}
