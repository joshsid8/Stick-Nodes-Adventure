using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class Shenanigans : MonoBehaviour
{
    // This script is meant to manage literally every small function that occurs in this game, similar to the player manager.
    // If it cant be managed anywhere else without it being inconvenient, add it here!
    
    [Header("Stored Object")]
    public GameObject storedObj;
    
    public static Shenanigans Instance {get; private set;}

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public IEnumerator JumpScare(GameObject jsObj = null,Image image = null, float duration = 0.08f, float fadeMult = 1.5f)
    {
        if (jsObj != null)
        {
            if (image != null) jsObj.GetComponent<Image>().sprite = image.sprite;
            jsObj.GetComponent<Image>().color = new Color(1,1,1,1);
            jsObj.GetComponent<Image>().enabled = true;
            yield return new WaitForSeconds(duration);
            while (jsObj.GetComponent<Image>().color.a > 0)
            {
                jsObj.GetComponent<Image>().color -= new Color(1, 0, 0, Time.deltaTime * fadeMult);
                yield return new WaitForFixedUpdate();
            }
            jsObj.GetComponent<Image>().enabled = false;
        }
    }

    public void StoreObject(GameObject obj) => storedObj = obj;
    
    public void ApplyForce(Vector2 force, GameObject obj = null)
    {
        if (storedObj != null)
        {
            storedObj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
            storedObj = null;
        }
        else if (obj != null) obj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
    }
}
