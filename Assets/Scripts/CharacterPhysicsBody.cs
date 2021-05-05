using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class CharacterPhysicsBody : MonoBehaviour
{
    public BoxCollider2D Collider;

    [Header("Ground Movement")]
    public float TimeToTopSpeed;
    public float TimeToStop;
    public float GroundSpeed;

    public float MaxSlopeAngle;
    public float MaxSlideSpeed;

    [Header("Jumping")]
    public float CoyoteTime;
    public float MinJumpHeight;
    public float MaxJumpHeight;
    public float TimeToReachMaxJumpHeight;
    public float MaxJumpButtonHoldTime;
    public float MaxFallSpeed;
    [Range(0, 1)]
    public float InAirMovementModifier = 0.33f;
    [Range(0, 1)]
    public float ImpactVelocityModifier = 0.5f;

    enum BodyState { OnGround, InContactWithOtherBody, FreeFall, Jumping };

    public float BodyAngleDegrees
    {
        get
        {
            return BodyAngleRadians * Mathf.Rad2Deg;
        }
    }

    public float BodyAngleRadians
    {
        get
        {
            if (_state == BodyState.OnGround ||
                (_state == BodyState.InContactWithOtherBody && Mathf.Abs(_groundContact.AngleFromUpright) < 2 * MaxSlopeAngle))
            {
                return _groundContact.RadiansFromUpright;
            }
            return 0;
        }
    }

    public void SetInputDirection(Vector2 input)
    {
        _input = input;
    }

    public void SetJumpPressed(bool jumpPressed)
    {
        _jumpPressed = jumpPressed;
    }

    public void UpdateConstants()
    {
        _horizontalAcceleration = GroundSpeed / TimeToTopSpeed;
        _horizontalDeceleration = GroundSpeed / TimeToStop;
        _gravity = 2 * TimeToReachMaxJumpHeight * TimeToReachMaxJumpHeight * MaxJumpHeight;
        _initialJumpImpulse = Mathf.Sqrt(2 * _gravity * MinJumpHeight);
        _jumpHoldAcceleration = _gravity - (_initialJumpImpulse * _initialJumpImpulse) / (2 * MaxJumpHeight);
    }

    void Start()
    {
        _collisionMask = ~LayerMask.GetMask("Character");
        UpdateConstants();
    }

    void OnValidate()
    {
        /// Called when changing property values in the Editor
        UpdateConstants();
    }

    void FixedUpdate()
    {
        switch (_state)
        {
            case BodyState.FreeFall:
                OnFreeFallUpdate();
                break;
            case BodyState.InContactWithOtherBody:
                OnSlideUpdate();
                break;
            case BodyState.OnGround:
                OnGroundUpdate();
                break;
            case BodyState.Jumping:
                OnJumpUpdate();
                break;
        }
    }

    private void OnFreeFallUpdate()
    {
        StartJumpIfPossible();
        ApplyInAirHorizontalAdjustments();
        ApplyGravity();
        HandleAirMovement(Time.fixedDeltaTime);
        UpdateNotCollidingWithList();
    }

    private void OnSlideUpdate()
    {
        if (_groundContact.HasContact && Mathf.Abs(_groundContact.AngleFromUpright) < 2 * MaxSlopeAngle)
        {
            _lastGroundTime = Time.fixedTime;
        }
        if (StartJumpIfPossible())
        {
            OnJumpUpdate();
            return;
        }
        ApplyInAirHorizontalAdjustments();
        /// We also want to apply some friction here
        float friction = _horizontalDeceleration * (0.1f + 0.4f * Mathf.Abs(Vector2.Dot(_velocity, Vector2.right)));
        _velocity -= _velocity.normalized * friction * Time.fixedDeltaTime;

        ApplyGravity();
        HandleSlideMovement(Time.fixedDeltaTime);
        CheckForStateChangeFromSlideOrGroundMovement();
        UpdateNotCollidingWithList();
    }

    private void OnGroundUpdate()
    {
        _lastGroundTime = Time.fixedTime;
        if (StartJumpIfPossible())
        {
            OnJumpUpdate();
            return;
        }
        if (_hasJumped && !_jumpPressed)
        {
            _hasJumped = false;
        }
        ApplyGroundInput();
        HandleGroundMovement(Time.fixedDeltaTime);
        CheckForStateChangeFromSlideOrGroundMovement();
        UpdateNotCollidingWithList();
    }

    private void OnJumpUpdate()
    {
        if (!_jumpPressed || _jumpHoldTime >= MaxJumpButtonHoldTime)
        {
            _jumpHoldTime = MaxJumpButtonHoldTime;
            _state = BodyState.FreeFall;
            OnFreeFallUpdate();
            return;
        }
        _jumpHoldTime += Time.fixedDeltaTime;
        _velocity += Vector2.up * _jumpHoldAcceleration * Time.fixedDeltaTime;
        ApplyInAirHorizontalAdjustments();
        ApplyGravity();
        HandleAirMovement(Time.fixedDeltaTime);
        UpdateNotCollidingWithList();
    }

    private void UpdateNotCollidingWithList()
    {
        Collider2D[] overlapping = new Collider2D[10];
        int numOverlapping = Physics2D.OverlapBoxNonAlloc((Vector2)transform.position + Collider.offset, Collider.size, 0, overlapping, _collisionMask);
        foreach (var notCollideWith in _notCollidingWith.ToList())
        {
            bool keepInList = false;
            for (int i = 0; i < numOverlapping; i++)
            {
                if (notCollideWith == overlapping[i])
                {
                    keepInList = true;
                    break;
                }
            }
            if (!keepInList)
            {
                _notCollidingWith.Remove(notCollideWith);
            }
        }
    }

    private bool StartJumpIfPossible()
    {
        if (!_hasJumped && _jumpPressed && Time.fixedTime - _lastGroundTime < CoyoteTime)
        {
            _jumpHoldTime = 0;
            _velocity.y = _initialJumpImpulse;
            _state = BodyState.Jumping;
            _hasJumped = true;
            return true;
        }
        return false;
    }

    private void CheckForStateChangeFromSlideOrGroundMovement()
    {
        if (Physics2D.OverlapBox((Vector2)transform.position + Collider.offset, Collider.size, 0, _collisionMask) == null)
        {
            _state = BodyState.FreeFall;
        }
        else
        {
            _groundContact = GetCurrentGroundContact();
            if (_groundContact.HasContact && Mathf.Abs(_groundContact.AngleFromUpright) > MaxSlopeAngle)
            {
                _state = BodyState.InContactWithOtherBody;
            }
            else if (_groundContact.HasContact)
            {
                _state = BodyState.OnGround;
            }
            else
            {
                _state = BodyState.FreeFall;
            }
        }
    }

    private void ApplyInAirHorizontalAdjustments()
    {
        /// Need to allow for in air horizontal adjustments, but with some limitations
        /// We will cap the speed that can be reached with just in air movements to GroundSpeed * InAirMovementModifier
        /// That means that if we're current moving left at that speed or greater, we can't move faster going left,
        /// but we could slow down by pressing right
        Vector2 airMovement = HorizontalInput * _horizontalAcceleration * InAirMovementModifier * Time.fixedDeltaTime;
        if (Vector2.Dot(airMovement, _velocity) > 0)
        {
            /// Trying to adjust in the same direction that we're going, so we need to only apply horizontal movement 
            /// up to the maximum speed as noted above
            float appliedX = Mathf.Max(_velocity.x, Mathf.Min(_velocity.x + airMovement.x, GroundSpeed * InAirMovementModifier));
            _velocity.x = appliedX;
        }
        else
        {
            /// Trying to adjust in the opposite direction, so we'll apply all of the accleration
            _velocity += airMovement;
        }
    }

    private void ApplyGroundInput()
    {
        if (_input.sqrMagnitude < EPSILON)
        {
            float deceleration = _horizontalDeceleration * Time.fixedDeltaTime;
            if (deceleration >= _velocity.magnitude)
            {
                _velocity = Vector2.zero;
            }
            else
            {
                _velocity -= deceleration * _velocity.normalized;
            }
        }
        else
        {
            Vector2 desiredDirection = _input.RotateRadians(_groundContact.RadiansFromUpright) * GroundSpeed;
            Vector2 velocityDifference = desiredDirection - _velocity;
            if (velocityDifference.sqrMagnitude > 0)
            {
                velocityDifference = velocityDifference.normalized * _horizontalAcceleration * Time.fixedDeltaTime;
                _velocity += velocityDifference;
                _velocity = Vector2.ClampMagnitude(_velocity, GroundSpeed);
            }
        }
        _velocity = _groundContact.VelocityAlongSlope(_velocity);
    }

    private void ApplyGravity()
    {
        _velocity += Vector2.down * _gravity * Time.fixedDeltaTime;
        if (_velocity.y < -MaxFallSpeed)
        {
            _velocity.y = -MaxFallSpeed;
        }
    }

    private void HandleAirMovement(float deltaTime)
    {
        _groundContact = GroundContact.InAirContact;
        RaycastHit2D[] hits = new RaycastHit2D[10];
        float totalDistance = _velocity.magnitude * deltaTime;
        Vector2 currentPosition = (Vector2)transform.position + Collider.offset;
        if (CheckForCollisions(currentPosition, totalDistance, hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
        {
            currentPosition = hit.centroid;
            _velocity += adjustment;
            _velocity *= ImpactVelocityModifier;
            deltaTime *= 1 - (distance / totalDistance);
            transform.position = transform.position.SetXY(currentPosition - Collider.offset);
            if (Vector2.Angle(adjustment, Vector2.up) > MaxSlopeAngle)
            {
                _state = BodyState.InContactWithOtherBody;
                HandleSlideMovement(deltaTime);
                return;
            }
            _state = BodyState.OnGround;
            HandleGroundMovement(deltaTime);
        }
        else
        {
            currentPosition += _velocity * deltaTime;
            transform.position = transform.position.SetXY(currentPosition - Collider.offset);
        }
    }

    private void HandleSlideMovement(float deltaTime)
    {
        _groundContact = GetCurrentGroundContact();
        RaycastHit2D[] hits = new RaycastHit2D[10];
        Vector2 currentPosition = (Vector2)transform.position + Collider.offset;
        while (deltaTime > 0)
        {
            float totalDistance = _velocity.magnitude * deltaTime;
            if (CheckForCollisions(currentPosition, totalDistance, hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
            {
                currentPosition = hit.centroid;
                _velocity += adjustment;
                deltaTime *= 1 - (distance / totalDistance);
                
            }
            else
            {
                currentPosition += _velocity * deltaTime;
                deltaTime = 0;
            }
        }
        transform.position = transform.position.SetXY(currentPosition - Collider.offset);
    }

    private void HandleGroundMovement(float deltaTime)
    {
        _groundContact = GetCurrentGroundContact();
        RaycastHit2D[] hits = new RaycastHit2D[10];
        float totalDistance = _velocity.magnitude * deltaTime;
        Vector2 currentPosition = (Vector2)transform.position + Collider.offset.RotateRadians(_groundContact.RadiansFromUpright);
        RaycastHit2D placementHit = Physics2D.BoxCast(currentPosition + Vector2.up.RotateRadians(_groundContact.RadiansFromUpright) * 0.5f, Collider.size, _groundContact.AngleFromUpright, Vector2.down.RotateRadians(_groundContact.RadiansFromUpright), 0.75f, _collisionMask);
        currentPosition = placementHit.centroid;
        bool seenZero = false;
        while (totalDistance > EPSILON)
        {
            _velocity = _groundContact.VelocityAlongSlope(_velocity);
            if (CheckForCollisions(currentPosition, totalDistance, hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
            {
                currentPosition = hit.centroid;
                deltaTime *= 1 - (distance / totalDistance);
                if (distance == 0)
                {
                    if (seenZero)
                    {
                        break;
                    }
                    seenZero = true;
                }
                else
                {
                    seenZero = false;
                }
                totalDistance -= distance;
                if (Vector2.Angle(adjustment, Vector2.up) > MaxSlopeAngle)
                {
                    transform.position = transform.position.SetXY(currentPosition - Collider.offset);
                    HandleSlideMovement(deltaTime);
                    return;
                }
                else
                {
                    _velocity = Vector2.right.RotateRadians(Vector2.SignedAngle(Vector2.up, hit.normal)) 
                                * _velocity.magnitude * Mathf.Sign(Vector2.Dot(_velocity, Vector2.right));
                    //_groundContact = GetCurrentGroundContact(currentPosition - Collider.offset.RotateRadians(_groundContact.RadiansFromUpright));
                    placementHit = Physics2D.BoxCast(currentPosition + Vector2.up.RotateRadians(_groundContact.RadiansFromUpright) * 0.5f, Collider.size, _groundContact.AngleFromUpright, Vector2.down.RotateRadians(_groundContact.RadiansFromUpright), 0.75f, _collisionMask);
                    currentPosition = placementHit.centroid;
                }
            }
            else
            {
                currentPosition += _velocity * deltaTime;
                totalDistance = 0;
            }
        }
        transform.position = transform.position.SetXY(currentPosition - Collider.offset);
    }

    private bool CheckForCollisions(Vector2 currentPosition, float totalDistance, RaycastHit2D[] hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment)
    {
        float angle = Mathf.Abs(_groundContact.AngleFromUpright) < 2 * MaxSlopeAngle ? _groundContact.AngleFromUpright : 0;
        int numHit = Physics2D.BoxCastNonAlloc(currentPosition, Collider.size, angle, _velocity.normalized, hits, totalDistance, _collisionMask);
        adjustment = Vector2.zero;
        distance = float.PositiveInfinity;
        hit = default(RaycastHit2D);
        for (int i = 0; i < numHit; i++)
        {
            RaycastHit2D current = hits[i];
            if (current.distance > distance || _notCollidingWith.Contains(current.collider))
            {
                /// Only handle the first hit and ignore collisions with colliders in _notCollidingWith list
                /// That list is used to handle cases where the character is in the middle of a one way platform
                /// and starts going down or if we want the character to be able to drop through some platforms
                continue;
            }
            OneWayPlatform oneWayPlatform = current.collider.GetComponent<OneWayPlatform>();
            if (oneWayPlatform != null && Vector2.Dot(_velocity, oneWayPlatform.SolidDirection) < 0)
            {
                /// If we are going through a one way platform in the opposite direction as its solid direction
                /// ignore the collision and add to our _notCollidingWith list
                _notCollidingWith.Add(current.collider);
                continue;
            }
            if (Vector2.Dot(current.normal, _velocity) >= 0)
            {
                /// This handles any case where our movement shouldn't be impeded by the collider
                /// This includes getting a collision with the ground as we jump
                /// And sliding along a surface
                continue;
            }
            hit = current;
            distance = current.distance;
            adjustment = current.normal * Vector2.Dot(current.normal, _velocity) * -1f;
        }
        return !float.IsPositiveInfinity(distance);
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

    private Vector2 HorizontalInput
    {
        get
        {
            return Vector2.right * Vector2.Dot(_input, Vector2.right);
        }
    }

    private BodyState _state;

    Vector2 _velocity;

    private float _gravity;
    private float _initialJumpImpulse;
    private float _jumpHoldAcceleration;
    private float _horizontalAcceleration;
    private float _horizontalDeceleration;

    private Vector2 _input;
    private bool _jumpPressed;
    private bool _hasJumped;
    private float _lastGroundTime;
    private float _jumpHoldTime;
    private GroundContact _groundContact;
    private HashSet<Collider2D> _notCollidingWith = new HashSet<Collider2D>();

    private static int _collisionMask;

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

        public static GroundContact InAirContact = new GroundContact() { Slope = Vector2.right, HasContact = false };
    }
}
