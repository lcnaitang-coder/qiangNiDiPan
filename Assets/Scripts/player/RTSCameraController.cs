using UnityEngine;

public class RTSCameraController : MonoBehaviour {
    [Header("Camera Movement Settings")]
    [SerializeField] private float moveSensitivity = 0.8f;   // 平移灵敏度
    [SerializeField] private float rotateSensitivity = 4.0f; // 旋转灵敏度
    [SerializeField] private float scrollSensitivity = 10f;  // 缩放灵敏度
    [SerializeField] private float minHeight = 5f;          // 相机最低高度
    [SerializeField] private float maxHeight = 50f;         // 相机最高高度

    private Vector3 _lastMousePosition;

    void Update() {
        HandleCameraInputs();
    }

    /// <summary>
    /// 处理鼠标中键平移、右键旋转、滚轮缩放
    /// </summary>
    private void HandleCameraInputs() {
        // 1. 中键平移 (Panning)
        if (Input.GetMouseButtonDown(2)) {
            _lastMousePosition = Input.mousePosition;
        }
        if (Input.GetMouseButton(2)) {
            Vector3 delta = Input.mousePosition - _lastMousePosition;
            // 相对于相机朝向的水平平移
            Vector3 moveDir = transform.right * (-delta.x) + transform.up * (-delta.y);
            moveDir.y = 0; // 锁定高度，不产生垂直位移
            
            // 根据高度动态调整平移速度，越高移得越快
            float speedFactor = transform.position.y / 10f;
            transform.position += moveDir * moveSensitivity * speedFactor * Time.deltaTime;
            _lastMousePosition = Input.mousePosition;
        }

        // 2. 右键旋转 (Rotation)
        if (Input.GetMouseButton(1)) {
            float mouseX = Input.GetAxis("Mouse X") * rotateSensitivity;
            // 绕世界 Y 轴旋转，保持水平对齐
            transform.RotateAround(transform.position, Vector3.up, mouseX);
        }

        // 3. 滚轮缩放 (Zooming)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f) {
            Vector3 zoomDir = transform.forward * scroll * scrollSensitivity;
            Vector3 nextPos = transform.position + zoomDir;
            
            // 限制高度范围，防止穿模或飞离地图
            if (nextPos.y >= minHeight && nextPos.y <= maxHeight) {
                transform.position = nextPos;
            }
        }
    }
}
