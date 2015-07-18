using UnityEngine;

public class MooseController : MonoBehaviour
{
    // Ratio of Flash game coordinate space to Unity units.
    const float MOVEMENT_SCALE = 0.62f / 17.6f;
    //3.5227e-2f

    // Left/right movement
    const float MAX_RUN   = 12 * MOVEMENT_SCALE;
    const float RUN_DECEL = 4  * MOVEMENT_SCALE;
    const float RUN_ACCEL = 1  * MOVEMENT_SCALE;
    const float AIR_DECEL = 1  * MOVEMENT_SCALE;

    // Jumping
    const float GRAVITY          =  2 * MOVEMENT_SCALE;
    const float MAX_FALL         = 16 * MOVEMENT_SCALE;
    const float JUMP             = 16 * MOVEMENT_SCALE;
    const int   JUMP_FRAMES      =  8;
    const float JUMP_END_GRAVITY =  3 * MOVEMENT_SCALE;

    static public int CollisionLayerMask { get {
        return ~(
            1 << LayerMask.NameToLayer("TriggerObject") |
            1 << LayerMask.NameToLayer("TriggerDetect") |
            1 << LayerMask.NameToLayer("Rope")
        );
    } }

    MooseDimensions _heroDim;
    MooseSnap _snap;

    Vector2 _newPosition;
    Vector2 _oldPosition;
    Vector2 _vel;

    int _canJumpCounter;
    int _cantSnapCounter;
    int _jumpFrames;
    bool _faceRight = true;

    BlobBinder _blobBinder;

    public Vector2 Position { get { return _snap.enabled ? _snap.Position : _newPosition; } }
    public Vector2 Velocity { get { return _snap.enabled ? _snap.VelocityEstimate : _vel; } }
    public bool FaceRight { get { return _faceRight; } }

    void Awake()
    {
        _newPosition = transform.position.AsVector2();
        _oldPosition = _newPosition;

        _snap = GetComponent<MooseSnap>();
        _heroDim = GetComponent<MooseDimensions>();
        _blobBinder = GetComponentInChildren<BlobBinder>();

        endSnap();
    }

    void Update()
    {
        var p0 = _oldPosition.AsVector3(transform.position.z);
        var p1 = _newPosition.AsVector3(transform.position.z);
        transform.position = Vector3.Lerp(p0, p1, (Time.time - Time.fixedTime) / Time.fixedDeltaTime);
    }

    void FixedUpdate()
    {
        if (_cantSnapCounter > 0) _cantSnapCounter--;
        _oldPosition = _newPosition;

        bool pressingLeft = false;
        bool pressingRight = false;

        if (_blobBinder.HasBlob) {
            pressingLeft = Controls.IsDown(Controls.Instance.Left);
            pressingRight = Controls.IsDown(Controls.Instance.Right);
        }

        handleRun(pressingLeft, pressingRight);
        handleJump();

        if (_snap.enabled) {
            _canJumpCounter = 3;

            var updateResult = _snap.UpdatePosition(_vel.x, normalIsGround);

            if (updateResult.solidWallCollision) {
                _vel.x = 0;
            }
            if (!updateResult.stillStanding) {
                endSnap();
                freeMovement();
            }
        } else {
            _canJumpCounter--;
            freeMovement();
        }
    }

    static bool normalIsGround(Vector2 n) { return n.y >=  Mathf.Cos(Mathf.PI / 3); }
    static bool normalIsRoof(Vector2 n)   { return n.y <= -Mathf.Cos(Mathf.PI / 3); }
    static bool normalIsWall(Vector2 n)   { return !normalIsGround(n) && !normalIsRoof(n); }

    void freeMovement()
    {
        _newPosition =
            vertCollision(
            wallCollision(
                _newPosition + _vel
            ));
    }

    Vector2 vertCollision(Vector2 newPos)
    {
        var test = vertTestAtOffset(newPos, 0);
        if (test.HasValue) return test.Value;
        test = vertTestAtOffset(newPos, 1);
        if (test.HasValue) return test.Value;
        test = vertTestAtOffset(newPos, -1);
        if (test.HasValue) return test.Value;
        return newPos;
    }

    Vector2? vertTestAtOffset(Vector2 newPos, float offsetScale)
    {
        var offVec = offsetScale * new Vector2(_heroDim.HalfWidth - _heroDim.InsetX, 0);
        var pt0 = newPos + Vector2.up * _heroDim.Height + offVec;
        var pt1 = newPos + offVec;
        var maybeHit = DoubleLineCast.Cast(pt0, pt1, CollisionLayerMask);
        Debug.DrawLine(pt0, pt1, Color.green);
        if (maybeHit.HasValue && !maybeHit.Value.embedded) {
            return reactToVertCollision(newPos, maybeHit.Value, Vector2.zero);
        }
        return null;
    }

    Vector2 reactToVertCollision(Vector2 newPos, DoubleLineCast.Result hit, Vector2 offset)
    {
        if (normalIsGround(hit.normal)) {
            _vel.y = 0;
            newPos.y = hit.point.y;
            if (_cantSnapCounter == 0) {
                startSnap(hit.collider, hit.rigidbody, newPos - offset, hit.normal);
            }
        } else if (normalIsRoof(hit.normal)) {
            if (_vel.y > 0) {
                _vel.y = 0;
            }
            newPos.y = (hit.point - Vector2.up * _heroDim.Height).y;

            var hitBody = hit.collider.GetComponent<Rigidbody2D>();
            if (hitBody && hitBody.velocity.y < 0) {
                _vel.y = hitBody.velocity.y * Time.fixedDeltaTime;
                newPos.y += _vel.y;
            }
        }

        return newPos;
    }

    Vector2 wallCollision(Vector2 newPos)
    {
        float firstY = Mathf.Max(
            _vel.y < 0 ? -_vel.y : 0,
            _heroDim.InsetY
        );
        float lastY = Mathf.Min(
            _vel.y > 0 ? _heroDim.Height - _vel.y : _heroDim.Height,
            _heroDim.Height - _heroDim.InsetY
        );
        float stepY = (lastY - firstY) / 2;

        for (float offy = firstY; offy < lastY+1e-9f; offy += stepY) {
            var offset = Vector2.up * offy;
            var pt0 = newPos + offset - _heroDim.HalfWidth * Vector2.right;
            var pt1 = newPos + offset + _heroDim.HalfWidth * Vector2.right;
            Debug.DrawLine(pt0, pt1, Color.green);

            var maybeHit = DoubleLineCast.Cast(pt0, pt1, CollisionLayerMask);
            if (maybeHit.HasValue && normalIsWall(maybeHit.Value.normal)) {
                newPos.x = maybeHit.Value.point.x + Mathf.Sign(maybeHit.Value.normal.x) * _heroDim.HalfWidth;
                if (maybeHit.Value.normal.x * _vel.x < 0) {
                    _vel.x = 0;
                }
            }
        }

        return newPos;
    }

    void startSnap(Collider2D coll, Rigidbody2D rb, Vector2 pt, Vector2 normal)
    {
        _jumpFrames = 0;
        _snap.SnapTo(coll, rb, pt, normal, _vel);
    }

    void endSnap()
    {
        _snap.Unsnap();
        _vel.y = 0;
        _cantSnapCounter = 2;
        //_vel = _snap.VelocityEstimate;
        _oldPosition = transform.position.AsVector2();
        _newPosition = _oldPosition;
    }

    void handleRun(bool left, bool right)
    {
        if (left) {
            _vel.x -= _vel.x > 0 ? RUN_DECEL : RUN_ACCEL;
            if (_vel.x < -MAX_RUN) _vel.x = -MAX_RUN;
            _faceRight = false;
        }
        else if (right) {
            _vel.x += _vel.x < 0 ? RUN_DECEL : RUN_ACCEL;
            if (_vel.x > MAX_RUN) _vel.x = MAX_RUN;
            _faceRight = true;
        }
        else {
            var decel = _snap.enabled ? RUN_DECEL : AIR_DECEL;
            if (_vel.x > decel) {
                _vel.x -= decel;
            } else if (_vel.x < -decel) {
                _vel.x += decel;
            } else {
                _vel.x = 0;
            }
        }
    }

    void handleJump()
    {
        _vel.y -= GRAVITY;
        if (_vel.y < -MAX_FALL) {
            _vel.y = -MAX_FALL;
        }
    }
}