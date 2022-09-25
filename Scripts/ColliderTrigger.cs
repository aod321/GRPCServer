using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderTrigger : MonoBehaviour
{
    public float Reward = 0.0F;
    public byte collided = 0;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (transform.position.y < 0)
        {
            // stop game if drop 
            Reward = 0.0f;
            collided = 1;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if(other.name == "Sphere01")
        {
            Reward += 1.0F;
            collided = 1;
        }
    }

    void OnTriggerExit(Collider other)
    {
    }
}
