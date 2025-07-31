using System;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    public bool oneTime;
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerHealth.Instance.playerController.currentCheckPoint = gameObject;
            if (oneTime) GetComponent<Collider2D>().enabled = false;
        }
    }
}
