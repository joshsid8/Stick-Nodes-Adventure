using UnityEngine;
using Unity.Cinemachine;

public class MapInfo : MonoBehaviour
{
    [SerializeField] private string mapName;
    [SerializeField] [TextArea] private string mapDescription;
    [SerializeField] private Color mapTextColor = Color.white;
    [SerializeField] private Color mapBgColor = Color.white;

    [SerializeField] private AudioClip[] newOst;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && AreaManager.Instance.titleText.text != mapName || collision.CompareTag("Player") && AreaManager.Instance.titleText.text != null)
        {
            AreaManager.Instance.StartCoroutine(AreaManager.Instance.Announce(mapName, mapDescription, mapTextColor,
                mapBgColor));

            AudioManager.Instance.musicSource.Stop();
            AudioManager.Instance.backgroundMusic[0] = newOst[0];
            AudioManager.Instance.backgroundMusic[1] = newOst[1];
            AudioManager.Instance.backgroundMusic[2] = newOst[2];
            AudioManager.Instance.musicSource.Play();

            GameObject.Find("Main Camera").GetComponent<CinemachineConfiner2D>().BoundingShape2D =
                GetComponent<Collider2D>();
        }
    }
}