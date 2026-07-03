using System;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance {get; private set;}
    
    public AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    
    public AudioClip[] backgroundMusic;
    public float[] backgroundMusicVolume;
    public AudioClip[] sFXMusic;
    
    //public float musicVolume;

    public GameObject[] gUI;
    
    [SerializeField] private bool withPlayer = true;
    private PlayerHealth player;

    [SerializeField] private bool toggle;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        musicSource.clip = backgroundMusic[0];
        musicSource.Play();
        if (withPlayer) player = PlayerHealth.Instance;
    }

    // Update is called once per frame
    void Update()
    {
        QuietOnGUI();
        if (player != null) InCombat();
    }

    void InCombat()
    {
        if (player.inCombat && !toggle)
        {
            toggle = true;
            musicSource.Pause();
            musicSource.clip = backgroundMusic[1];
            musicSource.volume = backgroundMusicVolume[1];
            musicSource.Play();
        }
        else if (!player.inCombat)
        {
            toggle = false;
            musicSource.Pause();
            musicSource.clip = backgroundMusic[0];
            musicSource.volume = backgroundMusicVolume[0];
            musicSource.Play();
        }
    }
    void QuietOnGUI()
    {
        for (int i = 0; i < gUI.Length; i++)
        {
            if (gUI[i].activeInHierarchy)
            {
                musicSource.volume = backgroundMusicVolume[0] / 2;
                sfxSource.volume = 0.2f;

            }
            if (!gUI[i].activeInHierarchy)
            {
                musicSource.volume = backgroundMusicVolume[0];
                sfxSource.volume = 1f;
            }
        }
    }
}
