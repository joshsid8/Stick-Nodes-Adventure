using System;
using UnityEngine;

public class CameraSection : MonoBehaviour
{
    [SerializeField] GameObject cameraObject;

    private void Start()
    {
        cameraObject.SetActive(false);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cameraObject.SetActive(true);
        }
    }
    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cameraObject.SetActive(false);
        }
    }
}