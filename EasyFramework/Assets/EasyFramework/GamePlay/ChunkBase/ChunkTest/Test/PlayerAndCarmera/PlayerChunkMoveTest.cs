using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerChunkMoveTest : MonoBehaviour,IChunkLoadSource
{
    [Header("Move")]
    [Tooltip("开启：使用 CharacterController 碰撞、重力、跳跃；关闭：无视碰撞自由移动，Space 上升，Ctrl 下降。")]
    [SerializeField] private bool useCharacterControllerCollision = true;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 1.6f;
    [SerializeField] private float flyVerticalSpeed = 5f;
    [SerializeField] private float gravity = -20f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 2f;

    [Header("Camera Relative Movement")]
    [SerializeField] private Camera playerCamera;

    [Header("Auto Step / Grid Assist")]
    [SerializeField, Range(0.1f, 0.9f)] private float kneeHeightRatio = 0.33f;
    [SerializeField] private float obstacleCheckDistance = 0.6f;
    [SerializeField] private float stepHeight = 0.35f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Chunk")]
    // TODO(Chunk): 区块交互逻辑已从该测试脚本移除，后续通过事件系统接入。

    private CharacterController controller;
    private float verticalVelocity;
    private bool lastUseCharacterControllerCollision;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        lastUseCharacterControllerCollision = useCharacterControllerCollision;
        ApplyControllerCollisionMode();
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        // 不在此处锁定光标：右键旋转相机由 CameraFollowPlayer 负责。
    }

    private void Update()
    {
        if (lastUseCharacterControllerCollision != useCharacterControllerCollision)
        {
            lastUseCharacterControllerCollision = useCharacterControllerCollision;
            ApplyControllerCollisionMode();
        }

        HandleMovement();

    }

    private void ApplyControllerCollisionMode()
    {
        if (controller != null)
        {
            controller.enabled = useCharacterControllerCollision;
        }

        if (!useCharacterControllerCollision)
        {
            verticalVelocity = 0f;
        }
    }

    private void HandleMovement()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        // 视角正对方向为正方向（W 永远往相机正前方的水平投影走）
        Vector3 camForward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
        Vector3 camRight = playerCamera != null ? playerCamera.transform.right : transform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 move = camForward * vertical + camRight * horizontal;
        if (move.sqrMagnitude > 1f)
        {
            move.Normalize();
        }

        // 让角色朝向移动方向（可选：如果你希望独立于相机，可去掉这段）
        if (move.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(move, Vector3.up);
        }

        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= sprintMultiplier;
        }

        if (!useCharacterControllerCollision)
        {
            HandleNoCollisionMovement(move, speed);
            return;
        }

        TryAutoStep(move);

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = Mathf.Sqrt(Mathf.Max(0f, jumpHeight) * -2f * gravity);
            }
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = move * speed;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    private void HandleNoCollisionMovement(Vector3 planarMove, float speed)
    {
        verticalVelocity = 0f;

        float upDown = 0f;
        if (Input.GetKey(KeyCode.Space))
        {
            upDown += 1f;
        }
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            upDown -= 1f;
        }

        Vector3 velocity = planarMove * speed + Vector3.up * (upDown * flyVerticalSpeed);
        transform.position += velocity * Time.deltaTime;
    }

    private void TryAutoStep(Vector3 moveDir)
    {
        if (!controller.isGrounded)
        {
            return;
        }

        Vector3 planar = moveDir;
        if (planar.sqrMagnitude < 0.0001f)
        {
            return;
        }

        planar.y = 0f;
        planar.Normalize();

        // 计算“膝盖高度”射线起点（世界坐标）
        Vector3 centerWorld = transform.position + controller.center;
        float halfHeight = controller.height * 0.5f;
        Vector3 bottom = centerWorld - Vector3.up * (halfHeight - controller.radius);
        Vector3 knee = bottom + Vector3.up * (controller.height * kneeHeightRatio);

        // 低位射线：检测前方是否有障碍
        if (!Physics.Raycast(knee, planar, obstacleCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        // 高位射线：在 stepHeight 上方再测一次，如果前方为空则允许抬升
        Vector3 kneeUp = knee + Vector3.up * stepHeight;
        if (Physics.Raycast(kneeUp, planar, obstacleCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        controller.Move(Vector3.up * stepHeight);
    }

    public void CollectTargetChunks(ChunkSettings settings, ICollection<ChunkCoord> results)
    {
        
    }
}