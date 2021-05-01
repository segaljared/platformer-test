using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    public BoxCollider2D Collider;

    public float Acceleration;
    public float Deceleration;
    public float Speed;

    public float Gravity;

    public float MaxSlopeAngle;
    public float MaxSlideSpeed;

    public float MaxJumpHeight;
    public float MaxJumpTime;
    public float MaxJumpButtonHoldTime;
    public float MaxFallSpeed;

    public float SimulationIncrementDistance;

    // Start is called before the first frame update
    void Start()
    {
        if (Collider == null)
        {
            Collider = GetComponent<BoxCollider2D>();
        }
    }

    // Update is called once per frame
    void Update()
    {
        float x = Input.GetAxis("Horizontal");
        _inputDirection = Vector2.right * x;
        Debug.Log(_inputDirection);
        RotateWithGround();
    }

    void FixedUpdate()
    {
        /// Stuff to remember
        /// -When jumping, need to zero out y part of velocity, since we don't want running up slope to cause the player to jumphigher
        /// -If rotating the player due to a slope, do we rotate back to normal on jumping
        /// -To keep movement speed the same when entering a slope, need to adjust after determining that we're entering a slope
        /// -Need to get information about the collider that you're colliding with (i.e. find the place you're colliding with)
        _currentContact = GetCurrentGroundContact();
        if (_currentContact.HasContact)
        {
            _lastGroundTime = Time.fixedTime;
            _inAir = false;
            if (Mathf.Abs(_currentContact.AngleFromUpright) > MaxSlopeAngle)
            {
                Vector2 slideDirection = _currentContact.Slope;
                if (slideDirection.y > 0)
                {
                    slideDirection *= -1f;
                }
                Vector2 slideForce = slideDirection * Vector2.Dot(Vector2.down * Gravity, slideDirection);
                if (Vector2.Dot(slideDirection, _velocity) < 0)
                {
                    slideForce += slideDirection * Deceleration;
                }
                _velocity += slideForce * Time.fixedDeltaTime;
                _velocity = _velocity.CapMagnitude(MaxSlideSpeed);
            }
            else if (_inputDirection.sqrMagnitude < 0.5f)
            {
                // Player isn't moving the character in any direction, so we should use this to skid the
                // character to a halt if they are moving
                float velocityMagnitude = _velocity.magnitude;
                if (velocityMagnitude > 0)
                {
                    float frameDeceleration = Deceleration * Time.fixedDeltaTime;
                    if (frameDeceleration >= velocityMagnitude)
                    {
                        _velocity = Vector2.zero;
                    }
                    else
                    {
                        _velocity -= frameDeceleration * _velocity.normalized;
                    }
                }
            }
            else
            {
                Vector2 desiredDireciton = _inputDirection.RotateRadians(_currentContact.RadiansFromUpright) * Speed;
                Vector2 velocityDifference = desiredDireciton - _velocity;
                if (velocityDifference.sqrMagnitude > 0)
                {
                    velocityDifference = velocityDifference.normalized * Acceleration * Time.fixedDeltaTime;
                    _velocity += velocityDifference;
                    _velocity = _velocity.CapMagnitude(Speed);
                }
            }
            _velocity = _currentContact.VelocityAlongSlope(_velocity);

            MoveAlongGround(Time.fixedDeltaTime);
        }
    }

    private void RotateWithGround()
    {
        Quaternion rotation = Quaternion.Euler(0, 0, _currentContact.AngleFromUpright);
        transform.rotation = rotation;
    }

    private void MoveAlongGround(float deltaTime)
    {
        /// We need to handle a few things here:
        /// 1) Our velocity is projected using our current contact points, which may put
        ///    our character above or below the ground if we're moving from one slope
        ///    to another.
        /// 2) We may get stopped by an obstacle along the ground path
        /// 3) Using a boxcast may catch the ground when we're changing slopes
        /// So we need to be able to use a boxcast along our actual path of movement, which
        /// is determined by our current slope and future slope
        /// To fix all of these things, we'll move the character in small increments
        /// fixing positioning and rotation along the way
        float totalDistance = _velocity.magnitude * deltaTime;
        float incrementTime = deltaTime * (totalDistance / SimulationIncrementDistance);
        Vector2 currentPosition = transform.position;
        while (totalDistance > EPSILON && _currentContact.HasContact)
        {
            deltaTime = Mathf.Max(0, deltaTime - incrementTime);
            _velocity = _currentContact.VelocityAlongSlope(_velocity);
            float stepMovement = Mathf.Min(SimulationIncrementDistance, totalDistance);
            currentPosition += _velocity.normalized * stepMovement;
            _currentContact = GetCurrentGroundContact(currentPosition);
            if (_currentContact.HasContact)
            {
                Vector2 startPosition = currentPosition + Vector2.up.RotateRadians(_currentContact.RadiansFromUpright);
                Vector2 downDirection = Vector2.down.RotateRadians(_currentContact.RadiansFromUpright);
                RaycastHit2D hit = Physics2D.BoxCast(startPosition, Collider.size, _currentContact.AngleFromUpright, downDirection, 2f, ~LayerMask.GetMask("Character"));
                currentPosition = startPosition + downDirection * hit.distance - Collider.offset.RotateRadians(_currentContact.RadiansFromUpright);
            }
            totalDistance -= stepMovement;
        }
        transform.position = transform.position.SetXY(currentPosition);
    }

    private GroundContact GetCurrentGroundContact()
    {
        return GetCurrentGroundContact((Vector2)transform.position);
    }

    private GroundContact GetCurrentGroundContact(Vector2 position)
    {
        Vector2 left = (Vector2)transform.position + Vector2.left * Collider.size.x * 0.5f + Vector2.up;
        Vector2 right = left + Vector2.right * Collider.size.x;
        return FindGroundContact(left, right, 4);
    }

    private static GroundContact FindGroundContact(Vector2 left, Vector2 right, int maxDepth)
    {
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.down, 2f, ~LayerMask.GetMask("Character"));
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.down, 2f, ~LayerMask.GetMask("Character"));
        if (leftHit.collider == null)
        {
            if (rightHit.collider == null)
            {
                return new GroundContact() { Slope = Vector2.right, HasContact = false };
            }
            while (leftHit.collider == null && maxDepth > 0)
            {
                left = Vector2.Lerp(left, right, 0.5f);
                leftHit = Physics2D.Raycast(left, Vector2.down, 2f, ~LayerMask.GetMask("Character"));
                maxDepth--;
            }
            if (leftHit.collider == null)
            {
                return new GroundContact() { Slope = Vector2.right, HasContact = false };
            }
        }
        while (rightHit.collider == null && maxDepth > 0)
        {
            right = Vector2.Lerp(right, left, 0.5f);
            rightHit = Physics2D.Raycast(right, Vector2.down, 2f, ~LayerMask.GetMask("Character"));
            maxDepth--;
        }
        if (rightHit.collider == null)
        {
            return new GroundContact() { Slope = Vector2.right, HasContact = false };
        }
        Vector2 slope = (rightHit.point - leftHit.point).normalized;
        return new GroundContact() { Slope = slope, HasContact = true };
    }

    private Vector2 _velocity;
    private Vector2 _inputDirection;
    private float _jumpHoldTime;
    private float _lastGroundTime;
    private bool _inAir;
    private GroundContact _currentContact;

    private const float EPSILON = 0.001f;

    private struct GroundContact
    {
        public Vector2 Slope;
        public bool HasContact;

        public float AngleFromUpright
        {
            get
            {
                return Mathf.Atan2(Slope.y, Slope.x) * Mathf.Rad2Deg;
            }
        }

        public float RadiansFromUpright
        {
            get
            {
                return Mathf.Atan2(Slope.y, Slope.x);
            }
        }

        public Vector2 VelocityAlongSlope(Vector2 velocity)
        {
            return Slope * velocity.magnitude * Mathf.Sign(Vector2.Dot(velocity, Slope));
        }
    }
}
