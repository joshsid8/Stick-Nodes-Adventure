using System;
using System.Collections;
using UnityEngine;
using scripies.Core;
using UnityEngine.UI;

public class Shenanigans : MonoBehaviour
{
    // This script is meant to manage literally every small function that occurs in this game, similar to the player manager.
    // If it cant be managed anywhere else without it being inconvenient, add it here!

    [Header("Jump Scare Object")] 
    [SerializeField] private Image jkObjImage;
    
    [Header("Stored Object")]
    public GameObject storedObj;
    
    [Header("Freeze Camera")]
    public bool freezeCamera;
    [SerializeField] private GameObject camObj;
    
    public static Shenanigans Instance {get; private set;}

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        GameEvents.JumpScare += JumpScare;
        GameEvents.StopTime += StopTime;
    }

    private void Update()
    {
        camObj.SetActive(!freezeCamera);
    }

    private void StopTime(float time = 0.2f) => StartCoroutine(StopTimeC(time));
    
    private static IEnumerator StopTimeC(float time = 0.2f)
    {
        Time.timeScale = 0;
        yield return new WaitForSecondsRealtime(time);
        Time.timeScale = 1;
    }

    private void JumpScare(Image image, float duration, float fadeMult) => StartCoroutine(StartJumpScare(image, duration, fadeMult)); 

    private IEnumerator StartJumpScare(Image image, float duration, float fadeMult)
    {
        if (jkObjImage != null)
        {
            if (image != null) jkObjImage.sprite = image.sprite;
            jkObjImage.color = new Color(1,1,1,1);
            jkObjImage.enabled = true;
            yield return new WaitForSeconds(duration);
            while (jkObjImage.color.a > 0)
            {
                jkObjImage.color -= new Color(1, 0, 0, Time.deltaTime * fadeMult);
                yield return new WaitForFixedUpdate();
            }
            jkObjImage.enabled = false;
        }
    }

    public void StoreObject(GameObject obj) => storedObj = obj;
    
    /// <summary>
    ///  Applies a force to a stored object, or any object thats in the radius if otherwise.
    /// </summary>
    public void ApplyForce(Vector2 force, float radius = 0, GameObject obj = null, Transform area = null)
    {
        if (storedObj != null)
        {
            storedObj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
            storedObj = null;
        }
        else if (obj != null) obj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
        else if (area != null)
        {
           var hit = Physics2D.CircleCast(area.position, radius, new Vector2(area.transform.right.x, area.transform.up.y), 0f, LayerMask.GetMask("FX", "EVFX", "PVFX"));
           if (hit.collider != null) hit.collider.gameObject.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
        }
    }
    
    public IEnumerator FreezeCameraTime(float freezeTime)
    {
        freezeCamera = true;
        yield return new WaitForSecondsRealtime(freezeTime);
        freezeCamera = false;
    }
    
    public void ToggleFreezeCamera(bool freeze = true) => freezeCamera = freeze;
}
