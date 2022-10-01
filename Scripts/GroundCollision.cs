using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GroundCollision : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Agent"))
        {
            ////Debug.Log("Test OK!!"); 
        }
    }
}
