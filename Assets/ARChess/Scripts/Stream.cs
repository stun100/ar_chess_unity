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
    public string backendUrlInput = "192.168.1.5:8000";
    public Text fpsText;
    public Text screenInfoText; // Add this line
    public ARPlaneManager arPlaneManager;      // Add this line
    public ARRaycastManager arRaycastManager;  // Add this line

    private float realScreenWidth = 296f; // Add this line
    private float realScreenHeight = 640f; // Add this line
    private WebSocket ws;
    private int frameCount = 0;
    private float elapsedTime = 0.0f;
    private List<Vector2> points = new List<Vector2>(); // Change this line
    private List<Vector3> cornerPositions = new List<Vector3>(); // Store the 3D corner positions
    private GameObject virtualChessboard; // Add this line

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

        // Create a simple 3D plane for the virtual chessboard
        virtualChessboard = GameObject.CreatePrimitive(PrimitiveType.Plane);
        virtualChessboard.transform.localScale = new Vector3(0.5f, 1f, 0.5f); // Adjust as needed
        virtualChessboard.SetActive(false); // Disable initially
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

        // Example: Attempt to back-project and align chessboard once we have four 2D corners
        if (points.Count == 4)
        {
            cornerPositions.Clear();
            foreach (var point2D in points)
            {
                // Convert 2D screen coords to a ray
                Vector3 screenPoint = new Vector3(
                    point2D.x / realScreenWidth * Screen.width,
                    point2D.y / realScreenHeight * Screen.height,
                    0f
                );
                Ray ray = Camera.main.ScreenPointToRay(screenPoint);

                var hits = new List<ARRaycastHit>();
                // Cast against detected planes
                if (arRaycastManager.Raycast(ray, hits, TrackableType.Planes))
                {
                    // Take the first hit
                    cornerPositions.Add(hits[0].pose.position);
                }
            }

            // If we have four 3D points, align the chessboard
            if (cornerPositions.Count == 4)
            {
                AlignChessboard3D(cornerPositions);
            }
        }
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
        while (true)
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
            yield return new WaitForSeconds(1f); // Adjust the interval as needed
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
        // Draw the points on the screen
        foreach (var point in points)
        {
            // Convert the point coordinates to screen coordinates
            float screenX = point.x / realScreenWidth * Screen.width;
            float screenY = point.y / realScreenHeight * Screen.height;

            // Draw the point
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(screenX - 5, screenY - 5, 10, 10), Texture2D.whiteTexture);
        }
    }

    // Example method to align a custom chessboard object in the scene
    private void AlignChessboard3D(List<Vector3> cornerPositions)
    {
        // Compute center
        Vector3 center = Vector3.zero;
        foreach (var pos in cornerPositions) center += pos;
        center /= cornerPositions.Count;

        // Compute normal of the plane
        Vector3 normal = Vector3.Cross(cornerPositions[1] - cornerPositions[0], cornerPositions[2] - cornerPositions[0]).normalized;

        // For simplicity, place the chessboard at the center of the corners
        if (virtualChessboard != null)
        {
            virtualChessboard.SetActive(true);
            virtualChessboard.transform.position = center;

            // Align the chessboard to the plane
            virtualChessboard.transform.rotation = Quaternion.LookRotation(normal);

            // Additional orientation math could go here...
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
