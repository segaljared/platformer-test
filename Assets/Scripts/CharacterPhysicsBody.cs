using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class CharacterPhysicsBody : MonoBehaviour
{
    public BoxCollider2D Collider;

    [Header("Ground Movement")]
    /// <summary>
    /// The time in second to reach the top ground speed from standstill (used to calculate how fast the character accelerates)
    /// </summary>
    public float TimeToTopSpeed;
    /// <summary>
    /// The time from the top ground speed to stopping when the player stops pressing Left/Right arrows
    /// </summary>
    public float TimeToStop;
    /// <summary>
    /// Top speed on the ground
    /// </summary>
    public float GroundSpeed;
    /// <summary>
    /// The maximum angle of a slope that the character can navigate
    /// </summary>
    public float MaxSlopeAngle;

    [Header("Jumping")]
    /// <summary>
    /// Amount of time after leaving the ground (if walking off an edge, but not from jumping) where it is
    /// still possible to jump.
    /// 
    /// Gives a little extra time when running off a platform, for example, to actually jump
    /// </summary>
    public float CoyoteTime;
    /// <summary>
    /// The minimum height of any jump if Space is only pressed for one frame
    /// </summary>
    public float MinJumpHeight;
    /// <summary>
    /// The maximum height reached by holding Space
    /// </summary>
    public float MaxJumpHeight;
    /// <summary>
    /// The time it takes for the character to reach the maximum height
    /// </summary>
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
        _initialJumpImpulse = Mathf.Sqrt(2 * _gravity * MaxJumpHeight);
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
        bool stillUp = _velocity.y > 0;
        ApplyGravity();
        if (stillUp && _velocity.y <= 0)
        {
            Debug.Log($"Time to max height: {Time.fixedTime - _lastGroundTime}");
        }
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
        float friction = _horizontalDeceleration * (0.1f + 0.15f * Mathf.Abs(Vector2.Dot(_velocity, Vector2.right)));
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
        if (CheckForDropThrough(out Collider2D collider))
        {
            _notCollidingWith.Add(collider);
            _state = BodyState.FreeFall;
            OnFreeFallUpdate();
            return;
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
        int numOverlapping = Physics2D.OverlapBoxNonAlloc(GetColliderCenter(), Collider.size, _groundContact.AngleFromUpright, overlapping, _collisionMask);
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

    private bool CheckForDropThrough(out Collider2D collider)
    {
        if (_input.y < -0.5f)
        {
            RaycastHit2D platformHit = Physics2D.BoxCast(GetColliderCenter(), Collider.size, _groundContact.AngleFromUpright, Vector2.down.RotateRadians(_groundContact.RadiansFromUpright), 0.5f, _collisionMask);
            if (platformHit.collider != null)
            {
                OneWayPlatform platform = platformHit.collider.GetComponent<OneWayPlatform>();
                collider = platformHit.collider;
                return platform != null && Vector2.Dot(platform.SolidDirection, Vector2.down.RotateRadians(_groundContact.RadiansFromUpright)) > 0;
            }
        }
        collider = null;
        return false;
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
        _groundContact = GetCurrentGroundContact();
        if (Physics2D.OverlapBox(GetColliderCenter(), Collider.size, _groundContact.AngleFromUpright, _collisionMask) == null && false)
        {
            _state = BodyState.FreeFall;
        }
        else
        {
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
            float appliedX = Mathf.Max(Mathf.Abs(_velocity.x), Mathf.Min(Mathf.Abs(_velocity.x + airMovement.x), GroundSpeed * InAirMovementModifier));

            _velocity.x = appliedX * Mathf.Sign(_velocity.x);
        }
        else
        {
            /// Trying to adjust in the opposite direction, so we'll apply all of the accleration
            _velocity += airMovement;
        }
    }

    private void ApplyGroundInput()
    {
        if (HorizontalInput.sqrMagnitude < EPSILON)
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
            Vector2 desiredDirection = HorizontalInput.RotateRadians(_groundContact.RadiansFromUpright) * GroundSpeed;
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
        Vector2 currentPosition = GetColliderCenter();
        if (CheckForCollisions(currentPosition, totalDistance, hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
        {
            currentPosition = hit.centroid;
            _velocity += adjustment;
            _velocity *= ImpactVelocityModifier;
            deltaTime *= 1 - (distance / totalDistance);
            SetTransformPositionFromColliderPosition(currentPosition);
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
            SetTransformPositionFromColliderPosition(currentPosition);
        }
    }

    private void HandleSlideMovement(float deltaTime)
    {
        _groundContact = GetCurrentGroundContact();
        RaycastHit2D[] hits = new RaycastHit2D[10];
        Vector2 currentPosition = GetColliderCenter();
        while (deltaTime > 0 && _velocity.sqrMagnitude > EPSILON)
        {
            float totalDistance = _velocity.magnitude * deltaTime;
            if (CheckForCollisions(currentPosition, totalDistance, hits, 0.99f, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
            {
                currentPosition = hit.centroid;
                _velocity += adjustment;
                deltaTime *= 1 - distance / totalDistance;
            }
            else
            {
                currentPosition += _velocity * deltaTime;
                deltaTime = 0;
            }
        }
        SetTransformPositionFromColliderPosition(currentPosition);
    }

    private void HandleGroundMovement(float deltaTime)
    {
        _groundContact = GetCurrentGroundContact();
        RaycastHit2D[] hits = new RaycastHit2D[10];
        float totalDistance = _velocity.magnitude * deltaTime;
        Vector2 currentPosition = GetColliderCenter();
        currentPosition = GetPositionOnGroundBelow(currentPosition);
        bool seenZero = false;

        while (totalDistance > EPSILON)
        {
            _velocity = _groundContact.VelocityAlongSlope(_velocity);
            if (CheckForCollisions(currentPosition, totalDistance, hits, 0.99f, out float distance, out RaycastHit2D hit, out Vector2 adjustment))
            {
                currentPosition = hit.centroid;
                deltaTime *= 1 - distance / totalDistance; ;
                if (distance == 0)
                {
                    if (seenZero)
                    {
                        /// If we have two 0's in a row, we'll just assume that we've hit something solid and
                        /// stop this current simulation round in order to avoid any possible infinite loops
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
                    _state = BodyState.InContactWithOtherBody;
                    SetTransformPositionFromColliderPosition(currentPosition);
                    HandleSlideMovement(deltaTime);
                    return;
                }
                else
                {
                    Vector2 newDirection = Vector2.right.RotateDegrees(Vector2.SignedAngle(Vector2.up, hit.normal));
                    newDirection *= Mathf.Sign(Vector2.Dot(_velocity, Vector2.right));
                    _velocity = newDirection * _velocity.magnitude;
                    _velocity = Vector2.right.RotateDegrees(Vector2.SignedAngle(Vector2.up, hit.normal)) 
                                * _velocity.magnitude * Mathf.Sign(Vector2.Dot(_velocity, Vector2.right));
                    _groundContact = GetCurrentGroundContact(currentPosition - Collider.offset.RotateRadians(_groundContact.RadiansFromUpright));
                    currentPosition = GetPositionOnGroundBelow(currentPosition);
                }
            }
            else
            {
                currentPosition += _velocity * deltaTime;
                totalDistance = 0;
            }
        }
        currentPosition = GetPositionOnGroundBelow(currentPosition);
        SetTransformPositionFromColliderPosition(currentPosition);
    }

    private Vector2 GetPositionOnGroundBelow(Vector2 currentPosition)
    {
        RaycastHit2D placementHit = Physics2D.BoxCast(currentPosition + Vector2.up.RotateRadians(_groundContact.RadiansFromUpright) * 0.5f, Collider.size - new Vector2(Collider.size.x * 0.05f, 0), _groundContact.AngleFromUpright, Vector2.down.RotateRadians(_groundContact.RadiansFromUpright), 0.75f, _collisionMask);
        if (placementHit.collider != null)
        {
            return placementHit.centroid;
        }
        return currentPosition;
    }

    private void SetTransformPositionFromColliderPosition(Vector2 colliderPosition)
    {
        transform.position = transform.position.SetXY(colliderPosition - Collider.offset.RotateRadians(_groundContact.RadiansFromUpright));
    }

    private Vector2 GetColliderCenter()
    {
        return (Vector2)transform.position + Collider.offset.RotateRadians(_groundContact.RadiansFromUpright);
    }

    private bool CheckForCollisions(Vector2 currentPosition, float totalDistance, RaycastHit2D[] hits, out float distance, out RaycastHit2D hit, out Vector2 adjustment)
    {
        return CheckForCollisions(currentPosition, totalDistance, hits, 1.0f, out distance, out hit, out adjustment);
    }

    private bool CheckForCollisions(Vector2 currentPosition, float totalDistance, RaycastHit2D[] hits, float sizeModifier, out float distance, out RaycastHit2D hit, out Vector2 adjustment)
    {
        float angle = Mathf.Abs(_groundContact.AngleFromUpright) < 2 * MaxSlopeAngle ? _groundContact.AngleFromUpright : 0;
        int numHit = Physics2D.BoxCastNonAlloc(currentPosition, Collider.size * sizeModifier, angle, _velocity.normalized, hits, totalDistance, _collisionMask);
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
        if (!float.IsPositiveInfinity(distance) && sizeModifier != 1.0f)
        {
            numHit = Physics2D.BoxCastNonAlloc(hit.centroid + hit.normal * 0.5f / sizeModifier, Collider.size, angle, -hit.normal, hits, 1f / sizeModifier, _collisionMask);
            for (int i = 0; i < numHit; i++)
            {
                if (hits[i].collider == hit.collider)
                {
                    hit = hits[i];
                    break;
                }
            }
            //hit = Physics2D.BoxCast(hit.centroid + hit.normal * 0.5f / sizeModifier, Collider.size, angle, -hit.normal, 1f / sizeModifier, _collisionMask);
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

    private GroundContact FindGroundContact(Vector2 left, Vector2 right, int maxDepth)
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
            return new Vector2(_input.x, 0); ;
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
