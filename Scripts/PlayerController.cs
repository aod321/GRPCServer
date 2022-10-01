using System;
using UnityEngine;
using System.Collections;
using System.Data.SqlTypes;
using JetBrains.Annotations;

public class PlayerController : MonoBehaviour {

    Vector3 velocity;

    CharacterController characterController;
    
    public const int _textureWidth = 84;
    public const int _textureHeight = 84;
    public float jumpSpeed = 1.0f;
    public float Gravity = 98f;
    public float CheckRadius = 0.2f;
    public float JumpHeight = 4.0f;
    public LayerMask layerMask;
    public bool IsGround;
    public bool isJump = false;
    public float agentMoveSpeed = 500.0f;
    public float agentLookSpeed = 50.0f;
    public float rayLength = 0.2f;

    public Transform GroundCheck;
    public Camera PlayerCamera;
    public ColliderTrigger colliderTrigger;
    public Texture2D renderingTexture;

    void Start () {
        characterController = GetComponent<CharacterController>();
        colliderTrigger = GetComponent<ColliderTrigger>();
        renderingTexture = new Texture2D(_textureWidth, _textureHeight, TextureFormat.RGBA32, false);
    }

    private void Update()
    {
        Vector3 GroundVelocity = Vector3.zero;
        IsGround = Physics.CheckSphere(GroundCheck.position, CheckRadius, layerMask);
        if (IsGround)
        {
            // Jump
            if (isJump)
            {
               GroundVelocity = new Vector3(0,  JumpHeight, 0);
               isJump = false; 
            }
        }
        GroundVelocity -= new Vector3(0, Gravity * Time.deltaTime, 0);
        characterController.Move(GroundVelocity * jumpSpeed);
    }

    public void FixedUpdate()
    {
        OnRay();
    }

    public void OnRay()
    {
        Ray ray = new Ray(GroundCheck.position, Vector3.down);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayLength))
        {
            if (!hit.transform.gameObject.CompareTag("Playable"))
            {
                // if out of playable area, finish the eposide
                colliderTrigger.Done = 1;
            }
        }
    }

    public void SetSpeed(Vector3 _velocity) {
        // IsGround = Physics.CheckSphere(GroundCheck.position, CheckRadius, layerMask);
        // if (IsGround && GroundVelocity.y < 0)
        // {
        //     GroundVelocity.y = 0;
        // }
        velocity = _velocity;
        // GroundVelocity.y += Gravity * Time.deltaTime;
        characterController.Move(velocity);
    }
    

    public void LookAt(Vector3 lookPoint) {
        Vector3 heightCorrectedPoint = new Vector3 (lookPoint.x, transform.position.y, lookPoint.z);
        transform.LookAt (heightCorrectedPoint);
    }
    
    public void Look(Vector2 PIXELS_PER_FRAME)
    {
        // LOOK_LEFT_RIGHT_PIXELS_PER_FRAME
        transform.Rotate(Vector3.up, PIXELS_PER_FRAME.x, Space.World);
        // transform.localEulerAngles = Vector3.up * PIXELS_PER_FRAME.x;
        // LOOK_UP_DOWN_PIXELS_PER_FRAME;
        // PlayerCamera.transform.localEulerAngles = Vector3.right * PIXELS_PER_FRAME.y;
        PlayerCamera.transform.Rotate(Vector3.right, PIXELS_PER_FRAME.y);
        if(PlayerCamera.transform.localEulerAngles.x > 300)
            PlayerCamera.transform.localEulerAngles = Vector3.right * Mathf.Clamp(PlayerCamera.transform.localEulerAngles.x - 360, -15.0f, 15.0f);
        else
            PlayerCamera.transform.localEulerAngles = Vector3.right * Mathf.Clamp(PlayerCamera.transform.localEulerAngles.x, -15.0f, 15.0f);
        // if(PlayerCamera.transform.localEulerAngles.x > )
    }
    
    public void Move(Vector3 PIXELS_PER_FRAME)
    {
        SetSpeed(transform.forward * PIXELS_PER_FRAME.z);
        SetSpeed(transform.right * PIXELS_PER_FRAME.x);
    }
    
}
