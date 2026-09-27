using System.IO;
using scripies.Player;
using UnityEngine;

/// <summary>
/// The global save system, communicating the data in game to the save files!
/// </summary>
public class SaveSystem
{
    public static int SaveSlot;
    private static SaveData saveData = new()
    {
        sceneData = new SceneData(true),
        globalNpcData = new GlobalNpcData(),
        playerCurrentData = new PlayerSaveCurrentData(),
        playerStats = new PlayerSaveStats(),
        playerInventory = new PlayerSaveInventory(),
        playerIndex = PlayerSaveIndex.CreateNew(),
        playerBadges = new PlayerBadges(),
        playerQuests = new PlayerSaveQuests(),
        mainMenuData = new MainMenuData(),
    };

    [System.Serializable]
    public struct SaveData
    {
        public SceneData sceneData;
        public GlobalNpcData globalNpcData;
        public PlayerSaveCurrentData playerCurrentData;
        public PlayerSaveStats playerStats;
        public PlayerSaveInventory playerInventory;
        public PlayerSaveIndex playerIndex;
        public PlayerSaveQuests playerQuests;
        public PlayerBadges playerBadges;
        public MainMenuData mainMenuData;
    }

    public static string SaveFileName()
    {
        var saveFile = Application.persistentDataPath + "/save" + SaveSlot + ".sna";
        return saveFile;
    }

    public static string SettingsSaveFileName()
    {
        var saveFile = Application.persistentDataPath + "/saveSettings.sna";
        return saveFile;
    }
    public static void SetSaveSlot(int slot = 0) => SaveSlot = slot;

    public static void HandleSaveData(int slot = 0)
    {
        foreach (var map in MapManager.Instances.Values)
        {
            if (map == null) continue;
            map.SaveSceneData();
        }
        if (DataManager.Instance != null) DataManager.Instance.Save(ref saveData.sceneData, ref saveData.globalNpcData);
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.pc != null) PlayerHealth.Instance.pc.Save(ref saveData.playerCurrentData);
        if (PlayerManager.Instance != null) PlayerManager.Instance.Save(ref saveData.playerStats);
        if (InvManager.Instance != null)
        {
            InvManager.Instance.Save(ref saveData.playerInventory);
            if (InvManager.Instance.invIndex != null) InvManager.Instance.invIndex.Save(ref saveData.playerIndex);
        }
        if (QuestLogManager.Instance != null) QuestLogManager.Instance.Save(ref saveData.playerQuests);
        if (StatsManager.Instance != null) StatsManager.Instance.Save(ref saveData.playerBadges);
    }

    public static void HandleSettingsSaveData() => MainMenuManager.Instance.Save(ref saveData.mainMenuData);
    public static void HandleSettingsLoadData()
    {
        MainMenuManager.Instance.Load(saveData.mainMenuData);
    }

    public static void HandleLoadData()
    {
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.pc != null) PlayerHealth.Instance.pc.Load(saveData.playerCurrentData);
        if (PlayerManager.Instance != null) PlayerManager.Instance.Load(saveData.playerStats);
        if (InvManager.Instance != null)
        {
            InvManager.Instance.Load(saveData.playerInventory);
            if (InvManager.Instance.invIndex != null) InvManager.Instance.invIndex.Load(saveData.playerIndex);
        }
        if (QuestLogManager.Instance != null) QuestLogManager.Instance.Load(saveData.playerQuests);
        if (StatsManager.Instance != null) StatsManager.Instance.Load(saveData.playerBadges);
        if (DataManager.Instance != null) DataManager.Instance.Load(saveData.sceneData, saveData.globalNpcData);
        
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
        var path = SettingsSaveFileName();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                saveData = JsonUtility.FromJson<SaveData>(json);
                Debug.Log($"[SaveSystem] Loaded settings from: {path}");
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
            Debug.LogWarning($"[SaveSystem] No save file found at: {path}. Initializing default data.");
            // Initialize default state
            saveData = new SaveData
            {
                sceneData = new SceneData(true),
                globalNpcData = new GlobalNpcData(),
                playerCurrentData = new PlayerSaveCurrentData(),
                playerStats = new PlayerSaveStats(),
                playerInventory = new PlayerSaveInventory(),
                playerIndex = PlayerSaveIndex.CreateNew(),
                playerBadges = new PlayerBadges(),
                playerQuests = new PlayerSaveQuests(),
                mainMenuData = new MainMenuData(),
            };
            HandleLoadData();
            return;
        }

        try
        {
            var saveDataString = File.ReadAllText(path);
            saveData = JsonUtility.FromJson<SaveData>(saveDataString);
            HandleLoadData();
            if (Application.isEditor) Debug.Log($"[SaveSystem] Loaded from: {path}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SaveSystem] Failed to load: {ex.Message}");
        }
    }
    
    public static void DeleteSave(){}
}
