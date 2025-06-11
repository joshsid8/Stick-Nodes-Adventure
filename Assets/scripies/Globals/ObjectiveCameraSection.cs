using System;
using System.Collections;
using UnityEngine;

public class ObjectiveCameraSection : MonoBehaviour
{
    public Transform originalCameraTarget;
    public Transform newCameraTarget;
    public CameraFollow cf;
    public float camMoveSpeed;
    public float camExitSpeed;
    private float originalCamMoveSpeed;
    public ObjectiveDoor od;
    public bool willDestroyCam;
    void Start()
    {
        originalCamMoveSpeed = camMoveSpeed;
        od.objectiveComplete = false;
    }

    private void Update()
    {
        if (od.objectiveComplete)
        {
            if (willDestroyCam)
            {
                cf.FollowSpeed = camExitSpeed;
                cf.Target = originalCameraTarget;
                cf.FollowSpeed = originalCamMoveSpeed;
                Destroy(gameObject);
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cf.FollowSpeed = camMoveSpeed;
            cf.Target =  newCameraTarget;
        }
        if (collision.CompareTag("Enemy") || collision.CompareTag("Boss")) od.objectiveComplete = false;
    }
    
    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            cf.FollowSpeed = camExitSpeed;
            cf.Target = originalCameraTarget;
            cf.FollowSpeed = originalCamMoveSpeed;
        }
        if (collision.CompareTag("Dead") && willDestroyCam)
        {
            od.objectiveComplete = true;
        }
    }

    void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Dead") && !willDestroyCam)
        {
            od.objectiveComplete = true;
        }
    }
}