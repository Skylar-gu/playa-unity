using UnityEngine;

namespace Playa
{
    // WASD + mouse look + spacebar tap. §4 is emphatic that this is the whole
    // control surface, so keep it that way. Uses legacy Input so the project
    // works whether Active Input Handling is set to Legacy or Both.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerRig : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 3.5f;
        public float lookSensitivity = 2.4f;
        public float bobAmplitude = 0.05f;
        public float bobFrequency = 2.0f;

        [Header("References")]
        public Transform head;
        public DanceFloor floor;

        public TapEstimator Tap { get; } = new TapEstimator();

        CharacterController controller;
        float pitch;
        float bobT;
        float headBaseY;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (floor == null) floor = FindFirstObjectByType<DanceFloor>();
            if (head == null)
            {
                var cam = GetComponentInChildren<Camera>();
                head = cam != null ? cam.transform : transform;
            }
            headBaseY = head.localPosition.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            // Look
            float mx = Input.GetAxis("Mouse X") * lookSensitivity;
            float my = Input.GetAxis("Mouse Y") * lookSensitivity;
            transform.Rotate(0f, mx, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - my, -70f, 70f);
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Move
            float fwd = Input.GetAxisRaw("Vertical");
            float right = Input.GetAxisRaw("Horizontal");
            Vector3 dir = transform.forward * fwd + transform.right * right;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            Vector3 vel = dir * moveSpeed;
            // Constant gentle gravity to keep grounded.
            vel.y = -3f;
            controller.Move(vel * Time.deltaTime);

            // Head bob when walking — subtle.
            float speedFrac = Mathf.Clamp01(new Vector2(vel.x, vel.z).magnitude / moveSpeed);
            bobT += Time.deltaTime * bobFrequency * (0.4f + 0.6f * speedFrac);
            head.localPosition = new Vector3(
                head.localPosition.x,
                headBaseY + Mathf.Sin(bobT * 2f * Mathf.PI) * bobAmplitude * speedFrac,
                head.localPosition.z);

            // Escape releases cursor for debug.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Tap!
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Tap.Tap(Time.time);
            }

            Tap.Sample(Time.time, out float phase, out _, out bool active);
            if (floor != null)
            {
                floor.PlayerPosition = transform.position;
                floor.PlayerPhase = phase;
                floor.PlayerActive = active;
            }
        }
    }
}
