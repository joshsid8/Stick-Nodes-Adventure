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
        SceneDataManager.Instance.Save(ref saveData.sceneData);
        PlayerHealth.Instance.pc.Save(ref saveData.playerCurrentData);
        PlayerManager.Instance.Save(ref saveData.playerStats);
        InvManager.Instance.Save(ref saveData.playerInventory);
        InvManager.Instance.invIndex.Save(ref saveData.playerIndex);
        QuestLogManager.Instance.Save(ref saveData.playerQuests);
        AudioManager.Instance.Save(ref saveData.currentScene);
    }

    public static void HandleSettingsSaveData() => MainMenuManager.Instance.Save(ref saveData.mainMenuData);
    public static void HandleSettingsLoadData()
    {
        AudioManager.Instance.Load(saveData.currentScene);
        MainMenuManager.Instance.Load(saveData.mainMenuData);
    }

    public static void HandleLoadData()
    {
        SceneDataManager.Instance.Load(saveData.sceneData);
        PlayerHealth.Instance.pc.Load(saveData.playerCurrentData);
        PlayerManager.Instance.Load(saveData.playerStats);
        InvManager.Instance.Load(saveData.playerInventory);
        InvManager.Instance.invIndex.Load(saveData.playerIndex);
        QuestLogManager.Instance.Load(saveData.playerQuests);
        AudioManager.Instance.Load(saveData.currentScene);
    }

    public static void Save()
    {
        HandleSaveData();

        try
        {
            var json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(SaveFileName(), json);
            //Debug.Log($"[SaveSystem] Saved to: {SaveFileName()}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to save: {ex.Message}");
        }
    }
    
    public static void SaveSettings() => HandleSettingsSaveData();
    public static void LoadSettings() => HandleSettingsLoadData();

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
            Debug.Log(saveData.currentScene);
            Debug.Log(saveDataString);

            HandleLoadData();
            //Debug.Log($"[SaveSystem] Loaded from: {path}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"[SaveSystem] Failed to load: {ex.Message}");
        }

    }

}
