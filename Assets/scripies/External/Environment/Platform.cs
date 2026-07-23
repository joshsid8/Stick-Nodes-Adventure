using System;
using System.Collections;
using UnityEngine;

public class Platform : MonoBehaviour
{
    [SerializeField] private float InputDirection;
    [SerializeField] private bool tangible;

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.tag == "Player")
        {
            Debug.Log("on!");
            if (InputDirection == -1f) tangible = false;
        }

    }

    private void Update()
    {
        InputDirection = Input.GetAxisRaw("Vertical");
        if (tangible)
        {
            GetComponent<BoxCollider2D>().enabled = true;
        }
        else
        {
            GetComponent<BoxCollider2D>().enabled = false;
        }
    }

    private IEnumerator OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.tag == "Player")
        {
            yield return new WaitForSeconds(0.5f);
            tangible = true;
        }
    }
}
