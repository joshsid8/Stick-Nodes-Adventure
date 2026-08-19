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
    
    public string[] mapDisasterNames = { "Temperature", "Earthquake", "Wind", "Thunderstorm" };
    public float[] mapDisasterFactors = new float[4];
    
    private WeatherManager weather;
    private AudioManager audioManager;
    #endregion
    
    [SerializeField] private GameObject cameraObject;
    
    private void Start()
    {
        cameraObject.SetActive(false);
        weather = GetComponent<WeatherManager>();
        audioManager = AudioManager.Instance;
    }
    
    public void UpdateNormalAreaInfo() => UpdateAreaInfo(sectionName, sectionTextColor, sectionBgColor, newOst, ostVolume);
    public void UpdateAreaInfo(string mName, Color mTextColor = default, Color mBgColor = default, AudioClip[] mOst = null, float[] mOstVolume = null)
    {
        if (!announceDetails) return;
        AreaManager.Instance.Announce(mName, "", mTextColor, mBgColor);

        // Below is from map manager, and i dont think its needed here
        
        /*for (var i = 0; i < audioManager.backgroundMusic.Length; i++)
        {
            audioManager.backgroundMusic[i] = mOst[i];
            audioManager.backgroundMusicVolume[i] = mOstVolume[i];
        }
        audioManager.currentScene = gameObject.scene.name;
        GameEvents.TriggerMusic(0, false);*/
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (weather != null) weather.SetWeather();
        if (cameraObject != null) cameraObject.SetActive(true);
        
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && cameraObject != null) cameraObject.SetActive(false);
    }
}