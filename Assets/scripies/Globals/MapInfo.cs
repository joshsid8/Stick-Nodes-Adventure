using UnityEngine.SceneManagement;
using UnityEngine;

public class MapInfo : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerHealth.Instance.currentScene = SceneManager.GetActiveScene().name;
            print($"the scene is now based on {SceneManager.GetActiveScene().name}!");
        }
    }
}
