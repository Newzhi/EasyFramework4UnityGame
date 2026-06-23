using UnityEngine;

// 相机跟随：自动找 Player(tag)，跟随玩家；按住右键水平旋转视角。
[DisallowMultipleComponent]
public class CameraFollowPlayer : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Follow")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private float followSmoothTime = 0.06f;

    [Header("Orbit")]
    [SerializeField] private float distance = 6f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 20f;
    [SerializeField] private float pitchDegrees = 20f;
    [SerializeField] private float minPitchDegrees = 10f;
    [SerializeField] private float maxPitchDegrees = 70f;

    [Header("Right Mouse Drag (Yaw)")]
    [SerializeField] private float yawSensitivity = 180f;

    private Vector3 followVelocity;
    private float yawDegrees;

    private void Awake()
    {
        if (target == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
        }

        yawDegrees = transform.eulerAngles.y;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            yawDegrees += mouseX * yawSensitivity * Time.deltaTime;
        }

        pitchDegrees = Mathf.Clamp(pitchDegrees, minPitchDegrees, maxPitchDegrees);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Vector3 lookAt = target.position + targetOffset;
        Quaternion rot = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
        Vector3 desiredPos = lookAt + rot * new Vector3(0f, 0f, -distance);

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref followVelocity, followSmoothTime);
        transform.LookAt(lookAt);
    }
}

