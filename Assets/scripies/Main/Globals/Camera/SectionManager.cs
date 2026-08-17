using System;
using UnityEngine;

public class SectionManager : MonoBehaviour
{
    [SerializeField] private GameObject cameraObject;
    
    [SerializeField] private Color backgroundColor = Color.darkSlateGray;

    private void Start()
    {
        cameraObject.SetActive(false);
    }

    private void Update()
    {
        
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        
        if (cameraObject != null) cameraObject.SetActive(true);
        
    }
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && cameraObject != null) cameraObject.SetActive(false);
    }
}