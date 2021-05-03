using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    public BoxCollider2D Collider;

    public float TimeToTopSpeed;
    public float TimeToStop;

    public float Acceleration
    {
        get
        {
            return Speed / TimeToTopSpeed;
        }
    }
    public float Deceleration
    {
        get
        {
            return Speed / TimeToStop;
        }
    }
    public float Speed;

    public float MaxSlopeAngle;
    public float MaxSlideSpeed;

    public float CoyoteTime;
    public float MinJumpHeight;
    public float MaxJumpHeight;
    public float MaxJumpTime;
    public float MaxJumpButtonHoldTime;
    public float MaxFallSpeed;
    public float InAirMovementModifier;

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
        _jumpPressed = Input.GetButton("Jump");
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
        if (!_hasJumped && _jumpPressed && Time.fixedTime - _lastGroundTime < CoyoteTime)
        {
            _jumpHoldTime = 0;
            _velocity.y = InitialJumpImpulse;
            _inAir = true;
            _hasJumped = true;
        }
        else if (_currentContact.HasContact && !_inAir)
        {
            if (Mathf.Abs(_currentContact.AngleFromUpright) < MaxSlopeAngle && !_jumpPressed)
            {
                _hasJumped = false;
            }
            _lastGroundTime = Time.fixedTime;
            _jumpHoldTime = 0;
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
                _velocity = Vector2.ClampMagnitude(_velocity, MaxSlideSpeed);
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
                    _velocity = Vector2.ClampMagnitude(_velocity, Speed);
                }
            }
            _velocity = _currentContact.VelocityAlongSlope(_velocity);

            MoveAlongGround(Time.fixedDeltaTime);
        }
        else if (!_inAir)
        {
            _inAir = true;
        }
        if (_inAir)
        {
            if (_jumpHoldTime < MaxJumpButtonHoldTime && _jumpPressed)
            {
                _velocity += Vector2.up * JumpHoldAcceleration * Time.fixedDeltaTime;
                _jumpHoldTime += Time.fixedDeltaTime;
            }
            else
            {
                _jumpHoldTime = MaxJumpButtonHoldTime;
            }
            Vector2 airMovement = _inputDirection * Acceleration * InAirMovementModifier;
            if (Mathf.Abs((_velocity + airMovement).x) < Speed)
            {
                _velocity += airMovement;
            }
            _velocity += Vector2.down * Gravity * Time.fixedDeltaTime;
            if (_velocity.y < -MaxFallSpeed)
            {
                _velocity.y = -MaxFallSpeed;
            }
            HandleAirMovement(Time.fixedDeltaTime);
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
        _inAir = false;
        float totalDistance = _velocity.magnitude * deltaTime;
        float incrementTime = deltaTime * (totalDistance / SimulationIncrementDistance);
        Vector2 currentPosition = transform.position;
        if (_currentContact.HasContact)
        {
            Vector2 startPosition = currentPosition + Vector2.up.RotateRadians(_currentContact.RadiansFromUpright);
            Vector2 downDirection = Vector2.down.RotateRadians(_currentContact.RadiansFromUpright);
            RaycastHit2D hit = Physics2D.BoxCast(startPosition, Collider.size, _currentContact.AngleFromUpright, downDirection, 2f, ~LayerMask.GetMask("Character"));
            currentPosition = startPosition + downDirection * hit.distance - Collider.offset.RotateRadians(_currentContact.RadiansFromUpright);
        }
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

    private void HandleAirMovement(float deltaTime, bool shouldRecurse = false)
    {
        Collider2D[] overlapping = new Collider2D[10];
        RaycastHit2D[] hits = new RaycastHit2D[10];
        float totalDistance = _velocity.magnitude * deltaTime;
        float incrementTime = deltaTime * (SimulationIncrementDistance/ totalDistance);
        Vector2 currentPosition = (Vector2)transform.position + Collider.offset;
        while (totalDistance > EPSILON)
        {
            int numOverlapping = Physics2D.OverlapBoxNonAlloc(currentPosition, Collider.size * 0.95f, 0, overlapping);
            float movementTime = Mathf.Min(deltaTime, incrementTime);
            deltaTime = Mathf.Max(0, deltaTime - incrementTime);
            float movementDistance = Mathf.Min(totalDistance, SimulationIncrementDistance);
            int numHits = Physics2D.BoxCastNonAlloc(currentPosition, Collider.size, 0, _velocity.normalized, hits, movementDistance, ~LayerMask.GetMask("Character"));
            Vector2 adjustment = Vector2.zero;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < numHits; i++)
            {
                RaycastHit2D current = hits[i];
                if (current.distance > distance)
                {
                    continue;
                }
                if (current.collider.gameObject.name == "block")
                {
                    Debug.Log("Hit platform");
                    if (shouldRecurse)
                    {
                        HandleAirMovement(Time.fixedDeltaTime, false);
                    }
                }
                bool currentlyHitting = true;
                for (int j = 0; j < numOverlapping; j++)
                {
                    if (current.collider == overlapping[j])
                    {
                        currentlyHitting = false;
                        break;
                    }
                }
                if (currentlyHitting)
                {
                    if ((current.collider.GetComponent<OneWayPlatform>() != null && _velocity.y > 0)
                        || Vector2.Dot(current.normal, _velocity) > 0)
                    {
                        /// If we're going up and hit a one way platform, ignore it
                        continue;
                    }
                    distance = current.distance;
                    adjustment = current.normal * Vector2.Dot(current.normal, _velocity) * -1f;
                }
            }
            if (!float.IsPositiveInfinity(distance))
            {
                currentPosition += _velocity.normalized * distance;
                _velocity += adjustment;
                _velocity *= 0.5f;
                movementTime *= 1 - (distance / movementDistance);
                currentPosition += _velocity * movementTime;
                totalDistance = _velocity.magnitude * deltaTime;
                if (Vector2.Angle(adjustment, Vector2.up) < 60)
                {
                    /// We've hit the ground, so let's start moving along the ground
                    transform.position = transform.position.SetXY(currentPosition - Collider.offset);
                    MoveAlongGround(deltaTime);
                    return;
                }
            }
            else
            {
                currentPosition += _velocity * movementTime;
                totalDistance -= movementDistance;
            }
        }
        transform.position = transform.position.SetXY(currentPosition - Collider.offset);
    }

    private GroundContact GetCurrentGroundContact()
    {
        return GetCurrentGroundContact((Vector2)transform.position);
    }

    private GroundContact GetCurrentGroundContact(Vector2 position)
    {
        Vector2 left = position + Vector2.left * Collider.size.x * 0.5f + Vector2.up * 0.25f;
        Vector2 right = left + Vector2.right * Collider.size.x;
        return FindGroundContact(left, right, 4);
    }

    private static GroundContact FindGroundContact(Vector2 left, Vector2 right, int maxDepth)
    {
        RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.down, 0.5f, ~LayerMask.GetMask("Character"));
        RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.down, 0.5f, ~LayerMask.GetMask("Character"));
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

    public float Gravity
    {
        get
        {
            return 2 * MaxJumpTime * MaxJumpTime * MaxJumpHeight;
        }
    }

    private float InitialJumpImpulse
    {
        get
        {
            return Mathf.Sqrt(2 * Gravity * MinJumpHeight);
        }
    }

    private float JumpHoldAcceleration
    {
        get
        {
            float initialVelocity = InitialJumpImpulse;
            return Gravity - (initialVelocity * initialVelocity) / (2 * MaxJumpHeight);
        }
    }

    private Vector2 _velocity;
    private Vector2 _inputDirection;
    private float _jumpHoldTime;
    private float _lastGroundTime;
    private bool _inAir;
    private bool _jumpPressed;
    private bool _hasJumped;
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
