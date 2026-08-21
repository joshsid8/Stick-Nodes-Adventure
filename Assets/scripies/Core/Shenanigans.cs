using System;
using System.Collections;
using UnityEngine;
using scripies.Core;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
///  This script is meant to manage literally every small function that occurs in this game, similar to the player manager.
///  If it cant be managed anywhere else without it being inconvenient, add it here!
/// </summary>
public class Shenanigans : MonoBehaviour
{

    [Header("Jump Scare Object")] 
    [SerializeField] private Image jsObjImage;
    [SerializeField] private Sprite[] jsImages;
    
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
        //camObj.SetActive(!freezeCamera);
        Time.timeScale = freezeCamera ? 0 : 1;
    }

    private void StopTime(float time = 0.2f) => StartCoroutine(StopTimeC(time));
    
    private IEnumerator StopTimeC(float time = 0.2f)
    {
        freezeCamera = true;
        yield return new WaitForSecondsRealtime(time);
        freezeCamera = false;
    }

    private void JumpScare(Image image, float duration, float fadeMult) => StartCoroutine(StartJumpScare(image, duration, fadeMult)); 

    private IEnumerator StartJumpScare(Image image, float duration, float fadeMult)
    {
        if (jsObjImage != null)
        {
            jsObjImage.sprite = image == null ? jsImages[Random.Range(0, jsImages.Length)] : image.sprite;
            jsObjImage.color = new Color(1,1,1,1);
            jsObjImage.enabled = true;
            yield return new WaitForSeconds(duration);
            while (jsObjImage.color.a > 0)
            {
                jsObjImage.color -= new Color(0, 0, 0, Time.deltaTime * fadeMult);
                yield return new WaitForFixedUpdate();
            }
            jsObjImage.enabled = false;
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

    public void RandomAttribute(float fidelity = 1, IDamageable target = null)
    {
        var rng = Random.Range(0, 2);
        float[] rng2 = { Random.Range(0, 15), Random.Range(0, 2), Random.Range(0, 10), Random.Range(0, 4)};
        switch (rng)
        {
            case 0:
                target?.Damage(rng2[0] * fidelity, true, 2, IDamageable.ElementType.Normal, 1, true, rng2[1] == 0, rng2[2], 0.1f);
                target?.Shock(rng2[3]);
                break;
            case 1:
                target?.Heal(rng2[0]);
                break;
        }
    }
}
