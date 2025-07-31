using System;
using UnityEngine.SceneManagement;
using UnityEngine;

public class MapInfo : MonoBehaviour
{
    [SerializeField] private AudioClip[] newOst;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerHealth.Instance.currentScene = SceneManager.GetActiveScene().name;
            
            AudioManager.Instance.musicSource.Stop();
            AudioManager.Instance.backgroundMusic[0] = newOst[0];
            AudioManager.Instance.backgroundMusic[1] = newOst[1];
            AudioManager.Instance.backgroundMusic[2] = newOst[2];
            AudioManager.Instance.musicSource.Play();
        }
    }
}
