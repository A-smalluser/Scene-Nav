using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;
using TMPro;

public class CanvasSetupController : MonoBehaviour
{
    [SerializeField]
    private TMP_Text pathDisplayText;

    [SerializeField]
    private TMP_Text topRightText; // 新增的 Text (TMP) 组件

    [SerializeField]
    private Canvas textCanvas;

    [SerializeField]
    private float distanceFromCamera = 2f;

    [Header("RawImage Settings")]
    [SerializeField]
    private RawImage photoDisplay;

    [SerializeField]
    private float imageOffsetX = 50f;

    [SerializeField]
    private float imageOffsetY = 50f;

    [SerializeField]
    private Vector2 imageSize = new Vector2(200f, 150f);

    // 新添加的变量
    private Transform mainCameraTransform;
    private bool needsUpdate = true;

    private void Start()
    {
        // 缓存相机引用
        mainCameraTransform = Camera.main.transform;

        InitializeCanvas();
        SetupText();
        SetupTopRightText(); // 初始化右上角文本
        SetupRawImage();
    }

    private void InitializeCanvas()
    {
        if (textCanvas != null)
        {
            // 设置为世界空间渲染模式
            textCanvas.renderMode = RenderMode.WorldSpace;
            textCanvas.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

            // 优化性能设置
            textCanvas.pixelPerfect = false;

            // 如果不需要交互，禁用射线检测器
            GraphicRaycaster raycaster = textCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;
            }
        }
        else
        {
            Debug.LogError("Canvas reference not set in inspector!");
        }
    }

    private void SetupText()
    {
        if (pathDisplayText != null)
        {
            // 设置文本属性
            pathDisplayText.alignment = TextAlignmentOptions.Center;
            pathDisplayText.color = Color.white;
        }
        else
        {
            Debug.LogWarning("PathDisplayText reference not set in inspector!");
        }
    }

    private void SetupTopRightText()
    {
        if (topRightText != null)
        {
            // 获取 RectTransform 组件
            RectTransform rectTransform = topRightText.GetComponent<RectTransform>();

            // 设置锚点为右上角
            rectTransform.anchorMin = new Vector2(1, 1);
            rectTransform.anchorMax = new Vector2(1, 1);

            // 设置轴心点为右上角
            rectTransform.pivot = new Vector2(1, 1);

            // 设置位置（相对于右上角的偏移）
            rectTransform.anchoredPosition = new Vector2(-imageOffsetX, -imageOffsetY); // 注意这里的负号

            // 设置文本属性
            topRightText.alignment = TextAlignmentOptions.TopRight;
            topRightText.color = Color.white;

            Debug.Log($"TopRightText setup - Position: {rectTransform.anchoredPosition}");
        }
        else
        {
            Debug.LogWarning("TopRightText (TMP_Text) reference not set in inspector!");
        }
    }

    private void SetupRawImage()
    {
        if (photoDisplay != null)
        {
            // 获取 RectTransform 组件
            RectTransform imageRect = photoDisplay.GetComponent<RectTransform>();

            // 设置锚点为左下角
            imageRect.anchorMin = new Vector2(0, 0);
            imageRect.anchorMax = new Vector2(0, 0);

            // 设置轴心点为左下角
            imageRect.pivot = new Vector2(0, 0);

            // 设置位置（相对于左下角的偏移）
            imageRect.anchoredPosition = new Vector2(imageOffsetX, imageOffsetY);

            // 设置大小
            imageRect.sizeDelta = imageSize;

            // 确保初始颜色不透明
            photoDisplay.color = Color.white;

            Debug.Log($"RawImage setup - Position: {imageRect.anchoredPosition}, Size: {imageRect.sizeDelta}");
        }
        else
        {
            Debug.LogWarning("PhotoDisplay (RawImage) reference not set in inspector!");
        }
    }

    private void LateUpdate()
    {
        UpdateTextPosition();
    }

    private void UpdateTextPosition()
    {
        if (mainCameraTransform != null && textCanvas != null)
        {
            // 计算文本应该在的位置
            Vector3 newPosition = mainCameraTransform.position +
                                mainCameraTransform.forward * distanceFromCamera;

            // 更新Canvas位置
            textCanvas.transform.position = newPosition;

            // 使Canvas始终面向相机
            textCanvas.transform.LookAt(mainCameraTransform.position);
            // 旋转90度确保正面朝向用户
            textCanvas.transform.Rotate(0, 180, 0);
        }
    }

    // 提供公共方法用于控制更新
    public void SetNeedsUpdate(bool value)
    {
        needsUpdate = value;
    }

    // 提供公共方法用于更新RawImage位置
    public void UpdateImagePosition(float newOffsetX, float newOffsetY)
    {
        if (photoDisplay != null)
        {
            imageOffsetX = newOffsetX;
            imageOffsetY = newOffsetY;
            photoDisplay.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(imageOffsetX, imageOffsetY);
        }
    }

    // 提供公共方法用于更新RawImage大小
    public void UpdateImageSize(float width, float height)
    {
        if (photoDisplay != null)
        {
            imageSize = new Vector2(width, height);
            photoDisplay.GetComponent<RectTransform>().sizeDelta = imageSize;
        }
    }

    // 提供公共方法用于更新TopRightText内容
    public void SetTopRightText(string newText)
    {
        if (topRightText != null)
        {
            topRightText.text = newText;
        }
    }
}