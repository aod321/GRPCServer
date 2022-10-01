using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderTrigger : MonoBehaviour
{
    public float Reward = 0.0F;
    public byte Done = 0;
    public bool AlreadyDrop = false;


    void FixedUpdate()
    {
        if (transform.position.y < 0)
        {
            // stop game if drop 
            if (!AlreadyDrop)
            {
                AlreadyDrop = true;
                Reward = 0.0f;
                Done = 1;
            } 
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.gameObject.CompareTag("Food"))
        {
            Reward = 1.0F;
            Done = 1;
        }
    }
    // private void OnTriggerEnter(Collider other)
    // {
    //     if(other.CompareTag("Food"))
    //     {
    //         Reward += 1.0F;
    //         Done = 1;
    //     }
    // }
    
    void OnTriggerExit(Collider other)
    {
    }
}
