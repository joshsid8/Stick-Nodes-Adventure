using System;
using scripies.Core;
using UnityEngine;

public class SectionManager : MonoBehaviour
{
    #region Section Info

    public bool announceDetails;
    [SerializeField] private string sectionName;
    //[SerializeField] [TextArea] private string sectionDescription;
    [SerializeField] private Color sectionTextColor = Color.white;
    [SerializeField] private Color sectionBgColor = Color.darkSlateGray;
    
    [SerializeField] private AudioClip[] newOst;
    public float[] ostVolume = {0.5f,0.5f,0.5f};
    [SerializeField] private bool muteMusic;
    
    public string[] mapDisasterNames = { "Temperature", "Earthquake", "Wind", "Thunderstorm" };
    public float[] mapDisasterFactors = new float[4];
    #endregion
    
    public enum SectionType {Default, Endangerment, Boss}
    [EnumButtons] public SectionType sectionType;
    [SerializeField] private AudioClip endangeredOst;
    
    public static SectionManager ActiveSection { get; private set; }
    public bool activeSection;
    public bool MuteMusic => muteMusic;

    private WeatherManager weather;
    private AudioManager audioManager;
    
    [SerializeField] private GameObject cameraObject;
    private Color prevMapBgColor;
    private string prevAreaName;

    private void Start()
    {
        if (cameraObject != null) cameraObject.SetActive(false);
        weather = GetComponent<WeatherManager>();
        audioManager = AudioManager.Instance;
    }

    private void OnDisable()
    {
        if (ActiveSection == this) ActiveSection = null;
        activeSection = false;
    }
    
    public void UpdateNormalAreaInfo() => UpdateAreaInfo(sectionName, sectionTextColor, sectionBgColor, newOst, ostVolume);
    public void UpdateAreaInfo(string mName, Color mTextColor = default, Color mBgColor = default, AudioClip[] mOst = null, float[] mOstVolume = null)
    {
        if (!announceDetails) return;
        if (AreaManager.Instance != null)
        {
            if (mName != AreaManager.Instance.currentArea)
                AreaManager.Instance.Announce(mName, "", mTextColor, mBgColor, true);
            else if (mBgColor != default)
                AreaManager.Instance.TriggerColorFade(mBgColor);
        }
        
        if (audioManager == null) audioManager = AudioManager.Instance;
        if (mOst == null || mOstVolume == null || muteMusic || audioManager == null) return;
        if (mOst.Length == 0) return;

        var count = Mathf.Min(audioManager.backgroundMusic.Length, mOst.Length);
        for (var i = 0; i < count; i++)
        {
            audioManager.backgroundMusic[i] = mOst[i];
            if (i < mOstVolume.Length)
                audioManager.backgroundMusicVolume[i] = mOstVolume[i];
        }
        if (audioManager.musicType != IAudioStatus.MusicType.Boss)
            GameEvents.TriggerPlayMusic(0, false);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player") || activeSection) return;
        activeSection = true;
        ActiveSection = this;

        if (cameraObject != null) cameraObject.SetActive(true);
        if (AreaManager.Instance != null)
        {
            prevAreaName = AreaManager.Instance.currentArea;
            if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
                prevMapBgColor = mm.mapBgColor;
            else if (AreaManager.Instance.actualCamera != null)
                prevMapBgColor = AreaManager.Instance.actualCamera.backgroundColor;
        }

        if (weather != null) weather.ChangeWeather(weather.weatherType);
        else if (announceDetails) UpdateNormalAreaInfo();

        if (audioManager == null) audioManager = AudioManager.Instance;
        if (muteMusic && audioManager != null && audioManager.musicType != IAudioStatus.MusicType.Boss)
            audioManager.ToggleMuteMusic(true);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player") || !activeSection) return;
        if (TryGetComponent<Collider2D>(out var col) && col.OverlapPoint(collision.transform.position)) return;

        activeSection = false;
        if (ActiveSection == this) ActiveSection = null;

        if (cameraObject != null) cameraObject.SetActive(false);
        if (weather != null) weather.ClearDisasters();

        if (audioManager == null) audioManager = AudioManager.Instance;
        if (muteMusic && audioManager != null) audioManager.ToggleMuteMusic(false);

        if (AreaManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(prevAreaName))
                AreaManager.Instance.currentArea = prevAreaName;
            if (prevMapBgColor != default)
                AreaManager.Instance.TriggerColorFade(prevMapBgColor);
        }
    }
}
