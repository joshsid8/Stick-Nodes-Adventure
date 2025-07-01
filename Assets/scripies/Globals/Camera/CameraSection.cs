using System;
using System.Collections;
using UnityEngine;

public class CameraSection : MonoBehaviour
{
    public Transform originalCameraTarget;
    public Transform newCameraTarget;
    public CameraFollow cf;
    public float camMoveSpeed;
    public float camExitSpeed;
    private float originalCamMoveSpeed;

    void Start()
    {
        originalCamMoveSpeed = camMoveSpeed;
    }
    
    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cf.FollowSpeed = camMoveSpeed;
            cf.Target =  newCameraTarget;
        }
    }
    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cf.FollowSpeed = camExitSpeed;
            cf.Target = originalCameraTarget;
            cf.FollowSpeed = originalCamMoveSpeed;
        }
    }
}