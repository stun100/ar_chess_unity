using System;
using System.Collections;
using System.Collections.Generic; // Add this line
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using WebSocketSharp;
using UnityEngine.UI;

public class Stream : MonoBehaviour
{
    public ARCameraManager arCameraManager;
    public string backendUrlInput = "192.168.1.15:8000";
    public Text fpsText;
    public Text screenInfoText; // Add this line
    public ARPlaneManager planeManager; // Add this line
    public ARRaycastManager raycastManager; // Add this line
    public Button calibrationButton; // Add this line
    private bool isCalibrationMode = false; // Add this line
    // Remove the chessboardPrefab declaration
    // public GameObject chessboardPrefab; // Remove this line

    public float realScreenWidth = 296f; // Add this line
    public float realScreenHeight = 640f; // Add this line
    private WebSocket ws;
    private int frameCount = 0;
    private float elapsedTime = 0.0f;
    private List<Vector2> points = new List<Vector2>(); // Change this line
    private GameObject chessboard; // Add this line

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ws = new WebSocket($"ws://{backendUrlInput}/stream");
        ws.OnMessage += (sender, e) =>
        {
            Debug.Log("Response: " + e.Data);
            // Parse the response to get the points
            var response = JsonUtility.FromJson<ResponseData>(e.Data);
            points.Clear();
            foreach (var point in response.corners)
            {
                points.Add(new Vector2(point.center_x, point.center_y));
            }
        };
        ws.OnError += (sender, e) =>
        {
            Debug.Log("Error: " + e.Message);
        };
        ws.Connect();
        StartCoroutine(streamImage());

        // Display screen width and height
        screenInfoText.text = $"Screen Width: {Screen.width}, Screen Height: {Screen.height}"; // Add this line
        planeManager = FindFirstObjectByType<ARPlaneManager>(); // Add this line
        raycastManager = FindFirstObjectByType<ARRaycastManager>(); // Add this line
        chessboard = GameObject.CreatePrimitive(PrimitiveType.Cube); // Add this line
        chessboard.SetActive(false); // Add this line

        calibrationButton.onClick.AddListener(ToggleCalibrationMode); // Add this line
        UpdateScreenInfoText(); // Add this line
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate FPS based on the number of images sent
        elapsedTime += Time.deltaTime;
        if (elapsedTime >= 1.0f)
        {
            fpsText.text = $"{frameCount} img/s";
            frameCount = 0;
            elapsedTime = 0.0f;
        }
        if (isCalibrationMode && planeManager.trackables.count > 0 && points.Count == 4) // Modify this line
        {
            PlaceChessboard();
        }
        // ...existing code...
    }

    private string accessImage()
    {
        // Acquire an XRCpuImage
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            return null;

        // Set up our conversion params
        var conversionParams = new XRCpuImage.ConversionParams
        {
            // Convert the entire image
            inputRect = new RectInt(0, 0, image.width, image.height),

            // Output at full resolution
            outputDimensions = new Vector2Int(image.width, image.height),

            // Convert to RGBA format
            outputFormat = TextureFormat.RGBA32,

            // Flip across the vertical axis (mirror image)
            transformation = XRCpuImage.Transformation.MirrorY
        };

        // Create a Texture2D to store the converted image
        var texture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);

        // Texture2D allows us write directly to the raw texture data as an optimization
        var rawTextureData = texture.GetRawTextureData<byte>();
        try
        {
            unsafe
            {
                // Synchronously convert to the desired TextureFormat
                image.Convert(
                    conversionParams,
                    new IntPtr(rawTextureData.GetUnsafePtr()),
                    rawTextureData.Length);
            }
        }
        finally
        {
            // Dispose the XRCpuImage after we're finished to prevent any memory leaks
            image.Dispose();
        }

        // Apply the converted pixel data to our texture
        texture.Apply();

        byte[] imageBytes = texture.EncodeToPNG();
        string base64Image = Convert.ToBase64String(imageBytes);

        return base64Image;
    }

    private IEnumerator streamImage()
    {
        while (true && isCalibrationMode)
        {
            string base64Image = accessImage();
            if (base64Image != null)
            {
                // Convert base64 string to byte array
                byte[] imageBytes = Convert.FromBase64String(base64Image);

                // Send image bytes via WebSocket
                ws.Send(imageBytes);
                frameCount++; // Increment frame count for each image sent
            }
            yield return new WaitForSeconds(0.5f); // Adjust the interval as needed
        }
    }

    void OnDestroy()
    {
        if (ws != null)
        {
            ws.Close();
        }
    }

    void OnGUI()
    {
        int pointSize = 20;
        // Draw the points on the screen
        foreach (var point in points)
        {
            // Convert the point coordinates to screen coordinates
            float screenX = point.x / realScreenWidth * Screen.width;
            float screenY = point.y / realScreenHeight * Screen.height;

            // Draw the point
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(screenX - 5, screenY - 5, pointSize, pointSize), Texture2D.whiteTexture);
        }
    }

    private void ToggleCalibrationMode() // Add this method
    {
        isCalibrationMode = !isCalibrationMode;
        UpdateScreenInfoText();
    }

    private void UpdateScreenInfoText() // Add this method
    {
        screenInfoText.text = $"Screen Width: {Screen.width}, Screen Height: {Screen.height}\n" +
                              $"Mode: {(isCalibrationMode ? "Calibration" : "Normal")}";
    }

    private void PlaceChessboard() // Modify this block
    {
        if (!isCalibrationMode) return; // Add this line

        var worldPoints = new List<Vector3>();
        foreach (var p in points)
        {
            var screenPos = new Vector2(p.x / realScreenWidth * Screen.width,
                                        p.y / realScreenHeight * Screen.height);
            var hits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenPos, hits, TrackableType.Planes))
            {
                worldPoints.Add(hits[0].pose.position);

                // Draw the raycast line
                Debug.DrawLine(Camera.main.transform.position, hits[0].pose.position, Color.green, 2f);
            }
        }
        if (worldPoints.Count == 4)
        {
            Vector3 center = (worldPoints[0] + worldPoints[1] + worldPoints[2] + worldPoints[3]) / 4f;
            Vector3 forward = (worldPoints[1] - worldPoints[0]).normalized;
            Vector3 right = Vector3.Cross((worldPoints[2] - worldPoints[0]).normalized, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, forward).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, up);

            // Calculate the maximum size to ensure the cube is square
            float maxSize = Mathf.Max(Vector3.Distance(worldPoints[0], worldPoints[1]), Vector3.Distance(worldPoints[0], worldPoints[2]));

            // Adjust the center position to be on the plane
            center.y = worldPoints[0].y;

            // Place the chessboard
            if (chessboard == null)
            {
                chessboard = new GameObject("Chessboard");
                // Add necessary components to the chessboard
            }
            chessboard.transform.position = center;
            chessboard.transform.rotation = rotation;
            chessboard.transform.localScale = new Vector3(maxSize, 1, maxSize);
        }
    }

    [System.Serializable]
    private class ResponseData
    {
        public List<Point> corners;
    }

    [System.Serializable]
    private class Point
    {
        public float center_x;
        public float center_y;
    }
}
