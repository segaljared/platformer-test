using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    public CharacterPhysicsBody PhysicsBody;

    // Update is called once per frame
    void Update()
    {
        float x = Input.GetAxis("Horizontal");
        float y = Input.GetAxis("Vertical");
        PhysicsBody.SetInputDirection(new Vector2(x, y));
        bool jumpPressed = Input.GetButton("Jump");
        PhysicsBody.SetJumpPressed(jumpPressed);
        RotateWithGround();
    }
    
    private void RotateWithGround()
    {
        Quaternion rotation = Quaternion.Euler(0, 0, PhysicsBody.BodyAngleDegrees);
        transform.rotation = rotation;
    }
}
