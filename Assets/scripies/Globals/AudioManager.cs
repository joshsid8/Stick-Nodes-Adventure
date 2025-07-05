using System;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    
    public AudioClip[] backgroundMusic;
    public AudioClip[] sFXMusic;
    
    public float musicVolume;

    public GameObject[] gUI;
    
    private PlayerController player;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        musicSource.clip = backgroundMusic[0];
        musicSource.Play();
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerController>();
    }

    // Update is called once per frame
    void Update()
    {
        QuietOnGUI();
        InCombat();
    }

    void InCombat()
    {
        if (player.inCombat)
        {
            musicSource.Pause();
            musicSource.clip = backgroundMusic[1];
            musicSource.Play();
        }
        else
        {
            musicSource.Pause();
            musicSource.clip = backgroundMusic[0];
            musicSource.Play();
        }
    }
    void QuietOnGUI()
    {
        for (int i = 0; i < gUI.Length; i++)
        {
            if (gUI[i].activeInHierarchy)
            {
                musicSource.volume = musicVolume;
                sfxSource.volume = 0.2f;

            }
            if (!gUI[i].activeInHierarchy)
            {
                musicSource.volume = 1f;
                sfxSource.volume = 1f;
            }
        }
    }
}
